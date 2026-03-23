// me11+12: 后处理栈 Shader
// Pass 顺序必须和 PostFXStack.cs 的 Pass 枚举完全一致！
// 枚举：BloomAdd=0 BloomHorizontal=1 BloomPrefilter=2 BloomPrefilterFireflies=3
//        BloomScatter=4 BloomScatterFinal=5 BloomVertical=6 Copy=7
//        ToneMappingACES=8 ToneMappingNeutral=9 ToneMappingReinhard=10
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

        // Pass 0: BloomAdd（加法叠加，原 BloomCombine）
        Pass {
            Name "Bloom Add"

            HLSLPROGRAM
                #pragma target 3.5
                #pragma vertex DefaultPassVertex
                #pragma fragment BloomAddPassFragment
            ENDHLSL
        }

        // Pass 1: BloomHorizontal（水平高斯模糊 + 降采样）
        Pass {
            Name "Bloom Horizontal"

            HLSLPROGRAM
                #pragma target 3.5
                #pragma vertex DefaultPassVertex
                #pragma fragment BloomHorizontalPassFragment
            ENDHLSL
        }

        // Pass 2: BloomPrefilter（亮度阈值预过滤）
        Pass {
            Name "Bloom Prefilter"

            HLSLPROGRAM
                #pragma target 3.5
                #pragma vertex DefaultPassVertex
                #pragma fragment BloomPrefilterPassFragment
            ENDHLSL
        }

        // Pass 3: BloomPrefilterFireflies（萤火虫消除预过滤）
        // me12: 5点对角采样 + 反亮度加权，稀释极亮孤立像素
        Pass {
            Name "Bloom Prefilter Fireflies"

            HLSLPROGRAM
                #pragma target 3.5
                #pragma vertex DefaultPassVertex
                #pragma fragment BloomPrefilterFirefliesPassFragment
            ENDHLSL
        }

        // Pass 4: BloomScatter（散射模式合并，lerp 代替加法，能量守恒）
        Pass {
            Name "Bloom Scatter"

            HLSLPROGRAM
                #pragma target 3.5
                #pragma vertex DefaultPassVertex
                #pragma fragment BloomScatterPassFragment
            ENDHLSL
        }

        // Pass 5: BloomScatterFinal（散射最终输出，补偿阈值截断的能量）
        Pass {
            Name "Bloom Scatter Final"

            HLSLPROGRAM
                #pragma target 3.5
                #pragma vertex DefaultPassVertex
                #pragma fragment BloomScatterFinalPassFragment
            ENDHLSL
        }

        // Pass 6: BloomVertical（竖直高斯模糊）
        Pass {
            Name "Bloom Vertical"

            HLSLPROGRAM
                #pragma target 3.5
                #pragma vertex DefaultPassVertex
                #pragma fragment BloomVerticalPassFragment
            ENDHLSL
        }

        // Pass 7: Copy（直接复制，ToneMapping=None 时用）
        Pass {
            Name "Copy"

            HLSLPROGRAM
                #pragma target 3.5
                #pragma vertex DefaultPassVertex
                #pragma fragment CopyPassFragment
            ENDHLSL
        }

        // Pass 8: ToneMappingACES（学院色彩标准，电影感强）
        Pass {
            Name "Tone Mapping ACES"

            HLSLPROGRAM
                #pragma target 3.5
                #pragma vertex DefaultPassVertex
                #pragma fragment ToneMappingACESPassFragment
            ENDHLSL
        }

        // Pass 9: ToneMappingNeutral（John Hable S形曲线，URP/HDRP同款）
        Pass {
            Name "Tone Mapping Neutral"

            HLSLPROGRAM
                #pragma target 3.5
                #pragma vertex DefaultPassVertex
                #pragma fragment ToneMappingNeutralPassFragment
            ENDHLSL
        }

        // Pass 10: ToneMappingReinhard（c/(c+1)，简单经典）
        Pass {
            Name "Tone Mapping Reinhard"

            HLSLPROGRAM
                #pragma target 3.5
                #pragma vertex DefaultPassVertex
                #pragma fragment ToneMappingReinhardPassFragment
            ENDHLSL
        }
    }
}
