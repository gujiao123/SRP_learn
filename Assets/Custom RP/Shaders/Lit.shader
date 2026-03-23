Shader "Custom RP/Lit"{
    
    Properties {
        //在编辑器面板添加颜色选择器。
        // 变量名("显示名", 类型) = 默认值
        _BaseMap("Texture", 2D) = "white" {}
        _BaseColor("Color", Color) = (1.0, 1.0, 1.0, 1.0)
        // --- PBR 属性 ---
        // me08: Mask Map (MODS) - 一张贴图4通道控制4种属性
        // NoScaleOffset: 和 BaseMap 共用 Tiling/Offset，不单独设置
        [Toggle(_MASK_MAP)] _MaskMapToggle ("Mask Map", Float) = 0
        [NoScaleOffset] _MaskMap("Mask (MODS)", 2D) = "white" {}
        _Metallic ("Metallic", Range(0, 1)) = 0
        _Occlusion ("Occlusion", Range(0, 1)) = 1 // me08: 遮蔽强度（0=无遮蔽, 1=完整遮蔽）
        _Smoothness ("Smoothness", Range(0, 1)) = 0.5
        _Fresnel ("Fresnel", Range(0, 1)) = 1  // me07: 菲涅尔强度（1=完整反射边缘, 0=关闭）
        // --------------------
        //SrcBlend (源混合因子)：新画上去的颜色占多少比例？
        //DstBlend (目标混合因子)：屏幕上原本的颜色保留多少比例？
        //ZWrite (深度写入)：半透明物体通常不应该写入深度缓冲（否则会挡住后面的东西）。
        // [Enum] 特性让它在面板上显示为下拉菜单，而不是数字
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 0
        
         // --- 必须有这一行！ ---
        //根据透明度 裁剪
        _Cutoff ("Alpha Cutoff", Range(0.0, 1.0)) = 0.5
        // --------------------
        
        //!!这个并不是宏定义 ,而是 运行时的选择, 
        //!!#pragma shader_feature _CLIPPING 这个在编译时起作用 生成两个版本 而你 只是选择 一个罢了
        [Toggle(_CLIPPING)] _Clipping ("Alpha Clipping", Float) =0

         // 新增开关
        [Toggle(_PREMULTIPLY_ALPHA)] _PremulAlpha ("Premultiply Alpha", Float) = 0
        
        // ZWrite: 0 = Off, 1 = On
        
        [Enum(Off, 0, On, 1)] _ZWrite ("Z Write", Float) = 1
        // -----------------------
        
        
        // KeywordEnum 会生成一个下拉菜单
        [KeywordEnum(On, Clip, Dither, Off)] _Shadows ("Shadows", Float) = 0
        // Toggle 生成一个勾选框，控制是否接收阴影
        [Toggle(_RECEIVE_SHADOWS)] _ReceiveShadows ("Receive Shadows", Float) = 1
        
        // --- 自发光属性 ---
        // NoScaleOffset：Emission 贴图和 BaseMap 共用同一套 UV 变换，不需要单独的 Tiling/Offset 控制
        [NoScaleOffset] _EmissionMap("Emission", 2D) = "white" {}
        // HDR：允许颜色亮度超过 1（用于非常明亮的发光效果，比如霓虹灯/岩浆）
        [HDR] _EmissionColor("Emission", Color) = (0.0, 0.0, 0.0, 0.0)
        // me08: Detail Map - 高频细节叠加（默认 linearGrey，0.5=中性=不改变任何属性）
        [Toggle(_DETAIL_MAP)] _DetailMapToggle ("Detail Maps", Float) = 0
        _DetailMap("Details", 2D) = "linearGrey" {}  // 有自己的 Tiling 设置
        _DetailAlbedo("Detail Albedo", Range(0, 1)) = 1    // 细节对颜色的影响强度
        _DetailSmoothness("Detail Smoothness", Range(0, 1)) = 1 // 细节对光滑度的影响强度
        // me08: Normal Map（法线贴图）=让平面看起来有凹凸的光照欺骗
        [Toggle(_NORMAL_MAP)] _NormalMapToggle ("Normal Map", Float) = 0
        [NoScaleOffset] _NormalMap("Normals", 2D) = "bump" {}  // "bump" = 平坦法线默认值
        _NormalScale("Normal Scale", Range(0, 1)) = 1           // 法线强度（0=无效果, 1=完整）
        [NoScaleOffset] _DetailNormalMap("Detail Normals", 2D) = "bump" {}
        _DetailNormalScale("Detail Normal Scale", Range(0, 1)) = 1
        
        // --- 烘焙透明度兼容用的隐藏属性 ---
        // 因为光照烘焙器只认识 _MainTex 和 _Color，不认识我们自定义的 _BaseMap 和 _BaseColor
        // 所以我们加这两个隐藏马甲变量，在 CustomShaderGUI 里复制真实贴图和颜色进来
        [HideInInspector] _MainTex("Texture for Lightmap", 2D) = "white" {}
        [HideInInspector] _Color("Color for Lightmap", Color) = (0.5, 0.5, 0.5, 1.0)
        // ----------------------------------
        
        
    }
    
    SubShader {
        
        // 🔊 HLSLINCLUDE：广播大喇叭
        // 这个块里的内容会被自动插入到【本 SubShader 内所有 Pass】的 HLSL 代码头部
        // 不需要在每个 Pass 里重复 #include！
        HLSLINCLUDE
        #include "../ShaderLibrary/Common.hlsl"
        #include "LitInput.hlsl"
        ENDHLSL
        
        Pass {
             Tags {
            "LightMode" = "CustomLit" // ✅ 正确
            }
            
            // --- 应用渲染状态 ---
            // 这些指令在 HLSL 代码块之外！它们是 ShaderLab 的指令
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            // ------------------
            
            
            
            // 1. 定义 HLSL 代码块的开始
            HLSLPROGRAM
            // --- 新增指令 ---
            // 这会让 Unity 编译出支持 Instancing 的变体
            // 同时会在材质面板增加一个 "Enable GPU Instancing" 的勾选框
            //这一指令会让Unity生成两个该Shader的变体，一个支持GPU Instancing，另一个不支持。
            
            #pragma multi_compile_instancing
            //又编写三个不同的版本
            #pragma multi_compile _ _DIRECTIONAL_PCF3 _DIRECTIONAL_PCF5 _DIRECTIONAL_PCF7
            #pragma multi_compile _ _OTHER_PCF3 _OTHER_PCF5 _OTHER_PCF7
            //告诉Unity启用_CLIPPING关键字时编译不同版本的Shader
            #pragma shader_feature _CLIPPING
            //定义是否接收阴影关键字
            #pragma shader_feature _RECEIVE_SHADOWS
            //一个级联间软阴影和抽签阴影
			#pragma multi_compile _ _CASCADE_BLEND_SOFT _CASCADE_BLEND_DITHER
             // 新增 Feature 宏
            #pragma shader_feature _PREMULTIPLY_ALPHA

            // 2. 告诉编译器，顶点和片元着色器的入口函数名叫什么,类似main函数作为入口
            #pragma vertex LitPassVertex       
            #pragma fragment LitPassFragment
            
            //me05 警告编译器：分化去生成一个带光照贴图处理逻辑的 Shader 平行宇宙变体
            #pragma multi_compile _ LIGHTMAP_ON
            //me06 Shadow Mask 变体（让 _SHADOW_MASK_DISTANCE 关键词生效）
            #pragma multi_compile _ _SHADOW_MASK_ALWAYS _SHADOW_MASK_DISTANCE
            //me07 LOD 变体
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile _ _LIGHTS_PER_OBJECT // me09: 每物体灯光索引
            #pragma shader_feature _MASK_MAP    // me08: Mask Map 开关
            #pragma shader_feature _DETAIL_MAP  // me08: Detail Map 开关
            #pragma shader_feature _NORMAL_MAP  // me08: Normal Map 开关（只在主 Pass 需要）
            // 3. 核心逻辑不写在这里，而是引用外部文件
            // 注意：这一步会导致报错，因为文件还没创建，这是正常的
            #include "LitPass.hlsl"            // ✅ 引用 Lit 文件

            
            // 4. 结束 HLSL 代码块
            ENDHLSL
        }
        // Pass 2: 阴影投射 Pass (新增)
        Pass {
            // 与context.DrawShadows只渲染包含ShadowCaster Pass的材质 呼应
            
            Tags {
                "LightMode" = "ShadowCaster"
            }
        
            // 禁用颜色写入 (只写深度)
            ColorMask 0
            HLSLPROGRAM 
            //支持的最低平台
            #pragma target 3.5
            //阴影投射模式
            #pragma shader_feature _ _SHADOWS_CLIP _SHADOWS_DITHER
            #pragma multi_compile_instancing
            #pragma vertex ShadowCasterPassVertex
            #pragma fragment ShadowCasterPassFragment
            
            #pragma multi_compile _ LOD_FADE_CROSSFADE

            #include "ShadowCasterPass.hlsl"
            
            ENDHLSL
    }

        // Pass 3: Meta Pass — 烘変9大师的翻译官
        // 当点击 Generate Lighting 时，烘焙大师会通过这个通道来读取材质的真实色彩！
        //me05只有在光照烘焙时候生效提供对应的颜色
        Pass {
            Tags {
                "LightMode" = "Meta"
            }
            // 烘焙永远需要关闭背面剔除，否则模型背面会缺失数据
            Cull Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex MetaPassVertex
            #pragma fragment MetaPassFragment
            #include "MetaPass.hlsl"
            ENDHLSL
        }
    }

    //第三章 --- 指定自定义编辑器 ---
    CustomEditor "CustomShaderGUI"
    // ----------------------

}

/*好，我们暂停一下，彻底拆解 `_CLIPPING` 宏的整个生命周期。

这是一个涉及 **材质面板 -> C# -> Unity 编译器 -> GPU 运行时** 的完整数据流。

---

### 完整流程拓扑 (The Full Pipeline)

#### 阶段 1：Shader 编译时 (Compile Time)

当你保存 `Lit.shader` 时，Unity 会启动 Shader 编译器。

```hlsl
// Lit.shader
#pragma shader_feature _CLIPPING
```

**这一行的作用：**
*   告诉编译器："我这个 Shader 有两种可能性。"
    *   **变体 A**：不定义 `_CLIPPING` 宏（用户没勾选）。
    *   **变体 B**：定义 `_CLIPPING` 宏（用户勾选了）。

**编译器的行为：**
*   它会把你的 `LitPass.hlsl` 编译**两次**。
    *   第一次：预处理器设置 `#undef _CLIPPING`（删掉宏），然后编译。
        *   结果：`#if defined(_CLIPPING)` 判定为 false，`clip()` 代码**被删除**，不编译进去。
        *   生成：`Lit_Variant_0.bin`（伪代码，实际是 Unity 的内部格式）。
    *   第二次：预处理器设置 `#define _CLIPPING`（定义宏），然后编译。
        *   结果：`#if defined(_CLIPPING)` 判定为 true，`clip()` 代码**编译进去**。
        *   生成：`Lit_Variant_1.bin`。

**此时硬盘上（或内存里）存在两个不同的 GPU 程序。**

---

#### 阶段 2：材质面板交互时 (Editor Time)

当你在材质面板上勾选 `Alpha Clipping` 时：

```csharp
// 背后发生的逻辑 (Unity 内部或你的 CustomShaderGUI)
material.EnableKeyword("_CLIPPING");
```

**这一行的作用：**
*   它把字符串 `"_CLIPPING"` 添加到材质的 **Keywords (关键字列表)** 里。
*   这个列表会被序列化到材质文件（.mat）的 YAML 数据里。

**你可以验证**：
*   在 Project 窗口右键材质 -> **Open**（用文本编辑器打开）。
*   你会看到类似这样的代码：
    ```yaml
    m_ShaderKeywords: _CLIPPING INSTANCING_ON
    ```

---

#### 阶段 3：运行时选择变体 (Runtime)

游戏运行时，Unity 的渲染管线执行到 `DrawRenderers`。

**每一个 Draw Call 触发时**：
1.  Unity 检查这个材质的 `Keywords` 列表。
2.  如果列表里有 `_CLIPPING`：
    *   Unity 加载 `Lit_Variant_1.bin`（带 clip() 的版本）。
3.  如果列表里没有 `_CLIPPING`：
    *   Unity 加载 `Lit_Variant_0.bin`（不带 clip() 的版本）。
4.  把这段 GPU 代码发给显卡。

---

### 为什么需要这么复杂？

**性能优化。**

*   如果你有 100 个材质，只有 5 个需要 Clipping。
*   如果我们把 `clip()` 写死在代码里（不用宏）：
    *   那 95 个材质会浪费 GPU 指令去执行一个永远不会生效的 `clip()` 判断。
    *   在低端 GPU 上，这会导致帧率下降。

*   **有了变体系统**：
    *   95 个材质加载的 GPU 代码里，根本**不包含** `clip()` 指令（因为它们用的是 Variant_0）。
    *   5 个材质用的是 Variant_1，只有它们付出了 `clip()` 的性能代价。

---

### 总结流程图

```
1. 你写代码：#pragma shader_feature _CLIPPING
   ↓
2. Unity 编译：生成两个变体 (有 clip / 无 clip)
   ↓
3. 你勾选材质：material.EnableKeyword("_CLIPPING")
   ↓
4. 游戏运行：Unity 根据 Keyword 选择对应变体
   ↓
5. GPU 执行：执行带 clip() 的代码
```

**如果你跳过了第 1 步**（忘了写 `#pragma`），那么第 2 步不会生成 Variant_1。
即使第 3 步勾选了，第 4 步也找不到对应的变体，就会使用默认的 Variant_0（没有 clip）。

**这就是为什么你手动去掉 `#if` 就生效了**——因为你强制把 `clip()` 编译进了默认变体里。

现在理解了吗？*/