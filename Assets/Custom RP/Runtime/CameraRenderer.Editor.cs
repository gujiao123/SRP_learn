using System;
using UnityEngine;
using UnityEngine.Rendering;


using UnityEditor;
using UnityEngine.Profiling;


//G partial 关键字允许将类、结构体或接口的定义分散在多个文件中。
//me 这样做的好处是 可以根据不同平台或者不同版本来编写不同的代码
//也就是说 一旦没有定义对应的实现就直接删除

public partial class CameraRenderer
{
    partial void DrawGizmos();//绘制摄像机 光源等表示
    partial void PrepareForSceneWindow();
    partial void PrepareBuffer();
    //定义分部函数的方式类似C++ 控制发行版本 和 编辑器版本的代码
    partial void DrawUnsupportedShaders();//这个在scene没有影响,在game中有影响
    
    
    

    //这块代码只会在Editor下起作用
    //也就是说runtime的时候 不会渲染下面错误tag  等等
#if UNITY_EDITOR
    string SampleName { get; set; }
    
    /// <summary>
    /// 设置每个camera将要debbug采样点的名称 相机名称即为采样点名称
    /// </summary>
    //!!由于每帧都会调用所以在game模式下 直接用常数字符串代替避免频繁访问
    partial void PrepareBuffer()
    {
        //作用：当你查看性能分析器时，你会一眼看到哪些开销是“编辑器特有的”。
        //!!editor才有拷贝会多一些 game没有就会只有固定的开销
        Profiler.BeginSample("Editor Only");
        // 编辑器版本：获取相机真实名字，方便在 Frame Debugger 看
        buffer.name = SampleName = camera.name;
        Profiler.EndSample();
    }
    
 
    
    //获取Unity默认的shader tag id 
    //me 为了让我们的管线能识别Unity内置的shader 并进行渲染
    private static ShaderTagId[] legacyShaderTagIds =
    {
            new ShaderTagId("Always"),
            new ShaderTagId("ForwardBase"),
            new ShaderTagId("PrepassBase"),
            new ShaderTagId("Vertex"),
            new ShaderTagId("VertexLMRGBM"),
            new ShaderTagId("VertexLM")
        };

    //Error Material
    private static Material errorMaterial;


    /// <summary>
    /// 也就是我们当前还有没有处理的Shader Pass
    /// 绘制使用不支持的Shader Pass的物体。此方法会使用一个错误材质来渲染这些物体，以便在场景中标识出使用了当前管线不支持的Shader Pass的对象。
    /// </summary>
    partial void DrawUnsupportedShaders()
    {
        //获取Error材质
        if (errorMaterial == null)
        {
            errorMaterial = new Material(Shader.Find("Hidden/InternalErrorShader"));
        }
        //绘制走不支持的Shader Pass的物体
        //me 这个相当于 drawingSettings.SetShaderPassName(0, legacyShaderTagIds[0]);了 只是默认要带一个
        var drawingSettings = new DrawingSettings(legacyShaderTagIds[0], new SortingSettings(camera))
        {
            //设置覆写的材质
            overrideMaterial = errorMaterial
        };

        //设置更多在此次DrawCall中要渲染的ShaderPass，也就是不支持的ShaderPass
        for (int i = 1; i < legacyShaderTagIds.Length; i++)
        {
            drawingSettings.SetShaderPassName(i, legacyShaderTagIds[i]);
        }
        
        var filteringSettings = FilteringSettings.defaultValue;
        context.DrawRenderers(cullingResults, ref drawingSettings, ref filteringSettings);
    }
    
    
    
    
    /// <summary>
    /// 绘制Gizmosx小控键
    /// </summary>
    partial void DrawGizmos()
    {
        //绘制摄像机的Gizmos
        //U 是否在 Unity 编辑器顶部的工具栏中勾选了 Gizmos 按钮
        if (Handles.ShouldRenderGizmos())
        {
            //让gizmos也收到后处理的影响
            //如果你开启了模糊（Blur）或景深，这部分 Gizmos 也会跟着变模糊。通常用于 3D 空间的物理框线。
            context.DrawGizmos(camera, GizmoSubset.PreImageEffects);
            //这部分 Gizmos 永远保持清晰，不会受屏幕特效影响。通常用于 UI 类型的辅助线或选中框。
            context.DrawGizmos(camera, GizmoSubset.PostImageEffects);
        }
    }
    
    /// <summary>
    /// 绘制UI 这个比较特殊 在editor模式下要显式告诉渲染管线 不同相机设置 效果不一样
    /// </summary>
    partial void PrepareForSceneWindow()
    {
        //绘制Scene窗口下的UI
        if (camera.cameraType == CameraType.SceneView)
        {
            //UI在scene场景中存在却不会被当做几何体对待 需要显式告诉渲染管线
            //而Game中则会渲染在canvas覆盖屏幕
            ScriptableRenderContext.EmitWorldGeometryForSceneView(camera);
        }
    }
    
#else
    // 正式版：直接用常量，省电省内存
	const string SampleName = bufferName;//在非编辑器模式下，直接使用bufferName作为SampleName 缓冲区的名字而不是摄像机
#endif
}


/*drawingSettings参数名称,作用,为什么它很重要
    ShaderTagId,通行证,决定搜索哪些 LightMode 标签。你可以添加多个，让一笔绘制同时支持多种 Shader。
sortingSettings,排队规则,决定物体是“由远及近”还是“由近及远”绘制，直接影响渲染正确性和 Overdraw 优化。
overrideMaterial,材质强制覆盖,像“报错粉色”或“全场阴影”逻辑，都需要通过它来强行改变物体的外表。
overrideMaterialPassIndex,指定通道索引,如果覆盖材质有多个 Pass，你可以指定只用其中的第几个。默认 0 是全部生效。
perObjectData,发送额外数据,告诉 GPU 渲染时是否需要带上：光照探针 (Light Probes)、光照贴图 (Lightmaps)、反射探针等。
enableDynamicBatching,动态批处理开关,是否允许 Unity 自动合并细小的模型以减少 Draw Call。
enableInstancing,GPU 实例化开关,是否开启 GPU Instancing（用于高效渲染大量重复模型，如草丛、石块）。*/



/*1.camera.cameraType == CameraType.SceneView 意思是如果相机在场景视图下吗

2.Game 到底是怎么渲染UI的 ,不是像对待几何体吗 是怎么渲染的

3.直接在scene里面渲染了吗 顺序呢 为什么不像普通的几何体一样对待


这三个问题直击 Unity UI 渲染的核心底层逻辑。在自定义渲染管线（SRP）中，UI 的特殊性源于它在“屏幕空间”与“世界空间”之间的切换。

### 1. `camera.cameraType == CameraType.SceneView` 是什么意思？

**是的，你的理解完全正确。**

    * **含义**：这行代码是在判断当前正在执行渲染指令的“相机”是不是编辑器里那个用来让你“飞来飞去”检查场景的**场景相机（Scene Camera）**。
* **背景**：在 Unity 编辑器中，`Game` 视图和 `Scene` 视图其实是两个不同的相机在干活。
* `CameraType.Game`：玩家真正看到的相机。
* `CameraType.SceneView`：你在编辑代码、摆放物体时用的那个相机。


* **为什么要判断**：因为 `EmitWorldGeometryForSceneView` 这个函数是一个“补丁”，它只在你想在场景窗口里看到 UI 时才需要运行。

---

### 2. Game 视图到底是怎么渲染 UI 的？

    在 Game 视图中，UI 的渲染取决于 **Canvas（画布）** 的 `Render Mode` 设置。它并不总是像普通的几何体（如 Cube）那样渲染。

#### A. Screen Space - Overlay (最常用)

* **逻辑**：UI 完全不属于 3D 世界。它像是一层**透明贴纸**，直接贴在相机的镜片上。
* **渲染方式**：它不参与 3D 空间的排序、不接受光照、没有深度。Unity 会在所有 3D 物体画完后，直接根据像素坐标把 UI “盖”在屏幕上。

#### B. Screen Space - Camera

* **逻辑**：UI 被放置在相机前方一段距离。
* **渲染方式**：Canvas 会动态生成一个平面网格（Mesh），然后这个网格会像普通几何体一样被投射到相机视锥体中。

#### C. World Space (世界空间)

* **逻辑**：UI 就像一个普通的 3D 广告牌，放在场景里的某个坐标。
* **渲染方式**：此时它**完全就是普通几何体**，会参与剔除和排序。

---

### 3. 为什么 UI 不能像普通几何体一样对待？

    这是一个关于**效率**与**编辑需求**的权衡结果。

#### 为什么不直接当成普通几何体？（效率原因）

* **频繁变动**：UI 经常动（比如血条减少、文字变化）。如果 UI 是普通 3D 几何体，每次变动都要去更新复杂的 3D 空间索引（如八叉树），非常耗电耗性能。
* **像素对齐**：UI 要求“像素完美”（Pixel Perfect）。如果作为 3D 物体在空间里转来转去，文字会因为采样问题变得模糊。

#### 为什么在 Scene 窗口要“显式添加”？

这是最关键的一点：

* 在 **Game 视图**里，UI 系统知道怎么直接把图画到屏幕上。
* 在 **Scene 视图**里，你作为开发者需要能够旋转镜头看到 UI 飘在空中，或者选中它。
* **矛盾点**：SRP（你写的这个管线）的剔除系统（`Cull`）默认只认“网格（Mesh）”。由于 UI（特别是 Overlay 模式）在内存里没有持久的 3D 网格，`Cull` 会直接无视它。
* **解决方案**：`EmitWorldGeometryForSceneView` 的作用就是**“临时伪造”**。它告诉 Unity：“请为这个 Scene 相机临时生成一套 UI 的 3D 几何数据，这样我的 `Cull` 就能抓到它，我的渲染器就能把它画出来。”

---

### 渲染顺序总结

    在你的 `DrawVisibleGeometry` 函数中，渲染顺序通常是：

1. **不透明几何体** (Opaque)
    2. **天空盒** (Skybox)
    3. **透明几何体** (Transparent)
    4. **UI 渲染** (通常包含在透明渲染步骤中，或者紧随其后)

**由于你现在正在手动构建管线，你是否遇到了 UI 遮挡了 3D 物体，或者 3D 物体反而把 UI 遮挡了的问题？这通常涉及 `FilteringSettings` 的渲染队列设置。*/