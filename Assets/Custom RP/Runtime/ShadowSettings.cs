using UnityEngine;


//配置驱动


// 这是一个纯数据类，只负责存储配置
[System.Serializable]
public class ShadowSettings {
    
    // 最大阴影距离 (超过这个距离就不画阴影了)
    //maxDistance决定视野内多大范围会被渲染到阴影贴图上，距离主摄像机超过maxDistance的物体不会被渲染在阴影贴图上
    //其具体逻辑猜测如下：
    //1.根据maxDistance（或者摄像机远平面）得到一个BoundingBox（也可能是个球型），这个BoundingBox容纳了所有要渲染阴影的物体
    //2.根据这个BoundingBox（也可能是个球型）和方向光源的方向，确定渲染阴影贴图用的正交摄像机的视锥体，渲染阴影贴图

    [Min(0.001f)]
    public float maxDistance = 100f;
	
    [Range(0.001f, 1f)]
    public float distanceFade = 0.1f;
    
   
    
    // 定义纹理大小枚举
    public enum TextureSize {
        _256 = 256, _512 = 512, _1024 = 1024,
        _2048 = 2048, _4096 = 4096, _8192 = 8192
    }
    public enum FilterMode {
        PCF2x2, PCF3x3, PCF5x5, PCF7x7
    }
    
    

    // 定向光的阴影设置 (嵌套结构)
    [System.Serializable]
    public struct Directional {
        public TextureSize atlasSize;
        //阴影的类似过滤模式 比如双线性采样的方法PCF
        public FilterMode filter;

        // 级联数量 (后面会讲)
        [Range(1, 4)]
        public int cascadeCount;
        
        // 级联比例 (先写上，后面会用)
        [Range(0f, 1f)]
        public float cascadeRatio1, cascadeRatio2, cascadeRatio3;
        
        // 返回级联比例的 Vector3 (方便传给 Unity API)
        public Vector3 CascadeRatios => 
            new Vector3(cascadeRatio1, cascadeRatio2, cascadeRatio3);
        
        //最大一层级联的阴影过度更加细致控制远处的阴影如何淡出
        [Range(0.001f, 1f)]
        public float cascadeFade;
        
      
        //级联间的混合模式
        public enum CascadeBlendMode {
            Hard,
            Soft, 
            Dither//这个是抽签混合阴影 配合TAA很快的
        }
        
        public CascadeBlendMode cascadeBlend;
        
    }
    
    // 默认值
    public Directional directional = new Directional {
        atlasSize = TextureSize._1024,
        filter = FilterMode.PCF2x2,
        cascadeCount = 4,
        cascadeRatio1 = 0.1f,//100米内 渲染 0-10(100*0.1) 
        cascadeRatio2 = 0.25f,//
        cascadeRatio3 = 0.5f,//最后自动占比 51-100的 层次 所以一共四个层次
        cascadeFade = 0.1f,
        cascadeBlend = Directional.CascadeBlendMode.Hard

    };
}