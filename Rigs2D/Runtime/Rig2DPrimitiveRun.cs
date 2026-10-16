using Microsoft.Xna.Framework.Graphics;

namespace InnoVault.Rigs2D.Runtime
{
    /// <summary>
    /// 三角形图元（带状件、网格件）的一段批次：段内连续的图元共用一轮 Immediate 批次，混合态或采样态变了才重开；
    /// <see cref="End"/> 把调用方的批次恢复成 Deferred / AlphaBlend / <see cref="Rig2DDrawContext.Sampler"/>
    /// <br/>图元用 <see cref="GraphicsDevice.DrawUserIndexedPrimitives{T}(PrimitiveType, T[], int, int, short[], int, int)"/> 提交，
    /// 顶点变换取 SpriteBatch 在 Immediate 模式 <c>Begin</c> 时装好的矩阵（<see cref="Rig2DDrawContext.BatchMatrix"/>）
    /// </summary>
    internal struct Rig2DPrimitiveRun
    {
        private bool opened;
        private BlendState blend;
        private SamplerState sampler;

        /// <summary>
        /// 本段是否已经把调用方的批次切成了 Immediate
        /// </summary>
        public readonly bool Opened => opened;

        /// <summary>
        /// 提交一组三角形；首次提交或混合 / 采样态变化时重开 Immediate 批次（此前排队的精灵随 <c>End</c> 冲出，层序保持）
        /// </summary>
        public void Submit(SpriteBatch sb, in Rig2DDrawContext ctx, Texture2D texture, BlendState blendState, SamplerState samplerState,
            Rig2DVertex[] vertices, int vertexCount, short[] indices, int indexCount) {
            if (!opened || !ReferenceEquals(blendState, blend) || !ReferenceEquals(samplerState, sampler)) {
                sb.End();
                sb.Begin(SpriteSortMode.Immediate, blendState, samplerState, DepthStencilState.None, ctx.Rasterizer, null, ctx.BatchMatrix);
                opened = true;
                blend = blendState;
                sampler = samplerState;
            }
            GraphicsDevice gd = sb.GraphicsDevice;
            gd.Textures[0] = texture;
            gd.SamplerStates[0] = samplerState;
            gd.RasterizerState = Rig2DRibbonRenderer.CullNoneLike(ctx.Rasterizer);
            gd.DrawUserIndexedPrimitives(PrimitiveType.TriangleList, vertices, 0, vertexCount, indices, 0, indexCount / 3);
        }

        /// <summary>
        /// 结束本段：开过 Immediate 就恢复成 Deferred 批次
        /// </summary>
        public void End(SpriteBatch sb, in Rig2DDrawContext ctx) {
            if (!opened) {
                return;
            }
            opened = false;
            blend = null;
            sampler = null;
            sb.End();
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, ctx.Sampler, DepthStencilState.None, ctx.Rasterizer, null, ctx.BatchMatrix);
        }
    }
}
