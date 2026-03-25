#ifndef CUSTOM_LIT_INPUT_INCLUDED
#define CUSTOM_LIT_INPUT_INCLUDED

// =============================================
// 🏦 材质数据银行 (Lit 材质的公共输入)
// 所有需要读取 Lit 材质属性的 Pass，统一从这里取数据
// 以后新增属性也只需要改这一个文件！
// =============================================

// me08: 简化宏，代替冗长的 UNITY_ACCESS_INSTANCED_PROP
#define INPUT_PROP(name) UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, name)

// 贴图声明（共用 sampler_BaseMap 采样器，节省资源）
TEXTURE2D(_BaseMap);
TEXTURE2D(_EmissionMap);
TEXTURE2D(_MaskMap);    // me08: MODS 遮罩图（R=Metallic, G=Occlusion, B=DetailMask, A=Smoothness）
TEXTURE2D(_DetailMap);  // me08: Detail Map，有自己的采样器（独立 Tiling）
TEXTURE2D(_NormalMap);       // me08: 法线贴图（平抦该贴图明显呈蓝色调）
TEXTURE2D(_DetailNormalMap); // me08: 细节法线贴图（更细的微表面凹凸）
SAMPLER(sampler_BaseMap);
SAMPLER(sampler_DetailMap);  // me08: Detail Map 独立采样器

// GPU 实例化材质属性数组
// 每个实例可以有自己独立的 _BaseColor 等属性值
UNITY_INSTANCING_BUFFER_START(UnityPerMaterial)
    UNITY_DEFINE_INSTANCED_PROP(float4, _BaseMap_ST)
    UNITY_DEFINE_INSTANCED_PROP(float4, _DetailMap_ST)  // me08: Detail Map 的独立 Tiling/Offset
    UNITY_DEFINE_INSTANCED_PROP(float4, _BaseColor)
    UNITY_DEFINE_INSTANCED_PROP(float4, _EmissionColor)
    UNITY_DEFINE_INSTANCED_PROP(float,  _Cutoff)
    UNITY_DEFINE_INSTANCED_PROP(float,  _Metallic)
    UNITY_DEFINE_INSTANCED_PROP(float,  _Occlusion)  // me08: 遮蔽强度（全局倍率）
    UNITY_DEFINE_INSTANCED_PROP(float,  _Smoothness)
    UNITY_DEFINE_INSTANCED_PROP(float,  _Fresnel)
    UNITY_DEFINE_INSTANCED_PROP(float,  _DetailAlbedo)     // me08: 细节颜色影响强度
    UNITY_DEFINE_INSTANCED_PROP(float,  _DetailSmoothness) // me08: 细节光滑度影响强度
    UNITY_DEFINE_INSTANCED_PROP(float,  _NormalScale)        // me08: 法线强度
    UNITY_DEFINE_INSTANCED_PROP(float,  _DetailNormalScale)  // me08: 细节法线强度
    UNITY_DEFINE_INSTANCED_PROP(float, _ZWrite)
UNITY_INSTANCING_BUFFER_END(UnityPerMaterial)

// =============================================
// 📦 接口函数
// =============================================

// 将基础 UV 应用贴图的 Tiling 和 Offset
float2 TransformBaseUV (float2 baseUV) {
    float4 baseST = INPUT_PROP(_BaseMap_ST);
    return baseUV * baseST.xy + baseST.zw;
}

// me08: Detail Map 有自己的 Tiling，单独变换
float2 TransformDetailUV (float2 detailUV) {
    float4 detailST = INPUT_PROP(_DetailMap_ST);
    return detailUV * detailST.xy + detailST.zw;
}

// me08: 采样 Detail Map，值域 0~1 转为 -1~1（0.5=中性=不影响任何属性）
float4 GetDetail (float2 detailUV) {
    #if defined(_DETAIL_MAP)
        float4 map = SAMPLE_TEXTURE2D(_DetailMap, sampler_DetailMap, detailUV);
        return map * 2.0 - 1.0;  // 0.5 → 0（中性），>0.5 → 正（变亮），<0.5 → 负（变暗）
    #else
        return 0.0;  // 没有 Detail Map = 中性 = 不影响
    #endif
}

// me08: 采样 MODS Mask 贴图，返回4通道 (R=Metallic, G=Occlusion, B=DetailMask, A=Smoothness)
// 如果未开 _MASK_MAP，直接返回 1（不影响任何属性）
float4 GetMask (float2 baseUV) {
    #if defined(_MASK_MAP)
        return SAMPLE_TEXTURE2D(_MaskMap, sampler_BaseMap, baseUV);
    #else
        return 1.0;
    #endif
}

// 获取基础颜色，加入细节调制
// me08: detailUV 默认=0（不传的地方不受影响）
float4 GetBase (float2 baseUV, float2 detailUV = 0.0) {
    float4 map   = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, baseUV);
    float4 color = INPUT_PROP(_BaseColor);
    #if defined(_DETAIL_MAP)
        // 细节只影响 RGB，不影响 Alpha
        float detail = GetDetail(detailUV).r * INPUT_PROP(_DetailAlbedo);
        float mask = GetMask(baseUV).b;  // B 通道 = 细节影响区域
        // 用 sqrt 在 gamma 空间做插值（让变亮/变暗视觉上均衡）
        map.rgb = lerp(sqrt(map.rgb), detail < 0.0 ? 0.0 : 1.0, abs(detail) * mask);
        map.rgb *= map.rgb;  // 平方还原 linear
    #endif
    return map * color;
}

// 获取 Alpha 剪裁阈值
float GetCutoff (float2 baseUV) {
    return INPUT_PROP(_Cutoff);
}

// 金属度 = 滑条值 × 遮罩 R 通道
float GetMetallic (float2 baseUV) {
    float metallic = INPUT_PROP(_Metallic);
    metallic *= GetMask(baseUV).r;  // me08: 乘以 R 通道
    return metallic;
}

// me08: 遮蔽 = lerp(遮罩G通道, 1, 强度滑条)
// 强度=0 → 完全忽略遮罩（occlusion=1，无遮蔽）
// 强度=1 → 完全使用遮罩（occlusion=遮罩G）
float GetOcclusion (float2 baseUV) {
    float strength = INPUT_PROP(_Occlusion);
    float occlusion = GetMask(baseUV).g;
    return lerp(occlusion, 1.0, strength);
}

// 光滑度 = (滑条值 × 遮罩A) ± 细节调制
float GetSmoothness (float2 baseUV, float2 detailUV = 0.0) {
    float smoothness = INPUT_PROP(_Smoothness);
    smoothness *= GetMask(baseUV).a;
    #if defined(_DETAIL_MAP)
        float detail = GetDetail(detailUV).b * INPUT_PROP(_DetailSmoothness);
        float mask = GetMask(baseUV).b;
        smoothness = lerp(smoothness, detail < 0.0 ? 0.0 : 1.0, abs(detail) * mask);
    #endif
    return smoothness;
}

float3 GetEmission (float2 baseUV) {
    float4 map   = SAMPLE_TEXTURE2D(_EmissionMap, sampler_BaseMap, baseUV);
    float4 color = INPUT_PROP(_EmissionColor);
    return map.rgb * color.rgb;
}

// me07: 菲涅耳强度滑条（1.0=开启菲涅耳，0.0=关闭）
float GetFresnel (float2 baseUV) {
    return INPUT_PROP(_Fresnel);
}

// me08: 采样法线贴图，返回切线空间法线
// detailUV 默认=0，不传则不应用细节法线
float3 GetNormalTS(float2 baseUV, float2 detailUV = 0.0) {
    float4 map = SAMPLE_TEXTURE2D(_NormalMap, sampler_BaseMap, baseUV);
    float scale = INPUT_PROP(_NormalScale);
    float3 normal = DecodeNormal(map, scale); // 解码法线贴图
    #if defined(_DETAIL_MAP)
        // 细节法线：用 Mask B 通道控制强度
        map = SAMPLE_TEXTURE2D(_DetailNormalMap, sampler_DetailMap, detailUV);
        scale = INPUT_PROP(_DetailNormalScale) * GetMask(baseUV).b;
        float3 detail = DecodeNormal(map, scale);
        // BlendNormalRNM：把细节法线绕基础法线旋转叠加（比直接相加更准确）
        normal = BlendNormalRNM(normal, detail);
    #endif
    return normal;
}

// me14: 获取最终 Alpha（根据 ZWrite 决定）
float GetFinalAlpha(float alpha) {
    // ZWrite=1（不透明物体）→ alpha强制为1，避免半透明深度写入干扰叠层
    // ZWrite=0（透明物体）→ 保留真实alpha
    return INPUT_PROP(_ZWrite) ? 1.0 : alpha;
}

// me15: InputConfig——把片元级别的配置集中管理
// LitPassFragment 通过它获取 Fragment（含深度/屏幕UV）
struct InputConfig {
    Fragment fragment;  // 屏幕坐标、自身深度、缓冲区深度
    float2 baseUV;
    float2 detailUV;
};

InputConfig GetInputConfig(float4 positionCS_SS, float2 baseUV, float2 detailUV = 0.0) {
    InputConfig c;
    c.fragment = GetFragment(positionCS_SS);  // 构建 Fragment（含深度信息）
    c.baseUV = baseUV;
    c.detailUV = detailUV;
    return c;
}

#endif
