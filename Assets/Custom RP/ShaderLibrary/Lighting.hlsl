#ifndef CUSTOM_LIGHTING_INCLUDED
#define CUSTOM_LIGHTING_INCLUDED

// Square 已移到 Common.hlsl，此处不重复定义

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

// me14: 判断物体和灯光的渲染层是否有交集（按位 AND）
bool RenderingLayersOverlap(Surface surface, Light light) {
    return (surface.renderingLayerMask & light.renderingLayerMask) != 0;
}


// 5. 主循环：方向光 + 点光源/聚光灯
float3 GetLighting (Surface surfaceWS, BRDF brdf, GI gi) {
    ShadowData shadowData = GetShadowData(surfaceWS);
    // me06：把 GI 模块读到的烘焙遮罩数据搬运给阴影计算模块
    shadowData.shadowMask = gi.shadowMask;
    // me07: 用 IndirectBRDF 合并漫反射间接光 + 镜面环境反射
    float3 color = IndirectBRDF(surfaceWS, brdf, gi.diffuse, gi.specular);

    // 方向光循环（不变）
    for (int i = 0; i < GetDirectionalLightCount(); i++) {
        Light light = GetDirectionalLight(i, surfaceWS, shadowData);
        // 改成：
        if (RenderingLayersOverlap(surfaceWS, light)) {
            color += GetLighting(surfaceWS, brdf, light);
        }
    }

    // me09: Other Lights 循环（支持每物体灯光索引优化）
    #if defined(_LIGHTS_PER_OBJECT)
        // 每物体灯光模式：Unity 预算好哪些灯影响这个物体（最多8盏）
        // unity_LightData.y = 影响此物体的灯数
        // unity_LightIndices = 最多 2×float4 = 8 个灯光索引
        for (int j = 0; j < min(unity_LightData.y, 8); j++) {
            // uint 除法比 int 更快（GPU 优化）
            int lightIndex = unity_LightIndices[(uint)j / 4][(uint)j % 4];
            Light light = GetOtherLight(lightIndex, surfaceWS, shadowData);
            if (RenderingLayersOverlap(surfaceWS, light)) {
                color += GetLighting(surfaceWS, brdf, light);
            }
        }
    #else
        // 标准模式：遍历所有可见 Other Lights（最多64盏）
        for (int j = 0; j < GetOtherLightCount(); j++) {
            Light light = GetOtherLight(j, surfaceWS, shadowData);
            if (RenderingLayersOverlap(surfaceWS, light)) {
                color += GetLighting(surfaceWS, brdf, light);
            }
        }
    #endif

    return color;
}

#endif