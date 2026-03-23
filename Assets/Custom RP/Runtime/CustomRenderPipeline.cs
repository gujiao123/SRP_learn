using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// me09: partial class，Editor 部分在 CustomRenderPipeline.Editor.cs 里
public partial class CustomRenderPipeline : RenderPipeline
{
    // 存储开关状态
    bool useDynamicBatching, useGPUInstancing;
    bool useLightsPerObject; // me09: 每物体灯光索引开关

    ShadowSettings shadowSettings;

    // 构造函数接收参数
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
            // 把开关传给 Renderer
            renderer.Render(
                context, camera,
                useDynamicBatching, useGPUInstancing,
                useLightsPerObject, // me09
                shadowSettings
            );
        }
    }

    
    CameraRenderer renderer = new CameraRenderer();//创建一个CameraRenderer对象
}