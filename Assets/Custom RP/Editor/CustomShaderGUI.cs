using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;


//用来美化控制shader的脚本

public class CustomShaderGUI : ShaderGUI {

    // 存储当前编辑的材质编辑器和属性数组
    MaterialEditor editor;
    Object[] materials;
    MaterialProperty[] properties;

    // 当 Unity 绘制材质面板时调用
    public override void OnGUI (MaterialEditor materialEditor, MaterialProperty[] properties) {
        // 先画出默认的面板 (颜色、贴图等)
        base.OnGUI(materialEditor, properties);
        
        // 初始化字段
        editor = materialEditor;
        materials = materialEditor.targets;
        this.properties = properties;
        
        // 画我们自定义的按钮
        EditorGUILayout.Space();
        DrawPresetButtons();
    }

    void DrawPresetButtons () {
        // 折叠页
        if (GUILayout.Button("Presets (预设)")) {
            // 这里我们简单点，直接画按钮，不用折叠逻辑了
        }
        
        // 横向排列按钮
        EditorGUILayout.BeginHorizontal();
        
        if (GUILayout.Button("Opaque")) {
            ApplyPreset(ClipState.Off, BlendMode.One, BlendMode.Zero, true, false, RenderQueue.Geometry);
        }
        if (GUILayout.Button("Clip")) {
            ApplyPreset(ClipState.On, BlendMode.One, BlendMode.Zero, true, false, RenderQueue.AlphaTest);
        }
        if (GUILayout.Button("Fade")) { // 标准半透明
            ApplyPreset(ClipState.Off, BlendMode.SrcAlpha, BlendMode.OneMinusSrcAlpha, false, false, RenderQueue.Transparent);
        }
        if (GUILayout.Button("Transparent")) { // 玻璃 (预乘Alpha)
            ApplyPreset(ClipState.Off, BlendMode.One, BlendMode.OneMinusSrcAlpha, false, true, RenderQueue.Transparent);
        }
        
        EditorGUILayout.EndHorizontal();
    }
    
    // 辅助枚举
    enum ClipState { Off, On }

    // 核心逻辑：一键设置所有属性
    void ApplyPreset (
        ClipState clip, 
        BlendMode src, BlendMode dst, 
        bool zWrite, 
        bool premulAlpha, 
        RenderQueue queue
    ) {
        // 1. 设置 Clipping
        SetKeyword("_CLIPPING", clip == ClipState.On);
        SetProperty("_Clipping", clip == ClipState.On ? 1f : 0f); // 假设你有 _Clipping 这个 float 属性
        
        // 2. 设置混合模式
        SetProperty("_SrcBlend", (float)src);
        SetProperty("_DstBlend", (float)dst);
        
        // 3. 设置深度写入
        SetProperty("_ZWrite", zWrite ? 1f : 0f);
        
        // 4. 设置预乘 Alpha
        SetKeyword("_PREMULTIPLY_ALPHA", premulAlpha);
        SetProperty("_PremulAlpha", premulAlpha ? 1f : 0f);
        
        // 5. 设置渲染队列
        foreach (Material m in materials) {
            m.renderQueue = (int)queue;
        }
    }

    // 辅助函数：设置属性值
    // 修改返回值类型为 bool，表示是否成功
    bool SetProperty (string name, float value) {
        // 关键点：FindProperty 的第三个参数 false 表示 "不是必须的"
        // 如果找不到，它会返回 null，而不是抛出异常
        MaterialProperty property = FindProperty(name, properties, false);
        if (property != null) {
            property.floatValue = value;
            return true;
        }
        return false;
    }

    // 辅助函数：设置 Keyword
    void SetKeyword (string keyword, bool enabled) {
        if (enabled) {
            foreach (Material m in materials) m.EnableKeyword(keyword);
        } else {
            foreach (Material m in materials) m.DisableKeyword(keyword);
        }
    }
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