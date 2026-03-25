#ifndef CUSTOM_CAMERA_RENDERER_PASSES_INCLUDED
#define CUSTOM_CAMERA_RENDERER_PASSES_INCLUDED

// me15: CameraRenderer 全屏 Pass 的 HLSL 实现
// 复用了 PostFXStack 的全屏顶点着色器模式（程序化三角形，不需要 Mesh）

// 源纹理（要被拷贝的贴图）
TEXTURE2D(_SourceTexture);

// me15: 全屏三角形顶点着色器（程序化，不需要 Mesh）
// 传入 vertexID（0,1,2），直接生成覆盖全屏的大三角形
struct Varyings {
    float4 positionCS_SS : SV_POSITION;
    float2 screenUV      : VAR_SCREEN_UV;
};

Varyings DefaultPassVertex(uint vertexID : SV_VertexID) {
    Varyings output;
    // 用 vertexID 生成三角形的三个顶点
    // vertexID=0: (-1,-1) 左下，=1: (-1,3) 左上，=2: (3,-1) 右下
    // 这个三角形足够大，覆盖整个 NDC [-1,1] 范围
    output.positionCS_SS = float4(
        vertexID <= 1 ? -1.0 : 3.0,
        vertexID == 1 ?  3.0 : -1.0,
        0.0, 1.0
    );
    // UV 对应 [0,1] 范围
    output.screenUV = float2(
        vertexID <= 1 ? 0.0 : 2.0,
        vertexID == 1 ? 2.0 : 0.0
    );
    // me15: 部分平台 UV 竖直翻转
    if (_ProjectionParams.x < 0.0) {
        output.screenUV.y = 1.0 - output.screenUV.y;
    }
    return output;
}

// Pass 0: 拷贝颜色（直接输出源纹理颜色）
float4 CopyPassFragment(Varyings input) : SV_TARGET {
    return SAMPLE_TEXTURE2D_LOD(_SourceTexture, sampler_linear_clamp, input.screenUV, 0);
}

// Pass 1: 拷贝深度（采样深度值并写入深度缓冲）
float CopyDepthPassFragment(Varyings input) : SV_DEPTH {
    return SAMPLE_DEPTH_TEXTURE_LOD(_SourceTexture, sampler_point_clamp, input.screenUV, 0);
}

#endif
