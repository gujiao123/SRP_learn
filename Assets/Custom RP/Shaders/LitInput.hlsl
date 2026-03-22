#ifndef CUSTOM_LIT_INPUT_INCLUDED
#define CUSTOM_LIT_INPUT_INCLUDED

// =============================================
// 🏦 材质数据银行 (Lit 材质的公共输入)
// 所有需要读取 Lit 材质属性的 Pass，统一从这里取数据
// 以后新增属性也只需要改这一个文件！
// =============================================

// 主色贴图 + 自发光贴图
// 注意：两张贴图共用同一个采样器 sampler_BaseMap（节省资源）
TEXTURE2D(_BaseMap);
TEXTURE2D(_EmissionMap);
SAMPLER(sampler_BaseMap);

// GPU 实例化材质属性数组
// 每个实例可以有自己独立的 _BaseColor 等属性值
UNITY_INSTANCING_BUFFER_START(UnityPerMaterial)
    UNITY_DEFINE_INSTANCED_PROP(float4, _BaseMap_ST)  // 纹理的 Tiling / Offset
    UNITY_DEFINE_INSTANCED_PROP(float4, _BaseColor)   // 主颜色
    UNITY_DEFINE_INSTANCED_PROP(float4, _EmissionColor)// 自发光颜色（支持 HDR 超亮度）
    UNITY_DEFINE_INSTANCED_PROP(float,  _Cutoff)      // Alpha 剪裁阈值
    UNITY_DEFINE_INSTANCED_PROP(float,  _Metallic)    // 金属度
    UNITY_DEFINE_INSTANCED_PROP(float,  _Smoothness)  // 光滑度
    UNITY_DEFINE_INSTANCED_PROP(float,  _Fresnel)  // me07: 菲涅耳反射强度滑条
UNITY_INSTANCING_BUFFER_END(UnityPerMaterial)

// =============================================
// 📦 标准接口函数 (银行的"取款窗口")
// 所有 Pass 通过这些函数来获取材质数据，不直接操作内部变量
// 参数 baseUV：基础纹理的采样坐标
// =============================================

// 将基础 UV 应用贴图的 Tiling 和 Offset
float2 TransformBaseUV (float2 baseUV) {
    float4 baseST = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _BaseMap_ST);
    return baseUV * baseST.xy + baseST.zw;
}

// 获取基础颜色（贴图颜色 × 材质颜色叠乘）
float4 GetBase (float2 baseUV) {
    float4 map   = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, baseUV);
    float4 color = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _BaseColor);
    return map * color;
}

// 获取 Alpha 剪裁阈值
float GetCutoff (float2 baseUV) {
    return UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _Cutoff);
}

// 获取金属度
float GetMetallic (float2 baseUV) {
    return UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _Metallic);
}

// 获取光滑度
float GetSmoothness (float2 baseUV) {
    return UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _Smoothness);
}

// 获取自发光颜色（发光贴图颜色 × 发光颜色属性）
// 和 GetBase 逻辑完全一致，只是换了贴图和颜色认入
// 出口是 float3，不需要 alpha 通道
float3 GetEmission (float2 baseUV) {
    float4 map   = SAMPLE_TEXTURE2D(_EmissionMap, sampler_BaseMap, baseUV);
    float4 color = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _EmissionColor);
    return map.rgb * color.rgb;
}

// me07: 菲涅耳强度滑条（1.0=开启菲涅耳，0.0=关闭）
float GetFresnel (float2 baseUV) {
    return UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _Fresnel);
}

#endif
