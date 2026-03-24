#ifndef CUSTOM_UNLIT_INPUT_INCLUDED
#define CUSTOM_UNLIT_INPUT_INCLUDED

// =============================================
// 🏦 材质数据银行 (Unlit 材质的公共输入)
// 相比 LitInput，Unlit 没有金属度和光滑度属性
// 但为了让 ShadowCasterPass 能正常复用，保留 GetMetallic / GetSmoothness 接口
// =============================================

// Unlit 只需要基础贴图（用于 Alpha Clipping）
TEXTURE2D(_BaseMap);
SAMPLER(sampler_BaseMap);

UNITY_INSTANCING_BUFFER_START(UnityPerMaterial)
    UNITY_DEFINE_INSTANCED_PROP(float4, _BaseMap_ST)
    UNITY_DEFINE_INSTANCED_PROP(float4, _BaseColor)
    UNITY_DEFINE_INSTANCED_PROP(float,  _Cutoff)
    UNITY_DEFINE_INSTANCED_PROP(float, _ZWrite)
UNITY_INSTANCING_BUFFER_END(UnityPerMaterial)

// =============================================
// 📦 标准接口函数
// =============================================

float2 TransformBaseUV (float2 baseUV) {
    float4 baseST = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _BaseMap_ST);
    return baseUV * baseST.xy + baseST.zw;
}

float4 GetBase (float2 baseUV) {
    float4 map   = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, baseUV);
    float4 color = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _BaseColor);
    return map * color;
}

float GetCutoff (float2 baseUV) {
    return UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _Cutoff);
}

// Unlit 没有金属度和光滑度，直接返回 0（保持接口兼容，不让 ShadowCasterPass 报错）
float GetMetallic (float2 baseUV) { return 0.0; }
float GetSmoothness (float2 baseUV) { return 0.0; }
float GetFresnel (float2 baseUV) { return 0.0; } // me07: Unlit 不需要菲涅尔反射

// Unlit 的自发光就是它本身的颜色！
// 这样设计的妙处：一个 Unlit 物体参与烘焙时，烘焙大师看到的就是它的全颜色，
// 而不是元气彌漫的白色！
float3 GetEmission (float2 baseUV) { return GetBase(baseUV).rgb; }

// me14: 获取最终 Alpha（根据 ZWrite 决定） 就是为了防蠢设置了不透明物体有alpha
float GetFinalAlpha(float alpha) {
    // ZWrite=1（不透明物体）→ alpha强制为1，避免半透明深度写入干扰叠层
    // ZWrite=0（透明物体）→ 保留真实alpha
    return UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _ZWrite) ? 1.0 : alpha;
}
#endif
