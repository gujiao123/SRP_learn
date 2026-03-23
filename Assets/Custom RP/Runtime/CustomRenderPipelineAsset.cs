using UnityEngine;
using UnityEngine.Rendering;

//创建一个右点击菜单，方便创建CustomRenderPipeline 
[CreateAssetMenu(menuName = "Rendering/Custom Render Pipeline")]//menuName 是右键菜单的名字
//@用于设置的保存和配置
//所以渲染管线资源本身只是一个存储渲染设置的东西，以及给 Unity 提供一种方法来获取负责渲染的管线对象实例。
public class CustomRenderPipelineAsset : RenderPipelineAsset
{
    [SerializeField]
    ShadowSettings shadows = default;

    // me12: 允许 HDR 渲染（项目级总开关，还需相机自身 allowHDR=true 才生效）
    [SerializeField]
    bool allowHDR = true;

    // me13: Color LUT 分辨率（16/32/64，越大精度越高但内存越大）
    // 32 是默认值，通常足够，banding明显时再用 64
    public enum ColorLUTResolution { _16 = 16, _32 = 32, _64 = 64 }
    [SerializeField]
    ColorLUTResolution colorLUTResolution = ColorLUTResolution._32;

    // me11: 后处理设置
    [SerializeField]
    PostFXSettings postFXSettings = default;

    //用于动态批处理 过时技术,消耗大量cpu资源
    // 1. 定义序列化字段 (面板开关)
    [SerializeField]
    //动态批处理(对应可见物体的gpu实例化) 和 GPU 实例化(对应可见物体的gpu实例化) 和 SRP Batcher(全局按钮)  
    bool useDynamicBatching = true, useGPUInstancing = true, useSRPBatcher = true,
         useLightsPerObject = true; // me09: 默认开启每物体灯光索引 渲染时候告诉摄像机 在range范围内就要去计算阴影烘焙的
    //重写创建实际RenderPipeline的函数
    // 2. 传给管线实例
    protected override RenderPipeline CreatePipeline()
    {
        return new CustomRenderPipeline(
            allowHDR,               // me12
            useDynamicBatching, useGPUInstancing, useSRPBatcher,
            useLightsPerObject,     // me09
            shadows,
            postFXSettings,         // me11
            (int)colorLUTResolution // me13: LUT分辨率转为 int（16/32/64）
        );
    }


}





