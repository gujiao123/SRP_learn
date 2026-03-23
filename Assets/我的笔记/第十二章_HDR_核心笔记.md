# 第十二章 HDR — 核心笔记

---

## 前置知识：色彩与光照基础

---

### 一、显示器与颜色存储

```
显示器每个像素有 RGB 三盏灯，亮度范围固定：
  最暗 = 0（关灯）
  最亮 = 1（全功率）

存储格式（LDR）：每通道 8 位 = 0~255 整数
  255 → 1.0（最亮）
  128 → 0.5（中等）
  0   → 0.0（最暗）

这就是 LDR（Low Dynamic Range）
```

---

### 二、sRGB vs 线性空间

**人眼对暗处更敏感，对亮处不敏感（对数感知）。**

```
如果用线性存储颜色（0~255 均匀分配）：
  暗处只有几个值 → 出现色阶断层，人眼能看出来
  亮处浪费大量位数存人眼几乎感知不到的差异

sRGB 的做法（解决方案）：
  存储时做 gamma 压缩（暗处占更多位数）→ sRGB ≈ linear^(1/2.2)
  读取时做 gamma 展开（还原为线性光量）→ linear ≈ sRGB^2.2
```

**Unity 里 Shader 在线性空间工作，GPU 自动在 sRGB RT 读写时做转换。**

---

### 三、颜色（Albedo）vs 光照强度

| 概念 | 范围 | 含义 |
|---|---|---|
| **Albedo（反射率）** | 0~1 | 物体反射光的比例，1.0 = 100% 反射，物理上不能超1 |
| **光照强度（Intensity）** | 0~∞ | Unity 中任意单位，没有物理上限 |
| **最终像素值** | 0~∞（HDR） | albedo × lightColor × intensity × NdotL |

```
例子：
  albedo       = (1.0, 0.5, 0.0)  橙色物体
  lightColor   = (1.0, 1.0, 1.0)  白色光
  lightIntensity = 10             很亮的光
  NdotL        = 0.8              光线与法线夹角较小

基础光照计算（Diffuse）：
finalColor = albedo × lightColor × lightIntensity × NdotL

finalColor = (1.0×10×0.8, 0.5×10×0.8, 0.0×10×0.8) = (8.0, 4.0, 0.0)
  ↑ 超过1了！这是 HDR 值
```

```
像素值        视觉感受         典型来源
────────────────────────────────────────────
0.0          纯黑            背光面/阴影
0.05~0.3     阴暗处          漫反射 × 弱光
0.5          中灰            漫反射 × 正常光
1.0          标准白          较强光，LDR 上限
2~5          轻微发光        发光材质低强度
10~50        明显发光        强发光材质
100~1000     刺眼光源        灯泡/太阳直射面
```

---

### 四、LDR vs HDR 存储格式

**不是读取方式不同，是物理比特位数不同：**

```
LDR 格式：B8G8R8A8（每通道 8 位整数）
  范围：0~255 = 0.0~1.0
  大小：32 bits = 4 bytes / 像素
  限制：GPU 写入时强制 clamp，写 5.0 进去存成 1.0，数据丢失

HDR 格式：R16G16B16A16_SFloat（每通道 16 位浮点）
  范围：IEEE 754 半精度浮点，最大约 65504
  大小：64 bits = 8 bytes / 像素（是LDR的两倍）
  特点：无 clamp，写 5.0 存 5.0，完整保留
```

```
文件格式对应：
  LDR 图片：PNG / JPG  → 8-bit，范围 0~1
  HDR 图片：.hdr / .exr → 16-bit float，范围无上限
```

---

### 五、HDR 引入的三个问题

#### 问题1：萤火虫（Fireflies）

```
场景里有一个像素亮度 = 100，周围全是 0.1

LDR 下：clamp 到 1，和周围亮度差 10 倍，Bloom 光晕自然
HDR 下：亮度是周围的 1000 倍！Bloom 后那个像素产生巨大光晕
         而且随相机移动，单像素忽大忽小 → 剧烈闪烁（萤火虫）

解决：BloomPrefilterFireflies — 按亮度加权平均，稀释萤火虫
```

#### 问题2：白色一片

```
HDR RT 里保留了所有层次（好！）
但最终必须输出到 LDR 显示器：> 1 的全 clamp 成 1

白墙（1.0）= 255（最亮）
发光体（3.0）= 255（也是最亮）  ← 无法区分！
太阳（200）= 255（也他妈255）

解决：Tone Mapping — HDR到LDR的非线性压缩
```

#### 问题3：Bloom additive 模式不守恒

```
Additive Bloom：lowRes + highRes → 叠加光 → 总亮度增加，不符合真实散射

解决：Scattering Bloom — lerp(highRes, lowRes, scatter) → 总能量守恒
```

---

### 六、Tone Mapping 本质

**线性调低 vs 非线性压缩的区别：**

```
线性调低 × 0.005：
  树叶（0.05）→ 0.00025   完全黑，看不见细节
  灯光（200） → 1.0        才勉强可见
  → 暗处全毁，只看得见极亮的东西

Reinhard（output = input / (input + 1)）：
  树叶（0.05）→ 0.048      几乎没变，细节保留
  白墙（1.0） → 0.5        中灰（牺牲白的感觉）
  发光体（3） → 0.75       浅灰（比白墙亮，能区分）
  灯光（200） → 0.995      接近白但和别的有区别
  → 暗处保留，亮处压缩层次
```

**关键洞察：**
```
"普通白"（1.0）被压成中灰（0.5）
这样 超亮物体 才能在 中灰和白之间展示层次
牺牲 "白就是最亮" 这个主观感受
换来 "能区分不同亮度等级" 的能力
```

**三种 Tone Mapping 模式：**

| 模式 | 公式/来源 | 特点 |
|---|---|---|
| **None** | 直接 Copy | 无映射，> 1 的 clamp |
| **Reinhard** | `c / (c + 1)` | 简单，白点趋向无穷 |
| **Neutral** | John Hable S形曲线 | URP/HDRP 同款，胶片感 |
| **ACES** | 学院色彩标准 | 超亮处色相偏移→白，电影感最强 |

---

## 本章架构改动

### Render() 流程变化

```
第11章：
  DoBloom(sourceId) → 直接写屏幕

第12章：
  DoBloom(sourceId)      → 写 bloomResultId（全分辨率新RT），返回 bool
  DoToneMapping(...)     → 读 bloomResult/sourceId，写屏幕
  ReleaseTemporaryRT(bloomResultId)
```

### RT 格式切换

```csharp
// CameraRenderer.Setup()
buffer.GetTemporaryRT(
    frameBufferId, width, height, 32, FilterMode.Bilinear,
    useHDR ? RenderTextureFormat.DefaultHDR   // R16G16B16A16_SFloat
           : RenderTextureFormat.Default       // B8G8R8A8
);

// PostFXStack.DoBloom()
RenderTextureFormat format = useHDR ?
    RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default;
```

---

## 代码改动详解

### PostFXSettings.cs 新增配置

```
BloomSettings 新增：
  fadeFireflies       → 萤火虫消除开关（开启时用更激进的预过滤Pass）
  Mode 枚举           → Additive（叠加）/ Scattering（散射）
  scatter             → 散射强度滑条（0.05~0.95）
  
ToneMappingSettings（新结构体）：
  Mode 枚举 { None=-1, ACES=0, Neutral=1, Reinhard=2 }
  ← None=-1 是设计：Pass = ToneMappingACES + (int)mode 的偏移计算依赖它
```

### PostFXStack.cs 核心变化

```csharp
// Render() — 11章 vs 12章
// 11章：DoBloom 直接写屏幕
// 12章：Bloom 写中间RT → ToneMapping 读中间RT → 写屏幕

public void Render(int sourceId) {
    if (DoBloom(sourceId)) {           // 有Bloom？
        DoToneMapping(bloomResultId);  // 对Bloom结果ToneMapping
        buffer.ReleaseTemporaryRT(bloomResultId); // 用完释放
    } else {
        DoToneMapping(sourceId);       // 无Bloom直接ToneMapping
    }
}

// DoBloom 返回 bool 的意义：
// true  = 有Bloom结果存在 bloomResultId 里，Render 继续做 ToneMapping
// false = Bloom被禁用，直接对原图 ToneMapping
```

```
scatter（散射强度）的两种用途：
  combinePass （中间层合并）：
    lerp(highRes, lowRes, scatter)  → 散射模式每层合并

  finalIntensity（最终强度）：
    Additive:   = bloom.intensity（无上限）
    Scattering: = min(bloom.intensity, 0.95f)  ← 上限0.95
                  原因：lerp比例=1时完全用模糊图，原图细节全消失

  散射最终Pass (BloomScatterFinal) 多了能量补偿：
    lowRes += highRes - ApplyBloomThreshold(highRes)
    把被阈值截断的暗部能量加回来，保证散射模式总能量守恒
```

### Pass 枚举对应关系（必须和 Shader 里 Pass 顺序完全一致）

```
Pass 枚举              Shader Pass 顺序
─────────────────────────────────────
BloomAdd              0
BloomHorizontal       1
BloomPrefilter        2
BloomPrefilterFireflies 3
BloomScatter          4
BloomScatterFinal     5
BloomVertical         6
Copy                  7
ToneMappingACES       8   ←── ACES+0
ToneMappingNeutral    9   ←── ACES+1
ToneMappingReinhard   10  ←── ACES+2
```

---

## 萤火虫消除数学

### 为什么普通预过滤会产生萤火虫

```
BloomPrefilter（旧）：直接采中心1个点
  极亮像素(亮度500)直接进入金字塔
  降采样：500 → 250 → 125 → 62 ...仍然很亮
  相机移动时单像素位置跳动 → 光晕闪烁 = 萤火虫
```

### 萤火虫消除 Pass 的采样策略

```
采样5个点：中心 + 四个斜对角（偏移1texel×2.0）

为什么×2？
  目标RT是半分辨率，UV坐标对应原图是2倍空间
  不×2的话斜方向采样点挤在中间，覆盖范围太小

为什么斜对角能读4个原图像素？
  Shader 写入的是 半分辨率RT
  读取的是 全分辨率源图
  半分辨率的UV坐标(0.1, 0.1)对应原图坐标(1.0, 1.0)
  = 正好落在原图两像素的边界上
  → GPU 双线性过滤自动读4个邻居像素加权平均
```

### 双线性过滤数学（GPU内置）

```
4个像素 A=1 B=3 C=2 D=4 排列：

  A ─── B
  |     |
  C ─── D

采样点 UV = (0.3, 0.4) （离A在x方向0.3，y方向0.4）

水平插值：
  E = A×0.7 + B×0.3 = 1.6   （上边）
  F = C×0.7 + D×0.3 = 2.6   （下边）

竖直插值：
  result = E×0.6 + F×0.4 = 2.0

等价权重：
  A: 0.7×0.6 = 0.42
  B: 0.3×0.6 = 0.18
  C: 0.7×0.4 = 0.28
  D: 0.3×0.4 = 0.12

规律：离哪个角越近，那个角权重越大
GPU硬件自动完成，不需要手写代码
```

### 加权平均公式

```hlsl
float w = 1.0 / (Luminance(c) + 1.0);

// 亮度越高权重越低：
// 亮度 0.5 → w = 0.67（高权重）
// 亮度 9.0 → w = 0.10（低权重）
// 亮度 99  → w = 0.01（极低权重）

// 结果：极亮像素的贡献被数学性地投票否决
```

---

## Luminance 函数

```hlsl
float Luminance(float3 color) {
    return dot(color, float3(0.2126, 0.7152, 0.0722));
}
// 人眼对绿色最敏感（71%），蓝色最不敏感（7%）
// 把 RGB 映射成单一亮度感知值
```

---

## 三种 Tone Mapping 数学

### Reinhard

```hlsl
color.rgb = min(color.rgb, 60.0);  // 防止 inf/NaN
color.rgb /= color.rgb + 1.0;      // c/(c+1)

输入    输出
 0.5    0.33
 1.0    0.50  ← 原来的"纯白"变成了中灰
 3.0    0.75
10.0    0.91
200     0.995

为什么 min(60)？
  half float inf/(inf+1) = NaN → 画面黑，崩了
  60/(60+1) = 0.983，安全
```

### Neutral（John Hable S形曲线）

```hlsl
color.rgb = NeutralTonemap(color.rgb);  // Core库函数，URP/HDRP同款

S形：暗处保留对比度，1.0附近压到~0.7，亮部渐渐趋白
比 Reinhard 暗区对比更好，不会感觉灰蒙蒙
```

### ACES（学院色彩标准 / 好莱坞电影级）

```hlsl
color.rgb = AcesTonemap(unity_to_ACES(color.rgb));

// unity_to_ACES：把 Unity sRGB 颜色转到 ACES 色彩空间
// AcesTonemap：应用 RRT+ODT 压缩曲线（多项式近似）
//   a=2.51, b=0.03, c=2.43, d=0.59, e=0.14
//   output = x*(a*x+b) / (x*(c*x+d)+e)

效果：超亮处色相偏移向白（胶片高光特征），整体饱和度略降
```

### 三种曲线对比

```
输入值   Reinhard   Neutral   ACES
  0.5     0.33      0.42     0.40
  1.0     0.50      0.65     0.60
  3.0     0.75      0.86     0.85
 10.0     0.91      0.96     0.95

ACES ≈ Neutral，区别：超亮处 ACES 更偏黄/白（胶片高光）
Reinhard 最简单，整体偏暗偏灰
```

---

## 细节修正：BeginSample 位置

```csharp
// ❌ 错误（Bloom禁用时 Frame Debugger 出现空标签）：
bool DoBloom(int sourceId) {
    buffer.BeginSample("Bloom");   // 最顶部
    if (disabled) {
        buffer.EndSample("Bloom"); // 空块出现
        return false;
    }
    ...
}

// ✅ 正确（禁用时完全跳过，不产生任何标签）：
bool DoBloom(int sourceId) {
    if (disabled) {
        return false;              // 直接返回，BeginSample 从未调用
    }
    buffer.BeginSample("Bloom");   // 确认有 Bloom 才开始计时
    ...
}
```

**原则：BeginSample / EndSample 必须成对出现。确认要做某件事再开始计时，别先开始再取消。**

---

## Fade Fireflies 使用场景

**效果不明显是正常的**，需要特定场景才会触发：

```
触发条件：
  1. 单个/少量像素亮度远高于周围（超过10倍以上）
  2. 相机移动时这些极亮像素位置跳动
  3. 这些像素亮度超过 bloom threshold

典型场景（开启 FadeFireflies 才有效果）：
  ✦ 镜面材质（Metallic=1）反射点光源 → 高光只落在1~2像素
  ✦ HDR 环境贴图里的太阳盘面（几百亮度，就几个像素）
  ✦ 粒子系统的极亮小火花

普通场景（开不开都一样）：
  ✦ 漫射光照（大面积均匀照明）
  ✦ 天空盒（连续大面积，不是孤立像素）
  ✦ 普通发光材质（周围也有一定亮度）
```

---

## 第12章完成验证清单

```
✅ Pipeline Asset Inspector → 有 Allow HDR 勾选
✅ Camera Inspector → HDR 选 "Use Graphics Settings"
✅ Post FX Settings → Bloom → Fade Fireflies 可勾选
✅ Post FX Settings → Bloom → Mode 可切 Additive/Scattering
✅ Post FX Settings → Tone Mapping → Mode 可选 ACES/Neutral/Reinhard
✅ 开 ACES/Reinhard 后天空盒不再一片白（Tone Mapping 生效）
✅ Frame Debugger → Bloom 禁用时无空标签
✅ Bloom RT 格式 = R16G16B16A16_SFloat（HDR开启时）
```
