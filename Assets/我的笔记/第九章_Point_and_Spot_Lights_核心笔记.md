# 第九章：Point and Spot Lights 核心笔记

> 核心思想：在平行光基础上支持有限范围的点光源和聚光灯，同时引入性能优化（每物体灯光索引）

---

## 🎯 为什么需要这一章？

```
第八章以前：
  只有平行光 → 方向固定，全场景等强度，无衰减
  一个场景通常只有1~4盏平行光（模拟太阳/月亮）

现实中还有：
  台灯（点光源）→ 照亮桌子周围，越远越暗
  手电筒（聚光灯）→ 锥形光束，边缘渐暗
  
  这些灯：
  ① 有位置（不是无限远）
  ② 有衰减（越远越暗，平方反比定律）
  ③ 有范围（超出 Range 完全没有光）
```

---

## 第一部分：点光源 vs 聚光灯的关系

```
聚光灯 = 点光源 + 角度限制

点光源：
  → 向四面八方发光，无方向限制

聚光灯：
  → 有朝向，只在锥形范围内发光
  → 有内角（全亮区）和外角（渐暗区）

代码层面：
  点光源和聚光灯共用同一个 GetOtherLight() 函数！
  区别只在 spotAngles 参数：
    点光源：SpotAngles = (a=0, b=1) → dot×0+1=1 → 角度衰减=1 → 无限制
    聚光灯：SpotAngles = (a, b) → 计算出真实锥形衰减
```

---

## 第二部分：完整数据链路

### CPU → GPU 数据流

```
①  Unity CullingResults.visibleLights
       ↓ 包含点光源/聚光灯
②  Lighting.cs::SetupLights(useLightsPerObject)
       ├─ SetupPointLight(i, ref light)
       │     otherLightColors[i]     = finalColor
       │     otherLightPositions[i]  = 位置(xyz) + 1/range²(w)  ← CPU预算，省GPU除法
       │     otherLightSpotAngles[i] = (0, 1)                    ← 无角度衰减
       │     otherLightShadowData[i] = ReserveOtherShadows(...)
       │
       └─ SetupSpotLight(i, ref light)
             otherLightColors[i]     = 同上
             otherLightPositions[i]  = 同上
             otherLightDirections[i] = -矩阵第3列 (Z轴取反=灯朝向)
             otherLightSpotAngles[i] = (a, b)    ← 内外角计算
             otherLightShadowData[i] = 同上
       ↓
③  buffer.SetGlobalVectorArray × 5个数组 → GPU
       _OtherLightCount
       _OtherLightColors
       _OtherLightPositions    (xyz + 1/r²)
       _OtherLightDirections   (聚光灯方向)
       _OtherLightSpotAngles   (a, b)
       _OtherLightShadowData   (阴影强度 + 通道号)
       ↓
④  Light.hlsl::GetOtherLight() 每片元计算衰减
       ↓
⑤  Lighting.hlsl::GetLighting() 累加所有灯
```

---

## 第三部分：四重衰减详解

### ③ 距离平方衰减（物理定律）

```
原理：光向四面八方扩散，球面积 = 4πd²
      相同总光量照到越大面积 → 单位亮度 ∝ 1/d²

代码：
    float3 ray = lightPos - surfacePos;      // 光线向量
    float distanceSqr = max(dot(ray, ray), 0.00001);
                            ↑
                        dot(ray,ray) = |ray|² = d²
    最终 1/distanceSqr = 1/d²

    max(..., 0.00001)：防止距离=0时除以零崩溃
```

### ④ 范围衰减（软截断，避免硬边界）

```
问题：纯 1/d² 在 Range 外仍有光（极小值）
     → Unity 剔除系统不敢判断灯"对该物体无效"
     → 所有灯必须对所有物体计算 → 性能爆炸

解决：在 Range 边缘将光强度截断到严格 0

公式：saturate(1 - (d²/r²)²)²

代码拆解：
    distanceSqr * _OtherLightPositions[i].w
                                        ↑
                                     1/r²（CPU预算好了）
    = d² × (1/r²) = d²/r²

    saturate(1 - d²/r²)²：
      d很小（靠近灯）→ ≈1.0（不衰减）
      d = r（Range边缘）→ = 0（完全消失）
      d > r → saturate强制=0
      两次Square → 边缘曲线平滑，不是线性截断

为什么截断到严格0很重要？
  → Unity 做 Lights Per Object 时：
    判断"灯的包围球是否碰到物体" → 没碰 → 不发索引
  → Shader 里根本不会循环到这盏灯
  → 性能大幅提升！
```

### ⑤ 聚光灯角度衰减

```
公式：saturate(dot(灯方向D, 光线方向L) × a + b)²

dot(D, L) = cos(两者夹角)
  夹角 = 0° 正中心 → dot = 1.0（最亮）
  夹角 = 外角/2    → dot = cos(外角/2)（应该=0）
  夹角 = 内角/2    → dot = cos(内角/2)（应该=1）

a, b 的含义（CPU 里算好，省 GPU 计算）：
  a = 1 / (cos(内角/2) - cos(外角/2))
  b = -cos(外角/2) × a

  当 dot = cos(内角/2) → 代入 dot×a+b = 1 ✅ 内角内全亮
  当 dot = cos(外角/2) → 代入 dot×a+b = 0 ✅ 外角外完全没光
  中间                 → 线性插值（再Square→曲线更好看）

点光源的处理：
  a=0, b=1 → 任何方向 dot×0+1=1 → ²=1 → 无角度衰减 ✅
```

### ⑦ 最终叠乘

```hlsl
light.attenuation =
    shadowAtten        // 烘焙阴影（0=被遮蔽，1=无遮蔽）
  * spotAttenuation    // 角度衰减（锥外=0）
  * rangeAttenuation   // 范围衰减（Range外=0）
  / distanceSqr;       // 平方反比（越远越暗）

任意一项=0 → 整体=0（不影响渲染）
都=1       → 纯物理 1/d² 衰减
```

---

## 第四部分：点/聚光灯的烘焙阴影

### 和平行光的设置一样，但有关键区别

```
相同：
  Inspector → Mode 改 Mixed → Unity 自动烘焙 Shadow Mask

区别（重要！）：
  平行光：影响整个场景 → Shadow Mask 一整张图都有数据
          → 一盏灯独占一个通道（R/G/B/A）最多4盏
  
  点/聚光灯：有 Range 限制 → 超出Range的像素=白色（无阴影）
             → 不重叠的点光源可以共用同一通道！
             → 理论上可以有无限盏 Mixed 点光源（只要不重叠）

             重叠情况：Unity 自动分配不同通道
             4个通道全被重叠的灯占满时：
               → 多余的灯自动降级为 Baked 模式
```

### ReserveOtherShadows 代码逻辑

```csharp
// 和方向光的区别：只关心 Shadow Mask，不处理实时阴影（本章不含实时阴影）
if (light.shadows != LightShadows.None && light.shadowStrength > 0f) {
    if (是Mixed + Shadowmask模式) {
        useShadowMask = true;
        return (strength, 0, 0, occlusionMaskChannel);  // 通道号
    }
}
return (0, 0, 0, -1);  // 无烘焙阴影，通道=-1
```

---

## 第五部分：Lights Per Object（性能优化）

### 问题

```
场景里64盏可见点光源，每个片元都要跑64次 GetOtherLight
→ 大场景有大量点光源时，GPU 计算量爆炸
```

### 解决方案

```
Unity 预先算好：每个物体受哪些灯影响（按 Range 包围球相交判断）
最多8盏灯的索引存到物体的 unity_LightIndices 里

Shader 只循环这8盏：
    for (j < min(unity_LightData.y, 8)) {
        int lightIndex = unity_LightIndices[j/4][j%4];
        // → 用 uint 除法（比 int 快）
        color += GetLighting(GetOtherLight(lightIndex, ...));
    }

代价：
  ① 以物体为单位，不够精确（大物体一端有灯，另一端也算）
  ② GPU Instancing 效率降低（相同灯列表才能合批）
  SRP Batcher 不受影响（每个物体独立 draw call）
```

### IndexMap：翻译本

```
Unity 内部编号（visibleLights顺序）≠ 我们的 otherLight 数组索引

IndexMap 做翻译：
  Unity 灯[0]（平行光）→ -1（跳过）
  Unity 灯[1]（点光源A）→ 0（我们的 index）
  Unity 灯[2]（聚光灯）→ 1

写回 SetLightIndexMap → Unity 往物体上打的 unity_LightIndices
已经是"我们的编号" → Shader 里直接用没有歧义
```

---

## 改动文件汇总

| 文件 | 改动内容 |
|---|---|
| `CameraRenderer.cs` | `useLightsPerObject` 参数链 + `perObjectData` 条件加 `LightData|LightIndices` |
| `CustomRenderPipeline.cs` | 改成 `partial class`，加 `useLightsPerObject`，调用 `InitializeForEditor()` |
| `CustomRenderPipeline.Editor.cs` | ✨ 新建：烘焙 Falloff Delegate |
| `CustomRenderPipelineAsset.cs` | 加 `useLightsPerObject` 面板开关（默认=true）|
| `CustomLightEditor.cs` | ✨ 新建：聚光灯内角滑条 |

---

## ⚠️ 两个 HLSL 坑（调试记录）

```
坑1：HLSL 函数必须先定义再调用（无前向声明！）
  GetOtherShadowAttenuation 调用了 GetBakedShadow
  但 GetBakedShadow 定义在后面 → 报错
  → 修复：把 GetOtherShadowAttenuation 移到 GetBakedShadow 之后

坑2：Include 顺序决定函数可见性
  Square 原来在 Lighting.hlsl（最后 include）
  Light.hlsl（中间 include）调用时找不到 → 报错
  → 修复：把 Square 移到 Common.hlsl（最先 include，所有文件可用）
```

---

## 第六部分：收尾功能

### CustomLightEditor（内角滑条）

```
问题：Unity 默认 Light Inspector 不显示 Inner Spot Angle
     但我们的 Shader 里读了 light.innerSpotAngle 来计算聚光灯渐变区

解决：Editor/CustomLightEditor.cs
  [CustomEditorForRenderPipeline(typeof(Light), typeof(CustomRenderPipelineAsset))]
  → 告诉 Unity：使用我们管线时，Light 的 Inspector 用这个类

  OnInspectorGUI():
    ① base.OnInspectorGUI()                  → 先画原来的内容不丢失
    ② 判断是否全是聚光灯
    ③ settings.DrawInnerAndOuterSpotAngle()   → 加联动滑条
       左边手柄 = 内角（全亮区）
       右边手柄 = 外角（渐暗区，联动 Spot Angle）
    ④ settings.ApplyModifiedProperties()      → 保存修改
```

### Baked Falloff Delegate（烘焙衰减修正）

```
问题：Unity 烘焙器默认用旧版 Falloff（不是 1/d²）
     我们 Shader 用严格 1/d²
     → Mixed 灯光：实时区 vs 烘焙区 亮度不匹配！

两套数据格式：
  Light（UnityEngine.Light）
    = 运行时渲染格式（Inspector 里的组件）
    = 角度单位是"度"，强度分开存

  LightDataGI（Experimental.GlobalIllumination）
    = 烘焙器格式（离线渲染器用的）
    = 角度单位是"弧度"，有 falloff 字段
    = 跟 Light 是两套独立系统

LightmapperUtils.Extract()
  = 把 Light 翻译成 LightDataGI（"工程图纸 → 施工说明书"）
  = 但不设置 falloff！（不知道你用什么衰减）

我们补上：
  lightData.falloff = FalloffType.InverseSquared  ← 整个文件的核心！

代码结构：
  partial class（两个文件写同一个类）：
    CustomRenderPipeline.cs      → 运行时代码
    CustomRenderPipeline.Editor.cs → Editor 专用（#if UNITY_EDITOR 包裹）

  Dispose() 里要 ResetDelegate()
    → 切换 RP 时清理钩子，避免影响其他 RP
```

---

> 🚧 下一章（第十章）：Point and Spot Shadows（点光源/聚光灯实时阴影！）
