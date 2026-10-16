using InnoVault.Rigs2D.Data;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace InnoVault.Rigs2D.Runtime
{
    /// <summary>
    /// 网格件渲染器：按蒙皮表把每个顶点从各骨骼的局部坐标加权变到世界，贴上纹理画成三角形
    /// <br/>蒙皮公式：顶点 = Σ 权重 × (骨近端 + 骨轴向 × x × Scale + 骨右侧 × y × Scale × 镜像号)；
    /// 骨架镜像时网格沿骨轴翻面，与整图件的镜像规则一致。权重在加载期求好（<see cref="Mesh2DDef"/>），帧内只做加法
    /// <br/><b>批次例外</b>：与带状件相同，网格件要在 Immediate 批次里画，本渲染器会 <c>End</c> 当前批次、
    /// 以 <see cref="Rig2DDrawContext.BatchMatrix"/> 重开，画完恢复成 Deferred / AlphaBlend / <see cref="Rig2DDrawContext.Sampler"/>；
    /// 与整图件、带状件按层序交错用 <see cref="Rig2DRenderer.DrawAll(SpriteBatch, Rig2DInstance, in Rig2DDrawContext)"/>，相邻的带状件与网格件共用一轮批次
    /// <br/><see cref="Rig2DDrawContext.RibbonBlendOverride"/> 对网格件同样生效
    /// </summary>
    public static class Rig2DMeshRenderer
    {
        private static Vector2[] positionScratch = new Vector2[256];
        private static Rig2DVertex[] vertexScratch = new Rig2DVertex[256];
        //逐骨三角函数缓存：每次变形递增印记，同一张网格里同一根骨只算一次
        private static float[] cosCache = [];
        private static float[] sinCache = [];
        private static int[] trigStamp = [];
        private static int stamp;

        /// <summary>
        /// 绘制实例的全部可见网格件（按 <see cref="Mesh2DState.SortKey"/> 升序，受 <see cref="Rig2DDrawContext.LayerMin"/> / <see cref="Rig2DDrawContext.LayerMax"/> 过滤）
        /// </summary>
        public static void Draw(SpriteBatch sb, Rig2DInstance rig, in Rig2DDrawContext ctx) {
            if (rig == null || rig.Definition == null) {
                return;
            }
            Draw(sb, rig, rig.Bones, in ctx);
        }

        /// <summary>
        /// 用另一套骨骼位姿（例如 <see cref="Rig2DPoseTrail"/> 的快照）绘制实例的网格件；件状态仍取实例当前值
        /// </summary>
        public static void Draw(SpriteBatch sb, Rig2DInstance rig, Bone2D[] bones, in Rig2DDrawContext ctx) {
            if (rig == null || rig.Definition == null || bones == null) {
                return;
            }
            DrawIndices(sb, rig, bones, in ctx, rig.SortedMeshes());
        }

        /// <summary>
        /// 绘制给定索引的网格件（顺序即绘制顺序；不可见或层序越界的跳过）。一次调用只切一轮批次
        /// </summary>
        public static void DrawIndices(SpriteBatch sb, Rig2DInstance rig, Bone2D[] bones, in Rig2DDrawContext ctx, ReadOnlySpan<int> indices) {
            if (sb == null || rig == null || rig.Definition == null || bones == null || ctx.Alpha <= 0.001f || indices.Length == 0) {
                return;
            }
            Rig2DPrimitiveRun run = default;
            for (int k = 0; k < indices.Length; k++) {
                Submit(ref run, sb, rig, indices[k], bones, in ctx);
            }
            run.End(sb, in ctx);
        }

        /// <summary>
        /// 求一张网格件本帧的顶点世界位置（含 <see cref="Mesh2DState.PositionOffset"/>），写进 <paramref name="output"/>（长度不够时换新数组）
        /// <br/>判定、贴附装饰、在变形后的某点上叠特效时用；顶点顺序同 <see cref="Mesh2DDef.Vertices"/>
        /// </summary>
        /// <returns>顶点数；网格无效时为 0</returns>
        public static int Deform(Rig2DInstance rig, int meshIndex, Bone2D[] bones, ref Vector2[] output) {
            if (rig?.Definition == null || bones == null || (uint)meshIndex >= (uint)rig.Definition.Meshes.Count) {
                return 0;
            }
            Mesh2DDef def = rig.Definition.Meshes[meshIndex];
            int n = def.VertexCount;
            if (n == 0 || def.SkinStart.Length != n + 1) {
                return 0;
            }
            if (output == null || output.Length < n) {
                output = new Vector2[Math.Max(n, output?.Length * 2 ?? 0)];
            }
            if (trigStamp.Length < bones.Length) {
                int size = Math.Max(bones.Length, trigStamp.Length * 2);
                cosCache = new float[size];
                sinCache = new float[size];
                trigStamp = new int[size];
                stamp = 0;
            }
            if (++stamp == int.MaxValue) {
                Array.Clear(trigStamp);
                stamp = 1;
            }
            float s = Math.Max(rig.Scale, 0.001f);
            float ms = s * rig.MirrorSign;
            Vector2 offset = (uint)meshIndex < (uint)rig.Meshes.Length ? rig.Meshes[meshIndex].PositionOffset : Vector2.Zero;
            int[] start = def.SkinStart;
            int[] skinBone = def.SkinBone;
            float[] skinWeight = def.SkinWeight;
            Vector2[] skinLocal = def.SkinLocal;
            for (int v = 0; v < n; v++) {
                float x = 0f;
                float y = 0f;
                for (int e = start[v]; e < start[v + 1]; e++) {
                    int b = skinBone[e];
                    if ((uint)b >= (uint)bones.Length) {
                        continue;
                    }
                    if (trigStamp[b] != stamp) {
                        trigStamp[b] = stamp;
                        cosCache[b] = MathF.Cos(bones[b].Dir);
                        sinCache[b] = MathF.Sin(bones[b].Dir);
                    }
                    float c = cosCache[b];
                    float sn = sinCache[b];
                    float lx = skinLocal[e].X * s;
                    float ly = skinLocal[e].Y * ms;
                    float w = skinWeight[e];
                    Vector2 p = bones[b].Pos;
                    //轴向 (c, sn)，右侧 (−sn, c)
                    x += w * (p.X + c * lx - sn * ly);
                    y += w * (p.Y + sn * lx + c * ly);
                }
                output[v] = new Vector2(x + offset.X, y + offset.Y);
            }
            return n;
        }

        /// <summary>
        /// 网格件某点的最终着色：环境光 × 设计着色 × 运行时着色 × 压暗 × 不透明度（与整图件同一套规则）
        /// </summary>
        public static Color MeshColor(Mesh2DDef def, in Mesh2DState st, Vector2 world, in Rig2DDrawContext ctx) {
            Color c = def.Unlit ? ctx.UnlitAt() : ctx.LightAt(world);
            if (def.Tint != Color.White) {
                c = Rig2DMath.MultiplyRGBA(c, def.Tint);
            }
            if (st.TintMul != Color.White) {
                c = Rig2DMath.MultiplyRGBA(c, st.TintMul);
            }
            float dark = def.Dark * st.DarkMul;
            if (dark != 1f) {
                c = new Color((byte)MathHelper.Clamp(c.R * dark, 0f, 255f), (byte)MathHelper.Clamp(c.G * dark, 0f, 255f),
                    (byte)MathHelper.Clamp(c.B * dark, 0f, 255f), c.A);
            }
            float alpha = def.Alpha * st.AlphaMul;
            return alpha != 1f ? c * alpha : c;
        }

        /// <summary>
        /// 网格件本帧的世界外接框（不可见或无效返回 <see langword="false"/>）
        /// </summary>
        public static bool MeasureBounds(Rig2DInstance rig, int meshIndex, Bone2D[] bones, out Vector2 min, out Vector2 max) {
            min = new Vector2(float.MaxValue);
            max = new Vector2(float.MinValue);
            int n = Deform(rig, meshIndex, bones, ref positionScratch);
            for (int v = 0; v < n; v++) {
                min = Vector2.Min(min, positionScratch[v]);
                max = Vector2.Max(max, positionScratch[v]);
            }
            return n > 0;
        }

        /// <summary>
        /// 把一张网格件交给批次段（不可见、层序越界、缺贴图或网格无效时跳过）；<see cref="Rig2DRenderer.DrawAll(SpriteBatch, Rig2DInstance, in Rig2DDrawContext)"/> 合并相邻图元用
        /// </summary>
        internal static void Submit(ref Rig2DPrimitiveRun run, SpriteBatch sb, Rig2DInstance rig, int i, Bone2D[] bones, in Rig2DDrawContext ctx) {
            if (i < 0 || i >= rig.Meshes.Length) {
                return;
            }
            ref Mesh2DState st = ref rig.Meshes[i];
            if (!st.Visible || st.SortKey < ctx.LayerMin || st.SortKey > ctx.LayerMax) {
                return;
            }
            Mesh2DDef def = rig.Definition.Meshes[i];
            Texture2D tex = ResolveTexture(rig, i, in st);
            if (tex == null) {
                return;
            }
            int n = Deform(rig, i, bones, ref positionScratch);
            if (n == 0 || def.Indices.Length < 3) {
                return;
            }
            if (vertexScratch.Length < n) {
                vertexScratch = new Rig2DVertex[Math.Max(n, vertexScratch.Length * 2)];
            }
            //没有逐点光照来源时整片同色，省掉逐顶点采样
            bool perVertex = !def.Unlit && (ctx.Light != null || ctx.WorldLighting);
            Color flat = perVertex ? default : MeshColor(def, in st, positionScratch[0], in ctx);
            if (!perVertex && flat.A == 0 && flat.R == 0 && flat.G == 0 && flat.B == 0) {
                return;
            }
            Vector2[] uv = def.TexCoords;
            for (int v = 0; v < n; v++) {
                Vector2 p = positionScratch[v];
                Color color = perVertex ? MeshColor(def, in st, p, in ctx) : flat;
                vertexScratch[v] = new Rig2DVertex(p - ctx.ViewOffset, color, new Vector3(uv[v].X, uv[v].Y, 1f));
            }
            BlendState blend = ctx.RibbonBlendOverride ?? (def.Additive ? BlendState.Additive : BlendState.AlphaBlend);
            run.Submit(sb, in ctx, tex, blend, ctx.Sampler, vertexScratch, n, def.Indices, def.Indices.Length);
        }

        private static Texture2D ResolveTexture(Rig2DInstance rig, int meshIndex, in Mesh2DState st) {
            if (st.TextureOverride != null) {
                return st.TextureOverride.Value;
            }
            return rig.Asset?.MeshTexture(meshIndex);
        }
    }
}
