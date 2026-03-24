#ifndef CUSTOM_LIGHT_INCLUDED
#define CUSTOM_LIGHT_INCLUDED

// 定义最大灯光数，必须和 C# 里的 maxDirLightCount 一致！
#define MAX_DIRECTIONAL_LIGHT_COUNT 4
#define MAX_OTHER_LIGHT_COUNT 64  // me09: 最多64盏点/聚光灯

CBUFFER_START(_CustomLight)
    int _DirectionalLightCount;
    float4 _DirectionalLightColors[MAX_DIRECTIONAL_LIGHT_COUNT];
    float4 _DirectionalLightDirectionsAndMasks[MAX_DIRECTIONAL_LIGHT_COUNT]; // me14: xyz=方向 w=渲染层掩码
    float4 _DirectionalLightShadowData[MAX_DIRECTIONAL_LIGHT_COUNT];         // 方向光阴影数据
    // me09: Other Lights 数据（点光源 + 聚光灯共用）
    int _OtherLightCount;
    float4 _OtherLightColors[MAX_OTHER_LIGHT_COUNT];
    float4 _OtherLightPositions[MAX_OTHER_LIGHT_COUNT];   // xyz=世界坐标, w=1/range²
    float4 _OtherLightDirectionsAndMasks[MAX_OTHER_LIGHT_COUNT]; // me14: xyz=聚光灯方向 w=渲染层掩码
    float4 _OtherLightSpotAngles[MAX_OTHER_LIGHT_COUNT];  // x=a, y=b（角度衰减参数）
    float4 _OtherLightShadowData[MAX_OTHER_LIGHT_COUNT];  // 烘焙阴影数据
CBUFFER_END

// 定义灯光结构体
struct Light {
    float3 color;
    float3 direction;
    float attenuation;//等于1代表颜色底色,灯光没有衰减 没有阴影
    uint renderingLayerMask;   // me14
};

// 获取当前有效灯光数量
int GetDirectionalLightCount () {
    return _DirectionalLightCount;
}

// me09: 其他灯光数量（点光源 + 聚光灯）
int GetOtherLightCount () {
    return _OtherLightCount;
}



//构造一个光源的ShadowData 新增了对应级联的 贴图选择 返回一个级联阴影贴图
DirectionalShadowData GetDirectionalShadowData(int lightIndex, ShadowData shadowData)
{
    DirectionalShadowData data;
    // me06：不再乘以 shadowData.strength！
    // 原因：远处 shadowData.strength→0 会让 directional.strength→0
    // 导致 GetDirectionalShadowAttenuation 提前返回 1.0（全亮），
    // 永远无法走到 MixBakedAndRealtimeShadows 去使用烘焙阴影！
    // 现在把 global.strength 的处理权留给 MixBakedAndRealtimeShadows。
    data.strength = _DirectionalLightShadowData[lightIndex].x; // ← 只取灯光自身强度
    //Tile索引
    data.tileIndex = _DirectionalLightShadowData[lightIndex].y + shadowData.cascadeIndex;
    data.normalBias = _DirectionalLightShadowData[lightIndex].z;
    data.shadowMaskChannel = _DirectionalLightShadowData[lightIndex].w; // me06c：读取通道号 哪一个灯光的shadowmask
    return data;
}

//对于每个片元，构造一个方向光源并返回，其颜色与方向取自常量缓冲区的数组中index下标处
Light GetDirectionalLight (int index, Surface surfaceWS, ShadowData shadowData)
{
    Light light;
    //float4的rgb和xyz完全等效
    light.color = _DirectionalLightColors[index].rgb;
    light.direction = _DirectionalLightDirectionsAndMasks[index].xyz;
    light.renderingLayerMask = asuint(_DirectionalLightDirectionsAndMasks[index].w);  // me14
    //构造光源阴影信息
    DirectionalShadowData dirShadowData = GetDirectionalShadowData(index, shadowData);
    light.attenuation = GetDirectionalShadowAttenuation(dirShadowData, shadowData, surfaceWS);
    return light;
}

// me09: --- 点光源 / 聚光灯 ---

// me10: 从 _OtherLightShadowData 构造 OtherShadowData（含 tileIndex、isPoint）
OtherShadowData GetOtherShadowData (int lightIndex) {
    OtherShadowData data;
    data.strength = _OtherLightShadowData[lightIndex].x;
    data.tileIndex = _OtherLightShadowData[lightIndex].y;
    data.isPoint = _OtherLightShadowData[lightIndex].z == 1.0;
    data.shadowMaskChannel = _OtherLightShadowData[lightIndex].w;
    data.lightPositionWS = 0.0;
    data.lightDirectionWS = 0.0;
    data.spotDirectionWS = 0.0;
    return data;
}

// 获取单盏点光源/聚光灯的 Light 结构体
Light GetOtherLight (int index, Surface surfaceWS, ShadowData shadowData) {
    Light light;
    light.color = _OtherLightColors[index].rgb;

    // 光线方向：从片元位置 → 光源位置，再归一化
    float3 ray = _OtherLightPositions[index].xyz - surfaceWS.position;
    light.direction = normalize(ray);
    // 距离平方衰减（平方反比定律：强度 ∝ 1/d²）
    // max 防止除以零（极近处过亮但不会崩）
    float distanceSqr = max(dot(ray, ray), 0.00001);

    // 范围衰减：saturate(1-(d²/r²)²)²
    // 在 Range 边缘平滑降到0，不会有硬截断边界线
    float rangeAttenuation = Square(saturate(1.0 - Square(distanceSqr * _OtherLightPositions[index].w)));

    // 聚光灯角度衰减：saturate(dot(灯方向, 光线方向) * a + b)²
    // 点光源的 a=0, b=1 → 始终=1（无角度衰减）
    float3 spotDirection = _OtherLightDirectionsAndMasks[index].xyz;
    light.renderingLayerMask = asuint(_OtherLightDirectionsAndMasks[index].w);  // me14
    float4 spotAngles = _OtherLightSpotAngles[index];
    float spotAttenuation = Square(
        saturate(dot(spotDirection, light.direction) * spotAngles.x + spotAngles.y)
    );

    // me10: 构造阴影数据，传入光源位置和方向
    OtherShadowData otherShadowData = GetOtherShadowData(index);
    otherShadowData.lightPositionWS = _OtherLightPositions[index].xyz;
    otherShadowData.lightDirectionWS = light.direction;
    otherShadowData.spotDirectionWS = spotDirection;
    light.attenuation =
        GetOtherShadowAttenuation(otherShadowData, shadowData, surfaceWS) *
        spotAttenuation * rangeAttenuation / distanceSqr;

    return light;
}

#endif