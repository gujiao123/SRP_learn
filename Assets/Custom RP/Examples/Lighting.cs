using UnityEngine;
using UnityEngine.Rendering;
using Unity.Collections; // 需要用到 NativeArray

public class Lighting {

    
    
    const string bufferName = "Lighting";
    
    // 命令缓冲区，用于发送数据
    CommandBuffer buffer = new CommandBuffer {
        name = bufferName
    };

    // 限制最大灯光数量
    const int maxDirLightCount = 4;

    // Shader 属性 ID
    // 1. 获取变量的“地址” (类似 glGetUniformLocation)
    // PropertyToID 把字符串名字哈希成一个整数 ID，比直接传字符串快
    static int
        dirLightCountId = Shader.PropertyToID("_DirectionalLightCount"),
        dirLightColorsId = Shader.PropertyToID("_DirectionalLightColors"),
        dirLightDirectionsId = Shader.PropertyToID("_DirectionalLightDirections"),
        dirLightShadowDataId = Shader.PropertyToID("_DirectionalLightShadowData");


    // CPU 端缓存数组
    static Vector4[]
        dirLightColors = new Vector4[maxDirLightCount],
        dirLightDirections = new Vector4[maxDirLightCount],
        dirLightShadowData = new Vector4[maxDirLightCount];

    // 剔除结果
    CullingResults cullingResults;
    
    Shadows shadows = new Shadows();

    

    // --- 公共接口 ---
    /// <summary>
    /// 通过上下文的剔除结果来获取灯光
    /// </summary>
    /// <param name="context"></param>
    /// <param name="cullingResults"></param>
    public void Setup (ScriptableRenderContext context, CullingResults cullingResults,
        ShadowSettings shadowSettings // 新增参数
        ) {
        this.cullingResults = cullingResults;
        
        buffer.BeginSample(bufferName);
        
        // 1. 先设置阴影
        shadows.Setup(context, cullingResults, shadowSettings); // 转发给 Shadows

        // 核心逻辑：设置灯光
        SetupLights();
        
        // 2. 渲染阴影 (在设置完所有光源后)
        shadows.Render();
        
        buffer.EndSample(bufferName);
        
        context.ExecuteCommandBuffer(buffer);
        buffer.Clear();
    }
    
    
    /// <summary>
    /// 配置定向光，包括其颜色和方向，并预留阴影。
    /// </summary>
    /// <param name="index">定向光在数组中的索引。</param>
    /// <param name="visibleLight">可见的定向光源。</param>
    void SetupDirectionalLight(int index, ref VisibleLight visibleLight)
    {
        // 颜色：finalColor 已经包含了强度(Intensity)
        dirLightColors[index] = visibleLight.finalColor;
        
        // 方向：是 localToWorldMatrix 的第三列 (Z轴)，取反
        // 因为光照方向通常是指“光从哪里来”，而 forward 是“光去哪里”
        dirLightDirections[index] = -visibleLight.localToWorldMatrix.GetColumn(2);
        //得到可见光的强度和ID
        dirLightShadowData[index] = shadows.ReserveDirectionalShadows(visibleLight.light, index);
    }
    
    
    void SetupLights () {
        // 从剔除结果中获取所有可见灯光
        //me05 这个visibleLights 只会传递 realtime和mixed 也就是说用于baked模式的灯光你烘焙完后就不给了你了,你要取用生成好的光照贴图
        NativeArray<VisibleLight> visibleLights = cullingResults.visibleLights;
        
        int dirLightCount = 0;
        
        for (int i = 0; i < visibleLights.Length; i++) {
            VisibleLight visibleLight = visibleLights[i];
            
            // 只处理定向光
            if (visibleLight.lightType == LightType.Directional) {
                // 配置这盏灯
                SetupDirectionalLight(dirLightCount++, ref visibleLight);
                
                // 如果超过上限，就停止
                if (dirLightCount >= maxDirLightCount) {
                    break;
                }
            }
        }
        
        // 发送数据给 GPU
        // 2. 赋值 (类似 glUniform1i)
        // 把 dirLightCount 的值，赋给 ID 对应的 Shader 变量
        buffer.SetGlobalInt(dirLightCountId, dirLightCount);
        buffer.SetGlobalVectorArray(dirLightColorsId, dirLightColors);
        buffer.SetGlobalVectorArray(dirLightDirectionsId, dirLightDirections);
        buffer.SetGlobalVectorArray(dirLightShadowDataId, dirLightShadowData);
    }

    
    /// 新增清理接口
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