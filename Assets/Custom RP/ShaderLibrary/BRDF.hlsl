#ifndef CUSTOM_BRDF_INCLUDED
#define CUSTOM_BRDF_INCLUDED

// 定义最小反射率 (非金属的 F0 通常是 0.04)
#define MIN_REFLECTIVITY 0.04

struct BRDF {
    float3 diffuse;
    float3 specular;
    float roughness;
    float perceptualRoughness; // me07: 感知粗糙度（用于 mip 级别计算）
    float fresnel;             // me07: 菲涅尔反射强度（掠射角变镜面）
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
    brdf.perceptualRoughness = PerceptualSmoothnessToPerceptualRoughness(surface.smoothness);
    brdf.roughness = PerceptualRoughnessToRoughness(brdf.perceptualRoughness);
    
    // me07: Fresnel = 光滑度 + 反射率之和（控制掠射角镜面反射的强度上限）
    brdf.fresnel = saturate(surface.smoothness + 1.0 - oneMinusReflectivity);
    
    return brdf;
}

// me07: 间接光的 BRDF（漫反射 GI + 镜面环境反射）
float3 IndirectBRDF(Surface surface, BRDF brdf, float3 diffuse, float3 specular) {
    // Fresnel 强度：摄像机系法线越垂直（掠射角），镜射越强
    float fresnelStrength = surface.fresnelStrength *
        Pow4(1.0 - saturate(dot(surface.normal, surface.viewDirection)));
    // 镜面反射 = 环境 × lerp(材质镜面颜色, fresnel白色, 掠射强度)
    float3 reflection = specular * lerp(brdf.specular, brdf.fresnel, fresnelStrength);
    // 粗糙度越高，镜面反射越弱（光线被散射掉了）
    reflection /= brdf.roughness * brdf.roughness + 1.0;
    return diffuse * brdf.diffuse + reflection;
}

#endif