using InnoVault.Vectors.Tessellation;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using Terraria;

namespace InnoVault.Vectors
{
    /// <summary>
    /// 网格顶点所处的坐标空间，决定 <see cref="VectorRenderer"/> 用哪套矩阵把它投到当前视口
    /// </summary>
    public enum VectorSpace
    {
        /// <summary>世界坐标（<c>Projectile.Center</c> 一类原始坐标，不要自己减 <c>Main.screenPosition</c>）：平移 + <c>GameViewMatrix</c> 缩放 / 反重力 + 正交投影</summary>
        World,
        /// <summary>已减去 <c>Main.screenPosition</c> 的屏幕坐标，跟随游戏缩放：与 <c>Main.spriteBatch</c> 世界批次中 <c>Draw(pos - screenPosition)</c> 的落点一致</summary>
        Screen,
        /// <summary>UI 坐标，跟随 <c>Main.UIScaleMatrix</c>：与 UIHandle / 界面层的 <c>SpriteBatch</c> 落点一致</summary>
        UI,
        /// <summary>当前视口的像素坐标，不做任何缩放（渲染目标内绘制时即该目标的像素）</summary>
        Raw,
    }

    /// <summary>
    /// 一次网格提交的全部选项。值类型，可用 <c>with</c> 派生
    /// </summary>
    public struct VectorDrawOptions
    {
        /// <summary>着色器；为空时按 <see cref="Antialias"/> 选内置 SDF 描边着色器或 <see cref="BasicEffect"/>（顶点色 × 可选贴图）。自带着色器须是 vs+ps 成对的 technique</summary>
        public Effect Effect;
        /// <summary>绑到 <c>Textures[0]</c> 的贴图（在每个 pass Apply 之后绑定，不会被着色器参数覆盖）；内置着色器下为空即纯顶点色</summary>
        public Texture2D Texture;
        /// <summary>顶点坐标空间</summary>
        public VectorSpace Space;
        /// <summary>自定义投影矩阵，非空时覆盖 <see cref="Space"/>；往尺寸不同于屏幕的渲染目标里画时按该目标尺寸自己拼</summary>
        public Matrix? CustomMatrix;
        /// <summary>混合状态；为空沿用设备当前值（Deferred 批次中那是上次冲刷的残留，正式代码请显式给）</summary>
        public BlendState Blend;
        /// <summary>采样器（<c>SamplerStates[0]</c>）；为空时：有贴图取 <see cref="SamplerState.LinearClamp"/>，平铺描边请显式给 Wrap 采样器</summary>
        public SamplerState Sampler;
        /// <summary>
        /// 自带着色器里接收变换矩阵的参数名（如 <c>"transformMatrix"</c>）；非空时按 <see cref="Space"/> 自动赋值，
        /// 消费者不必再手算 <c>VaultUtils.GetTransfromMatrix()</c>，也不会再把世界坐标误减一次 <c>screenPosition</c>
        /// </summary>
        public string MatrixParameter;
        /// <summary>
        /// 正在其中绘制的 <see cref="SpriteBatch"/>（可空）。Immediate 批次只在 Begin 时设一次着色器，网格提交后同批次的精灵会用错着色器；
        /// 给了这个字段，提交结束时会调 <see cref="SpriteBatchState.TryRestoreImmediate"/> 把批次自己的状态与精灵着色器重新应用。Deferred 批次不需要
        /// </summary>
        public SpriteBatch SpriteBatch;
        /// <summary>
        /// 抗锯齿软边宽度（像素），大于 0 且 <see cref="Effect"/> 为空时改用内置 SDF 描边着色器：沿条带横向做距离场，边缘按此宽度过渡，
        /// 并支持 <see cref="Glow"/> 中心提亮；着色器缺失时退回 <see cref="BasicEffect"/> 硬边。只对描边条带有意义（v 横跨 0~1），填充网格的 v 是包围盒坐标，不要开
        /// </summary>
        public float Antialias;
        /// <summary>SDF 描边的中心提亮强度（0 = 关）：越靠条带中线越亮，按 <see cref="GlowPower"/> 的幂衰减到边缘</summary>
        public float Glow;
        /// <summary>中心提亮的衰减幂；≤ 0 时取 2</summary>
        public float GlowPower;

        /// <summary>世界空间、内置着色器</summary>
        public static VectorDrawOptions WorldDefault => new() { Space = VectorSpace.World };
        /// <summary>屏幕空间、内置着色器</summary>
        public static VectorDrawOptions ScreenDefault => new() { Space = VectorSpace.Screen };
        /// <summary>UI 空间、内置着色器</summary>
        public static VectorDrawOptions UIDefault => new() { Space = VectorSpace.UI };

        /// <summary>构造：指定空间与可选着色器 / 贴图</summary>
        public VectorDrawOptions(VectorSpace space, Effect effect = null, Texture2D texture = null) {
            Space = space;
            Effect = effect;
            Texture = texture;
            CustomMatrix = null;
            Blend = null;
            Sampler = null;
            MatrixParameter = null;
            SpriteBatch = null;
            Antialias = 0f;
            Glow = 0f;
            GlowPower = 0f;
        }

        /// <summary>构造：在某个 <see cref="SpriteBatch"/> 批次内绘制（提交后自动恢复 Immediate 批次的着色器）</summary>
        public VectorDrawOptions(SpriteBatch spriteBatch, VectorSpace space = VectorSpace.UI, Effect effect = null, Texture2D texture = null) : this(space, effect, texture) {
            SpriteBatch = spriteBatch;
        }
    }

    /// <summary>
    /// 网格后端：把 <see cref="VectorMesh"/> 以 <c>DrawUserIndexedPrimitives</c> 提交给显卡
    /// <br/>批次规则：Deferred 批次内可直接调用（SpriteBatch 会在 End 冲刷时重设自身状态），但已排队的精灵会晚于本次网格落到画面，需要严格层序时先 End 再画再 Begin；
    /// <b>Immediate 批次内必须在 <see cref="VectorDrawOptions.SpriteBatch"/> 填入该批次</b>，否则同批次后续精灵会沿用本次提交留下的着色器（Immediate 只在 Begin 时设一次着色器）
    /// <br/>只允许在渲染线程调用
    /// </summary>
    public static class VectorRenderer
    {
        private static BasicEffect basicEffect;
        private static RasterizerState cullNoneScissor;
        private static readonly VectorMesh scratch = new(512, 1536);
        //贴图端帽专用暂存网格，首次用到贴图端帽才分配
        private static VectorMesh capScratch;

        /// <summary>
        /// 取某个坐标空间的顶点 → 裁剪空间矩阵，供自带着色器手动赋值。正交投影按当前视口尺寸（与 SpriteBatch 同法），渲染目标内自动适配
        /// </summary>
        public static Matrix GetMatrix(VectorSpace space) {
            Viewport vp = Main.instance.GraphicsDevice.Viewport;
            Matrix projection = Matrix.CreateOrthographicOffCenter(0f, vp.Width, vp.Height, 0f, -1f, 1f);
            return space switch {
                VectorSpace.World => Matrix.CreateTranslation(-Main.screenPosition.X, -Main.screenPosition.Y, 0f) * Main.GameViewMatrix.TransformationMatrix * projection,
                VectorSpace.Screen => Main.GameViewMatrix.TransformationMatrix * projection,
                VectorSpace.UI => Main.UIScaleMatrix * projection,
                _ => projection,
            };
        }

        /// <summary>提交网格：默认世界空间 + 内置着色器</summary>
        public static void Draw(VectorMesh mesh, Effect effect = null, VectorSpace space = VectorSpace.World, Texture2D texture = null)
            => Draw(mesh, new VectorDrawOptions(space, effect, texture));

        /// <summary>提交网格</summary>
        public static void Draw(VectorMesh mesh, in VectorDrawOptions options) {
            if (mesh == null || mesh.IsEmpty || Main.dedServ) {
                return;
            }
            GraphicsDevice gd = Main.instance.GraphicsDevice;
            Matrix matrix = options.CustomMatrix ?? GetMatrix(options.Space);

            BlendState prevBlend = gd.BlendState;
            RasterizerState prevRaster = gd.RasterizerState;
            SamplerState prevSampler = gd.SamplerStates[0];
            DepthStencilState prevDepth = gd.DepthStencilState;
            if (options.Blend != null) {
                gd.BlendState = options.Blend;
            }
            //平面网格不参与深度测试，免受上一个绘制者残留的深度状态影响
            gd.DepthStencilState = DepthStencilState.None;
            gd.RasterizerState = CullNoneLike(prevRaster);
            SamplerState sampler = options.Sampler ?? (options.Texture != null ? SamplerState.LinearClamp : null);

            Effect effect = options.Effect;
            if (effect == null) {
                if (options.Antialias > 0f && VectorEffects.SdfStrokeAvailable) {
                    effect = VectorEffects.VectorStroke.Value;
                    EffectParameterCollection p = effect.Parameters;
                    p["transformMatrix"]?.SetValue(matrix);
                    p["uAA"]?.SetValue(options.Antialias);
                    p["uGlow"]?.SetValue(MathF.Max(options.Glow, 0f));
                    p["uGlowPower"]?.SetValue(options.GlowPower > 0f ? options.GlowPower : 2f);
                    p["uTextured"]?.SetValue(options.Texture != null ? 1f : 0f);
                }
                else {
                    BasicEffect basic = GetBasicEffect(gd);
                    basic.Projection = matrix;
                    basic.TextureEnabled = options.Texture != null;
                    basic.Texture = options.Texture;
                    effect = basic;
                }
            }
            else if (!string.IsNullOrEmpty(options.MatrixParameter)) {
                effect.Parameters[options.MatrixParameter]?.SetValue(matrix);
            }

            foreach (EffectPass pass in effect.CurrentTechnique.Passes) {
                pass.Apply();
                //贴图与采样器在 Apply 之后绑定，否则会被着色器自己的参数覆盖
                if (options.Texture != null) {
                    gd.Textures[0] = options.Texture;
                }
                if (sampler != null) {
                    gd.SamplerStates[0] = sampler;
                }
                gd.DrawUserIndexedPrimitives(PrimitiveType.TriangleList, mesh.Vertices, 0, mesh.VertexCount, mesh.Indices, 0, mesh.TriangleCount);
            }

            gd.RasterizerState = prevRaster;
            gd.DepthStencilState = prevDepth;
            if (options.Blend != null) {
                gd.BlendState = prevBlend;
            }
            if (sampler != null) {
                gd.SamplerStates[0] = prevSampler;
            }
            if (options.SpriteBatch != null) {
                SpriteBatchState.TryRestoreImmediate(options.SpriteBatch);
            }
        }

        /// <summary>
        /// 一步完成：描边路径并提交（内部共享暂存网格，每次调用一次提交）
        /// <br/>样式用了 <see cref="LineCap.Texture"/> 时，描边之后再提交一次端帽四边形（同空间 / 矩阵 / 混合，内置着色器 + <see cref="StrokeStyle.CapTexture"/>）
        /// </summary>
        public static void DrawStroke(VectorPath path, StrokeStyle style, in VectorTransform transform, in VectorDrawOptions options) {
            scratch.Clear();
            StrokeTessellator.ResetCapStamps();
            scratch.AppendStroke(path, style, in transform);
            Draw(scratch, in options);
            DrawCapStamps(style, in options);
        }

        /// <summary>一步完成：描边路径并提交（恒等变换，默认世界空间）</summary>
        public static void DrawStroke(VectorPath path, StrokeStyle style, Effect effect = null, VectorSpace space = VectorSpace.World, Texture2D texture = null)
            => DrawStroke(path, style, in VectorTransform.Identity, new VectorDrawOptions(space, effect, texture));

        /// <summary>在 <paramref name="sb"/> 批次内描边路径并提交，结束时自动恢复 Immediate 批次的着色器（默认 UI 空间）</summary>
        public static void DrawStroke(SpriteBatch sb, VectorPath path, StrokeStyle style, in VectorTransform transform, VectorSpace space = VectorSpace.UI, Effect effect = null, Texture2D texture = null)
            => DrawStroke(path, style, in transform, new VectorDrawOptions(sb, space, effect, texture) { Blend = BlendState.AlphaBlend });

        /// <summary>
        /// 一步完成：描一条输出空间点列并提交。旧 <c>Trail.TrailPositions</c> + <c>DrawTrail(effect)</c> 的直接替代
        /// </summary>
        public static void DrawStroke(ReadOnlySpan<Vector2> points, StrokeStyle style, in VectorDrawOptions options, bool closed = false) {
            scratch.Clear();
            StrokeTessellator.ResetCapStamps();
            scratch.AppendStroke(points, style, closed);
            Draw(scratch, in options);
            DrawCapStamps(style, in options);
        }

        /// <summary>一步完成：描一条输出空间点列并提交（默认世界空间）</summary>
        public static void DrawStroke(ReadOnlySpan<Vector2> points, StrokeStyle style, Effect effect = null, VectorSpace space = VectorSpace.World, Texture2D texture = null)
            => DrawStroke(points, style, new VectorDrawOptions(space, effect, texture));

        /// <summary>在 <paramref name="sb"/> 批次内描一条点列并提交，结束时自动恢复 Immediate 批次的着色器（默认 UI 空间）</summary>
        public static void DrawStroke(SpriteBatch sb, ReadOnlySpan<Vector2> points, StrokeStyle style, VectorSpace space = VectorSpace.UI, bool closed = false, Effect effect = null, Texture2D texture = null)
            => DrawStroke(points, style, new VectorDrawOptions(sb, space, effect, texture) { Blend = BlendState.AlphaBlend }, closed);

        /// <summary>一步完成：填充路径并提交</summary>
        public static void DrawFill(VectorPath path, FillStyle style, in VectorTransform transform, in VectorDrawOptions options) {
            scratch.Clear();
            scratch.AppendFill(path, style, in transform);
            Draw(scratch, in options);
        }

        /// <summary>一步完成：填充路径并提交（恒等变换，默认世界空间）</summary>
        public static void DrawFill(VectorPath path, FillStyle style, Effect effect = null, VectorSpace space = VectorSpace.World, Texture2D texture = null)
            => DrawFill(path, style, in VectorTransform.Identity, new VectorDrawOptions(space, effect, texture));

        /// <summary>在 <paramref name="sb"/> 批次内填充路径并提交，结束时自动恢复 Immediate 批次的着色器（默认 UI 空间）</summary>
        public static void DrawFill(SpriteBatch sb, VectorPath path, FillStyle style, in VectorTransform transform, VectorSpace space = VectorSpace.UI, Effect effect = null, Texture2D texture = null)
            => DrawFill(path, style, in transform, new VectorDrawOptions(sb, space, effect, texture) { Blend = BlendState.AlphaBlend });

        /// <summary>
        /// 提交一份手写网格（<see cref="VertexPositionColorTexture"/>），不改动着色器，沿用设备当前已 Apply 的 pass：
        /// 旧 <c>Trail.DrawUserPrimitives</c> 的直接替代。需要自动矩阵与着色器时改用 <see cref="Draw(VectorMesh, in VectorDrawOptions)"/>
        /// </summary>
        public static void DrawUserMesh(VertexPositionColorTexture[] vertices, int vertexCount, short[] indices, int indexCount) {
            if (vertices == null || indices == null || vertexCount < 3 || indexCount < 3 || Main.dedServ) {
                return;
            }
            GraphicsDevice gd = Main.instance.GraphicsDevice;
            RasterizerState prev = gd.RasterizerState;
            gd.RasterizerState = CullNoneLike(prev);
            gd.DrawUserIndexedPrimitives(PrimitiveType.TriangleList, vertices, 0, vertexCount, indices, 0, indexCount / 3);
            gd.RasterizerState = prev;
        }

        /// <summary>提交一份手写网格（整数组）</summary>
        public static void DrawUserMesh(VertexPositionColorTexture[] vertices, short[] indices)
            => DrawUserMesh(vertices, vertices?.Length ?? 0, indices, indices?.Length ?? 0);

        /// <summary>提交一份 <see cref="ColoredVertex"/> 手写网格，沿用设备当前着色器；旧 <c>Trail.DrawUserPrimitives(ColoredVertex[], short[])</c> 的直接替代</summary>
        public static void DrawUserMesh(ColoredVertex[] vertices, short[] indices) {
            if (vertices == null || indices == null || vertices.Length < 3 || indices.Length < 3 || Main.dedServ) {
                return;
            }
            GraphicsDevice gd = Main.instance.GraphicsDevice;
            RasterizerState prev = gd.RasterizerState;
            gd.RasterizerState = CullNoneLike(prev);
            gd.DrawUserIndexedPrimitives(PrimitiveType.TriangleList, vertices, 0, vertices.Length, indices, 0, indices.Length / 3);
            gd.RasterizerState = prev;
        }

        //==================== 内部 ====================

        //贴图端帽的提交选项：沿用同一空间 / 矩阵 / 批次，强制内置着色器（忽略调用方自带着色器与 SDF 参数）+ 端帽贴图；
        //混合优先用 StrokeStyle.CapBlend，为空才沿用本次提交的混合
        internal static VectorDrawOptions CapStampOptions(StrokeStyle style, in VectorDrawOptions options) {
            VectorDrawOptions capOptions = options;
            capOptions.Effect = null;
            capOptions.MatrixParameter = null;
            capOptions.Texture = style.CapTexture;
            capOptions.Blend = style.CapBlend ?? options.Blend;
            capOptions.Sampler = options.Sampler ?? SamplerState.LinearClamp;
            capOptions.Antialias = 0f;
            capOptions.Glow = 0f;
            capOptions.GlowPower = 0f;
            return capOptions;
        }

        //把上一次描边收集到的贴图端帽单独提交一次
        private static void DrawCapStamps(StrokeStyle style, in VectorDrawOptions options) {
            if (style?.CapTexture == null || StrokeTessellator.CapStampCount == 0) {
                return;
            }
            capScratch ??= new VectorMesh(64, 192);
            capScratch.Clear();
            StrokeTessellator.AppendCapQuads(capScratch);
            VectorDrawOptions capOptions = CapStampOptions(style, in options);
            Draw(capScratch, in capOptions);
        }

        private static BasicEffect GetBasicEffect(GraphicsDevice gd) {
            if (basicEffect == null || basicEffect.IsDisposed) {
                basicEffect = new BasicEffect(gd) {
                    VertexColorEnabled = true,
                    TextureEnabled = false,
                    LightingEnabled = false,
                    World = Matrix.Identity,
                    View = Matrix.Identity,
                };
            }
            return basicEffect;
        }

        //关剔除但沿用调用方的裁剪开关：舞台裁剪内的网格不得画出裁剪框
        private static RasterizerState CullNoneLike(RasterizerState source) {
            if (source == null || !source.ScissorTestEnable) {
                return RasterizerState.CullNone;
            }
            cullNoneScissor ??= new RasterizerState {
                CullMode = CullMode.None,
                ScissorTestEnable = true,
            };
            return cullNoneScissor;
        }

        /// <summary>卸载：在主线程释放自建的 GPU 资源</summary>
        internal static void Unload() {
            BasicEffect fx = basicEffect;
            RasterizerState rs = cullNoneScissor;
            basicEffect = null;
            cullNoneScissor = null;
            scratch.Clear();
            capScratch = null;
            StrokeTessellator.ResetCapStamps();
            if (Main.dedServ) {
                return;
            }
            Main.QueueMainThreadAction(() => {
                fx?.Dispose();
                rs?.Dispose();
            });
        }
    }
}
