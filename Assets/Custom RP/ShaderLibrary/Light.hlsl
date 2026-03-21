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
    float4 _DirectionalLightShadowData[MAX_DIRECTIONAL_LIGHT_COUNT];
CBUFFER_END
// ----------------------------

// 定义灯光结构体
struct Light {
    float3 color;
    float3 direction;
    float attenuation;//等于1代表颜色底色,灯光没有衰减 没有阴影
};

// 获取当前有效灯光数量
int GetDirectionalLightCount () {
    return _DirectionalLightCount;
}




//构造一个光源的ShadowData 新增了对应级联的 贴图选择 返回一个级联阴影贴图
DirectionalShadowData GetDirectionalShadowData(int lightIndex, ShadowData shadowData)
{
    DirectionalShadowData data;
    //阴影强度 增加shadowData.strength 纯粹是为了截断超过级联阴影的阴影,直接不显示,当然可以自己顶一个强度把
    //超过了 就是data.strength=0 代表全亮 无阴影 
    data.strength = _DirectionalLightShadowData[lightIndex].x * shadowData.strength;
    //Tile索引
    data.tileIndex = _DirectionalLightShadowData[lightIndex].y+ shadowData.cascadeIndex;
    
    data.normalBias = _DirectionalLightShadowData[lightIndex].z;

    return data;
}

//对于每个片元，构造一个方向光源并返回，其颜色与方向取自常量缓冲区的数组中index下标处
Light GetDirectionalLight (int index, Surface surfaceWS, ShadowData shadowData)
{
    Light light;
    //float4的rgb和xyz完全等效
    light.color = _DirectionalLightColors[index].rgb;
    light.direction = _DirectionalLightDirections[index].xyz;
    //构造光源阴影信息
    DirectionalShadowData dirShadowData = GetDirectionalShadowData(index, shadowData);
    //根据片元的强度
    light.attenuation = GetDirectionalShadowAttenuation(dirShadowData,shadowData,surfaceWS);
    
    //这个是debug的模式展示级联的效果,
    //根据级联贴图->对应阴影强度->完成 阴影效果就是影响光照强度罢了
    //light.attenuation = shadowData.cascadeIndex * 0.25;
    return light;
}


#endif