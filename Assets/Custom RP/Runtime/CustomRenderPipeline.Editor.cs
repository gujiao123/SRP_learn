// 烘焙 Falloff Delegate：让 Unity Lightmapper 用 InverseSquared 衰减
// 只在 Editor 里有效（#if UNITY_EDITOR），运行时自动剥离
//
// 为什么需要：
//   Unity 默认烘焙用旧版 Falloff（不是严格 1/d²）
//   我们的实时 Shader 用 1/d²（平方反比）
//   两者不一致 → Mixed 模式的灯光在实时与烘焙区域交界处亮度不匹配
//
// 解决：注册 Lightmapping.SetDelegate，告诉烘焙器用 InverseSquared

//me09主要是修改烘焙灯光的衰减模式所以是editor使用 毕竟就是在editor烘焙
#if UNITY_EDITOR

using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.GlobalIllumination;
using LightType = UnityEngine.LightType;  // 防止和 GlobalIllumination 命名空间里的 LightType 冲突

public partial class CustomRenderPipeline
{
    // partial void InitializeForEditor() 在 Editor 环境下的真实实现
    partial void InitializeForEditor()
    {
        Lightmapping.SetDelegate(lightsDelegate);
    }

    // me15: Editor 专用清理（尋常就是重置烘焙 Delegate）
    partial void DisposeForEditor()
    {
        Lightmapping.ResetDelegate();
    }

    // 烘焙灯光数据的 delegate
    // 为每盏灯构造对应的 LightDataGI，并统一设为 InverseSquared 衰减
    static Lightmapping.RequestLightsDelegate lightsDelegate =
        (Light[] lights, NativeArray<LightDataGI> output) =>
        {
            var lightData = new LightDataGI();
            for (int i = 0; i < lights.Length; i++)
            {
                Light light = lights[i];
                switch (light.type)
                {
                    case LightType.Directional:
                        var directionalLight = new DirectionalLight();
                        //me09Light = 运行时渲染用的"工程图纸"（游戏引擎格式）
                        //LightDataGI = 烘焙器用的"施工说明书"（离线渲染器格式）
                        //LightmapperUtils.Extract() = 帮你把工程图纸翻译成施工说明书
                        //但翻译完了 falloff 空着 → 我们补上去
                        LightmapperUtils.Extract(light, ref directionalLight);
                        lightData.Init(ref directionalLight);
                        break;

                    case LightType.Point:
                        var pointLight = new PointLight();
                        LightmapperUtils.Extract(light, ref pointLight);
                        lightData.Init(ref pointLight);
                        break;

                    case LightType.Spot:
                        var spotLight = new SpotLight();
                        LightmapperUtils.Extract(light, ref spotLight);
                        // Unity 2022+：可以设置内角和角度衰减类型
                        spotLight.innerConeAngle = light.innerSpotAngle * Mathf.Deg2Rad;// ← 我们加的
                        spotLight.angularFalloff = AngularFalloffType.AnalyticAndInnerAngle;// ← 我们加的
                        lightData.Init(ref spotLight);
                        break;

                    case LightType.Area:
                        // Area Light 不支持实时，只能强制 Baked
                        var rectangleLight = new RectangleLight();
                        LightmapperUtils.Extract(light, ref rectangleLight);
                        rectangleLight.mode = LightMode.Baked;// ← 我们加的（Area Light 强制 Baked）
                        lightData.Init(ref rectangleLight);
                        break;

                    default:
                        // 不认识的灯类型：告诉烘焙器不要烘焙
                        lightData.InitNoBake(light.GetInstanceID());
                        break;
                }

                // 所有灯统一使用 InverseSquared，和我们 Shader 里的 1/d² 对齐
               
                // ========= 最关键的一行（整个文件的核心）=========
                lightData.falloff = FalloffType.InverseSquared;  // ← 这就是全部的目的！
                //            ↑
                //把所有灯的衰减改成 1/d²（和我们 Shader 一致）
                output[i] = lightData;
            }
        };
}

#endif
