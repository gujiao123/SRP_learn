using System;
using UnityEngine;

// me11: 后处理设置资源（ScriptableObject）
// 存储 Bloom 等后处理效果的配置参数
[CreateAssetMenu(menuName = "Rendering/Custom Post FX Settings")]
public class PostFXSettings : ScriptableObject
{

    [SerializeField]
    Shader shader = default;

    // me11: 按需创建 Material（不序列化，运行时生成）
    [System.NonSerialized]
    Material material;

    public Material Material
    {
        get
        {
            if (material == null && shader != null)
            {
                material = new Material(shader);
                material.hideFlags = HideFlags.HideAndDontSave;
            }
            return material;
        }
    }

    // me11: Bloom 效果配置
    [System.Serializable]
    public struct BloomSettings
    {
        [Range(0f, 16f)]
        public int maxIterations;       // 最大降采样次数

        [Min(1f)]
        public int downscaleLimit;      // 最小缩放到多少像素就停止 我靠 一般都是16

        public bool bicubicUpsampling;  // 升采样时用双三次插值（更平滑）   

        // me16: 开启后 Bloom 的起始尺寸以相机原始分辨率为准，不受 renderScale 影响
        // 好处：renderScale 动态变化时 Bloom 效果更稳定一致
        public bool ignoreRenderScale;
        [Min(0f)]
        public float threshold;         // 亮度阈值（gamma空间，低于此值不参与Bloom）

        [Range(0f, 1f)]
        public float thresholdKnee;     // 阈值膝盖曲线（0=硬切，1=最软过渡）

        [Min(0f)]
        public float intensity;         // Bloom 整体强度

        // me12: 萤火虫消除（HDR下极亮孤立像素会产生闪烁光晕）
        public bool fadeFireflies;

        // me12: Bloom 模式
        // Additive  = 叠加光（经典Bloom，能量增加）
        // Scattering = 散射光（模拟相机镜头散射，能量守恒）
        // 在 高频原图 和 低频光晕 之间插值 散射 就是配合控制hdr的光晕的
        // scatter=0 → 完全用 highRes（原图，没有光晕效果）
        // scatter=1 → 完全用 lowRes（全模糊，没有原图细节）
        // scatter=0.7 → 7成光晕 + 3成原图（光从原图"散射"出去）

        // Additive：给图加一层光晕，总亮度增加（超出显示范围就爆了）
        // Scattering：把原图的光"移"到周围，原来亮的地方变暗一点，周围变亮
        // 总亮度不变（能量守恒），像相机镜头的真实散射行为
        public enum Mode { Additive, Scattering }
        public Mode mode;

        // me12: 散射强度（仅 Scattering 模式有效）
        // 0=只用最小层，1=只用最大层，通常 0.7 左右
        [Range(0.05f, 0.95f)]
        public float scatter;
    }

    [SerializeField]
    // me12: scatter 默认值 0.7（和 URP/HDRP 一致）
    BloomSettings bloom = new BloomSettings { scatter = 0.7f };

    public BloomSettings Bloom => bloom;

    // me12: Tone Mapping 配置（放在 BloomSettings 外面，是独立效果）
    [System.Serializable]
    public struct ToneMappingSettings
    {
        // me13: None 改为从0开始（有了自己的 ColorGradingNone Pass，不再用 Copy 代替）
        // Pass = ColorGradingNone + (int)mode
        //   None=0 → ColorGradingNone（有ColorGrade，无ToneMap）
        //   ACES=1 → ColorGradingACES
        //   Neutral=2 → ColorGradingNeutral
        //   Reinhard=3 → ColorGradingReinhard
        public enum Mode { None, ACES, Neutral, Reinhard }
        public Mode mode;
    }

    [SerializeField]
    ToneMappingSettings toneMapping = default;

    public ToneMappingSettings ToneMapping => toneMapping;

    // me13: 颜色调整（对应 URP/HDRP 的 Color Adjustments 工具）
    [Serializable]
    public struct ColorAdjustmentsSettings
    {
        public float postExposure;        // 曝光（stop单位，2^x 传给Shader）

        [Range(-100f, 100f)]
        public float contrast;            // 对比度（在LogC空间操作）

        [ColorUsage(false, true)]         // HDR颜色，无alpha
        public Color colorFilter;         // 颜色滤镜（直接乘）

        [Range(-180f, 180f)]
        public float hueShift;            // 色相旋转（RGB→HSV→H偏移→RGB）

        [Range(-100f, 100f)]
        public float saturation;          // 饱和度
    }

    [SerializeField]
    ColorAdjustmentsSettings colorAdjustments = new ColorAdjustmentsSettings
    {
        colorFilter = Color.white  // 默认白色滤镜 = 不改变颜色
    };

    public ColorAdjustmentsSettings ColorAdjustments => colorAdjustments;

    // me13: 白平衡（在 LMS 颜色空间调整，模拟人眼感知）
    [Serializable]
    public struct WhiteBalanceSettings
    {
        [Range(-100f, 100f)]
        public float temperature;  // 色温（负=冷蓝，正=暖黄）

        [Range(-100f, 100f)]
        public float tint;         // 色彩偏移（负=偏绿，正=偏洋红）
    }

    [SerializeField]
    WhiteBalanceSettings whiteBalance = default;

    public WhiteBalanceSettings WhiteBalance => whiteBalance;

    // me13: 分离映射（暗部/亮部分别染色，经典电影色调：暗蓝亮橙）
    [Serializable]
    public struct SplitToningSettings
    {
        [ColorUsage(false)]  // LDR颜色，无alpha
        public Color shadows;    // 暗部色调

        [ColorUsage(false)]
        public Color highlights; // 亮部色调

        [Range(-100f, 100f)]
        public float balance;    // 平衡点（负=暗部范围更大，正=亮部范围更大）
    }

    [SerializeField]
    SplitToningSettings splitToning = new SplitToningSettings
    {
        shadows = Color.gray,     // 默认灰色 = 不染色
        highlights = Color.gray
    };

    public SplitToningSettings SplitToning => splitToning;

    // me13: 通道混合（3×3矩阵，R/G/B输入自由组合成新RGB）
    [Serializable]
    public struct ChannelMixerSettings
    {
        public Vector3 red;    // 新R = dot(原RGB, red)
        public Vector3 green;  // 新G = dot(原RGB, green)
        public Vector3 blue;   // 新B = dot(原RGB, blue)
    }

    [SerializeField]
    ChannelMixerSettings channelMixer = new ChannelMixerSettings
    {
        red = Vector3.right,   // (1,0,0) 默认单位矩阵
        green = Vector3.up,      // (0,1,0)
        blue = Vector3.forward  // (0,0,1)
    };

    public ChannelMixerSettings ChannelMixer => channelMixer;

    // me13: 阴影/中间调/高光（按亮度分区域染色，可配置区域边界）
    [Serializable]
    public struct ShadowsMidtonesHighlightsSettings
    {
        [ColorUsage(false, true)]
        public Color shadows;    // 暗部颜色（白=不变）

        [ColorUsage(false, true)]
        public Color midtones;   // 中间调颜色

        [ColorUsage(false, true)]
        public Color highlights; // 亮部颜色

        [Range(0f, 2f)] public float shadowsStart;    // 暗部区域起点
        [Range(0f, 2f)] public float shadowsEnd;      // 暗部区域终点
        [Range(0f, 2f)] public float highlightsStart; // 亮部区域起点
        [Range(0f, 2f)] public float highLightsEnd;   // 亮部区域终点
    }

    [SerializeField]
    ShadowsMidtonesHighlightsSettings shadowsMidtonesHighlights =
        new ShadowsMidtonesHighlightsSettings
        {
            shadows = Color.white,
            midtones = Color.white,
            highlights = Color.white,
            shadowsEnd = 0.3f,   // 和 Unity 默认值一致
            highlightsStart = 0.55f,
            highLightsEnd = 1f
        };

    public ShadowsMidtonesHighlightsSettings ShadowsMidtonesHighlights =>
        shadowsMidtonesHighlights;
}
