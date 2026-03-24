// 1. Include Guard 开始
#ifndef CUSTOM_UNLIT_PASS_INCLUDED
#define CUSTOM_UNLIT_PASS_INCLUDED

// Common 和材质属性已由 Unlit.shader 的 HLSLINCLUDE 自动注入

struct Attributes {
    float3 positionOS : POSITION;
    float2 baseUV     : TEXCOORD0;  // me14: 加了UV，用于采样贴图
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings {
    float4 positionCS : SV_POSITION;
    float2 baseUV     : VAR_BASE_UV; // me14: 传给片元着色器
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

Varyings UnlitPassVertex (Attributes input) {
    Varyings output;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);

    float3 positionWS = TransformObjectToWorld(input.positionOS);
    output.positionCS = TransformWorldToHClip(positionWS);
    output.baseUV = TransformBaseUV(input.baseUV); // me14: 变换UV（含Tiling/Offset）
    return output;
}

float4 UnlitPassFragment (Varyings input) : SV_TARGET {
    UNITY_SETUP_INSTANCE_ID(input);

    // me14: 通过 GetBase 接口读取颜色（支持贴图采样 + 颜色叠乘）
    float4 base = GetBase(input.baseUV);

    // Clip 模式：alpha 低于阈值则丢弃片元
    #if defined(_CLIPPING)
        clip(base.a - GetCutoff(input.baseUV));
    #endif

    // me14: 用 GetFinalAlpha 确保不透明物体 alpha=1，透明物体保留真实 alpha
    return float4(base.rgb, GetFinalAlpha(base.a));
}

// 4. Include Guard 结束
#endif
