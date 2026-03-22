#ifndef CUSTOM_COMMON_INCLUDED
#define CUSTOM_COMMON_INCLUDED
// 1. 引入 Core 库的基础定义 (包含 real4 等类型别名)
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

// --- 新增引用 --- 用于简便材质计算
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"


// 2. 引入我们的变量定义
#include "UnityInput.hlsl"




// 3. 定义宏映射 (关键步骤！)
// 告诉 Core 库：当你找 UNITY_MATRIX_M 时，请使用我的 unity_ObjectToWorld 变量
#define UNITY_MATRIX_M unity_ObjectToWorld
#define UNITY_MATRIX_I_M unity_WorldToObject
#define UNITY_MATRIX_V unity_MatrixV
#define UNITY_MATRIX_I_V unity_MatrixInvV  // Unity 2022 必需
#define UNITY_MATRIX_VP unity_MatrixVP
#define UNITY_PREV_MATRIX_M unity_prev_MatrixM
#define UNITY_PREV_MATRIX_I_M unity_prev_MatrixIM
#define UNITY_MATRIX_P glstate_matrix_projection

//me06什么意思 告诉 Core 库：我们要用 Shadow Mask 你定义

// Unity 内部 UnityInstancing.hlsl 里大概是这样：
//#if defined(SHADOWS_SHADOWMASK)
//    UNITY_INSTANCED_PROP(float4, unity_ProbesOcclusion)  // 按实例分发
//#endif


//me06 告诉 UnityInstancing：要透传遮挡数据
#if defined(_SHADOW_MASK_ALWAYS) || defined(_SHADOW_MASK_DISTANCE)
    #define SHADOWS_SHADOWMASK 
#endif

// 1. 引入通用库 (注意路径引用，.. 表示上一级目录)
// --- 顺序非常重要 ---
// 1. 引用 Core 的 Instancing 库
//引入了这个库 你的材质选项就会有 enable GPU Instancing 选项 一旦你勾选了 就会改变你的UNITY_MATRIX_M b不勾选就没有发生

//所以先定义好一般情况,就是上面#define UNITY_MATRIX_M unity_ObjectToWorld 然后再根据是否勾选进行修改启动gpu渲染,它把 UNITY_MATRIX_M 劫持，改成查数组（unity_ObjectToWorldArray[ID]）。
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"


// 4. 引入 Core 库的空间变换函数
// 这个文件里包含了 TransformObjectToWorld, TransformWorldToHClip 等标准实现
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl"

//计算两点间的距离公式
float DistanceSquared(float3 pA, float3 pB) {
    return dot(pA - pB, pA - pB);
}




// 思路：用抖动图案来"挖掉"一部分像素，制造半透明过渡效果
// fade > 0 → 正在淡出（从1减到0）
// fade < 0 → 正在淡入（从-1增到0）
void ClipLOD(float2 positionCS, float fade) {
    #if defined(LOD_FADE_CROSSFADE)
        float dither = InterleavedGradientNoise(positionCS.xy, 0);
        clip(fade + (fade < 0.0 ? dither : -dither));
    #endif
}





//已经在官方实现
// 封装函数：从对象空间 -> 世界空间
/*float3 TransformObjectToWorld (float3 positionOS) {
    // mul 是矩阵乘法函数
    // 补全 float4(..., 1.0) 是为了进行位移运算（齐次坐标）
    // .xyz 是 Swizzle 操作，只取前三个分量
    return mul(unity_ObjectToWorld, float4(positionOS, 1.0)).xyz;
}*/

// 封装函数：从世界空间 -> 裁剪空间 (HClip)
/*float4 TransformWorldToHClip (float3 positionWS) {
    return mul(unity_MatrixVP, float4(positionWS, 1.0));
}*/
#endif