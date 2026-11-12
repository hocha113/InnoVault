using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using Terraria;
using Terraria.Graphics.Capture;

namespace InnoVault.RenderHandles
{
    /// <summary>
    /// 天空阶段（<see cref="RenderHandle.DrawSky"/>）收到的一片深度切片<br/>
    /// 原版把背景按深度由远到近分片：每画一层背景之前调用 <c>SkyManager.DrawToDepth</c>，全部背景层画完后调用
    /// <c>SkyManager.DrawRemainingDepth</c>；每一片先画完所有活动的 <c>CustomSky</c>，再分发给天空阶段<br/>
    /// 地表背景与地狱背景各有一整套切片：屏幕底边够得着地狱时，同一帧里两套都会出现，用 <see cref="Underworld"/> 区分
    /// </summary>
    public readonly struct SkySlice
    {
        //进入本片时原版批次的快照，RestoreBatch 照它重开
        private readonly SpriteBatchState entryState;
        //构造时批次快照是否可用（FNA 私有字段改名后不可用，此时按原版背景那一段的参数重开）
        private readonly bool entryCaptured;

        /// <summary>这一片的深度下界（靠近镜头的一端）</summary>
        public float MinDepth { get; }

        /// <summary>这一片的深度上界（远离镜头的一端）</summary>
        public float MaxDepth { get; }

        /// <summary>这一片是否属于地狱背景那一套切片</summary>
        public bool Underworld { get; }

        /// <summary>
        /// 是否处于相机模式的截图绘制中。截图那条路的屏幕参数、批次与正常帧都不同，
        /// <see cref="RealScreenPosition"/> 在这里不可靠，天空类效果一般直接跳过
        /// </summary>
        public bool Capturing { get; }

        /// <summary>
        /// 真实的相机左上角（世界坐标）。原版在背景这一段里把 <see cref="Main.screenPosition"/> 加上了背景缩放的平移，
        /// 这里已经减回去，与世界层用的相机一致
        /// </summary>
        public Vector2 RealScreenPosition { get; }

        /// <summary>
        /// 当前渲染目标的像素尺寸。原版在背景这一段里把 <see cref="Main.screenWidth"/> / <see cref="Main.screenHeight"/>
        /// 除以了背景缩放，屏幕宽过 1920 或高过 1200 时它们不是真实尺寸
        /// </summary>
        public Vector2 RealScreenSize { get; }

        /// <summary>
        /// 按当前绘制状态构造一片切片：记下批次快照、真实相机与截图状态<br/>
        /// 框架分发天空阶段时自行构造；自己从 <c>CustomSky.Draw</c> 转发时（例如天空阶段不可用时的兜底天空）也可以用它得到同一套量
        /// </summary>
        /// <param name="spriteBatch">当前的背景批次，一般就是 <see cref="Main.spriteBatch"/></param>
        /// <param name="minDepth">深度下界</param>
        /// <param name="maxDepth">深度上界</param>
        /// <param name="underworld">是否属于地狱背景，可取 <see cref="RenderHandleLoader.InUnderworldBackground"/></param>
        public SkySlice(SpriteBatch spriteBatch, float minDepth, float maxDepth, bool underworld) {
            MinDepth = minDepth;
            MaxDepth = maxDepth;
            Underworld = underworld;
            Capturing = CaptureManager.Instance.IsCapturing;
            RealScreenPosition = Main.screenPosition - Main.BackgroundViewMatrix.Translation;
            Viewport viewport = Main.instance.GraphicsDevice.Viewport;
            RealScreenSize = new Vector2(viewport.Width, viewport.Height);
            entryCaptured = SpriteBatchState.Available;
            entryState = spriteBatch.Capture();
        }

        /// <summary>
        /// 是否是最远的一片（上界为 <see cref="float.MaxValue"/>）：画在这里的东西压在原版天穹、星与日月之上，
        /// 原版的远山、云等背景层照常画在它上面
        /// </summary>
        public bool Farthest => MaxDepth == float.MaxValue;

        /// <summary>
        /// 是否是最后一片（跨过深度 0，即 <c>DrawRemainingDepth</c> 那一片）：画在这里的东西盖住这一套里的全部原版背景层<br/>
        /// 别的代码以全部深度调用 <c>SkyManager.Draw</c> 时只有一片，它同时是 <see cref="Farthest"/> 与 <see cref="Final"/>
        /// </summary>
        public bool Final => MinDepth < 0f && MaxDepth >= 0f;

        /// <summary>整个屏幕是否都在地表以下（原版在这个条件下不再画天穹与日月）</summary>
        public bool ScreenFullyUnderground => RealScreenPosition.Y >= Main.worldSurface * 16.0 + 16.0;

        /// <summary>
        /// 进入本片时原版背景批次的变换矩阵。原版背景层用它画（只有背景缩放，重力反转时带竖翻），
        /// 想让自己的东西与原版背景层同一套坐标时把它传给 <see cref="BeginBatch"/>
        /// </summary>
        public Matrix BackgroundMatrix => entryState.IsValid ? entryState.Matrix : VanillaBackgroundMatrix();

        //进入本片时批次是否已开（快照可用且有效），分发方据此核对实例返回时有没有还原批次
        internal bool EntryBegun => entryState.IsValid;

        /// <summary>
        /// 结束当前批次并开一个自己的批次。深度模板固定为 <see cref="DepthStencilState.None"/>，光栅固定为 <see cref="RasterizerState.CullNone"/><br/>
        /// 可以多次调用来切换状态；全部画完后必须调用 <see cref="RestoreBatch"/>
        /// </summary>
        /// <param name="spriteBatch">天空阶段传入的批次</param>
        /// <param name="sortMode">排序模式，带着色器画整屏四边形时用 <see cref="SpriteSortMode.Immediate"/></param>
        /// <param name="blendState">混合状态，默认 <see cref="BlendState.AlphaBlend"/></param>
        /// <param name="samplerState">采样状态，默认 <see cref="SamplerState.LinearClamp"/></param>
        /// <param name="effect">自定义着色器，可空</param>
        /// <param name="transformMatrix">变换矩阵，默认单位矩阵，即以 <see cref="RealScreenSize"/> 为范围的屏幕像素坐标</param>
        public void BeginBatch(SpriteBatch spriteBatch, SpriteSortMode sortMode = SpriteSortMode.Deferred, BlendState blendState = null
            , SamplerState samplerState = null, Effect effect = null, Matrix? transformMatrix = null) {
            EndIfBegun(spriteBatch);
            spriteBatch.Begin(sortMode, blendState ?? BlendState.AlphaBlend, samplerState ?? SamplerState.LinearClamp
                , DepthStencilState.None, RasterizerState.CullNone, effect, transformMatrix ?? Matrix.Identity);
        }

        /// <summary>
        /// 结束自己的批次，把批次还原成进入本片时的状态（原版背景那一段的排序、混合、采样与矩阵）<br/>
        /// 还原用的是进入时的快照，所以缩放不为 1、重力反转、截图这几条路上后面的背景层都不会偏
        /// </summary>
        /// <param name="spriteBatch">天空阶段传入的批次</param>
        public void RestoreBatch(SpriteBatch spriteBatch) {
            if (entryState.IsValid) {
                spriteBatch.Restart(in entryState);
                return;
            }

            EndIfBegun(spriteBatch);
            //快照可用却无效，说明进入时批次本来就没开，保持结束状态；快照不可用时按原版背景那一段的参数重开
            if (!entryCaptured) {
                spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp
                    , DepthStencilState.None, Main.Rasterizer, null, VanillaBackgroundMatrix());
            }
        }

        /// <summary>
        /// 把 <see cref="Main.screenPosition"/> / <see cref="Main.screenWidth"/> / <see cref="Main.screenHeight"/>
        /// 暂时换成真实的相机与渲染目标尺寸，<c>using</c> 结束时换回原版在背景这一段里用的值<br/>
        /// 天空里要调用按这三个量取屏幕范围的现成绘制代码时使用
        /// </summary>
        public RealScreenScope EnterRealScreen() => new(RealScreenPosition, RealScreenSize);

        //原版 DoDraw 里背景那一段的批次矩阵：背景缩放减去缩放带来的平移，重力反转时平移的竖向取反
        //对照 Terraria/Main.cs DoDraw：'transformationMatrix.Translation -= BackgroundViewMatrix.ZoomMatrix.Translation * ...'
        internal static Matrix VanillaBackgroundMatrix() {
            Matrix matrix = Main.BackgroundViewMatrix.TransformationMatrix;
            float flip = Main.BackgroundViewMatrix.Effects.HasFlag(SpriteEffects.FlipVertically) ? -1f : 1f;
            matrix.Translation -= Main.BackgroundViewMatrix.ZoomMatrix.Translation * new Vector3(1f, flip, 1f);
            return matrix;
        }

        private static void EndIfBegun(SpriteBatch spriteBatch) {
            if (spriteBatch.GetBeginCalledBool()) {
                spriteBatch.End();
            }
        }

        /// <summary>
        /// <see cref="EnterRealScreen"/> 的作用域：构造时换成真实屏幕量，释放时换回
        /// </summary>
        public readonly struct RealScreenScope : IDisposable
        {
            private readonly Vector2 position;
            private readonly int width;
            private readonly int height;
            private readonly bool entered;

            internal RealScreenScope(Vector2 realPosition, Vector2 realSize) {
                position = Main.screenPosition;
                width = Main.screenWidth;
                height = Main.screenHeight;
                entered = true;
                Main.screenPosition = realPosition;
                Main.screenWidth = (int)realSize.X;
                Main.screenHeight = (int)realSize.Y;
            }

            /// <summary>换回进入作用域之前的屏幕量</summary>
            public void Dispose() {
                if (!entered) {
                    return;
                }
                Main.screenPosition = position;
                Main.screenWidth = width;
                Main.screenHeight = height;
            }
        }
    }
}
