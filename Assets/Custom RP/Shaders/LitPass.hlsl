// 1. Include Guard 开始
// 这里的宏命名规范通常是：项目名_文件名_INCLUDED,
//me 防止重复定义宏文件
#ifndef CUSTOM_LIT_PASS_INCLUDED
#define CUSTOM_LIT_PASS_INCLUDED


// Common 和 LitInput 已由 Lit.shader 的 HLSLINCLUDE 自动注入，无需在此重复引用
// #include "../ShaderLibrary/Common.hlsl"  ← 已由 HLSLINCLUDE 广播
// #include "LitInput.hlsl"                 ← 已由 HLSLINCLUDE 广播
#include "../ShaderLibrary/Surface.hlsl"
#include "../ShaderLibrary/Shadows.hlsl"
#include "../ShaderLibrary/Light.hlsl"
#include "../ShaderLibrary/BRDF.hlsl"
#include "../ShaderLibrary/GI.hlsl"
#include "../ShaderLibrary/Lighting.hlsl"

// ✅ 材质变量已全部迁移到 LitInput.hlsl！
// 通过 Lit.shader 的 HLSLINCLUDE 自动注入，这里不再重复定义
// 直接使用 LitInput.hlsl 提供的接口函数即可：
//   GetBase(uv), GetCutoff(uv), GetMetallic(uv), GetSmoothness(uv), TransformBaseUV(uv)


struct Attributes {
    float3 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float4 tangentOS : TANGENT; // me08: 切线向量，w分量指定 B轴方向（用于左右对称网格翻转法线）
    GI_ATTRIBUTE_DATA
    UNITY_VERTEX_INPUT_INSTANCE_ID
    float2 baseUV : TEXCOORD0;
};
struct Varyings {
    float4 positionCS : SV_POSITION;
    float3 positionWS : VAR_POSITION;
    float3 normalWS : VAR_NORMAL;
    #if defined(_NORMAL_MAP)
    float4 tangentWS : VAR_TANGENT; // me08: 切线向量（w 存差山方向）
    #endif
    GI_VARYINGS_DATA
    UNITY_VERTEX_INPUT_INSTANCE_ID
    float2 baseUV : VAR_BASE_UV;
    #if defined(_DETAIL_MAP)
    float2 detailUV : VAR_DETAIL_UV; // me08: Detail Map 的独立 UV
    #endif
};

// 2. 顶点着色器 (Vertex Shader)
// 职责：处理顶点位置。目前我们暂时返回 0，这会导致模型不可见（塌缩成一个点）。
// float4 是返回值类型
// : SV_POSITION 是语义，告诉 GPU 这个返回值是裁剪空间坐标
//好像只是为了返回值而已 如果使用结构体的话秩序为变量就行


//总结：
//简单返回：必须在函数屁股后面挂语义。
//结构体返回：必须在结构体成员屁股后面挂语义。
Varyings LitPassVertex (Attributes input) {
    Varyings output;
    
    // 1. 提取 ID：从 input 中把 ID 拿出来存到全局变量
    UNITY_SETUP_INSTANCE_ID(input);
    // 2. 传递 ID：把 ID 塞给 output，传给片元着色器
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    TRANSFER_GI_DATA(input, output);
    
    // 步骤 A: 对象空间 -> 世界空间
    output.positionWS = TransformObjectToWorld(input.positionOS); // 赋值给 output
    // 步骤 B: 世界空间 -> 裁剪空间
    output.positionCS = TransformWorldToHClip(output.positionWS );
    
    // --- 法线变换 ---
    //使用 Core 库函数，自动处理缩放问题
    //法线变换 (Normal Transformation)
//问题：顶点的位置和法线都是在模型空间 (Object Space) 定义的。
//陷阱：位置可以用 M 矩阵变换到世界空间，但法线不能！
//如果物体有非统一缩放（比如压扁了），直接用 M 矩阵变换法线，法线会歪掉（不再垂直于表面）。
//解法：必须使用逆转置矩阵 (Inverse Transpose Matrix)。
//好消息：Core Library 的 SpaceTransforms.hlsl 提供了一个函数 TransformObjectToWorldNormal，它会自动处理这一切（还记得我们定义的 unity_WorldTransformParams 吗？就是给它用的）。
    
    output.normalWS = TransformObjectToWorldNormal(input.normalOS);
    // ---------------
    #if defined(_NORMAL_MAP)
    // me08: 切线 XYZ 转到世界空间，w 保留原始符号（1=正常，-1=翻转）
    output.tangentWS = float4(
        TransformObjectToWorldDir(input.tangentOS.xyz), input.tangentOS.w
    );
    #endif
    // 计算 UV：调用银行接口，内部已封装 _BaseMap_ST 的计算
    output.baseUV = TransformBaseUV(input.baseUV);
    #if defined(_DETAIL_MAP)
    output.detailUV = TransformDetailUV(input.baseUV); // me08: 细节 UV 独立计算
    #endif
    return output;

}

// 3. 片元着色器 (Fragment Shader)
// 职责：计算像素颜色。
// : SV_TARGET 是语义，告诉 GPU 把这个颜色输出到帧缓存
float4 LitPassFragment (Varyings input) : SV_TARGET {
    //me07：LOD Cross-Fade
    // unity_LODFade.x 由 Unity 自动填入：正数=淡出，负数=淡入
    ClipLOD(input.positionCS.xy, unity_LODFade.x);
    // 1. 提取 ID：从 input 中拿到刚才传过来的 ID
    UNITY_SETUP_INSTANCE_ID(input);
    
    // me08: 如果开了 Detail Map 就传入 detailUV，否则传默认值 0
    #if defined(_DETAIL_MAP)
    float4 base = GetBase(input.baseUV, input.detailUV);
    #else
    float4 base = GetBase(input.baseUV);
    #endif
    // --- 归一化 ---
    // 插值后的法线长度可能不是 1，必须重置
    /*插值与归一化 (Interpolation & Normalization)
流程：顶点着色器算出 3 个顶点的法线 -> 光栅化阶段进行线性插值 -> 片元着色器拿到插值后的法线。
问题：线性插值会破坏向量的长度（不再是 1）。
解法：在片元着色器里，必须对法线再次执行 normalize()。
    */
    float3 normal = normalize(input.normalWS);
    // -------------
    
    // 1. 准备表面数据 (Surface)
    Surface surface;
    // !!! 修正点：使用 base (混合结果) !!! 
    surface.color = base.rgb; 
    surface.alpha = base.a;
    // 新增赋值
    // 必须计算视线方向
    // _WorldSpaceCameraPos 来自 UnityInput.hlsl
    surface.viewDirection = normalize(_WorldSpaceCameraPos - input.positionWS);
    surface.metallic = GetMetallic(input.baseUV);
    surface.occlusion = GetOcclusion(input.baseUV); // me08: 从 Mask G 通道读遮蔽
    #if defined(_DETAIL_MAP)
    surface.smoothness = GetSmoothness(input.baseUV, input.detailUV); // me08: 细节调制光滑度
    #else
    surface.smoothness = GetSmoothness(input.baseUV);
    #endif
    surface.fresnelStrength = GetFresnel(input.baseUV); // me07: 从材质读取 Fresnel 滑条
    // me08: 法线设置（条件编译决定是否用法线贴图）
    #if defined(_NORMAL_MAP)
        // 有法线贴图：切线空间法线 → 世界空间
        #if defined(_DETAIL_MAP)
        surface.normal = NormalTangentToWorld(
            GetNormalTS(input.baseUV, input.detailUV),
            input.normalWS, input.tangentWS
        );
        #else
        surface.normal = NormalTangentToWorld(
            GetNormalTS(input.baseUV),
            input.normalWS, input.tangentWS
        );
        #endif
        surface.interpolatedNormal = input.normalWS; // Shadow Bias 用几何法线
    #else
        // 没有法线贴图：直接用插值几何法线
        surface.normal = normalize(input.normalWS);
        surface.interpolatedNormal = surface.normal;
    #endif

    surface.position = input.positionWS;
    
    //得到相机坐标下的离相机距离就是深度 用于 当离相机远了就不生成阴影
    surface.depth = -TransformWorldToView(input.positionWS).z;
    
    //表面扰动值 我去 这个不是类似毛毛贴图吗
    //目前配合TAA消除级联间阴影 抖动混合阴影专用
    surface.dither = InterleavedGradientNoise(input.positionCS.xy, 0);
    
    // me14: 从全局变量读取渲染层掩码
    surface.renderingLayerMask = asuint(unity_RenderingLayer.x);  // me14: 原样读比特，不做数值转换

    // Clipping 逻辑
    #if defined(_CLIPPING)
    clip(surface.alpha - GetCutoff(input.baseUV));
    #endif

    // --- 根据宏决定是否预乘 ---
    #if defined(_PREMULTIPLY_ALPHA)
    BRDF brdf = GetBRDF(surface, true);
    #else
    BRDF brdf = GetBRDF(surface, false);
    #endif
    
    // 3. 计算光照
    GI gi = GetGI(GI_FRAGMENT_DATA(input), surface, brdf); // me07: 传入 brdf 用于粗糙度→mip
    float3 color = GetLighting(surface, brdf, gi);
    
    //float4 finalColor = test(surface);
    
    //return finalColor;
    // 2. 查表取色：不再直接用 _BaseColor
    // 而是去 UnityPerMaterial 数组里，根据当前 ID 取出对应的 _BaseColor
    //return UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _BaseColor);
    // 叠加自发光：自发光不受实时光照影响，直接加在最终颜色上
    // 如果没有设置 EmissionColor 或贴图，默认是黑色（0,0,0），等于没有影响
    color += GetEmission(input.baseUV);
    return float4(color,  GetFinalAlpha(surface.alpha));
}
// 4. Include Guard 结束
#endif
