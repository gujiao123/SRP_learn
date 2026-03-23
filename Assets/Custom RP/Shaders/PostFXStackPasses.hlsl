// me11: 后处理栈的所有 HLSL Pass
// 包含: Copy, Bloom 水平/竖直/合并/预过滤

#ifndef CUSTOM_POST_FX_PASSES_INCLUDED
#define CUSTOM_POST_FX_PASSES_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Filtering.hlsl"
// me12: Color.hlsl 提供 Luminance, NeutralTonemap, AcesTonemap, unity_to_ACES
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

// me11: 源纹理（当前帧画面）和第二源（用于 Bloom 合并）
TEXTURE2D(_PostFXSource);
TEXTURE2D(_PostFXSource2);
SAMPLER(sampler_linear_clamp);

// me11: 自动获取纹理 texel 大小 (Unity 自动填充 _TexelSize 后缀)
// Unity 自动填充的！格式：(1/width, 1/height, width, height)
// 这是 Unity 的命名约定：变量名加 _TexelSize 后缀就自动填充
float4 _PostFXSource_TexelSize;

float4 GetSourceTexelSize () {
    return _PostFXSource_TexelSize;
}

// me11: 采样主源纹理（LOD0，避免 mipmap 问题）
float4 GetSource (float2 screenUV) {
    return SAMPLE_TEXTURE2D_LOD(
        _PostFXSource, sampler_linear_clamp, screenUV, 0
    );
}

// me11: 采样第二源纹理
float4 GetSource2 (float2 screenUV) {
    return SAMPLE_TEXTURE2D_LOD(
        _PostFXSource2, sampler_linear_clamp, screenUV, 0
    );
}

// me11: 双三次采样（更平滑的升采样，4次纹理查询模拟）
float4 GetSourceBicubic (float2 screenUV) {
    return SampleTexture2DBicubic(
        TEXTURE2D_ARGS(_PostFXSource, sampler_linear_clamp),
        screenUV, _PostFXSource_TexelSize.zwxy, 1.0, 0.0
    );
}

// me11: Bloom 阈值参数（CPU 端预计算后传入）
float4 _BloomThreshold;

// me11: 应用 Bloom 亮度阈值
// 膝盖曲线实现：让阈值附近的过渡更平滑（而非硬切）
float3 ApplyBloomThreshold (float3 color) {
    float brightness = Max3(color.r, color.g, color.b);
    float soft = brightness + _BloomThreshold.y;
    soft = clamp(soft, 0.0, _BloomThreshold.z);
    soft = soft * soft * _BloomThreshold.w;
    float contribution = max(soft, brightness - _BloomThreshold.x);
    contribution /= max(brightness, 0.00001);
    return color * contribution;
}

// --- 顶点着色器 ---

struct Varyings {
    float4 positionCS : SV_POSITION;  // 裁剪空间坐标（告诉GPU这个顶点在屏幕哪里）
    float2 screenUV : VAR_SCREEN_UV;  // UV坐标（传给 fragment shader 用来采样）
};

// me11: 程序化生成全屏三角形（不需要 Mesh 数据）
// 3个顶点的超大三角形覆盖屏幕，比传统的四边形（2个三角形）更高效
//   顶点0: (-1, -1)  UV(0, 0)
//   顶点1: (-1,  3)  UV(0, 2)
//   顶点2: ( 3, -1)  UV(2, 0)
// 超出 [-1,1] 范围的部分被 GPU 自动裁剪
Varyings DefaultPassVertex (uint vertexID : SV_VertexID) {
    Varyings output;
    output.positionCS = float4(
        vertexID <= 1 ? -1.0 : 3.0,
        vertexID == 1 ? 3.0 : -1.0,
        0.0, 1.0
    );
    output.screenUV = float2(
        vertexID <= 1 ? 0.0 : 2.0,
        vertexID == 1 ? 2.0 : 0.0
    );
    // me11: 某些平台 RT 的 V 坐标是反的，需要翻转
    if (_ProjectionParams.x < 0.0) {
        output.screenUV.y = 1.0 - output.screenUV.y;
    }
    return output;
}

// --- 片元着色器（各 Pass） ---

// me11: Bloom 水平模糊（9点高斯 + 降采样 2x）
// 水平方向采9个点，offset 乘2是因为同时做降采样
float4 BloomHorizontalPassFragment (Varyings input) : SV_TARGET {
    float3 color = 0.0;
    float offsets[] = {
        -4.0, -3.0, -2.0, -1.0, 0.0, 1.0, 2.0, 3.0, 4.0
    };
    float weights[] = {
        0.01621622, 0.05405405, 0.12162162, 0.19459459, 0.22702703,
        0.19459459, 0.12162162, 0.05405405, 0.01621622
    };
    for (int i = 0; i < 9; i++) {
        float offset = offsets[i] * 2.0 * GetSourceTexelSize().x;
        color += GetSource(input.screenUV + float2(offset, 0.0)).rgb
            * weights[i];
    }
    return float4(color, 1.0);
}

// me11: Bloom 竖直模糊（5点优化高斯，利用双线性过滤减少采样数）
// 不降采样（和水平pass配合完成完整高斯模糊）
float4 BloomVerticalPassFragment (Varyings input) : SV_TARGET {
    float3 color = 0.0;
    float offsets[] = {
        -3.23076923, -1.38461538, 0.0, 1.38461538, 3.23076923
    };
    float weights[] = {
        0.07027027, 0.31621622, 0.22702703, 0.31621622, 0.07027027
    };
    for (int i = 0; i < 5; i++) {
        float offset = offsets[i] * GetSourceTexelSize().y;
        color += GetSource(input.screenUV + float2(0.0, offset)).rgb
            * weights[i];
    }
    return float4(color, 1.0);
}

// me11->me12: Bloom 加法叠加（原 BloomCombine，改名 BloomAdd 区分散射 Pass）
bool _BloomBicubicUpsampling;
float _BloomIntensity;

float4 BloomAddPassFragment (Varyings input) : SV_TARGET {
    float3 lowRes;
    if (_BloomBicubicUpsampling) {
        lowRes = GetSourceBicubic(input.screenUV).rgb;
    }
    else {
        lowRes = GetSource(input.screenUV).rgb;
    }
    float3 highRes = GetSource2(input.screenUV).rgb;
    return float4(lowRes * _BloomIntensity + highRes, 1.0);
}

// me11: Bloom 预过滤（应用阈值 + 半分辨率降采样）
float4 BloomPrefilterPassFragment (Varyings input) : SV_TARGET {
    float3 color = ApplyBloomThreshold(GetSource(input.screenUV).rgb);
    return float4(color, 1.0);
}

// me12: 萤火虫消除预过滤
// 采样对角5点，按 weight=1/(luminance+1) 加权平均
// 极亮像素权重极低，被周围暗像素稀释，Bloom 光晕不再闪烁
// 读取原图写入半分辨率RT
float4 BloomPrefilterFirefliesPassFragment (Varyings input) : SV_TARGET {
    float3 color = 0.0;
    float weightSum = 0.0;
    float2 offsets[] = {
        float2( 0.0,  0.0),   // 中心
        float2(-1.0, -1.0),   // 左下 斜对角
        float2(-1.0,  1.0),   // 左上 斜对角
        float2( 1.0, -1.0),   // 右下 斜对角
        float2( 1.0,  1.0)    // 右上 斜对角
    };
    for (int i = 0; i < 5; i++) {
        float3 c = GetSource(
            //me12: GetSourceTexelSize这个是原图的 你想想 是不是该扩大一下 2倍 毕竟这里是读取原图的点对于原图采样
            input.screenUV + offsets[i] * GetSourceTexelSize().xy * 2.0
        ).rgb;
//         如果不先过滤：
//   暗区像素（亮度0.1）也参与加权，影响权重分布
//   但暗区像素本来就应该被阈值截掉，不参与Bloom
// 先过滤：
//   阈值以下的像素 c = 0 → Luminance(0) = 0 → w = 1/(0+1) = 1
//   但 c = 0，贡献的颜色也是 0
//   所以它们不影响最终颜色，只影响权重分母（变大 → 结果更小）
//   实际上是让输出更靠近0，进一步抑制了光晕
        c = ApplyBloomThreshold(c); // 先过滤阈值以下的颜色
        //这是"反亮度权重"，越亮权重越小：
        float w = 1.0 / (Luminance(c) + 1.0);
        color += c * w;
        weightSum += w;
    }
    color /= weightSum;
    return float4(color, 1.0);
}

// me12: Bloom 散射合并（能量守恒）lerp(highRes, lowRes, scatter)
float4 BloomScatterPassFragment (Varyings input) : SV_TARGET {
    float3 lowRes;
    if (_BloomBicubicUpsampling) {
        lowRes = GetSourceBicubic(input.screenUV).rgb;
    }
    else {
        lowRes = GetSource(input.screenUV).rgb;
    }
    float3 highRes = GetSource2(input.screenUV).rgb;
    return float4(lerp(highRes, lowRes, _BloomIntensity), 1.0);
}

// me12: Bloom 散射最终输出（补偿阈值截断损失的能量）
// lowRes += highRes - threshold(highRes)  把被截断的暗部直接加回来
float4 BloomScatterFinalPassFragment (Varyings input) : SV_TARGET {
    float3 lowRes;
    if (_BloomBicubicUpsampling) {
        lowRes = GetSourceBicubic(input.screenUV).rgb;
    }
    else {
        lowRes = GetSource(input.screenUV).rgb;
    }
    float3 highRes = GetSource2(input.screenUV).rgb;
    lowRes += highRes - ApplyBloomThreshold(highRes);
    return float4(lerp(highRes, lowRes, _BloomIntensity), 1.0);
}

// me11: 简单复制（ToneMapping=None 时用）
float4 CopyPassFragment (Varyings input) : SV_TARGET {
    return GetSource(input.screenUV);
}

// me12: ACES Tone Mapping — 转到 ACES 色彩空间后压缩，电影感强
float4 ToneMappingACESPassFragment (Varyings input) : SV_TARGET {
    float4 color = GetSource(input.screenUV);
    color.rgb = min(color.rgb, 60.0);
    color.rgb = AcesTonemap(unity_to_ACES(color.rgb));
    return color;
}

// me12: Neutral Tone Mapping — URP/HDRP 同款 S 形曲线，胶片感
float4 ToneMappingNeutralPassFragment (Varyings input) : SV_TARGET {
    float4 color = GetSource(input.screenUV);
    color.rgb = min(color.rgb, 60.0);
    color.rgb = NeutralTonemap(color.rgb);
    return color;
}

// me12: Reinhard Tone Mapping — c/(c+1)，暗处几乎不变，亮处渐压向1
float4 ToneMappingReinhardPassFragment (Varyings input) : SV_TARGET {
    float4 color = GetSource(input.screenUV);
    color.rgb = min(color.rgb, 60.0);
    color.rgb /= color.rgb + 1.0;
    return color;
}

#endif
