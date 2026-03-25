# 第十五章 Particles — 核心笔记

> 章节任务：为 SRP 添加粒子渲染支持，包括顶点颜色、Flipbook 动画、近面渐隐、软粒子、扭曲效果。
> 核心基础设施：将帧缓冲拆分为颜色/深度 Attachment，并在透明阶段前拷贝快照。

---

## 一、粒子系统与 Shader 的分工

```
粒子系统（CPU 端）：
  - 管理粒子生命周期、位置、颜色、UV
  - 每帧把所有数据打包成一个大 Mesh 传给 GPU
  - Flipbook 的帧号、混合因子已经在 CPU 算好

Shader（GPU 端）：
  - 负责渲染：顶点颜色叠乘、Flipbook lerp、近面渐隐、软粒子 fade
  - 不关心时间、帧率，只接受 CPU 算好的数据
```

---

## 二、新增功能详解

### 1. 顶点颜色（`_VERTEX_COLORS`）
```hlsl
// 粒子系统 Inspector 设置 Start Color = Random Between Two Colors
// → 每个粒子有不同颜色，写入顶点 COLOR 语义
// → 顶点着色器透传，片元着色器乘入最终颜色

struct Attributes { float4 color : COLOR; ... };
output.color = input.color;           // 顶点着色器
base *= c.color;                      // 片元着色器（GetBase 里）
```

### 2. Flipbook 帧间混合（`_FLIPBOOK_BLENDING`）
```
Flipbook 贴图 = 把多帧动画拼在一张大图（Atlas）
  4×4 格子 = 16帧动画
  CPU 算好：当前帧UV、下一帧UV、混合因子（小数部分）
  GPU 只做：lerp(采样A, 采样B, blend)

好处：
  - 不切换贴图 → 不打断合批
  - 帧间平滑过渡，不跳帧
  - 与游戏帧率无关（基于时间，不基于帧数）

顶点流（Renderer module → Vertex Streams）：
  UV  → TEXCOORD0.xy（当前帧）
  UV2 → TEXCOORD0.zw（下一帧）
  AnimBlend → TEXCOORD1（混合因子）
```

### 3. 近面渐隐（`_NEAR_FADE`）
```
问题：粒子靠近相机时突然出现（pop in），很违和
解决：fragment.depth < NearFadeDistance 时，逐渐降低 alpha

数据来源：
  fragment.depth = positionCS_SS.w（GPU 自动提供，透视相机下=真实距离）

计算：
  attenuation = saturate((depth - Distance) / Range)
  // depth < Distance → attenuation < 0 → saturate = 0 → 完全透明
  // depth > Distance+Range → attenuation = 1 → 正常
  baseMap.a *= attenuation;
```

### 4. 软粒子（`_SOFT_PARTICLES`）
```
问题：粒子面片插进墙里，交界线有硬边
解决：粒子片元靠近场景固体时，alpha 渐渐降低

需要两个深度：
  fragment.depth    = 粒子自身深度（positionCS_SS.w）
  fragment.bufferDepth = 这个屏幕位置上固体的深度（从 _CameraDepthTexture 采样）

计算：
  depthDelta = bufferDepth - fragment.depth  // 差值 = 粒子离固体多远
  attenuation = saturate((depthDelta - Distance) / Range)
  差值越小 → 粒子越贴近固体 → fade

关键：需要深度快照 _CameraDepthTexture！
```

### 5. 扭曲（`_DISTORTION`）
```
效果：粒子不画自己，而是扭曲背后的场景（热浪效果）

原理：
  ① 读法线图 → 得到 UV 偏移 (dx, dy)
  ② screenUV + offset → 采样 _CameraColorTexture
  ③ 把偏移后的背景色混入粒子颜色

base.rgb = lerp(
    GetBufferColor(fragment, distortion).rgb,  // 偏移后背景
    base.rgb,                                   // 粒子自身颜色
    saturate(base.a - DistortionBlend)          // 混合比
);
// base.a 低 → 边缘 → 更多背景扭曲
// base.a 高 → 中心 → 更多粒子自身颜色

关键：需要颜色快照 _CameraColorTexture！
```

---

## 三、核心基础设施：缓冲区拆分 + 快照

### 为什么要拆分帧缓冲？

```
旧：frameBufferId（颜色+深度合一）
    → 无法单独访问深度通道
    → 软粒子和扭曲都需要单独读取

新：
    colorAttachmentId（_CameraColorAttachment）← 只存 RGBA
    depthAttachmentId（_CameraDepthAttachment）← 只存深度（RenderTextureFormat.Depth）

SetRenderTarget(color, Load, Store, depth, Load, Store);
// GPU 分别往两张RT写，互不干扰
```

### CopyAttachments()——拍快照

```
调用时机：天空盒画完之后、透明粒子之前

操作：
  depthAttachmentId → _CameraDepthTexture（深度快照，软粒子用）
  colorAttachmentId → _CameraColorTexture（颜色快照，扭曲用）

实现方式：
  buffer.CopyTexture(src, dst)  ← GPU内存直拷，无 Shader，极快
  备用（WebGL 等不支持时）：
  Draw(src, dst, isDepth) → 全屏三角形 + CameraRenderer shader

为什么在天空盒后？
  → 确保快照里只有不透明固体的深度/颜色
  → 没有粒子污染 → 粒子读到的是"纯净场景"
```

### useIntermediateBuffer 逻辑

```
触发条件（任一满足）：
  - postFXStack.IsActive（后处理激活）
  - useDepthTexture（需要深度拷贝）
  - useColorTexture（需要颜色拷贝）

结果：
  - 渲染目标切换到 colorAttachment（不再直接写屏幕）
  - 最终要输出到屏幕：
    有后处理 → postFXStack.Render(colorAttachmentId) 自己输出
    无后处理 → Draw(colorAttachmentId → CameraTarget)
```

---

## 四、片元深度系统：Fragment.hlsl

### SV_POSITION 的双重含义

```
顶点着色器输出时（Clip Space）：
  .xyz = 裁剪空间坐标（-w ~ +w）
  .w   = 齐次因子（等于 view-space 深度，未做透视除法）

GPU 光栅化后传给片元着色器（Screen Space）：
  .xy  = 屏幕像素坐标（如 640.5, 360.5）
  .z   = 非线性深度（0~1，写入深度缓冲用）
  .w   = 原始 clip.w（= view-space 深度，GPU 保留！）

重命名 positionCS → positionCS_SS：
  强调这个字段在顶点/片元阶段语义完全不同
```

### Fragment 结构体

```hlsl
struct Fragment {
    float2 positionSS;   // 屏幕像素坐标（LOD 抖动用）
    float2 screenUV;     // 屏幕UV 0~1（采样快照用）
    float  depth;        // 自身深度，= positionCS_SS.w（近面渐隐用）
    float  bufferDepth;  // 快照深度（软粒子用：从 _CameraDepthTexture 采样）
};

// screenUV = positionSS / _ScreenParams.xy
// bufferDepth = LinearEyeDepth(采样深度缓冲, _ZBufferParams)
```

### 正交 vs 透视相机的深度换算

```
透视相机：positionCS_SS.w = view-space 深度（直接用）
正交相机：positionCS_SS.w 永远 = 1（必须从 .z 换算）
  linear = (far - near) × rawDepth + near

GetFragment() 自动判断并选择正确方式
```

---

## 五、CameraRenderer.shader（新文件）

```
专属 Shader，用于全屏拷贝 Pass：

  Pass 0 (Copy Color)：
    → 采样 _SourceTexture → 输出颜色
    → 用于无后处理时把 colorAttachment 输出到屏幕

  Pass 1 (Copy Depth)：
    → 采样 _SourceTexture → 写入 SV_DEPTH
    → 备用深度拷贝（WebGL 等不支持 CopyTexture 时）

顶点着色器：程序化全屏三角形（不需要 Mesh，vertexID=0,1,2 生成覆盖全屏的大三角）
```

---

## 五·五、SRP 核心模式：C# 运行时创建材质

> 💡 重要理解：C# 可以在运行时创建材质，绑定 Shader，然后让 GPU 用它处理贴图

### C# 端（CPU）如何指挥 GPU

```csharp
// ① 用 Shader 创建材质（运行时，不需要在 Project 里有 .mat 文件）
Material material = CoreUtils.CreateEngineMaterial(cameraRendererShader);

// ② 把要处理的贴图绑给 Shader
buffer.SetGlobalTexture("_SourceTexture", colorAttachment);

// ③ 指定渲染目标
buffer.SetRenderTarget(screenTarget, ...);

// ④ 用这个材质画一个全屏三角形（3个顶点，无Mesh）
buffer.DrawProcedural(Matrix4x4.identity, material, passIndex,
    MeshTopology.Triangles, 3);
```

```
GPU 端（Shader 执行）：
  读 _SourceTexture → Pass 0 直接输出颜色 → 写到屏幕
  就是一次"全屏采样+输出"
```

### Shader 通过 Inspector 传递，不能硬编码路径

```
问题：不能在 C# 里写 Shader.Find("Hidden/Custom RP/Camera Renderer")
      如果 Shader 被移动或改名就会失效

正确做法：在 Inspector 里拖拽赋值，形成持久引用

传递链：
  硬盘上的 CameraRenderer.shader
       ↓ Inspector 拖拽
  CustomRenderPipelineAsset（存 [SerializeField] Shader 引用）
       ↓ CreatePipeline() 里传构造函数
  CustomRenderPipeline（接收 Shader 参数）
       ↓ new CameraRenderer(shader)
  CameraRenderer（material = CoreUtils.CreateEngineMaterial(shader)）
       ↓ Draw() 方法使用
  DrawProcedural + material
```

### 这个模式在 SRP 里到处都是

```
PostFXStack：
  C# 设置 _PostFXSource = 当前帧画面
  → 用 PostFXStack.shader 的 Bloom Pass 处理
  → 输出到屏幕

Shadow Atlas：
  C# 绑定 ShadowAtlas RT
  → ShadowCaster shader 往里画深度
  → 结果供 Lit shader 读取

CameraRenderer（本章）：
  C# 设置 _SourceTexture = colorAttachment
  → CameraRenderer.shader 全屏采样输出
  → 写到屏幕

本质都是：C# 准备数据 → 指定 Shader → GPU 执行
```

---

## 六、InputConfig 渐进式扩展

```
这章引入了 InputConfig 结构体：

Lit/Unlit 版（简单版）：
  struct InputConfig { Fragment fragment; float2 baseUV; }

粒子版（完整版）：
  struct InputConfig {
    Fragment fragment;
    float4   color;           // 顶点颜色
    float2   baseUV;
    float3   flipbookUVB;     // Flipbook 第二帧UV + 混合因子
    bool     flipbookBlending;
    bool     nearFade;
    bool     softParticles;
  }

GetBase(InputConfig c) 集中处理所有 alpha 修改：
  顶点色叠乘 → Flipbook lerp → 近面渐隐 → 软粒子 fade
```

---

## 七、文件改动总览

| 文件 | 改动 |
|------|------|
| `Fragment.hlsl` | 新建：Fragment 结构体 + GetFragment + GetBufferColor |
| `Common.hlsl` | 引入 Fragment.hlsl，ClipLOD 改接 Fragment，声明公共采样器 |
| `UnityInput.hlsl` | 新增 unity_OrthoParams / _ScreenParams / _ZBufferParams |
| `LitInput.hlsl` | 新增 InputConfig（简版），GetInputConfig |
| `UnlitInput.hlsl` | 新增 InputConfig（简版），GetInputConfig |
| `LitPass.hlsl` | positionCS → positionCS_SS，ClipLOD/dither 改用 Fragment |
| `UnlitPass.hlsl` | 改名 + 使用 InputConfig |
| `ShadowCasterPass.hlsl` | 改名 + ClipLOD/dither 改用 Fragment |
| `PostFXStackPasses.hlsl` | 删除重复的 sampler_linear_clamp 声明 |
| `Particles/ParticlesUnlit.shader` | 新建粒子专用 Shader |
| `Particles/ParticlesUnlitInput.hlsl` | 新建粒子完整版 InputConfig+GetBase |
| `Particles/ParticlesUnlitPass.hlsl` | 新建粒子完整版顶点/片元着色器 |
| `CameraRenderer.shader` | 新建全屏拷贝 Shader（2个Pass） |
| `CameraRendererPasses.hlsl` | 新建全屏三角形顶点着色器 + 拷贝 Pass |
| `CameraRenderer.cs` | 拆分缓冲区 + CopyAttachments + Draw 方法 |
| `CustomRenderPipeline.cs` | 构造函数新增 Shader 参数 |
| `CustomRenderPipelineAsset.cs` | 新增 cameraRendererShader 字段 |

---

## 八、拓展：LUT 贴图为什么长那个样子？

> 💡 在 Frame Debugger 里能看到 `_ColorGradingLUT`（1024×32 的五彩条纹贴图），这就是 LUT。

### LUT 是什么

```
LUT = Look-Up Table（颜色查找表）
原理：把原始颜色 → 查表 → 输出颜色
好处：比实时计算色调/饱和度/对比度快得多（只是一次贴图采样）

理想形式：3D 贴图（32×32×32 立方体）
  输入 (R,G,B) → 查立方体 → 输出新的 (R,G,B)
  
问题：很多硬件不支持 3D 贴图
解决：把 32 个"切片"横向拼成 2D 长条带
  32层 × 每层32×32 = 1024×32 的横条
```

### 为什么是彩虹色条纹？

```
每一小格（32×32）内部：
  X轴 = 红色分量（0→1，左到右）
  Y轴 = 绿色分量（0→1，下到上）

32格横向拼接 → 每格的蓝色值不同（0→1）：
  左边的格：蓝色=0 → 偏红/绿
  右边的格：蓝色=1 → 偏青/蓝/紫

结果：整条长图看起来是
  [红/橙/黄] → [黄/绿] → [青/蓝] → [蓝/紫]
  = 所有 RGB 组合的"颜色地图"
  
本质：存了1024×32 = 32768 个"输入颜色→输出颜色"的对应关系
```

### 游戏解包中常见的 3 种 LUT

```
① 色调映射 LUT（本章 _ColorGradingLUT）
   形态：1024×32 的长条带
   用途：全屏后处理，控制整个画面的颜色风格
   每帧根据色彩设置实时烘焙生成

② 卡通皮肤 Ramp 贴图（常见于原神等）
   形态：一张窄长图（如 256×1 或 256×16）
   用途：控制人物皮肤从亮到暗的颜色过渡
   X轴 = 光照强度（0=暗部,1=亮部）
   对应颜色：暗部偏冷/蓝，亮部偏暖/橙
   比简单的 diffuse * lightColor 效果更好

③ 调色 LUT（美术风格贴图）
   形态：512×512 的特殊方形布局
   用途：滤镜效果，统一全屏颜色风格
   类似于 Instagram 滤镜的工作原理
```

### 我们的 LUT 在 SRP 里的位置

```
PostFXStack.Render() 流程：

  ① 渲染 ColorGradingLUT（烘焙查找表）
     Pass: "Color Grading ACES"
     RenderTarget: _ColorGradingLUT（1024×32）
     ← 这就是 Frame Debugger 里那个五彩条纹！

  ② 渲染最终画面
     Pass: "Final"
     读 _CameraColorAttachment（原始画面）
     同时读 _ColorGradingLUT（查找表）
     → 原始颜色 → 插值查表 → 输出最终颜色
```

---

## 十、Blend 模式原理（Src Blend 的影响）

```
GPU 混合公式：
  最终颜色 = Src × 粒子颜色 + Dst × 背景颜色

粒子贴图（particles-single.png）：
  中心：RGB=(1,1,1)，alpha=1（不透明白色）
  边缘：RGB=(1,1,1)，alpha=0（完全透明）

Src = One（错误）：
  边缘：1 × (1,1,1) + (1-0) × 背景 = 白色 + 背景 → 白色方块出现
  → 透明区域也贡献白色 → 方块感

Src = SrcAlpha（正确）：
  边缘：0 × (1,1,1) + (1-0) × 背景 = 背景
  → 透明区域完全透明 → 软边圆形 ✅

结论：粒子软边效果必须用 Src=SrcAlpha，Transparent 预设自动设好
```

---

## 十一、粒子贴图的 Alpha 通道结构

```
particles-single.png 内容：
  RGB：全白 (1,1,1)—— 决定颜色
  Alpha：中心=1，向外渐变到0—— 决定形状

原理：RGB 通道画颜色，Alpha 通道雕刻形状

在白色背景上看不到：因为白色圆叠在白色背景上
放到有色背景（蓝天/灰地）才能看到软边白圆

结论：用 Alpha 通道控制形状，用 RGB 控制颜色
      Transparent 混合模式利用 alpha 把边缘透明化
```

---

## 十二、Vertex Colors 与材质 Color 的关系

```hlsl
// GetBase() 最终颜色公式：
return baseMap * baseColor * c.color;
//  ①贴图颜色  ②材质Color   ③顶点颜色（粒子实例色）

// 三者相乘：乘以(1,1,1)=不变，乘以(0,0,0)=黑色

// 不勾 Vertex Colors：
//   c.color = 1.0（白色，固定值）→ 粒子 Start Color 无效

// 勾上 Vertex Colors：
//   c.color = input.color（来自粒子系统每粒子单独颜色）
//   → Start Color = 随机黑白 → 粒子颜色各不相同
```

---

## 十三、Custom Vertex Streams 与粒子系统的关系

### 核心原理

```
Shader Attributes 结构体 = 只是"收货单"（声明我要读什么格式）
粒子系统 = 按约定格式往 GPU 顶点缓冲里塞数据
两者通过语义名（POSITION/COLOR/TEXCOORD）约定接口

Shader 不知道也不关心数据来自 Mesh 还是粒子系统
只要格式匹配就能读取 ✅
```

### 两类数据的区别

```
我们传的（所有粒子共享）：
  材质 Properties：_BaseMap, _BaseColor, _DistortionStrength
  全局 Texture：_CameraDepthTexture, _CameraColorTexture
  → 在 Inspector 赋值 / C# SetGlobalTexture
  → 同一批粒子用同一份数据

粒子系统传的（每粒子独立）：
  Attributes 结构体里的：POSITION, COLOR, TEXCOORD0...
  → 粒子系统 CPU 每帧实时计算，写入顶点缓冲
  → 每个粒子位置/颜色/UV帧/混合因子各不相同
```

### Flipbook 数据流全链路

```
粒子产生时（CPU 每帧）：
  粒子A 生命进度 50% → 当前第8帧，下一帧第9帧，blend=0.6
  → 写入 TEXCOORD0.xy = 第8帧UV
  → 写入 TEXCOORD0.zw = 第9帧UV（Custom Vertex Streams: UV2）
  → 写入 TEXCOORD1.x  = 0.6（Custom Vertex Streams: AnimBlend）

Shader 读取：
  float4 baseUV : TEXCOORD0;    // .xy=第8帧，.zw=第9帧
  float flipbookBlend : TEXCOORD1; // 0.6

片元着色器：
  lerp(采样第8帧, 采样第9帧, 0.6) → 平滑过渡 ✅

Custom Vertex Streams = 粒子系统与 Shader 之间的"接口协议"
不配置 → 数据不够 → Shader 读到垃圾 → 效果错误
```

### UNITY_VERTEX_INPUT_INSTANCE_ID 是什么

```
展开后约等于：uint instanceID : SV_InstanceID;

GPU Instancing 时每个粒子（实例）有一个编号（0,1,2,...）
Shader 通过编号找自己实例的独立数据（颜色/矩阵等）

UNITY_SETUP_INSTANCE_ID(input)            → 初始化当前编号
UNITY_TRANSFER_INSTANCE_ID(input, output) → 顶点→片元传递
不加这个 → 所有粒子读同一份实例数据 → 颜色/大小全一样
```

---

## 十四、扭曲效果完整流程（Distortion）

### 四张 RT 的职责

| 贴图 | 谁写 | 谁读 | 内容 |
|------|------|------|------|
| `_CameraColorAttachment` | 渲染管线 | 后处理/DrawFinal | 当前帧颜色（实时更新） |
| `_CameraDepthAttachment` | 渲染管线 | Gizmos深度写回 | 当前帧深度（实时更新） |
| `_CameraColorTexture` | CopyAttachments | 粒子Shader（扭曲） | 天空盒后颜色快照（只读） |
| `_CameraDepthTexture` | CopyAttachments | 粒子Shader（软粒子） | 天空盒后深度快照（只读） |

### 为什么要拷贝快照

```
不拷贝（直接读 ColorAttachment）：
  粒子边读边写 → Read-Write 冲突 → GPU 不保证顺序 → 结果随机

拷贝快照后：
  粒子 读 ColorTexture（只读快照，永远干净）
  粒子 写 ColorAttachment（只写当前帧缓冲）
  → 完全分离，安全 ✅
```

### lerp 公式解读

```hlsl
base.rgb = lerp(
    GetBufferColor(config.fragment, distortion).rgb,  // A = 扭曲背景色
    base.rgb,                                          // B = 粒子原色
    saturate(base.a - GetDistortionBlend(config))     // t = 混合权重
);

// t = saturate(alpha - DistortionBlend)
// DistortionBlend=1 → t≈0 → 只显示扭曲背景（纯玻璃）
// DistortionBlend=0 → t≈alpha → 粒子原色占主导
// 粒子边缘 alpha=0 → t=0 → 扭曲强，边缘自然消融
// 粒子中心 alpha=1 → t=1-Blend → 可见粒子颜色
```

### CopyTexture vs Draw 全屏拷贝

```
CopyTexture：
  GPU 显存内部直接 memcpy，不经过 Shader
  极快，0 DrawCall，类似 CPU 的 memcpy

Draw 全屏三角形（备用）：
  用 CameraRenderer Shader 画全屏三角形采样 _SourceTexture
  完整 DrawCall 流程，每像素过一遍 Shader
  WebGL 2.0 等不支持 CopyTexture 的平台用此方式

copyTextureSupported = SystemInfo.copyTextureSupport > CopyTextureSupport.None
  支持 → CopyTexture（更快）
  不支持 → Draw（兼容但稍慢，拷贝后要切回渲染目标）
```

---

## 十五、Gizmos 深度问题

```
问题根源：
  后处理把 ColorAttachment 输出到 CameraTarget（颜色）
  但深度没有跟着写入 CameraTarget！
  → DrawGizmos 做 ZTest 读到的是空/错误深度
  → 编辑器里选中框穿透场景显示出来

解决：绘制 Gizmos 前先拷贝深度到 CameraTarget：
  Draw(depthAttachmentId, CameraTarget, isDepth=true)
  → CopyDepth Pass（ZWrite=On, ColorMask=0）把深度写入
  → DrawGizmos ZTest 正常 ✅
```

---

## 十六、DrawFinal 替代简单 Draw（多相机支持）

```
简单 Draw(colorAttachment, CameraTarget)：
  内部 SetRenderTarget(CameraTarget, DontCare)
  → 直接覆盖 CameraTarget 原有内容
  → 多相机叠加时底层相机画面被清除 ❌

DrawFinal(cameraSettings.finalBlendMode)：
  ① SetGlobalFloat(srcBlendId, finalBlendMode.source)
  ② SetGlobalFloat(dstBlendId, finalBlendMode.destination)
  ③ SetRenderTarget( Load or DontCare 取决于：
       全屏 + Dst=Zero → DontCare（省带宽）
       叠加 / 小视口  → Load（保留底层相机内容）
     )
  ④ SetViewport(camera.pixelRect)（支持分割屏幕）
  ⑤ DrawProcedural 全屏三角形

CameraSettings.FinalBlendMode：
  底层相机：Source=One, Dest=Zero（覆盖）
  叠加相机：Source=One, Dest=OneMinusSrcAlpha（Alpha 混合）
```

---

## 十七、CameraBufferSettings 架构改进

```
之前：useDepthTexture = true（硬编码，永远拷贝，浪费性能）

改进后：CameraBufferSettings 结构体（在 Pipeline Asset 管理）
  bool allowHDR;
  bool copyDepth;             ← 普通相机是否拷贝深度
  bool copyDepthReflection;   ← 反射相机（通常关）
  bool copyColor;             ← 普通相机是否拷贝颜色
  bool copyColorReflection;   ← 反射相机（通常关）

CameraSettings（每台相机独立）：
  bool copyDepth = true;  ← 相机级别的二次开关
  bool copyColor = true;

最终逻辑：管线全局开 AND 相机开关，两者都 true 才拷贝

missingTexture（占位贴图）：
  1×1 灰色纹理（(0.5, 0.5, 0.5, 1)）
  Setup() 时先绑定到 _CameraDepthTexture / _CameraColorTexture
  防止深度/颜色拷贝未就绪时 Shader 采样到垃圾数据

Dispose()（防止内存泄漏）：
  Pipeline Asset 每次修改都会重建 Pipeline 实例
  不 Dispose → 旧 Material 和 Texture 堆积 → 内存泄漏
  CoreUtils.Destroy(material) + CoreUtils.Destroy(missingTexture)
```
