// me11: 后处理栈 Shader
// 包含 Bloom 和 Copy 的所有 Pass
// Pass 顺序必须和 PostFXStack.cs 的 Pass 枚举一一对应！
Shader "Hidden/Custom RP/Post FX Stack" {

    SubShader {
        // me11: 后处理不需要剔除和深度测试
        Cull Off
        ZTest Always
        ZWrite Off

        HLSLINCLUDE
        #include "../ShaderLibrary/Common.hlsl"
        #include "PostFXStackPasses.hlsl"
        ENDHLSL

        // Pass 0: BloomHorizontal
        Pass {
            Name "Bloom Horizontal"

            HLSLPROGRAM
                #pragma target 3.5
                #pragma vertex DefaultPassVertex
                #pragma fragment BloomHorizontalPassFragment
            ENDHLSL
        }

        // Pass 1: BloomVertical
        Pass {
            Name "Bloom Vertical"

            HLSLPROGRAM
                #pragma target 3.5
                #pragma vertex DefaultPassVertex
                #pragma fragment BloomVerticalPassFragment
            ENDHLSL
        }

        // Pass 2: BloomCombine
        Pass {
            Name "Bloom Combine"

            HLSLPROGRAM
                #pragma target 3.5
                #pragma vertex DefaultPassVertex
                #pragma fragment BloomCombinePassFragment
            ENDHLSL
        }

        // Pass 3: BloomPrefilter
        Pass {
            Name "Bloom Prefilter"

            HLSLPROGRAM
                #pragma target 3.5
                #pragma vertex DefaultPassVertex
                #pragma fragment BloomPrefilterPassFragment
            ENDHLSL
        }

        // Pass 4: Copy
        Pass {
            Name "Copy"

            HLSLPROGRAM
                #pragma target 3.5
                #pragma vertex DefaultPassVertex
                #pragma fragment CopyPassFragment
            ENDHLSL
        }
    }
}
