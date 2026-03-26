#ifndef CUSTOM_FRAGMENT_INCLUDED
#define CUSTOM_FRAGMENT_INCLUDED

// me15: Fragment 结构体——统一管理片元阶段用到的所有屏幕和深度信息
// 以前零散地到处用 positionCS.xy，现在统一打包
//
// 各字段用途：
//   positionSS  → 屏幕像素坐标（LOD 抖动、InterleavedGradientNoise）
//   screenUV    → 屏幕 UV（0~1）（采样深度/颜色快照时用）
//   depth       → 该片元的 view-space 深度（Near Fade 用）
//   bufferDepth → 深度快照里对应位置的深度（Soft Particles 用）

// 声明深度贴图和颜色贴图（由 CameraRenderer 拷贝好再绑定）
TEXTURE2D(_CameraColorTexture);
TEXTURE2D(_CameraDepthTexture);

float4 _CameraBufferSize;  // me16: 存放实际 buffer 的 1/w, 1/h, w, h

struct Fragment {
    float2 positionSS;   // 屏幕像素坐标，如 (640.5, 360.5)
    float2 screenUV;     // 屏幕 UV（positionSS / _ScreenParams.xy），范围 0~1
    float  depth;        // 该片元自身的 view-space 深度（离相机的距离）
    float  bufferDepth;  // 深度快照里同一屏幕位置的固体深度（场景几何体的深度）
};

// me15: 判断当前是否是正交相机
// unity_OrthoParams.w == 1 → 正交；== 0 → 透视
bool IsOrthographicCamera() {
    return unity_OrthoParams.w;
}

// me15: 把正交相机的原始深度值（0~1）转换为 view-space 线性深度
// 参数：rawDepth = depth buffer 里的原始值（非线性，0~1）
// 公式：先反转（如果是 ReversedZ），再映射到 [near, far] 范围
float OrthographicDepthBufferToLinear(float rawDepth) {
    #if UNITY_REVERSED_Z
        rawDepth = 1.0 - rawDepth;  // ReversedZ 时，近=1，远=0，需要翻转
    #endif
    // _ProjectionParams.y = near，.z = far
    return (_ProjectionParams.z - _ProjectionParams.y) * rawDepth + _ProjectionParams.y;
}

// me15: 根据 SV_POSITION（屏幕空间位置）构建 Fragment
// 在顶点着色器里叫 positionCS（裁剪空间），
// 进入片元着色器后 GPU 自动转成 positionSS（屏幕坐标），
// 我们改名 positionCS_SS 来表示这种双重含义
Fragment GetFragment(float4 positionCS_SS) {
    Fragment f;

    // 屏幕像素坐标（0.5偏移，左下角第一个像素是 (0.5, 0.5)） 自带0.5偏移 通过顶点着色器传入
    f.positionSS = positionCS_SS.xy;

    // 屏幕 UV（归一化到 0~1）
    //f.screenUV = f.positionSS / _ScreenParams.xy;
    f.screenUV = f.positionSS * _CameraBufferSize.xy;  // me16: 用 buffer 实际尺寸的倒数，避免 _ScreenParams 与缩放 buffer 不匹配


    // 该片元自身深度：
    //   透视相机：SV_POSITION.w = view-space 深度（GPU 自动提供）
    //   正交相机：SV_POSITION.w 永远 = 1，要用 .z 通道换算
    f.depth = IsOrthographicCamera()
        ? OrthographicDepthBufferToLinear(positionCS_SS.z)
        : positionCS_SS.w;

    // 从深度快照里采样同一屏幕位置的场景深度（固体的深度）
    // SAMPLE_DEPTH_TEXTURE_LOD 只取 R 通道（深度值），LOD=0（最清晰）
    float rawBufferDepth = SAMPLE_DEPTH_TEXTURE_LOD(
        _CameraDepthTexture, sampler_point_clamp, f.screenUV, 0
    );

    // 把原始深度转成 view-space 线性深度（和 fragment.depth 同一单位才能相减）
    f.bufferDepth = IsOrthographicCamera()
        ? OrthographicDepthBufferToLinear(rawBufferDepth)
        : LinearEyeDepth(rawBufferDepth, _ZBufferParams);

    return f;
}

// me15: 采样颜色快照（扭曲用）
// uvOffset：法线图提供的屏幕 UV 偏移量（模拟折射）
float4 GetBufferColor(Fragment fragment, float2 uvOffset = float2(0.0, 0.0)) {
    float2 uv = fragment.screenUV + uvOffset;
    //            ↑ 当前片元的屏幕UV  + 偏移量
    //me15这个读取 复制的贴图添加了偏移量 就是去读了 偏移图片的颜色
    return SAMPLE_TEXTURE2D_LOD(_CameraColorTexture, sampler_linear_clamp, uv, 0);
}

#endif
