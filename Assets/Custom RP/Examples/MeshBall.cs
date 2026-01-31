using UnityEngine;

public class MeshBall : MonoBehaviour {

    static int baseColorId = Shader.PropertyToID("_BaseColor");

    [SerializeField] Mesh mesh = default;
    [SerializeField] Material material = default;

    // 两个大数组，存变换矩阵和颜色
    Matrix4x4[] matrices = new Matrix4x4[1023];
    Vector4[] baseColors = new Vector4[1023];

    MaterialPropertyBlock block;

    void Awake () {
        // 初始化：随机生成 1023 个位置和颜色
        for (int i = 0; i < matrices.Length; i++) {
            matrices[i] = Matrix4x4.TRS(
                Random.insideUnitSphere * 10f, 
                Quaternion.identity, 
                Vector3.one
            );
            baseColors[i] = new Vector4(
                Random.value, Random.value, Random.value, 1f
            );
        }
    }

    void Update () {
        if (block == null) {
            block = new MaterialPropertyBlock();
            // 把颜色数组一次性塞进 Block
            block.SetVectorArray(baseColorId, baseColors);
        }

        // 核心 API：DrawMeshInstanced
        //  不需要创建 GameObject，直接命令 GPU 画 mesh
        Graphics.DrawMeshInstanced(
            mesh, 0, material, matrices, 1023, block
        );
    }
}