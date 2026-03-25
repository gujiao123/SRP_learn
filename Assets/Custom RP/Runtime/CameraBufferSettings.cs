using System;
using UnityEngine;

// me15: 相机帧缓冲配置（替代原来散落的 allowHDR 布尔值）
// 集中管理：HDR / 深度拷贝 / 颜色拷贝 的开关
[Serializable]
public struct CameraBufferSettings
{
    // me12: 是否允许 HDR 渲染（还需相机自身 allowHDR=true 才生效）
    public bool allowHDR;

    // me15: 深度拷贝开关（软粒子、深度特效需要）
    [Tooltip("普通相机是否拷贝深度缓冲（软粒子需要）")]
    public bool copyDepth;
    [Tooltip("反射相机是否拷贝深度（通常不需要，性能开销大）")]
    public bool copyDepthReflection;

    // me15: 颜色拷贝开关（扭曲/折射特效需要）
    [Tooltip("普通相机是否拷贝颜色缓冲（扭曲效果需要）")]
    public bool copyColor;
    [Tooltip("反射相机是否拷贝颜色")]
    public bool copyColorReflection;
}
