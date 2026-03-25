// me15: CameraRenderer 专属 Shader
// 用于两个全屏 Pass：
//   Pass 0 (Copy)      → 拷贝颜色（无后处理场景下把中间缓冲输出到屏幕）
//   Pass 1 (CopyDepth) → 拷贝深度（备用，当 GPU 不支持 CopyTexture 时用）
Shader "Hidden/Custom RP/Camera Renderer"
{
    SubShader
    {
        Cull Off
        ZTest Always
        ZWrite Off

        HLSLINCLUDE
        #include "../ShaderLibrary/Common.hlsl"
        #include "CameraRendererPasses.hlsl"
        ENDHLSL

        // Pass 0: Copy（拷贝颜色到目标 RT，支持 finalBlendMode）
        Pass
        {
            Name "Copy Color"

            // me15: 由 C# 端 DrawFinal() 动态设置混合模式（支持多相机叠加）
            Blend [_CameraSrcBlend] [_CameraDstBlend]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DefaultPassVertex
            #pragma fragment CopyPassFragment
            ENDHLSL
        }

        // Pass 1: CopyDepth（备用深度拷贝，需要写深度通道）
        Pass
        {
            Name "Copy Depth"

            ColorMask 0       // 不写颜色通道
            ZWrite On         // 写深度

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DefaultPassVertex
            #pragma fragment CopyDepthPassFragment
            ENDHLSL
        }
    }
}
