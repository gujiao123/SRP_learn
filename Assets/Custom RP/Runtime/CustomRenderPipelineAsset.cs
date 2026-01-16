using UnityEngine;
using UnityEngine.Rendering;

//创建一个右点击菜单，方便创建CustomRenderPipeline 
[CreateAssetMenu(menuName = "Rendering/Custom Render Pipeline")]//menuName 是右键菜单的名字
//@用于设置的保存和配置
//所以渲染管线资源本身只是一个存储渲染设置的东西，以及给 Unity 提供一种方法来获取负责渲染的管线对象实例。
public class CustomRenderPipelineAsset : RenderPipelineAsset
{
    //重写创建实际RenderPipeline的函数
    protected override RenderPipeline CreatePipeline()
    {
        return new CustomRenderPipeline();
    }
}   