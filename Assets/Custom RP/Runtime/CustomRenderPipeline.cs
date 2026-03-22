using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// me09: partial class，Editor 部分在 CustomRenderPipeline.Editor.cs 里
public partial class CustomRenderPipeline : RenderPipeline
{
    bool useDynamicBatching, useGPUInstancing;
    bool useLightsPerObject; // me09: 每物体灯光索引开关

    ShadowSettings shadowSettings;

    public CustomRenderPipeline (
        bool useDynamicBatching, bool useGPUInstancing, bool useSRPBatcher,
        bool useLightsPerObject,       // me09
        ShadowSettings shadowSettings
    ) {
        this.useDynamicBatching  = useDynamicBatching;
        this.useGPUInstancing    = useGPUInstancing;
        this.useLightsPerObject  = useLightsPerObject;
        this.shadowSettings      = shadowSettings;

        GraphicsSettings.useScriptableRenderPipelineBatching = useSRPBatcher;
        GraphicsSettings.lightsUseLinearIntensity = true;

        // me09: 初始化 Editor 专用逻辑（烘焙 Falloff Delegate）
        InitializeForEditor();
    }

    // 声明 partial 方法（Editor 版在 CustomRenderPipeline.Editor.cs 里实现）
    partial void InitializeForEditor();

    // 旧版接口保留（Unity 要求重写，但实际用下面的 List 版本）
    protected override void Render(ScriptableRenderContext context, Camera[] cameras) { }

    protected override void Render (ScriptableRenderContext context, List<Camera> cameras) {
        foreach (Camera camera in cameras) {
            renderer.Render(
                context, camera,
                useDynamicBatching, useGPUInstancing,
                useLightsPerObject, // me09
                shadowSettings
            );
        }
    }

    CameraRenderer renderer = new CameraRenderer();
}