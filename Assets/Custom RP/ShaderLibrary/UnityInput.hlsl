#ifndef CUSTOM_UNITY_INPUT_INCLUDED
#define CUSTOM_UNITY_INPUT_INCLUDED



// 把所有每帧变化的矩阵放入 UnityPerDraw
//把同一个材质的所有信息都发送到显存的某一块去,包含同一材质所有物体的位置信息等等,和一份对应的材质信息
//这样cpu叫gpu干活只需要叫他去取对应位置比如材质A信息+材质A所有物体的信息即可,
//对应的MPB (MaterialPropertyBlock)就会改变材质信息,生成材质B 就不会走SRP batcher了,
CBUFFER_START(UnityPerDraw)
    float4x4 unity_ObjectToWorld;
float4x4 unity_WorldToObject;
// 这是一个特殊的 Hack：
// SRP Batcher 要求 UnityPerDraw 必须包含 unity_LODFade
// 即使我们不用它，也必须占个位，否则内存对齐会错，导致闪烁
float4 unity_LODFade; 
float4 unity_WorldTransformParams;
CBUFFER_END





// 摄像机相关的矩阵不需要进 UnityPerDraw，因为它们是全局的 (PerFrame)
float4x4 unity_MatrixVP;
float4x4 unity_MatrixV;
// Unity 2022 新增/必需的矩阵
float4x4 unity_MatrixInvV;      // 视图逆矩阵
float4x4 unity_prev_MatrixM;    // 上一帧的模型矩阵（用于运动矢量）
float4x4 unity_prev_MatrixIM;   // 上一帧的逆模型矩阵
float4x4 glstate_matrix_projection;// 投影矩阵
float3 _WorldSpaceCameraPos;// 这是一个特殊向量，包含时间等信息，Core库依赖它


// -----------------
#endif


/*这是一个非常棒的暂停。**如果不懂原理，只抄代码，就是“因果倒置”。**

SRP Batcher 的核心原理可以用一句话概括：**“数据驻留，只更新指针”。**

为了让你彻底理解，我需要对比一下**“旧时代”**和**“SRP 时代”**的区别。

---

### 1. 旧时代的渲染逻辑 (Standard Rendering)
假设你要画 10 个红色的球。

*   **CPU**: "GPU 老兄，我要画第 1 个球。"
*   **CPU**: "这是它的颜色（红色），这是它的位置。" **(上传数据 -> 慢)**
*   **GPU**: "画好了。"
*   **CPU**: "我要画第 2 个球。"
*   **CPU**: "这是它的颜色（红色），这是它的位置。" **(再次上传同样的数据 -> 慢)**
*   **GPU**: "画好了。"
*   ...
*   **痛点**：哪怕 10 个球颜色一样，CPU 每次都要重新设置一遍材质参数（SetPass Call）。这就像你每次去超市买同样的面包，收银员都要重新把价格输入一遍，而不是直接扫码。

---

### 2. SRP Batcher 的原理 (CBUFFER 驻留)
SRP Batcher 引入了 **CBUFFER (Constant Buffer / 常量缓冲区)** 的概念。它把数据拆分成了两块：

#### A. 材质数据 (`UnityPerMaterial`)
*   **内容**：颜色、贴图、光泽度等。
*   **特点**：**持久化驻留显存 (VRAM)**。
*   只要你不改材质球的属性，这块数据就**永远躺在 GPU 里**，不需要每帧上传。

#### B. 物体数据 (`UnityPerDraw`)
*   **内容**：位置矩阵 (M)、逆矩阵 (I_M) 等。
*   **特点**：**大块显存**。
*   Unity 会在显存里开辟一大块区域，把这 10 个球的位置矩阵一次性填进去。

#### SRP Batcher 的渲染流程：
*   **Setup**: 游戏开始时，红色材质的数据上传到 GPU 的 CBUFFER A。
*   **Draw 1**: CPU 说：“用 CBUFFER A 的材质数据，位置数据在 CBUFFER B 的**第 0 行**。”
*   **Draw 2**: CPU 说：“还是用 CBUFFER A，位置数据在 CBUFFER B 的**第 1 行**。”
*   **Draw 3**: CPU 说：“还是用 CBUFFER A，位置数据在 CBUFFER B 的**第 2 行**。”

**核心优势**：
CPU 不再传输材质数据了！它只是在告诉 GPU **“去内存的哪个偏移量 (Offset) 找数据”**。
**绑定缓冲区 (Bind Buffer) 是极快的，上传数据 (Upload Data) 是慢的。**

---

### 3. 为什么 MPB (MaterialPropertyBlock) 会打断 SRP Batcher？

现在你理解了原理，就能明白为什么 MPB 会破坏它。

*   **场景**：你有 10 个球，共用**同一个材质球**（在显存里是 CBUFFER A）。
*   **操作**：你给第 5 个球挂了 MPB，把颜色改成了**蓝色**。
*   **冲突**：
    *   SRP Batcher 尝试复用 CBUFFER A（里面写着是红色）。
    *   但是第 5 个球说：“不行，我是蓝色的，CBUFFER A 里的数据对我是错的。”
    *   **结果**：渲染管线被迫中断 Batch 流程，专门为第 5 个球建立一个新的临时上下文，上传蓝色数据，画完它，再切回 SRP Batcher 画剩下的红色球。

这就是为什么我说：**MPB 会打断 SRP Batcher。**

### 总结
*   **SRP Batcher** = 材质数据在 GPU 显存里**不动**，CPU 只快速切换位置指针。
*   **条件**：Shader 里的变量定义（内存布局）必须和 C# 里的数据块严格对齐（这就是为什么我们要写 `CBUFFER_START`）。

这个逻辑现在清晰了吗？如果清晰了，我们再看怎么用 **GPU Instancing** 来解决“既要不同颜色，又要合批”的问题。*/