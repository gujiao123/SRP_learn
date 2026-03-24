using UnityEngine;

[DisallowMultipleComponent, RequireComponent(typeof(Camera))]
public class CustomRenderPipelineCamera : MonoBehaviour
{
    [SerializeField]
    CameraSettings settings = default;

    // 保证 settings 不为 null（比如刚添加组件还没序列化时）
    public CameraSettings Settings =>
        settings ?? (settings = new CameraSettings());
}
