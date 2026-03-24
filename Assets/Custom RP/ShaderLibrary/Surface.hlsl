#ifndef CUSTOM_SURFACE_INCLUDED
#define CUSTOM_SURFACE_INCLUDED

struct Surface {
    float3 normal;             // 法线贴图提供的法线（用于光照计算）
    float3 interpolatedNormal; // me08: 插值几何法线（用于 Shadow Bias，不受法线贴图影响）这个要用物体自己定点法线解决自阴影问题 法线外推
    float3 color;   // 表面基色 (Albedo)
    float alpha;    // 透明度
    // 新增
    float3 viewDirection; // <--- 必须加这个！
    float metallic;
    float occlusion;       // me08: 细小区域的遇闭系数（0~1）
    float smoothness;
    float fresnelStrength; // me07: 菲涅尔反射强度滑条（0=无菲涅尔, 1=完整菲涅尔）
    float3 position;
    float depth;//表面深度 用于裁剪剔除球
    float dither;//扰动值
    uint renderingLayerMask;   // me14: 物体渲染层掩码（位掩码）
};

#endif