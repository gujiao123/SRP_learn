using UnityEngine;
using UnityEngine.Rendering;
using Unity.Collections; // 需要用到 NativeArray

public class Lighting {

    const string bufferName = "Lighting";
    
    // 命令缓冲区，用于发送数据
    CommandBuffer buffer = new CommandBuffer {
        name = bufferName
    };

    // me09: 方向光最多4，其他光最多64
    const int maxDirLightCount = 4, maxOtherLightCount = 64;

    // Shader 属性 ID
    // 1. 获取变量的“地址” (类似 glGetUniformLocation)
    // PropertyToID 把字符串名字哈希成一个整数 ID，比直接传字符串快
    static int
        dirLightCountId   = Shader.PropertyToID("_DirectionalLightCount"),
        dirLightColorsId  = Shader.PropertyToID("_DirectionalLightColors"),
        dirLightDirectionsId = Shader.PropertyToID("_DirectionalLightDirections"),
        dirLightShadowDataId = Shader.PropertyToID("_DirectionalLightShadowData"),
        // me09: 点光源/聚光灯的属性
        otherLightCountId      = Shader.PropertyToID("_OtherLightCount"),
        otherLightColorsId     = Shader.PropertyToID("_OtherLightColors"),
        otherLightPositionsId  = Shader.PropertyToID("_OtherLightPositions"),
        otherLightDirectionsId = Shader.PropertyToID("_OtherLightDirections"),
        otherLightSpotAnglesId = Shader.PropertyToID("_OtherLightSpotAngles"),
        otherLightShadowDataId = Shader.PropertyToID("_OtherLightShadowData");

    static Vector4[]
        dirLightColors     = new Vector4[maxDirLightCount],
        dirLightDirections = new Vector4[maxDirLightCount],
        dirLightShadowData = new Vector4[maxDirLightCount],//存放剔除结果
        // me09: Other Lights 缓存
        otherLightColors     = new Vector4[maxOtherLightCount],
        otherLightPositions  = new Vector4[maxOtherLightCount],   // xyz=位置, w=1/r²
        otherLightDirections = new Vector4[maxOtherLightCount],   // 聚光灯方向
        otherLightSpotAngles = new Vector4[maxOtherLightCount],   // x=a, y=b 角度衰减参数
        otherLightShadowData = new Vector4[maxOtherLightCount];   // 烘焙阴影数据

    CullingResults cullingResults;
    Shadows shadows = new Shadows();

    // me09: 每物体灯光关键字
    static string lightsPerObjectKeyword = "_LIGHTS_PER_OBJECT";

    // --- 公共接口 ---
    public void Setup (
        ScriptableRenderContext context,
        CullingResults cullingResults,
        ShadowSettings shadowSettings,
        bool useLightsPerObject = false  // me09: 每物体灯光开关
    ) {
        this.cullingResults = cullingResults;
        buffer.BeginSample(bufferName);
        shadows.Setup(context, cullingResults, shadowSettings);
        SetupLights(useLightsPerObject);
        shadows.Render();
        buffer.EndSample(bufferName);
        context.ExecuteCommandBuffer(buffer);
        buffer.Clear();
    }

    // --- 配置方向光 ---
    void SetupDirectionalLight (int index, ref VisibleLight visibleLight) {
        dirLightColors[index]     = visibleLight.finalColor;
        // 方向：是 localToWorldMatrix 的第三列 (Z轴)，取反
        // 因为光照方向通常是指“光从哪里来”，而 forward 是“光去哪里”
        dirLightDirections[index] = -visibleLight.localToWorldMatrix.GetColumn(2);
        //得到可见光的强度和ID
        dirLightShadowData[index] = shadows.ReserveDirectionalShadows(visibleLight.light, index);
    }

    // me09: 配置点光源
    // 位置 xyz = 世界坐标，w = 1/range²（CPU 预算好发给 GPU，省一次除法）
    void SetupPointLight (int index, ref VisibleLight visibleLight) {
        otherLightColors[index] = visibleLight.finalColor;
        Vector4 position = visibleLight.localToWorldMatrix.GetColumn(3);
        position.w = 1f / Mathf.Max(visibleLight.range * visibleLight.range, 0.00001f);
        otherLightPositions[index] = position;
        // 点光源没有角度限制：a=0, b=1 → dot*0+1=1，不衰减
        otherLightSpotAngles[index] = new Vector4(0f, 1f);
        otherLightShadowData[index] = shadows.ReserveOtherShadows(visibleLight.light, index);
    }

    // me09: 配置聚光灯
    // 额外存：灯的方向 + 内外角参数 a/b（使 GPU 只需 dot*a+b 做衰减）
    void SetupSpotLight (int index, ref VisibleLight visibleLight) {
        otherLightColors[index] = visibleLight.finalColor;
        Vector4 position = visibleLight.localToWorldMatrix.GetColumn(3);
        position.w = 1f / Mathf.Max(visibleLight.range * visibleLight.range, 0.00001f);
        otherLightPositions[index] = position;
        // 方向：变换矩阵第3列取反（Z轴正方向即灯朝向，取反得"光来源方向"）
        otherLightDirections[index] = -visibleLight.localToWorldMatrix.GetColumn(2);
        // 内外角转换为 cos 值，再算 a/b 减少 GPU 工作量
        Light light = visibleLight.light;
        float innerCos = Mathf.Cos(Mathf.Deg2Rad * 0.5f * light.innerSpotAngle);
        float outerCos = Mathf.Cos(Mathf.Deg2Rad * 0.5f * visibleLight.spotAngle);
        float angleRangeInv = 1f / Mathf.Max(innerCos - outerCos, 0.001f);
        otherLightSpotAngles[index] = new Vector4(angleRangeInv, -outerCos * angleRangeInv);
        otherLightShadowData[index] = shadows.ReserveOtherShadows(light, index);
    }

    void SetupLights (bool useLightsPerObject) {
        // me09: 每物体灯光模式才需要 IndexMap
        NativeArray<int> indexMap = useLightsPerObject ?
            cullingResults.GetLightIndexMap(Allocator.Temp) : default;
        NativeArray<VisibleLight> visibleLights = cullingResults.visibleLights;

        int dirLightCount = 0, otherLightCount = 0;
        int i;
        //me09:遍历可见光
        for (i = 0; i < visibleLights.Length; i++) {
            int newIndex = -1;
            VisibleLight visibleLight = visibleLights[i];
            switch (visibleLight.lightType) {
                case LightType.Directional:
                    if (dirLightCount < maxDirLightCount)
                        SetupDirectionalLight(dirLightCount++, ref visibleLight);
                    break;
                case LightType.Point:
                    if (otherLightCount < maxOtherLightCount) {
                        newIndex = otherLightCount;
                        SetupPointLight(otherLightCount++, ref visibleLight);
                    }
                    break;
                case LightType.Spot:
                    if (otherLightCount < maxOtherLightCount) {
                        newIndex = otherLightCount;
                        SetupSpotLight(otherLightCount++, ref visibleLight);
                    }
                    break;
            }
            if (useLightsPerObject)
                indexMap[i] = newIndex;
        }

        // me09: 把不可见灯光的索引设为 -1，传回 Unity
        if (useLightsPerObject) {
            for (; i < indexMap.Length; i++)
                indexMap[i] = -1;
            cullingResults.SetLightIndexMap(indexMap);
            indexMap.Dispose();
            Shader.EnableKeyword(lightsPerObjectKeyword);
        } else {
            Shader.DisableKeyword(lightsPerObjectKeyword);
        }

        // 发送数据给 GPU（只在有灯光时才发送对应数组，减少开销）
        buffer.SetGlobalInt(dirLightCountId, dirLightCount);
        if (dirLightCount > 0) {
            buffer.SetGlobalVectorArray(dirLightColorsId, dirLightColors);
            buffer.SetGlobalVectorArray(dirLightDirectionsId, dirLightDirections);
            buffer.SetGlobalVectorArray(dirLightShadowDataId, dirLightShadowData);
        }
        buffer.SetGlobalInt(otherLightCountId, otherLightCount);
        if (otherLightCount > 0) {
            buffer.SetGlobalVectorArray(otherLightColorsId, otherLightColors);
            buffer.SetGlobalVectorArray(otherLightPositionsId, otherLightPositions);
            buffer.SetGlobalVectorArray(otherLightDirectionsId, otherLightDirections);
            buffer.SetGlobalVectorArray(otherLightSpotAnglesId, otherLightSpotAngles);
            buffer.SetGlobalVectorArray(otherLightShadowDataId, otherLightShadowData);
        }
    }

    public void Cleanup () {
        shadows.Cleanup();
    }
}


/*
这三个问题非常精准，涵盖了 SRP 的核心机制。

### 1. `context.ExecuteCommandBuffer(buffer)` 是发送到 GPU 吗？

**不完全是，但也差不多。**

    *   **CommandBuffer (命令缓冲区)**：你可以把它想象成一张**“购物清单”**。
*   你在 C# 里调用 `buffer.SetGlobalVector(...)`，只是在纸上写下了“我要买苹果”。此时并没有真正执行，GPU 根本不知道。
*   **ExecuteCommandBuffer**：这是把清单**“提交”**给 RenderContext（渲染上下文）。
*   Context 会把这张清单里的指令，转换成底层的图形 API 调用（DX11/Vulkan/OpenGL）。
*   **真正发给 GPU**：通常是在 `context.Submit()` 被调用的时候，或者 Context 内部缓冲区满的时候。但你可以认为执行完这行代码，管线就已经把任务排进日程表了。

**总结**：`buffer` 是记录，`Execute` 是提交记录。

---

### 2. `buffer.SetGlobalInt(...)` 是干什么？

    这是在设置 **Shader 的全局变量**。

*   **Global (全局)**：意味着这个变量对**所有** Shader 都可见。
*   **对应关系**：
*   C# 代码：`buffer.SetGlobalInt(dirLightCountId, 2);`
*   Shader 代码 (HLSL)：`int _DirectionalLightCount;`
*   **作用**：
*   告诉 Shader：“嘿，现在场景里有 **2** 盏灯哦。”
*   这样 Shader 里的 `for` 循环就知道要循环 2 次，而不是 4 次（最大值）。

这是 CPU 控制 Shader 逻辑流的关键手段。

---

### 3. 为什么能从剔除结果 (`CullingResults`) 中获取可见灯光？

    这是 Unity 引擎的**剔除 (Culling) 系统**的职责。

*   **场景**：你的场景极其巨大，有 1000 盏灯。
*   **问题**：如果把这 1000 盏灯都发给 GPU，显卡直接冒烟。
*   **Culling 的作用**：
*   当你调用 `context.Cull()` 时，Unity 会计算摄像机的**视锥体 (Frustum)**。
*   它会判断：哪些灯光照到了视锥体内的物体？
*   **结果**：假设只有 3 盏灯照到了你当前看到的房间。
*   Unity 会把这 3 盏灯放进 `cullingResults.visibleLights` 数组里返回给你。
*   **意义**：你拿到的 `visibleLights` 已经是经过**空间优化**的列表了。你不需要自己去算“哪盏灯离我近”，Unity 帮你算好了。

逻辑闭环了吗？*/