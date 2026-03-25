#ifndef CUSTOM_PARTICLES_UNLIT_INPUT_INCLUDED
#define CUSTOM_PARTICLES_UNLIT_INPUT_INCLUDED

// me15: 粒子专用 Input——基于 UnlitInput 扩展
// 新增：_NearFadeDistance/Range, _SoftParticlesDistance/Range
//       _DistortionMap, _DistortionStrength/_Blend

TEXTURE2D(_BaseMap);
SAMPLER(sampler_BaseMap);

// me15: 扭曲贴图（法线图，xy 通道存扭曲方向）
TEXTURE2D(_DistortionMap);
SAMPLER(sampler_DistortionMap);

UNITY_INSTANCING_BUFFER_START(UnityPerMaterial)
    UNITY_DEFINE_INSTANCED_PROP(float4, _BaseMap_ST)
    UNITY_DEFINE_INSTANCED_PROP(float4, _BaseColor)
    UNITY_DEFINE_INSTANCED_PROP(float,  _NearFadeDistance)   // me15: 近面渐隐起始距离
    UNITY_DEFINE_INSTANCED_PROP(float,  _NearFadeRange)      // me15: 近面渐隐过渡范围
    UNITY_DEFINE_INSTANCED_PROP(float,  _SoftParticlesDistance) // me15: 软粒子起始距离
    UNITY_DEFINE_INSTANCED_PROP(float,  _SoftParticlesRange)   // me15: 软粒子过渡范围
    UNITY_DEFINE_INSTANCED_PROP(float,  _DistortionStrength) // me15: 扭曲强度
    UNITY_DEFINE_INSTANCED_PROP(float,  _DistortionBlend)    // me15: 扭曲与原色混合比
    UNITY_DEFINE_INSTANCED_PROP(float,  _Cutoff)
    UNITY_DEFINE_INSTANCED_PROP(float,  _ZWrite)
UNITY_INSTANCING_BUFFER_END(UnityPerMaterial)

#define INPUT_PROP(name) UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, name)

// =============================================
// InputConfig：把所有片元级别的配置集中到一个结构体
// 避免大量零散参数传来传去
// =============================================
struct InputConfig {
    Fragment fragment;          // me15: 片元屏幕坐标/深度（来自 Fragment.hlsl）
    float4 color;               // me15: 顶点颜色（粒子系统每粒子颜色）
    float2 baseUV;
    float3 flipbookUVB;         // me15: Flipbook 第二帧 UV + 混合因子
    bool flipbookBlending;      // me15: 是否启用帧间混合
    bool nearFade;              // me15: 是否启用近面渐隐
    bool softParticles;         // me15: 是否启用软粒子（需深度纹理）
};

InputConfig GetInputConfig(float4 positionCS_SS, float2 baseUV) {
    InputConfig c;
    c.fragment = GetFragment(positionCS_SS); // me15: 从屏幕位置构建 Fragment
    c.color = 1.0;              // 默认白色（无顶点颜色时不改变贴图颜色）
    c.baseUV = baseUV;
    c.flipbookUVB = 0.0;
    c.flipbookBlending = false;
    c.nearFade = false;
    c.softParticles = false;
    return c;
}

float2 TransformBaseUV(float2 baseUV) {
    float4 baseST = INPUT_PROP(_BaseMap_ST);
    return baseUV * baseST.xy + baseST.zw;
}

// me15: GetBase：集中所有 alpha 修改逻辑（顶点色 / Flipbook / 近面渐隐 / 软粒子）
float4 GetBase(InputConfig c) {
    float4 baseMap = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, c.baseUV);

    // me15: Flipbook 帧间混合（lerp 两帧贴图）
    if (c.flipbookBlending) {
        baseMap = lerp(
            baseMap,
            SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, c.flipbookUVB.xy),
            c.flipbookUVB.z
        );
    }

    // me15: 近面渐隐：depth 越小（越近）→ attenuation 越小 → alpha 越低
    if (c.nearFade) {
        float nearAttenuation = (c.fragment.depth - INPUT_PROP(_NearFadeDistance))
                                / INPUT_PROP(_NearFadeRange);
        baseMap.a *= saturate(nearAttenuation);
    }

    // me15: 软粒子：bufferDepth（场景固体深度）- fragment.depth（粒子自身深度）
    // 差值越小（粒子越贴近固体表面）→ attenuation 越小 → alpha 越低
    if (c.softParticles) {
        float depthDelta = c.fragment.bufferDepth - c.fragment.depth;
        float nearAttenuation = (depthDelta - INPUT_PROP(_SoftParticlesDistance))
                                / INPUT_PROP(_SoftParticlesRange);
        baseMap.a *= saturate(nearAttenuation);
    }

    float4 baseColor = INPUT_PROP(_BaseColor);
    // me15: 顶点颜色乘入最终颜色（c.color 默认 1.0 = 不影响）
    return baseMap * baseColor * c.color;
}

// me15: 扭曲向量（从法线图中解码）
float2 GetDistortion(InputConfig c) {
    float4 rawMap = SAMPLE_TEXTURE2D(_DistortionMap, sampler_DistortionMap, c.baseUV);
    // UnpackNormal→解码法线 → 取 xy 分量作为屏幕空间偏移
    return DecodeNormal(rawMap, 1.0).xy * INPUT_PROP(_DistortionStrength);
}

float GetDistortionBlend(InputConfig c) {
    return INPUT_PROP(_DistortionBlend);
}

float GetCutoff(InputConfig c) { return INPUT_PROP(_Cutoff); }

float GetFinalAlpha(float alpha) {
    return INPUT_PROP(_ZWrite) ? 1.0 : alpha;
}

#endif
