using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace InnoVault.Models3D.Runtime
{
    /// <summary>
    /// 单次 3D 绘制的上下文快照
    /// <br/>由 <see cref="Model3DRenderer"/> 在每个实例 / 每个 mesh group 绘制前构造，
    /// 用于传递给 <see cref="Model3DConfigureEffect"/>、<see cref="Model3DDrawCallback"/>、
    /// <see cref="IModel3DEffectProvider"/> 等扩展点
    /// <br/>所有字段都是只读的，外部可以放心通过 <see langword="in"/> 形参以零拷贝方式接收
    /// </summary>
    public readonly struct Model3DDrawContext
    {
        /// <summary>
        /// 当前帧使用的 <see cref="Microsoft.Xna.Framework.Graphics.GraphicsDevice"/>
        /// </summary>
        public GraphicsDevice GraphicsDevice { get; }

        /// <summary>
        /// 正在绘制的 3D 实例
        /// </summary>
        public Model3DInstance Instance { get; }

        /// <summary>
        /// 正在绘制的模型资源（即 <see cref="Instance"/> 的 <see cref="Model3DInstance.Model"/>）
        /// </summary>
        public Vault3DModel Model { get; }

        /// <summary>
        /// 当前 mesh group；仅在 group 级回调中非空，实例级回调中为 <see langword="null"/>
        /// </summary>
        public Model3DMeshGroup Group { get; }

        /// <summary>
        /// 当前 mesh group 关联的材质；可能为 <see langword="null"/>
        /// </summary>
        public Model3DMaterial Material { get; }

        /// <summary>
        /// 当前实例所在的渲染层级
        /// </summary>
        public Model3DLayer Layer { get; }

        /// <summary>
        /// 已根据 <see cref="Instance"/> 的位置 / 旋转 / 缩放计算好的世界矩阵
        /// </summary>
        public Matrix World { get; }

        /// <summary>
        /// 当前帧的相机视图矩阵
        /// </summary>
        public Matrix View { get; }

        /// <summary>
        /// 当前帧使用的正交投影矩阵
        /// </summary>
        public Matrix Projection { get; }

        /// <summary>
        /// 已解析完成的光照配置（<see cref="Model3DInstance.LightingOverride"/> 或全局配置 的 scratch 副本，
        /// 并经过 <see cref="Model3DRenderer.ResolveLighting"/> 订阅者的修改）
        /// <br/>对自定义 shader 可用作"读取已确定的方向光参数"的来源，
        /// 通过 <see cref="Model3DRenderer.ApplyLighting"/> 写入任意 <see cref="IEffectLights"/>
        /// </summary>
        public Model3DLightingConfig Lighting { get; }

        /// <summary>
        /// 当前实例是否被分到了透明桶
        /// <br/>由桶分类的结果，反映"渲染时是否使用 alpha blend / 只读深度"等约束
        /// </summary>
        public bool IsTransparent { get; }

        /// <summary>
        /// 当前帧的视觉特效时间（等价于 <see cref="Terraria.Main.timeForVisualEffects"/>）
        /// <br/>对于程序化动画 shader 来说，这是最常用的时间参数来源
        /// </summary>
        public float Time { get; }

        /// <summary>
        /// 完整构造一个绘制上下文
        /// </summary>
        public Model3DDrawContext(GraphicsDevice graphicsDevice, Model3DInstance instance, Vault3DModel model
            , Model3DMeshGroup group, Model3DMaterial material, Model3DLayer layer
            , Matrix world, Matrix view, Matrix projection
            , Model3DLightingConfig lighting, bool isTransparent, float time) {
            GraphicsDevice = graphicsDevice;
            Instance = instance;
            Model = model;
            Group = group;
            Material = material;
            Layer = layer;
            World = world;
            View = view;
            Projection = projection;
            Lighting = lighting;
            IsTransparent = isTransparent;
            Time = time;
        }

        /// <summary>
        /// 在已有上下文的基础上"换一个 group"，便于渲染器在实例级上下文上派生 group 级上下文
        /// </summary>
        /// <param name="group">新的 mesh group</param>
        /// <param name="material">新的材质</param>
        public Model3DDrawContext WithGroup(Model3DMeshGroup group, Model3DMaterial material) {
            return new Model3DDrawContext(GraphicsDevice, Instance, Model, group, material, Layer
                , World, View, Projection, Lighting, IsTransparent, Time);
        }
    }

    /// <summary>
    /// 可选的渲染状态覆盖配置
    /// <br/>每个字段为 <see langword="null"/> 表示"沿用更外层的默认值"；不为 <see langword="null"/> 时覆盖
    /// <br/>解析顺序：实例覆盖 → 材质覆盖 → 桶/实例 bool 字段决定的默认
    /// <br/>注意：若设置 <see cref="Blend"/> 为非 <see cref="BlendState.Opaque"/> 值，对应实例会被自动归入透明桶
    /// </summary>
    public sealed class Model3DRenderState
    {
        /// <summary>
        /// 混合状态覆盖；<see langword="null"/> 沿用桶级（不透明桶为 <see cref="BlendState.Opaque"/>，透明桶为 <see cref="BlendState.NonPremultiplied"/>）
        /// </summary>
        public BlendState Blend;
        /// <summary>
        /// 深度模板状态覆盖；<see langword="null"/> 时按 <see cref="Model3DInstance.DepthEnabled"/> 与透明性自动选择
        /// </summary>
        public DepthStencilState Depth;
        /// <summary>
        /// 光栅化状态覆盖；<see langword="null"/> 时按 <see cref="Model3DInstance.CullBackface"/> 自动选择
        /// </summary>
        public RasterizerState Rasterizer;
        /// <summary>
        /// 0 号采样器状态覆盖；<see langword="null"/> 时默认 <see cref="SamplerState.LinearClamp"/>
        /// </summary>
        public SamplerState Sampler0;

        /// <summary>
        /// 是否需要走透明桶（被显式给了一个非 <see cref="BlendState.Opaque"/> 的 <see cref="Blend"/>）
        /// <br/>渲染器 <see cref="Model3DRenderer"/> 的桶分类逻辑会读取此判定
        /// </summary>
        public bool ForcesTransparentBucket => Blend != null && Blend != BlendState.Opaque;

        /// <summary>
        /// 把"实例覆盖 → 材质覆盖 → 默认"这一链路解析后的最终状态应用到 GraphicsDevice
        /// <br/>任意层级为 <see langword="null"/> 都会被跳过，最终使用默认值
        /// </summary>
        public static void ApplyResolved(GraphicsDevice gd
            , Model3DRenderState instanceState, Model3DRenderState materialState
            , BlendState defaultBlend, DepthStencilState defaultDepth
            , RasterizerState defaultRasterizer, SamplerState defaultSampler) {
            if (gd == null) {
                return;
            }
            BlendState blend = instanceState?.Blend ?? materialState?.Blend ?? defaultBlend;
            DepthStencilState depth = instanceState?.Depth ?? materialState?.Depth ?? defaultDepth;
            RasterizerState raster = instanceState?.Rasterizer ?? materialState?.Rasterizer ?? defaultRasterizer;
            SamplerState sampler = instanceState?.Sampler0 ?? materialState?.Sampler0 ?? defaultSampler;
            if (blend != null) {
                gd.BlendState = blend;
            }
            if (depth != null) {
                gd.DepthStencilState = depth;
            }
            if (raster != null) {
                gd.RasterizerState = raster;
            }
            if (sampler != null) {
                gd.SamplerStates[0] = sampler;
            }
        }
    }

    /// <summary>
    /// 自定义 Effect 提供者；适合"跨实例可复用"的 shader 封装
    /// <br/>例如：一个 <c>HologramEffectProvider</c> 实现，可以被多个 boss 实例共享，
    /// 每帧由 <see cref="Resolve"/> 返回同一份 <see cref="Effect"/>，再由 <see cref="Configure"/> 写不同的实例参数
    /// </summary>
    public interface IModel3DEffectProvider
    {
        /// <summary>
        /// 返回当前实例应该使用的 <see cref="Effect"/>；返回 <see langword="null"/> 时渲染器会回落到下一级（材质 / 默认 BasicEffect）
        /// </summary>
        /// <param name="ctx">当前绘制上下文</param>
        Effect Resolve(in Model3DDrawContext ctx);

        /// <summary>
        /// 在每个 group 绘制前调用，负责把 World / View / Projection / 自定义参数写入 <paramref name="effect"/>
        /// <br/>当渲染器使用自定义 <see cref="Effect"/>（非 <see cref="BasicEffect"/>）时，
        /// 渲染器自身不会自动写入 World/View/Proj 与光照参数，全部由本方法负责
        /// </summary>
        /// <param name="ctx">当前绘制上下文</param>
        /// <param name="effect">由 <see cref="Resolve"/> 返回（或上一级解析得到）的 Effect 实例</param>
        void Configure(in Model3DDrawContext ctx, Effect effect);
    }

    /// <summary>
    /// 配置 Effect 参数的轻量回调；适合"一次性使用"的场景，无需写一个 Provider 类
    /// <br/>调用时机：每个 group 绘制前；调用顺序：实例级 → 材质级（实例级先调，材质级再调）
    /// </summary>
    /// <param name="ctx">当前绘制上下文</param>
    /// <param name="effect">将要使用的 Effect 实例</param>
    public delegate void Model3DConfigureEffect(in Model3DDrawContext ctx, Effect effect);

    /// <summary>
    /// 绘制阶段回调；用于 Pre/Post Instance/Group 等事件订阅
    /// </summary>
    /// <param name="ctx">当前绘制上下文（group 级时含 <see cref="Model3DDrawContext.Group"/>，实例级时为空）</param>
    public delegate void Model3DDrawCallback(in Model3DDrawContext ctx);
}
