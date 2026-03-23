# 第十一章 Post Processing — 核心笔记

---

## 一、整体架构改动

### 旧流程 vs 新流程

```
旧（第1~10章）：
  场景渲染 → 直接写入 FrameBuffer（屏幕）→ 显示

新（第11章起）：
  场景渲染 → 写入中间RT → [Post FX Stack 处理] → 写入屏幕 → 显示
```

### 新增文件

| 文件 | 职责 |
|---|---|
| `PostFXSettings.cs` | ScriptableObject，存 Bloom 参数 + Shader 引用 |
| `PostFXStack.cs` | 后处理执行器（Setup/Render/DoBloom） |
| `PostFXStack.Editor.cs` | Scene 窗口后处理开关 |
| `PostFXStackPasses.hlsl` | 所有后处理 HLSL Pass |
| `PostFXStack.shader` | 5-Pass Shader |

---

## 二、CommandBuffer 执行模型

SRP 的核心不是"立即执行"，而是**先录制再提交**：

```
buffer.SetRenderTarget(...)   ← 录磁带（命令入队）
buffer.ClearRenderTarget(...) ← 录磁带
buffer.DrawProcedural(...)    ← 录磁带

context.ExecuteCommandBuffer(buffer)  ← 把磁带交给 context 收集
buffer.Clear()                        ← 清空磁带容器，准备下次录

context.Submit()              ← 把所有命令发给 GPU 真正执行
```

**GPU 严格按命令顺序线性执行，中间没有并行。**

---

## 三、中间帧缓冲（临时 RT）

### 为什么需要中间 RT

后处理需要"读取整张画面再处理"，不能边读边写屏幕。所以加了一个中转站：

```csharp
// 申请（和屏幕等大）
buffer.GetTemporaryRT(
    frameBufferId, camera.pixelWidth, camera.pixelHeight,
    32, FilterMode.Bilinear, RenderTextureFormat.Default
);
// 切换渲染目标到这张 RT
buffer.SetRenderTarget(frameBufferId, RenderBufferLoadAction.DontCare, ...);
// 场景正常渲染...
// 后处理读取 frameBufferId，输出到屏幕
postFXStack.Render(frameBufferId);
// 释放
buffer.ReleaseTemporaryRT(frameBufferId);
```

### 强制清色的原因

```csharp
if (flags > CameraClearFlags.Color) {
    flags = CameraClearFlags.Color;
}
```

`CameraClearFlags` 枚举值大小：Skybox=1 < Color=2 < Depth=3 < Nothing=4

- Depth(3) 和 Nothing(4) 都大于 Color(2)
- 这两种设置意味着"不清颜色，保留上一帧"
- 但中间 RT 是新申请的，里面是显存中的**随机垃圾数据**
- "保留上一帧" = 保留垃圾 → 画面花屏
- 所以强制至少清掉颜色

### 对比阴影贴图 RT

| | 阴影贴图 RT | 中间帧缓冲 RT |
|---|---|---|
| 尺寸 | 固定（如 2048×2048） | 随相机分辨率 |
| 格式 | `Shadowmap`（深度专用） | `Default`（RGBA 颜色） |
| Load 策略 | `DontCare`（每帧重绘） | `DontCare`（马上清除） |
| 使用方式 | HLSL 采样深度比较阴影 | HLSL 采样颜色做后处理 |
| 释放时机 | `Shadows.Cleanup()` | `CameraRenderer.Cleanup()` |

---

## 四、渲染目标状态机

渲染目标是**全局状态**，最后一次 `SetRenderTarget` 决定后续所有绘制去哪里。

```csharp
// Setup() 里：
buffer.SetRenderTarget(frameBufferId, ...);   // 目标 = 中间RT

// 之后所有绘制都进中间RT：
DrawVisibleGeometry();         // → 中间RT
DrawGizmosBeforeFX();          // → 中间RT（没人改过目标）

// PostFXStack.Render() 内部最后一次 Draw：
buffer.SetRenderTarget(CameraTarget, ...);    // 目标 = 屏幕

// 之后就写到屏幕：
DrawGizmosAfterFX();           // → 屏幕（目标已被改成屏幕）
```

**不需要任何额外操作，顺序本身就决定了一切。**

---

## 五、Gizmos 拆分

### 为什么要拆

后处理（Bloom）会对整张画面做模糊，Gizmos 的两种子集对此有不同需求：

- `PreImageEffects`：应该融进场景（随场景一起被 Bloom 影响）
- `PostImageEffects`：必须保持清晰（选中框、Transform 箭头要看清楚）

### `GizmoSubset` 是分类标签，不是渲染选项

```csharp
context.DrawGizmos(camera, GizmoSubset.PreImageEffects);
//                          ↑
//  这只是告诉 Unity "把 Pre 这一批 Gizmos 画出来"
//  画到哪里 = 完全取决于调用时机（当前的渲染目标是什么）
```

Unity 内部已经把 Gizmos 分好类了（你无法更改）：
- 灯光锥形、摄像机视锥 → PreImageEffects（融进3D世界）
- 选中框、Transform 箭头 → PostImageEffects（永远清晰）

### 执行顺序

```csharp
DrawGizmosBeforeFX();          // 当前RT=中间RT → 随场景被 Bloom 处理
postFXStack.Render(frameBufferId); // 处理后输出到屏幕
DrawGizmosAfterFX();           // 当前RT=屏幕 → 直接叠到最终画面
```

---

## 六、全屏三角形（程序化顶点）

后处理不需要 Mesh，用 3 个顶点的超大三角形覆盖屏幕：

```hlsl
Varyings DefaultPassVertex(uint vertexID : SV_VertexID) {
    // ID=0: (-1,-1) UV(0,0) — 左下
    // ID=1: (-1, 3) UV(0,2) — 左上（超出屏幕）
    // ID=2: ( 3,-1) UV(2,0) — 右下（超出屏幕）
    // GPU 自动裁剪超出部分，比四边形（6个顶点）省一个三角形
    
    // _ProjectionParams.x < 0 时翻转 V（某些平台 RT 坐标系不同）
    if (_ProjectionParams.x < 0.0) {
        output.screenUV.y = 1.0 - output.screenUV.y;
    }
}
```

---

## 七、Bloom 实现原理

### 金字塔流程

```
原图 → [BloomPrefilter: 阈值过滤 + 半分辨率]
     → Level0: [BloomHorizontal] → [BloomVertical]  (960×540)
     → Level1: [BloomHorizontal] → [BloomVertical]  (480×270)
     → Level2: ...（最多 maxIterations 次）

↑ 升采样（反向）：
     [BloomCombine: 当前层 + 上一层叠加]
     ...直到原分辨率
     最后叠加原图，乘以 intensity，输出到屏幕
```

### 高斯模糊的两步

- **BloomHorizontal**：9点高斯采样 + 横向降采样 2x（每个点平均 2×2 源像素）
- **BloomVertical**：5点优化高斯（利用双线性过滤，9点压缩到5点），不降采样

### Bloom 参数

| 参数 | 作用 |
|---|---|
| `maxIterations` | 最多几层金字塔（越多光晕越大） |
| `downscaleLimit` | 缩到多小停止（避免意义不大的极小层） |
| `threshold` | 亮度阈值（低于此值不参与 Bloom） |
| `thresholdKnee` | 阈值过渡曲线（0=硬切，1=最软） |
| `intensity` | 最终 Bloom 强度 |
| `bicubicUpsampling` | 升采样用双三次插值（更平滑，4次采样代替1次） |

---

## 八、CPU vs GPU 分工

### 核心模型

CPU 和 GPU 是两条独立的流水线，**CPU 不等 GPU 画完就继续录制下一帧**：

```
CPU 这帧（录制，非常快）：
  写剧本（录所有命令到 buffer）
  Submit() → 推给 GPU，CPU 去录下一帧

GPU 这帧（执行，慢慢画）：
  按序执行命令队列
  同一帧内命令串行：上一条没完成下一条不开始
  上一帧还在执行时，CPU 已在录下下一帧
```

### 一帧内的命令顺序（后处理场景）

```
── CPU 录入 CameraRenderer.buffer ──

[申请显存] GetTemporaryRT("_CameraFrameBuffer", 1920×1080)
[改写入目标] SetRenderTarget → _CameraFrameBuffer
[清除] ClearRenderTarget
[画场景] DrawRenderers
[画Gizmos1] DrawGizmos(PreImageEffects)

── CPU 录入 PostFXStack.buffer（DoBloom 调用时）──

[申请显存] GetTemporaryRT("_BloomPrefilter", 960×540)
[改纹理槽] SetGlobalTexture(_PostFXSource → _CameraFrameBuffer)
[改写入目标] SetRenderTarget → _BloomPrefilter
[触发shader] DrawProcedural(Pass.BloomPrefilter, 3顶点)

[申请显存] GetTemporaryRT(mid, 960×540)
[申请显存] GetTemporaryRT(lv1, 960×540)
[改纹理槽] _PostFXSource → _BloomPrefilter
[改写入目标] → mid
[触发shader] DrawProcedural(Pass.BloomHorizontal)
[改纹理槽] _PostFXSource → mid
[改写入目标] → lv1
[触发shader] DrawProcedural(Pass.BloomVertical)

... 更多层 ...

[设置float] _BloomIntensity = intensity
[改纹理槽] _PostFXSource → 最小层, _PostFXSource2 → _CameraFrameBuffer
[改写入目标] → 屏幕
[触发shader] DrawProcedural(Pass.BloomCombine)  ← 最终输出

── CPU 继续录 CameraRenderer.buffer ──

[画Gizmos2] DrawGizmos(PostImageEffects)
[释放显存] ReleaseTemporaryRT(各Bloom RT)
[释放显存] ReleaseTemporaryRT(_CameraFrameBuffer)

context.Submit()  ← CPU 把全部命令推给 GPU，去忙下一帧
```

### GPU 执行时做什么

```
[触发shader] DrawProcedural 时 GPU 做：
  ① 生成3个程序化顶点（不需要Mesh）
  ② 光栅化 → 确定覆盖目标RT的哪些像素
  ③ 每个像素运行 fragment shader：
       读 _PostFXSource（SetGlobalTexture 设好的槽）
       执行对应 Pass 的 HLSL 逻辑
       写进当前渲染目标（SetRenderTarget 设好的）
```

---

## 九、CPU 控制 GPU 的命令分类

```csharp
// ── 1. 显存资源管理 ──
buffer.GetTemporaryRT(id, w, h, depth, filter, format)   // 借一块显存（RT）
buffer.ReleaseTemporaryRT(id)                            // 还回去

// ── 2. 渲染目标（写到哪里）──
buffer.SetRenderTarget(rt, LoadAction, StoreAction)      // 切换写入目标
buffer.ClearRenderTarget(clearDepth, clearColor, color)  // 清除当前目标

// ── 3. 传数据给 Shader ──
buffer.SetGlobalTexture(id, rt)          // 改纹理槽指针
buffer.SetGlobalFloat(id, value)         // 传 float
buffer.SetGlobalVector(id, vector4)      // 传 Vector4
buffer.SetGlobalVectorArray(id, array)   // 传数组
buffer.SetGlobalInt(id, value)           // 传 int

// ── 4. 触发绘制 ──
context.DrawRenderers(culling, drawing, filtering)   // 画场景几何体
buffer.DrawProcedural(matrix, material, pass, topo, verts)  // 画程序化顶点
context.DrawSkybox(camera)              // 画天空盒
context.DrawGizmos(camera, subset)      // 画 Gizmos（编辑器）

// ── 5. Shader 关键字开关 ──
buffer.EnableShaderKeyword("_XXX")      // 开启变体
buffer.DisableShaderKeyword("_XXX")     // 关闭变体

// ── 6. 调试标记 ──
buffer.BeginSample("name")             // Profiler/FrameDebugger 开始
buffer.EndSample("name")               // 结束
```

**CPU 能控制的是：资源、状态、参数、触发**  
**CPU 不能控制的是：逐像素的 HLSL 计算过程（那是 Shader 的事）**

---

## 十、`Draw()` 三行的含义

```csharp
void Draw(RenderTargetIdentifier from, RenderTargetIdentifier to, Pass pass)
{
    buffer.SetGlobalTexture(fxSourceId, from);
    // ↑ 改 GPU 纹理槽：_PostFXSource 指针 = from 的显存地址
    // HLSL 里 TEXTURE2D(_PostFXSource) 就能读到 from 的内容

    buffer.SetRenderTarget(to, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
    // ↑ 改 GPU 渲染目标寄存器 = to 的显存地址
    // 后续 DrawProcedural 的 SV_TARGET 写到这里

    buffer.DrawProcedural(Matrix4x4.identity, settings.Material, (int)pass, MeshTopology.Triangles, 3);
    // ↑ 触发执行：
    //   - 用 PostFXStack.shader 的第 pass 个 Pass
    //   - 3个程序化顶点覆盖整个目标
    //   - GPU 读 from，处理，写 to
}

// 语义 = "把 from 这张图，经过 pass 处理，写进 to 这张图"
```

---

## 十一、材质贴图 vs SetGlobalTexture — 都是 CPU 改纹理槽

### 关键认知

**两种方式本质相同，都是 CPU 录制"改纹理槽"命令交给 GPU，区别只是谁来写这段代码。**

### 材质球贴图（Unity 自动做）

```
你在 Inspector 把草地贴图拖到 _MainTex 槽
             ↓
这个信息存在 Material 对象里

DrawRenderers 时，Unity 内部对每个物体自动执行：
  buffer.SetGlobalTexture("_MainTex", 材质球里那张贴图)
  ← 这句话 Unity 帮你写了，你看不见但确实发生
```

**为什么能自动？因为 MeshRenderer → Material → _MainTex 之间有绑定关系，Unity 知道每个 DrawCall 用什么贴图，可以替你生成命令。**

### 后处理贴图（你手动做）

```
后处理没有物体、没有 MeshRenderer、没有材质球
没有任何东西告诉 Unity "_PostFXSource 应该对应哪张 RT"

所以只能你自己写：
  buffer.SetGlobalTexture(fxSourceId, from);
```

### 对比表

| | 普通材质贴图 | 后处理纹理 |
|---|---|---|
| **谁写命令** | Unity 帮你写 | 你自己写 |
| **什么时候设** | DrawRenderers 时每个物体自动切 | Draw() 前手动设 |
| **绑定关系** | MeshRenderer → Material → 贴图 | 无绑定关系，纯手动 |
| **CPU 侧命令** | `SetGlobalTexture`（隐式） | `SetGlobalTexture`（显式） |
| **GPU 侧结果** | 完全相同：纹理槽指向对应显存 | 完全相同 |

---

## 十二、Shader vs Material — 声明需求 vs 提供数值

### Unity 不读 Shader 来决定 CPU 行为

Unity **不是**"先检查 shader 变量再决定发什么"，而是读 **Material 的属性表**发给 GPU。

```
Shader  = 声明"我需要 _MainTex"（只是变量声明）
Material = 存储"_MainTex = 草地贴图"（实际的值）

两者解耦，运行时 Unity 只看 Material，不检查 Shader
```

### 运行时流程

```
编辑阶段（拖贴图时）：
  Material A 内部属性表：
    _MainTex   → [草地贴图引用]
    _NormalMap → [法线贴图引用]
    _Color     → (0.5, 0.8, 0.3, 1.0)

运行时 DrawRenderers：
  对每个可见物体 →
    取出它绑定的 Material A →
    把 Material A 整个属性表打包发给 GPU →
    GPU shader 按变量名匹配读值
```

### 为什么后处理不能这样

```
材质球属性 = 编辑时预先确定的固定值，适合静态资源

后处理的 _PostFXSource = 每帧都不同的 RT
  第1帧：场景画面 960×540
  第2帧：同分辨率但内容不同
  某帧：可能分辨率都变了（窗口缩放）

→ 无法预先存进材质球
→ 必须每帧 CPU 手动 SetGlobalTexture
```

### 一句话总结

**Material 是属性的容器（存数值），Shader 是计算的声明（用数值）。Unity 运行时把 Material 的属性表发给 GPU，GPU shader 按名字消费。后处理没有 Material 容器，所以手动发。**

---

## 十三、DefaultPassVertex — 程序化全屏三角形

### 输入：只有顶点 ID

```hlsl
Varyings DefaultPassVertex(uint vertexID : SV_VertexID)
// 没有 Mesh，没有顶点缓冲
// DrawProcedural(..., 3) → GPU 自动给三个顶点传入 0、1、2
```

普通 LitPassVertex 输入 `Attributes`（来自 Mesh 的坐标/UV/法线），后处理顶点 shader 只有一个整数，坐标和 UV 全靠数学现算。

### 三个顶点的值

```
vertexID=0 → positionCS=(-1,-1)  screenUV=(0,0)   ← 左下角
vertexID=1 → positionCS=(-1, 3)  screenUV=(0,2)   ← 左上（超出屏幕）
vertexID=2 → positionCS=( 3,-1)  screenUV=(2,0)   ← 右下（超出屏幕）
```

超出 NDC[-1,1] 的部分被 GPU 裁剪掉，裁剪后 UV 有效范围恰好是 [0,1]×[0,1]。

### Fragment 拿到的 screenUV

顶点 shader 只对3个顶点运行，但屏幕上有几百万像素。GPU 在光栅化阶段自动为三角形内每个像素插值出 UV：

```
左下像素 → UV(0.0, 0.0)
右上像素 → UV(1.0, 1.0)
中间像素 → UV(0.5, 0.5)
...每个像素都不同
```

`Varyings input` 传入 fragment shader 时，`input.screenUV` 已经是 GPU 插值好的、这个像素专属的值。

---

## 十四、下采样与上采样的本质

### 核心认知：Shader 不管分辨率，RT 大小决定一切

```
SetRenderTarget(小RT) → fragment 少跑 → 下采样
SetRenderTarget(大RT) → fragment 多跑 → 上采样

screenUV 永远是 [0,1]，永远覆盖整张纹理
```

### 下采样（BloomHorizontal）

```
输入RT：960×540（bloomPrefilter）
输出RT：480×270（mid，宽和高都是一半！）← C# 里 width/2, height/2 申请的

输出像素 UV(0.5, 0.5) 的横向计算：
  横向9点，offset * 2 * texelSize.x（texelSize.x = 1/960）
  偏移量 *2 是因为同时做2x降采样（相邻采样点隔2个源像素）
  9个采样点加权平均 → 写进这个 480×270 的像素
```

**Q：横向 Pass 为什么同时缩了纵向分辨率？**

这是常见的误区：以为 BloomHorizontal 只缩横向。实际上**输出 RT 的宽和高都已经减半（480×270）**。

```
输出 270 行 → fragment shader 只跑 270 次
每行的 screenUV.y 间隔 = 1/270 ≈ 0.0037

对应输入 540 行：
  UV.y = 0.0037 → 输入第 0.0037 × 540 = 2.0 行
  相邻两个输出行的 UV.y 差距 = 2 个输入行

即：每个输出行"覆盖"了输入的2行
GPU 双线性在采样 UV(x, 0.0037) 时，自动混合输入第1行和第2行
→ 纵向也完成了2x降采样（隐式，by GPU bilinear）
```

结论：
- **横向**：9点高斯显式精确模糊 + *2采样间距实现降采样
- **纵向**：GPU 双线性隐式完成降采样（没有9点，精度略低于横向）

### 纵向 Pass — 在缩好的图上精确模糊

```
BloomVertical 输入480×270，输出480×270（尺寸完全不变）

此时图已经是480×270了，只需要补上纵向的精确高斯模糊：
  5点采样，offset * texelSize.y（texelSize.y = 1/270），没有 *2
  
  为什么是5点而不是9点？
  利用双线性插值技巧：两个相邻采样点合并成一次采样（偏移放在两点之间）
  GPU硬件自动按权重插值，9点 → 5次采样，效果等价，采样数减44%
```

**两 Pass 合起来：**
```
BloomHorizontal：横向精确高斯 + 两个维度都降采样（横向显式，纵向隐式）
BloomVertical：  纵向精确高斯 + 尺寸不变

= 一次完整 2D 高斯模糊，下一级的完整分辨率是上一级的 1/4（宽半×高半）
```

### 上采样（BloomCombine）

```
_PostFXSource  = 240×135（小 Bloom 层，低频光晕）
_PostFXSource2 = 480×270（上一层，高频细节）
输出RT         = 480×270

对输出 UV(0.5, 0.5)：

  采 _PostFXSource(240×135) at UV(0.5, 0.5)：
    → 映射到像素坐标 (120, 67.5)（非整数！）
    → GPU 双线性：取附近4个像素插值 → 平滑的低频光晕
    （可选双三次：取周围16像素，更圆滑）

  采 _PostFXSource2(480×270) at UV(0.5, 0.5)：
    → 映射到像素坐标 (240, 135)（精确）
    → 直接取值 → 清晰的上层细节

  输出 = lowRes × intensity + highRes
```

**小图被"拉伸"到大图空间，GPU 双线性自动插值，不需要写代码处理。**

---

## 十五、Bloom 完整流程总览（整合版）

```
原图(1920×1080)
  ↓ [Prefilter] 阈值膝盖曲线过滤，输出 960×540
  960×540（bloomPrefilter）
  ↓ [Horizontal] 9点横向高斯(*2) + RT=480×270（宽高都缩半，纵向隐式降采样）
  480×270（mid0）
  ↓ [Vertical] 5点纵向高斯（不缩，精确补上纵向模糊）
  480×270（lv0，这级完整高斯结果）
  ↓ [Horizontal] → 240×135（mid1）
  ↓ [Vertical]   → 240×135（lv1）
  ↓ ... maxIterations 次或尺寸 < downscaleLimit 停止

升采样（逆向）：
  lv2 + lv1 → result（写进 mid1 的空位）
  result + lv0 → result2（写进 mid0 的空位）
  result2 × intensity + 原图 → 屏幕

每级中间层（midN）：用完立即释放，还回显存池
每级最终层（lvN）：升采样读完后释放
```

### 各 Pass 功能一览

| Pass | 输入→输出尺寸 | 横向 | 纵向 |
|---|---|---|---|
| **Prefilter** | 原图→1/2 | 隐式双线性降采样 | 隐式双线性降采样 |
| **Horizontal** | N→N/4像素（宽高各半） | 9点高斯显式精确 | 双线性隐式 |
| **Vertical** | N→N（不变） | 不采样 | 5点高斯显式精确 |
| **Combine** | 小→大 | 双线性/双三次上采样 + 叠加 | 同左 |
| **Copy** | 不变 | 直接一次采样 | 同左 |

---

## 十六、Gizmos 渲染时机与机制

### Gizmos 是什么

```
仅在 Editor 里存在，不进入 Build（被 #if UNITY_EDITOR 包住）

来源：
  Unity 内置：Light 锥、Camera 视锥、Collider 框 → 绑定在组件上自动显示
  自定义代码：void OnDrawGizmos() { Gizmos.DrawWireSphere(...); }
  
不是 Mesh，不是材质球，是 Unity 内部的一套辅助绘制系统
```

### Scene View vs Game View

```
Scene View（场景编辑窗口）：
  Editor 里飞来飞去查看场景的那个窗口
  有内置的"编辑器相机"（CameraType.SceneView）
  Gizmos 在这里显示

Game View（游戏视图）：
  模拟玩家运行时的画面
  使用你场景里的 Main Camera（CameraType.Game）
  没有 Gizmos，玩家看不到
```

`Handles.ShouldRenderGizmos()` 就是在判断当前相机是不是 Scene View 相机，不是则跳过整个 DrawGizmos。

### 两个 GizmoSubset

Unity 内部已经把所有 Gizmos 分好类，你无法更改：

| Gizmo | 分类 | 理由 |
|---|---|---|
| 灯光锥形（Spot） | **Pre**ImageEffects | 存在于3D世界，要融入场景 |
| 摄像机视锥 / 相机图标 | **Pre** | 同上 |
| Collider 绿色框 | **Pre** | 同上 |
| 自定义 `OnDrawGizmos()` | **Pre** | 默认 Pre |
| 选中框（橙色轮廓） | **Post**ImageEffects | 必须永远清晰 |
| Transform 箭头/圆圈 | **Post** | 同上 |
| `OnDrawGizmosSelected()` | **Post** | 选中时显示，需清晰 |

### 渲染时机决定效果

```csharp
DrawVisibleGeometry();           // 场景 → 中间RT

DrawGizmosBeforeFX();            // Pre Gizmos → 中间RT（和场景在一起）
//  ↑ 被写进中间RT，会经过后处理（Bloom等）

postFXStack.Render(frameBufferId);
// PostFX：读中间RT（场景+PreGizmos）→ 写到屏幕

DrawGizmosAfterFX();             // Post Gizmos → 屏幕（直接叠在最终画面上）
//  ↑ 绕过了后处理，永远清晰
```

**不需要任何额外判断——调用时机（当前RT是什么）决定了一切。**

### 验证方法

```
✅ 灯光图标 = Pre → 画进中间RT → 理论上受Bloom（但图标颜色通常不超threshold，视觉不明显）
✅ 选中框   = Post → 直接写屏幕 → 无论Bloom多强都清晰

测试：把 intensity 调到 5（强 Bloom），选中一个物体
  → 选中框依然锐利 = DrawGizmosAfterFX 工作正常
  → 如果选中框模糊 = 代码顺序写错了
```
