#ifndef CUSTOM_PARTICLES_UNLIT_PASS_INCLUDED
#define CUSTOM_PARTICLES_UNLIT_PASS_INCLUDED

// me15: 粒子专用 UnlitPass
// 顶点结构体根据启用的功能条件编译，减少不必要的数据传输

//me15这些通过粒子系统传递过来的数据

struct Attributes {
    float3 positionOS : POSITION;
    float4 color      : COLOR;   // me15: 顶点颜色（粒子系统分配的每粒子颜色）
    // me15: Flipbook 开启时 TEXCOORD0 变成 float4：
    //   .xy = 当前帧 UV，.zw = 下一帧 UV
    // 未开启时只用 float2
    #if defined(_FLIPBOOK_BLENDING)
        float4 baseUV         : TEXCOORD0;
        float  flipbookBlend  : TEXCOORD1; // me15: 两帧之间的插值因子（0→1）
    #else
        float2 baseUV         : TEXCOORD0;
    #endif
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings {
    float4 positionCS_SS : SV_POSITION; // me15: 改名强调双重含义
    float4 color         : VAR_COLOR;   // me15: 始终传递（如不用顶点色，保持白色）
    float2 baseUV        : VAR_BASE_UV;
    #if defined(_FLIPBOOK_BLENDING)
        float3 flipbookUVB : VAR_FLIPBOOK; // me15: .xy=下帧UV，.z=blend因子
    #endif
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

Varyings UnlitPassVertex(Attributes input) {
    Varyings output;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);

    float3 positionWS = TransformObjectToWorld(input.positionOS);
    output.positionCS_SS = TransformWorldToHClip(positionWS);

    // me15: 顶点颜色直接透传（粒子系统已经算好了每粒子颜色）
    output.color = input.color;

    // me15: Flipbook 开启时，TEXCOORD0.xy = 当前帧，.zw = 下一帧
    output.baseUV = TransformBaseUV(input.baseUV.xy);
    #if defined(_FLIPBOOK_BLENDING)
        output.flipbookUVB.xy = TransformBaseUV(input.baseUV.zw);
        output.flipbookUVB.z  = input.flipbookBlend;
    #endif

    return output;
}

float4 UnlitPassFragment(Varyings input) : SV_TARGET {
    UNITY_SETUP_INSTANCE_ID(input);

    // me15: 构建 InputConfig（包含 Fragment 深度信息）
    InputConfig config = GetInputConfig(input.positionCS_SS, input.baseUV);

    // me15: 顶点颜色（只有开启 _VERTEX_COLORS 时才乘入）
    #if defined(_VERTEX_COLORS)
        config.color = input.color;
    #endif

    // me15: Flipbook 帧间混合
    #if defined(_FLIPBOOK_BLENDING)
        config.flipbookUVB    = input.flipbookUVB;
        config.flipbookBlending = true;
    #endif

    // me15: 近面渐隐（离相机越近 alpha 越低）
    #if defined(_NEAR_FADE)
        config.nearFade = true;
    #endif

    // me15: 软粒子（和场景几何体相交时 alpha 越低）
    #if defined(_SOFT_PARTICLES)
        config.softParticles = true;
    #endif

    // 获取最终颜色（含所有 alpha 修改）
    float4 base = GetBase(config);

    // Alpha Clipping
    #if defined(_CLIPPING)
        clip(base.a - GetCutoff(config));
    #endif

    // me15: 扭曲效果（读取背后的颜色缓冲，用法线图偏移 UV）
    #if defined(_DISTORTION)
        float2 distortion = GetDistortion(config);
        // 把背后颜色和粒子颜色按 DistortionBlend 混合
        // base.a 调节扭曲影响范围（alpha 低的地方扭曲也弱）
        base.rgb = lerp(
            GetBufferColor(config.fragment, distortion).rgb,// A = 扭曲背景色
            base.rgb,// B = 粒子原色
            saturate(base.a - GetDistortionBlend(config)) // t = 混合权重
        );
    #endif

    return float4(base.rgb, GetFinalAlpha(base.a));
}

#endif
