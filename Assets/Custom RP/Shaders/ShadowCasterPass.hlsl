#ifndef CUSTOM_SHADOW_CASTER_PASS_INCLUDED
#define CUSTOM_SHADOW_CASTER_PASS_INCLUDED

// Common 和材质属性已由 Lit.shader 的 HLSLINCLUDE 自动注入
// #include "../ShaderLibrary/Common.hlsl" ← 已广播，不再重复引用
// 材质变量 (TEXTURE2D, UNITY_INSTANCING_BUFFER) 已全部在 LitInput.hlsl 中定义
// 这里直接使用接口函数即可

// 输入结构 (只需要位置和 UV)
struct Attributes {
    float3 positionOS : POSITION;
    float2 baseUV : TEXCOORD0;
    //定义GPU Instancing使用的每个实例的ID，告诉GPU当前绘制的是哪个Object
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

// 输出结构 (只需要裁剪空间位置和 UV)
struct Varyings {
    float4 positionCS : SV_POSITION;
    float2 baseUV : VAR_BASE_UV;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};
// me10: 控制是否启用 Shadow Pancaking（C# 端通过 SetGlobalFloat 设置）
bool _ShadowPancaking;

Varyings ShadowCasterPassVertex (Attributes input) {



    Varyings output;
    //从input中提取实例的ID并将其存储在其他实例化宏所依赖的全局静态变量中
    UNITY_SETUP_INSTANCE_ID(input);
    //将实例ID传递给output
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    
    // 变换到世界空间
    float3 positionWS = TransformObjectToWorld(input.positionOS);
    // 变换到裁剪空间 (这里用的是光源的 VP 矩阵，不是摄像机的)
    output.positionCS = TransformWorldToHClip(positionWS);
    
    //!!这里的z就是 距离光源的距离 我们直接用它来做近平面裁剪
    // --- Shadow Pancaking (防止近平面裁剪) ---
    // 如果使用反向 Z 缓冲，Clamp 深度到近平面 
    //output.positionCS.w =1或0
    //UNITY_NEAR_CLIP_VALUE就是近平面距离 
    //就是防止 阴影摄像机中 一些物体 超过了近平面 我们直接压扁到近平面上
    // me10: 但是只在正交投影（方向光）时才启用！
    // 透视投影（点/聚光灯）时关闭，否则灯后的投射体会被错误压扁
    if (_ShadowPancaking) {
        #if UNITY_REVERSED_Z
            output.positionCS.z = min(
                output.positionCS.z, 
                output.positionCS.w * UNITY_NEAR_CLIP_VALUE
            );
        #else
            output.positionCS.z = max(
                output.positionCS.z, 
                output.positionCS.w * UNITY_NEAR_CLIP_VALUE
            );
        #endif
    }
    // ---------------------------------------
    
    // UV 变换：调用银行接口
    output.baseUV = TransformBaseUV(input.baseUV);
    
    return output;
}

// Fragment Shader：只负责 Alpha Clipping，不输出颜色
void ShadowCasterPassFragment (Varyings input) {
   //me07 ShadowCasterPassFragment 最开始  
    ClipLOD(input.positionCS.xy, unity_LODFade.x);

    //从input中提取实例的ID并将其存储在其他实例化宏所依赖的全局静态变量中
    UNITY_SETUP_INSTANCE_ID(input);
    
    // 采样纹理 + 颜色叠成，调用银行接口
    float4 base = GetBase(input.baseUV);
    
    // Alpha Clipping (如果开启的话)
    
    //抖动裁剪 好像跟自己定义阈值裁剪不同,可能根据TAA抖动后效果好把
    //抖动可以用来近似半透明的阴影投射器，但这是一种相当粗糙的方法。硬抖动阴影看起来很差，但如果用更大的 PCF 滤镜，效果可能还可以接受。
    #if defined(_SHADOWS_CLIP)
        clip(base.a - GetCutoff(input.baseUV));
    #elif defined(_SHADOWS_DITHER)

        float dither = InterleavedGradientNoise(input.positionCS.xy, 0);
        clip(base.a - dither);
    #endif
    // 注意：这个函数是 void，不返回任何颜色
    // GPU 会自动把深度写入深度缓冲
}

#endif