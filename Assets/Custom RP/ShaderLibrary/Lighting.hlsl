#ifndef CUSTOM_LIGHTING_INCLUDED
#define CUSTOM_LIGHTING_INCLUDED

// 辅助函数：平方
float Square (float v) {
    return v * v;
}

// 1. 计算入射光能量 (Lambert项)
float3 IncomingLight (Surface surface, Light light) {
    return
        saturate(dot(surface.normal, light.direction) * light.attenuation) *
        light.color;
}

// 2. 计算高光强度 (Cook-Torrance 简化版)
// 这是 PBR 的核心数学公式，决定了高光的大小和亮度
float SpecularStrength (Surface surface, BRDF brdf, Light light) {
    // 半向量 H = L + V
    float3 h = SafeNormalize(light.direction + surface.viewDirection);
    
    // N dot H
    float nh2 = Square(saturate(dot(surface.normal, h)));
    float lh2 = Square(saturate(dot(light.direction, h)));
    float r2 = Square(brdf.roughness);
    float d2 = Square(nh2 * (r2 - 1.0) + 1.00001);
    float normalization = brdf.roughness * 4.0 + 2.0;
    
    return r2 / (d2 * max(0.1, lh2) * normalization);
}

// 3. 计算直接光照的 BRDF 结果
float3 DirectBRDF (Surface surface, BRDF brdf, Light light) {
    // 最终颜色 = (高光强度 * 高光颜色) + 漫反射颜色
    return SpecularStrength(surface, brdf, light) * brdf.specular + brdf.diffuse;
}

// 4. 计算单盏灯的最终颜色
float3 GetLighting (Surface surface, BRDF brdf, Light light) {
    // 入射光 * BRDF函数
    return IncomingLight(surface, light) * DirectBRDF(surface, brdf, light);
}

// 5. 主循环
float3 GetLighting (Surface surfaceWS, BRDF brdf, GI gi) {
    ShadowData shadowData = GetShadowData(surfaceWS);
    // me06：把 GI 模块读到的烘焙遮罩数据搬运给阴影计算模块
    shadowData.shadowMask = gi.shadowMask;
    float3 color = gi.diffuse * brdf.diffuse;
    for (int i = 0; i < GetDirectionalLightCount(); i++) {
        Light light = GetDirectionalLight(i, surfaceWS, shadowData);
        color += GetLighting(surfaceWS, brdf, light);
    }
    // ========= 📸 DEBUG 可视化（用哪行取消注释哪行）=========
    //return gi.shadowMask.shadows.rrr;   // Shadow Mask R通道（第1盏灯）：黑=有阴影 白=无阴影
    //return gi.shadowMask.shadows.ggg;   // Shadow Mask G通道（第2盏灯）
    //return gi.shadowMask.shadows.rgba;  // Shadow Mask 全4通道彩色（RGBA=4盏灯）
    //return gi.diffuse;                  // Lightmap间接光：彩色，代表弹射光颜色分布
    //return float3(shadowData.shadowMask.distance, 0, 0); // 红=Distance模式已激活
    // =========================================================
    return color;
}

#endif