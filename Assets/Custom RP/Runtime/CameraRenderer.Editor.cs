using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;


//G partial 关键字允许将类、结构体或接口的定义分散在多个文件中。
//me 这样做的好处是 可以根据不同平台或者不同版本来编写不同的代码
//也就是说 一旦没有定义对应的实现就直接删除

public partial class CameraRenderer
{
    // me11: Gizmos 拆分 — 后处理之前和之后分别绘制
    partial void DrawGizmosBeforeFX();
    partial void DrawGizmosAfterFX();
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
        //作用：当你查看性能分析器时，你会一眼看到哪些开销是"编辑器特有的"。
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
    /// 绘制使用不支持的Shader Pass的物体。
    /// </summary>
    partial void DrawUnsupportedShaders()
    {
        //获取Error材质
        if (errorMaterial == null)
        {
            errorMaterial = new Material(Shader.Find("Hidden/InternalErrorShader"));
        }
        var drawingSettings = new DrawingSettings(legacyShaderTagIds[0], new SortingSettings(camera))
        {
            overrideMaterial = errorMaterial
        };

        for (int i = 1; i < legacyShaderTagIds.Length; i++)
        {
            drawingSettings.SetShaderPassName(i, legacyShaderTagIds[i]);
        }

        var filteringSettings = FilteringSettings.defaultValue;
        context.DrawRenderers(cullingResults, ref drawingSettings, ref filteringSettings);
    }

    // me11: PreImageEffects — 这些 Gizmos 会受后处理影响
    partial void DrawGizmosBeforeFX()
    {
        if (Handles.ShouldRenderGizmos())
        {
            context.DrawGizmos(camera, GizmoSubset.PreImageEffects);
        }
    }

    // me11: PostImageEffects — 这些 Gizmos 保持清晰，不受后处理影响
    partial void DrawGizmosAfterFX()
    {
        if (Handles.ShouldRenderGizmos())
        {
            context.DrawGizmos(camera, GizmoSubset.PostImageEffects);
        }
    }

    /// <summary>
    /// 绘制UI 这个比较特殊 在editor模式下要显式告诉渲染管线
    /// </summary>
    partial void PrepareForSceneWindow()
    {
        if (camera.cameraType == CameraType.SceneView)
        {
            ScriptableRenderContext.EmitWorldGeometryForSceneView(camera);
        }
    }

#else
    // 正式版：直接用常量，省电省内存
	const string SampleName = bufferName;
#endif
}