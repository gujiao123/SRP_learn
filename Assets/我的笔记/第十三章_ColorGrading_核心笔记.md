# 第十三章 Color Grading — 核心笔记

---

## 本章目标

```
在 Bloom + ToneMapping 的基础上，加入颜色分级（Color Grading）
最终流水线：

场景渲染（HDR）
    ↓
Bloom（发光光晕）
    ↓
Color Grading（颜色调整，本章新增）
  曝光 / 对比度 / 颜色滤镜 / 色相 / 饱和度
  白平衡 / 分离映射 / 通道混合 / 阴影中间调高光
    ↓
Tone Mapping（HDR → LDR）
    ↓
屏幕输出
```

---

## 前置知识：美术

### 一、色彩空间

```
RGB: 三通道各自独立，技术计算用
  (1.0, 0.5, 0.0) = 橙红色

HSV: 更直观，Hue Shift 在这个空间做
  H = 色相（0~360°，颜色种类）
  S = 饱和度（0=灰，1=纯色）
  V = 亮度

  纯红 → H=0°, S=1, V=1
  色相旋转 H+120° → 变绿（颜色种类改变了）
  降低饱和度 S=0 → 全变灰^

LMS: 模拟人眼三种锥细胞（Long/Medium/Short 波长）
  白平衡在这个空间做，更接近人眼感知
```

### 二、分离映射（Split Toning）

```
经典电影色调：
  暗部（阴影）→ 冷蓝/绿蓝  「恐惧、冷漠、疏离感」
  亮部（高光）→ 暖橙/金黄  「温暖、希望、活力感」

  代表：Teal（青绿）vs Orange（橙）

几乎所有好莱坞大片都使用这种配色
原理：暗部染蓝 + 亮部染橙 → 色温反差 → 画面层次感强
```

### 三、LogC 对数空间

```
线性空间：亮度 0~1 均匀分布
  问题：暗处只有少量格子 → 对比度调整时暗处细节消失

LogC 空间：暗处格子密，亮处格子疏（类似人眼感知）
  好处：调对比度时暗处也能保留细节

对比度为什么在 LogC 做？
  LINEAR → LogC（LinearToLogC）→ 做对比度 → LogC → LINEAR（LogCToLinear）
  中间步把亮度分布改成对数，再做线性缩放，结果保留暗部
```

---

## 前置知识：技术

### 四、Channel Mixer 矩阵乘法

```
本质是 3×3 矩阵乘法：

[ R' ]   [ r.x  r.y  r.z ] [ R ]
[ G' ] = [ g.x  g.y  g.z ] [ G ]
[ B' ]   [ b.x  b.y  b.z ] [ B ]

默认（不改变颜色）= 单位矩阵：
  r = (1, 0, 0)
  g = (0, 1, 0)
  b = (0, 0, 1)

例子：把 r.y 设为 0.5 → R' = R + 0.5G
  红里加了50%的绿 → 偏黄（红+绿=黄）

HLSL 实现：
  return mul(float3x3(red, green, blue), color);
```

### 五、smoothstep

```
smoothstep(edge0, edge1, x) 返回平滑过渡值：
  x ≤ edge0 → 0.0
  x ≥ edge1 → 1.0
  之间      → 平滑 S 形曲线（一阶导数为0在边界）

Shadows/Midtones/Highlights 用它判断归属：
  shadowWeight    = 1 - smoothstep(shadowStart, shadowEnd, luminance)
  highlightWeight = smoothstep(highlightStart, highlightEnd, luminance)
  midtoneWeight   = 1 - shadowWeight - highlightWeight

效果：像素按亮度平滑分配到三个区域，不是硬切换
```

### 六、LUT（Look Up Table，查找表）— 本章核心技术

```
问题：每像素都做 8 种颜色调整 → 性能极差

解决：把颜色映射关系烘焙成查找表
  输入颜色 (r, g, b) → 查表 → 输出颜色 (r', g', b')
  等于把8步计算压成1次纹理采样

LUT 结构：
  逻辑上是 3D 纹理：32×32×32
  存储上是宽 2D 贴图：1024×32（32个切片横向排列）

  1024 = 32 × 32（水平方向 = 切片数 × 切片宽度）
  32   = 32（垂直方向 = 切片高度）

工作流：
  ① 渲染 LUT（只有 32768 像素，很快）
       对每个可能的颜色坐标，计算调整后的颜色
       把结果写入 LUT 贴图

  ② 真正渲染时（每帧全屏）
       只做 1 次 LUT 采样
       代替了 8 种调整的所有计算

性能对比：
  无 LUT：每像素 × 8种调整的计算量
  有 LUT：每像素 × 1 次纹理采样（GPU 极快）
```

---

## 颜色调整工具汇总

### ColorAdjustments（5种旋钮）

| 调节项 | Shader 实现 | 范围 |
|---|---|---|
| **Post Exposure** | `color *= pow(2, exposure)` | 任意float（stop） |
| **Contrast** | 在 LogC 空间做 `(color-midGray)*contrast+midGray` | -100~100 |
| **Color Filter** | `color *= filterColor` | HDR颜色 |
| **Hue Shift** | RGB→HSV→H偏移→HSV→RGB | -180~180° |
| **Saturation** | `luminance + (color-luminance)*saturation` | -100~100 |

```
C# 传给 Shader 的方式（打包成 Vector4）：
  _ColorAdjustments.x = pow(2, postExposure)     曝光（2的幂次）
  _ColorAdjustments.y = contrast*0.01+1          对比度（映射到0~2）
  _ColorAdjustments.z = hueShift*(1/360)         色相（映射到-1~1）
  _ColorAdjustments.w = saturation*0.01+1        饱和度（映射到0~2）

  _ColorFilter = colorAdjustments.colorFilter.linear  // 线性空间颜色
```

### ColorGrade 函数调用顺序

```hlsl
float3 ColorGrade(float3 color, bool useACES = false) {
    color = min(color, 60.0);              // 限制极值（后来在 LUT 里删掉）
    color = ColorGradePostExposure(color); // 1. 曝光
    color = ColorGradeWhiteBalance(color); // 2. 白平衡（LMS空间）
    color = ColorGradingContrast(color, useACES); // 3. 对比度（LogC/ACEScc）
    color = ColorGradeColorFilter(color);  // 4. 颜色滤镜
    color = max(color, 0.0);              // 去负数
    color = ColorGradeSplitToning(color, useACES); // 5. 分离映射
    color = ColorGradingChannelMixer(color); // 6. 通道混合（3×3矩阵）
    color = max(color, 0.0);
    color = ColorGradingShadowsMidtonesHighlights(color, useACES); // 7. SMH
    color = ColorGradingHueShift(color);   // 8. 色相偏移
    color = ColorGradingSaturation(color, useACES); // 9. 饱和度
    return max(useACES ? ACEScg_to_ACES(color) : color, 0.0);
}
```

---

## ACES 模式下的特殊处理

```
普通模式（Neutral/Reinhard/None）：
  颜色操作全在线性空间 → 简单

ACES 模式：
  Post Exposure + White Balance → 线性空间
  Contrast 开始 → 转换到 ACEScc 空间（比 LogC 更适合ACES流程）
  之后操作 → ACEScg 空间
  最终 → 转回 ACES 空间 → AcesTonemap

  好处：和专业电影 VFX 管线一致，颜色分级效果更准确

  关键转换函数：
    unity_to_ACES    → 线性 → ACES
    ACES_to_ACEScc   → ACES → ACEScc（对数感知空间）
    ACEScc_to_ACES   → ACEScc → ACES
    ACES_to_ACEScg   → ACES → ACEScg（线性工作空间）
    ACEScg_to_ACES   → ACEScg → ACES（最终）
```

---

## LUT 实现细节

### LUT 分辨率

```csharp
// CustomRenderPipelineAsset.cs
public enum ColorLUTResolution { _16 = 16, _32 = 32, _64 = 64 }
// 通常 32 就够，banding 明显时用 64
```

### LUT 纹理尺寸

```
colorLUTResolution = 32
lutHeight = 32
lutWidth  = 32 × 32 = 1024

GetTemporaryRT(colorGradingLUTId, 1024, 32, 0,
    FilterMode.Bilinear, RenderTextureFormat.DefaultHDR)
```

### LUT 参数向量

```
烘焙 LUT 时：_ColorGradingLUTParameters = (lutHeight, 0.5/lutWidth, 0.5/lutHeight, lutHeight/(lutHeight-1))
应用 LUT 时：_ColorGradingLUTParameters = (1/lutWidth, 1/lutHeight, lutHeight-1)
```

### LogC 模式（HDR支持）

```
LUT 默认覆盖 0~1 范围
开启 _ColorGradingLUTInLogC：把切片坐标解释为 LogC → 覆盖到 ~60

bool _ColorGradingLUTInLogC;
// 开启条件：useHDR && 有 ToneMapping（不是 None 模式）
buffer.SetGlobalFloat(colorGradingLUTInLogId,
    useHDR && pass != Pass.ColorGradingNone ? 1f : 0f);
```

### LUT 如何处理 HDR 曝光（核心问题 + 解决方案）

#### 核心问题

```
LUT 的 UV 坐标只能是 0~1
但 HDR 场景的像素颜色可以是 (8.0, 7.0, 2.0) 这样的超亮值

如果没有特殊处理：
  (8.0, 7.0, 2.0) → saturate() → (1.0, 1.0, 1.0)
  所有亮度 ≥ 1 的像素全部被当成"纯白"查同一个格子！
  → ToneMap 曲线完全失效，爆亮的太阳和普通白墙无法区分
```

#### LogC 压缩原理

```
LogC 是对数编码，把线性 0~59 的大范围压缩进 0~1：

  线性亮度    LogC值      说明
    0.0   →   ~0.09
    1.0   →   ~0.39      ← LDR 上限（以前 LUT 只到这里）
    4.0   →   ~0.55
   18.0   →   ~0.70
   59.0   →   ~0.90      ← HDR 上限，LUT 现在能覆盖到这里

结论：LUT 的 32 个切片不再代表"0~1 线性亮度"
      而是代表"0~59 的全 HDR 亮度范围"
      极亮像素（太阳、爆炸）也有专属格子，ToneMap 正确生效
```

#### 完整流程（数字例子：太阳光像素 = (8.0, 7.0, 2.0)）

```
【第一步：烘焙 LUT（只执行一次，32768 个格子）】

每个格子的 UV 坐标先被 LogCToLinear 解压成 HDR 颜色：
  UV 坐标 (0.72, 0.71, 0.53)
      ↓ LogCToLinear()
  Linear HDR (8.0, 7.0, 2.0)   ← 这个格子负责处理这个亮度！
      ↓ ColorGrade()（曝光/白平衡/对比度等 9 种调整）
  调整后 ≈ (8.2, 7.1, 2.1)
      ↓ AcesTonemap()（HDR → LDR）
  输出 ≈ (0.97, 0.95, 0.75)   ← 亮橙黄，非常亮但没有过曝

  这个结果写入 LUT 格子 (0.72, 0.71, 0.53)，永远存在那里


【第二步：Final Pass 查 LUT（每帧每像素）】

原图 HDR 像素 (8.0, 7.0, 2.0)
    ↓ LinearToLogC()  把 HDR 压缩回 0~1
  (0.72, 0.71, 0.53)
    ↓ 查 LUT 贴图（格子已经烘焙好了！）
  (0.97, 0.95, 0.75)   ← 正确的亮橙黄色
    ↓ 输出到屏幕
```

#### 两个像素对比（LDR 模式 vs HDR 模式）

| 像素 | LDR 模式（无 LogC） | HDR 模式（开启 LogC） |
|---|---|---|
| 太阳光 **(8.0, 7.0, 2.0)** | 查 UV(1,1,1) ❌ 当成纯白 | 查 UV(0.72,0.71,0.53) ✅ |
| 白墙 **(1.0, 1.0, 1.0)** | 查 UV(1,1,1) ❌ 和太阳同格 | 查 UV(0.39,0.39,0.39) ✅ |
| 灰色 **(0.5, 0.5, 0.5)** | 查 UV(0.5,0.5,0.5) ✅ | 查 UV(0.27,0.27,0.27) ✅ |

```
LDR 模式：亮度 ≥ 1 的像素全部挤在同一个格子 (1,1,1)
HDR 模式：0~59 的全部亮度均匀分配到 32 个格子，每个亮度有自己的格子
```

#### 对应 Shader 代码

```hlsl
// ——— 烘焙阶段：UV 坐标 → 解压成 HDR → ColorGrade ———
float3 GetColorGradedLUT(float2 uv, bool useACES = false) {
    float3 color = GetLutStripValue(uv, _ColorGradingLUTParameters);
    // LogC 模式：把 LUT 坐标(0~1) 解读为 LogC → 展开成 HDR(0~59)
    return ColorGrade(_ColorGradingLUTInLogC ? LogCToLinear(color) : color, useACES);
}

// ——— 应用阶段：HDR 原图像素 → 压缩到 LogC → 查表 ———
float3 ApplyColorGradingLUT(float3 color) {
    return ApplyLut2D(
        TEXTURE2D_ARGS(_ColorGradingLUT, sampler_linear_clamp),
        // 把 HDR 像素先压缩到 LogC(0~1)，匹配烘焙时的坐标系
        saturate(_ColorGradingLUTInLogC ? LinearToLogC(color) : color),
        _ColorGradingLUTParameters.xyz  // (1/w, 1/h, h-1)
    );
}
```

#### 开启条件

| 情况 | _ColorGradingLUTInLogC | 原因 |
|---|---|---|
| LDR 相机 | `false` | 不需要，根本没有超过 1 的颜色 |
| HDR + ToneMapping = None | `false` | 不做 ToneMap，HDR 直接透传，不需要扩展 |
| **HDR + 任意 ToneMap** | **`true`** | **必须覆盖 0~59，否则高亮区 ToneMap 失效** |

---

## Pass 枚举变化（12章 → 13章）

```
12章 Pass 枚举：          13章改名后：
  ToneMappingACES     →   ColorGradingACES
  ToneMappingNeutral  →   ColorGradingNeutral
  ToneMappingNone     →   ColorGradingNone（新增，含ColorGrade但不ToneMap）
  ToneMappingReinhard →   ColorGradingReinhard
                      +   Final（新增，应用LUT到原图写屏幕）

ToneMappingSettings.Mode 枚举变化：
  12章：{None=-1, ACES=0, Neutral=1, Reinhard=2}
  13章：{None=0, ACES=1, Neutral=2, Reinhard=3}  ← 从0开始，加了None自己的Pass
```

---

## 代码改动文件清单

```
PostFXSettings.cs       加 5 个设置结构体 + using System
CustomRenderPipelineAsset.cs  加 ColorLUTResolution 枚举
CustomRenderPipeline.cs 透传 colorLUTResolution
CameraRenderer.cs       透传 colorLUTResolution 给 PostFXStack.Setup
PostFXStack.cs          DoToneMapping 改名
                        加 Configure* 方法（白平衡/分离映射/通道混合/SMH）
                        LUT 生成逻辑
                        加 using static PostFXSettings
PostFXStackPasses.hlsl  加 ColorGrade() 大函数（含9种调整）
                        所有 Pass 改名（ToneMapping→ColorGrading）
                        加 GetColorGradedLUT()
                        加 FinalPassFragment
PostFXStack.shader      Pass 重命名 + 加 Final Pass
```

---

## Inspector 界面中文对照

### Color Adjustments（颜色调整）

| 英文 | 中文 | 效果 | 范围 |
|---|---|---|---|
| Post Exposure | 后期曝光 | 整体亮暗，不影响Bloom | 任意float |
| Contrast | 对比度 | 正→暗更黑亮更白；负→灰蒙蒙 | -100~100 |
| Color Filter | 颜色滤镜 | 整图乘以该颜色（白=不变） | HDR颜色 |
| Hue Shift | 色相旋转 | 所有颜色在色轮上整体旋转 | -180~180 |
| Saturation | 饱和度 | 正→鲜艳；负→褪色；-100=黑白 | -100~100 |

### White Balance（白平衡）

| 英文 | 中文 | 效果 |
|---|---|---|
| Temperature | 色温 | 负→冷蓝（冬/科幻）；正→暖黄（夕阳/温馨） |
| Tint | 色调偏移 | 负→偏绿（荧光灯）；正→偏洋红（晨光） |

### Split Toning（分离映射）

| 英文 | 中文 | 说明 |
|---|---|---|
| Shadows | 暗部色调 | 暗处染色（LDR颜色，柔和混合） |
| Highlights | 亮部色调 | 亮处染色 |
| Balance | 平衡点 | 负→阴影区更大；正→高光区更大 |

### Channel Mixer（通道混合）

| 英文 | 中文 | 说明 |
|---|---|---|
| Red X/Y/Z | 红通道 输入RGB权重 | 新R = R*X + G*Y + B*Z |
| Green X/Y/Z | 绿通道 输入RGB权重 | 新G = R*X + G*Y + B*Z |
| Blue X/Y/Z | 蓝通道 输入RGB权重 | 新B = R*X + G*Y + B*Z |

### Shadows Midtones Highlights（阴影/中间调/高光）

| 英文 | 中文 | 说明 |
|---|---|---|
| Shadows/Midtones/Highlights | 暗/中/亮 颜色 | HDR颜色，白=不变 |
| Shadows Start/End | 阴影范围起/终 | 亮度在此范围内被判为阴影 |
| Highlights Start/End | 高光范围起/终 | 亮度在此范围内被判为高光 |

---

## Channel Mixer 使用场景

```
普通调色只能整体调，Channel Mixer 能让颜色之间互相影响：

例：草地枯黄化（不影响天空）：
  Green 通道 → Y设为0.5：G' = 0*R + 0.5*G + ...
  绿色里混入红 → 草地偏黄褐
  蓝天不含绿分量，不受影响

常见用途：
  草地枯黄     → 调 Green 通道加红
  水体颜色调整 → 调 Blue 通道
  皮肤色调整   → 调 Red 通道（皮肤含大量红色成分）
  夜视仪效果   → 只保留绿(0,1,0)，Red/Blue 全清零
  黑金风格     → 压蓝 + 压绿，金色（红绿混合）凸显
```

---

## Split Toning vs SMH 对比

| 维度 | Split Toning | Shadows Midtones Highlights |
|---|---|---|
| **区域数量** | 2（暗/亮） | 3（暗/中/亮） |
| **颜色强度** | LDR（柔和） | HDR（强烈） |
| **区域边界** | 固定不可调 | 4个滑条自由配置 |
| **混合方式** | SoftLight（胶片感） | 直接相乘（线性精准） |
| **学习曲线** | 简单，拖两个颜色 | 较复杂，需理解亮度范围 |
| **适合场景** | 快速风格化 | 精细三段调色 |

```
选哪个？
  快速做电影感 → Split Toning（暗蓝亮橙，5秒搞定）
  精准控制某亮度区域的颜色 → SMH（专业调色工作流）
  
  两者并不互斥，可以同时用
```

---

## 快速上手调色建议（从简单开始）

```
1. Saturation -100      → 黑白（最直观的效果）
2. Contrast +50         → 层次感增强
3. Temperature -50      → 冷蓝科幻风格
4. Split Toning:        → 经典电影感
   Shadows = 冷蓝
   Highlights = 暖橙
5. Post Exposure        → 根据场景微调亮度
```

---

## 第13章完成验证清单

```
✅ Post FX Settings Inspector 出现5组新设置
✅ Saturation 拖到 -100 → 画面变黑白
✅ Temperature 拖到极端 → 画面冷暖色调变化
✅ Split Toning Shadows蓝/Highlights橙 → 电影感
✅ Frame Debugger → 能看到 Color Grading LUT RT（1024×32 宽贴图）
✅ Frame Debugger → 能看到 Final Pass（应用LUT到原图）
✅ 切换 ToneMapping 模式 → 效果变化（ACES最有电影感）
```

---

## 后处理通用思想

### 代码形式上都一样，关键在数据理解

```
后处理代码永远是这个模式：

【C# 端】
  读设置值（用户拖的滑条）
    ↓  "人类友好" → "GPU友好" 的换算（重要！）
  SetGlobalXXX 传给 Shader
    ↓  申请中间贴图（如果需要）
  Draw(from, to, pass) 触发 GPU

【Shader 端】
  UV → 采样/用全局变量 → 对 color 做函数变换 → 输出

区别只在"对 color 做什么函数"
```

---

## 颜色数据的本质理解

```
一个像素的颜色是一个 float3 (R, G, B)

线性空间下：
  (0, 0, 0) = 纯黑
  (1, 1, 1) = 白（LDR上限）
  (3, 2, 1) = HDR暖光色（超出显示范围，需ToneMap）

颜色的"成分"：
  纯红 = (1, 0, 0) → R高，G/B低
  纯绿 = (0, 1, 0)
  纯蓝 = (0, 0, 1)
  黄 = 红+绿 = (1, 1, 0)
  紫 = 红+蓝 = (1, 0, 1)
  青 = 绿+蓝 = (0, 1, 1)
  
  这就是为什么：
    Channel Mixer Red.y=0.5 → R' = R + 0.5G → 红里加绿 → 偏黄
    分离映射暗部蓝 = 把暗处 color 往 (0,0,1) 方向推
```

---

## 自定义调色的思路框架

### "我想要 X 效果 → 我应该操作什么"

```
想整体变亮/暗
→ 曝光（乘法，×2^x）或 Reinhard ToneMapping 白点调整

想暗处更暗、亮处更亮（层次感）
→ 对比度（以midGray为轴的线性缩放）

想整体偏某种颜色（比如整体偏绿）
→ Color Filter（直接乘以目标颜色）
  或 White Balance（更自然的冷暖偏移）

想让某种颜色变成另一种（比如绿变红）
→ 色相旋转（Hue Shift，色轮旋转）
  或 Channel Mixer（精确重组RGB分量）

想暗处一种色调、亮处另一种（电影感）
→ Split Toning（简单快速）
  或 SMH Shadows/Highlights（精确分区）

想让某个亮度区域颜色不同
→ SMH（自定义边界的三区域调色）

想让画面去色（黑白）
→ Saturation = -100（等价于只保留 luminance）

想让高光不过曝（控制亮部）
→ ToneMapping（Reinhard/ACES/Neutral）
  或 SMH Highlights 颜色降低
```

---

## 数学层面的几个核心操作

```
乘法（Multiply）：整体增强/削弱某分量
  color *= factor
  用于：曝光、颜色滤镜、饱和度

加法（Add）：整体偏移
  color += offset
  用于：少量亮度提升（提升阴影细节）

线性插值（Lerp）：在两个颜色/状态之间过渡
  lerp(a, b, t)  = a*(1-t) + b*t
  用于：散射Bloom、Split Toning混合

以轴点缩放（Contrast-like）：
  output = (input - pivot) * scale + pivot
  pivot = 中间灰（不变的点）
  scale > 1 = 放大差异（对比度增强）
  scale = 0 = 所有值坍缩到 pivot（纯灰）
  用于：对比度、饱和度（luminance为轴）

矩阵乘（Matrix Multiply）：
  output = M × input
  用于：Channel Mixer、颜色空间转换（线性→LMS→ACES等）
```

---

## 颜色空间的坑

```
坑1：在错误的空间做操作
  问题：对比度在线性空间做 → 暗部细节全没了
  规律：感知相关操作（对比度/饱和度）要在对数/感知空间做
         线性操作（曝光/滤镜）可以在线性空间做

坑2：忘记 gamma 转换
  C# 里 Color GUI 默认是 gamma 空间
  传给 Shader 要用 .linear 转换
    color.colorFilter.linear  ← 必须加 .linear
  忘记加 → 颜色偏暗/偏亮（不对）

坑3：负数颜色没有去掉
  对比度拉高 → 暗部可能变负数
  负数颜色在后续操作（如 SoftLight、HSV转换）会产生错误
  固定做法：每个阶段后 color = max(color, 0.0)

坑4：LDR/HDR 范围搞混
  LDR 操作：用 saturate() 限在 0~1
  HDR 操作：不能用 saturate()，用 max(x, 0.0)
  Split Toning 要 saturate(color) 是因为它在近似gamma空间工作
  LUT 采样输入要 saturate() 是因为 LUT 索引是 0~1

坑5：颜色空间转换顺序搞混（ACES模式）
  一定要按这个顺序：
    线性 → unity_to_ACES → ACES_to_ACEScc （做对比度）
            → ACEScc_to_ACES → ACES_to_ACEScg （后续操作）
            → ACEScg_to_ACES （最终）→ AcesTonemap
  搞错顺序 → 颜色偏差很大
```

---

## 实现新后处理效果的思路步骤

```
Step 1：想清楚"数学上要对 color 做什么"
  我要让画面柔和 → 对比度降低 → (color-pivot)*0.8+pivot
  我要让夜晚场景更蓝 → Color Filter 设置为蓝色？还是 Temperature？
  我要做热成像 → 只保留 luminance，然后用 luminance 采样一个渐变色

Step 2：决定在哪个颜色空间做
  感知相关 → LogC 或 ACEScc
  颜色混合 → 线性
  白平衡 → LMS
  基本上能在线性做就在线性做，不确定就先试线性

Step 3：C# 端把用户参数换算成 GPU参数
  确保默认值无效果（乘法默认1，加法默认0，lerp默认0或1）
  确保范围合理（不要让用户能输入产生NaN的值）

Step 4：Shader 写函数，最后记得 max(color, 0)
  先单独测试，Insert 到 ColorGrade 的合适位置

Step 5：加入 LUT 管线（已有），自动享受性能优化
```

---

## 最重要的认知

```
后处理的本质 = 对每个像素的 float3 (R,G,B) 做数学变换

所有视觉效果 = 数学函数的组合：
  更亮 = 乘大数
  更暗 = 乘小数
  改颜色 = 调整各分量的比例
  改对比 = 以某点为轴放大差异
  改色彩风格 = 上面所有的组合

颜色感觉 → 找到对应的数学操作 → 决定空间 → 写函数
这就是自己做颜色调整的全部思路
```
