using UnityEngine;
using UnityEngine.Rendering;

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

    // me11: Pass 枚举（和 shader 中的 Pass 顺序一一对应）
    enum Pass
    {
        BloomHorizontal,
        BloomVertical,
        BloomCombine,
        BloomPrefilter,
        Copy
    }

    // me11: Shader 属性 ID
    int
        bloomBucibicUpsamplingId = Shader.PropertyToID("_BloomBicubicUpsampling"),//设置是否用双三次插值
        bloomIntensityId = Shader.PropertyToID("_BloomIntensity"),//设置光照强度
        bloomPrefilterId = Shader.PropertyToID("_BloomPrefilter"),//设置预过滤
        bloomThresholdId = Shader.PropertyToID("_BloomThreshold"),//设置阈值
        fxSourceId = Shader.PropertyToID("_PostFXSource"),//设置源图
        fxSource2Id = Shader.PropertyToID("_PostFXSource2");//叠加用的第二张图（Combine 时）

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

    // me11: 是否激活后处理（没设置 settings 就跳过）
    public bool IsActive => settings != null;

    // me11: 每帧初始化
    public void Setup(
        ScriptableRenderContext context, Camera camera,
        PostFXSettings settings
    )
    {
        this.context = context;
        this.camera = camera;
        // me11: 只对 Game 和 Scene 相机启用后处理
        // 反射探针、预览相机等不需要
        this.settings =
            camera.cameraType <= CameraType.SceneView ? settings : null;
        ApplySceneViewState();
    }

    // me11: 渲染后处理入口
    //me11以后后处理都在这里加
    public void Render(int sourceId)
    {
        DoBloom(sourceId); // ① 把所有 Bloom 命令录进 buffer
        context.ExecuteCommandBuffer(buffer);  // ② 把 buffer 交给 context 收集
        buffer.Clear(); // ③ 清空 buffer，准备下一帧
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

    // me11: Bloom 效果
    // 降采样金字塔：原图 → 半分辨率 → 四分之一 → ... 每级做高斯模糊
    // 升采样叠加：从最小层往回走，每级和上一级叠加
    void DoBloom(int sourceId)
    {
        buffer.BeginSample("Bloom");
        PostFXSettings.BloomSettings bloom = settings.Bloom;
        int width = camera.pixelWidth / 2, height = camera.pixelHeight / 2;

        // me11: 如果 Bloom 被禁用或源图太小，直接 Copy
        if (
            bloom.maxIterations == 0 || bloom.intensity <= 0f ||
            height < bloom.downscaleLimit * 2 ||
            width < bloom.downscaleLimit * 2
        )
        {
            Draw(sourceId, BuiltinRenderTextureType.CameraTarget, Pass.Copy);
            buffer.EndSample("Bloom");
            return;
        }

        RenderTextureFormat format = RenderTextureFormat.Default;

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
        buffer.GetTemporaryRT(
            bloomPrefilterId, width, height, 0,
            FilterMode.Bilinear, format
        );
        Draw(sourceId, bloomPrefilterId, Pass.BloomPrefilter);
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

        // me11: 升采样循环（从最小层往回走，叠加每一级）
        buffer.SetGlobalFloat(
            bloomBucibicUpsamplingId, bloom.bicubicUpsampling ? 1f : 0f
        );
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
                buffer.SetGlobalTexture(fxSource2Id, toId + 1); // source2 = 上一层（较大的）
                Draw(fromId, toId, Pass.BloomCombine);  // fromId(当前小图) → toId(上一层大小)
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

        // me11: 最后一次叠加，把 Bloom 结果和原图合并输出到屏幕
        buffer.SetGlobalFloat(bloomIntensityId, bloom.intensity);
        buffer.SetGlobalTexture(fxSource2Id, sourceId);
        Draw(
            fromId, BuiltinRenderTextureType.CameraTarget,
            Pass.BloomCombine
        );
        buffer.ReleaseTemporaryRT(fromId);
        buffer.EndSample("Bloom");
    }

    // me11: 分部方法声明（Editor 版本在 PostFXStack.Editor.cs 里实现）
    partial void ApplySceneViewState();
}
