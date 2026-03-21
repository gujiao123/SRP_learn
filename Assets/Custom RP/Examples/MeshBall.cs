using UnityEngine;
using UnityEngine.Rendering; // 需要引入：ShadowCastingMode / LightProbeUsage

public class MeshBall : MonoBehaviour {

    // 材质属性 ID 缓存（提前查好，避免每帧字符串查找）
    static int baseColorId  = Shader.PropertyToID("_BaseColor");
    static int metallicId   = Shader.PropertyToID("_Metallic");
    static int smoothnessId = Shader.PropertyToID("_Smoothness");

    [SerializeField] Mesh mesh = default;
    [SerializeField] Material material = default;

    // 🆕 LPPV（Light Probe Proxy Volume）可选字段
    // 如果挂了 LPPV 组件，就用体积方式提供探针数据，否则用手动计算方式
    [SerializeField] LightProbeProxyVolume lightProbeVolume = null;

    // 1023 个实例的变换矩阵、颜色、金属度、光滑度
    Matrix4x4[] matrices    = new Matrix4x4[1023];
    Vector4[]   baseColors  = new Vector4[1023];
    float[]     metallic    = new float[1023];
    float[]     smoothness  = new float[1023];

    MaterialPropertyBlock block;

    void Awake () {
        // 初始化：随机生成 1023 个位置、颜色、材质属性
        for (int i = 0; i < matrices.Length; i++) {
            matrices[i] = Matrix4x4.TRS(
                Random.insideUnitSphere * 10f,
                Quaternion.identity,
                Vector3.one
            );
            baseColors[i] = new Vector4(
                Random.value, Random.value, Random.value, 
                Random.Range(0.5f, 1f)
            );
            metallic[i]   = Random.value < 0.25f ? 1f : 0f;
            smoothness[i] = Random.Range(0.05f, 0.95f);
        }
    }

    void Update () {
        if (block == null) {
            block = new MaterialPropertyBlock();
            block.SetVectorArray(baseColorId,  baseColors);
            block.SetFloatArray(metallicId,    metallic);
            block.SetFloatArray(smoothnessId,  smoothness);

            // =============================================
            // 🌟 手动为 1023 个实例查询光照探针数据
            // =============================================
            // 只有在没有使用 LPPV 的情况下才手动计算探针
            if (!lightProbeVolume) {
                // 步骤 1：从变换矩阵里提取每个实例的世界空间位置
                // 变换矩阵是一个 4×4 矩阵，第 4 列（index 3）= 世界坐标的 XYZ
                var positions = new Vector3[1023];
                for (int i = 0; i < matrices.Length; i++) {
                    positions[i] = matrices[i].GetColumn(3);
                }

                // 步骤 2：调用 Unity API，根据位置批量计算插值好的球谐系数
                // 球谐（Spherical Harmonics）= 把周围探针的光照信息压缩成一组系数
                // 第三个参数是遮蔽数据（occlusion），我们暂时不需要，传 null
                var lightProbes = new SphericalHarmonicsL2[1023];
                LightProbes.CalculateInterpolatedLightAndOcclusionProbes(
                    positions, lightProbes, null
                );

                // 步骤 3：把球谐数据批量写入 MaterialPropertyBlock
                // 这样每个实例就带着自己位置对应的 GI 数据发给 Shader 了！
                block.CopySHCoefficientArraysFrom(lightProbes);
            }
        }

        // =============================================
        // 🎨 提交 Draw Call
        // =============================================
        // 比原版多了 5 个参数，用来声明阴影和探针的使用方式
        Graphics.DrawMeshInstanced(
            mesh, 0, material, matrices, 1023, block,
            ShadowCastingMode.On,   // 投射阴影：开启
            true,                   // 接收阴影：开启
            0,                      // Layer：默认层
            null,                   // Camera：null = 对所有相机可见
            // 探针模式选择：
            // - 有 LPPV → 使用体积代理探针（不需要每实例单独计算）
            // - 没有 LPPV → 使用我们手动填入 block 的自定义探针数据
            lightProbeVolume ? LightProbeUsage.UseProxyVolume 
                             : LightProbeUsage.CustomProvided,
            lightProbeVolume        // 传入 LPPV 组件引用（没有就是 null）
        );
    }
}