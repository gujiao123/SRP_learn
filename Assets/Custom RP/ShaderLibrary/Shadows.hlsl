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

// me10: 点/聚光灯 PCF 过滤宏
#if defined(_OTHER_PCF3)
    #define OTHER_FILTER_SAMPLES 4
    #define OTHER_FILTER_SETUP SampleShadow_ComputeSamples_Tent_3x3
#elif defined(_OTHER_PCF5)
    #define OTHER_FILTER_SAMPLES 9
    #define OTHER_FILTER_SETUP SampleShadow_ComputeSamples_Tent_5x5
#elif defined(_OTHER_PCF7)
    #define OTHER_FILTER_SAMPLES 16
    #define OTHER_FILTER_SETUP SampleShadow_ComputeSamples_Tent_7x7
#endif

#define MAX_SHADOWED_DIRECTIONAL_LIGHT_COUNT 4
#define MAX_SHADOWED_OTHER_LIGHT_COUNT 16
#define MAX_CASCADE_COUNT 4

/* --- 纹理与采样器 --- */

// TEXTURE2D_SHADOW 明确该贴图只包含深度值（Depth）
TEXTURE2D_SHADOW(_DirectionalShadowAtlas);
TEXTURE2D_SHADOW(_OtherShadowAtlas);// 采样用的阴影贴图

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
    float4x4 _OtherShadowMatrices[MAX_SHADOWED_OTHER_LIGHT_COUNT]; // 世界→tile空间 变换矩阵
    float4 _OtherShadowTiles[MAX_SHADOWED_OTHER_LIGHT_COUNT]; // xy=tile偏移, z=scale, w=normalBias
    float4 _ShadowDistanceFade; // x: 1/maxDist, y: 1/fadeRange, z: cascadeFade
    float4 _ShadowAtlasSize;    // xy=方向光(size, 1/size), zw=其他光(size, 1/size)
CBUFFER_END

/* --- 数据结构 --- */

// ⚠️ 必须先定义 ShadowMask，因为 ShadowData 里面用到了它
//me06 阴影遮罩shadowmask贴图
struct ShadowMask {
    bool always;    // ← 新加是否一直开启静态烘焙阴影
    bool distance;   // 是否在使用 Distance Shadowmask 模式？ 这个和always是不同的模式,烘焙阴影受到距离影响
    float4 shadows;  // 从贴图里采样到的 4 个通道（可以存 4 盏灯的遮挡数据！）
};

struct ShadowData {
    int cascadeIndex;
    float cascadeBlend; // 级联间的混合系数//用于不同级联之间的渐变过渡 也类似插值 属于0-1 等于1 代表完全不过度 也就是没有用啊 还有这个大于0
    float strength;     // 阴影总强度（基于全局距离衰减）//用于强行剔除级联外的阴影 (0-无阴影 1-有阴影) 应该说是 级联内外的分界线 按钮
    ShadowMask shadowMask; // ← 新加的 阴影遮罩
};

struct DirectionalShadowData {
    float strength;
    int tileIndex;
    float normalBias;
    int shadowMaskChannel;
};

// me10: 点光源/聚光灯的阴影数据（支持实时阴影采样）
struct OtherShadowData {
    float strength;
    int tileIndex;  // 在 Atlas 上的第几个格子
    bool isPoint;  // 是否点光源
    int shadowMaskChannel;
    float3 lightPositionWS;// 光源世界坐标
    float3 lightDirectionWS;// 表面→光源方向（用于选cubemap面）
    float3 spotDirectionWS; // 聚光灯方向（用于算到光平面距离）
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
    // me06 初始化 shadowMask，防止未定义垃圾值
    data.shadowMask.always = false;   // me06b 新加
    data.shadowMask.distance = false;
    data.shadowMask.shadows = 1.0;
    
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
    // me10: 加 _CascadeCount > 0 的判断！
    // 原因：没有方向光时 _CascadeCount=0， i初始就=0，一进入就触发 strength=0
    // 导致所有点/聚光灯的阴影被错误杀掉！ 否则会报错
    if (i == _CascadeCount && _CascadeCount > 0) {
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


//me06
// 新函数：只负责实时级联阴影的采样
float GetCascadedShadow(
    DirectionalShadowData directional, ShadowData global, Surface surfaceWS
) {
     // me08: 用 interpolatedNormal（几何法线）做 Shadow Bias，不受法线贴图扰动
    float3 normalBias = surfaceWS.interpolatedNormal * 
        (directional.normalBias * _CascadeData[global.cascadeIndex].y);
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
    // 级联混合
    if (global.cascadeBlend < 1.0) {
        normalBias = surfaceWS.interpolatedNormal * 
            (directional.normalBias * _CascadeData[global.cascadeIndex + 1].y);
        positionSTS = mul(
            _DirectionalShadowMatrices[directional.tileIndex + 1],
            float4(surfaceWS.position + normalBias, 1.0)
        ).xyz;
         // 执行混合：lerp(远级阴影, 近级阴影, 混合系数)
        shadow = lerp(FilterDirectionalShadow(positionSTS), shadow, global.cascadeBlend);
    }
    return shadow;
}

// 从 Shadow Mask 里取对应通道的遮挡值
float GetBakedShadow(ShadowMask mask, int channel) {
    float shadow = 1.0;
    // me06b：always 和 distance 两种模式都需要读烘焙数据
    if (mask.always || mask.distance) {
        if (channel >= 0) {
            shadow = mask.shadows[channel]; // me06c：动态选择通道！R/G/B/A
        }
    }
    return shadow;
}
// 带强度衰减的版本
float GetBakedShadow(ShadowMask mask, int channel, float strength) {
    if (mask.always || mask.distance) {
        return lerp(1.0, GetBakedShadow(mask, channel), strength);
    }
    return 1.0;
}


float MixBakedAndRealtimeShadows(
    ShadowData global, float shadow, int shadowMaskChannel, float strength
) {
    float baked = GetBakedShadow(global.shadowMask, shadowMaskChannel);
    
    // me06b：Always 模式 - 静态物体永远用烘焙，动态物体叠加实时
    if (global.shadowMask.always) {
        // 实时阴影按距离淡出（超过 MaxDistance 就消失）
        shadow = lerp(1.0, shadow, global.strength);
        // min：取实时和烘焙中较暗的那个（两者同时生效）
        // 动态物体实时阴影 + 静态物体烘焙阴影 = 同时可见
        shadow = min(baked, shadow);
        return lerp(1.0, shadow, strength);
    }
    if (global.shadowMask.distance) {
        // Distance 模式：近处用实时，远处用烘焙
        shadow = lerp(baked, shadow, global.strength);
        return lerp(1.0, shadow, strength);
    }
    // 没有 Shadow Mask：走原来的逻辑
    return lerp(1.0, shadow, strength * global.strength);
}
/**
 * \brief 计算最终方向光阴影衰减值 [0, 1]
 */
 //me10这个是计算方向光的阴影衰减值
float GetDirectionalShadowAttenuation(
     DirectionalShadowData directional, ShadowData global, Surface surfaceWS
)
{

    //只有人为定义了材质可以接受阴影才会显示阴影
    #if !defined(_RECEIVE_SHADOWS)
    return 1.0;
    #endif
    float shadow;
    //me06 为了判断使用实时阴影还是烘焙阴影

    //me06 global.strength 近处=1.0，超出 Max Distance=0.0 Shadow Settings 的 Max Distance
    //me06directional.strength 这盏灯的阴影强度是多少？ Light 组件的 Shadow Strength 属性
//     ① 摄像机退很远，超出 Max Shadow Distance：
//     directional.strength = 1.0（灯很亮，正常）
//     global.strength = 0.0（太远了）
//     相乘 = 0 → 进入烘焙阴影分支！✅
// ② 灯没有实时投影体（我们返回了负数）：
//     directional.strength = -1.0（负数信号）
//     global.strength = 1.0（摄像机很近，没问题）
//     相乘 = -1.0 → 也是 ≤ 0 → 进入烘焙阴影分支！✅
// ③ 正常情况：
//     directional.strength = 1.0
//     global.strength = 1.0
//     相乘 = 1.0 → 走实时阴影分支 ✅
    if (directional.strength * global.strength <= 0.0) {
        // 远处或无实时投影体时：直接返回烘焙阴影
        shadow = GetBakedShadow(
            global.shadowMask, directional.shadowMaskChannel, abs(directional.strength)
        );
    } else {
        shadow = GetCascadedShadow(directional, global, surfaceWS);
        shadow = MixBakedAndRealtimeShadows(
            global, shadow, directional.shadowMaskChannel, directional.strength
        );
    }
    /*如果 strength 是 0：旋钮在最左边，输出全亮 1.0（阴影消失了）。
    如果 strength 是 1：旋钮在最右边，输出原始结论 shadow（该黑的地方全黑）。
    如果 strength 是 0.5：输出 0.5 + 0.5 * shadow，原本全黑的地方变成了半灰。*/
    return shadow;
}


// me10: 采样 Other Shadow Atlas（带边界裁剪）
float SampleOtherShadowAtlas (float3 positionSTS, float3 bounds) {
    positionSTS.xy = clamp(positionSTS.xy, bounds.xy, bounds.xy + bounds.z);
    return SAMPLE_TEXTURE2D_SHADOW(
        _OtherShadowAtlas, SHADOW_SAMPLER, positionSTS
    );
}

// me10: PCF 过滤采样（使用 _ShadowAtlasSize.wwzz）
float FilterOtherShadow (float3 positionSTS, float3 bounds) {
    #if defined(OTHER_FILTER_SETUP)
        real weights[OTHER_FILTER_SAMPLES];
        real2 positions[OTHER_FILTER_SAMPLES];
        float4 size = _ShadowAtlasSize.wwzz;
        OTHER_FILTER_SETUP(size, positionSTS.xy, weights, positions);
        float shadow = 0;
        for (int i = 0; i < OTHER_FILTER_SAMPLES; i++) {
            shadow += weights[i] * SampleOtherShadowAtlas(
                float3(positions[i].xy, positionSTS.z), bounds
            );
        }
        return shadow;
    #else
        return SampleOtherShadowAtlas(positionSTS, bounds);
    #endif
}

// me10: 点光源的 6 个面法线（方向与 cubemap face 相反）
static const float3 pointShadowPlanes[6] = {
    float3(-1.0, 0.0, 0.0),
    float3(1.0, 0.0, 0.0),
    float3(0.0, -1.0, 0.0),
    float3(0.0, 1.0, 0.0),
    float3(0.0, 0.0, -1.0),
    float3(0.0, 0.0, 1.0)
};

// me10: 计算点/聚光灯实时阴影
float GetOtherShadow (
    OtherShadowData other, ShadowData global, Surface surfaceWS
) {
    float tileIndex = other.tileIndex;
    float3 lightPlane = other.spotDirectionWS;
    // 点光源：根据方向选择 cubemap 面
    if (other.isPoint) {
        float faceOffset = CubeMapFaceID(-other.lightDirectionWS);
        tileIndex += faceOffset;
        lightPlane = pointShadowPlanes[faceOffset];
    }
    float4 tileData = _OtherShadowTiles[tileIndex];
    float3 surfaceToLight = other.lightPositionWS - surfaceWS.position;
    float distanceToLightPlane = dot(surfaceToLight, lightPlane);
    // 法线偏移随距离缩放（透视投影 texel 大小随距离线性增长）
    float3 normalBias = surfaceWS.interpolatedNormal *
        (distanceToLightPlane * tileData.w);
    float4 positionSTS = mul(
        _OtherShadowMatrices[tileIndex],
        float4(surfaceWS.position + normalBias, 1.0)
    );
    // 透视除法
    return FilterOtherShadow(positionSTS.xyz / positionSTS.w, tileData.xyz);
}

// me10: 改写 GetOtherShadowAttenuation，支持实时 + 烘焙阴影混合（和方向光逻辑一致）
//me10这个是计算点/聚光灯的阴影衰减值
float GetOtherShadowAttenuation (
    OtherShadowData other, ShadowData global, Surface surfaceWS
) {
    #if !defined(_RECEIVE_SHADOWS)
        return 1.0;
    #endif
    float shadow;
    // other.strength * global.strength 的符号判断（和方向光一样）:
    // 正数 = 有实时阴影，需要采样
    // 负数或零 = 超出距离或只有烘焙 → 只查 Shadow Mask
    if (other.strength * global.strength <= 0.0) {
        shadow = GetBakedShadow(
            global.shadowMask, other.shadowMaskChannel, abs(other.strength)
        );
    } else {
        shadow = GetOtherShadow(other, global, surfaceWS);
        shadow = MixBakedAndRealtimeShadows(
            global, shadow, other.shadowMaskChannel, other.strength
        );
    }
    return shadow;
}

#endif
