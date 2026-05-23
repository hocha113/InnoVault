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

        // 每层一个临时提交桶；每帧绘制完即清空
        private readonly List<Model3DInstance>[] _transientByLayer;
        // 每层一个常驻实例桶；不会在帧间清空
        private readonly List<Model3DInstance>[] _persistentByLayer;
        private readonly object _persistentLock = new object();

        private BasicEffect _effect;
        private bool _effectInitFailed;

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
            List<Model3DInstance> drawList = null;
            int transientCount = _transientByLayer[idx].Count;
            int persistentCount;
            lock (_persistentLock) {
                persistentCount = _persistentByLayer[idx].Count;
            }
            int total = transientCount + persistentCount;
            if (total == 0) {
                return;
            }

            drawList = new List<Model3DInstance>(total);
            lock (_persistentLock) {
                drawList.AddRange(_persistentByLayer[idx]);
            }
            drawList.AddRange(_transientByLayer[idx]);

            // 按 SortKey 升序，越大越后画
            drawList.Sort((a, b) => a.SortKey.CompareTo(b.SortKey));

            if (!EnsureEffect(graphicsDevice)) {
                return;
            }

            SavedState saved = SaveState(graphicsDevice);
            try {
                Matrix view = Main.GameViewMatrix.TransformationMatrix;
                Matrix projection = Matrix.CreateOrthographicOffCenter(0f, Main.screenWidth, Main.screenHeight, 0f, -10000f, 10000f);

                for (int i = 0; i < drawList.Count; i++) {
                    Model3DInstance instance = drawList[i];
                    if (instance == null || !instance.Visible || instance.Model == null) {
                        continue;
                    }
                    if (!instance.Model.IsValid) {
                        continue;
                    }
                    DrawSingle(graphicsDevice, instance, view, projection);
                }
            } catch (Exception ex) {
                VaultMod.LoggerError($"[Model3DRenderer:{layer}]"
                    , $"Failed to draw 3D models on layer {layer}: {ex.Message}");
            } finally {
                RestoreState(graphicsDevice, saved);
            }
        }

        private void DrawSingle(GraphicsDevice graphicsDevice, Model3DInstance instance, Matrix view, Matrix projection) {
            VaultObjModel model = instance.Model;

            // 配置渲染状态：每个实例可以独立选择是否启用深度测试和背面剔除
            graphicsDevice.BlendState = BlendState.AlphaBlend;
            graphicsDevice.SamplerStates[0] = SamplerState.LinearClamp;
            graphicsDevice.DepthStencilState = instance.DepthEnabled ? DepthStencilState.Default : DepthStencilState.None;
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
            _effect.LightingEnabled = instance.LightingEnabled;
            if (instance.LightingEnabled) {
                _effect.EnableDefaultLighting();
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

        private static Color MultiplyColor(Color a, Color b) {
            return new Color(
                (byte)(a.R * b.R / 255),
                (byte)(a.G * b.G / 255),
                (byte)(a.B * b.B / 255),
                (byte)(a.A * b.A / 255));
        }
    }
}
