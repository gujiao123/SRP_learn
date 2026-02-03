using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class CustomRenderPipeline : RenderPipeline
{
    // 存储开关状态
    bool useDynamicBatching, useGPUInstancing;
    
    // 构造函数接收参数
    public CustomRenderPipeline (
        bool useDynamicBatching, bool useGPUInstancing, bool useSRPBatcher
    ) {
        this.useDynamicBatching = useDynamicBatching;
        this.useGPUInstancing = useGPUInstancing;
        
        // 这就是开启 SRP Batcher 的全局开关
        // 一旦开启，Unity 会尝试把所有兼容的 Shader 自动合批
        GraphicsSettings.useScriptableRenderPipelineBatching = useSRPBatcher;
        
        // 必须开启，否则灯光强度不对
        GraphicsSettings.lightsUseLinearIntensity = true; 
    }
   
    
    //必须重写Render函数，目前函数内部什么都不执行
    //me 这个是老版本的不用的
    protected override void Render(ScriptableRenderContext context, Camera[] cameras)
    {

    }
    //!每帧调用
    protected override void Render (ScriptableRenderContext context, List<Camera> cameras) {
        foreach (Camera camera in cameras) {
            // 把开关传给 Renderer
            renderer.Render(
                context, camera, useDynamicBatching, useGPUInstancing
            );
        }
    }
    
    
    CameraRenderer renderer = new CameraRenderer();//创建一个CameraRenderer对象
}