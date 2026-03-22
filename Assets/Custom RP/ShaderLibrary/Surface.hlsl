#ifndef CUSTOM_SURFACE_INCLUDED
#define CUSTOM_SURFACE_INCLUDED

struct Surface {
    float3 normal;  // 世界空间法线
    float3 color;   // 表面基色 (Albedo)
    float alpha;    // 透明度
    // 新增
    float3 viewDirection; // <--- 必须加这个！
    float metallic;
    float smoothness;
    float fresnelStrength; // me07: 菲涅尔反射强度滑条（0=无菲涅尔, 1=完整菲涅尔）
    float3 position;
    float depth;//表面深度 用于裁剪剔除球
    float dither;//扰动值

};

#endif