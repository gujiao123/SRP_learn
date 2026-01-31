using UnityEngine;
using UnityEngine.Rendering;

//创建一个右点击菜单，方便创建CustomRenderPipeline 
[CreateAssetMenu(menuName = "Rendering/Custom Render Pipeline")]//menuName 是右键菜单的名字
//@用于设置的保存和配置
//所以渲染管线资源本身只是一个存储渲染设置的东西，以及给 Unity 提供一种方法来获取负责渲染的管线对象实例。
public class CustomRenderPipelineAsset : RenderPipelineAsset
{
    

    //用于动态批处理 过时技术,消耗大量cpu资源
    // 1. 定义序列化字段 (面板开关)
    [SerializeField]
    //动态批处理(对应可见物体的gpu实例化) 和 GPU 实例化(对应可见物体的gpu实例化) 和 SRP Batcher(全局按钮)  
    bool useDynamicBatching = true, useGPUInstancing = true, useSRPBatcher = true;
    //重写创建实际RenderPipeline的函数
    // 2. 传给管线实例
    protected override RenderPipeline CreatePipeline () {
        return new CustomRenderPipeline(
            useDynamicBatching, useGPUInstancing, useSRPBatcher
        );
    }
    
}   