# 📒 第七章：LOD and Reflections 核心笔记

> 记录时间：2026-03-22  
> 章节进度：✅ LOD 部分完成 | 🔄 Reflections 待续

---

## 第一部分：LOD（层次细节 Level of Detail）

### 🤔 LOD 是什么？

**核心思路**：同一个物体按距离远近使用不同精度的模型，节省 GPU。

```
近处（物体大）   → LOD 0（最高精度，三角面最多）
中距离          → LOD 1（中等精度）
远处（物体小）   → LOD 2（最低精度，简模）
极远处          → Culled（直接不渲染）
```

**类比**：报纸里的高清照片远看就是一堆灰点，你不需要看清每个像素，所以远处的东西用简模就够了。

---

### 🛠️ Unity 里的设置

**LOD Group 组件参数：**

| 参数 | 含义 |
|---|---|
| `LOD 0 - 60%` | 物体占屏幕 60% 以上时用 LOD 0 |
| `LOD 1 - 30%` | 缩小到 30% 以下切到 LOD 1 |
| `LOD 2 - 10%` | 继续缩小到 10% 以下切到 LOD 2 |
| `Culled` | 更小就直接不渲染 |
| `Fade Mode` | None=硬切，Cross Fade=抖动过渡 |
| `Animate Cross-fading` | 勾选后固定 0.5 秒过渡，防止摄像机抖动导致反复闪烁 |

**⚠️ Quality Settings 里的 `LOD Bias = 2.0`：**
```
会把所有门槛缩小一半！
LOD 0 的 60% → 实际触发是 30%（更早保持高精度）
想按设定值测试 → 临时把 Bias 改成 1.0
```

---

### 🎨 Cross-Fade 过渡效果原理

**没有 Cross-Fade**：两个 LOD 级别之间直接硬切换（突变，视觉跳跃）

**开启 Cross-Fade 后**：
```
过渡区间内同时渲染两个 LOD 级别
LOD 0 淡出：用噪声图案挖掉越来越多的像素（像纸被虫吃穿）
LOD 1 淡入：用互补像素填进来（像另一张纸从下面穿上来）

LOD 0 的像素：  ■□■□■□□■
LOD 1 的像素：  □■□■□■■□
两者叠加 =     ■■■■■■■■（完整画面）
```

这种**每像素交替显示**的花纹技术叫做**抖动（Dither）**。

---

### 💻 代码实现

**Step 1：Lit.shader 两个 Pass 都加关键词**
```hlsl
// CustomLit Pass 和 ShadowCaster Pass 都要加！
#pragma multi_compile _ LOD_FADE_CROSSFADE
```

**Step 2：Common.hlsl 实现 ClipLOD 函数**
```hlsl
void ClipLOD(float2 positionCS, float fade) {
    #if defined(LOD_FADE_CROSSFADE)
        float dither = InterleavedGradientNoise(positionCS.xy, 0);
        // fade > 0：正在淡出（从1减到0）
        // fade < 0：正在淡入（从-1增到0）
        clip(fade + (fade < 0.0 ? dither : -dither));
    #endif
}
```

**Step 3：两个 Pass 的片元函数最开始调用**
```hlsl
// LitPassFragment 最开始
ClipLOD(input.positionCS.xy, unity_LODFade.x);

// ShadowCasterPassFragment 最开始
ClipLOD(input.positionCS.xy, unity_LODFade.x);
```

> ⚠️ 注意：是 `unity_LODFade.x`，不是 `input.depth`！LOD 淡出因子由 Unity 自动填入全局变量，不需要通过 Varyings 传递。

---

### 🔬 InterleavedGradientNoise 原理

**函数签名：**
```hlsl
float InterleavedGradientNoise(float2 positionCS, float offset)
//                                     ↑屏幕像素坐标   ↑时间偏移（0=不随时间变化）
```

**核心：根据屏幕像素位置生成 0~1 的伪随机数**

```
像素(0,0) → 0.73   像素(1,0) → 0.21
像素(0,1) → 0.56   像素(1,1) → 0.89
...相邻像素差异很大，但整体分布均匀
```

**怎么用来"挖洞"：**
```
clip(fade - dither)

fade = 0.5 时：
  某像素 dither = 0.3 → 0.5 - 0.3 = 0.2 > 0 → 保留 ✅
  某像素 dither = 0.7 → 0.5 - 0.7 = -0.2 < 0 → 丢弃 ❌（这个像素消失！）

fade → 0：越来越多像素被丢弃（逐渐消失）
fade → 1：越来越少像素被丢弃（逐渐出现）
```

**为什么不用普通随机数：**
```
普通随机：每帧结果不同 → 滴答闪烁，雪花屏效应 ❌
IGN：同一像素每帧结果相同 → 稳定，不闪烁 ✅
（除非手动传入时间 offset）
```

**这个函数在本教程的复用：**
- 第四章：半透明物体的阴影（Dithered Shadows）
- 第七章：LOD Cross-Fade（本节）

---

> 进度：LOD 部分完成 ✅

---

## 第二部分：反射（Reflections）

### 🤔 为什么金属球是黑色的？

```
金属材质的物理特性：
  漫反射 ≈ 0（几乎不散射，光线全部镜面反射）
  镜面反射颜色 = 材质自身颜色

以前的代码：
  color = gi.diffuse × brdf.diffuse   ← 金属的 brdf.diffuse ≈ 0 → 黑色！
        + 实时灯光 for 循环           ← 只有灯光直射时才有高光

  没有采样"镜子里的环境" = 镜子里什么都没有 = 全黑！
```

**金属球的颜色完全来自"它反射的环境"，不添加环境采样就永远是黑色。**

---

### 🌍 unity_SpecCube0 从哪来？

```
情况1：场景里没有 Reflection Probe
  → unity_SpecCube0 = 天空盒 CubeMap（默认兜底）

情况2：场景里有 Reflection Probe
  → Unity 找距离当前物体最近/最重要的探针
  → 把那个探针烘焙好的 CubeMap 绑定到 unity_SpecCube0
  → 不同物体可以用不同探针！

PerObjectData.ReflectionProbes 这个 flag 的作用：
  告诉 Unity 渲染时把"最近探针的 CubeMap"传给每个物体的 GPU 数据
  没有它 → unity_SpecCube0 只有天空盒，探针没效果
  有了它 → 每个物体精准使用最近的反射探针
```

---

### 🪞 SampleEnvironment 完整逐行解析

```hlsl
float3 SampleEnvironment(Surface surfaceWS, BRDF brdf) {
    
    // ① 计算反射方向（3D 方向向量）
    float3 uvw = reflect(-surfaceWS.viewDirection, surfaceWS.normal);
    // 就像光打镜子：入射角=出射角
    // 摄像机射来的方向 → 关于法线镜像 → 得到"球朝哪个方向看到环境"
    
    // ② 粗糙度 → mip 级别
    float mip = PerceptualRoughnessToMipmapLevel(brdf.perceptualRoughness);
    // CubeMap 有多个模糊版本：mip=0最清晰，mip=8极度模糊
    // 粗糙度高 → mip 高 → 采样模糊版本（模拟散射效果）
    
    // ③ 按方向和模糊级别采样 CubeMap
    float4 environment = SAMPLE_TEXTURECUBE_LOD(
        unity_SpecCube0, samplerunity_SpecCube0, uvw, mip
    );
    // uvw 是方向（不是普通 UV！），CubeMap 用方向采样
    
    // ④ 解码 HDR（天空盒可能比 0~1 更亮）
    return DecodeHDREnvironment(environment, unity_SpecCube0_HDR);
    // unity_SpecCube0_HDR 存了压缩格式和亮度倍率元数据
}
```

---

### 📐 两种"镜面"的区别

| | 直接光高光 | 间接环境反射 |
|---|---|---|
| **代码位置** | `Lighting.hlsl SpecularStrength()` | `GI.hlsl SampleEnvironment()` |
| **计算方式** | 半程向量 `H = normalize(L+V)` | `reflect(-V, N)` |
| **结果** | 灯光照到的一个亮斑 | 整个环境的镜像倒影 |
| **是否贵** | 实时计算，有一定消耗 | CubeMap采样，极其便宜 |
| **数据来源** | 实时灯光方向 | 预烘焙 CubeMap |

---

### 🌊 菲涅尔效应（Fresnel）

**现象**：任何表面从掠射角看都会变成镜面

```
正面看湖水 → 透明，能看到水下 → 弱反射
斜角看湖水 → 完整的天空倒影 → 强反射

用 Pow4(1 - dot(N, V)) 近似：
  N·V ≈ 1（正面看）→ Pow4(0) = 0 → 无额外反射
  N·V ≈ 0（掠射角）→ Pow4(1) = 1 → 最强反射
```

```hlsl
float fresnelStrength = surface.fresnelStrength *
    Pow4(1.0 - saturate(dot(surface.normal, surface.viewDirection)));

float3 reflection = specular * lerp(brdf.specular, brdf.fresnel, fresnelStrength);
// 正面（fresnelStrength=0）→ 用材质自身的镜面颜色
// 掠射角（fresnelStrength=1）→ 颜色趋向 brdf.fresnel（接近白色）
// 效果：边缘出现白色/亮色的光晕
```

**`_Fresnel` 滑条**（我们加在材质面板上的）：
```
1.0 → 完整菲涅尔效果（写实场景）
0.0 → 关闭菲涅尔（水下、特殊情况时用）
```

---

### 🔧 改动了哪些文件

| 文件 | 改动 |
|---|---|
| `GI.hlsl` | 加 `TEXTURECUBE unity_SpecCube0`，加 `gi.specular` 字段，加 `SampleEnvironment()` |
| `BRDF.hlsl` | 结构体加 `perceptualRoughness`/`fresnel`，加 `IndirectBRDF()` |
| `Lighting.hlsl` | `GetLighting()` 改用 `IndirectBRDF()` |
| `Surface.hlsl` | 加 `fresnelStrength` 字段 |
| `LitPass.hlsl` | 填 `surface.fresnelStrength`，`GetGI` 传 `brdf` |
| `LitInput.hlsl` | 加 `_Fresnel` + `GetFresnel()` |
| `UnlitInput.hlsl` | 加虚假 `GetFresnel()` 存根（返回0）|
| `UnityInput.hlsl` | 加 `unity_SpecCube0_HDR` |
| `CameraRenderer.cs` | `perObjectData` 加 `ReflectionProbes` |

---

> 🎉 第七章 LOD + 反射全部完成！下一章：Complex Maps（法线贴图、遮挡贴图等）
