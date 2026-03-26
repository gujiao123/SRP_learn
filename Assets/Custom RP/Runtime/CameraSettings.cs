using System;
using UnityEngine;
using UnityEngine.Rendering;

[Serializable]
public class CameraSettings
{

    //me13新增的 混合模式 就是说 多一步渲染 利用 one minus src alpha 来混合 问题是如何从inspector传递到shader
    [Serializable]
    public struct FinalBlendMode
    {
        public BlendMode source, destination;
    }
    // me14: 每台相机独立的 PostFX 设置
    public bool overridePostFX = false;
    public PostFXSettings postFXSettings = default;

    // 默认：One Zero（覆盖，底层相机用）
    // 叠层相机改成：source=One, destination=OneMinusSrcAlpha
    public FinalBlendMode finalBlendMode = new FinalBlendMode
    {
        source = BlendMode.One,
        destination = BlendMode.Zero
    };

    // me14: 相机的渲染层遮罩（过滤哪些物体被这台相机渲染）
    // -1 = All Layers（默认不过滤）
    [RenderingLayerMaskField]
    public int renderingLayerMask = -1;

    // me14: 是否用 renderingLayerMask 来过滤灯光
    // false = 所有灯光正常生效（默认）
    // true  = 只接受与 renderingLayerMask 有交集的灯光
    public bool maskLights = false;

    // me15: 每台相机独立控制是否拷贝深度/颜色（配合 CameraBufferSettings 的全局开关）
    // 两者都 true 才真正拷贝
    public bool copyDepth = true;
    public bool copyColor = true;
    // me16: 每台相机独立的 renderScale 模式
    // Inherit  = 直接用管线全局的 renderScale（默认）
    // Multiply = 相机自己的 scale × 全局 scale（叠加）
    // Override = 忽略全局，用相机自己的 scale
    public enum RenderScaleMode { Inherit, Multiply, Override }
    public RenderScaleMode renderScaleMode = RenderScaleMode.Inherit;
    [Range(0.1f, 2f)]
    public float renderScale = 1f;
    // me16: 根据模式计算最终 renderScale
    public float GetRenderScale(float scale)
    {
        return renderScaleMode == RenderScaleMode.Inherit ? scale :
               renderScaleMode == RenderScaleMode.Override ? renderScale :
               scale * renderScale;  // Multiply
    }
}
