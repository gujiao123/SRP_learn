# Unity Shader 变量系统 — 核心笔记

---

## Shader 的三层结构

```
Shader "Custom RP/Example"
{
    Properties          ← 第一层：Inspector 可见参数
    {
        _Color(...)
        _MainTex(...)
    }

    SubShader
    {
        Pass
        {
            Blend [_X] [_Y]   ← 第二层：ShaderLab 渲染状态命令
            ZWrite Off

            HLSLPROGRAM        ← 第三层：GPU 计算代码
                float4 _Color;
                float4 frag() { return _Color; }
            ENDHLSL
        }
    }
}
```

---

## 三层详解

### 第一层：Properties 块

```
Properties {
    _MyColor ("颜色", Color)   = (1,1,1,1)
    _MyFloat ("强度", Float)   = 1.0
    _MyTex   ("贴图", 2D)      = "white" {}
    _MyRange ("范围", Range(0,1)) = 0.5
}
```

**特点：**
- Inspector 里能看到和拖拽调整
- 美术 / 策划可以直接操作
- 必须在 HLSL 里也声明同名变量，GPU 才能用这个值
- 每个 Material 实例独立存储（不同材质球可以有不同值）

**C# 读写方式：**

```csharp
// 通过 Material（只改这个材质实例）
material.SetColor("_MyColor", Color.red);
material.SetFloat("_MyFloat", 2.0f);
material.SetTexture("_MyTex", myTexture);

// 通过 PropertyToID（更高效，避免字符串查找）
int myColorId = Shader.PropertyToID("_MyColor");
material.SetColor(myColorId, Color.red);
```

---

### 第二层：ShaderLab 渲染状态命令

这层控制 GPU 的**硬件渲染状态**，不是 GPU 计算代码。

#### 常见命令

| 命令 | 作用 | 写死示例 | 属性控制示例 |
|---|---|---|---|
| `Blend` | 混合模式 | `Blend SrcAlpha OneMinusSrcAlpha` | `Blend [_SrcBlend] [_DstBlend]` |
| `ZWrite` | 是否写深度 | `ZWrite Off` | `ZWrite [_ZWrite]` |
| `ZTest` | 深度测试 | `ZTest LEqual` | `ZTest [_ZTest]` |
| `Cull` | 面剔除 | `Cull Back` | `Cull [_CullMode]` |
| `ColorMask` | 颜色通道写入 | `ColorMask RGB` | `ColorMask [_ColorMask]` |
| `Stencil` | 模板测试 | `Ref 1 Comp Equal` | `Ref [_StencilRef]` |

#### `[变量名]` 语法的规则

```
Blend [_FinalSrcBlend] [_FinalDstBlend]
      ↑
      这个 [] 读的是：
        ① Material 属性（如果 Properties 里有同名的）
        ② 全局 float（C# 用 SetGlobalFloat 设置的）
      
      不需要在 HLSL 里声明
      因为它在着色器运行之前就配置好 GPU 硬件了
```

**C# 传值方式（两种）：**

```csharp
// 方式一：全局属性（所有 Shader 都能读到）
buffer.SetGlobalFloat("_FinalSrcBlend", (float)BlendMode.One);
// 或用 CommandBuffer：
Shader.SetGlobalFloat(finalSrcBlendId, (float)BlendMode.One);

// 方式二：Material 属性（只对这个材质生效）
material.SetFloat("_ZWrite", 1.0f);
material.SetInt("_CullMode", (int)CullMode.Back);
```

#### 使用场景

```
场景一：运行时动态改混合模式（本章用法）
  不同相机需要不同混合模式
  → C# SetGlobalFloat → Shader Blend [_X] [_Y]

场景二：透明材质的 ZWrite 控制
  透明物体不写深度
  → Material.SetFloat("_ZWrite", 0) → ZWrite [_ZWrite]

场景三：UI Stencil 遮罩
  不同 UI 元素需要不同模板值
  → Material.SetFloat("_StencilRef", maskValue) → Ref [_StencilRef]

场景四：双面渲染开关
  皮肤、薄叶片等需要双面
  → Material.SetFloat("_CullMode", 0) → Cull [_CullMode]
```

---

### 第三层：HLSLPROGRAM 里的变量

GPU 着色器代码，这里用到的所有变量都必须显式声明。

#### 基本类型声明

```hlsl
// 标量 / 向量
float  _MyFloat;
float4 _MyColor;      // (r, g, b, a)
float4x4 _MyMatrix;

// 贴图（Unity SRP 方式）
TEXTURE2D(_MyTex);
SAMPLER(sampler_MyTex);
// 使用：
float4 color = SAMPLE_TEXTURE2D(_MyTex, sampler_MyTex, uv);

// 数组（用于批量传灯光数据等）
float4 _LightColors[4];
```

#### 变量的作用域

```
全局变量（Shader 全局属性）：
  C# SetGlobalXXX 传来的
  所有 Shader、所有 Pass 都能看到
  每帧可以多次设置（覆盖前一次）

CBUFFER（常量缓冲区）：
  CBUFFER_START(UnityPerMaterial)
      float4 _MyColor;    ← 每帧只传一次，GPU 缓存优化
  CBUFFER_END
  用于和 SRP Batcher 兼容（提升 Draw Call 合批效率）

PerDraw（每次 Draw Call）：
  unity_ObjectToWorld 等
  每个物体不同，由 Unity 自动填充
```

#### C# 传值到 HLSL 的方式

```csharp
// 全局（所有 Shader 都能读）
buffer.SetGlobalVector("_MyColor", new Vector4(1,0,0,1));
buffer.SetGlobalFloat("_MyFloat", 1.5f);
buffer.SetGlobalMatrix("_MyMatrix", matrix);
buffer.SetGlobalTexture("_MyTex", renderTexture);

// 材质（只有用这个材质的 Shader 能读）
material.SetVector("_MyColor", new Vector4(1,0,0,1));
material.SetFloat("_MyFloat", 1.5f);
material.SetTexture("_MyTex", texture);

// 属性ID（缓存 string hash，性能更好）
static int myColorId = Shader.PropertyToID("_MyColor");
buffer.SetGlobalVector(myColorId, new Vector4(1,0,0,1));
```

---

## C# → Shader 完整传值路径总结

```
C# 端                    Shader 端

SetGlobalFloat()    →    ShaderLab 命令 [_xxx]  （渲染状态）
SetGlobalFloat()    →    HLSL float _xxx         （计算用）
SetGlobalVector()   →    HLSL float4 _xxx
SetGlobalMatrix()   →    HLSL float4x4 _xxx
SetGlobalTexture()  →    HLSL TEXTURE2D(_xxx)
SetGlobalColor()    →    HLSL float4 _xxx        （自动 gamma 处理）

Material.SetXXX()   →    Properties 块同名属性
                    →    HLSL 同名变量（也需声明）

注意：
  SetGlobalFloat 的值可以被 ShaderLab 命令和 HLSL 都读到
  Material.SetXXX 只对这个 Material 实例生效
  PropertyToID 做的是 string→int 的 hash，避免每帧字符串比较
```

---

## 常见问题与陷阱

### 陷阱一：HLSL 里忘记声明变量

```hlsl
// Properties 里有 _MyColor
// 但 HLSL 里忘了声明：

float4 frag() {
    return _MyColor;   // ❌ 编译报错：未声明
}

// 解决：
float4 _MyColor;       // 必须声明
float4 frag() {
    return _MyColor;   // ✅
}
```

### 陷阱二：Properties 有但 SetGlobalFloat 覆盖了

```
表现：Material 改了 _MyFloat，但 Shader 里值不对
原因：某处 SetGlobalFloat("_MyFloat", ...) 覆盖了 Material 的值

全局属性 > 材质属性？
  不对，是后设置的覆盖先设置的
  每帧 SetGlobalFloat 会持续覆盖 Material 的值
  
解决：用 Material.SetFloat 代替 SetGlobalFloat，或确认不冲突
```

### 陷阱三：Color 的颜色空间

```csharp
// Inspector 里的 Color 是 gamma 空间（人眼感知）
// GPU 计算需要线性空间

// 错误：
buffer.SetGlobalColor(colorId, myColor);           // gamma 空间传进去

// 正确：
buffer.SetGlobalColor(colorId, myColor.linear);    // 转线性空间再传
```

### 陷阱四：ShaderLab 命令的 [] 只能读 float

```
Blend [_SrcBlend] [_DstBlend]   ← 只能是 float

// C# 传枚举时要转换：
material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);  // ✅
material.SetEnum("_SrcBlend", BlendMode.SrcAlpha);          // ✅（等价）
material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);      // ❌ 可能报错
```

---

## 本章实际应用（第14章 Custom Blending）

```
目标：让每台相机能独立配置 Final Pass 的混合模式

数据流：
  Inspector
    CameraSettings.FinalBlendMode { source, destination }
       ↓ CameraRenderer.Render() 读取
  CameraSettings.finalBlendMode
       ↓ postFXStack.Setup() 传入并存储
  PostFXStack.finalBlendMode
       ↓ DrawFinal() 里
  buffer.SetGlobalFloat(finalSrcBlendId, (float)finalBlendMode.source)
  buffer.SetGlobalFloat(finalDstBlendId, (float)finalBlendMode.destination)
       ↓ GPU 执行 Final Pass 前
  Shader: Blend [_FinalSrcBlend] [_FinalDstBlend]
       ↓ GPU 混合硬件
  正确叠层输出到屏幕

为什么不在 HLSL 里声明 _FinalSrcBlend：
  因为 Blend 命令在 HLSL 着色器运行之前配置 GPU 混合硬件
  HLSL 代码根本不需要知道混合模式是多少
  只有 ShaderLab 层（渲染状态）需要这个数值
```

---

## 速查：需不需要在 HLSL 里声明？

| 用途 | 需要 HLSL 声明？ | 例子 |
|---|---|---|
| ShaderLab Blend 命令 | ❌ 不需要 | `Blend [_SrcBlend] [_DstBlend]` |
| ShaderLab ZWrite 命令 | ❌ 不需要 | `ZWrite [_ZWrite]` |
| HLSL 计算中用到 | ✅ 必须声明 | `float4 _MyColor;` |
| 采样贴图 | ✅ 必须声明 | `TEXTURE2D(_MyTex);` |
| Properties 里有 | ✅ HLSL 也要声明才能用 | 两边都写 `_MyColor` |
