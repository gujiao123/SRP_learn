Shader "Custom RP/Unlit"{
    
    Properties {
        //在编辑器面板添加颜色选择器。
        // 变量名("显示名", 类型) = 默认值
        // [HDR]：允许颜色亮度超过 1，让 Unlit 材质能作为非常明亮的自发光使用
        _BaseMap("Texture", 2D) = "white" {} // 补充：让 Unlit 也能贴图
        [HDR] _BaseColor("Color", Color) = (1.0, 1.0, 1.0, 1.0)
        _Cutoff ("Alpha Cutoff", Range(0.0, 1.0)) = 0.5 // 补充：用于 Alpha Clipping
        
        // --- 新增渲染状态控制 ---
        [Toggle(_CLIPPING)] _Clipping ("Alpha Clipping", Float) = 0 // 补充：控制开启裁剪
        [KeywordEnum(On, Clip, Dither, Off)] _Shadows ("Shadows", Float) = 0 // 补充：设置阴影模式
        
        //SrcBlend (源混合因子)：新画上去的颜色占多少比例？
        //DstBlend (目标混合因子)：屏幕上原本的颜色保留多少比例？
        //ZWrite (深度写入)：半透明物体通常不应该写入深度缓冲（否则会挡住后面的东西）。
        // [Enum] 特性让它在面板上显示为下拉菜单，而不是数字
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 0
        // ZWrite: 0 = Off, 1 = On
        [Enum(Off, 0, On, 1)] _ZWrite ("Z Write", Float) = 1
        // -----------------------
    }
    
    SubShader {
        
        // 🔊 HLSLINCLUDE：广播大喇叭（与 Lit.shader 同款）
        // Common 和 UnlitInput 自动注入到所有 Pass
        HLSLINCLUDE
        #include "../ShaderLibrary/Common.hlsl"
        #include "UnlitInput.hlsl"
        ENDHLSL
        
        Pass {
            
            // --- 应用渲染状态 ---
            // 这些指令在 HLSL 代码块之外！它们是 ShaderLab 的指令
            Blend [_SrcBlend] [_DstBlend], One OneMinusSrcAlpha
            ZWrite [_ZWrite]
            // ------------------
            
            
            
            // 1. 定义 HLSL 代码块的开始
            HLSLPROGRAM
            // --- 新增指令 ---
            // 这会让 Unity 编译出支持 Instancing 的变体
            // 同时会在材质面板增加一个 "Enable GPU Instancing" 的勾选框
            #pragma multi_compile_instancing
            
            // 2. 告诉编译器，顶点和片元着色器的入口函数名叫什么,类似main函数作为入口
            #pragma vertex UnlitPassVertex       
            #pragma fragment UnlitPassFragment
            
            // 3. 核心逻辑不写在这里，而是引用外部文件
            // 注意：这一步会导致报错，因为文件还没创建，这是正常的
            #include "UnlitPass.hlsl"            // ✅ 引用 Lit 文件

            
            // 4. 结束 HLSL 代码块
            ENDHLSL
        }

        Pass {
            Tags {
                "LightMode" = "ShadowCaster"
            }
        
            ColorMask 0
            HLSLPROGRAM 
            #pragma target 3.5
            #pragma shader_feature _ _SHADOWS_CLIP _SHADOWS_DITHER
            #pragma multi_compile_instancing
            #pragma vertex ShadowCasterPassVertex
            #pragma fragment ShadowCasterPassFragment
            #include "ShadowCasterPass.hlsl"
            ENDHLSL
        }

        // Meta Pass — Unlit 版本
        // Unlit 的 GetEmission 就是 GetBase，让不受光材质也能把颜色贡献给烘焙
        Pass {
            Tags { "LightMode" = "Meta" }
            Cull Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex MetaPassVertex
            #pragma fragment MetaPassFragment
            #include "MetaPass.hlsl"
            ENDHLSL
        }
    }
}

