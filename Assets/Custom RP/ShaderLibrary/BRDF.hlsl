
#ifndef CUSTOM_BRDF_INCLUDED
#define CUSTOM_BRDF_INCLUDED

// 定义最小反射率 (非金属的 F0 通常是 0.04)
#define MIN_REFLECTIVITY 0.04

struct BRDF {
    float3 diffuse;
    float3 specular;
    float roughness;
};

// 计算非金属的反射率范围
float OneMinusReflectivity (float metallic) {
    float range = 1.0 - MIN_REFLECTIVITY;
    return range - metallic * range;
}


// 修改 GetBRDF 函数签名，增加一个 bool 参数
//me 对于 高光来说 玻璃 只负责 漫反射的透明 不负责1高光透明混合
BRDF GetBRDF (Surface surface, bool applyAlphaToDiffuse = false) {
    BRDF brdf;
    
    float oneMinusReflectivity = OneMinusReflectivity(surface.metallic);
    brdf.diffuse = surface.color * oneMinusReflectivity;
    
    // --- 核心逻辑 ---
    if (applyAlphaToDiffuse) {
        // 只有漫反射受 Alpha 影响
        brdf.diffuse *= surface.alpha;
    }
    
    // 2. 计算镜面反射颜色 (Specular Color)
    // 非金属是白色高光(F0=0.04)，金属是自身颜色高光
    brdf.specular = lerp(MIN_REFLECTIVITY, surface.color, surface.metallic);
    
    // 3. 计算粗糙度 (Roughness)
    // Smoothness 是感官上的光滑度，Roughness 是物理上的粗糙度
    // 转换公式：roughness = (1 - smoothness)^2
    // Core库提供了 PerceptualSmoothnessToPerceptualRoughness 等函数
    float perceptualRoughness = PerceptualSmoothnessToPerceptualRoughness(surface.smoothness);
    brdf.roughness = PerceptualRoughnessToRoughness(perceptualRoughness);
    
    return brdf;
}

#endif