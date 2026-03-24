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

    // me11: 后处理栈实例
    PostFXStack postFXStack = new PostFXStack();

    // me11: 中间帧缓冲（后处理激活时先渲染到这里，再由 PostFXStack 处理后输出到屏幕）
    static int frameBufferId = Shader.PropertyToID("_CameraFrameBuffer");

    // me12: 是否真正启用 HDR（管线允许 && 相机允许）
    bool useHDR;
    // me14: 没有挂 CustomRenderPipelineCamera 组件时用这个默认值
    static CameraSettings defaultCameraSettings = new CameraSettings();

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
        bool allowHDR,                 // me12
        bool useDynamicBatching, bool useGPUInstancing,
        bool useLightsPerObject,       // me09
        ShadowSettings shadowSettings,
        PostFXSettings postFXSettings, // me11
        int colorLUTResolution         // me13
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
        useHDR = allowHDR && camera.allowHDR;
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
        // me11: 后处理激活时，将中间帧缓冲交给 PostFXStack 处理
        if (postFXStack.IsActive)
        {
            postFXStack.Render(frameBufferId);
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

        // me11: 后处理激活时，必须渲染到中间 RT 而非直接输出到屏幕
        // 因为后处理需要读取完整画面才能处理
        if (postFXStack.IsActive)
        {
            // me11: 强制清除颜色和深度（中间 RT 的初始内容是垃圾数据）
            if (flags > CameraClearFlags.Color)
            {
                flags = CameraClearFlags.Color;
            }
            // me11: 申请一张和屏幕等大的临时 RT 作为中间帧缓冲
            buffer.GetTemporaryRT(
                frameBufferId, camera.pixelWidth, camera.pixelHeight,
                32, FilterMode.Bilinear,
                // me12: HDR 用 16-bit float（不clamp），LDR 用 8-bit（clamp到0-1）
                useHDR ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default
            );
            // me11: 把渲染目标从屏幕切换到这张 RT
            buffer.SetRenderTarget(
                frameBufferId,
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
        buffer.BeginSample(SampleName);//用于debuger显示区别bufferName是采样点名称
        ExecuteBuffer();//命令缓冲区中的所有命令都已经被执行并清空。

    }

    // me11: 统一清理（释放 RT + 灯光清理）
    void Cleanup()
    {
        lighting.Cleanup();
        if (postFXStack.IsActive)
        {
            buffer.ReleaseTemporaryRT(frameBufferId);
        }
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
        //添加"绘制天空盒"指令，DrawSkybox为ScriptableRenderContext下已有函数，这里就体现了为什么说Unity已经帮我们封装好了很多我们要用到的函数，SPR的画笔~
        context.DrawSkybox(camera);
        //?为什么放在这里画？
        //@性能考量：天空盒通常覆盖整个屏幕。如果先画天空盒再画不透明物体，会浪费大量像素处理不必要的背景。
        //@逻辑关系：在不透明物体画完后，天空盒只会在没有物体遮挡的空白区域"填充"像素。
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
