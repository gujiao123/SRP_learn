using UnityEngine;
using UnityEngine.Rendering;
using static PostFXSettings; // me13: 省去重复写 PostFXSettings.xxx

// me11: 后处理栈 — 负责执行所有后处理效果
// 类似 Lighting、Shadows 的结构：Setup → Render
public partial class PostFXStack
{

    const string bufferName = "Post FX";

    CommandBuffer buffer = new CommandBuffer
    {
        name = bufferName
    };

    ScriptableRenderContext context;
    Camera camera;
    PostFXSettings settings;

    // me11+13: Pass 枚举（和 shader 中的 Pass 顺序一一对应）
    enum Pass
    {
        BloomAdd,
        BloomHorizontal,
        BloomPrefilter,
        BloomPrefilterFireflies,
        BloomScatter,
        BloomScatterFinal,
        BloomVertical,
        Copy,
        // me13: ToneMapping* 改名为 ColorGrading*，每个 Pass 都含 ColorGrade + ToneMap
        ColorGradingNone,     // 有ColorGrade，无ToneMap
        ColorGradingACES,
        ColorGradingNeutral,
        ColorGradingReinhard,
        Final,                // me13: 把 LUT 应用到原图并写屏幕
    }

    // me11: Shader 属性 ID
    int
        bloomBucibicUpsamplingId = Shader.PropertyToID("_BloomBicubicUpsampling"),
        bloomIntensityId = Shader.PropertyToID("_BloomIntensity"),
        bloomPrefilterId = Shader.PropertyToID("_BloomPrefilter"),
        bloomResultId = Shader.PropertyToID("_BloomResult"),
        bloomThresholdId = Shader.PropertyToID("_BloomThreshold"),
        fxSourceId = Shader.PropertyToID("_PostFXSource"),
        fxSource2Id = Shader.PropertyToID("_PostFXSource2"),
        // me13: 颜色分级相关
        colorAdjustmentsId = Shader.PropertyToID("_ColorAdjustments"),
        colorFilterId = Shader.PropertyToID("_ColorFilter"),
        whiteBalanceId = Shader.PropertyToID("_WhiteBalance"),
        splitToningShadowsId = Shader.PropertyToID("_SplitToningShadows"),
        splitToningHighlightsId = Shader.PropertyToID("_SplitToningHighlights"),
        channelMixerRedId = Shader.PropertyToID("_ChannelMixerRed"),
        channelMixerGreenId = Shader.PropertyToID("_ChannelMixerGreen"),
        channelMixerBlueId = Shader.PropertyToID("_ChannelMixerBlue"),
        smhShadowsId = Shader.PropertyToID("_SMHShadows"),
        smhMidtonesId = Shader.PropertyToID("_SMHMidtones"),
        smhHighlightsId = Shader.PropertyToID("_SMHHighlights"),
        smhRangeId = Shader.PropertyToID("_SMHRange"),
        colorGradingLUTId = Shader.PropertyToID("_ColorGradingLUT"),
        colorGradingLUTParametersId = Shader.PropertyToID("_ColorGradingLUTParameters"),
        colorGradingLUTInLogId = Shader.PropertyToID("_ColorGradingLUTInLogC"),
        // me14: 新增混合模式属性
        finalSrcBlendId = Shader.PropertyToID("_FinalSrcBlend"),
        finalDstBlendId = Shader.PropertyToID("_FinalDstBlend");

    // me11: Bloom 金字塔纹理 ID（最多16级，每级需要2个RT：水平+竖直）
    const int maxBloomPyramidLevels = 16;
    int bloomPyramidId;

    public PostFXStack()
    {
        bloomPyramidId = Shader.PropertyToID("_BloomPyramid0");
        for (int i = 1; i < maxBloomPyramidLevels * 2; i++)
        {
            Shader.PropertyToID("_BloomPyramid" + i);
        }
    }

    bool useHDR;            // me12
    int colorLUTResolution; // me13
    CameraSettings.FinalBlendMode finalBlendMode;  //me14从inspector那里得到的数据
    public bool IsActive => settings != null;

    public void Setup(
        ScriptableRenderContext context, Camera camera,
        PostFXSettings settings, bool useHDR, int colorLUTResolution,
        CameraSettings.FinalBlendMode finalBlendMode  //me14 ← 新增
    )
    {
        this.finalBlendMode = finalBlendMode;  //me14 ← 新增
        this.useHDR = useHDR;
        this.colorLUTResolution = colorLUTResolution; // me13
        this.context = context;
        this.camera = camera;
        this.settings =
            camera.cameraType <= CameraType.SceneView ? settings : null;
        ApplySceneViewState();
    }


    // me11: 自定义全屏绘制（用程序化三角形代替 Blit 的四边形）
    // 3个顶点的三角形覆盖整个屏幕，比四边形少一个三角形
    //每次调用 
    //Draw()
    //就是：从一张 RT 读取 → 在 shader 里处理 → 写到另一张 RT。
    void Draw(
        RenderTargetIdentifier from, RenderTargetIdentifier to, Pass pass
    )
    {
        buffer.SetGlobalTexture(fxSourceId, from); // 告诉 shader "源图是这张"
        buffer.SetRenderTarget(
            to, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store
        );

        //告诉 GPU "画3个顶点"，GPU 就自动给这3个顶点分别传入 vertexID = 0, 1, 2。
        //没有 Mesh，没有顶点缓冲，只有一个数字进来。
        //me11 shader端 uint vertexID : SV_VertexID 接收这个参数
        buffer.DrawProcedural(
            Matrix4x4.identity, settings.Material, (int)pass,
            MeshTopology.Triangles, 3
        );
    }

    // me13: 修复拉伸的 DrawFinal（手动设置 Viewport）
    static Rect fullViewRect = new Rect(0f, 0f, 1f, 1f);
    //me13 修复了 DrawFinal 的拉伸问题 就是增加手动设置viewport ndc坐标映射到屏幕 来解决uv对应的问题
    void DrawFinal(RenderTargetIdentifier from)
    {
        // me14: 把混合模式传给 Shader
        buffer.SetGlobalFloat(finalSrcBlendId, (float)finalBlendMode.source);
        buffer.SetGlobalFloat(finalDstBlendId, (float)finalBlendMode.destination);
        buffer.SetGlobalTexture(fxSourceId, from);
        buffer.SetRenderTarget(
            BuiltinRenderTextureType.CameraTarget,
            //me14 改成（两个条件都满足才能 DontCare）：  直接在一张图上混合就是了 load就是加载上一帧的画面
            //传递这个 src 和dst的混合才有对应的像素来对吧 你不传入的哪里来呢？
            //为了全屏+叠层能正常工作
            finalBlendMode.destination == BlendMode.Zero && camera.rect == fullViewRect ?
                RenderBufferLoadAction.DontCare :
                RenderBufferLoadAction.Load,
            RenderBufferStoreAction.Store
        );
        //me13关键就是要和一开始跟camera设置的viewport对应起来 直接传入 camera.pixelRect 这样ndc坐标就会映射到对应的屏幕区域
        buffer.SetViewport(camera.pixelRect);  //me13新增的 ← 关键：恢复正确的视口！
        buffer.DrawProcedural(
            Matrix4x4.identity, settings.Material, (int)Pass.Final,
            MeshTopology.Triangles, 3
        );
    }


    // me11+12: Bloom效果（me12: 返回 bool 告知 Render 是否有结果，输出到 bloomResultId）
    bool DoBloom(int sourceId)
    {
        PostFXSettings.BloomSettings bloom = settings.Bloom;
        int width = camera.pixelWidth / 2, height = camera.pixelHeight / 2;

        // me11: 如果 Bloom 被禁用或源图太小，直接返回 false
        // me12: BeginSample 移到这里之后，Bloom 禁用时不产生空的 Sample 块
        if (
            bloom.maxIterations == 0 || bloom.intensity <= 0f ||
            height < bloom.downscaleLimit * 2 ||
            width < bloom.downscaleLimit * 2
        )
        {
            return false;
        }

        buffer.BeginSample("Bloom"); // 确认有 Bloom 才开始计时
        RenderTextureFormat format =
            useHDR ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default;

        // me11: 阈值参数预计算（减少 Shader 里的计算量）
        Vector4 threshold;

        //me11景inspector里面设置的阈值 gamma值 转换为线性空间
        //threshold.x = 线性空间下的亮度阈值
        threshold.x = Mathf.GammaToLinearSpace(bloom.threshold);
        //threshold.y = 阈值膝盖曲线
        threshold.y = threshold.x * bloom.thresholdKnee;
        //threshold.z = 膝盖区域全宽（supply给 clamp 用）
        threshold.z = 2f * threshold.y;
        //threshold.w = 0.25f / (threshold.y + 0.00001f) threshold.w = 曲线系数（shader 里做二次计算用）
        threshold.w = 0.25f / (threshold.y + 0.00001f);
        //threshold.y -= threshold.x变成了相对于阈值的偏移量，这样 shader 里用 brightness + threshold.y 就能直接平移，不用额外减 threshold.x。
        threshold.y -= threshold.x;
        buffer.SetGlobalVector(bloomThresholdId, threshold);

        // me11: 先做一次预过滤（应用阈值 + 半分辨率）
        buffer.GetTemporaryRT(bloomPrefilterId, width, height, 0, FilterMode.Bilinear, format);
        // me12: fadeFireflies 开启时用萤火虫消除 Pass
        Draw(sourceId, bloomPrefilterId,
            bloom.fadeFireflies ? Pass.BloomPrefilterFireflies : Pass.BloomPrefilter);
        width /= 2;
        height /= 2;

        // me11: 降采样循环（每级: 水平模糊 → 竖直模糊）
        int fromId = bloomPrefilterId, toId = bloomPyramidId + 1;
        int i;
        //me11 maxIterations — 控制光晕范围
        //每多一级，Bloom 的模糊范围就更大（光晕扩散更远）
        //iterations=2：  只模糊到 1/4 分辨率，光晕范围小
        //iterations=8：  模糊到 1/256 分辨率，光晕范围很大很漫
        for (i = 0; i < bloom.maxIterations; i++)
        {
            //1080p 图无限缩下去：
            //540 → 270 → 135 → 67 → 33 → 16 → 8 → 4 → 2 → 1
            //当 RT 只有 2×2 像素时：
            //横向"9点高斯"根本采不到9个不同像素
            //全是同一个像素被重复采样 → 完全没有模糊效果
            //还白白占一张 RT 的申请和释放开销
            if (height < bloom.downscaleLimit || width < bloom.downscaleLimit)
            {
                break;
            }
            //偶数位（0,2,4...）= midId = 横向模糊中间结果
            //奇数位（1,3,5...）= toId  = 纵向模糊最终结果（每级的最终产物）
            int midId = toId - 1;
            buffer.GetTemporaryRT(
                midId, width, height, 0, FilterMode.Bilinear, format
            );
            buffer.GetTemporaryRT(
                toId, width, height, 0, FilterMode.Bilinear, format
            );
            Draw(fromId, midId, Pass.BloomHorizontal);
            Draw(midId, toId, Pass.BloomVertical);
            fromId = toId;
            toId += 2;
            width /= 2;
            height /= 2;
        }

        buffer.ReleaseTemporaryRT(bloomPrefilterId);

        buffer.SetGlobalFloat(bloomBucibicUpsamplingId, bloom.bicubicUpsampling ? 1f : 0f);

        // me12: 根据模式选择 Combine Pass
        Pass combinePass, finalPass;
        float finalIntensity;
        if (bloom.mode == PostFXSettings.BloomSettings.Mode.Additive)
        {
            combinePass = finalPass = Pass.BloomAdd;
            buffer.SetGlobalFloat(bloomIntensityId, 1f);
            finalIntensity = bloom.intensity;
        }
        else
        {
            combinePass = Pass.BloomScatter;
            finalPass = Pass.BloomScatterFinal;
            buffer.SetGlobalFloat(bloomIntensityId, bloom.scatter);
            finalIntensity = Mathf.Min(bloom.intensity, 0.95f);
        }
        if (i > 1)
        {
            //me11就是释放对应横向保存的mid可以释放 
            //me11不过指针还是指向那块内存
            //!!结论：Release 不是"删除"，是"放回仓库"。Get 不是"新建"，是"从仓库借"。显存总量基本不变，变的只是谁在用这块区域。
            buffer.ReleaseTemporaryRT(fromId - 1); // 最小层的 mid（横向中间结果）用完了释放 

            //me11
            //偶数位 = mid（横向）
            //奇数位 = to（纵向）
            //toId 此时经过 -= 5 后：
            //!!toId = 102（偶数位 = 横向位置）的结果用来复用的指针指向显存大小
            //toId + 1 = 103（奇数位 = 纵向位置）← 这个！
            toId -= 5;// 把 toId 回退到倒数第二层的位置（之前 +2 跑过头了）
            for (i -= 1; i > 0; i--)//从最小层往回走，叠加每一级
            {
                //指向上面一层toId + 1 是降采样时产物里更清晰的那层（纵向结果）
                buffer.SetGlobalTexture(fxSource2Id, toId + 1);
                Draw(fromId, toId, combinePass); // me12: 用选好的 combinePass
                buffer.ReleaseTemporaryRT(fromId);// 用完，释放 最小的纵层释放
                buffer.ReleaseTemporaryRT(toId + 1);  // 上一层原内容用完，释放
                fromId = toId; // 下一轮的输入 = 刚合并的结果
                toId -= 2;// 继续往更大层走
            }
        }
        else
        {
            buffer.ReleaseTemporaryRT(bloomPyramidId);
        }

        // me12: 最终输出到 bloomResultId，不直接写屏幕（让 ToneMapping 去写）


        buffer.SetGlobalFloat(bloomIntensityId, finalIntensity);
        buffer.SetGlobalTexture(fxSource2Id, sourceId);
        buffer.GetTemporaryRT(
            bloomResultId, camera.pixelWidth, camera.pixelHeight,
            0, FilterMode.Bilinear, format
        );
        Draw(fromId, bloomResultId, finalPass);
        buffer.ReleaseTemporaryRT(fromId);
        buffer.EndSample("Bloom");
        return true;
    }

    // me13: 颜色调整参数传给 Shader
    // x=曝光(2^postExposure)  y=对比度(0~2)  z=色相(-1~1)  w=饱和度(0~2)
    void ConfigureColorAdjustments()
    {
        ColorAdjustmentsSettings colorAdjustments = settings.ColorAdjustments;
        buffer.SetGlobalVector(colorAdjustmentsId, new Vector4(
            Mathf.Pow(2f, colorAdjustments.postExposure), // stop单位→倍率
            colorAdjustments.contrast * 0.01f + 1f,       // -100~100 → 0~2
            colorAdjustments.hueShift * (1f / 360f),      // 度 → 0~1
            colorAdjustments.saturation * 0.01f + 1f      // -100~100 → 0~2
        ));
        // 颜色滤镜转到线性空间（Inspector里显示的是 gamma）
        buffer.SetGlobalColor(colorFilterId, colorAdjustments.colorFilter.linear);
    }

    // me13: 白平衡——调用 Core 库的 ColorBalanceToLMSCoeffs，在 LMS 颜色空间做矩阵乘
    void ConfigureWhiteBalance()
    {
        WhiteBalanceSettings whiteBalance = settings.WhiteBalance;
        buffer.SetGlobalVector(whiteBalanceId, ColorUtils.ColorBalanceToLMSCoeffs(
            whiteBalance.temperature, whiteBalance.tint
        ));
    }

    // me13: 分离映射——暗部/亮部的色调，balance 存在暗部颜色的 alpha 通道
    void ConfigureSplitToning()
    {
        SplitToningSettings splitToning = settings.SplitToning;
        Color splitColor = splitToning.shadows;
        splitColor.a = splitToning.balance * 0.01f;  // -100~100 → -1~1
        buffer.SetGlobalColor(splitToningShadowsId, splitColor);    // gamma 空间直接传
        buffer.SetGlobalColor(splitToningHighlightsId, splitToning.highlights);
    }

    // me13: 通道混合——三个向量就是 3×3 矩阵的三行
    void ConfigureChannelMixer()
    {
        ChannelMixerSettings channelMixer = settings.ChannelMixer;
        buffer.SetGlobalVector(channelMixerRedId, channelMixer.red);
        buffer.SetGlobalVector(channelMixerGreenId, channelMixer.green);
        buffer.SetGlobalVector(channelMixerBlueId, channelMixer.blue);
    }

    // me13: 阴影/中间调/高光——三色转线性空间，区域范围打包成一个 Vector4
    void ConfigureShadowsMidtonesHighlights()
    {
        ShadowsMidtonesHighlightsSettings smh = settings.ShadowsMidtonesHighlights;
        buffer.SetGlobalColor(smhShadowsId, smh.shadows.linear);
        buffer.SetGlobalColor(smhMidtonesId, smh.midtones.linear);
        buffer.SetGlobalColor(smhHighlightsId, smh.highlights.linear);
        buffer.SetGlobalVector(smhRangeId, new Vector4(
            smh.shadowsStart, smh.shadowsEnd,
            smh.highlightsStart, smh.highLightsEnd
        ));
    }

    // me12→13: Tone Mapping 改名为 DoColorGradingAndToneMapping，和 ColorGrade 合并
    // 流程: Configure参数 → 生成 LUT → 应用 LUT 到原图 → 写屏幕
    void DoColorGradingAndToneMapping(int sourceId)
    {
        // 把所有颜色调整参数传给 Shader
        ConfigureColorAdjustments();
        ConfigureWhiteBalance();
        ConfigureSplitToning();
        ConfigureChannelMixer();
        ConfigureShadowsMidtonesHighlights();

        // me13: 根据 ToneMapping 模式选择 Pass
        // Mode 枚举从0开始: None=0, ACES=1, Neutral=2, Reinhard=3
        ToneMappingSettings.Mode mode = settings.ToneMapping.mode;
        Pass pass = Pass.ColorGradingNone + (int)mode;

        // me13: 刻度 LUT 纹理（宽2D贴图模拟 3D LUT）
        int lutHeight = colorLUTResolution;     // 32
        int lutWidth = lutHeight * lutHeight;  // 32×32 = 1024
        buffer.GetTemporaryRT(
            colorGradingLUTId, lutWidth, lutHeight, 0,
            FilterMode.Bilinear, RenderTextureFormat.DefaultHDR
        );
        // 烘焙阶段的参数
        buffer.SetGlobalVector(colorGradingLUTParametersId, new Vector4(
            lutHeight,
            0.5f / lutWidth,
            0.5f / lutHeight,
            lutHeight / (lutHeight - 1f)
        ));
        // me13: HDR + 有ToneMap 时用 LogC 模式，扩展到 ~59 左右的亮度范围
        buffer.SetGlobalFloat(
            colorGradingLUTInLogId,
            useHDR && pass != Pass.ColorGradingNone ? 1f : 0f
        );
        // 渲染 LUT（写入 colorGradingLUTId）
        Draw(sourceId, colorGradingLUTId, pass);

        // 应用 LUT 阶段的参数（不同，只有 3 个分量有用）
        buffer.SetGlobalVector(colorGradingLUTParametersId, new Vector4(
            1f / lutWidth, 1f / lutHeight, lutHeight - 1f
        ));
        // Final Pass：把 LUT 应用到原图，写屏幕
        //Draw(sourceId, BuiltinRenderTextureType.CameraTarget, Pass.Final);
        DrawFinal(sourceId);
        buffer.ReleaseTemporaryRT(colorGradingLUTId);
    }

    // me11+12+13: 渲染后处理入口
    public void Render(int sourceId)
    {
        if (DoBloom(sourceId))
        {
            // 有 Bloom：对 bloom 结果做颜色分级
            DoColorGradingAndToneMapping(bloomResultId);
            buffer.ReleaseTemporaryRT(bloomResultId);
        }
        else
        {
            // 无 Bloom：直接对原图做颜色分级
            DoColorGradingAndToneMapping(sourceId);
        }
        context.ExecuteCommandBuffer(buffer);
        buffer.Clear();
    }

    partial void ApplySceneViewState();
}
