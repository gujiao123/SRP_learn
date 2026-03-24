using Unity.Collections; // 需要用到 NativeArray
using UnityEngine;
using UnityEngine.Rendering;

public class Lighting
{

    const string bufferName = "Lighting";

    // 命令缓冲区，用于发送数据
    CommandBuffer buffer = new CommandBuffer
    {
        name = bufferName
    };

    // me09: 方向光最多4，其他光最多64
    const int maxDirLightCount = 4, maxOtherLightCount = 64;

    // Shader 属性 ID
    static int
        dirLightCountId = Shader.PropertyToID("_DirectionalLightCount"),
        dirLightColorsId = Shader.PropertyToID("_DirectionalLightColors"),
        dirLightDirectionsAndMasksId = Shader.PropertyToID("_DirectionalLightDirectionsAndMasks"),
        dirLightShadowDataId = Shader.PropertyToID("_DirectionalLightShadowData"),
        // me09: 点光源/聚光灯的属性
        otherLightCountId = Shader.PropertyToID("_OtherLightCount"),
        otherLightColorsId = Shader.PropertyToID("_OtherLightColors"),
        otherLightPositionsId = Shader.PropertyToID("_OtherLightPositions"),
        otherLightDirectionsAndMasksId = Shader.PropertyToID("_OtherLightDirectionsAndMasks"),
        otherLightSpotAnglesId = Shader.PropertyToID("_OtherLightSpotAngles"),
        otherLightShadowDataId = Shader.PropertyToID("_OtherLightShadowData");

    static Vector4[]
        dirLightColors = new Vector4[maxDirLightCount],
        dirLightDirectionsAndMasks = new Vector4[maxDirLightCount],
        dirLightShadowData = new Vector4[maxDirLightCount],//存放剔除结果
                                                           // me09: Other Lights 缓存
        otherLightColors = new Vector4[maxOtherLightCount],
        otherLightPositions = new Vector4[maxOtherLightCount],   // xyz=位置, w=1/r²
        otherLightDirectionsAndMasks = new Vector4[maxOtherLightCount],   // 聚光灯方向
        otherLightSpotAngles = new Vector4[maxOtherLightCount],   // x=a, y=b 角度衰减参数
        otherLightShadowData = new Vector4[maxOtherLightCount];   // 烘焙阴影数据

    CullingResults cullingResults;
    Shadows shadows = new Shadows();

    // me09: 每物体灯光关键字
    static string lightsPerObjectKeyword = "_LIGHTS_PER_OBJECT";

    // --- 公共接口 ---
    public void Setup(
        ScriptableRenderContext context,
        CullingResults cullingResults,
        ShadowSettings shadowSettings,
        bool useLightsPerObject = false,  // me09: 每物体灯光开关
        int renderingLayerMask = -1       // me14: 相机的渲染层遮罩，-1=不过滤
    )
    {
        this.cullingResults = cullingResults;
        buffer.BeginSample(bufferName);
        shadows.Setup(context, cullingResults, shadowSettings);
        SetupLights(useLightsPerObject, renderingLayerMask);
        shadows.Render();
        buffer.EndSample(bufferName);
        context.ExecuteCommandBuffer(buffer);
        buffer.Clear();
    }

    // --- 配置方向光 ---
    // me10: 加 visibleIndex 参数，修复传错索引的 Bug
    // visibleIndex = i（遍历 visibleLights 时的真实索引）
    // index = dirLightCount（我们自己的方向光计数器）
    // 两者当场景有多种灯光类型时会不同！必须传 visibleIndex 给 ReserveDirectionalShadows
    void SetupDirectionalLight(int index, int visibleIndex, ref VisibleLight visibleLight, Light light)
    {
        dirLightColors[index] = visibleLight.finalColor;
        Vector4 dirAndMask = -visibleLight.localToWorldMatrix.GetColumn(2);
        dirAndMask.w = light.renderingLayerMask.ReinterpretAsFloat(); // me14
        dirLightDirectionsAndMasks[index] = dirAndMask;
        dirLightShadowData[index] = shadows.ReserveDirectionalShadows(light, visibleIndex);
    }

    // me09: 配置点光源
    // me10: 加 visibleIndex 参数
    // me14: 加 Light 参数，传入渲染层掩码（点光源无方向，.xyz=0，.w=mask）
    void SetupPointLight(int index, int visibleIndex, ref VisibleLight visibleLight, Light light)
    {
        otherLightColors[index] = visibleLight.finalColor;
        Vector4 position = visibleLight.localToWorldMatrix.GetColumn(3);
        position.w = 1f / Mathf.Max(visibleLight.range * visibleLight.range, 0.00001f);
        otherLightPositions[index] = position;
        // 点光源无方向（xyz=0），只用 .w 存渲染层掩码
        Vector4 dirAndMask = Vector4.zero;
        dirAndMask.w = light.renderingLayerMask.ReinterpretAsFloat(); // me14: 位掩码原样传给 GPU
        otherLightDirectionsAndMasks[index] = dirAndMask;
        // 点光源没有角度限制：a=0, b=1 → dot*0+1=1，不衰减
        otherLightSpotAngles[index] = new Vector4(0f, 1f);
        otherLightShadowData[index] = shadows.ReserveOtherShadows(light, visibleIndex);
    }

    // me09: 配置聚光灯
    // me10: 加 visibleIndex 参数
    // me14: 加 Light 参数，传入渲染层掩码（存在 DirectionsAndMasks .w 里）
    void SetupSpotLight(int index, int visibleIndex, ref VisibleLight visibleLight, Light light)
    {
        otherLightColors[index] = visibleLight.finalColor;
        Vector4 position = visibleLight.localToWorldMatrix.GetColumn(3);
        position.w = 1f / Mathf.Max(visibleLight.range * visibleLight.range, 0.00001f);
        otherLightPositions[index] = position;
        // 聚光灯方向存 xyz，渲染层掩码存 .w
        Vector4 dirAndMask = -visibleLight.localToWorldMatrix.GetColumn(2);
        dirAndMask.w = light.renderingLayerMask.ReinterpretAsFloat(); // me14: 位掩码原样传给 GPU
        otherLightDirectionsAndMasks[index] = dirAndMask;
        float innerCos = Mathf.Cos(Mathf.Deg2Rad * 0.5f * light.innerSpotAngle);
        float outerCos = Mathf.Cos(Mathf.Deg2Rad * 0.5f * visibleLight.spotAngle);
        float angleRangeInv = 1f / Mathf.Max(innerCos - outerCos, 0.001f);
        otherLightSpotAngles[index] = new Vector4(angleRangeInv, -outerCos * angleRangeInv);
        otherLightShadowData[index] = shadows.ReserveOtherShadows(light, visibleIndex);
    }

    void SetupLights(bool useLightsPerObject, int renderingLayerMask)
    {
        // me09: 每物体灯光模式才需要 IndexMap
        NativeArray<int> indexMap = useLightsPerObject ?
            cullingResults.GetLightIndexMap(Allocator.Temp) : default;
        NativeArray<VisibleLight> visibleLights = cullingResults.visibleLights;

        int dirLightCount = 0, otherLightCount = 0;
        int i;
        for (i = 0; i < visibleLights.Length; i++)
        {
            int newIndex = -1;
            VisibleLight visibleLight = visibleLights[i];
            Light light = visibleLight.light;
            // me14: 相机 renderingLayerMask 过滤灯光（-1 & anything = anything ≠ 0，全通过）
            if ((light.renderingLayerMask & renderingLayerMask) != 0)
            {
                switch (visibleLight.lightType)
                {
                    case LightType.Directional:
                        if (dirLightCount < maxDirLightCount)
                            // me10: 传 i 作为 visibleIndex（真实索引）
                            // me14: 传 visibleLight.light 对象，用于读取 renderingLayerMask
                            SetupDirectionalLight(dirLightCount++, i, ref visibleLight, light);
                        break;
                    case LightType.Point:
                        if (otherLightCount < maxOtherLightCount)
                        {
                            newIndex = otherLightCount;
                            // me14: 传 light 对象，用于读取 renderingLayerMask
                            SetupPointLight(otherLightCount++, i, ref visibleLight, light);
                        }
                        break;
                    case LightType.Spot:
                        if (otherLightCount < maxOtherLightCount)
                        {
                            newIndex = otherLightCount;
                            // me14: 传 light 对象，用于读取 renderingLayerMask
                            SetupSpotLight(otherLightCount++, i, ref visibleLight, light);
                        }
                        break;
                }
            }
            if (useLightsPerObject)
                indexMap[i] = newIndex;
        }

        // me09: 把不可见灯光的索引设为 -1，传回 Unity
        if (useLightsPerObject)
        {
            for (; i < indexMap.Length; i++)
                indexMap[i] = -1;
            cullingResults.SetLightIndexMap(indexMap);
            indexMap.Dispose();
            Shader.EnableKeyword(lightsPerObjectKeyword);
        }
        else
        {
            Shader.DisableKeyword(lightsPerObjectKeyword);
        }

        // 发送数据给 GPU
        buffer.SetGlobalInt(dirLightCountId, dirLightCount);
        if (dirLightCount > 0)
        {
            buffer.SetGlobalVectorArray(dirLightColorsId, dirLightColors);
            buffer.SetGlobalVectorArray(dirLightDirectionsAndMasksId, dirLightDirectionsAndMasks);
            buffer.SetGlobalVectorArray(dirLightShadowDataId, dirLightShadowData);
        }
        buffer.SetGlobalInt(otherLightCountId, otherLightCount);
        if (otherLightCount > 0)
        {
            buffer.SetGlobalVectorArray(otherLightColorsId, otherLightColors);
            buffer.SetGlobalVectorArray(otherLightPositionsId, otherLightPositions);
            buffer.SetGlobalVectorArray(otherLightDirectionsAndMasksId, otherLightDirectionsAndMasks);
            buffer.SetGlobalVectorArray(otherLightSpotAnglesId, otherLightSpotAngles);
            buffer.SetGlobalVectorArray(otherLightShadowDataId, otherLightShadowData);
        }
    }

    public void Cleanup()
    {
        shadows.Cleanup();
    }
}