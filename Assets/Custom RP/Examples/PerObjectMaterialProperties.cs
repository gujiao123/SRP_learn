using UnityEngine;


//专门用于 修改材质 或者行宅男过 也就是MPB 配合GPU实例化 而已减少 材质 形体的 存储占用

//用在需要改变材质的物体身上

//新增 BRP的支持
[DisallowMultipleComponent]
public class PerObjectMaterialProperties : MonoBehaviour {
    
    //!覆盖材质打断Block的使用
    //!并不改变渲染顺序还是不透明的话 先远后进 srp batch就看做一坨就行
    static int baseColorId = Shader.PropertyToID("_BaseColor"),
        cutoffId = Shader.PropertyToID("_Cutoff"), // 记得加上这个
        metallicId = Shader.PropertyToID("_Metallic"),
        smoothnessId = Shader.PropertyToID("_Smoothness");
    
    // 这是一个静态的 Block，所有物体共用一个实例来传输数据，节省内存
    static MaterialPropertyBlock block;
    
    [SerializeField] Color baseColor = Color.white;
    [SerializeField, Range(0f, 1f)] float cutoff = 0.5f;
    // --- 新增 PBR 字段 ---
    [SerializeField, Range(0f, 1f)] float metallic = 0f;
    [SerializeField, Range(0f, 1f)] float smoothness = 0.5f;
    [SerializeField]

    // OnValidate 会在编辑器里修改数值时实时触发
    void OnValidate () {
        if (block == null) {
            block = new MaterialPropertyBlock();
        }
        // 1. 设置颜色到 Block
        block.SetColor(baseColorId, baseColor);
        block.SetFloat(cutoffId, cutoff);
        // --- 设置 PBR ---
        block.SetFloat(metallicId, metallic);
        block.SetFloat(smoothnessId, smoothness);
        // ---------------
        // 2. 把 Block 应用到当前物体的 Renderer
        GetComponent<Renderer>().SetPropertyBlock(block);
    }
    
    // 此外，OnValidate 不会在打包的程序中调用。为了使球体各自的颜色显示出来，我们还必须将它们应用于 Awake，我们可以通过简单地在 Awake 中调用 OnValidate来解决问题。
    void Awake () {
        OnValidate();
    }
}


