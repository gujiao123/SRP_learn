# 第十四章 Multiple Cameras — 核心笔记

---

## 本章目标

```
在已有的后处理管线基础上，支持多相机渲染：

核心问题：
  多个相机 + Post FX → 后者会把前者完全覆盖
  → 需要修复 Final Pass 的 Viewport 问题

本章新增：
  ① 分屏（Split Screen）修复
  ② 相机叠层（Layering）与透明度混合
  ③ 可配置混合模式（Custom Blending）
  ④ 渲染层遮罩（Rendering Layer Mask）
  ⑤ 按相机屏蔽光源（Mask Lights Per Camera）
```

---

## 渲染管线与多相机的关系

### 多相机是怎么支持的

```csharp
// CustomRenderPipeline.cs
protected override void Render(ScriptableRenderContext context, List<Camera> cameras)
{
    foreach (Camera camera in cameras)   // ← 遍历所有激活相机
    {
        renderer.Render(context, camera, ...);  // ← 每台相机独立完整渲染一次
    }
}
```

Unity 每帧把场景里所有激活相机收集成 `List<Camera>`，管线 for 循环挨个渲染。  
**多相机天然支持，不需要额外配置。**

---

## 相机与渲染的完整关系梳理

### 相机提供了哪些渲染相关信息？

#### ① 视锥体（Frustum）→ 决定"看到什么"

```
来源：camera.transform（位置/朝向）+ FOV/Near/Far/ViewportRect

用途：
  camera.TryGetCullingParameters(out p)  → 生成视锥体剔除参数
  context.Cull(ref p)                    → 得到 cullingResults
                                           （可见物体列表 + 可见灯光列表）

重要：Viewport Rect 会影响视锥体宽高比！
  W=1.0 → 视锥体是 16:9
  W=0.5 → 视锥体是 8:9（更窄），右边物体被剔除
```

#### ② VP 矩阵 → 决定"怎么变换顶点"

```
来源：SetupCameraProperties(camera) 传给 GPU

View 矩阵 = 相机的位置+朝向（世界→相机局部空间）
Proj 矩阵 = 相机的 FOV/Near/Far（相机空间→Clip 空间→NDC）

Shader 里：
  TransformObjectToHClip() = unity_MatrixVP × unity_ObjectToWorld × positionOS
  结果是 NDC (-1~+1)
```

#### ③ Viewport → 决定"画在屏幕哪里"

```
来源：SetupCameraProperties(camera) 传给 GPU（camera.pixelRect）

GPU 最后一步：
  NDC (-1~+1) 映射到 pixelRect（如 0~960 x 0~1080）

分屏效果靠这个实现，和剔除、VP矩阵无关！
```

#### ④ RT（渲染目标）→ 决定"画到哪张图上"

```
没有 PostFX：
  SetupCameraProperties → 绑定相机默认输出（屏幕）

有 PostFX：
  申请中间 RT = camera.pixelWidth × camera.pixelHeight
               （已考虑 Viewport，分屏自动半RT）
  设置 RenderTarget → 画到中间RT
  PostFXStack.Render() → 中间RT → 屏幕（Final Pass）
```

#### ⑤ 清除标志（ClearFlags）→ 决定"每帧开始时RT内容"

```
Skybox  → 清深度 + 画天空盒
Color   → 清深度 + 清颜色（camera.backgroundColor）
Depth   → 只清深度（保留上一台相机画的颜色！叠层相机用）
Nothing → 什么都不清
```

### SetupCameraProperties 的副作用

```csharp
//!! SetupCameraProperties 有副作用：
//   会把当前渲染目标重新绑定到这台相机的输出目标
//   覆盖掉 lighting.Setup 里阴影贴图绑定的那个 RT
//   所以阴影必须在这行之前渲染完毕！

// 顺序必须是：
// 阴影先渲（用 ShadowAtlas RT）
//     ↓
// SetupCameraProperties 把 RT 切回相机目标
//     ↓
// 画场景（用相机 RT）
```

### 相机信息流动路径

```
camera（Unity Component）
    │
    ├──→ TryGetCullingParameters()
    │         ↓
    │     context.Cull()  →  cullingResults（可见物体+灯光）
    │
    ├──→ SetupCameraProperties(camera)
    │         ↓ 向 GPU 传递：
    │         ├── View / Proj / VP 矩阵
    │         ├── 相机位置（_WorldSpaceCameraPos）
    │         ├── Viewport（pixelRect）
    │         └── 绑定 RT（副作用！切换渲染目标）
    │
    ├──→ camera.pixelWidth / pixelHeight
    │         ↓
    │     GetTemporaryRT()  →  中间帧缓冲大小（自动适配 Viewport）
    │
    ├──→ camera.clearFlags
    │         ↓
    │     ClearRenderTarget()  →  怎么清 RT
    │
    └──→ camera.rect（pixelRect）
              ↓
          DrawFinal() 里 SetViewport()  →  Post FX 输出区域
```

---

## 一、分屏渲染（Split Screen）

### Viewport Rect 对视锥体的影响（常见误解！）

```
❌ 错误理解：
  W=0.5 → 相机看到的是原来视野的左半边（切掉右边）

✅ 正确理解：
  W=0.5 → 相机的宽高比从 16:9 变成了 8:9（更窄）
  → 视锥体变窄，水平FOV缩小，看到的内容更少
  → 但内容是完整居中的，不是「切掉一半」

类比：手机横拍 vs 竖拍
  横拍（W=1.0）→ 16:9，水平FOV宽，看到更多左右内容
  竖拍（W=0.5）→ 8:9，水平FOV窄，看到更少左右，但图像是完整的

Gizmos 里能看到：
  W=1.0 → 视锥体宽（较扁的梯形）
  W=0.5 → 视锥体窄（更瘦高的梯形）

输出位置：
  视锥体内容完整渲染后，输出「位置」由 Viewport 决定（画在屏幕左半还是右半）
  视锥体 FOV 和输出位置是两个独立的概念！
```

### 分屏怎么做到的

```
Inspector 配置：
  左相机：X=0,   Y=0, W=0.5, H=1.0
  右相机：X=0.5, Y=0, W=0.5, H=1.0

背后原理：
  SetupCameraProperties(camera) 把 camera.pixelRect 传给 GPU
  GPU 的 Viewport 变换：把 NDC(-1~+1) 映射到对应屏幕区域

  NDC 是-1到+1的固定范围，Viewport 决定它映射到哪块像素：
    左相机 Viewport(0, 0, 960, 1080)：NDC+1 → 像素 x=960
    右相机 Viewport(960, 0, 960, 1080)：NDC-1 → 像素 x=960
```

### NDC → 屏幕 经历了两步（不是一步！）

```
世界坐标
    ↓  × VP矩阵
Clip 坐标
    ↓  GPU Clipping（裁掉视锥体外的三角形）← GPU 自动做！
NDC 坐标（-1~+1）← 这就是视锥体边界！超出的部分在上一步已裁掉
    ↓  Viewport 变换（GPU硬件）
屏幕像素坐标      ← 这里才决定画在哪块区域
```

```
NDC (-1~+1) 的含义：
  NDC_x = -1 → 相机看到的最左边
  NDC_x = +1 → 相机看到的最右边
  NDC_z = 0/1 → Near/Far 裁剪面

GPU Clipping 三种情况：
  物体完全在视锥体内 → 正常渲染
  物体穿过视锥体边缘 → GPU 自动裁掉超出部分，补新顶点
  物体完全在视锥体外 → CPU 端 Cull() 已提前剔除，不提交给 GPU
```

### 分屏下的性能分析

```
RT 大小：
  camera.pixelWidth 自动考虑 ViewportRect
  左半相机（W=0.5）→ camera.pixelWidth = 960（不是1920！）
  所以中间 RT 只申请 960×1080，不浪费

  全屏（1台）：1920×1080 = 206 万像素
  分屏（2台）：960×1080 × 2 = 207 万像素  ← 差不多

顶点提交：
  每台相机只提交视锥体内的物体顶点
  右相机视锥体向右，左侧物体被剔除
  → 各自只渲自己那一半，不重复

真正的额外开销：
  ❌ CPU 端剔除 ×2
  ❌ 阴影贴图渲染 ×2        ← 这个最贵！
  ✅ RT 像素总数：差不多
  ✅ 几何体光栅化：各渲自己那一半
```

### 灯光剔除与分屏

```
方向光（Directional Light）：
  永远对所有可见物体生效，不受 Viewport 影响

点光/聚光灯（Point/Spot Light）：
  context.Cull() 会判断灯光范围是否接触可见物体
  右边的点光如果不接触左相机可见物体 → 直接剔除，不参与光照计算
  → 不会有"灯不见了但还在计算"的浪费
```

---

## 二、分屏 + Post FX 的 Bug 与修复

### Bug：Final Pass 覆盖了整个屏幕

```
问题根源：
  PostFXStack 的最后一步：
    SetRenderTarget(CameraTarget)  ← 这一行会自动把 Viewport 重置成全屏！
    DrawProcedural(...)            ← 全屏三角形输出 → 覆盖整个屏幕

结果：
  左相机渲完（正确）
  右相机 Final Pass → Viewport 被重置 → 用全屏覆盖了所有内容
  → 只看到右相机，左相机消失
```

### 为什么没有 SetViewport 会拉伸？

#### 先搞清楚：两种渲染里 UV 来源完全不同！

```
普通场景渲染（画物体）：
  UV 来源 → Mesh 顶点数据（美术建模时设置）
  和 NDC 坐标没有关系！
  物体在屏幕左边还是右边，UV 都是那个值

Final Pass（全屏三角形）：
  UV 来源 → 顶点着色器硬编码，和 NDC 位置绑定
  NDC(-1,-1) → UV(0,0)   ← 人为这样定义的
  NDC( 1, 1) → UV(1,1)
  所以 NDC → 像素 → UV 这条推导链才在这里成立
  普通场景渲染不适用这条链！
```

#### UV 插值的数学（Final Pass）

```
全屏三角形顶点：
  顶点0: NDC(-1,-1), UV(0,0)
  顶点1: NDC(-1, 3), UV(0,2)   ← 超出屏幕，被裁掉
  顶点2: NDC( 3,-1), UV(2,0)   ← 超出屏幕，被裁掉

UV_x 和 NDC_x 的插值关系：
  UV_x = (NDC_x + 1) / 2

  NDC_x = -1 → UV_x = 0.0
  NDC_x =  0 → UV_x = 0.5
  NDC_x = +1 → UV_x = 1.0   ← 关键：NDC+1时UV刚好=1

像素位置 px 和 NDC_x 的关系（由 Viewport 决定）：
  NDC_x = (px - left) / width × 2 - 1
  反过来：px = (NDC_x + 1) / 2 × width + left

没有 SetViewport（Viewport=全屏 width=1920）：
  pixel 0    → NDC=-1 → UV=0.0
  pixel 960  → NDC= 0 → UV=0.5
  pixel 1920 → NDC=+1 → UV=1.0
  → UV(0~1) 铺展到 0~1920 像素 → 960RT被拉伸成1920 ❌

有 SetViewport（左半 width=960）：
  pixel 0   → NDC=-1 → UV=0.0
  pixel 480 → NDC= 0 → UV=0.5
  pixel 960 → NDC=+1 → UV=1.0
  → UV(0~1) 铺展到 0~960 像素 → 960RT 1:1映射 ✅

结论：Viewport 改变了「哪个像素对应哪个NDC」
      NDC 决定 UV，UV 决定采样 RT 的哪个位置
      所以 Viewport 间接决定了 RT 如何映射到屏幕
```

### 修复方案：新增 DrawFinal 方法

```csharp
// PostFXStack.cs 新增
static Rect fullViewRect = new Rect(0f, 0f, 1f, 1f);

void DrawFinal(RenderTargetIdentifier from) {
    // me14: 把混合模式传给 Shader（在 SetGlobalTexture 之前）
    buffer.SetGlobalFloat(finalSrcBlendId, (float)finalBlendMode.source);
    buffer.SetGlobalFloat(finalDstBlendId, (float)finalBlendMode.destination);
    buffer.SetGlobalTexture(fxSourceId, from);
    buffer.SetRenderTarget(
        BuiltinRenderTextureType.CameraTarget,
        // 两个条件都满足才用 DontCare（无需读旧内容）：
        //   1. destination=Zero（覆盖模式，不需要底层内容）
        //   2. 全屏相机（非全屏必须读旧内容保留其他区域）
        // 叠层相机 destination≠Zero → Load（需要读底层相机渲完的内容）
        finalBlendMode.destination == BlendMode.Zero && camera.rect == fullViewRect ?
            RenderBufferLoadAction.DontCare :
            RenderBufferLoadAction.Load,
        RenderBufferStoreAction.Store
    );
    buffer.SetViewport(camera.pixelRect);  // ← 关键！手动恢复 Viewport，UV 才能正确匹配
    buffer.DrawProcedural(
        Matrix4x4.identity, settings.Material, (int)Pass.Final,
        MeshTopology.Triangles, 3
    );
}

// DoColorGradingAndToneMapping 最后改用 DrawFinal：
void DoColorGradingAndToneMapping(int sourceId) {
    // ...
    DrawFinal(sourceId);   // ← 替换原来的 Draw(...)
    buffer.ReleaseTemporaryRT(colorGradingLUTId);
}
```

---

## 三、相机叠层（Layering Cameras）

### 叠层的概念

```
主相机：覆盖全屏（底层）
叠加相机：更小的视口覆盖在上面（叠层）
  → ClearFlags 设 Depth Only：只清深度，保留底层颜色

不用 PostFX 时：直接可以做透明叠加
用 PostFX 时：需要额外处理 alpha 混合（下面一一处理）
```

### Bloom 终止 Pass 保留 Alpha

```
问题：Bloom 之前把 alpha 强制写成 1.0
      叠层相机开了 Bloom 后透明区域变不透明

修复：BloomAddPassFragment 和 BloomScatterFinalPassFragment
      两个函数都要改：取 float4 highRes 保留 .a
```

```hlsl
// PostFXStackPasses.hlsl
float4 BloomAddPassFragment (Varyings input) : SV_TARGET {
    float3 lowRes = ...;
    float4 highRes = GetSource2(input.screenUV);        // float4（含 alpha）
    return float4(lowRes * _BloomIntensity + highRes.rgb, highRes.a);
}

float4 BloomScatterFinalPassFragment (Varyings input) : SV_TARGET {
    float3 lowRes = ...;
    float4 highRes = GetSource2(input.screenUV);
    lowRes += highRes.rgb - ApplyBloomThreshold(highRes.rgb);
    return float4(lerp(highRes.rgb, lowRes, _BloomIntensity), highRes.a);
}
```

### 正确的 Alpha 值（ZWrite 控制）

```
问题：不透明物体的贴图可能含有乱七八糟的 alpha（如 0.3、0.7）
      这些 alpha 写进 RT 会让实体物体"意外变半透明"
      不透明物体一定是 ZWrite=1，所以用它来判断

规则：
  ZWrite=1（不透明物体）→ alpha 强制 = 1.0
  ZWrite=0（透明物体）  → alpha 保留真实值
```

```hlsl
// LitInput.hlsl（用 INPUT_PROP 宏）：
UNITY_DEFINE_INSTANCED_PROP(float, _ZWrite)

float GetFinalAlpha(float alpha) {
    return INPUT_PROP(_ZWrite) ? 1.0 : alpha;
}

// UnlitInput.hlsl（没有 INPUT_PROP，用完整版）：
UNITY_DEFINE_INSTANCED_PROP(float, _ZWrite)

float GetFinalAlpha(float alpha) {
    return UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _ZWrite) ? 1.0 : alpha;
}

// LitPass.hlsl 片元着色器末尾：
return float4(color, GetFinalAlpha(surface.alpha));

// UnlitPass.hlsl 片元着色器末尾：
float4 base = GetBase(input.baseUV);
return float4(base.rgb, GetFinalAlpha(base.a));
```

### Lit.shader / Unlit.shader 加 Alpha 通道混合

```
问题：两个半透明物体叠在同一像素时，alpha 需要正确累加
      alpha1=0.5 和 alpha2=0.5 叠加后应是 0.25
      需要给 alpha 通道单独配置混合模式

语法：Blend RGBsrc RGBdst, AlphaSrc AlphaDst
      逗号左边是 RGB 通道，逗号右边是 alpha 通道
```

```hlsl
// Lit.shader 和 Unlit.shader 的 Pass 中修改：

// 修改前：
Blend [_SrcBlend] [_DstBlend]

// 修改后（alpha 通道固定用 One OneMinusSrcAlpha）：
Blend [_SrcBlend] [_DstBlend], One OneMinusSrcAlpha
```

### UnlitPass.hlsl 升级（之前过于简陋）

```
原来：直接 return _BaseColor，没有贴图采样、没有 UV、没有 Clip
现在：升级为和 LitPass 同等完善的结构
```

```hlsl
// 新增内容：
struct Attributes { float2 baseUV : TEXCOORD0; ... };
struct Varyings   { float2 baseUV : VAR_BASE_UV; ... };

// 顶点着色器：
output.baseUV = TransformBaseUV(input.baseUV);  // 支持 Tiling/Offset

// 片元着色器：
float4 base = GetBase(input.baseUV);            // 贴图采样 × BaseColor
#if defined(_CLIPPING)
    clip(base.a - GetCutoff(input.baseUV));     // Clip 模式
#endif
return float4(base.rgb, GetFinalAlpha(base.a)); // 正确 alpha
```

---



### DrawFinal 的 Load 条件完善

```
问题：原来只判断是否全屏来决定 DontCare
      但全屏叠层相机（全屏 + OneMinusSrcAlpha）也需要 Load！

修捉：两个条件都满足才能 DontCare
```

```csharp
// PostFXStack.cs DrawFinal() 中：

// 修改前：
camera.rect == fullViewRect ?
    RenderBufferLoadAction.DontCare :
    RenderBufferLoadAction.Load,

// 修改后：
finalBlendMode.destination == BlendMode.Zero && camera.rect == fullViewRect ?
    RenderBufferLoadAction.DontCare :   // 全屏且覆盖模式 → 不需要读旧内容
    RenderBufferLoadAction.Load,        // 其他情况 → 必需 Load（包括全屏叠层）
```

---

## 四点五、最终混合模式（Custom Blending）

### 混合模式是什么？

Final Pass 把中间 RT 的画面写到屏幕时，GPU 需要决定：**"新像素" 和 "屏幕已有颜色" 如何合并？**

```
混合公式：
  最终颜色 = 新像素 × Source  +  屏幕已有颜色 × Destination
```

#### One Zero（覆盖）← 底层相机默认

```
最终颜色 = 新像素 × 1 + 已有颜色 × 0 = 新像素（直接覆盖）

用途：第一台相机，屏幕还是空的，直接写入，最高效
```

#### One OneMinusSrcAlpha（预乘 alpha 叠加）← 叠层相机用

```
最终颜色 = 新像素 × 1 + 已有颜色 × (1 - 新像素alpha)

例子：
  底层已有颜色 = 红色 (1, 0, 0)
  叠层新像素   = 蓝色半透明 (0, 0, 1, alpha=0.5)

  结果 = (0,0,1)×1 + (1,0,0)×(1-0.5)
       = (0,0,1) + (0.5,0,0)
       = (0.5, 0, 1)  ← 底层红色透过来了！
```

### 混合先后顺序的保证

混合涉及"读屏幕已有内容"，所以底层相机必须先渲，叠层相机后渲。

#### 顺序保证：camera.depth

```csharp
// CustomRenderPipeline.cs
// Unity 传进来的 cameras 已按 camera.depth 从小到大排好序！
foreach (Camera camera in cameras)
{
    renderer.Render(context, camera, ...);  // 顺序执行，底层先渲
}
```

```
配置规则：
  底层相机  → Depth = 0（或更小），FinalBlendMode = One / Zero
  叠层相机  → Depth = 1（或更大），FinalBlendMode = One / OneMinusSrcAlpha

时间线：
  [底层渲染] → 屏幕有内容了
  [叠层渲染] → 读屏幕现有内容 → 混合公式生效 → 正确叠加 ✅

顺序反了会怎样：
  [叠层先渲]  → OneMinusSrcAlpha → 屏幕还是空的 → 叠个寂寞
  [底层后渲]  → One Zero → 直接覆盖叠层 → 叠层白渲了 ❌
```

#### 内容保证：Load vs DontCare

```csharp
// DrawFinal() 里，非全屏时自动用 Load
camera.rect == fullViewRect ?
    RenderBufferLoadAction.DontCare :   // 全屏 → 直接覆盖，不需要读旧内容
    RenderBufferLoadAction.Load         // 非全屏/叠层 → 必须先读屏幕现有内容！
```

```
叠层相机 Viewport 不是全屏 → 自动走 Load
GPU 先读出底层相机画好的颜色 → 再执行 One OneMinusSrcAlpha 混合公式 ✅
```

#### 三重保证总结

```
① 顺序保证：Unity 按 camera.depth 排序，foreach 顺序执行
② 内容保证：Load 模式确保叠层相机能读到底层相机的结果
③ 混合保证：One OneMinusSrcAlpha 让透明度参与混合公式
```

### 为什么不把混合模式放在 PostFXSettings 里？

```
PostFXSettings 是 ScriptableObject（资产），可以多台相机共享：
  主相机 ──┐
  小地图  ──┤── 共用同一个 PostFXSettings（相同的 Bloom/ColorGrade）
  肖像摄像─┘

如果混合模式在 PostFXSettings 里：
  改一个值 → 所有相机都变了 ❌

职责区分：
  PostFXSettings → "画什么效果"（Bloom 强度、ColorGrade 参数）
  CameraSettings → "渲完之后怎么叠到屏幕上"（混合模式、输出位置）

就像调色板和画框：
  调色板（PostFX）决定颜色
  画框（Camera）决定放在墙上哪里、和旁边画怎么搭配
```

### BlendMode 枚举为何能直接传给 Shader？

```csharp
buffer.SetGlobalFloat(finalSrcBlendId, (float)finalBlendMode.source);

// Unity 的 BlendMode 枚举值：
// Zero=0, One=1, SrcAlpha=5, OneMinusSrcAlpha=10...

// Shader 里：
// Blend [_FinalSrcBlend] [_FinalDstBlend]
// 期望整数，GPU 把它映射到对应混合因子
// (float)BlendMode.One = 1.0f → Shader 读到 1 → 对应 One ✅
```

### 问题

```
底层相机 → 用 One Zero（覆盖，正常）
叠加相机 → 用 One OneMinusSrcAlpha（透明叠加）

不同相机需要不同的 Final Pass 混合模式
→ 解决：CameraSettings 组件挂在相机上，每台相机独立配置
```

### 新增 CameraSettings 类

```csharp
// CameraSettings.cs（新文件）
using System;
using UnityEngine.Rendering;

[Serializable]
public class CameraSettings {
    [Serializable]
    public struct FinalBlendMode {
        public BlendMode source, destination;
    }
    // 默认：One Zero（覆盖，底层相机用）
    public FinalBlendMode finalBlendMode = new FinalBlendMode {
        source = BlendMode.One,
        destination = BlendMode.Zero
    };

    public bool overridePostFX = false;
    public PostFXSettings postFXSettings = default;
    public int renderingLayerMask = -1;       // -1 = All Layers
    public bool maskLights = false;
}
```

### CustomRenderPipelineCamera 组件

```csharp
// CustomRenderPipelineCamera.cs（新文件，挂到相机上）
[DisallowMultipleComponent, RequireComponent(typeof(Camera))]
public class CustomRenderPipelineCamera : MonoBehaviour {
    [SerializeField] CameraSettings settings = default;
    public CameraSettings Settings => settings ?? (settings = new CameraSettings());
}
```

### Shader 使用属性控制混合模式

```hlsl
// PostFXStack.shader Final Pass
Name "Final"
Blend [_FinalSrcBlend] [_FinalDstBlend]   // ← 属性控制，不写死
```

```csharp
// PostFXStack.cs DrawFinal 里设置属性
int finalSrcBlendId = Shader.PropertyToID("_FinalSrcBlend");
int finalDstBlendId = Shader.PropertyToID("_FinalDstBlend");

void DrawFinal(RenderTargetIdentifier from) {
    buffer.SetGlobalFloat(finalSrcBlendId, (float)finalBlendMode.source);
    buffer.SetGlobalFloat(finalDstBlendId, (float)finalBlendMode.destination);
    ...
}
```

---

## 四点五、Post FX Settings Per Camera（每台相机独立 PostFX）

### 目标

```
主相机：默认暖色调 Color Grading
小地图相机：冷色调 Color Grading
肖像相机：无后处理
→ 每台相机独立配置 PostFXSettings，不互相影响
```

### 改动一：CameraSettings.cs 加两个字段

```csharp
// 是否覆盖全局 PostFX 设置
public bool overridePostFX = false;
// 相机自己的 PostFX 设置（overridePostFX=true 时生效）
public PostFXSettings postFXSettings = default;
```

### 改动二：CameraRenderer.cs 加判断

```csharp
var crpCamera = camera.GetComponent<CustomRenderPipelineCamera>();
CameraSettings cameraSettings = crpCamera ? crpCamera.Settings : defaultCameraSettings;

// me14: 相机有自己的 PostFX → 覆盖管线传进来的全局设置
if (cameraSettings.overridePostFX) {
    postFXSettings = cameraSettings.postFXSettings;
}
// postFXSettings = null → PostFXStack.IsActive = false → 该相机无后处理
```

### 注意

```
postFXSettings = null（没有指定资产）→ 后处理完全关闭
postFXSettings = 某个资产           → 使用该资产的设置

这样可以让肖像相机把 postFXSettings 留空，就自动关闭所有后处理
```

---

## 五、渲染层遮罩（Rendering Layer Mask）

### 为什么不用 Culling Mask？

```
Culling Mask 的局限：
  ① 方向光的 Culling Mask 只影响阴影，不影响光照本身
  ② 非 LightsPerObject 模式下点光/聚光也不受 CullingMask 影响
  ③ 物体只能属于一个层

Rendering Layer Mask 的优势：
  ① 物体可以属于多个渲染层（位掩码）
  ② 专门为 SRP 设计，不干扰物理层
  ③ GPU 端做逐像素检查（比 CullingMask 更精确）
```

### GPU 端实现（Surface 与 Light 互相匹配）

```hlsl
// Surface struct 加渲染层
struct Surface {
    ...
    uint renderingLayerMask;   // 物体的渲染层掩码
};

// Light struct 加渲染层
struct Light {
    ...
    uint renderingLayerMask;   // 灯光的渲染层掩码
};

// 判断是否重叠（按位 AND）
bool RenderingLayersOverlap(Surface surface, Light light) {
    return (surface.renderingLayerMask & light.renderingLayerMask) != 0;
}

// GetLighting 里只对重叠的层计算光照
for (int i = 0; i < GetDirectionalLightCount(); i++) {
    Light light = GetDirectionalLight(i, surfaceWS, shadowData);
    if (RenderingLayersOverlap(surfaceWS, light)) {
        color += GetLighting(surfaceWS, brdf, light);
    }
}
```

### 物体 mask 怎么到 GPU 的？（`unity_RenderingLayer`）

```
MeshRenderer 组件（Inspector 里配置，CPU）：
  renderingLayerMask = 5  → 二进制 00000101

Unity 引擎每次 DrawCall 时，自动把这个值填进
UnityPerDraw 的 CBUFFER 里（和 unity_ObjectToWorld 矩阵一样，Unity 自动管理）

我们在 UnityInput.hlsl 里声明字段：
  float4 unity_RenderingLayer;   ← 告诉 GPU "我要用这块内存"

LitPassFragment 里读取：
  surface.renderingLayerMask = asuint(unity_RenderingLayer.x);
  // asuint() 把 float 的字节原样读成 uint → 还原位掩码
```

### 灯光 mask 怎么传给 GPU？（int → float → GPU → uint）

```
step1: C# 端，int 变 float（位模式不变）
  light.renderingLayerMask = 5  → 二进制 00000000 00000000 00000000 00000101

  普通 (float)5 会改变位（IEEE 754 格式）：
    → 01000000 10100000 00000000 00000000  ← GPU 读回来就乱了！

  ReinterpretAsFloat（Union Struct 共享 4 字节）：
    → 00000000 00000000 00000000 00000101  ← 位不变！

step2: 存进 Vector4.w
  dirAndMask.w = light.renderingLayerMask.ReinterpretAsFloat();
  dirLightDirectionsAndMasks[index] = dirAndMask;
  // Vector4 = (dirX, dirY, dirZ, 那个"奇怪的float")

step3: SetGlobalVectorArray 把字节原样复制给 GPU
  buffer.SetGlobalVectorArray(dirLightDirectionsAndMasksId, dirLightDirectionsAndMasks);
  // GPU 收到 float4，其 .w 的字节 = 00000000 00000000 00000000 00000101

step4: GPU 端，float 变回 uint（位模式不变）
  light.renderingLayerMask = asuint(_DirectionalLightDirectionsAndMasks[index].w);
  // asuint() = "把这 4 字节原样读成 uint" → 还原 = 5 = 00000101 ✅
```

### 完整传递链路图

```
C# int = 5（00000101）
    ↓ ReinterpretAsFloat（位不变）
C# float（字节依然是 00000101）
    ↓ 存进 Vector4.w
C# Vector4 = (dirX, dirY, dirZ, float_with_mask_bits)
    ↓ SetGlobalVectorArray（内存拷贝）
GPU float4 .w = （字节依然是 00000101）
    ↓ asuint()
GPU uint = 5（00000101）
    ↓ AND 运算
RenderingLayersOverlap：物体mask & 灯光mask ≠ 0 → 计算光照
```

### 位运算的精妙之处

```
用位掩码代替 for 循环逐层检查：
  物体属于 Layer1 + Layer3 → 00000101
  灯光影响 Layer1 + Layer2 → 00000011
  
  AND 运算：00000101
          & 00000011
          = 00000001  ≠ 0 → 有交集，计算光照

一条指令同时检查所有 32 层，比逐层遍历快 32 倍！
```

### C# 端：int 转 float 的内存重解释

```
问题：Light.renderingLayerMask 是 int（位掩码）
     但向量数组只能存 float
     普通 (float)mask 会改变位模式！

解决：用 Union Struct 实现内存重解释（不改变二进制位）
```

```csharp
// ReinterpretExtensions.cs（新文件）
using System.Runtime.InteropServices;

public static class ReinterpretExtensions {
    [StructLayout(LayoutKind.Explicit)]
    struct IntFloat {
        [FieldOffset(0)] public int intValue;    // ← 两个字段
        [FieldOffset(0)] public float floatValue; // ← 共享同4字节！
    }

    public static float ReinterpretAsFloat(this int value) {
        IntFloat converter = default;
        converter.intValue = value;
        return converter.floatValue;   // 二进制位完全相同，只是解释方式不同
    }
}

// 使用：
dirAndMask.w = light.renderingLayerMask.ReinterpretAsFloat();
// GPU 端用 asuint() 读回来
light.renderingLayerMask = asuint(_DirectionalLightDirectionsAndMasks[index].w);
```

### 阴影端为什么需要 mask？

```
如果只做光照 mask，不做阴影 mask：
  物体B（Layer2）在灯光X（Layer1）下：
    光照：RenderingLayersOverlap = false → 不接收光 ✅
    阴影：没有判断 → 物体B 还是画进了阴影贴图

  结果：物体B 不被照，但地面有它的影子 ❌（违和！）

加了 useRenderingLayerMaskTest = true：
  DrawShadows 渲染每个投影者前做 objectMask & lightMask
  mask 无交集 → 跳过，不投影 ✅  光照和阴影完全一致
```

### 阴影端 vs 光照端的实现方式

```
光照阶段（我们写的 Shader）：
  Unity 不知道我们如何算光照，不会自动帮我们过滤
  → 我们自己实现 RenderingLayersOverlap()，在三个循环里判断

阴影阶段（Unity 内置 DrawShadows）：
  Unity 内部 C++ 直接读 uint 做 AND，无需 float 中转
  → 我们只需打开开关：useRenderingLayerMaskTest = true

原则：
  我们写的代码 → 自己实现判断
  Unity 内置流程 → 打开开关，让 Unity 内部自己处理
```

### 为什么不新建 int 数组传 mask？

```
图形 API 层（D3D11/Vulkan/Metal）：
  完全支持 CBUFFER int/uint 数组，int4 = 16字节，和 float4 完全相同

Unity CommandBuffer 层不暴露 SetGlobalIntArray 的原因：
  ① 跨平台兼容性：OpenGL ES 对 int uniform 数组有限制
  ② 需求极少：Shader 数据几乎都是 float 的
  ③ float reinterpret 完全替代，几乎零开销

最优解：借用已有 float4 的空闲 .w 分量（原来就是 0，浪费的）
  .xyz = 方向（原有数据）
  .w   = mask（借用，零额外 CBUFFER 占用）
  两端各一次 reinterpret（ReinterpretAsFloat / asuint）≈ 零开销
```

---

## 六、按相机屏蔽光源（Mask Lights Per Camera）

```
标准行为：所有相机共享同一套灯光
可选功能：每台相机用自己的 renderingLayerMask 来过滤灯光

使用场景：
  ① 两台相机渲同一场景，但各自有不同的灯光效果
  ② 角色肖像摄像机：不受主场景灯光影响

注意：只影响实时光，烘焙光/混合光的间接贡献无法被遮罩
```

```csharp
// CameraSettings.cs
public bool maskLights = false;

// CameraRenderer.Render()
lighting.Setup(
    context, cullingResults, shadowSettings, useLightsPerObject,
    cameraSettings.maskLights ? cameraSettings.renderingLayerMask : -1
    //                                                               ↑ -1 = 不过滤
);
```

---

## 四点七、每台相机独立 PostFX 设置（Post FX Settings Per Camera）

```
目标：叠层相机、小地图相机等可以关闭 PostFX 或使用不同的效果
      而不影响主相机的全局 PostFXSettings

实现：CameraSettings 加 overridePostFX 和 postFXSettings 两个字段
      CameraRenderer.Render() 里判断是否覆盖
```

```csharp
// CameraSettings.cs：
public bool overridePostFX = false;
public PostFXSettings postFXSettings = default;

// CameraRenderer.Render() 里，读取 cameraSettings 后立刻检查：
if (cameraSettings.overridePostFX) {
    postFXSettings = cameraSettings.postFXSettings;
}
// 之后的 postFXStack.Setup() 就会用这台相机自己的 postFXSettings
```

```
使用场景举例：
  ① 小地图相机 → overridePostFX=true, postFXSettings=null → 关闭 PostFX
  ② 肖像相机   → 用冷色调+Neutral ToneMapping 的专用 PostFXSettings
  ③ 主相机     → 不覆盖，用管线全局的 PostFXSettings
```

---

## 七、Camera Rendering Layer Mask + Masking Lights Per Camera

### 两个功能的区别

```
Camera Rendering Layer Mask（过滤几何体）：
  相机决定"看哪些物体"（类似 CullingMask 但更灵活）
  默认 -1 = 不过滤（全部渲染）
  始终生效，不需要 maskLights 这种开关

Masking Lights Per Camera（过滤灯光）：
  相机决定"接受哪些灯光"
  Unity 自带 RP 不支持此特性，是我们自定义的非标准行为
  需要 maskLights 开关，因为默认不启用
```

### 为什么几何体不需要 maskLights 这样的开关？

```
filteringSettings.renderingLayerMask = (uint)renderingLayerMask

默认 renderingLayerMask = -1：
  -1 转 uint = 4294967295 = 11111111...（全1）
  任何物体 mask & 全1 = 物体本身 mask ≠ 0（物体 mask 不可能为 0）
  → 全部物体通过，等同于不过滤

配置了具体 mask（如 Layer1+Layer3 = 00000101）：
  物体 mask & 00000101 ≠ 0 → 渲染
  物体 mask & 00000101 = 0  → 跳过

所以"不过滤"和"有效过滤"都只需这一个字段，不需要额外开关
```

### 完整数据流

```
Inspector（CameraSettings）
  renderingLayerMask = 00000101  (Layer1+Layer3)
  maskLights = true
        ↓
CameraRenderer.Render()：
  ┌── lighting.Setup(..., mask = 00000101)
  │       ↓ 灯光系统（我们自己实现过滤）
  │   SetupLights for 循环：
  │     灯光X Layer1 → 00000001 & 00000101 = 1 ≠ 0 → 加进GPU数组
  │     灯光Y Layer2 → 00000010 & 00000101 = 0   → 跳过，对该相机不存在
  │
  └── DrawVisibleGeometry(..., mask = 00000101)
          ↓ 几何体渲染（Unity 内置实现过滤）
      FilteringSettings(renderingLayerMask = 0b00000101)
        物体A Layer1 → 00000001 & 00000101 = 1 ≠ 0 → DrawCall
        物体B Layer2 → 00000010 & 00000101 = 0   → 跳过，不渲染
```

### FilteringSettings 是 Unity 内置过滤

```csharp
// 我们只需要传参数，Unity DrawRenderers 内部自动完成 AND 判断
var filteringSettings = new FilteringSettings(
    RenderQueueRange.opaque,
    renderingLayerMask: (uint)renderingLayerMask  // ← 告诉 Unity 用这个 mask 过滤
);
context.DrawRenderers(cullingResults, ref drawingSettings, ref filteringSettings);
// Unity 内部：每个可见物体：
//   meshRenderer.renderingLayerMask & filteringSettings.renderingLayerMask != 0 → 才画
```

```
注意：透明物体不需要重设 mask！
  filteringSettings 是 struct，修改 renderQueueRange 后
  renderingLayerMask 保持原值，透明物体自动使用同一 mask 过滤
```

### 代码改动：SetupLights 灯光过滤

```csharp
void SetupLights(bool useLightsPerObject, int renderingLayerMask)
{
    for (i = 0; i < visibleLights.Length; i++) {
        Light light = visibleLight.light;
        // me14: 相机 mask 过滤灯光
        // maskLights=false → renderingLayerMask=-1（全1），任何灯光 AND -1 != 0 → 全通过
        // maskLights=true  → 用真实 mask，只有交集的灯光才生效
        if ((light.renderingLayerMask & renderingLayerMask) != 0) {
            switch (lightType) { /* 正常配置灯光 */ }
        }
        // 无交集 → 跳过，该灯光对这台相机"不存在"
    }
}
```

### 四套 Rendering Layer Mask 机制汇总

```
① 几何体过滤（相机决定渲哪些物体）：
   FilteringSettings.renderingLayerMask = (uint)mask
   → Unity DrawRenderers 内部处理，传参数即可

② GPU 光照过滤（逐像素：物体和灯光 mask 匹配才计算光照）：
   RenderingLayersOverlap() 在 Lighting.hlsl 里
   → 我们自己写的 Shader 判断，GPU 执行

③ 阴影投影过滤（物体和灯光 mask 不匹配则不投影）：
   ShadowDrawingSettings.useRenderingLayerMaskTest = true
   → Unity DrawShadows 内部处理，打开开关即可

④ 灯光过滤 Per Camera（相机决定接受哪些灯光）：
   SetupLights 循环里 AND 判断
   → 我们自己实现，Unity 内置 RP 不支持此特性

规律：
  Unity 内置流程 → 传参数或打开开关
  我们写的代码  → 自己实现判断逻辑
```

---



```
新建文件：
  CameraSettings.cs             相机配置（混合模式/层遮罩/PostFX覆盖）
  CustomRenderPipelineCamera.cs 挂到相机上的组件
  ReinterpretExtensions.cs      int→float 内存重解释扩展方法
  RenderingLayerMaskFieldAttribute.cs  自定义 Inspector 属性标记

修改文件：
  PostFXStack.cs                DrawFinal() 替换 Draw()，加 Viewport 恢复
  PostFXStack.shader            Final Pass Blend 改为属性控制
  PostFXStackPasses.hlsl        Bloom 最终 Pass 保留 alpha
  CameraRenderer.cs             读取 CameraSettings，通透传参数
  Lighting.cs                   SetupLights 加 renderingLayerMask 过滤
  Shadows.cs                    ShadowDrawingSettings 开启 useRenderingLayerMaskTest
  LitInput.hlsl / UnlitInput.hlsl  加 _ZWrite + GetFinalAlpha()
  Lit.shader / Unlit.shader     Blend 模式加 alpha 通道分量
  UnityInput.hlsl               加 unity_RenderingLayer 字段

Editor 端：
  CustomLightEditor.cs          加渲染层 Inspector + CullingMask 警告
  CustomRenderPipelineAsset.cs  partial class + renderingLayerMaskNames
  RenderingLayerMaskDrawer.cs   自定义 PropertyDrawer（可复用）
```

---

## 重要概念速查表

| 概念 | 说明 |
|---|---|
| **Viewport Rect** | 改变相机宽高比（窄化视锥体）并决定输出区域；不是「切掉一半视野」 |
| **camera.pixelWidth** | 已考虑 Viewport，分屏时自动缩小（W=0.5 → pixelWidth=960） |
| **SetupCameraProperties** | 传 VP 矩阵+Viewport 给 GPU，副作用是绑定 RT |
| **SetViewport** | 需要在 SetRenderTarget 之后手动调用，恢复正确区域 |
| **GPU Clipping** | 自动裁掉 NDC±1 外的三角形，NDC 就是视锥体边界 |
| **Final Pass UV** | 和 NDC 绑定（人为硬编码），普通场景渲染的 UV 来自 Mesh 顶点，两者不同 |
| **FinalBlendMode** | One Zero=覆盖（底层），One OneMinusSrcAlpha=叠层 |
| **RenderingLayerMask** | 位掩码，物体和灯光都有，AND!=0 才计算光照 |
| **ReinterpretAsFloat** | Union Struct，把 int 的比特原样当 float 传给 GPU |
| **asuint()** | HLSL 端，把 float 的比特原样读成 uint |
