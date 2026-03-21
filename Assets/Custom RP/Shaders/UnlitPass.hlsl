// 1. Include Guard 开始
// 这里的宏命名规范通常是：项目名_文件名_INCLUDED,
//me 防止重复定义宏文件
#ifndef CUSTOM_UNLIT_PASS_INCLUDED
#define CUSTOM_UNLIT_PASS_INCLUDED


// Common 和材质属性已由 Unlit.shader 的 HLSLINCLUDE 自动注入
// #include "../ShaderLibrary/Common.hlsl" ← 已广播
// 材质变量定义已全部在 UnlitInput.hlsl 中
// 这里使用 GetBase(uv) 接口函数即可

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
    
    return UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _BaseColor);
}
// 4. Include Guard 结束
#endif
