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
    };//G 初始化成员的语法，这里初始化了一个CommandBuffer对象，并且给它命名为“Render Camera”
    
    
    // 1. 实例化 Lighting 类
    Lighting lighting = new Lighting();
    
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
    public void Render(ScriptableRenderContext context, Camera camera,bool useDynamicBatching, bool useGPUInstancing, // <--- 新增参数用于动态批处理
        ShadowSettings shadowSettings // 新增参数

    )
    {
        
        //设定当前上下文和摄像机
        this.context = context;
        this.camera = camera;
        PrepareBuffer();//每个摄像机渲染器都会调用的函数，用于准备CommandBuffer   设置对应Buffer名字
        PrepareForSceneWindow();//渲染所有编辑世界(scene)的物体 然后剔除
        if (!Cull(shadowSettings.maxDistance)) { // 使用 Max Distance 剔除
            return;
        }
        
        //!!我们需要在渲染实际摄像机画面之前渲染阴影贴图
        //???不明白啊 ,到底 我们 屏幕上显示的是什么
        // 2. 在画物体之前，先设置灯光！
        //规定好条目
        buffer.BeginSample(SampleName);
        ExecuteBuffer();//提交一下 才能生效
        //将光源信息传递给GPU，在其中也会完成阴影贴图的渲染
        lighting.Setup(context, cullingResults, shadowSettings);
        buffer.EndSample(SampleName);
        
        
        //然后 把画布修改交给摄像机RT,
        //!!不能反过来,不然会导致 所有作画都在阴影上,而阴影贴图没有颜色通道
        Setup();
        
        // --- 新增：强制切回摄像机 RT ---
        // 方法 A：再次调用 SetupCameraProperties
        //?? 为了 解决 scene摄像机 画了阴影贴图 并不能回到大场景中 导致屏幕一片灰色 
        //context.SetupCameraProperties(camera);
        
        //注意是对shadertag 也就是filter来进行区别绘制
        // 传给 DrawVisibleGeometry
        DrawVisibleGeometry(useDynamicBatching, useGPUInstancing); 
        DrawUnsupportedShaders();
        DrawGizmos();
        
        lighting.Cleanup(); // 新增：在 Submit 之前清理

        //准备好缓冲后提交
        Submit();
    }

    /// <summary>
    /// 配置渲染上下文和摄像机属性，根据摄像机的清除标志来清除渲染目标，并开始采样。
    /// </summary>
    void Setup()
    {
        
        //!! 这个也会重新绑定RT 也就是说 这个会抢夺 目前GPU处理的 图像 
        context.SetupCameraProperties(camera);//把摄像机的属性传递给当前上下文 得到对应的MVP矩阵
        //me 这个是自己可在editor内选择
        //me做什么选择 只是选择一个枚举值罢了具体的逻辑还是你自己写 你就当成一个数字好了
        /*
        Skybox	1	画天空盒（需要清理深度）
        Color	2	画纯色背景（需要清理深度）
        Depth	3	只清深度，保留之前颜色 
        Nothing	4	啥也不清*/
        CameraClearFlags flags = camera.clearFlags;//选择摄像机的清除标志 枚举类
        //Debug.Log("flags:" + (int)flags);
        
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

    
    
    
    
    /// <summary>
    /// 绘制所有可见几何体，包括不透明和透明物体，并绘制天空盒。
    /// 该方法首先根据当前摄像机的设置来渲染不透明物体，然后修改排序设置以渲染透明物体。
    /// 最后调用Unity内置函数绘制天空盒。
    /// </summary>
    void DrawVisibleGeometry (bool useDynamicBatching, bool useGPUInstancing) {    
        
        
        
        //!第一阶段：渲染不透明物体 (Opaque)
        // 1. 根据相机初始化排序设置（获取相机位置、投影等）
        //! 这个默认的sortingSettings.criteria  criteria（排序准则）是None 也就是随机绘制 
        var sortingSettings = new SortingSettings(camera);
        //me 当然你可以在这里sortingSettings.criteria=  SortingCriteria.CommonOpaque 强制从前往后
        //决定摄像机支持的Shader Pass和绘制顺序等的配置
        //第二章新增是否开始动态批处理或者是gpu实例化的选项在管线中
        var drawingSettings = new DrawingSettings(
            unlitShaderTagId, sortingSettings//unlitId和顺序画画
        ) {
            // --- 核心应用点 ---
            enableDynamicBatching = useDynamicBatching,
            enableInstancing = useGPUInstancing,
            // -----------------
            //me05 你不主动写unity就不得发送,这里是要static物体的烘焙好的光照贴图坐标数据
            //me05 光照探针数据也要上报,不是这个光照探针又是什么鬼物体为什么要存储光照探针数据??存储的是什么
            perObjectData = PerObjectData.Lightmaps | PerObjectData.LightProbe
        };
        
        // 关键：添加第二个 Pass Name
        //第一个参数是槽位,第二是对应passtag  unlitShaderTagId就是从0槽位开始
        drawingSettings.SetShaderPassName(1, litShaderTagId);
        
        
        //设置绘制不透明物体
        var filteringSettings = new FilteringSettings(RenderQueueRange.opaque);//设置过滤对象
        //渲染CullingResults内的VisibleObjects
        context.DrawRenderers(cullingResults, ref drawingSettings, ref filteringSettings);
        //!第二阶段：绘制天空盒 (Skybox)
        //添加“绘制天空盒”指令，DrawSkybox为ScriptableRenderContext下已有函数，这里就体现了为什么说Unity已经帮我们封装好了很多我们要用到的函数，SPR的画笔~
        context.DrawSkybox(camera);
        //?为什么放在这里画？
        //@性能考量：天空盒通常覆盖整个屏幕。如果先画天空盒再画不透明物体，会浪费大量像素处理不必要的背景。
        //@逻辑关系：在不透明物体画完后，天空盒只会在没有物体遮挡的空白区域“填充”像素。
        //!第三阶段：渲染透明物体 (Transparent)
        sortingSettings.criteria = SortingCriteria.CommonTransparent;//设置顺序为透明物体顺序从后往前
        drawingSettings.sortingSettings = sortingSettings;//修改成员   sortingSettings.criteria决定渲染顺序默认为None
        filteringSettings.renderQueueRange = RenderQueueRange.transparent;//绘制透明物体
        //todo 这个filteringSettings里面还是有很多门道的 后续问问ai 
        context.DrawRenderers( cullingResults, ref drawingSettings, ref filteringSettings);
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
