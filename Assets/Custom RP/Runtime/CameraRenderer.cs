using System;
using UnityEngine;
using UnityEngine.Rendering;

public partial class CameraRenderer
{
    //存放当前渲染上下文,
    private ScriptableRenderContext context;

    //存放摄像机渲染器当前应该渲染的摄像机
    private Camera camera;

    const string bufferName = "buffer1 test";
    /// <summary>
    /// 存放将要渲染的物体,剔除不能渲染后的结果
    /// </summary>
    CullingResults cullingResults;
    /// <summary>
    /// 渲染将会渲染的pass 只会渲染 shader Pass里面 Tags { "LightMode" = "SRPDefaultUnlit" } 这个字样
    /// </summary>
    //me 和 URP feature 一样 你自己的pass也要去后处理设定才行
    //原理就是 生成hash 用于匹配而已 然后执行对应pass
    // 定义 Tag ID
    static ShaderTagId
        unlitShaderTagId = new ShaderTagId("SRPDefaultUnlit"), //必须获取 SRPDefaultUnlit 通道的着色器标记ID
        litShaderTagId = new ShaderTagId("CustomLit"); // 新增 

    //me 缓冲区是unity内置的只需要提供名字就行
    CommandBuffer buffer = new CommandBuffer
    {
        name = bufferName
    };//G 初始化成员的语法，这里初始化了一个CommandBuffer对象，并且给它命名为"Render Camera"


    // 1. 实例化 Lighting 类
    Lighting lighting = new Lighting();

    // me15: 全屏拷贝用的材质
    Material material;
    // me15: 占位贴图，当深度/颜色快照未就绪时防止 Shader 采样到垃圾数据
    Texture2D missingTexture;

    public CameraRenderer(Shader shader)
    {
        // me15: 用专属 Shader 创建材质（HideFlags 隐藏，避免出现在 Project 窗口）
        material = CoreUtils.CreateEngineMaterial(shader);
        // 创建 1x1 灰色占位贴图
        missingTexture = new Texture2D(1, 1)
        {
            hideFlags = HideFlags.HideAndDontSave,
            name = "Missing"
        };
        missingTexture.SetPixel(0, 0, Color.white * 0.5f);
        missingTexture.Apply(true, true);
    }

    // me15: 释放材质和占位贴图（防止管线 Asset 修改时内存泄漏）
    public void Dispose()
    {
        CoreUtils.Destroy(material);
        CoreUtils.Destroy(missingTexture);
    }

    // me11: 后处理栈实例
    PostFXStack postFXStack = new PostFXStack();

    // me15: 把原来的单一帧缓冲拆成两个 Attachment
    //   颜色 Attachment → 只存 RGBA 颜色
    //   深度 Attachment → 只存深度（这样才能单独拷贝）
    static int
        colorAttachmentId = Shader.PropertyToID("_CameraColorAttachment"),
        depthAttachmentId = Shader.PropertyToID("_CameraDepthAttachment"),
        depthTextureId = Shader.PropertyToID("_CameraDepthTexture"),
        colorTextureId = Shader.PropertyToID("_CameraColorTexture"),
        sourceTextureId = Shader.PropertyToID("_SourceTexture"),
        // me15: DrawFinal 最终输出的混合模式属性
        srcBlendId = Shader.PropertyToID("_CameraSrcBlend"),
        dstBlendId = Shader.PropertyToID("_CameraDstBlend");

    // me15: 是否需要拷贝深度/颜色（由 CameraBufferSettings 控制）
    bool useDepthTexture, useColorTexture;
    // me15: 是否使用中间缓冲（后处理激活 || 需要拷贝深度/颜色时都需要）
    bool useIntermediateBuffer;

    // me12: 是否真正启用 HDR（管线允许 && 相机允许）
    bool useHDR;
    // me14: 没有挂 CustomRenderPipelineCamera 组件时用这个默认值
    static CameraSettings defaultCameraSettings = new CameraSettings();
    // me15: 检测当前平台是否支持 CopyTexture（WebGL 2.0 不支持）
    static bool copyTextureSupported = SystemInfo.copyTextureSupport > CopyTextureSupport.None;

    void ExecuteBuffer()
    {
        context.ExecuteCommandBuffer(buffer);//执行当前上下文中的指令队列
        buffer.Clear();
    }
    /// <summary>
    /// 每帧调用 摄像机渲染器的渲染函数，在当前渲染上下文的基础上渲染当前摄像机
    /// </summary>
    /// <param name="context"></param>
    /// <param name="camera"></param>
    public void Render(ScriptableRenderContext context, Camera camera,
        CameraBufferSettings bufferSettings, // me15: 替代 bool allowHDR
        bool useDynamicBatching, bool useGPUInstancing,
        bool useLightsPerObject,
        ShadowSettings shadowSettings,
        PostFXSettings postFXSettings,
        int colorLUTResolution
    )
    {

        //设定当前上下文和摄像机
        this.context = context;
        this.camera = camera;

        // me14: 读取相机上的自定义设置组件
        //!!这个可以获取大量camera自定的设置组件哦
        var crpCamera = camera.GetComponent<CustomRenderPipelineCamera>();
        CameraSettings cameraSettings = crpCamera ? crpCamera.Settings : defaultCameraSettings;
        // me14: 如果相机有自己的 PostFX 设置，覆盖管线传进来的全局设置
        if (cameraSettings.overridePostFX)
        {
            postFXSettings = cameraSettings.postFXSettings;
        }

        // me12: 两层都允许 HDR 才真正启用
        useHDR = bufferSettings.allowHDR && camera.allowHDR;

        // me15: 根据管线配置 + 相机选筞决定是否拷贝
        if (camera.cameraType == CameraType.Reflection)
        {
            useDepthTexture = bufferSettings.copyDepthReflection;
            useColorTexture = bufferSettings.copyColorReflection;
        }
        else
        {
            useDepthTexture = bufferSettings.copyDepth && cameraSettings.copyDepth;
            useColorTexture = bufferSettings.copyColor && cameraSettings.copyColor;
        }

        PrepareBuffer();//每个摄像机渲染器都会调用的函数，用于准备CommandBuffer   设置对应Buffer名字
        PrepareForSceneWindow();//渲染所有编辑世界(scene)的物体 然后剔除
        if (!Cull(shadowSettings.maxDistance))
        { // 使用 Max Distance 剔除
            return;
        }

        //!!我们需要在渲染实际摄像机画面之前渲染阴影贴图
        //???不明白啊 ,到底 我们 屏幕上显示的是什么
        // 2. 在画物体之前，先设置灯光！
        //规定好条目
        buffer.BeginSample(SampleName);
        ExecuteBuffer();//提交一下 才能生效
        //将光源信息传递给GPU，在其中也会完成阴影贴图的渲染
        lighting.Setup(
            context, cullingResults, shadowSettings, useLightsPerObject,
            // me14: maskLights=true 时，传相机的 mask 来过滤灯光；否则传 -1（不过滤）
            cameraSettings.maskLights ? cameraSettings.renderingLayerMask : -1
        );

        // me11+13: 初始化后处理栈
        postFXStack.Setup(
            context, camera, postFXSettings, useHDR, colorLUTResolution,
            cameraSettings.finalBlendMode  // me14: 新增相机混合模式选择
        );
        buffer.EndSample(SampleName);


        //然后 把画布修改交给摄像机RT,
        //!!不能反过来,不然会导致 所有作画都在阴影上,而阴影贴图没有颜色通道
        Setup();

        //注意是对shadertag 也就是filter来进行区别绘制
        // 传给 DrawVisibleGeometry
        DrawVisibleGeometry(
            useDynamicBatching, useGPUInstancing, useLightsPerObject,
            cameraSettings.renderingLayerMask  // me14: 相机的渲染层遮罩，过滤几何体
        );
        DrawUnsupportedShaders();

        // me11: Gizmos 拆分 — PreImageEffects 在后处理之前绘制
        DrawGizmosBeforeFX();
        // me11: 后处理激活时，将颜色 Attachment 交给 PostFXStack 处理
        if (postFXStack.IsActive)
        {
            postFXStack.Render(colorAttachmentId);
        }
        else if (useIntermediateBuffer)
        {
            // me15: 无后处理但用了中间缓冲时，用 DrawFinal 支持 finalBlendMode
            DrawFinal(cameraSettings.finalBlendMode);
            ExecuteBuffer();
        }
        // me11: PostImageEffects 在后处理之后绘制（保持清晰）
        DrawGizmosAfterFX();

        Cleanup(); // me11: 统一清理

        //准备好缓冲后提交
        Submit();
    }

    /// <summary>
    /// 配置渲染上下文和摄像机属性，根据摄像机的清除标志来清除渲染目标，并开始采样。
    /// </summary>
    void Setup()
    {

        //!! 这个也会重新绑定RT 也就是说 这个会抢夺 目前GPU处理的 图像 
        //me11补充说明所以在这个之前就把shadowatlas给渲染完了再切换目标
        //阴影先渲（用ShadowAtlas RT）
        //    ↓
        //SetupCameraProperties 把RT切回来
        //    ↓
        //画场景（用相机RT）
        context.SetupCameraProperties(camera);//把摄像机的属性传递给当前上下文 得到对应的MVP矩阵
        //me 这个是自己可在editor内选择
        //me做什么选择 只是选择一个枚举值罢了具体的逻辑还是你自己写 你就当成一个数字好了
        /*
        Skybox	1	画天空盒（需要清理深度）
        Color	2	画纯色背景（需要清理深度）
        Depth	3	只清深度，保留之前颜色 
        Nothing	4	啥也不清*/
        CameraClearFlags flags = camera.clearFlags;//选择摄像机的清除标志 枚举类

        // me15: 需要中间缓冲的条件：后处理激活 OR 需要深度/颜色拷贝
        useIntermediateBuffer = postFXStack.IsActive || useDepthTexture || useColorTexture;

        if (useIntermediateBuffer)
        {
            // me15: 强制清除颜色和深度（中间 RT 的初始内容是垃圾数据）
            if (flags > CameraClearFlags.Color)
            {
                flags = CameraClearFlags.Color;
            }
            // me15: 颜色 Attachment（只存 RGBA，不包含深度）
            buffer.GetTemporaryRT(
                colorAttachmentId, camera.pixelWidth, camera.pixelHeight,
                0, FilterMode.Bilinear,
                useHDR ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default
            );
            // me15: 深度 Attachment（单独存深度，32bit 精度）
            buffer.GetTemporaryRT(
                depthAttachmentId, camera.pixelWidth, camera.pixelHeight,
                32, FilterMode.Point, RenderTextureFormat.Depth
            );
            // me15: 同时绑定两个 Attachment，GPU 分别写颜色和深度
            buffer.SetRenderTarget(
                colorAttachmentId,
                RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store,
                depthAttachmentId,
                RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store
            );
        }

        //!注意了渲染的内容是不会自动清除的 需要我们手动清除
        // U Unity 内部又自动开了一个层级（名字也是 buffer.name），包裹住 Clear 
        //buffer.ClearRenderTarget 这个函数需要三个参数：
        //clearDepth: 是否清除深度缓冲区（决定遮挡关系的）。
        //clearColor: 是否清除颜色缓冲区（决定画面颜色的）。
        //backgroundColor: 如果要清除颜色，清除成什么颜色？
        buffer.ClearRenderTarget(
            flags <= CameraClearFlags.Depth,
            flags == CameraClearFlags.Color,
            flags == CameraClearFlags.Color ?
                camera.backgroundColor.linear : Color.clear
        );


        // 这两个层级现在是 平级 的（显示在一起），而不是父子关系
        // 为了我们的层级不叠加就等上面的会自己结束标签
        buffer.BeginSample(SampleName);
        // me15: 绑定占位贴图，防止 Shader 采样到未就绪的 RT
        buffer.SetGlobalTexture(depthTextureId, missingTexture);
        buffer.SetGlobalTexture(colorTextureId, missingTexture);
        ExecuteBuffer();

    }

    // me15: 统一清理（释放所有临时 RT + 灯光清理）
    void Cleanup()
    {
        lighting.Cleanup();
        if (useIntermediateBuffer)
        {
            buffer.ReleaseTemporaryRT(colorAttachmentId);
            buffer.ReleaseTemporaryRT(depthAttachmentId);
        }
        if (useDepthTexture)
        {
            buffer.ReleaseTemporaryRT(depthTextureId);
        }
        if (useColorTexture)
        {
            buffer.ReleaseTemporaryRT(colorTextureId);
        }
    }

    void Draw(RenderTargetIdentifier from, RenderTargetIdentifier to, bool isDepth = false)
    {
        buffer.SetGlobalTexture(sourceTextureId, from);
        buffer.SetRenderTarget(
            to,
            RenderBufferLoadAction.DontCare,
            RenderBufferStoreAction.Store
        );
        buffer.DrawProcedural(
            Matrix4x4.identity,
            material,
            isDepth ? 1 : 0,
            MeshTopology.Triangles, 3
        );
    }

    // me15: 最终输出（支持 finalBlendMode，适配多相机/叠加渲染）
    static readonly Rect fullViewRect = new Rect(0f, 0f, 1f, 1f);
    void DrawFinal(CameraSettings.FinalBlendMode finalBlendMode)
    {
        buffer.SetGlobalFloat(srcBlendId, (float)finalBlendMode.source);
        buffer.SetGlobalFloat(dstBlendId, (float)finalBlendMode.destination);
        buffer.SetGlobalTexture(sourceTextureId, colorAttachmentId);
        buffer.SetRenderTarget(
            BuiltinRenderTextureType.CameraTarget,
            finalBlendMode.destination == BlendMode.Zero && camera.rect == fullViewRect ?
                RenderBufferLoadAction.DontCare : RenderBufferLoadAction.Load,
            RenderBufferStoreAction.Store
        );
        buffer.SetViewport(camera.pixelRect);
        buffer.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3);
        buffer.SetGlobalFloat(srcBlendId, 1f);
        buffer.SetGlobalFloat(dstBlendId, 0f);
    }

    void CopyAttachments()
    {
        if (useDepthTexture)
        {
            buffer.GetTemporaryRT(
                depthTextureId, camera.pixelWidth, camera.pixelHeight,
                32, FilterMode.Point, RenderTextureFormat.Depth
            );
            if (copyTextureSupported)
                buffer.CopyTexture(depthAttachmentId, depthTextureId);
            // ↑ GPU 内部直接把 depthAttachment 拷贝到 depthTexture
            else
                Draw(depthAttachmentId, depthTextureId, true);
        }
        if (useColorTexture)
        {
            buffer.GetTemporaryRT(
                colorTextureId, camera.pixelWidth, camera.pixelHeight,
                0, FilterMode.Bilinear,
                useHDR ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default
            );
            if (copyTextureSupported)
                buffer.CopyTexture(colorAttachmentId, colorTextureId);
            // ↑ GPU 内部直接把 depthAttachment 拷贝到 depthTexture
            //me 意思就是 粒子特效需要一份参照物 作为读取 然后修改后写入 attachment 毕竟不能又读又写
            else
                Draw(colorAttachmentId, colorTextureId);
        }
        // 如果平台不支持 CopyTexture，用 Draw 局部改变了渲染目标，必须切回来
        if (!copyTextureSupported && (useDepthTexture || useColorTexture))
        {
            buffer.SetRenderTarget(
                colorAttachmentId,
                RenderBufferLoadAction.Load, RenderBufferStoreAction.Store,
                depthAttachmentId,
                RenderBufferLoadAction.Load, RenderBufferStoreAction.Store
            );
        }
        if (useDepthTexture || useColorTexture)
            ExecuteBuffer();
    }




    /// <summary>
    /// 绘制所有可见几何体，包括不透明和透明物体，并绘制天空盒。
    /// 该方法首先根据当前摄像机的设置来渲染不透明物体，然后修改排序设置以渲染透明物体。
    /// 最后调用Unity内置函数绘制天空盒。
    /// </summary>
    void DrawVisibleGeometry(
        bool useDynamicBatching,
        bool useGPUInstancing,
        bool useLightsPerObject,
        int renderingLayerMask  // me14: 相机渲染层遮罩，控制哪些物体被渲染
    )
    {
        var sortingSettings = new SortingSettings(camera);
        var drawingSettings = new DrawingSettings(
            unlitShaderTagId, sortingSettings
        )
        {
            enableDynamicBatching = useDynamicBatching,
            enableInstancing = useGPUInstancing,
            perObjectData =
                PerObjectData.Lightmaps
                | PerObjectData.ShadowMask
                | PerObjectData.LightProbe
                | PerObjectData.OcclusionProbe
                | PerObjectData.LightProbeProxyVolume
                | PerObjectData.OcclusionProbeProxyVolume
                | PerObjectData.ReflectionProbes
                | (useLightsPerObject ?
                    PerObjectData.LightData | PerObjectData.LightIndices :
                    PerObjectData.None)
        };
        drawingSettings.SetShaderPassName(1, litShaderTagId);

        // me14: 把相机的 renderingLayerMask 传给 FilteringSettings，过滤几何体
        var filteringSettings = new FilteringSettings(
            RenderQueueRange.opaque,
            renderingLayerMask: (uint)renderingLayerMask
        );
        //渲染CullingResults内的VisibleObjects
        context.DrawRenderers(cullingResults, ref drawingSettings, ref filteringSettings);
        //!第二阶段：绘制天空盒 (Skybox)
        //添加"绘制天空盒"指令，DrawSkybox为ScriptableRenderContext下已有函数，这里就体现了为什么说Unity已经帮我们封装好了很多我们要用到的函数，        //!第二阶段：绘制天空盒 (Skybox)
        context.DrawSkybox(camera);

        // me15: 天空盒画完 → 不透明阶段结束 → 拷贝深度/颜色快照
        // 必须在透明物体（粒子）之前，粒子 Shader 才能读到正确数据
        CopyAttachments();

        //!第三阶段：渲染透明物体 (Transparent)
        sortingSettings.criteria = SortingCriteria.CommonTransparent;//设置顺序为透明物体顺序从后往前
        drawingSettings.sortingSettings = sortingSettings;//修改成员   sortingSettings.criteria决定渲染顺序默认为None
        filteringSettings.renderQueueRange = RenderQueueRange.transparent;//绘制透明物体
        //todo 这个filteringSettings里面还是有很多门道的 后续问问ai 
        context.DrawRenderers(cullingResults, ref drawingSettings, ref filteringSettings);
    }

    void Submit()
    {
        buffer.EndSample(SampleName);
        ExecuteBuffer();// 被调用是为了确保在提交渲染上下文之前，命令缓冲区中的所有命令都已经被执行并清空
        //提交当前上下文中缓存的指令队列，执行指令队列
        context.Submit();
    }

    bool Cull(float maxShadowDistance)//执行视锥体剔除（Frustum Culling）
    {
        //尝试获取摄像机的剔除参数 存入P中 然后在上下文剔除 ,这些被剔除的物体就不会被渲染了 
        if (camera.TryGetCullingParameters(out ScriptableCullingParameters p))
        {
            // 设置阴影距离 (取 Max Distance 和 摄像机远裁剪面的较小值)
            //? 
            //@
            p.shadowDistance = Mathf.Min(maxShadowDistance, camera.farClipPlane);
            //剔除后的结果（哪些物体可见、哪些灯光有效）被存入cullingResults 变量中，供后续渲染使用
            cullingResults = context.Cull(ref p);
            return true;
        }
        return false;
    }




}
