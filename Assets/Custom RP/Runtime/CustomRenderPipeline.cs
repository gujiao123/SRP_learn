using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// me09: partial class，Editor 部分在 CustomRenderPipeline.Editor.cs 里
public partial class CustomRenderPipeline : RenderPipeline
{
    // 存储开关状态
    bool useDynamicBatching, useGPUInstancing;
    bool useLightsPerObject;
    bool allowHDR; // me12

    ShadowSettings shadowSettings;
    PostFXSettings postFXSettings; // me11

    // 构造函数接收参数
    public CustomRenderPipeline(
        bool allowHDR,                 // me12
        bool useDynamicBatching, bool useGPUInstancing, bool useSRPBatcher,
        bool useLightsPerObject,       // me09
        ShadowSettings shadowSettings,
        PostFXSettings postFXSettings  // me11
    )
    {
        this.allowHDR = allowHDR;  // me12
        this.useDynamicBatching = useDynamicBatching;
        this.useGPUInstancing = useGPUInstancing;
        this.useLightsPerObject = useLightsPerObject;
        this.shadowSettings = shadowSettings;
        this.postFXSettings = postFXSettings; // me11

        GraphicsSettings.useScriptableRenderPipelineBatching = useSRPBatcher;
        GraphicsSettings.lightsUseLinearIntensity = true;

        // me09: 初始化 Editor 专用逻辑（烘焙 Falloff Delegate）
        InitializeForEditor();
    }

    // 声明 partial 方法（Editor 版在 CustomRenderPipeline.Editor.cs 里实现）
    partial void InitializeForEditor();

    // 旧版接口保留（Unity 要求重写，但实际用下面的 List 版本）
    protected override void Render(ScriptableRenderContext context, Camera[] cameras) { }

    protected override void Render(ScriptableRenderContext context, List<Camera> cameras)
    {
        foreach (Camera camera in cameras)
        {
            // 把开关传给 Renderer
            renderer.Render(
                context, camera,
                allowHDR,           // me12
                useDynamicBatching, useGPUInstancing,
                useLightsPerObject, // me09
                shadowSettings,
                postFXSettings      // me11
            );
        }
    }


    CameraRenderer renderer = new CameraRenderer();//创建一个CameraRenderer对象
}