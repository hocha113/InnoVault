using InnoVault.Models3D.Wavefront;
using InnoVault.RenderHandles;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using Terraria;

namespace InnoVault.Models3D.Runtime
{
    /// <summary>
    /// 3D 模型渲染器
    /// <br/>挂载在 <see cref="RenderHandle"/> 的多个分层钩子上，按 <see cref="Model3DLayer"/> 绘制 OBJ 模型
    /// <br/>对外暴露的便捷 API 见 <see cref="Submit"/> / <see cref="RegisterPersistent"/> / <see cref="Draw(VaultObjModel, Vector2, Vector3, Vector3, Model3DLayer, Color?)"/>
    /// </summary>
    public sealed class Model3DRenderer : RenderHandle
    {
        /// <inheritdoc/>
        public override float Weight => 5f; // 在 PRT 之后，避免覆盖粒子的画布状态

        /// <summary>
        /// 当前活跃的渲染器单例（由 <see cref="VaultRegister"/> 设置）
        /// </summary>
        public static Model3DRenderer Instance { get; private set; }

        /// <summary>
        /// 全局默认光照配置，所有未指定 <see cref="Model3DInstance.LightingOverride"/> 的实例都会使用它
        /// <br/>默认值与 <see cref="BasicEffect.EnableDefaultLighting"/> 一致
        /// <br/>开发者可以在 mod 加载阶段或运行时整体替换/修改，实现昼夜变化等全局效果
        /// </summary>
        public static Model3DLightingConfig GlobalLighting { get; set; } = Model3DLightingConfig.CreateDefault();

        /// <summary>
        /// 每实例光照解析事件，在每个可见实例绘制前触发
        /// <br/>订阅者拿到的 config 是 <see cref="GlobalLighting"/> 或 <see cref="Model3DInstance.LightingOverride"/> 的拷贝，
        /// 可以放心 mutate；未来扩展点光、阴影等新能力时会在此处加更多解析事件
        /// <br/>典型用法："让所有 boss 的主光朝向玩家光标"、"按 tile light 调整 ambient"等
        /// </summary>
        public static event Model3DLightingResolver ResolveLighting;

        // 每帧/每实例复用，避免分配；写入前从 source 拷贝，写入后由订阅者按需修改
        private readonly Model3DLightingConfig _scratchLighting = new Model3DLightingConfig();

        // 每层一个临时提交桶；每帧绘制完即清空
        private readonly List<Model3DInstance>[] _transientByLayer;
        // 每层一个常驻实例桶；不会在帧间清空
        private readonly List<Model3DInstance>[] _persistentByLayer;
        private readonly object _persistentLock = new object();

        private BasicEffect _effect;
        private bool _effectInitFailed;

        // Models3D 自己的渲染目标，必须带 Depth24 才能真正使用深度测试
        private RenderTarget2D _model3DRT;
        private bool _rtInitFailed;

        // 复用的临时桶，避免每帧分配
        private readonly List<Model3DInstance> _opaqueScratch = new List<Model3DInstance>(32);
        private readonly List<Model3DInstance> _transparentScratch = new List<Model3DInstance>(8);

        // 保存绘制前的 GraphicsDevice 状态，绘制后恢复
        private struct SavedState
        {
            public BlendState Blend;
            public DepthStencilState Depth;
            public RasterizerState Rasterizer;
            public SamplerState Sampler0;
        }

        /// <summary>
        /// 构造渲染器，初始化各层的桶
        /// </summary>
        public Model3DRenderer() {
            int layerCount = Enum.GetValues(typeof(Model3DLayer)).Length;
            _transientByLayer = new List<Model3DInstance>[layerCount];
            _persistentByLayer = new List<Model3DInstance>[layerCount];
            for (int i = 0; i < layerCount; i++) {
                _transientByLayer[i] = new List<Model3DInstance>();
                _persistentByLayer[i] = new List<Model3DInstance>();
            }
        }

        /// <inheritdoc/>
        protected override void VaultRegister() {
            base.VaultRegister();
            Instance = this;
        }

        /// <inheritdoc/>
        public override void OnResolutionChanged(Vector2 screenSize) {
            // 屏幕尺寸变化时丢弃旧 RT，下一帧 EnsureRenderTarget 会重建
            DisposeRenderTarget();
            _rtInitFailed = false;
        }

        /// <summary>
        /// 提交一个 3D 实例到当前帧绘制队列，绘制完成后会被自动清除
        /// </summary>
        /// <param name="instance">要绘制的实例</param>
        public static void Submit(Model3DInstance instance) {
            if (instance == null || instance.Model == null || !instance.Visible) {
                return;
            }
            Model3DRenderer self = Instance;
            if (self == null) {
                return;
            }
            int layerIdx = (int)instance.Layer;
            if ((uint)layerIdx >= (uint)self._transientByLayer.Length) {
                layerIdx = (int)Model3DLayer.AfterTiles;
            }
            self._transientByLayer[layerIdx].Add(instance);
        }

        /// <summary>
        /// 便捷绘制：构造一次性实例并提交
        /// </summary>
        public static void Draw(VaultObjModel model, Vector2 position, Vector3 rotation, Vector3 scale
            , Model3DLayer layer = Model3DLayer.AfterTiles, Color? tint = null) {
            if (model == null) {
                return;
            }
            Submit(new Model3DInstance(model) {
                Position = position,
                Rotation = rotation,
                Scale = scale,
                Layer = layer,
                Tint = tint ?? Color.White,
            });
        }

        /// <summary>
        /// 注册常驻实例。常驻实例每帧都会被绘制，直到调用 <see cref="UnregisterPersistent"/>
        /// </summary>
        public static bool RegisterPersistent(Model3DInstance instance) {
            if (instance == null || instance.Model == null) {
                return false;
            }
            Model3DRenderer self = Instance;
            if (self == null) {
                return false;
            }
            int layerIdx = (int)instance.Layer;
            if ((uint)layerIdx >= (uint)self._persistentByLayer.Length) {
                return false;
            }
            lock (self._persistentLock) {
                List<Model3DInstance> bucket = self._persistentByLayer[layerIdx];
                if (bucket.Contains(instance)) {
                    return false;
                }
                bucket.Add(instance);
            }
            return true;
        }

        /// <summary>
        /// 注销常驻实例
        /// </summary>
        public static bool UnregisterPersistent(Model3DInstance instance) {
            if (instance == null) {
                return false;
            }
            Model3DRenderer self = Instance;
            if (self == null) {
                return false;
            }
            lock (self._persistentLock) {
                for (int i = 0; i < self._persistentByLayer.Length; i++) {
                    if (self._persistentByLayer[i].Remove(instance)) {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// 清空所有常驻实例
        /// </summary>
        public static void ClearPersistent() {
            Model3DRenderer self = Instance;
            if (self == null) {
                return;
            }
            lock (self._persistentLock) {
                for (int i = 0; i < self._persistentByLayer.Length; i++) {
                    self._persistentByLayer[i].Clear();
                }
            }
        }

        // ========================================================================
        // 各层钩子
        // ========================================================================

        /// <inheritdoc/>
        public override void DrawBeforeTiles(SpriteBatch spriteBatch, GraphicsDevice graphicsDevice, RenderTarget2D screenSwap) {
            // 进入时 SpriteBatch Active；要 End → 3D → Begin 恢复
            if (IsLayerEmpty(Model3DLayer.BeforeTiles)) {
                return;
            }
            spriteBatch.End();
            DrawLayerInternal(graphicsDevice, Model3DLayer.BeforeTiles);
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState
                , DepthStencilState.None, Main.Rasterizer, null, Main.GameViewMatrix.TransformationMatrix);
        }

        /// <inheritdoc/>
        public override void DrawAfterTiles(SpriteBatch spriteBatch, GraphicsDevice graphicsDevice, RenderTarget2D screenSwap) {
            // 进入/退出都要求 SpriteBatch 非 Active；可以直接画 3D
            DrawLayerInternal(graphicsDevice, Model3DLayer.AfterTiles);
        }

        /// <inheritdoc/>
        public override void DrawBeforePlayers(SpriteBatch spriteBatch, GraphicsDevice graphicsDevice, RenderTarget2D screenSwap) {
            DrawLayerInternal(graphicsDevice, Model3DLayer.BeforePlayers);
        }

        /// <inheritdoc/>
        public override void DrawAfterPlayers(SpriteBatch spriteBatch, GraphicsDevice graphicsDevice, RenderTarget2D screenSwap) {
            DrawLayerInternal(graphicsDevice, Model3DLayer.AfterPlayers);
        }

        /// <inheritdoc/>
        public override void DrawBeforeInfernoRings(SpriteBatch spriteBatch, GraphicsDevice graphicsDevice, RenderTarget2D screenSwap) {
            // 进入/退出都为 Active
            if (IsLayerEmpty(Model3DLayer.BeforeInfernoRings)) {
                return;
            }
            spriteBatch.End();
            DrawLayerInternal(graphicsDevice, Model3DLayer.BeforeInfernoRings);
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState
                , DepthStencilState.None, Main.Rasterizer, null, Main.GameViewMatrix.TransformationMatrix);
        }

        /// <inheritdoc/>
        public override void EndEntityDraw(SpriteBatch spriteBatch, Main main, GraphicsDevice graphicsDevice, RenderTarget2D screenSwap) {
            // 在所有层绘制结束后，统一清理临时桶（即使某些层没有钩子触发，也保证下一帧从干净状态开始）
            for (int i = 0; i < _transientByLayer.Length; i++) {
                _transientByLayer[i].Clear();
            }
        }

        // ========================================================================
        // 内部
        // ========================================================================

        private bool IsLayerEmpty(Model3DLayer layer) {
            int idx = (int)layer;
            if (_transientByLayer[idx].Count > 0) {
                return false;
            }
            lock (_persistentLock) {
                return _persistentByLayer[idx].Count == 0;
            }
        }

        private void DrawLayerInternal(GraphicsDevice graphicsDevice, Model3DLayer layer) {
            if (Main.gameMenu) {
                return;
            }
            int idx = (int)layer;

            // 先取一份持久 + 临时合并的快照，避免持锁绘制
            int transientCount = _transientByLayer[idx].Count;
            int persistentCount;
            lock (_persistentLock) {
                persistentCount = _persistentByLayer[idx].Count;
            }
            int total = transientCount + persistentCount;
            if (total == 0) {
                return;
            }

            _opaqueScratch.Clear();
            _transparentScratch.Clear();
            lock (_persistentLock) {
                List<Model3DInstance> persistent = _persistentByLayer[idx];
                for (int i = 0; i < persistent.Count; i++) {
                    BucketizeInstance(persistent[i]);
                }
            }
            List<Model3DInstance> transientList = _transientByLayer[idx];
            for (int i = 0; i < transientList.Count; i++) {
                BucketizeInstance(transientList[i]);
            }

            if (_opaqueScratch.Count == 0 && _transparentScratch.Count == 0) {
                return;
            }

            // 不透明：仅按 SortKey 升序，深度测试自己解决遮挡
            _opaqueScratch.Sort(static (a, b) => a.SortKey.CompareTo(b.SortKey));
            // 透明：先按 SortKey 升序，再按 Depth 降序（远的先画，近的覆盖在上）
            _transparentScratch.Sort(static (a, b) => {
                int keyCmp = a.SortKey.CompareTo(b.SortKey);
                if (keyCmp != 0) {
                    return keyCmp;
                }
                return b.Depth.CompareTo(a.Depth);
            });

            if (!EnsureEffect(graphicsDevice)) {
                return;
            }
            if (!EnsureRenderTarget(graphicsDevice)) {
                return;
            }

            SavedState saved = SaveState(graphicsDevice);
            RenderTargetBinding[] savedRTs = graphicsDevice.GetRenderTargets();
            bool rtBound = false;
            try {
                graphicsDevice.SetRenderTarget(_model3DRT);
                rtBound = true;
                // 关键：必须同时清颜色和深度。颜色清成透明，合成时不影响下层；深度清为最远值 1
                graphicsDevice.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Transparent, 1f, 0);

                Matrix view = Main.GameViewMatrix.TransformationMatrix;
                Matrix projection = Matrix.CreateOrthographicOffCenter(0f, Main.screenWidth, Main.screenHeight, 0f, -10000f, 10000f);

                // ---- Opaque 桶：写深度 + 写颜色，无 blending ----
                if (_opaqueScratch.Count > 0) {
                    graphicsDevice.BlendState = BlendState.Opaque;
                    graphicsDevice.SamplerStates[0] = SamplerState.LinearClamp;
                    for (int i = 0; i < _opaqueScratch.Count; i++) {
                        DrawSingle(graphicsDevice, _opaqueScratch[i], view, projection, isTransparent: false);
                    }
                }

                // ---- Transparent 桶：只读深度，按 back-to-front 顺序 alpha blend ----
                if (_transparentScratch.Count > 0) {
                    // 使用 NonPremultiplied，让 BasicEffect 输出的直alpha 颜色正确叠加到透明 RT 上
                    graphicsDevice.BlendState = BlendState.NonPremultiplied;
                    graphicsDevice.SamplerStates[0] = SamplerState.LinearClamp;
                    for (int i = 0; i < _transparentScratch.Count; i++) {
                        DrawSingle(graphicsDevice, _transparentScratch[i], view, projection, isTransparent: true);
                    }
                }
            } catch (Exception ex) {
                VaultMod.LoggerError($"[Model3DRenderer:{layer}]"
                    , $"Failed to draw 3D models on layer {layer}: {ex.Message}");
            } finally {
                if (rtBound) {
                    if (savedRTs == null || savedRTs.Length == 0) {
                        graphicsDevice.SetRenderTarget(null);
                    }
                    else {
                        graphicsDevice.SetRenderTargets(savedRTs);
                    }
                }
                RestoreState(graphicsDevice, saved);
                _opaqueScratch.Clear();
                _transparentScratch.Clear();
            }

            // ---- 合成阶段：把 _model3DRT 以全屏 quad 形式 alpha blend 回当前 RT ----
            // 此时 NonPremultiplied 累积到透明背景的结果，RGB 已天然带 alpha 预乘，故合成用 AlphaBlend (premultiplied)
            try {
                SpriteBatch sb = Main.spriteBatch;
                sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp
                    , DepthStencilState.None, RasterizerState.CullCounterClockwise);
                sb.Draw(_model3DRT, Vector2.Zero, Color.White);
                sb.End();
            } catch (Exception ex) {
                VaultMod.LoggerError($"[Model3DRenderer:Composite]"
                    , $"Failed to composite 3D render target on layer {layer}: {ex.Message}");
            }
        }

        // 把一个实例按透明/不透明分到对应桶。null / 不可见 / 无效模型直接丢弃
        private void BucketizeInstance(Model3DInstance instance) {
            if (instance == null || !instance.Visible || instance.Model == null) {
                return;
            }
            if (!instance.Model.IsValid) {
                return;
            }
            if (IsInstanceTransparent(instance)) {
                _transparentScratch.Add(instance);
            }
            else {
                _opaqueScratch.Add(instance);
            }
        }

        // 实例是否需要走透明桶：实例本身半透明、显式 ForceTransparent，或任一材质半透明
        private static bool IsInstanceTransparent(Model3DInstance instance) {
            if (instance.ForceTransparent) {
                return true;
            }
            if (instance.Opacity < 0.999f) {
                return true;
            }
            if (instance.Tint.A < 255) {
                return true;
            }
            VaultObjModel model = instance.Model;
            if (model == null) {
                return false;
            }
            IReadOnlyList<ObjMeshGroup> groups = model.Groups;
            for (int g = 0; g < groups.Count; g++) {
                ObjMaterial mat = groups[g].Material;
                if (mat != null && mat.Opacity < 0.999f) {
                    return true;
                }
            }
            return false;
        }

        private void DrawSingle(GraphicsDevice graphicsDevice, Model3DInstance instance, Matrix view, Matrix projection, bool isTransparent) {
            VaultObjModel model = instance.Model;

            // BlendState 由外层桶统一设置；这里只设置每实例可覆盖的 Depth/Rasterizer
            if (isTransparent) {
                // 透明只读深度，避免互相 z-fight 把后面的透明面写丢
                graphicsDevice.DepthStencilState = instance.DepthEnabled ? DepthStencilState.DepthRead : DepthStencilState.None;
            }
            else {
                graphicsDevice.DepthStencilState = instance.DepthEnabled ? DepthStencilState.Default : DepthStencilState.None;
            }
            graphicsDevice.RasterizerState = instance.CullBackface ? RasterizerState.CullCounterClockwise : RasterizerState.CullNone;

            Vector3 worldOffset = new Vector3(
                instance.Position.X - Main.screenPosition.X,
                instance.Position.Y - Main.screenPosition.Y,
                instance.Depth);

            Matrix world = Matrix.CreateScale(instance.Scale)
                * Matrix.CreateRotationX(instance.Rotation.X)
                * Matrix.CreateRotationY(instance.Rotation.Y)
                * Matrix.CreateRotationZ(instance.Rotation.Z)
                * Matrix.CreateTranslation(worldOffset);

            _effect.World = world;
            _effect.View = view;
            _effect.Projection = projection;

            if (instance.LightingEnabled) {
                // 解析光照：实例 Override 优先；都为空则用空配置兜底（避免 NRE）
                Model3DLightingConfig source = instance.LightingOverride ?? GlobalLighting;
                if (source != null) {
                    source.CopyTo(_scratchLighting);
                }
                else {
                    Model3DLightingConfig.CreateDefault().CopyTo(_scratchLighting);
                }
                // 给订阅者按需 mutate scratch 的机会（光标光、昼夜变化、tile light 整合等）
                ResolveLighting?.Invoke(instance, _scratchLighting);
                _effect.LightingEnabled = true;
                ApplyLighting(_effect, _scratchLighting);
            }
            else {
                _effect.LightingEnabled = false;
            }

            for (int g = 0; g < model.Groups.Count; g++) {
                ObjMeshGroup group = model.Groups[g];
                if (group.Vertices.Length == 0 || group.Indices.Length == 0) {
                    continue;
                }

                Color materialColor = group.Material != null ? group.Material.DiffuseColor : Color.White;
                float materialOpacity = group.Material != null ? group.Material.Opacity : 1f;
                Color combined = MultiplyColor(materialColor, instance.Tint);
                float finalAlpha = MathHelper.Clamp(materialOpacity * instance.Opacity * (instance.Tint.A / 255f), 0f, 1f);

                _effect.DiffuseColor = combined.ToVector3();
                _effect.Alpha = finalAlpha;

                if (group.Material != null && group.Material.HasTexture) {
                    _effect.TextureEnabled = true;
                    _effect.Texture = group.Material.DiffuseTexture;
                }
                else {
                    _effect.TextureEnabled = false;
                    _effect.Texture = null;
                }

                EffectPassCollection passes = _effect.CurrentTechnique.Passes;
                for (int p = 0; p < passes.Count; p++) {
                    passes[p].Apply();
                    graphicsDevice.DrawUserIndexedPrimitives(
                        PrimitiveType.TriangleList,
                        group.Vertices,
                        0,
                        group.Vertices.Length,
                        group.Indices,
                        0,
                        group.TriangleCount);
                }
            }
        }

        /// <summary>
        /// 释放渲染器持有的 GPU 资源（仅供卸载时调用）
        /// </summary>
        internal void DisposeEffect() {
            if (_effect != null && !_effect.IsDisposed) {
                try {
                    _effect.Dispose();
                } catch {
                    // GPU 卸载阶段忽略异常
                }
            }
            _effect = null;
            _effectInitFailed = false;
        }

        /// <summary>
        /// 释放渲染器持有的 3D 渲染目标（仅供卸载/分辨率变化时调用）
        /// </summary>
        internal void DisposeRenderTarget() {
            if (_model3DRT != null && !_model3DRT.IsDisposed) {
                try {
                    _model3DRT.Dispose();
                } catch {
                    // GPU 卸载阶段忽略异常
                }
            }
            _model3DRT = null;
        }

        private bool EnsureRenderTarget(GraphicsDevice graphicsDevice) {
            int w = Main.screenWidth;
            int h = Main.screenHeight;
            if (w <= 0 || h <= 0) {
                return false;
            }
            if (_model3DRT != null && !_model3DRT.IsDisposed
                && _model3DRT.Width == w && _model3DRT.Height == h) {
                return true;
            }
            if (_rtInitFailed) {
                return false;
            }

            try {
                _model3DRT?.Dispose();
                // PreserveContents 让我们清楚地控制每层都从 Clear 开始；用 Depth24 才能真正启用深度测试
                _model3DRT = new RenderTarget2D(graphicsDevice, w, h, false, SurfaceFormat.Color
                    , DepthFormat.Depth24, 0, RenderTargetUsage.PreserveContents);
                return true;
            } catch (Exception ex) {
                _rtInitFailed = true;
                _model3DRT = null;
                VaultMod.LoggerError("[Model3DRenderer:RTInit]"
                    , $"Failed to create 3D render target ({w}x{h}): {ex.Message}");
                return false;
            }
        }

        private bool EnsureEffect(GraphicsDevice graphicsDevice) {
            if (_effect != null && !_effect.IsDisposed) {
                return true;
            }
            if (_effectInitFailed) {
                return false;
            }
            try {
                _effect = new BasicEffect(graphicsDevice) {
                    TextureEnabled = true,
                    VertexColorEnabled = false,
                    LightingEnabled = false,
                };
            } catch (Exception ex) {
                _effectInitFailed = true;
                VaultMod.LoggerError("[Model3DRenderer:EffectInit]"
                    , $"Failed to construct BasicEffect: {ex.Message}");
                return false;
            }
            return true;
        }

        private static SavedState SaveState(GraphicsDevice gd) {
            return new SavedState {
                Blend = gd.BlendState,
                Depth = gd.DepthStencilState,
                Rasterizer = gd.RasterizerState,
                Sampler0 = gd.SamplerStates[0],
            };
        }

        private static void RestoreState(GraphicsDevice gd, SavedState saved) {
            if (saved.Blend != null) gd.BlendState = saved.Blend;
            if (saved.Depth != null) gd.DepthStencilState = saved.Depth;
            if (saved.Rasterizer != null) gd.RasterizerState = saved.Rasterizer;
            if (saved.Sampler0 != null) gd.SamplerStates[0] = saved.Sampler0;
        }

        // 把 Model3DLightingConfig 写入 BasicEffect。调用方需先把 LightingEnabled 置 true。
        private static void ApplyLighting(BasicEffect effect, Model3DLightingConfig cfg) {
            effect.AmbientLightColor = cfg.AmbientColor;
            effect.EmissiveColor = cfg.EmissiveColor;
            effect.SpecularPower = cfg.SpecularPower;
            ApplyDirLight(effect.DirectionalLight0, cfg.Light0);
            ApplyDirLight(effect.DirectionalLight1, cfg.Light1);
            ApplyDirLight(effect.DirectionalLight2, cfg.Light2);
        }

        private static void ApplyDirLight(DirectionalLight slot, Model3DDirectionalLight src) {
            slot.Enabled = src.Enabled;
            if (!src.Enabled) {
                return;
            }
            slot.Direction = src.Direction;
            slot.DiffuseColor = src.DiffuseColor;
            slot.SpecularColor = src.SpecularColor;
        }

        private static Color MultiplyColor(Color a, Color b) {
            return new Color(
                (byte)(a.R * b.R / 255),
                (byte)(a.G * b.G / 255),
                (byte)(a.B * b.B / 255),
                (byte)(a.A * b.A / 255));
        }
    }
}
