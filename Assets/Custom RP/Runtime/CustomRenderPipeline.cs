using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// me09: partial class，Editor 部分在 CustomRenderPipeline.Editor.cs 里
public partial class CustomRenderPipeline : RenderPipeline
{
    // 存储开关状态
    bool useDynamicBatching, useGPUInstancing;
    bool useLightsPerObject;
    bool allowHDR;           // me12
    int colorLUTResolution;  // me13: LUT 分辨率（16/32/64）

    ShadowSettings shadowSettings;
    PostFXSettings postFXSettings; // me11

    // 构造函数接收参数
    public CustomRenderPipeline(
        bool allowHDR,                 // me12
        bool useDynamicBatching, bool useGPUInstancing, bool useSRPBatcher,
        bool useLightsPerObject,       // me09
        ShadowSettings shadowSettings,
        PostFXSettings postFXSettings, // me11
        int colorLUTResolution         // me13
    )
    {
        this.allowHDR = allowHDR;
        this.colorLUTResolution = colorLUTResolution; // me13
        this.useDynamicBatching = useDynamicBatching;
        this.useGPUInstancing = useGPUInstancing;
        this.useLightsPerObject = useLightsPerObject;
        this.shadowSettings = shadowSettings;
        this.postFXSettings = postFXSettings;

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
                allowHDR,            // me12
                useDynamicBatching, useGPUInstancing,
                useLightsPerObject,  // me09
                shadowSettings,
                postFXSettings,      // me11
                colorLUTResolution   // me13
            );
        }
    }


    CameraRenderer renderer = new CameraRenderer();//创建一个CameraRenderer对象
}