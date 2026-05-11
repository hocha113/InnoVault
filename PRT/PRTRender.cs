using InnoVault.RenderHandles;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;

namespace InnoVault.PRT
{
    /// <summary>
    /// PRT粒子的渲染器
    /// </summary>
    public sealed class PRTRender : RenderHandle
    {
        #region Data
        private static readonly PRTRenderLayer[] _allLayers = (PRTRenderLayer[])Enum.GetValues(typeof(PRTRenderLayer));
        private static readonly int _layerCount = _allLayers.Length;
        private static readonly int _modeCount = PRTLoader.allDrawModes.Length;

        /// <inheritdoc/>
        public override float Weight => 1f;

        /// <summary>
        /// 二维扁平桶按 <c>layerIndex * modeCount + modeIndex</c> 索引，存放无 shader 的粒子
        /// </summary>
        private static List<BasePRT>[] _modeBuckets;
        /// <summary>
        /// 每个 <see cref="PRTRenderLayer"/> 一份的 shader 粒子桶按 <c>layerIndex</c> 索引
        /// </summary>
        private static List<BasePRT>[] _shaderBuckets;
        /// <summary>
        /// 桶是否需要重建当 <see cref="PRTLoader.PRT_InGame_World_Inds"/> 发生变化或粒子状态改变后置 <see langword="true"/>
        /// </summary>
        private static bool _bucketsDirty = true;
        #endregion

        #region 生命周期
        /// <summary>
        /// 由 <see cref="PRTLoader.Load"/> 调用：初始化所有桶并把 <see cref="PRTLoader"/> 中的兼容字段
        /// 指向 <see cref="PRTRenderLayer.BeforeInfernoRings"/> 层对应的桶
        /// </summary>
        internal static void Initialize() {
            _modeBuckets = new List<BasePRT>[_layerCount * _modeCount];
            _shaderBuckets = new List<BasePRT>[_layerCount];
            for (int i = 0; i < _modeBuckets.Length; i++) {
                _modeBuckets[i] = [];
            }
            for (int i = 0; i < _shaderBuckets.Length; i++) {
                _shaderBuckets[i] = [];
            }

            //把 PRTLoader 中的旧公开字段指向"默认层"对应的桶，保持向后兼容
            //旧代码读取 PRT_AlphaBlend_Draw 等字段时仍能拿到当前帧 BeforeInfernoRings 层的粒子集合
            int baseIdx = (int)PRTRenderLayer.BeforeInfernoRings * _modeCount;
            PRTLoader.PRT_AlphaBlend_Draw = _modeBuckets[baseIdx + (int)PRTDrawModeEnum.AlphaBlend];
            PRTLoader.PRT_AdditiveBlend_Draw = _modeBuckets[baseIdx + (int)PRTDrawModeEnum.AdditiveBlend];
            PRTLoader.PRT_NonPremultiplied_Draw = _modeBuckets[baseIdx + (int)PRTDrawModeEnum.NonPremultiplied];
            PRTLoader.PRT_HasShader_Draw = _shaderBuckets[(int)PRTRenderLayer.BeforeInfernoRings];

            _bucketsDirty = true;
        }

        /// <summary>
        /// 由 <see cref="PRTLoader.InitializeWorldPRT"/> 调用：清空所有桶，但保留 List 实例不重新分配
        /// </summary>
        internal static void Reset() {
            if (_modeBuckets != null) {
                for (int i = 0; i < _modeBuckets.Length; i++) {
                    _modeBuckets[i]?.Clear();
                }
            }
            if (_shaderBuckets != null) {
                for (int i = 0; i < _shaderBuckets.Length; i++) {
                    _shaderBuckets[i]?.Clear();
                }
            }
            _bucketsDirty = true;
        }

        /// <summary>
        /// 由 <see cref="PRTLoader.Unload"/> 调用：释放所有桶引用
        /// </summary>
        internal static void Dispose() {
            _modeBuckets = null;
            _shaderBuckets = null;
        }

        /// <summary>
        /// 标记当前桶为脏，下一次 <see cref="DrawLayer"/> 调用时会重建
        /// 由 <see cref="PRTLoader"/> 在更新流程末尾调用
        /// </summary>
        internal static void MarkBucketsDirty() => _bucketsDirty = true;
        #endregion

        #region 桶管理
        /// <summary>
        /// 重新扫描 <see cref="PRTLoader.PRT_InGame_World_Inds"/> 把粒子重新分配到对应的（layer, mode）桶或 shader 桶
        /// 仅在 <see cref="_bucketsDirty"/> 为真时执行
        /// </summary>
        /// <remarks>
        /// 自带 null 守卫：未初始化或已 <see cref="Dispose"/> 时直接置位 dirty=false 后返回，
        /// 避免未来误从其它路径调用此私有方法时崩溃
        /// </remarks>
        private static void RebuildBuckets() {
            if (_modeBuckets == null || _shaderBuckets == null) {
                _bucketsDirty = false;
                return;
            }
            //先清空再重填，避免残留引用
            for (int i = 0; i < _modeBuckets.Length; i++) {
                _modeBuckets[i].Clear();
            }
            for (int i = 0; i < _shaderBuckets.Length; i++) {
                _shaderBuckets[i].Clear();
            }

            List<BasePRT> inds = PRTLoader.PRT_InGame_World_Inds;
            if (inds == null) {
                _bucketsDirty = false;
                return;
            }

            for (int i = 0; i < inds.Count; i++) {
                BasePRT particle = inds[i];
                if (particle == null || !particle.active) {
                    continue;
                }
                if (particle.PRTLayersMode == PRTLayersModeEnum.NoDraw) {
                    continue;
                }

                int layerIdx = (int)particle.RenderLayer;
                if ((uint)layerIdx >= (uint)_layerCount) {
                    //RenderLayer 越界时回落到默认层，避免崩溃
                    layerIdx = (int)PRTRenderLayer.BeforeInfernoRings;
                }

                if (particle.shader != null) {
                    _shaderBuckets[layerIdx].Add(particle);
                }
                else {
                    int modeIdx = (int)particle.PRTDrawMode;
                    if ((uint)modeIdx >= (uint)_modeCount) {
                        modeIdx = (int)PRTDrawModeEnum.AlphaBlend;
                    }
                    _modeBuckets[layerIdx * _modeCount + modeIdx].Add(particle);
                }
            }

            _bucketsDirty = false;
        }

        /// <summary>
        /// 检查指定层是否完全没有粒子（包括所有混合模式与 shader 粒子）
        /// 用于让 <see cref="PRTRender"/> 在空层提前返回，避免不必要的 SpriteBatch 状态切换
        /// </summary>
        /// <param name="layer">需要检查的渲染层级</param>
        /// <returns>没有任何粒子时返回<see langword="true"/></returns>
        public static bool IsLayerEmpty(PRTRenderLayer layer) {
            if (_modeBuckets == null || _shaderBuckets == null) {
                return true;
            }
            if (_bucketsDirty) {
                RebuildBuckets();
            }
            int baseIdx = (int)layer * _modeCount;
            for (int m = 0; m < _modeCount; m++) {
                if (_modeBuckets[baseIdx + m].Count > 0) {
                    return false;
                }
            }
            return _shaderBuckets[(int)layer].Count == 0;
        }
        #endregion

        #region 渲染入口
        /// <summary>
        /// 渲染一个层级中的所有粒子调用约定：进入时 <see cref="SpriteBatch"/> 必须为非活跃状态，离开时仍为非活跃状态
        /// 由本类各钩子根据自身 SpriteBatch 状态契约负责进出转换
        /// </summary>
        /// <remarks>
        /// 内层 <c>BeginDrawingWithMode</c>/<c>End</c> 配对使用 <see langword="try-finally"/> 保护，
        /// 即使某个粒子的 <see cref="BasePRT.PreDraw"/>/<see cref="BasePRT.PostDraw"/> 抛出异常，
        /// <see cref="SpriteBatch"/> 也会回到 non-Active 状态，避免污染下游 RenderHandle
        /// </remarks>
        /// <param name="spriteBatch">当前帧的 SpriteBatch</param>
        /// <param name="layer">要绘制的层级</param>
        public static void DrawLayer(SpriteBatch spriteBatch, PRTRenderLayer layer) {
            if (_modeBuckets == null || _shaderBuckets == null) {
                return;
            }
            if (_bucketsDirty) {
                RebuildBuckets();
            }

            int baseIdx = (int)layer * _modeCount;
            for (int m = 0; m < _modeCount; m++) {
                List<BasePRT> bucket = _modeBuckets[baseIdx + m];
                if (bucket.Count <= 0) {
                    continue;
                }

                BeginDrawingWithMode((PRTDrawModeEnum)m, spriteBatch);
                try {
                    for (int i = 0; i < bucket.Count; i++) {
                        BasePRT particle = bucket[i];
                        //粒子可能在前一个粒子的 PreDraw/PostDraw 中被 Kill，桶仍持有引用——这里二次校验避免画"已死"粒子
                        if (particle == null || !particle.active) {
                            continue;
                        }
                        PRTInstanceDraw(spriteBatch, particle);
                    }
                } finally {
                    spriteBatch.End();
                }
            }

            List<BasePRT> shaderBucket = _shaderBuckets[(int)layer];
            if (shaderBucket.Count > 0) {
                HandleShaderPRTDrawList(spriteBatch, shaderBucket);
            }
        }

        /// <summary>
        /// 一次性绘制所有层级的兼容入口供旧代码（或外部 mod）调用 <see cref="PRTLoader.Draw"/> 时仍可正常工作
        /// 调用约定：进入时 <see cref="SpriteBatch"/> 处于活跃状态，离开时仍为活跃状态（与旧 <c>PRTLoader.Draw</c> 一致）
        /// </summary>
        /// <remarks>
        /// 桶/世界列表任一未初始化或为空时直接 <see langword="return"/>，避免做出无意义的 End/Begin 配对，
        /// 也避免在卸载窗口期触碰已置 null 的状态
        /// 包裹 <c>End</c>→<c>DrawLayer</c>→<c>Begin</c> 的 <see langword="try-finally"/> 保证就算所有层都崩了，
        /// 进入时活跃的 <see cref="SpriteBatch"/> 在退出前也会被重新 <c>Begin</c>，维持调用方的状态契约
        /// </remarks>
        /// <param name="spriteBatch">当前帧的 SpriteBatch</param>
        public static void DrawAll(SpriteBatch spriteBatch) {
            if (_modeBuckets == null || _shaderBuckets == null
                || PRTLoader.PRT_InGame_World_Inds == null
                || PRTLoader.PRT_InGame_World_Inds.Count <= 0) {
                return;
            }

            spriteBatch.End();
            try {
                for (int l = 0; l < _layerCount; l++) {
                    DrawLayer(spriteBatch, (PRTRenderLayer)l);
                }
            } finally {
                spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState
                    , DepthStencilState.None, Main.Rasterizer, null, Main.Transform);
            }
        }
        #endregion

        #region 单粒子绘制与混合状态
        /// <summary>
        /// 默认绘制把粒子按其 <see cref="BasePRT.Frame"/> / <see cref="BasePRT.Color"/> 等字段画到屏幕上
        /// 不在外部直接调用，由 <see cref="PRTInstanceDraw"/> 在 <see cref="BasePRT.PreDraw"/> 返回 <see langword="true"/> 时触发
        /// </summary>
        private static void DefaultDraw(SpriteBatch spriteBatch, BasePRT particle) {
            Texture2D value = PRTLoader.PRT_IDToTexture[particle.ID];
            if (particle.Frame == default) {
                particle.Frame = new Rectangle(0, 0, value.Width, value.Height);
            }
            spriteBatch.Draw(value, particle.Position - Main.screenPosition, particle.Frame, particle.Color
                , particle.Rotation, particle.Frame.Size() * 0.5f, particle.Scale, SpriteEffects.None, 0f);
        }

        /// <summary>
        /// 完整处理一个粒子的绘制，包括 GlobalPRT 的 PreDraw / PostDraw 钩子调用
        /// </summary>
        /// <param name="spriteBatch">当前帧的 SpriteBatch</param>
        /// <param name="particle">要绘制的粒子实例</param>
        public static void PRTInstanceDraw(SpriteBatch spriteBatch, BasePRT particle) {
            bool result = true;
            foreach (var global in PRTLoader.HookPreDrawPRT.Enumerate()) {
                if (!global.PreDrawPRT(spriteBatch, particle)) {
                    result = false;
                }
            }

            if (result && particle.PreDraw(spriteBatch)) {
                DefaultDraw(spriteBatch, particle);
            }

            particle.PostDraw(spriteBatch);

            foreach (var global in PRTLoader.HookPostDrawPRT.Enumerate()) {
                global.PostDrawPRT(spriteBatch, particle);
            }
        }

        /// <summary>
        /// 根据 <see cref="PRTDrawModeEnum"/> 获取对应的 <see cref="BlendState"/>
        /// </summary>
        /// <param name="drawMode">粒子的绘制模式</param>
        /// <returns>对应的 BlendState 实例</returns>
        public static BlendState GetBlendStateFor(PRTDrawModeEnum drawMode) {
            return drawMode switch {
                PRTDrawModeEnum.AdditiveBlend => BlendState.Additive,
                PRTDrawModeEnum.NonPremultiplied => BlendState.NonPremultiplied,
                PRTDrawModeEnum.AlphaBlend => BlendState.AlphaBlend,
                _ => BlendState.AlphaBlend,
            };
        }

        /// <summary>
        /// 根据指定的绘制模式 <see cref="PRTDrawModeEnum"/>，为 <see cref="SpriteBatch"/> 设置适当的渲染状态并 Begin
        /// </summary>
        /// <param name="drawMode">绘制模式枚举</param>
        /// <param name="spriteBatch">用于进行绘制操作的 SpriteBatch</param>
        /// <param name="spriteSortMode">排序模式，默认为 <see cref="SpriteSortMode.Deferred"/></param>
        public static void BeginDrawingWithMode(PRTDrawModeEnum drawMode, SpriteBatch spriteBatch, SpriteSortMode spriteSortMode = SpriteSortMode.Deferred) {
            var rasterizer = Main.Rasterizer;
            rasterizer.ScissorTestEnable = true;
            Main.instance.GraphicsDevice.RasterizerState.ScissorTestEnable = true;
            Main.instance.GraphicsDevice.ScissorRectangle = new Rectangle(0, 0, Main.screenWidth, Main.screenHeight);

            switch (drawMode) {
                case PRTDrawModeEnum.AlphaBlend:
                    spriteBatch.Begin(spriteSortMode, BlendState.AlphaBlend, Main.DefaultSamplerState
                        , DepthStencilState.None, rasterizer, null, Main.GameViewMatrix.TransformationMatrix);
                    break;
                case PRTDrawModeEnum.AdditiveBlend:
                    spriteBatch.Begin(spriteSortMode, BlendState.Additive, SamplerState.PointClamp
                        , DepthStencilState.Default, rasterizer, null, Main.GameViewMatrix.TransformationMatrix);
                    break;
                case PRTDrawModeEnum.NonPremultiplied:
                    spriteBatch.Begin(spriteSortMode, BlendState.NonPremultiplied, SamplerState.PointClamp
                        , DepthStencilState.Default, rasterizer, null, Main.GameViewMatrix.TransformationMatrix);
                    break;
            }
        }

        /// <summary>
        /// 绘制一组使用 Shader 的粒子按 (shader, drawMode) 二级分组以减少状态切换次数
        /// 调用约定：进入时 <see cref="SpriteBatch"/> 必须为非活跃状态，离开时仍为非活跃状态
        /// </summary>
        /// <remarks>
        /// 内层 <c>BeginDrawingWithMode</c>/<c>End</c> 配对使用 <see langword="try-finally"/> 保护，
        /// 防止粒子绘制中途抛异常时把 <see cref="SpriteBatch"/> 留在 Active 状态污染下游
        /// </remarks>
        /// <param name="spriteBatch">画布实例</param>
        /// <param name="particles">传入的粒子集合，所有粒子的 <see cref="BasePRT.shader"/> 都不为 null</param>
        public static void HandleShaderPRTDrawList(SpriteBatch spriteBatch, List<BasePRT> particles) {
            //按 shader 实例分组，这是最高代价的状态切换
            var groupedByShader = particles.GroupBy(p => p.shader);

            foreach (var shaderGroup in groupedByShader) {
                //在每个 shader 组内部再按绘制模式分组
                var groupedByDrawMode = shaderGroup.GroupBy(p => p.PRTDrawMode);

                foreach (var drawModeGroup in groupedByDrawMode) {
                    BeginDrawingWithMode(drawModeGroup.Key, spriteBatch, SpriteSortMode.Immediate);
                    try {
                        shaderGroup.Key?.Apply(null);

                        foreach (BasePRT particle in drawModeGroup) {
                            //同样防御桶里残留 active=false 的脏引用
                            if (particle == null || !particle.active) {
                                continue;
                            }
                            PRTInstanceDraw(spriteBatch, particle);
                        }
                    } finally {
                        spriteBatch.End();
                    }
                }
            }
        }
        #endregion

        #region 进入时 SpriteBatch 处于活跃状态
        /// <inheritdoc/>
        public override void DrawBeforeTiles(SpriteBatch spriteBatch, GraphicsDevice graphicsDevice, RenderTarget2D screenSwap) {
            //空层提前返回，避免无谓地折腾 SpriteBatch 状态
            if (IsLayerEmpty(PRTRenderLayer.BeforeTiles)) {
                return;
            }

            spriteBatch.End();
            DrawLayer(spriteBatch, PRTRenderLayer.BeforeTiles);
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState
                , DepthStencilState.None, Main.Rasterizer, null, Main.Transform);
        }

        /// <inheritdoc/>
        public override void DrawBeforeInfernoRings(SpriteBatch spriteBatch, GraphicsDevice graphicsDevice, RenderTarget2D screenSwap) {
            if (IsLayerEmpty(PRTRenderLayer.BeforeInfernoRings)) {
                return;
            }

            spriteBatch.End();
            DrawLayer(spriteBatch, PRTRenderLayer.BeforeInfernoRings);
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState
                , DepthStencilState.None, Main.Rasterizer, null, Main.Transform);
        }
        #endregion

        #region 进入时 SpriteBatch 未处于活跃状态，离开时必须保持 End
        /// <inheritdoc/>
        public override void DrawAfterTiles(SpriteBatch spriteBatch, GraphicsDevice graphicsDevice, RenderTarget2D screenSwap) {
            if (IsLayerEmpty(PRTRenderLayer.AfterTiles)) {
                return;
            }
            DrawLayer(spriteBatch, PRTRenderLayer.AfterTiles);
        }

        /// <inheritdoc/>
        public override void DrawBeforePlayers(SpriteBatch spriteBatch, GraphicsDevice graphicsDevice, RenderTarget2D screenSwap) {
            if (IsLayerEmpty(PRTRenderLayer.BeforePlayers)) {
                return;
            }
            DrawLayer(spriteBatch, PRTRenderLayer.BeforePlayers);
        }

        /// <inheritdoc/>
        public override void DrawAfterPlayers(SpriteBatch spriteBatch, GraphicsDevice graphicsDevice, RenderTarget2D screenSwap) {
            if (IsLayerEmpty(PRTRenderLayer.AfterPlayers)) {
                return;
            }
            DrawLayer(spriteBatch, PRTRenderLayer.AfterPlayers);
        }
        #endregion
    }
}
