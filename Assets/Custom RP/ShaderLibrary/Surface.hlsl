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
};

#endif