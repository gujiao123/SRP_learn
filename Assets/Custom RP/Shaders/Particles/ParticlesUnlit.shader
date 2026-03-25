// me15: 粒子专用 Unlit Shader
// 基于 Unlit.shader，专门针对粒子系统扩展：
//   - 顶点颜色（_VERTEX_COLORS）
//   - Flipbook 帧间混合（_FLIPBOOK_BLENDING）
//   - 近面渐隐（_NEAR_FADE）
//   - 软粒子（_SOFT_PARTICLES）
//   - 扭曲（_DISTORTION）
// 注意：粒子是纯动态的，不需要 Meta Pass（不参与烘焙）
Shader "Custom RP/Particles/Unlit" {

    Properties {
        _BaseMap("Texture", 2D) = "white" {}
        [HDR] _BaseColor("Color", Color) = (1.0, 1.0, 1.0, 1.0)
        _Cutoff ("Alpha Cutoff", Range(0.0, 1.0)) = 0.5

        // --- 粒子功能开关 ---
        // me15: 顶点颜色（粒子系统可以给每个粒子设置不同颜色）
        [Toggle(_VERTEX_COLORS)] _VertexColors ("Vertex Colors", Float) = 0

        // me15: Flipbook 帧间混合（平滑过渡动画帧，消除跳帧感）
        [Toggle(_FLIPBOOK_BLENDING)] _FlipbookBlending ("Flipbook Blending", Float) = 0

        // me15: 近面渐隐（接近相机时自动消隐，避免粒子"pop in"）
        [Toggle(_NEAR_FADE)] _NearFade ("Near Fade", Float) = 0
        _NearFadeDistance ("Near Fade Distance", Range(0.0, 10.0)) = 1
        _NearFadeRange ("Near Fade Range", Range(0.01, 10.0)) = 1

        // me15: 软粒子（与场景几何体相交时自动消隐，需要深度贴图）
        [Toggle(_SOFT_PARTICLES)] _SoftParticles ("Soft Particles", Float) = 0
        _SoftParticlesDistance ("Soft Particles Distance", Range(0.0, 10.0)) = 0
        _SoftParticlesRange ("Soft Particles Range", Range(0.01, 10.0)) = 1

        // me15: 扭曲（读取背后颜色 + UV 偏移，模拟热浪折射）
        [Toggle(_DISTORTION)] _Distortion ("Distortion", Float) = 0
        [NoScaleOffset] _DistortionMap("Distortion Vectors", 2D) = "bump" {}
        _DistortionStrength("Distortion Strength", Range(0.0, 0.2)) = 0.1
        _DistortionBlend("Distortion Blend", Range(0.0, 1.0)) = 1

        // --- 渲染状态 ---
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 0
        [Enum(Off, 0, On, 1)] _ZWrite ("Z Write", Float) = 1
    }

    SubShader {

        HLSLINCLUDE
        #include "../../ShaderLibrary/Common.hlsl"
        #include "ParticlesUnlitInput.hlsl"
        ENDHLSL

        Pass {
            Blend [_SrcBlend] [_DstBlend], One OneMinusSrcAlpha
            ZWrite [_ZWrite]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma multi_compile_instancing

            // me15: 粒子功能关键词
            #pragma shader_feature _VERTEX_COLORS
            #pragma shader_feature _FLIPBOOK_BLENDING
            #pragma shader_feature _NEAR_FADE
            #pragma shader_feature _SOFT_PARTICLES
            #pragma shader_feature _DISTORTION

            #pragma vertex UnlitPassVertex
            #pragma fragment UnlitPassFragment
            #include "ParticlesUnlitPass.hlsl"
            ENDHLSL
        }
    }

    CustomEditor "CustomShaderGUI"
}
