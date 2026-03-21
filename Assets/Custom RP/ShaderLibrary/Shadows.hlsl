#ifndef CUSTOM_SHADOWS_INCLUDED
#define CUSTOM_SHADOWS_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Shadow/ShadowSamplingTent.hlsl"

/* --- 宏定义与配置 --- */

// 根据开启的关键字定义 PCF 采样参数
#if defined(_DIRECTIONAL_PCF3)
    #define DIRECTIONAL_FILTER_SAMPLES 4
    #define DIRECTIONAL_FILTER_SETUP SampleShadow_ComputeSamples_Tent_3x3
#elif defined(_DIRECTIONAL_PCF5)
    #define DIRECTIONAL_FILTER_SAMPLES 9
    #define DIRECTIONAL_FILTER_SETUP SampleShadow_ComputeSamples_Tent_5x5
#elif defined(_DIRECTIONAL_PCF7)
    #define DIRECTIONAL_FILTER_SAMPLES 16
    #define DIRECTIONAL_FILTER_SETUP SampleShadow_ComputeSamples_Tent_7x7
#endif

#define MAX_SHADOWED_DIRECTIONAL_LIGHT_COUNT 4
#define MAX_CASCADE_COUNT 4

/* --- 纹理与采样器 --- */

// TEXTURE2D_SHADOW 明确该贴图只包含深度值（Depth）
TEXTURE2D_SHADOW(_DirectionalShadowAtlas);

// SAMPLER_CMP 定义硬件比较采样器。这种采样器在采样时直接执行深度比较，
// 返回 0 或 1，利用 GPU 原生指令加速 PCF 过滤。
//sampler_linear_clamp_compare这个取名十分有讲究，Unity会将这个名字翻译成使用Linear过滤、Clamp包裹的用于深度比较的采样器
//me 这个对于我们altas图集来说可以避免越界读取阴影贴图
#define SHADOW_SAMPLER sampler_linear_clamp_compare
SAMPLER_CMP(SHADOW_SAMPLER);

/* --- 常量缓冲区 --- */

CBUFFER_START(_CustomShadows)
    int _CascadeCount;
    float4 _CascadeCullingSpheres[MAX_CASCADE_COUNT];
    float4 _CascadeData[MAX_CASCADE_COUNT];
    float4x4 _DirectionalShadowMatrices[MAX_SHADOWED_DIRECTIONAL_LIGHT_COUNT * MAX_CASCADE_COUNT];
    float4 _ShadowDistanceFade; // x: 1/maxDist, y: 1/fadeRange, z: cascadeFade
    float4 _ShadowAtlasSize;    // x: 1/size, y: 1/size, z: size, w: size
CBUFFER_END

/* --- 数据结构 --- */

struct ShadowData {
    int cascadeIndex;
    float cascadeBlend; // 级联间的混合系数//用于不同级联之间的渐变过渡 也类似插值 属于0-1 等于1 代表完全不过度 也就是没有用啊 还有这个大于0
    float strength;     // 阴影总强度（基于全局距离衰减）//用于强行剔除级联外的阴影 (0-无阴影 1-有阴影) 应该说是 级联内外的分界线 按钮
};

struct DirectionalShadowData {
    float strength;     // 灯光阴影强度（由 Light 组件设置）
    int tileIndex;      // 对应阴影图集中的 Tile 索引
    float normalBias;   // 法线偏移量
};

/* --- 核心计算函数 --- */

/**
 * \brief 计算阴影线性衰减强度
 * \param dist 距离值（物体距摄像机或球心距离）
 * \param scale 缩放系数（通常为 1/Radius 或 1/MaxDistance）
 * \param fade 衰减速率
 */
float FadedShadowStrength(float dist, float scale, float fade) {
    //saturate抑制了近处的级联阴影强度到1
    return saturate((1.0 - dist * scale) * fade);
}

/**
 * \brief 获取当前片元的级联阴影数据
 */
ShadowData GetShadowData(Surface surfaceWS) {
    ShadowData data;
    data.cascadeBlend = 1.0; // 默认为 1（代表完全位于当前级联，不混合）
    
    // 1. 全局距离衰减：超出最大阴影距离后阴影逐渐消失shadowsetting设置
    data.strength = FadedShadowStrength(
        surfaceWS.depth, _ShadowDistanceFade.x, _ShadowDistanceFade.y
    );

    int i;
    // 2. 遍历级联包围球，确定片元所属级联
    for (i = 0; i < _CascadeCount; i++) {
        float4 sphere = _CascadeCullingSpheres[i];
        float distanceSqr = DistanceSquared(surfaceWS.position, sphere.xyz);
        if (distanceSqr < sphere.w) {
            float fade = FadedShadowStrength(
            //!!1.0 / sphere.w,被在cpu端计算存放在_CascadeData[i].x,GPU少计算一次除法 
                distanceSqr, _CascadeData[i].x, _ShadowDistanceFade.z
            );
            
            if (i == _CascadeCount - 1) {
                // 最后一级级联：执行“球缘淡出”，直接减弱阴影强度
                data.strength *= fade;
            }
            else {
                // 级联过渡区：记录混合系数用于级联间的平滑切换
                data.cascadeBlend = fade;
            }
            break;
        }
    }

    // 3. 处理超出所有级联范围的情况
    //i=4 就代表超出了0-3 所以 就为0 
    if (i == _CascadeCount) {
        data.strength = 0.0;
    }
    // 4. Dither 混合逻辑：利用噪声随机跳转级联索引，实现单次采样模拟软过渡
    #if defined(_CASCADE_BLEND_DITHER)
    else if (data.cascadeBlend < surfaceWS.dither) {
        i += 1;
    }
    #endif

    // 5. 静态分支优化：若未开启软混合模式，将 blend 置 1，后续编译器将剔除相关分支
    #if !defined(_CASCADE_BLEND_SOFT)
    data.cascadeBlend = 1.0;
    #endif

    data.cascadeIndex = i;
    return data;
}

/**
 * \brief 采样原始阴影图集
 */
//采样ShadowAtlas，传入positionSTS（STS是Shadow Tile Space，即阴影贴图对应Tile像素空间下的片元坐标）
// xy是 对应tile上的uv坐标 z是对应像素距离灯光的距离
float SampleDirectionalShadowAtlas(float3 positionSTS)
{
    //使用特定宏来采样阴影贴图
    return SAMPLE_TEXTURE2D_SHADOW(_DirectionalShadowAtlas,SHADOW_SAMPLER,positionSTS);
}

/**
 * \brief 执行 PCF 过滤采样
 */
float FilterDirectionalShadow (float3 positionSTS) {
    // 检查是否启用了 PCF 滤波模式（即是否定义了上述任何一个 SETUP 函数）
    #if defined(DIRECTIONAL_FILTER_SETUP)
    // 1. 准备存储采样权重和坐标的数组
    float weights[DIRECTIONAL_FILTER_SAMPLES];
    float2 positions[DIRECTIONAL_FILTER_SAMPLES];
        
    // 2. 构建尺寸向量：X,Y 是纹理像素大小 (1/Size)，Z,W 是纹理总尺寸 (Size)
    // _ShadowAtlasSize.yyxx 这种写法是为了适配 Tent 库函数的参数要求
    float4 size = _ShadowAtlasSize.yyxx; 
        
    // 3. 调用库函数计算所有采样点的位置和权重
    // 该函数会计算出能够模拟“帐篷形状”分布的采样坐标
    DIRECTIONAL_FILTER_SETUP(size, positionSTS.xy, weights, positions);
        
    float shadow = 0;
    // 4. 执行循环采样
    for (int i = 0; i < DIRECTIONAL_FILTER_SAMPLES; i++) {
        // 采样阴影贴图，并乘以对应的权重进行累加
        // positionSTS.z 保持不变，代表当前像素的深度值
        shadow += weights[i] * SampleDirectionalShadowAtlas(
            float3(positions[i].xy, positionSTS.z)
        );
    }
    return shadow;
        
    #else
    // 如果没有开启 PCF（即 PCF 2x2 或 Hard Shadow），则直接执行单次采样
    // 硬件会自动处理 2x2 的双线性过滤
    return SampleDirectionalShadowAtlas(positionSTS);
    #endif
}

/**
 * \brief 计算最终方向光阴影衰减值 [0, 1]
 */
float GetDirectionalShadowAttenuation(DirectionalShadowData directional, ShadowData global, Surface surfaceWS) {
    //忽略不开启阴影和阴影强度为0的光源
    if(directional.strength <= 0.0)
    {
        return 1.0;
    }
    
    //只有人为定义了材质可以接受阴影才会显示阴影
    #if !defined(_RECEIVE_SHADOWS)
    return 1.0;
    #endif

    // 计算当前级联下的法线偏移，解决自阴影（Shadow Acne）问题
    float3 normalBias = surfaceWS.normal * (directional.normalBias * _CascadeData[global.cascadeIndex].y);
    //根据对应Tile阴影变换矩阵和片元的世界坐标计算Tile上的像素坐标STS
    //data.tileIndex 告诉它：“去查第几个矩阵”，从而确保它能定位到大图集里正确的那个小格（Tile）。
    float3 positionSTS = mul(
        _DirectionalShadowMatrices[directional.tileIndex], 
        float4(surfaceWS.position + normalBias, 1.0)
    ).xyz;
    //采样Tile得到阴影强度值
    float shadow = FilterDirectionalShadow(positionSTS);

    // 级联混合逻辑：如果 blend < 1，说明需要对下一级级联采样并混合
    // 注意：若未定义 _CASCADE_BLEND_SOFT，此处的 if 分支会被编译器优化剔除
    if (global.cascadeBlend < 1.0) {
        normalBias = surfaceWS.normal * (directional.normalBias * _CascadeData[global.cascadeIndex + 1].y);
        positionSTS = mul(
            _DirectionalShadowMatrices[directional.tileIndex + 1],
            float4(surfaceWS.position + normalBias, 1.0)
        ).xyz;
        
        // 执行混合：lerp(远级阴影, 近级阴影, 混合系数)
        shadow = lerp(FilterDirectionalShadow(positionSTS), shadow, global.cascadeBlend);
    }

    //考虑光源的阴影强度，strength为0，依然没有阴影
    //不至于lerp(a,b,t) 使得 根据阴影强度可以控制
    /*
    如果 strength 是 0：旋钮在最左边，输出全亮 1.0（阴影消失了）。
    如果 strength 是 1：旋钮在最右边，输出原始结论 shadow（该黑的地方全黑）。
    如果 strength 是 0.5：输出 0.5 + 0.5 * shadow，原本全黑的地方变成了半灰。*/
    //为了艺术效果
    return lerp(1.0, shadow, directional.strength * global.strength);
}

#endif