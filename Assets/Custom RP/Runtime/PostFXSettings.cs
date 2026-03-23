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
        // me12: None=-1 使得 ACES=0，Pass 枚举用 offset 计算：
        //   Pass = ToneMappingACES + (int)mode
        public enum Mode { None = -1, ACES, Neutral, Reinhard }
        public Mode mode;
    }

    [SerializeField]
    ToneMappingSettings toneMapping = default;

    public ToneMappingSettings ToneMapping => toneMapping;
}
