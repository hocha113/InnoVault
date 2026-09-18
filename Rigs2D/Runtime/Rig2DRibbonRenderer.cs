using InnoVault.Rigs2D.Data;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using System;
using System.Collections.Generic;
using Terraria;

namespace InnoVault.Rigs2D.Runtime
{
    /// <summary>
    /// 带状件渲染器：把一条骨链的关节点连成三角形条带，贴上纹理，逐顶点取光照
    /// <br/>贴图约定：u 沿链（根 0 → 尖 1，或按世界长度平铺），v 横跨（左 0 → 右 1）
    /// <br/><b>批次例外</b>：条带走 <see cref="GraphicsDevice.DrawUserIndexedPrimitives{T}(PrimitiveType, T[], int, int, short[], int, int)"/>，
    /// 必须在 Immediate 批次里画，所以本渲染器会 <c>End</c> 当前批次、以 <see cref="Rig2DDrawContext.BatchMatrix"/> /
    /// <see cref="Rig2DDrawContext.Rasterizer"/> 重开 Immediate 批次、画完再恢复成 Deferred / AlphaBlend / <see cref="Rig2DDrawContext.Sampler"/>。
    /// 调用方必须处于一个已 <c>Begin</c> 的批次内；此前排队的精灵会先被 <c>End</c> 冲出，层序因此得以保持。
    /// 要与整图件按层序交错，用 <see cref="Rig2DRenderer.DrawAll(SpriteBatch, Rig2DInstance, in Rig2DDrawContext)"/>，或自己用 <see cref="Rig2DDrawContext.Layers"/> 分带
    /// </summary>
    public static class Rig2DRibbonRenderer
    {
        private static readonly List<Vector2> jointScratch = new(32);
        private static readonly List<Vector2> pathScratch = new(128);
        private static readonly List<float> lengthScratch = new(128);
        private static ColoredVertex[] vertexScratch = new ColoredVertex[128];
        private static short[] indexScratch = new short[384];
        /// <summary>条带三角形不分正反面，必须关剔除；舞台裁剪（ScissorTestEnable）却要保留，所以缓存一份"CullNone + 裁剪开"的光栅态</summary>
        private static RasterizerState cullNoneScissor;

        /// <summary>
        /// 绘制实例的全部可见带状件（按 <see cref="Ribbon2DState.SortKey"/> 升序，受 <see cref="Rig2DDrawContext.LayerMin"/> / <see cref="Rig2DDrawContext.LayerMax"/> 过滤）
        /// </summary>
        public static void Draw(SpriteBatch sb, Rig2DInstance rig, in Rig2DDrawContext ctx) {
            if (rig == null || rig.Definition == null) {
                return;
            }
            Draw(sb, rig, rig.Bones, in ctx);
        }

        /// <summary>
        /// 用另一套骨骼位姿（例如 <see cref="Rig2DPoseTrail"/> 的快照）绘制实例的带状件；件状态仍取实例当前值
        /// </summary>
        public static void Draw(SpriteBatch sb, Rig2DInstance rig, Bone2D[] bones, in Rig2DDrawContext ctx) {
            if (rig == null || rig.Definition == null || bones == null) {
                return;
            }
            DrawIndices(sb, rig, bones, in ctx, rig.SortedRibbons());
        }

        /// <summary>
        /// 绘制给定索引的带状件（顺序即绘制顺序；不可见或层序越界的跳过）。
        /// 一次调用只切一轮批次：进入时 <c>End</c> → Immediate，退出时恢复 Deferred
        /// </summary>
        public static void DrawIndices(SpriteBatch sb, Rig2DInstance rig, Bone2D[] bones, in Rig2DDrawContext ctx, ReadOnlySpan<int> indices) {
            if (sb == null || rig == null || rig.Definition == null || bones == null || ctx.Alpha <= 0.001f || indices.Length == 0) {
                return;
            }
            GraphicsDevice gd = Main.instance.GraphicsDevice;
            bool opened = false;
            BlendState curBlend = null;
            SamplerState curSampler = null;
            for (int k = 0; k < indices.Length; k++) {
                int i = indices[k];
                if (i < 0 || i >= rig.Ribbons.Length) {
                    continue;
                }
                ref Ribbon2DState st = ref rig.Ribbons[i];
                if (!st.Visible || st.SortKey < ctx.LayerMin || st.SortKey > ctx.LayerMax) {
                    continue;
                }
                Ribbon2DDef def = rig.Definition.Ribbons[i];
                Texture2D tex = ResolveTexture(rig, i, in st);
                if (tex == null) {
                    continue;
                }
                if (!BuildMesh(rig, def, in st, bones, in ctx, out int vertexCount, out int indexCount)) {
                    continue;
                }
                BlendState blend = ctx.RibbonBlendOverride ?? (def.Additive ? BlendState.Additive : BlendState.AlphaBlend);
                SamplerState sampler = def.Uv == Ribbon2DUv.Tile ? WrapOf(ctx.Sampler) : ctx.Sampler;
                if (!opened || !ReferenceEquals(blend, curBlend) || !ReferenceEquals(sampler, curSampler)) {
                    sb.End();
                    sb.Begin(SpriteSortMode.Immediate, blend, sampler, DepthStencilState.None, ctx.Rasterizer, null, ctx.BatchMatrix);
                    opened = true;
                    curBlend = blend;
                    curSampler = sampler;
                }
                gd.Textures[0] = tex;
                gd.SamplerStates[0] = sampler;
                gd.RasterizerState = CullNoneLike(ctx.Rasterizer);
                gd.DrawUserIndexedPrimitives(PrimitiveType.TriangleList, vertexScratch, 0, vertexCount, indexScratch, 0, indexCount / 3);
            }
            if (opened) {
                sb.End();
                sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, ctx.Sampler, DepthStencilState.None, ctx.Rasterizer, null, ctx.BatchMatrix);
            }
        }

        /// <summary>
        /// 取一条带状件本帧的中心路径（世界坐标；关节点 + 可选尖端 + Catmull-Rom 细分），调试叠层与消费方判定复用
        /// </summary>
        /// <returns>路径点数（少于 2 表示画不出条带）</returns>
        public static int BuildPath(Rig2DInstance rig, Ribbon2DDef def, Bone2D[] bones, List<Vector2> path) {
            path.Clear();
            if (rig == null || def == null || bones == null) {
                return 0;
            }
            jointScratch.Clear();
            int[] chain = def.BoneIndices;
            for (int k = 0; k < chain.Length; k++) {
                int b = chain[k];
                if (b < 0 || b >= bones.Length) {
                    continue;
                }
                AddDistinct(jointScratch, bones[b].Pos);
            }
            if (def.IncludeTip && chain.Length > 0) {
                int last = chain[^1];
                if (last >= 0 && last < bones.Length) {
                    AddDistinct(jointScratch, bones[last].Tip);
                }
            }
            int n = jointScratch.Count;
            if (n < 2) {
                return n;
            }
            if (def.Smooth <= 0 || n < 3) {
                path.AddRange(jointScratch);
                return path.Count;
            }
            //Catmull-Rom：端点复用自身作为虚拟控制点
            int sub = def.Smooth;
            for (int i = 0; i < n - 1; i++) {
                Vector2 p0 = jointScratch[Math.Max(i - 1, 0)];
                Vector2 p1 = jointScratch[i];
                Vector2 p2 = jointScratch[i + 1];
                Vector2 p3 = jointScratch[Math.Min(i + 2, n - 1)];
                path.Add(p1);
                for (int s = 1; s <= sub; s++) {
                    float t = s / (float)(sub + 1);
                    float t2 = t * t;
                    float t3 = t2 * t;
                    Vector2 q = 0.5f * (2f * p1
                        + (-p0 + p2) * t
                        + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
                        + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
                    path.Add(q);
                }
            }
            path.Add(jointScratch[n - 1]);
            return path.Count;
        }

        /// <summary>
        /// 带状件某点的最终着色：环境光 × 设计着色 × 运行时着色 × 压暗 × 不透明度（与整图件同一套规则）
        /// </summary>
        public static Color RibbonColor(Ribbon2DDef def, in Ribbon2DState st, Vector2 world, in Rig2DDrawContext ctx) {
            Color c = def.Unlit ? ctx.UnlitAt() : ctx.LightAt(world);
            if (def.Tint != Color.White) {
                c = c.MultiplyRGBA(def.Tint);
            }
            if (st.TintMul != Color.White) {
                c = c.MultiplyRGBA(st.TintMul);
            }
            float dark = def.Dark * st.DarkMul;
            if (dark != 1f) {
                c = new Color((byte)MathHelper.Clamp(c.R * dark, 0f, 255f), (byte)MathHelper.Clamp(c.G * dark, 0f, 255f),
                    (byte)MathHelper.Clamp(c.B * dark, 0f, 255f), c.A);
            }
            float alpha = def.Alpha * st.AlphaMul;
            return alpha != 1f ? c * alpha : c;
        }

        //==================== 内部 ====================

        private static bool BuildMesh(Rig2DInstance rig, Ribbon2DDef def, in Ribbon2DState st, Bone2D[] bones, in Rig2DDrawContext ctx,
            out int vertexCount, out int indexCount) {
            vertexCount = 0;
            indexCount = 0;
            int m = BuildPath(rig, def, bones, pathScratch);
            if (m < 2 || m > short.MaxValue / 2) {
                return false;
            }
            //累计弧长
            lengthScratch.Clear();
            float total = 0f;
            lengthScratch.Add(0f);
            for (int k = 1; k < m; k++) {
                total += Vector2.Distance(pathScratch[k - 1], pathScratch[k]);
                lengthScratch.Add(total);
            }
            if (total < 0.5f) {
                return false;
            }

            vertexCount = m * 2;
            indexCount = (m - 1) * 6;
            if (vertexScratch.Length < vertexCount) {
                vertexScratch = new ColoredVertex[Math.Max(vertexCount, vertexScratch.Length * 2)];
            }
            if (indexScratch.Length < indexCount) {
                indexScratch = new short[Math.Max(indexCount, indexScratch.Length * 2)];
            }

            float scale = Math.Max(rig.Scale, 0.001f);
            float tileLen = Math.Max(def.TileLength * scale, 0.001f);
            for (int k = 0; k < m; k++) {
                Vector2 p = pathScratch[k];
                Vector2 tangent = Tangent(k, m);
                Vector2 normal = new(-tangent.Y, tangent.X);
                float t = lengthScratch[k] / total;
                float half = def.WidthAt(t) * 0.5f * scale * st.WidthMul;
                float u = def.Uv == Ribbon2DUv.Tile ? lengthScratch[k] / tileLen : t;
                u += st.UvOffset;
                Color color = RibbonColor(def, in st, p, in ctx);
                Vector2 left = p - normal * half - ctx.ViewOffset;
                Vector2 right = p + normal * half - ctx.ViewOffset;
                vertexScratch[k * 2] = new ColoredVertex(left, color, new Vector3(u, 0f, 1f));
                vertexScratch[k * 2 + 1] = new ColoredVertex(right, color, new Vector3(u, 1f, 1f));
            }
            for (int k = 0; k < m - 1; k++) {
                int vi = k * 2;
                int ii = k * 6;
                indexScratch[ii] = (short)vi;
                indexScratch[ii + 1] = (short)(vi + 1);
                indexScratch[ii + 2] = (short)(vi + 2);
                indexScratch[ii + 3] = (short)(vi + 2);
                indexScratch[ii + 4] = (short)(vi + 1);
                indexScratch[ii + 5] = (short)(vi + 3);
            }
            return true;
        }

        //关节处取相邻两段切向的平均，条带在转角处不会自交出楔口
        private static Vector2 Tangent(int k, int m) {
            Vector2 t = Vector2.Zero;
            if (k > 0) {
                t += Utils.SafeNormalize(pathScratch[k] - pathScratch[k - 1], Vector2.Zero);
            }
            if (k < m - 1) {
                t += Utils.SafeNormalize(pathScratch[k + 1] - pathScratch[k], Vector2.Zero);
            }
            if (t.LengthSquared() < 0.0001f) {
                //前后两段反向折死：退回单段切向
                t = k < m - 1 ? pathScratch[k + 1] - pathScratch[k] : pathScratch[k] - pathScratch[k - 1];
                if (t.LengthSquared() < 0.0001f) {
                    return Vector2.UnitX;
                }
            }
            t.Normalize();
            return t;
        }

        private static void AddDistinct(List<Vector2> list, Vector2 p) {
            if (list.Count > 0 && Vector2.DistanceSquared(list[^1], p) < 0.0001f) {
                return;
            }
            list.Add(p);
        }

        private static Texture2D ResolveTexture(Rig2DInstance rig, int ribbonIndex, in Ribbon2DState st) {
            if (st.TextureOverride != null) {
                return st.TextureOverride.Value;
            }
            Asset<Texture2D>[] textures = rig.Asset?.RibbonTextures;
            if (textures == null || ribbonIndex >= textures.Length) {
                return null;
            }
            return textures[ribbonIndex]?.Value;
        }

        /// <summary>
        /// 卸载：释放自建的光栅态（GPU 资源在主线程释放）
        /// </summary>
        internal static void Unload() {
            RasterizerState state = cullNoneScissor;
            cullNoneScissor = null;
            if (state == null || Main.dedServ) {
                return;
            }
            Main.QueueMainThreadAction(state.Dispose);
        }

        //关剔除但沿用调用方光栅态的裁剪开关：Stage(..., scissor) 的条带不得画出舞台裁剪框
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

        private static SamplerState WrapOf(SamplerState sampler) {
            if (sampler == null) {
                return SamplerState.LinearWrap;
            }
            if (sampler.AddressU == TextureAddressMode.Wrap && sampler.AddressV == TextureAddressMode.Wrap) {
                return sampler;
            }
            return sampler.Filter switch {
                TextureFilter.Point => SamplerState.PointWrap,
                TextureFilter.Anisotropic => SamplerState.AnisotropicWrap,
                _ => SamplerState.LinearWrap,
            };
        }
    }
}
