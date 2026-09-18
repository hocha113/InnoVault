using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Runtime.CompilerServices;

namespace InnoVault
{
    /// <summary>
    /// 一个 <see cref="SpriteBatch"/> 批次的完整 Begin 参数快照：排序模式、混合、采样、深度、光栅、着色器、矩阵
    /// <br/>用 <see cref="SpriteBatchStateExtensions.Capture"/> 取当前批次的参数，<see cref="SpriteBatchStateExtensions.Restart"/> 原样重开，
    /// 不必再手抄一份 Begin 参数，也不会把别人的批次恢复成错误的默认值
    /// <br/>底层读的是 FNA 的私有字段，FNA 改名时 <see cref="Available"/> 变为 false、所有操作退化为无动作
    /// </summary>
    public readonly struct SpriteBatchState
    {
        /// <summary>批次是否处于 Begin 与 End 之间</summary>
        public readonly bool BeginCalled;
        /// <summary>排序模式</summary>
        public readonly SpriteSortMode SortMode;
        /// <summary>混合状态</summary>
        public readonly BlendState Blend;
        /// <summary>采样器</summary>
        public readonly SamplerState Sampler;
        /// <summary>深度模板状态</summary>
        public readonly DepthStencilState DepthStencil;
        /// <summary>光栅状态</summary>
        public readonly RasterizerState Rasterizer;
        /// <summary>自定义着色器（可空）</summary>
        public readonly Effect Effect;
        /// <summary>变换矩阵</summary>
        public readonly Matrix Matrix;
        /// <summary>快照是否有效（底层字段访问可用且批次已 Begin）</summary>
        public bool IsValid => BeginCalled;

        /// <summary>构造快照</summary>
        public SpriteBatchState(bool beginCalled, SpriteSortMode sortMode, BlendState blend, SamplerState sampler, DepthStencilState depthStencil, RasterizerState rasterizer, Effect effect, Matrix matrix) {
            BeginCalled = beginCalled;
            SortMode = sortMode;
            Blend = blend;
            Sampler = sampler;
            DepthStencil = depthStencil;
            Rasterizer = rasterizer;
            Effect = effect;
            Matrix = matrix;
        }

        /// <summary>底层私有字段访问是否可用（首次访问时探测）</summary>
        public static bool Available => SpriteBatchAccess.Available;

        /// <summary>
        /// 若 <paramref name="sb"/> 已 Begin 且处于 <see cref="SpriteSortMode.Immediate"/>，重新应用它自己的渲染状态与精灵着色器
        /// <br/>Immediate 批次只在 Begin 时设置一次着色器，中途任何 <c>Effect.Apply</c>（网格提交、BasicEffect 等）都会让后续精灵用错着色器；
        /// 画完自己的东西后调一次本方法即可恢复。Deferred 批次会在 End 时自行重设，不需要
        /// </summary>
        /// <returns>确实做了恢复返回 true</returns>
        public static bool TryRestoreImmediate(SpriteBatch sb) {
            if (sb == null || !SpriteBatchAccess.Available) {
                return false;
            }
            try {
                if (!SpriteBatchAccess.BeginCalled(sb) || SpriteBatchAccess.SortMode(sb) != SpriteSortMode.Immediate) {
                    return false;
                }
                SpriteBatchAccess.PrepRenderState(sb);
                return true;
            } catch (Exception ex) {
                SpriteBatchAccess.Disable(ex);
                return false;
            }
        }
    }

    /// <summary>
    /// <see cref="SpriteBatch"/> 的批次状态扩展：快照 / 按快照 Begin / 重开
    /// </summary>
    public static class SpriteBatchStateExtensions
    {
        /// <summary>取当前批次的 Begin 参数快照；未 Begin 或底层不可用时 <see cref="SpriteBatchState.IsValid"/> 为 false</summary>
        public static SpriteBatchState Capture(this SpriteBatch sb) {
            if (sb == null || !SpriteBatchAccess.Available) {
                return default;
            }
            try {
                bool begun = SpriteBatchAccess.BeginCalled(sb);
                return new SpriteBatchState(begun,
                    SpriteBatchAccess.SortMode(sb),
                    SpriteBatchAccess.Blend(sb),
                    SpriteBatchAccess.Sampler(sb),
                    SpriteBatchAccess.DepthStencil(sb),
                    SpriteBatchAccess.Rasterizer(sb),
                    SpriteBatchAccess.CustomEffect(sb),
                    SpriteBatchAccess.TransformMatrix(sb));
            } catch (Exception ex) {
                SpriteBatchAccess.Disable(ex);
                return default;
            }
        }

        /// <summary>按快照 Begin（快照无效时什么都不做）</summary>
        public static void Begin(this SpriteBatch sb, in SpriteBatchState state) {
            if (sb == null || !state.IsValid) {
                return;
            }
            sb.Begin(state.SortMode, state.Blend, state.Sampler, state.DepthStencil, state.Rasterizer, state.Effect, state.Matrix);
        }

        /// <summary>
        /// 结束当前批次并按快照重开：典型用法是 <c>var s = sb.Capture(); sb.End(); …自己画…; sb.Restart(s);</c>
        /// <br/>快照无效（原本没 Begin）时不重开
        /// </summary>
        public static void Restart(this SpriteBatch sb, in SpriteBatchState state) {
            if (sb == null) {
                return;
            }
            if (sb.IsBegun()) {
                sb.End();
            }
            sb.Begin(in state);
        }

        /// <summary>批次是否处于 Begin 与 End 之间；底层不可用时返回 false</summary>
        public static bool IsBegun(this SpriteBatch sb) {
            if (sb == null || !SpriteBatchAccess.Available) {
                return false;
            }
            try {
                return SpriteBatchAccess.BeginCalled(sb);
            } catch (Exception ex) {
                SpriteBatchAccess.Disable(ex);
                return false;
            }
        }
    }

    /// <summary>
    /// FNA <see cref="SpriteBatch"/> 私有成员的零开销访问器（.NET 8 <see cref="UnsafeAccessorAttribute"/>）
    /// <br/>成员名来自 FNA <c>SpriteBatch.cs</c>：sortMode / beginCalled / blendState / samplerState / depthStencilState / rasterizerState / transformMatrix / customEffect / PrepRenderState()
    /// </summary>
    internal static class SpriteBatchAccess
    {
        private static bool available = true;
        private static bool probed;

        /// <summary>访问器是否可用：首次调用时用一个临时批次探测，任何缺失成员都会把整组关掉</summary>
        public static bool Available {
            get {
                if (!probed) {
                    Probe();
                }
                return available;
            }
        }

        private static void Probe() {
            probed = true;
            if (Terraria.Main.dedServ || Terraria.Main.spriteBatch == null) {
                //服务器没有 SpriteBatch；保持可用标记，真正调用时再兜底
                return;
            }
            try {
                SpriteBatch sb = Terraria.Main.spriteBatch;
                _ = BeginCalled(sb);
                _ = SortMode(sb);
                _ = Blend(sb);
                _ = Sampler(sb);
                _ = DepthStencil(sb);
                _ = Rasterizer(sb);
                _ = CustomEffect(sb);
                _ = TransformMatrix(sb);
            } catch (Exception ex) {
                Disable(ex);
            }
        }

        internal static void Disable(Exception ex) {
            if (!available) {
                return;
            }
            available = false;
            VaultMod.LoggerError("SpriteBatchAccess", $"[InnoVault] SpriteBatch private member access unavailable, batch snapshot / Immediate restore disabled: {ex.GetType().Name}: {ex.Message}");
        }

        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "beginCalled")]
        private static extern ref bool BeginCalledRef(SpriteBatch sb);
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "sortMode")]
        private static extern ref SpriteSortMode SortModeRef(SpriteBatch sb);
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "blendState")]
        private static extern ref BlendState BlendRef(SpriteBatch sb);
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "samplerState")]
        private static extern ref SamplerState SamplerRef(SpriteBatch sb);
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "depthStencilState")]
        private static extern ref DepthStencilState DepthStencilRef(SpriteBatch sb);
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "rasterizerState")]
        private static extern ref RasterizerState RasterizerRef(SpriteBatch sb);
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "transformMatrix")]
        private static extern ref Matrix TransformMatrixRef(SpriteBatch sb);
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "customEffect")]
        private static extern ref Effect CustomEffectRef(SpriteBatch sb);
        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "PrepRenderState")]
        private static extern void PrepRenderStateCall(SpriteBatch sb);

        public static bool BeginCalled(SpriteBatch sb) => BeginCalledRef(sb);
        public static SpriteSortMode SortMode(SpriteBatch sb) => SortModeRef(sb);
        public static BlendState Blend(SpriteBatch sb) => BlendRef(sb);
        public static SamplerState Sampler(SpriteBatch sb) => SamplerRef(sb);
        public static DepthStencilState DepthStencil(SpriteBatch sb) => DepthStencilRef(sb);
        public static RasterizerState Rasterizer(SpriteBatch sb) => RasterizerRef(sb);
        public static Matrix TransformMatrix(SpriteBatch sb) => TransformMatrixRef(sb);
        public static Effect CustomEffect(SpriteBatch sb) => CustomEffectRef(sb);
        public static void PrepRenderState(SpriteBatch sb) => PrepRenderStateCall(sb);
    }
}
