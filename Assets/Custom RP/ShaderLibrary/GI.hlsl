#ifndef CUSTOM_GI_INCLUDED
#define CUSTOM_GI_INCLUDED

// 1. 在 GI.hlsl 的 #ifndef 区域的最首部（最上面）引入官方基建：
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/EntityLighting.hlsl"

// 一旦上游激活了这个光照开关贴图宏
#if defined(LIGHTMAP_ON)
    // 1. 在顶点接收数据处：获取名为 TEXCOORD1 的二套光贴图坐标
    #define GI_ATTRIBUTE_DATA float2 lightMapUV : TEXCOORD1;

    // 2. 在上下游传输的高速路上：开辟一个属于贴图 UV 的车道
    #define GI_VARYINGS_DATA float2 lightMapUV : VAR_LIGHT_MAP_UV;

    // 3. 在顶点调度站处：不再是原样拿取，而是用 ST 偏移进行极其精确的全球图谱定位
    #define TRANSFER_GI_DATA(input, output) \
        output.lightMapUV = input.lightMapUV * \
        unity_LightmapST.xy + unity_LightmapST.zw;

    // 4. 在片段终点站处：专门提取车箱里特定数据的开箱钳
    #define GI_FRAGMENT_DATA(input) input.lightMapUV
#else
    // 对于动态没资格吃烘焙的物体，宏全部变成隐形人（空代码），一路安全放行不报错
    #define GI_ATTRIBUTE_DATA
    #define GI_VARYINGS_DATA // <--- 这里是你漏掉的核心
    #define TRANSFER_GI_DATA(input, output)
    #define GI_FRAGMENT_DATA(input) 0.0
#endif

// 我们用一个结构体把所有的全局光照数据打包
struct GI {
    // 漫反射的全局间接光颜色
    float3 diffuse;
};

// 2. 声明引擎通过 C++ 在后台悄悄绑定在内存里的那张巨大图集，以及它的采样器规则：
TEXTURE2D(unity_Lightmap);
SAMPLER(samplerunity_Lightmap);
// 3. 在那个结构体的下面，新增官方拿真货贴图的神仙函数！
float3 SampleLightMap (float2 lightMapUV) {
#if defined(LIGHTMAP_ON)
    // 借用刚才包含进来的官方库的神级方法，丢给它图集、采样器、纠正后的真实 UV，以及一堆复杂的解码参数，它就会提炼成纯净的反弹光！
    return SampleSingleLightmap(
        TEXTURE2D_ARGS(unity_Lightmap, samplerunity_Lightmap), lightMapUV,
        float4(1.0, 1.0, 0.0, 0.0),
        #if defined(UNITY_LIGHTMAP_FULL_HDR)
            false,
        #else
            true,
        #endif
        float4(LIGHTMAP_HDR_MULTIPLIER, LIGHTMAP_HDR_EXPONENT, 0.0, 0.0)
    );
#else
    return 0.0;
#endif
}

// 这次需要物体的表面法线作为输入（才知道小球的哪一“面”受了什么方向的探针光）
float3 SampleLightProbe (Surface surfaceWS) {
    // 那些背着金山(光照大贴图)的静态大佬，不需要探针这个穷人救济餐！
    #if defined(LIGHTMAP_ON)
        return 0.0;
    #else
        // 把上一步拿到的天书数据，装填成官方 API 要求的格式数组（系数）
        float4 coefficients[7];
        coefficients[0] = unity_SHAr;
        coefficients[1] = unity_SHAg;
        coefficients[2] = unity_SHAb;
        coefficients[3] = unity_SHBr;
        coefficients[4] = unity_SHBg;
        coefficients[5] = unity_SHBb;
        coefficients[6] = unity_SHC;
        
        // 扔进官方引擎 API 去一键还原光线！（如果算出来是变态的负数瞎眼光，直接砍掉归 0）
        return max(0.0, SampleSH9(coefficients, surfaceWS.normal));
    #endif
}


// 4. 把我们之前写的实习生 GetGI 里面的那句测试红绿伪代码删掉，彻底接手真正的图谱！
// 注意多加一个输入参数：表面信息 Surface (配合新兄弟)
GI GetGI (float2 lightMapUV, Surface surfaceWS) {
    GI gi;
    // 核心汇流：如果是静态，前项发挥作用，后项为 0；如果是动态怪，前项没图变 0，后项发力。
    gi.diffuse = SampleLightMap(lightMapUV) + SampleLightProbe(surfaceWS);
    return gi;
}
#endif
