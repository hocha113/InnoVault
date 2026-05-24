using InnoVault.Models3D.Animation;
using InnoVault.Models3D.Skinning;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace InnoVault.Models3D.Runtime
{
    /// <summary>
    /// 一个用于绘制的 3D 模型实例
    /// <br/>仅描述"这一帧/这个对象怎么画"，不包含游戏逻辑
    /// </summary>
    /// <remarks>
    /// 使用方式：
    /// <list type="bullet">
    /// <item>临时绘制：每帧构造好实例后调用 <see cref="Model3DRenderer.Submit"/> 提交一次</item>
    /// <item>持久绘制：构造好实例后调用 <see cref="Model3DRenderer.RegisterPersistent"/>，不再使用时调用 <see cref="Model3DRenderer.UnregisterPersistent"/></item>
    /// </list>
    /// 高级扩展（按需，全为可选）：
    /// <list type="bullet">
    /// <item><see cref="Effect"/> / <see cref="EffectProvider"/>：自定义 shader，使用时本实例上的 <see cref="ConfigureEffect"/> 委托负责写入所有 shader 参数</item>
    /// <item><see cref="ConfigureEffect"/>：每个 mesh group 绘制前的参数填充委托；自定义 Effect 路径上必须使用</item>
    /// <item><see cref="RenderStateOverride"/>：实例级的 Blend / Depth / Rasterizer / Sampler 覆盖；
    /// 把 Blend 设为非 Opaque 会让实例自动归入透明桶</item>
    /// <item><see cref="PreDrawInstance"/> / <see cref="PostDrawInstance"/>：实例级生命周期钩子</item>
    /// <item><see cref="PreDrawGroup"/> / <see cref="PostDrawGroup"/>：每个 mesh group 的生命周期钩子，
    /// 适合在不接管整个 Effect 的前提下修改 emissive / tint 等少量参数</item>
    /// </list>
    /// 解析优先级：实例字段 &gt; <see cref="Model3DMaterial"/> 上的同名字段 &gt; 框架默认
    /// </remarks>
    public sealed class Model3DInstance
    {
        /// <summary>
        /// 关联的模型资源
        /// </summary>
        public Vault3DModel Model { get; set; }

        /// <summary>
        /// 世界空间位置（按 Terraria 屏幕坐标系，单位为像素）
        /// <br/>对应屏幕渲染时会自动减去 <see cref="Terraria.Main.screenPosition"/>
        /// </summary>
        public Vector2 Position { get; set; }

        /// <summary>
        /// 沿 Z 轴的附加深度偏移（屏幕坐标系，向"屏幕外"为负）
        /// <br/>用于让模型相对于地面/玩家平面错位
        /// </summary>
        public float Depth { get; set; }

        /// <summary>
        /// 三轴欧拉旋转（弧度），按 X、Y、Z 顺序应用
        /// </summary>
        public Vector3 Rotation { get; set; } = Vector3.Zero;

        /// <summary>
        /// 三轴缩放
        /// </summary>
        public Vector3 Scale { get; set; } = Vector3.One;

        /// <summary>
        /// 整体颜色 tint，会与材质的漫反射颜色相乘
        /// </summary>
        public Color Tint { get; set; } = Color.White;

        /// <summary>
        /// 整体不透明度，会与 <see cref="Tint"/> 的 alpha 相乘
        /// </summary>
        public float Opacity { get; set; } = 1f;

        /// <summary>
        /// 绘制层级，决定模型与 Terraria 各阶段的遮挡关系
        /// </summary>
        public Model3DLayer Layer { get; set; } = Model3DLayer.AfterTiles;

        /// <summary>
        /// 是否启用 <see cref="Microsoft.Xna.Framework.Graphics.BasicEffect"/> 的内置定向光照
        /// <br/>关闭时使用纯色/贴图渲染
        /// </summary>
        public bool LightingEnabled { get; set; } = false;

        /// <summary>
        /// 实例级光照覆盖配置；<see langword="null"/> 时使用 <see cref="Model3DRenderer.GlobalLighting"/>
        /// <br/>仅在 <see cref="LightingEnabled"/> 为 <see langword="true"/> 时生效
        /// <br/>注意：传入的对象会被渲染器拷贝到内部 scratch 后再交给 <see cref="Model3DRenderer.ResolveLighting"/>，
        /// 因此订阅者的修改不会污染该对象，可放心在多实例间共享同一份配置
        /// </summary>
        public Model3DLightingConfig LightingOverride { get; set; }

        /// <summary>
        /// 是否在未来的阴影系统中作为投影者参与
        /// <br/><b>当前渲染管线尚未实现阴影，故此字段不被消费</b>，仅作为前向兼容预留
        /// </summary>
        public bool CastShadow { get; set; } = false;

        /// <summary>
        /// 是否在未来的阴影系统中接收阴影
        /// <br/><b>当前渲染管线尚未实现阴影，故此字段不被消费</b>，仅作为前向兼容预留
        /// </summary>
        public bool ReceiveShadow { get; set; } = true;

        /// <summary>
        /// 是否启用深度测试（多个模型相互重叠时可能需要打开）
        /// </summary>
        public bool DepthEnabled { get; set; } = true;

        /// <summary>
        /// 是否进行背面剔除，OBJ 来源不一致时建议保持 <see langword="false"/>
        /// </summary>
        public bool CullBackface { get; set; } = false;

        /// <summary>
        /// 是否强制将该实例视为半透明，即使材质 / <see cref="Tint"/> / <see cref="Opacity"/> 都满 1
        /// <br/>开启后该实例会进入透明桶：使用 alpha blend、只读深度、按距离 back-to-front 排序
        /// <br/>适合发光罩、玻璃外壳等需要从外往里看的"装饰外层"场景
        /// </summary>
        public bool ForceTransparent { get; set; } = false;

        /// <summary>
        /// 自定义排序权重（越大越后绘制），同 <see cref="Layer"/> 的实例间排序使用
        /// </summary>
        public float SortKey { get; set; }

        /// <summary>
        /// 是否参与绘制
        /// </summary>
        public bool Visible { get; set; } = true;

        //========================================================================
        // 高级扩展：Effect / RenderState / 生命周期回调
        //========================================================================

        /// <summary>
        /// 实例级自定义 <see cref="Microsoft.Xna.Framework.Graphics.Effect"/> 覆盖
        /// <br/>非 <see langword="null"/> 时，渲染器会用它而不是默认 <see cref="BasicEffect"/> 绘制该实例
        /// <br/>解析优先级：<see cref="Effect"/> &gt; <see cref="EffectProvider"/>.Resolve &gt; <see cref="Model3DMaterial.Effect"/>
        /// &gt; <see cref="Model3DMaterial.EffectProvider"/>.Resolve &gt; 渲染器自带 <see cref="BasicEffect"/>
        /// <br/><b>注意</b>：当使用自定义 Effect 时，渲染器不会自动写入光照 / Tint / Texture / Diffuse 等参数，
        /// 这些参数全部由 <see cref="ConfigureEffect"/>（或 <see cref="EffectProvider"/>.Configure）负责
        /// <br/>World / View / Projection 矩阵会被自动写入（仅当 Effect 实现 <see cref="IEffectMatrices"/>），
        /// 之后仍可被 <see cref="ConfigureEffect"/> 覆盖
        /// </summary>
        public Effect Effect { get; set; }

        /// <summary>
        /// 实例级 Effect 提供者；与 <see cref="Effect"/> 互斥，前者非空时优先使用前者
        /// <br/>适合"跨实例共享的 shader 封装"，参考 <see cref="IModel3DEffectProvider"/> 文档
        /// </summary>
        public IModel3DEffectProvider EffectProvider { get; set; }

        /// <summary>
        /// 实例级 Effect 参数配置回调；每个 mesh group 绘制前调用一次
        /// <br/>调用顺序：本回调 → <see cref="Model3DMaterial.ConfigureEffect"/>
        /// <br/>使用自定义 Effect 时<b>必须</b>由这里负责写入 World / View / Projection 等矩阵
        /// </summary>
        public Model3DConfigureEffect ConfigureEffect { get; set; }

        /// <summary>
        /// 实例级渲染状态覆盖；不为 <see langword="null"/> 的字段会覆盖材质级与桶级默认
        /// <br/>把 <see cref="Model3DRenderState.Blend"/> 设为非 <see cref="BlendState.Opaque"/> 时，
        /// 该实例会被自动归入透明桶（启用 alpha blend、只读深度、按距离排序）
        /// </summary>
        public Model3DRenderState RenderStateOverride { get; set; }

        /// <summary>
        /// 实例绘制开始前的回调；在解析 Effect / RenderState 之前调用
        /// <br/>典型用途：根据游戏逻辑动态调整 <see cref="Tint"/> / <see cref="Rotation"/> 等字段
        /// <br/>注意：回调中 <see cref="Model3DDrawContext.Group"/> 为 <see langword="null"/>
        /// </summary>
        public Model3DDrawCallback PreDrawInstance { get; set; }

        /// <summary>
        /// 实例绘制完成后的回调；在所有 group 都画完之后调用
        /// <br/>注意：回调中 <see cref="Model3DDrawContext.Group"/> 为 <see langword="null"/>
        /// </summary>
        public Model3DDrawCallback PostDrawInstance { get; set; }

        /// <summary>
        /// 每个 mesh group 绘制前的回调；在 <see cref="ConfigureEffect"/> 之后、提交几何之前调用
        /// <br/>可用于按 group 修改额外的 shader 参数
        /// </summary>
        public Model3DDrawCallback PreDrawGroup { get; set; }

        /// <summary>
        /// 每个 mesh group 绘制后的回调
        /// </summary>
        public Model3DDrawCallback PostDrawGroup { get; set; }

        /// <summary>
        /// 实例级动画播放头
        /// <br/>绑定到带骨架的 <see cref="Vault3DModel"/> 时才会生效；不为 <see langword="null"/> 时
        /// 渲染器会在每帧绘制前自动调用 <see cref="AnimationPlayer.Update(float)"/> + <see cref="AnimationPlayer.SamplePose"/>，
        /// 并把蒙皮结果写入实例自身的 scratch 缓冲
        /// <br/>多个实例共享同一模型时各自需要一个独立 Player（保存自己的播放时间）
        /// </summary>
        public AnimationPlayer Animation { get; set; }

        /// <summary>
        /// 蒙皮 scratch 缓冲
        /// <br/>渲染器使用；外部一般无需访问
        /// </summary>
        internal InstanceSkinScratch SkinScratch { get; } = new InstanceSkinScratch();

        /// <summary>
        /// 构造一个未绑定模型的空实例
        /// </summary>
        public Model3DInstance() { }

        /// <summary>
        /// 用指定模型构造实例
        /// </summary>
        public Model3DInstance(Vault3DModel model) {
            Model = model;
        }
    }
}
