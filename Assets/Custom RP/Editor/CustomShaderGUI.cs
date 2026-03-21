using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 自定义着色器界面类，用于在 Unity 材质面板提供更友好的交互和预设功能
/// </summary>
public class CustomShaderGUI : ShaderGUI {

    #region 字段定义 (Fields)

    MaterialEditor editor;      // 当前材质编辑器的引用
    Object[] materials;         // 当前选中的材质对象数组（支持批量编辑）
    MaterialProperty[] properties; // 材质的所有属性列表

    #endregion

    #region 枚举定义 (Enums)

    /// <summary> 阴影模式：对应 Shader 里的 _Shadows 属性 </summary>
    enum ShadowMode { On, Clip, Dither, Off }

    /// <summary> 裁剪状态：用于控制基础颜色的 Alpha 裁剪 </summary>
    enum ClipState { Off, On }

    #endregion

    #region 属性包装 (Properties)

    /// <summary>
    /// 阴影模式设置器：设置数字的同时，自动切换对应的 Shader 关键字 (Keyword)
    /// </summary>
    ShadowMode Shadows {
        set {
            // 设置材质上的 _Shadows 浮点值，如果设置成功则同步关键字
            if (SetProperty("_Shadows", (float)value)) {
                // 根据当前枚举值，开启或关闭对应的宏定义
                SetKeyword("_SHADOWS_CLIP", value == ShadowMode.Clip);
                SetKeyword("_SHADOWS_DITHER", value == ShadowMode.Dither);
            }
        }
    }

    #endregion

    #region 核心入口 (OnGUI)

    public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties) {
        // 1. 初始化引用
        this.editor = materialEditor;
        this.materials = materialEditor.targets;
        this.properties = properties;

        // 2. 开始检查界面是否有改动 (用于自动刷新 ShadowCaster Pass)
        EditorGUI.BeginChangeCheck();

        // 3. 绘制默认的属性界面 (纹理、颜色、滑动条等)
        base.OnGUI(materialEditor, properties);

        // ⭐ 新增：绘制自发光烘焙开关
        // 这个函数调用 editor.LightmapEmissionProperty()
        // 会在材质面板显示一个 'Global Illumination' 下拉框
        // 可以设置为 None / Baked / Realtime
        BakedEmission();

        // 4. 绘制自定义预设按钮
        EditorGUILayout.Space();
        DrawPresetButtons();

        // 5. 如果界面发生了改动，统一执行一次阴影通道的开关逻辑
        if (EditorGUI.EndChangeCheck()) {
            SetShadowCasterPass();
            // ⭐ 新增：同步光照贴图马甲属性
            // 确保 _MainTex 和 _Color（烘焙器认识的变量）
            // 始终和 _BaseMap 、_BaseColor 保持一致
            CopyLightMappingProperties();
        }
    }

    #endregion

    #region 界面绘制 (UI Drawing)

    /// <summary> 绘制一键预设按钮组 </summary>
    void DrawPresetButtons() {
        GUILayout.Label("Material Presets (材质预设)", EditorStyles.boldLabel);
        
        EditorGUILayout.BeginHorizontal();

        // --- Opaque: 不透明 ---
        if (GUILayout.Button("Opaque")) {
            ApplyPreset(ClipState.Off, BlendMode.One, BlendMode.Zero, true, false, ShadowMode.On, RenderQueue.Geometry);
        }

        // --- Clip: 镂空/裁剪 ---
        if (GUILayout.Button("Clip")) {
            ApplyPreset(ClipState.On, BlendMode.One, BlendMode.Zero, true, false, ShadowMode.Clip, RenderQueue.AlphaTest);
        }

        // --- Fade: 渐变半透明 ---
        if (GUILayout.Button("Fade")) {
            ApplyPreset(ClipState.Off, BlendMode.SrcAlpha, BlendMode.OneMinusSrcAlpha, false, false, ShadowMode.Dither, RenderQueue.Transparent);
        }

        // --- Transparent: 透明 (预乘Alpha) ---
        if (GUILayout.Button("Transparent")) {
            ApplyPreset(ClipState.Off, BlendMode.One, BlendMode.OneMinusSrcAlpha, false, true, ShadowMode.Dither, RenderQueue.Transparent);
        }

        EditorGUILayout.EndHorizontal();
    }

    #endregion

    #region 核心逻辑 (Business Logic)

    /// <summary>
    /// 应用材质预设：一键配置所有渲染状态、混合模式和阴影模式
    /// </summary>
    void ApplyPreset(
        ClipState clip, 
        BlendMode src, BlendMode dst, 
        bool zWrite, 
        bool premulAlpha, 
        ShadowMode shadowMode,
        RenderQueue queue
    ) {
        // 1. 设置 Alpha 裁剪
        SetKeyword("_CLIPPING", clip == ClipState.On);
        // 如果 Shader 中使用的是新的阴影关键字系统，建议主 Pass 也同步使用 _SHADOWS_CLIP
        SetProperty("_Clipping", clip == ClipState.On ? 1f : 0f); 
        
        // 2. 设置混合模式
        SetProperty("_SrcBlend", (float)src);
        SetProperty("_DstBlend", (float)dst);
        
        // 3. 设置深度写入
        SetProperty("_ZWrite", zWrite ? 1f : 0f);
        
        // 4. 设置预乘 Alpha (用于透明材质)
        SetKeyword("_PREMULTIPLY_ALPHA", premulAlpha);
        SetProperty("_PremulAlpha", premulAlpha ? 1f : 0f);

        // 5. 设置阴影模式 (调用属性包装器，自动处理关键字)
        Shadows = shadowMode;
        
        // 6. 设置渲染队列
        foreach (Material m in materials) {
            m.renderQueue = (int)queue;
        }
    }

    /// <summary>
    /// 关键功能：根据阴影模式自动开关 Shader 的 ShadowCaster Pass。
    /// 如果选了 Off，直接关掉该 Pass 以节省性能。
    /// </summary>
    void SetShadowCasterPass() {
        MaterialProperty shadows = FindProperty("_Shadows", properties, false);
        
        // 如果属性不存在，或者多个材质选了不同的模式（混合状态），则跳过
        if (shadows == null || shadows.hasMixedValue) return;

        // 如果 ShadowMode 小于 Off (即 On, Clip, Dither)，则启用阴影通道
        bool enabled = shadows.floatValue < (float)ShadowMode.Off;
        foreach (Material m in materials) {
            m.SetShaderPassEnabled("ShadowCaster", enabled);
        }
    }

    /// <summary>
    /// ⭐ 新增：在材质面板显示 Global Illumination 下拉框
    /// 让用户可以选择自发光是否参与烘焙（辐射给周围静态物体）
    /// </summary>
    void BakedEmission() {
        // 监听这个控件的改动
        EditorGUI.BeginChangeCheck();
        
        // 绘制内置的 Global Illumination 下拉框
        editor.LightmapEmissionProperty();
        
        if (EditorGUI.EndChangeCheck()) {
            // 当用户修改了 GI 设置（比如切换到 Baked）
            foreach (Material m in editor.targets) {
                // Unity 会马上点亮自发光的材质为 EmissiveIsBlack（误当黑色）
                // 这会导致烘焙器跳过这个材质的自发光烘焙轮次！
                // 用位运算 &= ~xxx ，昨掉这个错误标记，强制烘焙器认真处理用户设定的发光颜色
                m.globalIlluminationFlags &= ~MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            }
        }
    }

    /// <summary>
    /// ⭐ 新增：同步烘焙透明度马甲属性
    /// 将真实贴图和颜色复制到烘焙器能识别的马甲变量
    /// </summary>
    void CopyLightMappingProperties() {
        // 尝试找 _MainTex 和 _BaseMap 属性
        MaterialProperty mainTex  = FindProperty("_MainTex",  properties, false);
        MaterialProperty baseMap  = FindProperty("_BaseMap",  properties, false);
        if (mainTex != null && baseMap != null) {
            // 向马甲变量同步贴图内容和 UV 变换
            mainTex.textureValue          = baseMap.textureValue;
            mainTex.textureScaleAndOffset = baseMap.textureScaleAndOffset;
        }

        // 尝试找 _Color 和 _BaseColor 属性
        MaterialProperty color     = FindProperty("_Color",      properties, false);
        MaterialProperty baseColor = FindProperty("_BaseColor",  properties, false);
        if (color != null && baseColor != null) {
            // 向马甲变量同步颜色值
            color.colorValue = baseColor.colorValue;
        }
    }

    #endregion

    #region 基础辅助 (Helpers)

    /// <summary> 安全设置属性值：如果属性不存在，返回 false 而非报错 </summary>
    bool SetProperty(string name, float value) {
        MaterialProperty property = FindProperty(name, properties, false);
        if (property != null) {
            property.floatValue = value;
            return true;
        }
        return false;
    }

    /// <summary> 为所有当前编辑的材质 开启/关闭 指定关键字 </summary>
    void SetKeyword(string keyword, bool enabled) {
        if (enabled) {
            foreach (Material m in materials) ((Material)m).EnableKeyword(keyword);
        } else {
            foreach (Material m in materials) ((Material)m).DisableKeyword(keyword);
        }
    }

    #endregion
}
/*### 1. Opaque (不透明) —— 实心砖头
这是最基础的模式，用于石头、金属、皮肤等。

*   **现实物理**：光线要么被反射，要么被吸收，绝不会穿透过去。
*   **属性配置**：
    *   **_Clipping (Off)**: 不需要挖洞。
    *   **_PremulAlpha (Off)**: 不需要处理透明度。
    *   **Blend (One, Zero)**:
        *   `Src = One`: 新画的像素完全保留。
        *   `Dst = Zero`: 屏幕上原本的像素完全丢弃。
        *   **结果**：直接覆盖。
    *   **ZWrite (On)**: **开启深度写入**。因为它是实心的，它必须告诉深度缓冲：“我在这里，后面的东西别画了。”
    *   **Render Queue**: `Geometry` (2000)。最先渲染。

### 2. Clip (镂空) —— 铁丝网、树叶
这是“硬”透明。它没有半透明（0.5），只有“有”和“无”。

*   **现实物理**：一张纸上剪了几个洞。
*   **属性配置**：
    *   **_Clipping (On)**: **开启！**
        *   **作用**：Shader 会读取 `_Cutoff` 阈值。如果 Alpha < Cutoff，直接丢弃像素 (`discard`)。
        *   **注意**：你必须在 `Lit.shader` 的 `Properties` 里加上 `[Toggle(_CLIPPING)] _Clipping ...`，否则 C# 找不到它就会报错。
    *   **_PremulAlpha (Off)**: 剩下的部分是实心的，不需要半透明处理。
    *   **Blend (One, Zero)**: 同 Opaque。
    *   **ZWrite (On)**: 同 Opaque。保留下来的像素依然要挡住后面的东西。
    *   **Render Queue**: `AlphaTest` (2450)。在不透明物体之后渲染。

### 3. Fade (传统半透明) —— 幽灵、全息投影
这是“软”透明。整个物体均匀变淡。

*   **现实物理**：像一个虚幻的幽灵，或者淡入淡出的 UI。
*   **缺陷**：如果你用它画玻璃，**高光也会变淡**。Alpha 越低，高光越暗，最后什么都看不见。
*   **属性配置**：
    *   **_Clipping (Off)**: 不需要硬挖洞。
    *   **_PremulAlpha (Off)**: **关闭**。使用传统的 Alpha 混合公式。
    *   **Blend (SrcAlpha, OneMinusSrcAlpha)**:
        *   `Src * Alpha`: 颜色乘以透明度。
        *   `Dst * (1-Alpha)`: 背景乘以剩余比例。
    *   **ZWrite (Off)**: **关闭深度写入**。
        *   **原因**：透明物体不能挡住后面的东西。如果开了 ZWrite，你先画了前面的玻璃，后面的玻璃就画不上去了（被深度测试剔除了）。
    *   **Render Queue**: `Transparent` (3000)。最后渲染，且必须从后往前画。

### 4. Transparent (物理半透明) —— 玻璃、水
这是 PBR 时代的透明。

*   **现实物理**：玻璃本身是透明的（Diffuse 几乎为 0），但玻璃表面的反光（Specular）是 100% 强度的。
*   **属性配置**：
    *   **_Clipping (Off)**: 关。
    *   **_PremulAlpha (On)**: **开启！**
        *   **作用**：在 Shader 内部，我们只把 Alpha 乘到了 Diffuse 上，Specular 保持原样。
        *   **结果**：`Color = (Diffuse * Alpha) + Specular`。
    *   **Blend (One, OneMinusSrcAlpha)**:
        *   `Src = One`: **注意这里！** 因为 Shader 算出来的 Color 已经包含 Alpha 的预乘逻辑了，所以混合时不需要再乘一次 Alpha。
        *   `Dst = OneMinusSrcAlpha`: 背景依然要变暗。
    *   **ZWrite (Off)**: 同 Fade。
    *   **Render Queue**: `Transparent` (3000)。*/