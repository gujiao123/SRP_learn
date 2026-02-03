// 1. Include Guard 开始
// 这里的宏命名规范通常是：项目名_文件名_INCLUDED,
//me 防止重复定义宏文件
#ifndef CUSTOM_LIT_PASS_INCLUDED
#define CUSTOM_LIT_PASS_INCLUDED


// 2. 引用 Common (里面包含了 SpaceTransforms)
#include "../ShaderLibrary/Common.hlsl"
#include "../ShaderLibrary/Surface.hlsl"
#include "../ShaderLibrary/Light.hlsl"
#include "../ShaderLibrary/BRDF.hlsl"
#include "../ShaderLibrary/Lighting.hlsl"

// --- 修改变量定义方式 ---
// CBUFFER_START(缓冲区名字)
// UnityPerMaterial 这个名字是约定俗成的，SRP Batcher 会认这个名字
//注意：这个宏来自 Core Library，它会自动处理不同平台的常量缓冲区语法。
/*CBUFFER_START(UnityPerMaterial)
    float4 _BaseColor;
CBUFFER_END*/
// -----------------------

// 纹理和采样器必须定义在全局，不能进 CBUFFER
TEXTURE2D(_BaseMap);
SAMPLER(sampler_BaseMap);

// -------------------
// --- 修改属性定义 ---
// 不再是 CBUFFER_START(UnityPerMaterial)
//me 使用GPU实例化
//普通的 CBUFFER_START 定义的是单个变量。
//Instancing 需要用 UNITY_INSTANCING_BUFFER_START 定义数组。
UNITY_INSTANCING_BUFFER_START(UnityPerMaterial)
    // float4 _BaseColor;  <-- 旧写法
    // 新写法：定义一个名为 _BaseColor 的实例化属性
    UNITY_DEFINE_INSTANCED_PROP(float4, _BaseMap_ST) // 纹理变换
    UNITY_DEFINE_INSTANCED_PROP(float4, _BaseColor)
// --- 记得补上这个 ---
   UNITY_DEFINE_INSTANCED_PROP(float, _Cutoff)
   // -----------------
// 新增
   UNITY_DEFINE_INSTANCED_PROP(float, _Metallic)
   UNITY_DEFINE_INSTANCED_PROP(float, _Smoothness)
UNITY_INSTANCING_BUFFER_END(UnityPerMaterial)


struct Attributes {
    float3 positionOS : POSITION;
    // --- 新增 ---
    float3 normalOS : NORMAL; // 获取模型空间的法线
    // -----------
    // --- 新增宏 ---
    // 自动声明 instanceID 变量（如果开启了 Instancing）
    UNITY_VERTEX_INPUT_INSTANCE_ID
    // -------------
    float2 baseUV : TEXCOORD0; // 新增 UV 输入
    
};
struct Varyings {
    float4 positionCS : SV_POSITION;
    float3 positionWS : VAR_POSITION; // 后面算光照方向需要这个
    // --- 新增 ---
    float3 normalWS : VAR_NORMAL; // 传递世界空间的法线
    // -----------
    // --- 新增宏 ---
    // 自动声明 instanceID 变量用于传递
    UNITY_VERTEX_INPUT_INSTANCE_ID
    // -------------
    float2 baseUV : VAR_BASE_UV; // 新增 UV 传递
    
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
    // 计算 UV (应用 Tiling/Offset)
    float4 baseST = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _BaseMap_ST);
    output.baseUV = input.baseUV * baseST.xy + baseST.zw;
    return output;

}

// 3. 片元着色器 (Fragment Shader)
// 职责：计算像素颜色。
// : SV_TARGET 是语义，告诉 GPU 把这个颜色输出到帧缓存
float4 LitPassFragment (Varyings input) : SV_TARGET {
    // 1. 提取 ID：从 input 中拿到刚才传过来的 ID
    UNITY_SETUP_INSTANCE_ID(input);
    
    // 1. 采样纹理 目前只需要这个透明通道 
    float4 baseMap = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.baseUV);
    // 2. 获取颜色属性
    float4 baseColor = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _BaseColor);
    // 3. 混合结果
    float4 base = baseMap * baseColor;
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
    surface.metallic = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _Metallic);
    surface.smoothness = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _Smoothness);
    // A. 法线：归一化插值后的法线
    surface.normal = normal;
    
    
    // Clipping 逻辑 (现在可以用纹理的 Alpha 来剔除了！)
    #if defined(_CLIPPING)
    clip(surface.alpha - UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _Cutoff));
    #endif

    // --- 根据宏决定是否预乘 ---
    #if defined(_PREMULTIPLY_ALPHA)
    BRDF brdf = GetBRDF(surface, true);
    #else
    BRDF brdf = GetBRDF(surface, false);
    #endif
    
    // 3. 计算光照
    float3 color = GetLighting(surface, brdf);
    
    // 2. 查表取色：不再直接用 _BaseColor
    // 而是去 UnityPerMaterial 数组里，根据当前 ID 取出对应的 _BaseColor
    //return UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _BaseColor);
    return float4(color, surface.alpha);


}
// 4. Include Guard 结束
#endif
