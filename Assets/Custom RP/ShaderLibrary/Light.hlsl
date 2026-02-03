#ifndef CUSTOM_LIGHT_INCLUDED
#define CUSTOM_LIGHT_INCLUDED

// 定义最大灯光数，必须和 C# 里的 maxDirLightCount 一致！
#define MAX_DIRECTIONAL_LIGHT_COUNT 4

// --- 这里就是创建变量的地方 ---
// CBUFFER_START(_CustomLight) 
// 这是一个全局 CBUFFER，所有 Shader 都能访问
//用CBuffer包裹构造方向光源的两个属性，cpu会每帧传递（修改）这两个属性到GPU的常量缓冲区，对于一次渲染过程这两个值恒定

CBUFFER_START(_CustomLight)
    int _DirectionalLightCount;
    float4 _DirectionalLightColors[MAX_DIRECTIONAL_LIGHT_COUNT];
    float4 _DirectionalLightDirections[MAX_DIRECTIONAL_LIGHT_COUNT];
CBUFFER_END
// ----------------------------

// 定义灯光结构体
struct Light {
    float3 color;
    float3 direction;
};

// 获取当前有效灯光数量
int GetDirectionalLightCount () {
    return _DirectionalLightCount;
}

// 获取第 index 盏灯的数据
Light GetDirectionalLight (int index) {
    Light light;
    light.color = _DirectionalLightColors[index].rgb;
    light.direction = _DirectionalLightDirections[index].xyz;
    return light;
}

#endif