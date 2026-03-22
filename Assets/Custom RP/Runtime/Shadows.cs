using UnityEngine;
using UnityEngine.Rendering;



/// <summary>
/// 现在shadow完全属于light.cs 被调用
/// </summary>
public class Shadows {
    
    const string bufferName = "Shadows";
    CommandBuffer buffer = new CommandBuffer { name = bufferName };
    
    ScriptableRenderContext context;
    CullingResults cullingResults;
    ShadowSettings settings;
    
    // 最大支持的阴影光源数量 每个灯光还来得级联
    const int maxShadowedDirectionalLightCount =4,maxCascades = 4;
    static string[] directionalFilterKeywords = {
        "_DIRECTIONAL_PCF3",
        "_DIRECTIONAL_PCF5",
        "_DIRECTIONAL_PCF7",
    };
    
    
    static string[] cascadeBlendKeywords = {
        "_CASCADE_BLEND_SOFT",
        "_CASCADE_BLEND_DITHER"
    };
    
    // 存储哪些光源需要阴影 保存下标
    struct ShadowedDirectionalLight {
        public int visibleLightIndex;
        public float slopeScaleBias;
        public float nearPlaneOffset; // 新增：近平面偏移
    }
    ShadowedDirectionalLight[] shadowedDirectionalLights =
        new ShadowedDirectionalLight[maxShadowedDirectionalLightCount];
    
    int shadowedDirectionalLightCount;
    
    // Shader 属性 ID
    private static int dirShadowAtlasId = Shader.PropertyToID("_DirectionalShadowAtlas"),
        dirShadowMatricesId = Shader.PropertyToID("_DirectionalShadowMatrices"),
            cascadeCountId = Shader.PropertyToID("_CascadeCount"),//就是告诉每一个物体 要去哪一个级联里取阴影 
            cascadeCullingSpheresId = Shader.PropertyToID("_CascadeCullingSpheres"),
        cascadeDataId = Shader.PropertyToID("_CascadeData"),
        shadowAtlasSizeId = Shader.PropertyToID("_ShadowAtlasSize"),//告诉图集大小用于 再每个tile中计算偏移 采样过滤 充分利用cascadeData
        shadowDistanceFadeId = Shader.PropertyToID("_ShadowDistanceFade");

    
    static Vector4[]
        cascadeCullingSpheres = new Vector4[maxCascades],
        cascadeData = new Vector4[maxCascades];
    
    static Matrix4x4[] dirShadowMatrices =
        new Matrix4x4[maxShadowedDirectionalLightCount* maxCascades]; 
    
    public void Setup (
        ScriptableRenderContext context, 
        CullingResults cullingResults,
        ShadowSettings settings
    ) {
        this.context = context;
        this.cullingResults = cullingResults;
        this.settings = settings;
        
        shadowedDirectionalLightCount = 0; // 重置计数
        useShadowMask = false; // me06：每帧重置，避免上帧状态污染
    }

    //me06使用shadowmask 两种模式 _SHADOW_MASK_ALWAYS就是 静态的烘焙的阴影一直存在 不受到距离调节
    static string[] shadowMaskKeywords = {
    "_SHADOW_MASK_ALWAYS",   // index 0
    "_SHADOW_MASK_DISTANCE"  // index 1
};
    bool useShadowMask; // 这帧有没有需要 Shadow Mask 的灯？
    
    
    
    // 公共接口：供 Lighting.cs 调用，预留阴影槽位
    //每帧执行，用于为light配置shadow altas（shadowMap）上预留一片空间来渲染阴影贴图，同时存储一些其他必要信息
    /// <summary>
    /// 公共接口：供 Lighting.cs 调用，预留阴影槽位 
    /// </summary>
    /// <param name="light"></param>
    /// <param name="visibleLightIndex">除光照剔除外，剔除的灯光烘焙的光也会被剔除</param>
    /// <returns>一个是灯光强度 一个是对应灯光的ID用于对应阴影贴图</returns>
    public Vector4 ReserveDirectionalShadows (Light light, int visibleLightIndex) {

        float maskChannel = -1;  // -1 = 没有 Shadow Mask（不读任何通道）
        // 检查是否有空位，且光源真的需要阴影
        //!!这个就需要 在 light设置里面选择 阴影类型
        if (shadowedDirectionalLightCount < maxShadowedDirectionalLightCount &&
            light.shadows != LightShadows.None && 
            light.shadowStrength > 0f
        ) {


        //me06 检查是否需要shadowmask 根据这个光是否是Mixed类型而且mixedLightingMode是Shadowmask
        //?什么意思 这个shadowmask不是在render里面设置的吗 怎么跟light扯上关系了
        // ① 先检查 Shadow Mask（移到这里！）
        LightBakingOutput lightBaking = light.bakingOutput;
        if (lightBaking.lightmapBakeType == LightmapBakeType.Mixed &&
            lightBaking.mixedLightingMode == MixedLightingMode.Shadowmask) { 
            useShadowMask = true;
            // me06c：读取这盏灯分配到的通道号（Unity 烘焙时自动分配）
            //   0 = R 通道（影响最大的第1盏灯）
            //   1 = G 通道（第2盏灯）
            //   2 = B 通道， 3 = A 通道...
            maskChannel = lightBaking.occlusionMaskChannel;
        }


        // ② 再检查有没有实时投影体
        // 检查是否有物体在阴影范围内
        //Func ，cullingResults.GetShadowCasterBounds 该方法传入lightIndex为阴影投射光源的索引，outBounds为要计算的边界，其返回值为一个bool值，如果光源影响了场景中至少一个阴影投射对象，则为true。该方法返回封装了可见阴影投射物的包围盒（包围盒里的物体就是所有需要渲染到阴影贴图上的物体），其可用于动态调整级联范围。

        if (cullingResults.GetShadowCasterBounds(visibleLightIndex, out Bounds b)) {
                // 存储光源索引
                shadowedDirectionalLights[shadowedDirectionalLightCount] = 
                    new ShadowedDirectionalLight {
                        visibleLightIndex = visibleLightIndex,
                        slopeScaleBias = light.shadowBias,
                        nearPlaneOffset = light.shadowNearPlane//近平面偏移 用于解决暴力裁剪 留有余地
                    };
                // 返回阴影强度、阴影贴图Index、法线偏移、通道号
                return new Vector4(
                    light.shadowStrength,
                    settings.directional.cascadeCount * shadowedDirectionalLightCount++,
                    light.shadowNormalBias,
                    maskChannel            // 第4分量 = Shadow Mask 通道号
                );
            }
            // ③ 没有实时投影体，但可能有 Shadow Mask
            //me06 用负数 strength 告诉 Shader："跳过实时采样，去查 Shadow Mask！"
            return new Vector4(-light.shadowStrength, 0f, 0f, maskChannel);
        }
        return new Vector4(0f, 0f, 0f, -1f);  // 完全没有阴影，通道也是 -1
    }
    
    // 渲染阴影的主入口
    public void Render () {
        
        if (shadowedDirectionalLightCount > 0) {
            RenderDirectionalShadows();
        }
        else {
            // 即使没有阴影，也要申请一个 1x1 的虚拟纹理 (避免 WebGL 2.0 报错)
            buffer.GetTemporaryRT(
                dirShadowAtlasId, 1, 1,
                32, FilterMode.Bilinear, RenderTextureFormat.Shadowmap
            );
        }
    }

    void RenderDirectionalShadows () {
        int atlasSize = (int)settings.directional.atlasSize;
        
        // 1. 申请阴影贴图
        buffer.GetTemporaryRT(
            dirShadowAtlasId, atlasSize, atlasSize,
            32, FilterMode.Bilinear, RenderTextureFormat.Shadowmap
        );
        
        // 2. 设置渲染目标为阴影贴图
        //告诉GPU接下来操作的RT是ShadowAtlas
        //RenderBufferLoadAction.DontCare意味着在将其设置为RenderTarget之后，我们不关心它的初始状态，不对其进行任何预处理
        //RenderBufferStoreAction.Store意味着完成这张RT上的所有渲染指令之后（要切换为下一个RenderTarget时），我们会将其存储到显存中为后续采样使用

        buffer.SetRenderTarget(
            dirShadowAtlasId,
            RenderBufferLoadAction.DontCare, 
            RenderBufferStoreAction.Store
        );
        
        // 3. 清空深度
        buffer.ClearRenderTarget(true, false, Color.clear);
        
        ExecuteBuffer();
        
        int tiles =shadowedDirectionalLightCount * settings.directional.cascadeCount;
        int split = tiles <= 1 ? 1 : tiles <= 4 ? 2 : 4;
        int tileSize = atlasSize / split;
        
        // 4. 渲染每一盏有阴影的光源 (暂时只有一盏)
        for (int i = 0; i < shadowedDirectionalLightCount; i++) {
            RenderDirectionalShadows(i, split, tileSize);
        }
        
        //把剔除球信息发送给GPU端
        buffer.SetGlobalInt(cascadeCountId, settings.directional.cascadeCount);
        
        buffer.SetGlobalVectorArray(
            cascadeCullingSpheresId, cascadeCullingSpheres
        );
        
        buffer.SetGlobalVectorArray(cascadeDataId, cascadeData);
        
        buffer.SetGlobalMatrixArray(dirShadowMatricesId, dirShadowMatrices);
        
        //我们传出除法 因为GPU喜欢乘法 
        float f = 1f - settings.directional.cascadeFade;
        buffer.SetGlobalVector(
            shadowDistanceFadeId, new Vector4(
                1f / settings.maxDistance, 1f / settings.distanceFade,
                1f / (1f - f * f)
            )
        );
        
        SetKeywords(
            directionalFilterKeywords, (int)settings.directional.filter - 1
        );
        SetKeywords(
            cascadeBlendKeywords, (int)settings.directional.cascadeBlend - 1
        );

        //me06没有用多次的三元表达式
        //me06b：根据 Quality Settings 决定使用哪种 Shadowmask 模式
        int shadowMaskIndex = -1;
        if (useShadowMask) {
            switch (QualitySettings.shadowmaskMode) {
                case ShadowmaskMode.Shadowmask:
                    shadowMaskIndex = 0;  // Always 模式
                    break;
                case ShadowmaskMode.DistanceShadowmask:
                    shadowMaskIndex = 1;  // Distance 模式
                    break;
            }
        }
        SetKeywords(shadowMaskKeywords, shadowMaskIndex);

        buffer.SetGlobalVector(
            shadowAtlasSizeId, new Vector4(atlasSize, 1f / atlasSize)
        );
        ExecuteBuffer();
    }
    
    
    /// <summary>
    /// 根据当前配置激活对应的方向光过滤（阴影采样）关键词。
    /// 遍历关键词列表，仅开启当前选中的模式，并关闭其他模式。
    /// </summary>
    void SetKeywords (string[] keywords, int enabledIndex) {
        //int enabledIndex = (int)settings.directional.filter - 1;
        for (int i = 0; i < keywords.Length; i++) {
            if (i == enabledIndex) {
                buffer.EnableShaderKeyword(keywords[i]);
            }
            else {
                buffer.DisableShaderKeyword(keywords[i]);
            }
        }
    }
    /// <summary>
    /// 渲染单个光源的阴影（包含该光源的所有级联）
    /// </summary>
    /// <param name="index">该光源在 shadowedDirectionalLights 数组中的索引</param>
    /// <param name="split">阴影图集（Atlas）的总分块数（单边数量）</param>
    /// <param name="tileSize">单个 Tile 的像素大小</param>
    void RenderDirectionalShadows (int index, int split, int tileSize) {
        // 1. 获取当前需要渲染阴影的光源信息
        ShadowedDirectionalLight light = shadowedDirectionalLights[index];
    
        // 2. 初始化阴影绘制设置（绑定对应的可见光索引）
        var shadowSettings = new ShadowDrawingSettings(cullingResults, light.visibleLightIndex);
    
        // 3. 读取级联配置
        int cascadeCount = settings.directional.cascadeCount;
        // 计算该光源在 Atlas 中的起始偏移量（比如第0盏灯从0开始，第1盏灯从4开始）
        int tileOffset = index * cascadeCount;
        Vector3 ratios = settings.directional.CascadeRatios;
        
        //你的过渡带越宽（Fade 越大），你就得越早停止剔除，以确保在混合时大级联里依然有东西。
        float cullingFactor =
            Mathf.Max(0f, 0.8f - settings.directional.cascadeFade);
        // 4. 遍历并渲染该光源的每一个级联 (Cascade)
        for (int i = 0; i < cascadeCount; i++) {
            // A. 计算当前级联的视角矩阵和投影矩阵
            // 参数说明：i 是当前级联索引，cascadeCount 是总级联数，ratios 是切分比例
            	cullingResults.ComputeDirectionalShadowMatricesAndCullingPrimitives(
				light.visibleLightIndex, i, cascadeCount, ratios, tileSize,
				light.nearPlaneOffset, out Matrix4x4 viewMatrix,
				out Matrix4x4 projectionMatrix, out ShadowSplitData splitData
			);

            // B. 设置级联剔除数据（确保只渲染该级联范围内的物体）
            //shadowCascadeBlendCullingFactor避免小物体重复出现在级联里面
            //值为 1.0：最激进。只要物体在小级联里，大级联就绝对不画它。
            //值为 0：最保守。所有级联都老老实实地画出自己范围内的所有物体。
            splitData.shadowCascadeBlendCullingFactor = cullingFactor;
            
            shadowSettings.splitData = splitData;
            //??我们只对最主要的灯光做级联显示 其他的不值得吗
            //@所以，即使你有 10 盏方向光，只要它们的级联比例是一样的，它们对应的级联球在世界空间里就是完全重合的。既然都一样，我们只取第一盏灯（index == 0）算出来的球就足够全场景使用了。
            //因为剔除球针对的摄像机 所以说提取一个就行 后续通用 也对哈 我们是根据摄像机位置来计算的使用哪一级阴影而不是灯光
            if (index == 0) {
                SetCascadeData(i, splitData.cullingSphere, tileSize);
            }
            
            
            // C. 计算该级联在 Atlas 中的具体 Tile 索引
            int tileIndex = tileOffset + i;
        
            // D. 转换并存储采样矩阵
            // SetTileViewport 会同时设置 GPU 的 Viewport 区域
            //级联矩阵也有了
            dirShadowMatrices[tileIndex] = ConvertToAtlasMatrix(
                projectionMatrix * viewMatrix,
                SetTileViewport(tileIndex, split, tileSize), 
                split
            );

            // E. 设置渲染状态并立即执行一次 Buffer，确保 Viewport 生效
            buffer.SetViewProjectionMatrices(viewMatrix, projectionMatrix);
            
            
            buffer.SetGlobalDepthBias(0f, light.slopeScaleBias);
            ExecuteBuffer();
            // F. 渲染投射阴影的物体 (Shadow Casters) 到当前 Tile
            //使用context.DrawShadows来渲染阴影贴图，其需要传入一个shadowSettings
            //!!我们任在 申请的阴影贴图上画阴影 
            context.DrawShadows(ref shadowSettings);
            buffer.SetGlobalDepthBias(0f, 0f);
        }
    }
    
    void SetCascadeData (int index, Vector4 cullingSphere, float tileSize) {
        // 1. 计算基础纹素大小 (Texel Size)
        // 公式：剔除球直径 / 阴影贴图的分辨率
        // 结果代表世界空间中，阴影贴图的一个像素究竟对应多少米
        float texelSize = 2f * cullingSphere.w / tileSize;

        // 2. 根据 PCF 滤波模式计算滤波半径 (Filter Size)
        // settings.directional.filter 枚举对应：PCF2x2=0, PCF3x3=1, PCF5x5=2, PCF7x7=3
        // 滤波越大，采样范围越广，产生的深度误差（痤疮）就越大，因此需要更大的偏移补偿
        float filterSize = texelSize * ((float)settings.directional.filter + 1f);

        // 3. 预防 PCF 越界：缩小剔除球的有效半径
        // 因为 PCF 会采样当前像素周围的点，如果当前像素在球体边缘，采样点可能会跑到球外导致阴影闪烁
        // 我们在计算半径平方前，先减去 filterSize，作为“安全内衬”
        cullingSphere.w -= filterSize;
    
        // 存储球体位置及其半径的平方（Shader 中使用距离平方比较，效率更高）
        float sphereRadiusSquared = cullingSphere.w * cullingSphere.w;
        cascadeCullingSpheres[index] = new Vector4(
            cullingSphere.x, cullingSphere.y, cullingSphere.z, sphereRadiusSquared
        );

        // 4. 计算法线偏移 (Normal Bias)
        // 为什么要乘以 1.4142136f (√2)？
        // 因为法线偏移可能会在对角线方向上产生最大的像素跨度误差
        // 偏移量 = 滤波半径 * √2
        float normalBias = filterSize * 1.4142136f;

        // 5. 存储级联数据传给 Shader
        // x: 半径平方的倒数，用于 Shader 内部快速进行距离比对
        // y: 最终计算出的法线偏移量
        cascadeData[index] = new Vector4(
            1f / sphereRadiusSquared,
            normalBias
        );
    }
    
    // 清理：释放临时纹理
    public void Cleanup () {
        buffer.ReleaseTemporaryRT(dirShadowAtlasId);
        ExecuteBuffer();
    }
    
    void ExecuteBuffer () {
        context.ExecuteCommandBuffer(buffer);
        buffer.Clear();
    }
    
    
    /// <summary>
    /// 设置当前要渲染的Tile区域
    /// </summary>
    /// <param name="index">Tile索引</param>
    /// <param name="split">Tile一个方向上的总数</param>
    /// <param name="tileSize">一个Tile的宽度（高度）</param>
    /// <returns>对应的tile的id 比如(0,1)</returns>
    Vector2 SetTileViewport(int index, int split, float tileSize)
    {
        Vector2 offset = new Vector2(index % split, index / split);
        // 4. 设置视口
        //me 只要把 这个会了就可以自由管理图集了 一个贴图上存放多个贴图
        //buffer.SetViewport(new Rect(x, y, tileWidth, tileHeight));
        buffer.SetViewport(new Rect(offset.x * tileSize, offset.y * tileSize,tileSize,tileSize));
        return offset;
    }
    
    /// <summary>
    /// 这个矩阵的作用是：把【世界空间坐标】转换到【阴影贴图的采样坐标】。
    /// 然后直接用xy 就可以判断z在不在被遮挡
    /// </summary>
    /// <param name="m"></param>
    /// <param name="offset"></param>
    /// <param name="split"></param>
    /// <returns></returns>
    Matrix4x4 ConvertToAtlasMatrix(Matrix4x4 m, Vector2 offset, int split)
    {
        //如果使用反向Z缓冲区，为Z取反
        if (SystemInfo.usesReversedZBuffer)
        {
            m.m20 = -m.m20;
            m.m21 = -m.m21;
            m.m22 = -m.m22;
            m.m23 = -m.m23;
        }
        //光源裁剪空间坐标范围为[-1,1]，而纹理坐标和深度都是[0,1]，因此，我们将裁剪空间坐标转化到[0,1]内
        //然后将[0,1]下的x,y偏移到光源对应的Tile上
        float scale = 1f / split;
        m.m00 = (0.5f * (m.m00 + m.m30) + offset.x * m.m30) * scale;
        m.m01 = (0.5f * (m.m01 + m.m31) + offset.x * m.m31) * scale;
        m.m02 = (0.5f * (m.m02 + m.m32) + offset.x * m.m32) * scale;
        m.m03 = (0.5f * (m.m03 + m.m33) + offset.x * m.m33) * scale;
        m.m10 = (0.5f * (m.m10 + m.m30) + offset.y * m.m30) * scale;
        m.m11 = (0.5f * (m.m11 + m.m31) + offset.y * m.m31) * scale;
        m.m12 = (0.5f * (m.m12 + m.m32) + offset.y * m.m32) * scale;
        m.m13 = (0.5f * (m.m13 + m.m33) + offset.y * m.m33) * scale;
        m.m20 = 0.5f * (m.m20 + m.m30);
        m.m21 = 0.5f * (m.m21 + m.m31);
        m.m22 = 0.5f * (m.m22 + m.m32);
        m.m23 = 0.5f * (m.m23 + m.m33);
        return m;
    }
}