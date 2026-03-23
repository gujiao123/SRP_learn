// me11+12+13: 后处理栈 Shader
// Pass 顺序必须和 PostFXStack.cs 的 Pass 枚举完全一致！
// 枚举：BloomAdd=0 BloomHorizontal=1 BloomPrefilter=2 BloomPrefilterFireflies=3
//        BloomScatter=4 BloomScatterFinal=5 BloomVertical=6 Copy=7
//        ColorGradingNone=8 ColorGradingACES=9 ColorGradingNeutral=10 ColorGradingReinhard=11
//        Final=12
Shader "Hidden/Custom RP/Post FX Stack" {

    SubShader {
        Cull Off
        ZTest Always
        ZWrite Off

        HLSLINCLUDE
        #include "../ShaderLibrary/Common.hlsl"
        #include "PostFXStackPasses.hlsl"
        ENDHLSL

        // Pass 0: BloomAdd（加法叠加）
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
        Pass {
            Name "Bloom Prefilter Fireflies"
            HLSLPROGRAM
                #pragma target 3.5
                #pragma vertex DefaultPassVertex
                #pragma fragment BloomPrefilterFirefliesPassFragment
            ENDHLSL
        }

        // Pass 4: BloomScatter（散射模式合并，lerp 代替加法）
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

        // Pass 7: Copy（纯复制，无任何调整）
        Pass {
            Name "Copy"
            HLSLPROGRAM
                #pragma target 3.5
                #pragma vertex DefaultPassVertex
                #pragma fragment CopyPassFragment
            ENDHLSL
        }

        // Pass 8: ColorGradingNone（me13: 有ColorGrade，无ToneMap）
        Pass {
            Name "Color Grading None"
            HLSLPROGRAM
                #pragma target 3.5
                #pragma vertex DefaultPassVertex
                #pragma fragment ColorGradingNonePassFragment
            ENDHLSL
        }

        // Pass 9: ColorGradingACES（me13: ColorGrade + ACES ToneMap）
        Pass {
            Name "Color Grading ACES"
            HLSLPROGRAM
                #pragma target 3.5
                #pragma vertex DefaultPassVertex
                #pragma fragment ColorGradingACESPassFragment
            ENDHLSL
        }

        // Pass 10: ColorGradingNeutral（me13: ColorGrade + Neutral ToneMap）
        Pass {
            Name "Color Grading Neutral"
            HLSLPROGRAM
                #pragma target 3.5
                #pragma vertex DefaultPassVertex
                #pragma fragment ColorGradingNeutralPassFragment
            ENDHLSL
        }

        // Pass 11: ColorGradingReinhard（me13: ColorGrade + Reinhard ToneMap）
        Pass {
            Name "Color Grading Reinhard"
            HLSLPROGRAM
                #pragma target 3.5
                #pragma vertex DefaultPassVertex
                #pragma fragment ColorGradingReinhardPassFragment
            ENDHLSL
        }

        // Pass 12: Final（me13: 把 LUT 应用到原图并写屏幕）
        Pass {
            Name "Final"
            HLSLPROGRAM
                #pragma target 3.5
                #pragma vertex DefaultPassVertex
                #pragma fragment FinalPassFragment
            ENDHLSL
        }
    }
}
