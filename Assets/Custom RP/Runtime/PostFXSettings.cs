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
    }

    [SerializeField]
    BloomSettings bloom = default;

    public BloomSettings Bloom => bloom;
}
