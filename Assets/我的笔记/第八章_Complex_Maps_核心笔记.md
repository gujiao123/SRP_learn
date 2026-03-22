# 第八章：Complex Maps 核心笔记

> 核心思想：用贴图的每个像素控制材质属性，代替全局的手动滑条

---

## 🎯 为什么需要这一章？

```
第七章以前：
  金属度 = 0.8   ← 整个球体都是 0.8，没办法区分
  光滑度 = 0.5   ← 所有地方一样光滑

现实中一个电路板：
  金色导线 → 高金属度、高光滑度
  绿色基板 → 低金属度、低光滑度
  凹槽边缘 → 有 AO 遮蔽，带划痕
  近看时 → 有微细节质感

结论：一个数字根本表达不了复杂表面！→ 需要贴图来逐像素控制
```

---

## 第一部分：Mask Map（MODS 遮罩图）

### 📌 为什么需要 Mask Map？

```
一个材质的不同区域有不同属性（金属/非金属共存）
  → 仅靠滑条只能全局调节
  → 用一张贴图的像素分别存 4 种属性
  → "哪里金属哪里不金属" 用贴图的颜色来描述
```

### 📦 MODS 格式（Unity HDRP 标准）

| 通道 | 存的是什么 | 例子 |
|---|---|---|
| **R（红）** | Metallic（金属度分布）| 白=纯金属，黑=非金属 |
| **G（绿）** | Occlusion（遮蔽分布）| 白=正常，黑=被遮蔽 |
| **B（蓝）** | Detail Mask（细节影响区域）| 白=细节出现，黑=细节不出现 |
| **A（透明）** | Smoothness（光滑度分布）| 白=光滑，黑=粗糙 |

```
一张图4通道 = 4种贴图合一 = 节省采样次数和内存
```

### ⚠️ 重要：导入设置

```
Mask Map 存的是数据，不是颜色！
必须在 Unity Inspector 里：关闭 sRGB (Color Texture)

原因：
  sRGB 开启 → GPU 采样时做 gamma→linear 转换
  对颜色贴图正确，但对数据贴图是破坏！
  0.8 的金属度被 gamma 转换 → 变成 0.6 → 数据错误！
```

### 🔢 代码公式

```
最终金属度 = 材质滑条 × 贴图R通道
最终光滑度 = 材质滑条 × 贴图A通道

Occlusion（遮蔽）：
  GetMask(uv).g  = 遮蔽贴图值（0=完全遮蔽，1=无遮蔽）
  strength = _Occlusion滑条
  occlusion = lerp(遮蔽值, 1.0, strength)

  strength=1 → lerp(g, 1, 1) = 1 → 忽略遮蔽（全亮）
  strength=0 → lerp(g, 1, 0) = g → 完整遮蔽效果
  （注意：strength越大遮蔽越弱，反直觉但有历史原因）
```

---

## 第二部分：Occlusion（环境光遮蔽）

### 📌 为什么需要 Occlusion？

```
现实中螺丝孔/缝隙里面会比外面暗：
  凹槽被自身几何体挡住了四面八方的环境光
  → 环境光少了 → 看起来暗一圈

  但贴图是平面的，无法自动计算这种几何遮蔽
  → 美术预先把"哪里应该暗"画进贴图的G通道

直接光（手电）不受影响：
  你拿手电直射一个螺丝孔 → 手电能照进去 → 正常亮
  所以 Occlusion 只影响间接光（GI/环境反射），不影响直接光！
```

### 💻 代码位置

```hlsl
// BRDF.hlsl - IndirectBRDF 函数末尾：
return (diffuse * brdf.diffuse + reflection) * surface.occlusion;
//                                              ↑ 只乘到间接光结果上！
```

---

## 第三部分：Detail Map（细节贴图）

### 📌 为什么需要 Detail Map？

```
问题：游戏里模型拉近看，Base贴图分辨率不够 → 像素化糊掉
解决：叠加一张高 Tiling 的细节贴图

Base Map：Tiling=1，覆盖整个物体（表达大面积颜色和图案）
Detail Map：Tiling=8（或更高），细节密集重复

两者叠加：
  远看 → 只看到 Base Map 的整体图案 ✅
  近看 → Base Map 糊了但 Detail Map 仍然清晰 ✅
  = 任何距离都有细节感，像针织毛衣
```

### ⚠️ 导入设置

```
同样需要关 sRGB（数据贴图）
Filter Mode = Trilinear（必须！否则 Fadeout 无效）
勾选 Fadeout to Gray（Advanced 里面）
  → 远距离 mip 级别贴图自动渐变为中性灰（0.5）
  → 远处细节自动消失，不产生噪点
```

### 🔢 Detail Map 的数学原理

```
贴图存储值：0~1（普通范围）
实际意义：0.5 = 中性（不改变），>0.5 = 变亮/光滑，<0.5 = 变暗/粗糙

转换：map * 2.0 - 1.0  → 把 0~1 转为 -1~1（0.5变0）

叠加到 Albedo（颜色）：
  float detail = GetDetail(uv).r * detailAlbedo;  // R通道影响颜色
  float mask = GetMask(uv).b;                      // B通道控制哪里受影响
  map.rgb = lerp(sqrt(map.rgb), detail < 0 ? 0 : 1, abs(detail) * mask);
  map.rgb *= map.rgb;
  
  关键：用 sqrt 在 Gamma 空间插值
  原因：直接在 linear 空间做「变亮比变暗视觉上更强」，不均衡
        用 sqrt 近似在 gamma 空间操作 → 视觉上变亮变暗一样强

叠加到 Smoothness（光滑度）：
  同理，用 B 通道 + lerp
```

### 🎨 贴图从哪来？

```
Detail Map、Normal Map 等的来源：
  ① 从高模烘焙（主法线贴图的来源）
  ② Substance Painter/Designer 程序生成（最常用！）
     - 里面有现成的"金属拉丝"、"皮革纹"、"混凝土颗粒"材质库
     - 直接刷上去生成贴图
  ③ 真实表面扫描（Photogrammetry）
  ④ 手动绘制（2D 美术在贴图上画）

教程里的电路板贴图：教程作者专门制作的示范素材
```

---

## 第四部分：Normal Map（法线贴图）

### 📌 为什么需要 Normal Map？

```
游戏模型为了性能 = 低多边形（低模）
但视觉上需要和高多边形模型一样的细节

法线贴图：把高模的顶点法线方向"拍照"存进贴图
           低模渲染时，从贴图里读取"假"法线
           → GPU 用假法线计算光照
           → 看起来有高模的凹凸感！
           → 实际三角面数没变！这是纯光照欺骗！

外观区别：
  法线贴图不影响轮廓（侧面依然是低模的锯齿）
  只影响正面看的光照效果
```

### ⚠️ 导入设置

```
Texture Type = Normal map（必须！）
  → Unity 自动处理法线格式（DXT5nm 压缩等）
  → 平坦区域呈蓝紫色（z轴朝上存在B通道）
```

### 🔢 切线空间（Tangent Space）原理

```
法线贴图存储的不是世界空间法线，而是切线空间法线！

什么是切线空间：
  每个顶点有一个局部坐标系：
    Z轴 = 顶点法线方向（"朝上"）
    X轴 = 切线方向（"朝右"，mesh 里每个顶点存储）
    Y轴 = 由 X×Z 推导出来（"朝前"）

  这个坐标系随表面弯曲而弯曲 → 贴图可以正确跟随曲面

为什么不直接存世界空间法线：
  同一张法线贴图可以用在任何朝向的物体上
  如果存世界空间 → 球的左边和右边法线方向完全不同 → 贴图不能重用

代码：
  tangentOS → 顶点数据里的切线方向（float4，w 是方向符号）
  tangentWS = TransformObjectToWorldDir(tangentOS.xyz) + 保留 w
  
  // 构造切线空间→世界空间的转换矩阵
  float3x3 tbn = CreateTangentToWorld(normalWS, tangentWS.xyz, tangentWS.w);
  // 把切线空间法线转到世界空间
  float3 finalNormal = TransformTangentToWorld(normalTS, tbn);
```

### 🔍 为什么有两个法线字段？

```
surface.normal           = 法线贴图的"假"法线
  → 用于光照计算（制造凹凸假象）

surface.interpolatedNormal = 顶点插值的真实几何法线
  → 用于 Shadow Bias（阴影偏移）

Shadow Bias 问题：
  采样阴影贴图前，把采样点沿法线方向往外推一点（避免自阴影 Acne）

  如果用法线贴图的法线做 Bias：
    法线贴图让法线大幅扭曲 → 偏移方向错误 → 阴影位置错误
    → 表面出现黑色条纹（Acne）或物体飘离阴影（Peter Pan）

  所以 Shadow Bias 必须用真实几何法线 interpolatedNormal！
```

---

## 第五部分：Detail Normal Map（细节法线贴图）

### 📌 为什么需要 Detail Normal Map？

```
普通法线贴图解决了大结构的凹凸（导线隆起、焊点凸起）
但近看时：
  导线表面应该有金属拉丝的微细划痕
  基板有细小颗粒质感
  → 主法线贴图分辨率不够，放大后会糊！

Detail Normal Map = Tiling 更高的补充法线
  → 远看主法线负责大结构
  → 近看细节法线补充微表面质感
  → 两层叠加 = 所有距离都逼真

共享 detailUV（和 Detail Map 一起 Tiling）：
  细节颜色和细节法线必须对齐
  → Detail Normal Map 用 [NoScaleOffset]，跟着 Detail Map 的 Tiling
```

### 🔢 BlendNormalRNM 是什么？

```
目的：把两个切线空间法线正确叠加在一起

为什么不能直接相加：
  两个法线向量相加后需要重新归一化
  但归一化后的结果物理上不准确（角度会偏）

BlendNormalRNM = Reoriented Normal Mapping（重定向法线混合）
  → 把细节法线以主法线为轴进行旋转叠加
  → 得到的结果物理正确

  就像把一张纸（细节法线）贴到一个弯曲表面（主法线）上
  纸会跟着弯曲 → 最终效果正确

这个函数在 Unity Core RP Library 的 Packing.hlsl 里，直接调用即可
  normal = BlendNormalRNM(主法线, 细节法线);
```

---

## 改动文件汇总

| 文件 | 改动 |
|---|---|
| `Lit.shader` | 加 MaskMap/DetailMap/NormalMap 相关属性和 shader_feature |
| `LitInput.hlsl` | 加 `INPUT_PROP` 宏，加所有 GetMask/GetDetail/GetNormalTS 函数 |
| `Common.hlsl` | 加 Packing.hlsl，加 `DecodeNormal`/`NormalTangentToWorld` 函数 |
| `Surface.hlsl` | 加 `occlusion`/`interpolatedNormal` 字段 |
| `LitPass.hlsl` | 加 tangentOS/tangentWS，法线条件编译，设 occlusion/interpolatedNormal |
| `BRDF.hlsl` | IndirectBRDF 结果乘以 `surface.occlusion` |
| `Shadows.hlsl` | Shadow Bias 用 `interpolatedNormal` 替代 `normal` |

---

## PBR 材质贴图工作流（行业现实）

```
一个完整 PBR 材质 = 6~8 张贴图：

1. Albedo/Base Color  颜色基础
2. Normal Map         大结构凹凸（从高模烘焙）
3. Metallic           哪里是金属（MODS.R）
4. Roughness          哪里光滑/粗糙（MODS.A）
5. AO                 环境光遮蔽（MODS.G）
6. Emission           自发光
7. Detail Map         近看细节补充
8. Detail Normal      近看微表面质感

工具链：
  Maya/Blender → 高模建模 → 烘焙法线到低模
  Substance Painter → 刷颜色/粗糙度/AO/细节
  Substance Designer → 程序化生成Materials
  最终导出 → 放入 Unity 材质设置
```

---

> 🎉 第八章完成！下一章：Point and Spot Lights（点光源和聚光灯）
