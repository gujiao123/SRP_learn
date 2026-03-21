#ifndef CUSTOM_META_PASS_INCLUDED
#define CUSTOM_META_PASS_INCLUDED

// Meta Pass 只需要 PBR 相关的数据
// Common 和 LitInput 已由 Lit.shader 的 HLSLINCLUDE 注入
#include "../ShaderLibrary/Surface.hlsl"
#include "../ShaderLibrary/Shadows.hlsl"
#include "../ShaderLibrary/Light.hlsl"
#include "../ShaderLibrary/BRDF.hlsl"

// ============================================================
// 🌐 这两个变量由引擎传入，用来告知 Meta Pass 需要输出什么
// ============================================================

// 🎛️ 控制标志：引擎用它来告诉我们，这次烘焙要什么数据
// x = 1 时：要漫反射反射率 (Diffuse Reflectivity) → 用于光照弹射颜色计算
// y = 1 时：要自发光 (Emission) → 用于把自发光也烘焙进光照贴图里
bool4 unity_MetaFragmentControl;

// 烘焙器对最终输出值的两个亮度校正参数
float unity_OneOverOutputBoost; // 亮度次方值（校正曲线）
float unity_MaxOutputValue;     // 亮度上限（防止溢出过曝）

// ============================================================
// 顶点 / 片元结构体
// ============================================================
struct Attributes {
    float3 positionOS : POSITION;
    float2 baseUV     : TEXCOORD0;
    // 💡 关键：Meta Pass 需要模型的光照贴图 UV（用于展开到2D烘焙空间）
    float2 lightMapUV : TEXCOORD1;
};

struct Varyings {
    float4 positionCS : SV_POSITION;
    float2 baseUV     : VAR_BASE_UV;
};

// ============================================================
// 顶点着色器
// ============================================================
Varyings MetaPassVertex (Attributes input) {
    Varyings output;
    
    // 🔮 这是 Meta Pass 最诡异也最精髓的一行！
    // 一般的渲染：用 positionOS（模型空间位置）变换到世界空间再到裁剪空间
    // 但 Meta Pass 是反过来的：
    //   → 把 lightMapUV（光照贴图的二维坐标）直接当成模型"位置"的 XY 坐标！
    //   → 这样 GPU 就把模型的每个三角形"展平"投射到光照贴图纹素空间里
    //   → 烘焙器再对每个纹素调用一次片元着色器，询问这块区域的颜色
    input.positionOS.xy = input.lightMapUV * unity_LightmapST.xy + unity_LightmapST.zw;
    
    // 兼容 OpenGL 的 dummy 操作（防止 Z 坐标被优化掉导致渲染错误）
    input.positionOS.z = input.positionOS.z > 0.0 ? FLT_MIN : 0.0;
    
    // 把这个"展平"后的位置继续走常规变换流程
    output.positionCS = TransformWorldToHClip(input.positionOS);
    
    // 普通材质纹理的 UV 正常传递，用于采样 _BaseColor
    output.baseUV = TransformBaseUV(input.baseUV);
    
    return output;
}

// ============================================================
// 片元着色器
// ============================================================
float4 MetaPassFragment (Varyings input) : SV_TARGET {
    
    // 从材质银行取出基础颜色数据
    float4 base = GetBase(input.baseUV);
    
    // 用零初始化 Surface（只需要颜色/金属/光滑度，不需要位置/法线等）
    Surface surface;
    ZERO_INITIALIZE(Surface, surface);
    surface.color     = base.rgb;
    surface.metallic  = GetMetallic(input.baseUV);
    surface.smoothness = GetSmoothness(input.baseUV);
    
    // 通过 BRDF 计算出漫反射和高光的比例
    BRDF brdf = GetBRDF(surface);
    
    float4 meta = 0.0;
    
    if (unity_MetaFragmentControl.x) {
        // 烘焙器要漫反射反射率：就是"这个表面能反弹多少、什么颜色的光"
        meta = float4(brdf.diffuse, 1.0);
        
        // 额外奖励：粗糙的高光材质（比如磨砂金属）也会弹射一些环境光
        // 加入这部分贡献，让间接照明更真实
        meta.rgb += brdf.specular * brdf.roughness * 0.5;
        
        // 对亮度进行烘焙器要求的校正处理（防止过曝）
        meta.rgb = min(
            PositivePow(meta.rgb, unity_OneOverOutputBoost),
            unity_MaxOutputValue
        );
    }
    // 烘焙器要自发光时（Y 标志位）：直接把发光颜色输出
    // 这样霓虹灯、岩浆、发光方块等自发光物体的光，也会在烘焙期间照亮周围静态物体！
    else if (unity_MetaFragmentControl.y) {
        meta = float4(GetEmission(input.baseUV), 1.0);
    }
    
    return meta;
}

#endif
