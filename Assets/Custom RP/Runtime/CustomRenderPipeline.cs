using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// me09: partial class，Editor 部分在 CustomRenderPipeline.Editor.cs 里
public partial class CustomRenderPipeline : RenderPipeline
{
    // 存储开关状态
    bool useDynamicBatching, useGPUInstancing;
    bool useLightsPerObject;
    int colorLUTResolution;  // me13: LUT 分辨率（16/32/64）

    // me15: 替代原来的 allowHDR，集中管理相机缓冲配置
    CameraBufferSettings cameraBufferSettings;

    ShadowSettings shadowSettings;
    PostFXSettings postFXSettings; // me11

    // 构造函数接收参数
    public CustomRenderPipeline(
        CameraBufferSettings cameraBufferSettings, // me15: 替代原来的 bool allowHDR
        bool useDynamicBatching, bool useGPUInstancing, bool useSRPBatcher,
        bool useLightsPerObject,       // me09
        ShadowSettings shadowSettings,
        PostFXSettings postFXSettings, // me11
        int colorLUTResolution,        // me13
        Shader cameraRendererShader    // me15: 全屏拷贝专属 Shader
    )
    {
        this.cameraBufferSettings = cameraBufferSettings; // me15
        this.colorLUTResolution = colorLUTResolution; // me13
        this.useDynamicBatching = useDynamicBatching;
        this.useGPUInstancing = useGPUInstancing;
        this.useLightsPerObject = useLightsPerObject;
        this.shadowSettings = shadowSettings;
        this.postFXSettings = postFXSettings;

        GraphicsSettings.useScriptableRenderPipelineBatching = useSRPBatcher;
        GraphicsSettings.lightsUseLinearIntensity = true;
        // me15: 创建 CameraRenderer，传入专属 Shader
        renderer = new CameraRenderer(cameraRendererShader);

        // me09: 初始化 Editor 专用逻辑（烘焙 Falloff Delegate）
        InitializeForEditor();
    }

    // 声明 partial 方法（Editor 版在 CustomRenderPipeline.Editor.cs 里实现）
    partial void InitializeForEditor();

    // me15: Dispose 专属 Editor 逻辑（重置烘焙 Delegate）
    partial void DisposeForEditor();

    // me15: 覆写 Dispose，确保 Editor 每次重建管线时正确释放材质/贴图
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        DisposeForEditor();      // Editor 专用清理（重置烘焙 Delegate）
        renderer.Dispose();      // 释放 CameraRenderer 内的材质和占位贴图
    }

    // 旧版接口保留（Unity 要求重写，但实际用下面的 List 版本）
    protected override void Render(ScriptableRenderContext context, Camera[] cameras) { }

    protected override void Render(ScriptableRenderContext context, List<Camera> cameras)
    {
        foreach (Camera camera in cameras)
        {
            // 把开关传给 Renderer
            renderer.Render(
                context, camera,
                cameraBufferSettings,  // me15: 替代原来的 allowHDR
                useDynamicBatching, useGPUInstancing,
                useLightsPerObject,  // me09
                shadowSettings,
                postFXSettings,      // me11
                colorLUTResolution   // me13
            );
        }
    }


    CameraRenderer renderer; // me15: 改为后置创建（需要 Shader 参数）
}