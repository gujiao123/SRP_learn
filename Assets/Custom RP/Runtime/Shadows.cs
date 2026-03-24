using UnityEngine;
using UnityEngine.Rendering;



/// <summary>
/// 现在shadow完全属于light.cs 被调用
/// </summary>
public class Shadows
{

    const string bufferName = "Shadows";
    CommandBuffer buffer = new CommandBuffer { name = bufferName };

    ScriptableRenderContext context;
    CullingResults cullingResults;
    ShadowSettings settings;

    // me10: 点/聚光灯最多16盏实时阴影
    const int maxShadowedDirectionalLightCount = 4, maxShadowedOtherLightCount = 16, maxCascades = 4;
    static string[] directionalFilterKeywords = {
        "_DIRECTIONAL_PCF3",
        "_DIRECTIONAL_PCF5",
        "_DIRECTIONAL_PCF7",
    };

    // me10: 点/聚光灯阴影过滤关键词
    static string[] otherFilterKeywords = {
        "_OTHER_PCF3",
        "_OTHER_PCF5",
        "_OTHER_PCF7",
    };

    static string[] cascadeBlendKeywords = {
        "_CASCADE_BLEND_SOFT",
        "_CASCADE_BLEND_DITHER"
    };

    // 存储哪些光源需要阴影 保存下标
    struct ShadowedDirectionalLight
    {
        public int visibleLightIndex;
        public float slopeScaleBias;
        public float nearPlaneOffset;
    }
    ShadowedDirectionalLight[] shadowedDirectionalLights =
        new ShadowedDirectionalLight[maxShadowedDirectionalLightCount];

    // me10: 点/聚光灯阴影数据
    struct ShadowedOtherLight
    {
        public int visibleLightIndex;
        public float slopeScaleBias;
        public float normalBias;
        public bool isPoint; // me10: 是否是点光源（占6个tile）
    }
    ShadowedOtherLight[] shadowedOtherLights =
        new ShadowedOtherLight[maxShadowedOtherLightCount];

    int shadowedDirectionalLightCount;
    int shadowedOtherLightCount; // me10

    // Shader 属性 ID
    private static int
        dirShadowAtlasId = Shader.PropertyToID("_DirectionalShadowAtlas"),
        dirShadowMatricesId = Shader.PropertyToID("_DirectionalShadowMatrices"),
        otherShadowAtlasId = Shader.PropertyToID("_OtherShadowAtlas"),
        otherShadowMatricesId = Shader.PropertyToID("_OtherShadowMatrices"),
        otherShadowTilesId = Shader.PropertyToID("_OtherShadowTiles"), // me10: tile边界+法线偏移
                                                                       // me10: 控制是否启用 Shadow Pancaking（透视投影必须关闭）
        shadowPancakingId = Shader.PropertyToID("_ShadowPancaking"),
        cascadeCountId = Shader.PropertyToID("_CascadeCount"),
        cascadeCullingSpheresId = Shader.PropertyToID("_CascadeCullingSpheres"),
        cascadeDataId = Shader.PropertyToID("_CascadeData"),
        shadowAtlasSizeId = Shader.PropertyToID("_ShadowAtlasSize"),
        shadowDistanceFadeId = Shader.PropertyToID("_ShadowDistanceFade");


    static Vector4[]
        cascadeCullingSpheres = new Vector4[maxCascades],
        cascadeData = new Vector4[maxCascades],
        otherShadowTiles = new Vector4[maxShadowedOtherLightCount]; // me10: tile边界+法线偏移

    static Matrix4x4[]
        dirShadowMatrices = new Matrix4x4[maxShadowedDirectionalLightCount * maxCascades],
        // me10
        otherShadowMatrices = new Matrix4x4[maxShadowedOtherLightCount];

    // me10: 用于在 Render() 统一设置 Atlas 尺寸（XY=方向光, ZW=其他光）
    Vector4 atlasSizes;

    public void Setup(
        ScriptableRenderContext context,
        CullingResults cullingResults,
        ShadowSettings settings
    )
    {
        this.context = context;
        this.cullingResults = cullingResults;
        this.settings = settings;

        shadowedDirectionalLightCount = 0; // 重置计数
        shadowedOtherLightCount = 0;       // me10: 重置其他光源计数！
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
    public Vector4 ReserveDirectionalShadows(Light light, int visibleLightIndex)
    {

        float maskChannel = -1;  // -1 = 没有 Shadow Mask（不读任何通道）
        // 检查是否有空位，且光源真的需要阴影
        //!!这个就需要 在 light设置里面选择 阴影类型
        if (shadowedDirectionalLightCount < maxShadowedDirectionalLightCount &&
            light.shadows != LightShadows.None &&
            light.shadowStrength > 0f
        )
        {


            //me06 检查是否需要shadowmask 根据这个光是否是Mixed类型而且mixedLightingMode是Shadowmask
            //?什么意思 这个shadowmask不是在render里面设置的吗 怎么跟light扯上关系了
            // ① 先检查 Shadow Mask（移到这里！）
            LightBakingOutput lightBaking = light.bakingOutput;
            if (lightBaking.lightmapBakeType == LightmapBakeType.Mixed &&
                lightBaking.mixedLightingMode == MixedLightingMode.Shadowmask)
            {
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

            if (cullingResults.GetShadowCasterBounds(visibleLightIndex, out Bounds b))
            {
                // 存储光源索引
                shadowedDirectionalLights[shadowedDirectionalLightCount] =
                    new ShadowedDirectionalLight
                    {
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

    // me10: 重写 ReserveOtherShadows，支持实时阴影槽位分配（不只是 Shadow Mask）
    //me10每帧调用一次（每盏点光源/聚光灯调用一次）
    //x = shadowStrength（阴影强度）
    //y = tileIndex（在 Atlas 上占哪个格子）
    //z = 保留（点光源标记，第三节再用）
    //w = maskChannel（烘焙 Shadow Mask 通道号）
    public Vector4 ReserveOtherShadows(Light light, int visibleLightIndex)
    {
        // 没有阴影直接跳过
        if (light.shadows == LightShadows.None || light.shadowStrength <= 0f)
        {
            return new Vector4(0f, 0f, 0f, -1f);
        }

        // 检查 Shadow Mask 通道
        float maskChannel = -1f;
        LightBakingOutput lightBaking = light.bakingOutput;
        if (lightBaking.lightmapBakeType == LightmapBakeType.Mixed &&
            lightBaking.mixedLightingMode == MixedLightingMode.Shadowmask)
        {
            useShadowMask = true;
            maskChannel = lightBaking.occlusionMaskChannel;
        }

        // me10: 点光源占6个tile，聚光灯占1个
        bool isPoint = light.type == LightType.Point;
        int newLightCount = shadowedOtherLightCount + (isPoint ? 6 : 1);

        // me10: 超过上限 或 没有影子投射体 → 返回负强度（只用烘焙）
        if (newLightCount > maxShadowedOtherLightCount ||
            !cullingResults.GetShadowCasterBounds(visibleLightIndex, out Bounds b))
        {
            return new Vector4(-light.shadowStrength, 0f, 0f, maskChannel);
        }

        // me10: 将当前灯存入结构体
        shadowedOtherLights[shadowedOtherLightCount] = new ShadowedOtherLight
        {
            visibleLightIndex = visibleLightIndex,
            slopeScaleBias = light.shadowBias,
            normalBias = light.shadowNormalBias,
            isPoint = isPoint
        };

        Vector4 data = new Vector4(
            light.shadowStrength,         // x: 实时阴影强度（正数！）
            shadowedOtherLightCount,      // y: 当前 tileIndex
            isPoint ? 1f : 0f,            // z: 点光源标记（shader用于选cubemap面）
            maskChannel                   // w: Shadow Mask 通道
        );
        shadowedOtherLightCount = newLightCount; // 点光源+6，聚光灯+1
        return data;
    }

    // me10: 渲染阴影的主入口（统一设置级联数、距离衰减、Atlas 尺寸）
    public void Render()
    {
        if (shadowedDirectionalLightCount > 0)
        {
            RenderDirectionalShadows();
        }
        else
        {
            buffer.GetTemporaryRT(
                dirShadowAtlasId, 1, 1,
                32, FilterMode.Bilinear, RenderTextureFormat.Shadowmap
            );
        }

        // me10: 点/聚光灯 Atlas
        if (shadowedOtherLightCount > 0)
        {
            RenderOtherShadows();
        }
        else
        {
            // 没有其他阴影时，复用方向光 Atlas 作为占位（省显存）
            buffer.SetGlobalTexture(otherShadowAtlasId, dirShadowAtlasId);
        }

        // me10: 把级联数和距离衰减移到这里统一设置
        // 这样即使没有方向光阴影，也能正确设置 global strength（不会误判为 0）
        buffer.SetGlobalInt(
            cascadeCountId,
            shadowedDirectionalLightCount > 0 ? settings.directional.cascadeCount : 0
        );
        float f = 1f - settings.directional.cascadeFade;
        buffer.SetGlobalVector(
            shadowDistanceFadeId, new Vector4(
                1f / settings.maxDistance, 1f / settings.distanceFade,
                1f / (1f - f * f)
            )
        );

        // me10: 统一设置 Atlas 尺寸（XY=方向光, ZW=其他光）
        buffer.SetGlobalVector(shadowAtlasSizeId, atlasSizes);

        // me06: 设置 Shadow Mask 关键词
        int shadowMaskIndex = -1;
        if (useShadowMask)
        {
            // 遍历所有可见光，查找混合光的模式
            // 简单写法：如果有 ShadowMask 就开启对应关键词
            shadowMaskIndex = QualitySettings.shadowmaskMode == ShadowmaskMode.Shadowmask ? 0 : 1;
        }
        SetKeywords(shadowMaskKeywords, shadowMaskIndex);

        buffer.EndSample(bufferName);
        ExecuteBuffer();
    }

    // 方向光 Atlas 渲染
    void RenderDirectionalShadows()
    {
        int atlasSize = (int)settings.directional.atlasSize;
        atlasSizes.x = atlasSize;
        atlasSizes.y = 1f / atlasSize;

        // 1. 申请阴影贴图
        buffer.GetTemporaryRT(
            dirShadowAtlasId, atlasSize, atlasSize,
            32, FilterMode.Bilinear, RenderTextureFormat.Shadowmap
        );

        // 2. 设置渲染目标为阴影贴图
        buffer.SetRenderTarget(
            dirShadowAtlasId,
            RenderBufferLoadAction.DontCare,
            RenderBufferStoreAction.Store
        );

        // 3. 清空深度
        buffer.ClearRenderTarget(true, false, Color.clear);
        // me10: 开启 Pancaking（正交投影才需要）
        buffer.SetGlobalFloat(shadowPancakingId, 1f);
        buffer.BeginSample(bufferName);
        ExecuteBuffer();

        int tiles = shadowedDirectionalLightCount * settings.directional.cascadeCount;
        int split = tiles <= 1 ? 1 : tiles <= 4 ? 2 : 4;
        int tileSize = atlasSize / split;

        // 4. 渲染每一盏有阴影的光源
        for (int i = 0; i < shadowedDirectionalLightCount; i++)
        {
            RenderDirectionalShadows(i, split, tileSize);
        }

        //把剔除球信息发送给GPU端
        buffer.SetGlobalVectorArray(
            cascadeCullingSpheresId, cascadeCullingSpheres
        );
        buffer.SetGlobalVectorArray(cascadeDataId, cascadeData);
        buffer.SetGlobalMatrixArray(dirShadowMatricesId, dirShadowMatrices);
        SetKeywords(
            directionalFilterKeywords, (int)settings.directional.filter - 1
        );
        SetKeywords(
            cascadeBlendKeywords, (int)settings.directional.cascadeBlend - 1
        );

        buffer.EndSample(bufferName);
        ExecuteBuffer();
    }

    // me10: 点/聚光灯 Atlas 渲染主函数
    void RenderOtherShadows()
    {
        int atlasSize = (int)settings.other.atlasSize;
        atlasSizes.z = atlasSize;
        atlasSizes.w = 1f / atlasSize;

        //me10 临时申请一张图来渲染实时的阴影贴图
        buffer.GetTemporaryRT(
            otherShadowAtlasId, atlasSize, atlasSize,
            32, FilterMode.Bilinear, RenderTextureFormat.Shadowmap
        );
        buffer.SetRenderTarget(
            otherShadowAtlasId,
            RenderBufferLoadAction.DontCare,
            RenderBufferStoreAction.Store
        );
        // me10: Pancaking 关闭（聚光灯 = 透视投影，开启会将灯后的阴影投射体偏移出错）
        buffer.SetGlobalFloat(shadowPancakingId, 0f);
        buffer.ClearRenderTarget(true, false, Color.clear);
        buffer.BeginSample(bufferName);
        ExecuteBuffer();

        // tiles 决定 split
        int tiles = shadowedOtherLightCount;
        int split = tiles <= 1 ? 1 : tiles <= 4 ? 2 : 4;
        int tileSize = atlasSize / split;

        // me10: 点光源占6个tile，聚光灯占1个，按类型分派
        for (int i = 0; i < shadowedOtherLightCount;)
        {
            if (shadowedOtherLights[i].isPoint)
            {
                RenderPointShadows(i, split, tileSize);
                i += 6;
            }
            else
            {
                RenderSpotShadows(i, split, tileSize);
                i += 1;
            }
        }

        buffer.SetGlobalMatrixArray(otherShadowMatricesId, otherShadowMatrices);
        buffer.SetGlobalVectorArray(otherShadowTilesId, otherShadowTiles); // me10: 发送tile数据
        SetKeywords(otherFilterKeywords, (int)settings.other.filter - 1);

        buffer.EndSample(bufferName);
        ExecuteBuffer();
    }

    // me10: 设置 tile 边界数据（用于 shader 端裁剪采样，防止越界）
    void SetOtherTileData(int index, Vector2 offset, float scale, float bias)
    {
        // 半个像素的 UV 大小（1/atlasSize * 0.5）
        // 为什么要这个？因为 PCF 滤波会在周围采样几个像素
        // 不留 border 的话，tile 边缘的采样会读到隔壁 tile 的深度
        float border = atlasSizes.w * 0.5f;
        Vector4 data;

        // tile 在 UV 空间的左下角 + 半像素内缩
        data.x = offset.x * scale + border;   // 例：tile 5 → offset=(1,1), scale=0.25
        data.y = offset.y * scale + border;   //     → x=0.25+border, y=0.25+border

        // tile 的有效宽度（减两个 border：左边一个、右边一个）
        data.z = scale - border - border;     // 例：0.25 - 2*border

        // 法线偏移系数（Shader 用来缩放 normal bias）
        data.w = bias;
        otherShadowTiles[index] = data;
    }

    // me10: 渲染聚光灯阴影（透视投影）
    void RenderSpotShadows(int index, int split, int tileSize)
    {
        ShadowedOtherLight light = shadowedOtherLights[index];
        // me10: Unity 2022 必须指定 BatchCullingProjectionType.Perspective

        var shadowSettings = new ShadowDrawingSettings(
            cullingResults, light.visibleLightIndex,
            BatchCullingProjectionType.Perspective
        );
        // me14: 开启渲染层掩码测试
        shadowSettings.useRenderingLayerMaskTest = true;
        cullingResults.ComputeSpotShadowMatricesAndCullingPrimitives(
            light.visibleLightIndex,
            out Matrix4x4 viewMatrix,
            out Matrix4x4 projectionMatrix,
            out ShadowSplitData splitData
        );
        shadowSettings.splitData = splitData;
        // 计算法线偏移（透视投影下 texelSize 随距离变化，用 projMatrix.m00 取 scale）

        //me10对于聚光灯我们需要知道 对应阴影贴图上的一个像素在世界空间多大,但是尤其spot可以调节所以这个要跟本身fov有关系
        //在距离1处，tile覆盖的世界宽度 = 2 / projectionMatrix.m00
        //除以 tileSize（像素数）就得到每个像素的世界大小： 不过是距离为1的平面上的大小
        float texelSize = 2f / (tileSize * projectionMatrix.m00);
        float filterSize = texelSize * ((float)settings.other.filter + 1f);
        //me10normalBias（用户设的强度）× filterSize（几个像素宽）× √2（对角线最远距离）
        float bias = light.normalBias * filterSize * 1.4142136f;
        Vector2 offset = SetTileViewport(index, split, tileSize);
        float tileScale = 1f / split;
        SetOtherTileData(index, offset, tileScale, bias);
        otherShadowMatrices[index] = ConvertToAtlasMatrix(
            projectionMatrix * viewMatrix, offset, tileScale
        );
        buffer.SetViewProjectionMatrices(viewMatrix, projectionMatrix);
        buffer.SetGlobalDepthBias(0f, light.slopeScaleBias);
        ExecuteBuffer();
        context.DrawShadows(ref shadowSettings);
        buffer.SetGlobalDepthBias(0f, 0f);
    }

    // me10: 渲染点光源阴影（6个cubemap面）
    void RenderPointShadows(int index, int split, int tileSize)
    {
        ShadowedOtherLight light = shadowedOtherLights[index];

        // ① texelSize 不用除 projectionMatrix.m00
        // 因为点光源 FOV 固定 90°，tan(45°)=1，投影缩放不变
        // 在距离1处，tile覆盖的世界空间大小就是 2（-1到+1）
        //me10这个是透视投影
        var shadowSettings = new ShadowDrawingSettings(
            cullingResults, light.visibleLightIndex,
            BatchCullingProjectionType.Perspective
        );
        // me14: 开启渲染层掩码测试
        shadowSettings.useRenderingLayerMaskTest = true;
        // 点光源 FOV 固定90°，texelSize = 2 / tileSize
        float texelSize = 2f / tileSize;
        float filterSize = texelSize * ((float)settings.other.filter + 1f);
        float bias = light.normalBias * filterSize * 1.4142136f;
        float tileScale = 1f / split;
        // me10: FOV bias，扩大渲染范围避免cubemap面边缘不连续

        // ② FOV Bias —— 为什么要扩大 FOV？
        // 想象 cubemap 的6个面拼在一起，面与面之间有接缝
        // 如果刚好 90° 渲染，接缝处的法线偏移 + PCF 滤波
        // 会让采样坐标跑到 tile 外面 → 出现黑线伪影
        // 解决：把 FOV 稍微扩大一点（比如 90° → 91°）
        // 这样每个面多渲染一点边缘，接缝就被覆盖了
        float fovBias =
            Mathf.Atan(1f + bias + filterSize) * Mathf.Rad2Deg * 2f - 90f;

        // 分解：
        //   1f = tan(45°)，也就是标准90° FOV 下的半角正切
        //   + bias + filterSize = 在标准基础上扩大这么多
        //   Atan(...) = 新的半角（弧度）
        //   * Rad2Deg * 2 = 转成完整 FOV 角度
        //   - 90 = 减去原始 90°，得到需要增加的量

        // ③ 渲染6个面
        for (int i = 0; i < 6; i++)
        {
            cullingResults.ComputePointShadowMatricesAndCullingPrimitives(
                light.visibleLightIndex, (CubemapFace)i, fovBias,
                out Matrix4x4 viewMatrix,
                out Matrix4x4 projectionMatrix,
                out ShadowSplitData splitData
            );
            // me10: 翻转 view 矩阵第二行，撤销 Unity 的上下翻转
            // （Unity 渲染点光源阴影时会翻转三角形绕序，导致渲染背面）
            // ④ 翻转 view 矩阵第二行
            // Unity 内部渲染点光源阴影时，故意把图像上下翻转
            // 翻转 → 三角形绕序反了 → GPU 画背面（Back Face）不画正面
            // 好处：减少 shadow acne（因为背面离光源更远，深度偏差方向对）
            // 坏处：光会"穿透"薄物体（因为正面没画进深度图）
            // 
            // 我们再翻转一次，等于翻转了两次 = 恢复正常
            // 只改第二行（Y轴），因为第一个分量 m10 恒为0
            viewMatrix.m11 = -viewMatrix.m11;
            viewMatrix.m12 = -viewMatrix.m12;
            viewMatrix.m13 = -viewMatrix.m13;
            shadowSettings.splitData = splitData;
            int tileIndex = index + i;
            Vector2 offset = SetTileViewport(tileIndex, split, tileSize);
            SetOtherTileData(tileIndex, offset, tileScale, bias);
            otherShadowMatrices[tileIndex] = ConvertToAtlasMatrix(
                projectionMatrix * viewMatrix, offset, tileScale
            );
            buffer.SetViewProjectionMatrices(viewMatrix, projectionMatrix);
            buffer.SetGlobalDepthBias(0f, light.slopeScaleBias);
            ExecuteBuffer();
            context.DrawShadows(ref shadowSettings);
            buffer.SetGlobalDepthBias(0f, 0f);
        }
    }

    // me10: 释放 Atlas（只在有的时候才 Release OtherAtlas）
    public void Cleanup()
    {
        buffer.ReleaseTemporaryRT(dirShadowAtlasId);
        if (shadowedOtherLightCount > 0)
        {
            buffer.ReleaseTemporaryRT(otherShadowAtlasId);
        }
        ExecuteBuffer();
    }


    /// <summary>
    /// 根据当前配置激活对应的方向光过滤（阴影采样）关键词。
    /// 遍历关键词列表，仅开启当前选中的模式，并关闭其他模式。
    /// </summary>
    void SetKeywords(string[] keywords, int enabledIndex)
    {
        for (int i = 0; i < keywords.Length; i++)
        {
            if (i == enabledIndex)
            {
                buffer.EnableShaderKeyword(keywords[i]);
            }
            else
            {
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
    void RenderDirectionalShadows(int index, int split, int tileSize)
    {
        // 1. 获取当前需要渲染阴影的光源信息
        ShadowedDirectionalLight light = shadowedDirectionalLights[index];

        // 2. 初始化阴影绘制设置（绑定对应的可见光索引）
        var shadowSettings = new ShadowDrawingSettings(cullingResults, light.visibleLightIndex);
        // me14: 开启渲染层掩码测试
        shadowSettings.useRenderingLayerMaskTest = true;
        // 3. 读取级联配置
        int cascadeCount = settings.directional.cascadeCount;
        // 计算该光源在 Atlas 中的起始偏移量（比如第0盏灯从0开始，第1盏灯从4开始）
        int tileOffset = index * cascadeCount;
        Vector3 ratios = settings.directional.CascadeRatios;

        //你的过渡带越宽（Fade 越大），你就得越早停止剔除，以确保在混合时大级联里依然有东西。
        float cullingFactor =
            Mathf.Max(0f, 0.8f - settings.directional.cascadeFade);

        // me10: 提前算好 tileScale，传给 ConvertToAtlasMatrix
        float tileScale = 1f / split;

        // 4. 遍历并渲染该光源的每一个级联 (Cascade)
        for (int i = 0; i < cascadeCount; i++)
        {
            // A. 计算当前级联的视角矩阵和投影矩阵
            cullingResults.ComputeDirectionalShadowMatricesAndCullingPrimitives(
                light.visibleLightIndex, i, cascadeCount, ratios, tileSize,
                light.nearPlaneOffset, out Matrix4x4 viewMatrix,
                out Matrix4x4 projectionMatrix, out ShadowSplitData splitData
            );

            // B. 设置级联剔除数据
            splitData.shadowCascadeBlendCullingFactor = cullingFactor;

            shadowSettings.splitData = splitData;
            if (index == 0)
            {
                SetCascadeData(i, splitData.cullingSphere, tileSize);
            }

            // C. 计算该级联在 Atlas 中的具体 Tile 索引
            int tileIndex = tileOffset + i;

            // D. 转换并存储采样矩阵
            dirShadowMatrices[tileIndex] = ConvertToAtlasMatrix(
                projectionMatrix * viewMatrix,
                SetTileViewport(tileIndex, split, tileSize),
                tileScale
            );

            // E. 设置渲染状态并立即执行一次 Buffer，确保 Viewport 生效
            buffer.SetViewProjectionMatrices(viewMatrix, projectionMatrix);
            buffer.SetGlobalDepthBias(0f, light.slopeScaleBias);
            ExecuteBuffer();
            // F. 渲染投射阴影的物体 (Shadow Casters) 到当前 Tile
            context.DrawShadows(ref shadowSettings);
            buffer.SetGlobalDepthBias(0f, 0f);
        }
    }

    void SetCascadeData(int index, Vector4 cullingSphere, float tileSize)
    {
        float texelSize = 2f * cullingSphere.w / tileSize;
        float filterSize = texelSize * ((float)settings.directional.filter + 1f);
        cullingSphere.w -= filterSize;
        float sphereRadiusSquared = cullingSphere.w * cullingSphere.w;
        cascadeCullingSpheres[index] = new Vector4(
            cullingSphere.x, cullingSphere.y, cullingSphere.z, sphereRadiusSquared
        );
        float normalBias = filterSize * 1.4142136f;
        cascadeData[index] = new Vector4(
            1f / sphereRadiusSquared,
            normalBias
        );
    }

    void ExecuteBuffer()
    {
        context.ExecuteCommandBuffer(buffer);
        buffer.Clear();
    }


    /// <summary>
    /// 设置当前要渲染的Tile区域
    /// </summary>
    Vector2 SetTileViewport(int index, int split, float tileSize)
    {
        Vector2 offset = new Vector2(index % split, index / split);
        buffer.SetViewport(new Rect(offset.x * tileSize, offset.y * tileSize, tileSize, tileSize));
        return offset;
    }

    /// <summary>
    /// 这个矩阵的作用是：把【世界空间坐标】转换到【阴影贴图的采样坐标】。
    /// me10: 参数从 int split 改成 float scale（= 1f / split），避免重复除法
    /// </summary>
    Matrix4x4 ConvertToAtlasMatrix(Matrix4x4 m, Vector2 offset, float scale)
    {
        //如果使用反向Z缓冲区，为Z取反
        if (SystemInfo.usesReversedZBuffer)
        {
            m.m20 = -m.m20;
            m.m21 = -m.m21;
            m.m22 = -m.m22;
            m.m23 = -m.m23;
        }
        //光源裁剪空间坐标范围为[-1,1]，而纹理坐标和深度都是[0,1]
        //然后将[0,1]下的x,y偏移到光源对应的Tile上
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