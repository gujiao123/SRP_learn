# 第六章：阴影遮罩 (Shadow Masks) 核心概念笔记

> 前置知识：这是解答关于 Lightmap 与阴影存储最核心的疑问，也是理解现代管线混合光照的基础。

---

## 💡 Q1：烘焙光照（Lightmap）里面存了阴影吗？怎么分辨纯阴影和光？

> **用户疑问：**
> 现在的 Lightmap 存储阴影了吗？话说怎么分辨哪些是纯阴影哪些是光？毕竟现实中又不是突然变黑的。

这是一个直击物理本质的灵魂问题！要理解这个，我们必须先看你是用哪种模式烘焙的。因为**烘焙出的贴图里装了什么，完全取决于灯光的 Mode（模式）和 Lighting 设置。**

### 🍳 1. 完全烘焙模式 (Light Mode = Baked)
在传统的完全烘焙模式下：
- **存了阴影！**
- Lightmap 像一锅乱炖，把 **直接光 + 弹射的间接光 + 阴影** 全部死死地画在了一张图上。
- **缺点**：阴影和光照彻底融合，动态角色走进去无法被遮挡（因为阴影已经是地板上的死颜色了），你也无法获得实时的动态高光（Specular）。

### 🍹 2. 混合模式 (Light Mode = Mixed) 
> 这是现代 3A 大作和我们 Custom SRP 目前正在做的事！

Unity 天才般地决定：**为什么非要把光和阴影揉在一张图里？拆开它！**
当灯光设置为 Mixed（混合模式）时，底层发生了这样的化学反应：

#### 第一步：分类第一口光（直接光）👉 生成 Shadow Mask（阴影遮罩图）
- 烘焙大师（Lightmapper）发射射线，遇到障碍物被完全挡住的记为 `0`，完全没挡住的通透区域记为 `1`。
- 它把这些纯粹的 `0` 和 `1` 收集起来，存进一张名为 **Shadow Mask** 的新贴图中。
- **⚠️ 注意**：这张图**不存颜色、不存亮度，它是一张纯粹的数学关系图！仅仅代表"遮挡开关"。**

#### 第二步：分类弹射光（间接光）👉 生成 Lightmap（光照贴图）
- 光线撞击表面后产生的漫反射光，会继续弹射（Bounce）。这些第二口、第三口的弹射光，被收集起来。
- **⚠️ 惊天逆转**：在 Mixed 模式下，现在的 Lightmap **只存间接光/环境光！完全不存直接光和与之对应的生硬阴影了！**

---

## 🎨 Q2：Shader 里它们是怎么组合的？（为什么不会死黑？）

在运行时，你的 Shader 会执行这道优雅的数学题：

```text
最终颜色 = (直接光照计算 * Shadow Mask 中的 0或1) + Lightmap 中的间接光
```

这个公式完美解释了为什么**阴影绝对不会死黑**：

1. **当你在亮处时**：
   （极强的直接光 * `1`） + 环境间接光 = **非常亮**。
2. **当你在阴影处时**：
   （极强的直接光 * `0`） = 直接光变成了 `0`（也就是没有高光、没有太阳的直接照明了）。
   **但是！** 别忘了后面还要 **+ Lightmap 中的间接光**！
   由于间接光（天光、地面反光）依然存在，所以阴影部分**绝对不会是死黑的**！它会呈现出幽暗的、由周围环境弹射过来的微弱色彩。

这就是现代渲染管线最优雅的地方：
- **Lightmap** 负责提供永远不死黑的"环境底色"。
- **Shadow Mask** 负责提供切断直接光的"阴影开关"。
两者解耦，造就了真实的光影质感。

---

## ⚔️ Q3：Mixed 模式下的两种神仙策略

一旦有了 Shadow Mask，我们就能解决"实时阴影太耗性能，烘焙阴影又没法给动态物体用"的世纪难题。

Unity 提供了两种混合策略（在 Quality 面板设置）：

1. **Distance Shadowmask（距离遮罩模式） - 极致画质**
   - **近处**：使用你写的牛逼的**级联实时阴影**（动态、静态物体全都有高精度阴影）。
   - **远处**：超过 Max Shadow Distance 过渡区后，**无缝平滑切换**到烘焙好的 Shadow Mask。
   - 这样既有近处的完美清晰画角，又有远景的阴影形状，同时保住了帧率！

2. **Shadowmask（全遮罩模式） - 极致性能**
   - **静态物体**：无论多近，全靠老老实实读 Shadow Mask 烘焙贴图（省去大量实时计算）。
   - **动态物体**：依然保留少量的实时阴影计算。
   - 这种模式把有限的 GPU 实时计算性能，好钢用在刃上，全塞给动态的主角和怪物！

---
> 进度：第一阶段（读取数据）完成，第二阶段（混合逻辑）完成！

---

## 🗂️ 代码改动地图（第六章完整数据流）

### 第一站：`Shadows.cs`（CPU 端总指挥）
```
Setup() 每帧开始
└── useShadowMask = false  ← 每帧重置旗子

ReserveDirectionalShadows() 遍历每盏灯时
└── 检查 light.bakingOutput：
    └── 如果是 Mixed + Shadowmask 模式 → useShadowMask = true

Render() 每帧结束
└── SetKeywords → 开启 _SHADOW_MASK_DISTANCE 关键词
```

### 第二站：`Shadows.hlsl`（Shader 端数据结构）
```hlsl
// 必须先定义 ShadowMask，才能在 ShadowData 里用它
struct ShadowMask {
    bool distance;    // 是否在 Distance 模式
    float4 shadows;   // 4 通道，最多存 4 盏灯的遮挡值
};
struct ShadowData {
    ...
    ShadowMask shadowMask; // 新加
};
// GetShadowData() 里初始化默认值（全白=无遮挡）
```

### 第三站：`GI.hlsl`（Shader 端数据读取）
```hlsl
// 新增纹理声明
TEXTURE2D(unity_ShadowMask);
SAMPLER(samplerunity_ShadowMask);

// 采样函数
float4 SampleBakedShadows(float2 lightMapUV) {
    #if defined(LIGHTMAP_ON)
        return SAMPLE_TEXTURE2D(unity_ShadowMask, ...);  // 静态物体：读贴图
    #else
        return unity_ProbesOcclusion;  // 动态物体：读遮挡探针
    #endif
}
// GetGI() 里：关键词开启时采样并存入 gi.shadowMask.shadows
```

### 第四站：`Lighting.hlsl`（搬运数据）
```hlsl
shadowData.shadowMask = gi.shadowMask; // ← 这一行是关键！
```

---

## 🧠 Q4：每个像素是遍历每盏灯 + 计算阴影吗？

**是的！** Lighting.hlsl 的主循环：
```
for (每盏灯) {
    GetDirectionalLight() → 内部调用 GetDirectionalShadowAttenuation()
    → 得到这盏灯对这个像素的阴影衰减值
}
```
每个像素 × 每盏灯 都会走一次阴影计算。

---

## 🧠 Q5：实时 ShadowMap vs Shadow Mask 的存储方式

| | 实时 ShadowMap | Shadow Mask |
|---|---|---|
| **存储方式** | 每盏灯独立 Tile（空间分割） | 每盏灯独立颜色通道（R/G/B/A）|
| **上限** | 最多 4 盏灯 × 4 级联 = 16 格 | 最多 4 盏灯 = 4 通道 |
| **动态物体** | ✅ 有阴影 | ❌ 靠遮挡探针补救 |
| **远处可见** | ❌ 超出距离消失 | ✅ 永远可见 |

两者设计互补：近处用实时（精度高），远处用烘焙（范围广）。

---

## 🧠 Q6：Shadow Mask 的数据从哪里来？

```
编辑器                         Unity 自动                     你的代码
────                           ──────                         ──────
灯光 Mode=Mixed+Shadowmask         
[点击 Generate Lighting]
        ↓
Lightmapper 生成 Shadow Mask 贴图（存到 Assets/）
        ↓
运行时：perObjectData.ShadowMask → 绑定到 unity_ShadowMask 纹理槽
        ↓
GI.hlsl 里 SampleBakedShadows() 采样读取
```

---

## 🔀 混合逻辑核心（第二大块）

### `GetDirectionalShadowAttenuation()` 新版流程

```
进入函数
  ├── !_RECEIVE_SHADOWS → return 1.0（材质不接受阴影）
  │
  ├── strength <= 0 → shadow = 1.0
  │   （注意：不再是 return，而是 if/else，为后续烘焙支持留空间）
  │
  └── else：
        GetCascadedShadow()              ← 采样实时阴影
        MixBakedAndRealtimeShadows()     ← 混合实时+烘焙
        return shadow
```

### `MixBakedAndRealtimeShadows()` 核心公式

```hlsl
shadow = lerp(baked, shadow, global.strength);
//  global.strength=1（近处）→ 结果=shadow（全实时）
//  global.strength=0（远处）→ 结果=baked（全烘焙）
//  global.strength=0.5（过渡区）→ 两者各一半，无缝融合！
```

`global.strength` 从"让阴影消失的开关"→ 变成"在实时和烘焙之间移动的旋钮"！

---

## 🧠 Q7：`if(strength <= 0) { shadow = 1.0 }` 为什么改成 if/else 而非提前 return？

旧写法直接 `return 1.0` 时，负 strength 的情况下根本到不了烘焙阴影逻辑。
后续教程：`strength` 会被故意设为**负值**来表示"没有实时阴影但有烘焙阴影"的灯，
这时候需要走另一个分支 `GetBakedShadow(mask, abs(strength))`，所以必须变成 if/else 结构。

---
> 进度：第二阶段（混合逻辑）完成 ✅

---

## 🏁 第三阶段：完整实现总结（Part A + B + C）

### Part A：Only Baked Shadows（无实时投影体时保留烘焙阴影）

**问题：** 灯光没有任何实时投影体时，`ReserveDirectionalShadows` 提前返回 `Vector3.zero`，导致 strength=0，Shadow Mask 数据被跳过。

**修复：负数 strength 作为信号**
```
正数 strength → 有实时阴影 + 可能有 Shadow Mask
负数 strength → 无实时阴影，但有 Shadow Mask！跳过实时采样！
零             → 完全没有阴影
```

**C# 改动（`Shadows.cs`）：**
```csharp
// 没有实时投影体但有 Shadow Mask → 返回负数
return new Vector4(-light.shadowStrength, 0f, 0f, maskChannel);
```

**HLSL 改动（`Shadows.hlsl`）：**
```hlsl
// 旧：strength <= 0
// 新：strength * global.strength <= 0（两种情况都能触发）
if (directional.strength * global.strength <= 0.0) {
    shadow = GetBakedShadow(mask, channel, abs(directional.strength));
}
```

**`Light.hlsl` 改动：** 去掉 `* shadowData.strength`，把距离衰减留给 `MixBakedAndRealtimeShadows` 处理。

---

### Part B：Always Shadow Mask 模式

**新增第二种关键词 `_SHADOW_MASK_ALWAYS`**

| 模式 | 关键词 | 行为 |
|---|---|---|
| Distance Shadowmask | `_SHADOW_MASK_DISTANCE` | 近实时，远烘焙（平滑过渡）|
| Shadowmask（Always）| `_SHADOW_MASK_ALWAYS` | 静态物体始终烘焙，动态物体叠加实时 |

**混合方式区别：**
```hlsl
// Distance 模式：lerp 平滑切换
shadow = lerp(baked, realtime, global.strength);

// Always 模式：min 合并两种阴影（谁暗用谁）
shadow = lerp(1.0, realtime, global.strength);  // 实时按距离淡出
shadow = min(baked, shadow);                    // 合并：动态+静态都可见
```

**改动文件：**
- `Shadows.cs`：关键词数组加 `_SHADOW_MASK_ALWAYS`，根据 `QualitySettings.shadowmaskMode` 选 index
- `Common.hlsl`：`#if defined(_SHADOW_MASK_ALWAYS) || defined(_SHADOW_MASK_DISTANCE)` 都触发 `SHADOWS_SHADOWMASK`
- `Shadows.hlsl`：`ShadowMask` 结构体加 `bool always`，`MixBakedAndRealtimeShadows` 加 always 分支
- `GI.hlsl`：`GetGI()` 用 `#if/#elif` 区分两种模式
- `Lit.shader`：`#pragma multi_compile _ _SHADOW_MASK_ALWAYS _SHADOW_MASK_DISTANCE`

---

### Part C：多灯通道支持

**问题：** Shadow Mask 的 RGBA 4 个通道对应最多 4 盏灯，但旧代码硬编码只读 `.r`（灯1），灯2 的 `.g` 通道被忽略。

**解决方案：把通道号从 C# 传到最底层采样函数。**

**数据流：**
```
Unity 烘焙 → 自动分配 occlusionMaskChannel（0/1/2/3）
    ↓
Shadows.cs → 返回 Vector4.w = maskChannel
    ↓
Light.hlsl → shadowMaskChannel = _DirectionalLightShadowData[i].w
    ↓
Shadows.hlsl → GetBakedShadow(mask, channel)
    ↓
mask.shadows[channel]  ← 动态选 R/G/B/A！
```

**`DirectionalShadowData` 结构体新增：**
```hlsl
int shadowMaskChannel; // -1=无烘焙, 0=R, 1=G, 2=B, 3=A
```

**`GetBakedShadow` 签名变化：**
```hlsl
// 旧：硬编码 .r
float GetBakedShadow(ShadowMask mask)

// 新：动态通道
float GetBakedShadow(ShadowMask mask, int channel)
    → if (channel >= 0) shadow = mask.shadows[channel];
float GetBakedShadow(ShadowMask mask, int channel, float strength)
```

---

## 🧠 概念速查

### Light Mode vs ShadowmaskMode

| 设置位置 | 名称 | 作用 |
|---|---|---|
| 灯光 Inspector | Light Mode | 这盏灯是 Realtime/Mixed/Baked |
| Quality Settings | ShadowmaskMode | Mixed 灯的阴影混合方式 |

**关系：Light Mode=Mixed 是前提，ShadowmaskMode 才决定行为！**

### Shadow Mask 贴图颜色含义

在 Unity Lighting 窗口 → Baked Shadowmask 视图：
```
黄色 = R+G 都是 1.0（两盏灯都没有阴影在这里）
红色块 = 只有灯1的阴影（R 通道变暗）
绿色块 = 只有灯2的阴影（G 通道变暗）
黑色块 = 两盏灯的阴影都在这里（R+G 都变暗）
```

### `global.strength` vs `directional.strength`

| | `global.strength` | `directional.strength` |
|---|---|---|
| 代表 | 像素离摄像机的距离衰减 | 这盏灯的阴影强度设置 |
| 近处 | 1.0 | 保持不变（面板设置值）|
| 超出 Max Distance | 0.0 | 保持不变 |
| 无实时投影体 | 不变 | **变为负数**（信号！）|

---
> 🎉 第六章 Shadow Masks 全部完成！下一章：LOD and Reflections
