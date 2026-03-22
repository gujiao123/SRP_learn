using UnityEngine;
using UnityEditor;

/// <summary>
/// 自定义 Light 的 Inspector，为聚光灯暴露内角（Inner Spot Angle）滑条
///
/// 为什么需要这个：
///   Unity 默认 Inspector 是为旧版 Built-in RP 设计的，不显示 Inner Spot Angle
///   但我们的 Shader 里读了 light.innerSpotAngle 来计算聚光灯的渐变区
///   没有这个 UI 用户就无法调节内角，内角永远是默认值（通常=0）
///
/// 原理：
///   [CustomEditorForRenderPipeline] 告诉 Unity：
///   "当使用 CustomRenderPipelineAsset 这个管线时，
///    Light 组件的 Inspector 用我这个类来画"
/// </summary>
[CanEditMultipleObjects]
[CustomEditorForRenderPipeline(typeof(Light), typeof(CustomRenderPipelineAsset))]
public class CustomLightEditor : LightEditor
{
    public override void OnInspectorGUI()
    {
        // 先画默认的 Inspector（Range、Color、Intensity 等）
        base.OnInspectorGUI();

        // 只在全部选中的灯都是聚光灯时，才加内外角联动滑条
        // hasMultipleDifferentValues = 选了多盏不同类型的灯
        if (!settings.lightType.hasMultipleDifferentValues &&
            (LightType)settings.lightType.enumValueIndex == LightType.Spot)
        {
            // 画内外角联动滑条（拖动时内角不会超过外角）
            settings.DrawInnerAndOuterSpotAngle();
            // 应用修改（不调用这个的话滑条拖了不会保存）
            settings.ApplyModifiedProperties();
        }
    }
}
