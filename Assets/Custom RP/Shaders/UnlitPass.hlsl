// 1. Include Guard 开始
// 这里的宏命名规范通常是：项目名_文件名_INCLUDED,
//me 防止重复定义宏文件
#ifndef CUSTOM_UNLIT_PASS_INCLUDED
#define CUSTOM_UNLIT_PASS_INCLUDED


// 2. 引用 Common (里面包含了 SpaceTransforms)
#include "../ShaderLibrary/Common.hlsl"


// --- 修改变量定义方式 ---
// CBUFFER_START(缓冲区名字)
// UnityPerMaterial 这个名字是约定俗成的，SRP Batcher 会认这个名字
//注意：这个宏来自 Core Library，它会自动处理不同平台的常量缓冲区语法。
/*CBUFFER_START(UnityPerMaterial)
    float4 _BaseColor;
CBUFFER_END*/
// -----------------------


// -------------------
// --- 修改属性定义 ---
// 不再是 CBUFFER_START(UnityPerMaterial)
//me 使用GPU实例化
//普通的 CBUFFER_START 定义的是单个变量。
//Instancing 需要用 UNITY_INSTANCING_BUFFER_START 定义数组。
UNITY_INSTANCING_BUFFER_START(UnityPerMaterial)
    // float4 _BaseColor;  <-- 旧写法
    // 新写法：定义一个名为 _BaseColor 的实例化属性
    UNITY_DEFINE_INSTANCED_PROP(float4, _BaseColor)
UNITY_INSTANCING_BUFFER_END(UnityPerMaterial)
// -------------------

struct Attributes {
    float3 positionOS : POSITION;
    // --- 新增宏 ---
    // 自动声明 instanceID 变量（如果开启了 Instancing）
    UNITY_VERTEX_INPUT_INSTANCE_ID
    // -------------
};
struct Varyings {
    float4 positionCS : SV_POSITION;
    // --- 新增宏 ---
    // 自动声明 instanceID 变量用于传递
    UNITY_VERTEX_INPUT_INSTANCE_ID
    // -------------
};

// 2. 顶点着色器 (Vertex Shader)
// 职责：处理顶点位置。目前我们暂时返回 0，这会导致模型不可见（塌缩成一个点）。
// float4 是返回值类型
// : SV_POSITION 是语义，告诉 GPU 这个返回值是裁剪空间坐标
//好像只是为了返回值而已 如果使用结构体的话秩序为变量就行


//总结：
//简单返回：必须在函数屁股后面挂语义。
//结构体返回：必须在结构体成员屁股后面挂语义。
Varyings UnlitPassVertex (Attributes input) {
    Varyings output;
    
    // 1. 提取 ID：从 input 中把 ID 拿出来存到全局变量
    UNITY_SETUP_INSTANCE_ID(input);
    // 2. 传递 ID：把 ID 塞给 output，传给片元着色器
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    
    // 步骤 A: 对象空间 -> 世界空间
    float3 positionWS = TransformObjectToWorld(input.positionOS);
    
    // 步骤 B: 世界空间 -> 裁剪空间
    output.positionCS = TransformWorldToHClip(positionWS);
    return output;

}

// 3. 片元着色器 (Fragment Shader)
// 职责：计算像素颜色。
// : SV_TARGET 是语义，告诉 GPU 把这个颜色输出到帧缓存
float4 UnlitPassFragment (Varyings input) : SV_TARGET {
    // 1. 提取 ID：从 input 中拿到刚才传过来的 ID
    UNITY_SETUP_INSTANCE_ID(input);
    
    // 2. 查表取色：不再直接用 _BaseColor
    // 而是去 UnityPerMaterial 数组里，根据当前 ID 取出对应的 _BaseColor
    return UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _BaseColor);
}
// 4. Include Guard 结束
#endif

#ifndef CUSTOM_UNLIT_PASS_INCLUDED
#define CUSTOM_UNLIT_PASS_INCLUDED





#endif