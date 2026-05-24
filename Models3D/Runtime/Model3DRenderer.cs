using InnoVault.Models3D.Animation;
using InnoVault.Models3D.Skinning;
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
    /// <br/>挂载在 <see cref="RenderHandle"/> 的多个分层钩子上，按 <see cref="Model3DLayer"/> 绘制 3D 模型
    /// <br/>对外暴露三层 API：
    /// <list type="bullet">
    /// <item><b>便捷 API</b>：<see cref="Submit"/> / <see cref="RegisterPersistent"/>
    /// / <see cref="Draw(Vault3DModel, Vector2, Vector3, Vector3, Model3DLayer, Color?)"/>，最常用 80% 场景</item>
    /// <item><b>扩展 API</b>：实例 / 材质上的 <c>Effect</c> / <c>EffectProvider</c> / <c>ConfigureEffect</c> / <c>RenderStateOverride</c>
    /// / <c>Pre/Post DrawInstance/Group</c> 字段，配合 <see cref="ResolveLighting"/> / <see cref="PreDrawInstance"/>
    /// / <see cref="PostDrawInstance"/> / <see cref="PreDrawGroup"/> / <see cref="PostDrawGroup"/> 全局静态事件，
    /// 以及 <see cref="OnLayerRendered"/> / <see cref="CompositeOverride"/> RT 后处理钩子</item>
    /// <item><b>原子 API</b>：<see cref="DrawInstance"/> / <see cref="DrawMeshGroup"/> / <see cref="DrawMeshPrimitives"/>
    /// / <see cref="BuildWorldMatrix"/> / <see cref="BuildScreenViewMatrix"/> / <see cref="BuildScreenProjection"/>
    /// / <see cref="ApplyLighting"/> / <see cref="DefaultEffect"/>，允许开发者在自己的 <see cref="RenderHandle"/>
    /// 中完全手写一遍 3D 绘制路径，同时仍复用 OBJ 加载与渲染基础设施</item>
    /// </list>
    /// <br/><b>Effect 解析优先级链</b>：显式 effectOverride &gt; <see cref="Model3DInstance.Effect"/>
    /// &gt; <see cref="Model3DInstance.EffectProvider"/>.Resolve &gt; <see cref="Model3DMaterial.Effect"/>
    /// &gt; <see cref="Model3DMaterial.EffectProvider"/>.Resolve &gt; 默认 <see cref="BasicEffect"/>
    /// <br/><b>RenderState 解析顺序</b>：<see cref="Model3DInstance.RenderStateOverride"/>
    /// &gt; <see cref="Model3DMaterial.RenderStateOverride"/> &gt; 桶级默认（Opaque / NonPremultiplied + 实例 bool 字段）
    /// <br/><b>注意</b>：当使用非 <see cref="BasicEffect"/> 的自定义 Effect 时，渲染器不会自动写入光照 / Tint / Texture 等参数，
    /// 调用方需自行在 <see cref="IModel3DEffectProvider.Configure"/> 或 <c>ConfigureEffect</c> 委托中处理；
    /// 这是为了把"自定义 shader 的参数语义"完全交给开发者，避免框架做出不正确的假设
    /// <br/>World / View / Projection 矩阵会被自动写入（仅当 Effect 实现 <see cref="IEffectMatrices"/>），减少样板代码
    /// </summary>
    public sealed class Model3DRenderer : RenderHandle
    {
        /// <inheritdoc/>
        public override float Weight => 5f; //在 PRT 之后，避免覆盖粒子的画布状态

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

        /// <summary>
        /// 全局静态事件：每个实例绘制开始前触发；调用顺序在 <see cref="Model3DInstance.PreDrawInstance"/> 之后
        /// <br/>典型用法：让 mod 全局收集"本帧将要绘制的所有 3D 实例"以便做后处理或调试
        /// </summary>
        public static event Model3DDrawCallback PreDrawInstance;

        /// <summary>
        /// 全局静态事件：每个实例的所有 group 绘制完成后触发；调用顺序在 <see cref="Model3DInstance.PostDrawInstance"/> 之前
        /// </summary>
        public static event Model3DDrawCallback PostDrawInstance;

        /// <summary>
        /// 全局静态事件：每个 mesh group 绘制前触发，链路位于实例 / 材质回调之后
        /// </summary>
        public static event Model3DDrawCallback PreDrawGroup;

        /// <summary>
        /// 全局静态事件：每个 mesh group 绘制后触发
        /// </summary>
        public static event Model3DDrawCallback PostDrawGroup;

        /// <summary>
        /// 一层 3D 模型绘制结果已经写入 RT（但尚未合成回屏幕）时触发
        /// <br/>订阅者可在此 take 一份 RT 的拷贝，做模型描边、外发光、扭曲遮罩等任意后处理
        /// <br/>注意：触发时 GraphicsDevice 已经从 RT 切回上一级，但 SpriteBatch 处于非 Active 状态
        /// </summary>
        public static event Action<Model3DLayer, RenderTarget2D> OnLayerRendered;

        /// <summary>
        /// 自定义合成回调；返回 <see langword="true"/> 表示"我已经把 RT 合成到屏幕了，渲染器不要再走默认合成路径"
        /// <br/>典型用法：用自定义 shader 把 3D 层叠加回画面（色调映射、扭曲、辉光等）
        /// </summary>
        /// <param name="layer">当前正在合成的层</param>
        /// <param name="rt">本层 3D 渲染结果（PreMultiplied alpha 风格）</param>
        /// <param name="spriteBatch">主 <see cref="SpriteBatch"/>，调用时处于非 Active 状态；如自行 Begin 必须 End</param>
        /// <returns>是否已自行消费合成；返回 false 则继续走默认合成（AlphaBlend + sprite quad）</returns>
        public delegate bool Model3DCompositeOverride(Model3DLayer layer, RenderTarget2D rt, SpriteBatch spriteBatch);

        /// <summary>
        /// 整体替换默认合成行为的覆盖函数；为 <see langword="null"/> 时走默认合成
        /// </summary>
        public static Model3DCompositeOverride CompositeOverride;

        /// <summary>
        /// 当前正在被渲染器使用的层级 RT；仅在管线运行中（<see cref="OnLayerRendered"/> / <see cref="CompositeOverride"/> 期间）非空
        /// <br/>外部不应缓存此 RT，因为分辨率变化时它会被释放重建
        /// </summary>
        public static RenderTarget2D CurrentLayerRT => Instance?._model3DRT;

        /// <summary>
        /// 渲染器持有的默认 <see cref="BasicEffect"/>；首次访问时会按需在主线程构造
        /// <br/>外部 RenderHandle 在原子 API 路径上需要"借用一个 BasicEffect"时可直接复用此实例，避免重复创建
        /// </summary>
        public static BasicEffect DefaultEffect {
            get {
                Model3DRenderer self = Instance;
                if (self == null) {
                    return null;
                }
                GraphicsDevice gd = Main.instance?.GraphicsDevice;
                if (gd == null) {
                    return self._effect;
                }
                self.EnsureEffect(gd);
                return self._effect;
            }
        }

        //每帧/每实例复用，避免分配；写入前从 source 拷贝，写入后由订阅者按需修改
        //同时作为公开静态 DrawInstance API 的 lighting scratch（主线程渲染单一调用栈，无并发问题）
        private static readonly Model3DLightingConfig _scratchLighting = new Model3DLightingConfig();

        //每层一个临时提交桶；每帧绘制完即清空
        private readonly List<Model3DInstance>[] _transientByLayer;
        //每层一个常驻实例桶；不会在帧间清空
        private readonly List<Model3DInstance>[] _persistentByLayer;
        private readonly object _persistentLock = new object();

        private BasicEffect _effect;
        private bool _effectInitFailed;

        //Models3D 自己的渲染目标，必须带 Depth24 才能真正使用深度测试
        private RenderTarget2D _model3DRT;
        private bool _rtInitFailed;

        //复用的临时桶，避免每帧分配
        private readonly List<Model3DInstance> _opaqueScratch = new List<Model3DInstance>(32);
        private readonly List<Model3DInstance> _transparentScratch = new List<Model3DInstance>(8);

        //保存绘制前的 GraphicsDevice 状态，绘制后恢复
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
            //屏幕尺寸变化时丢弃旧 RT，下一帧 EnsureRenderTarget 会重建
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
        public static void Draw(Vault3DModel model, Vector2 position, Vector3 rotation, Vector3 scale
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

        //========================================================================
        //各层钩子
        //========================================================================

        /// <inheritdoc/>
        public override void DrawBeforeTiles(SpriteBatch spriteBatch, GraphicsDevice graphicsDevice, RenderTarget2D screenSwap) {
            //进入时 SpriteBatch Active；要 End → 3D → Begin 恢复
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
            //进入/退出都要求 SpriteBatch 非 Active；可以直接画 3D
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
            //进入/退出都为 Active
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
            //在所有层绘制结束后，统一清理临时桶（即使某些层没有钩子触发，也保证下一帧从干净状态开始）
            for (int i = 0; i < _transientByLayer.Length; i++) {
                _transientByLayer[i].Clear();
            }
        }

        //========================================================================
        //内部
        //========================================================================

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

            //先取一份持久 + 临时合并的快照，避免持锁绘制
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

            //不透明：仅按 SortKey 升序，深度测试自己解决遮挡
            _opaqueScratch.Sort(static (a, b) => a.SortKey.CompareTo(b.SortKey));
            //透明：先按 SortKey 升序，再按 Depth 降序（远的先画，近的覆盖在上）
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
                //关键：必须同时清颜色和深度。颜色清成透明，合成时不影响下层；深度清为最远值 1
                graphicsDevice.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Transparent, 1f, 0);

                Matrix view = Main.GameViewMatrix.TransformationMatrix;
                Matrix projection = Matrix.CreateOrthographicOffCenter(0f, Main.screenWidth, Main.screenHeight, 0f, -10000f, 10000f);

                //---- Opaque 桶：写深度 + 写颜色，无 blending ----
                if (_opaqueScratch.Count > 0) {
                    graphicsDevice.BlendState = BlendState.Opaque;
                    graphicsDevice.SamplerStates[0] = SamplerState.LinearClamp;
                    for (int i = 0; i < _opaqueScratch.Count; i++) {
                        DrawSingle(graphicsDevice, _opaqueScratch[i], view, projection, isTransparent: false);
                    }
                }

                //---- Transparent 桶：只读深度，按 back-to-front 顺序 alpha blend ----
                if (_transparentScratch.Count > 0) {
                    //使用 NonPremultiplied，让 BasicEffect 输出的直alpha 颜色正确叠加到透明 RT 上
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

            //RT 已包含本层 3D 内容，未合成回屏；先给订阅者抓 / 自定义后处理的机会
            try {
                OnLayerRendered?.Invoke(layer, _model3DRT);
            } catch (Exception ex) {
                VaultMod.LoggerError($"[Model3DRenderer:OnLayerRendered]"
                    , $"OnLayerRendered subscriber threw on layer {layer}: {ex.Message}");
            }

            //---- 合成阶段：先尝试 CompositeOverride 整体替换，否则走默认 AlphaBlend quad ----
            //此时 NonPremultiplied 累积到透明背景的结果，RGB 已天然带 alpha 预乘，故默认合成用 AlphaBlend (premultiplied)
            try {
                SpriteBatch sb = Main.spriteBatch;
                bool consumed = false;
                Model3DCompositeOverride overrideFn = CompositeOverride;
                if (overrideFn != null) {
                    try {
                        consumed = overrideFn(layer, _model3DRT, sb);
                    } catch (Exception ex) {
                        VaultMod.LoggerError($"[Model3DRenderer:CompositeOverride]"
                            , $"CompositeOverride threw on layer {layer}: {ex.Message}");
                        consumed = false;
                    }
                }
                if (!consumed) {
                    sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp
                        , DepthStencilState.None, RasterizerState.CullCounterClockwise);
                    sb.Draw(_model3DRT, Vector2.Zero, Color.White);
                    sb.End();
                }
            } catch (Exception ex) {
                VaultMod.LoggerError($"[Model3DRenderer:Composite]"
                    , $"Failed to composite 3D render target on layer {layer}: {ex.Message}");
            }
        }

        //把一个实例按透明/不透明分到对应桶。null / 不可见 / 无效模型直接丢弃
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

        //实例是否需要走透明桶：实例本身半透明、显式 ForceTransparent、Blend 覆盖为非 Opaque、或任一材质半透明/Blend 覆盖
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
            //RenderStateOverride 显式指定的非 Opaque Blend 必须走透明桶，否则会写深度污染后续画面
            if (instance.RenderStateOverride != null && instance.RenderStateOverride.ForcesTransparentBucket) {
                return true;
            }
            Vault3DModel model = instance.Model;
            if (model == null) {
                return false;
            }
            IReadOnlyList<Model3DMeshGroup> groups = model.Groups;
            for (int g = 0; g < groups.Count; g++) {
                Model3DMaterial mat = groups[g].Material;
                if (mat == null) {
                    continue;
                }
                if (mat.Opacity < 0.999f) {
                    return true;
                }
                if (mat.RenderStateOverride != null && mat.RenderStateOverride.ForcesTransparentBucket) {
                    return true;
                }
            }
            return false;
        }

        //========================================================================
        // 公开原子 API：可以从任意 RenderHandle / 自定义渲染路径上直接调用
        // 调用约定：SpriteBatch 必须处于非 Active 状态；调用方负责 RT 绑定与状态保存
        //========================================================================

        /// <summary>
        /// 把当前实例完整画到 <see cref="GraphicsDevice"/> 上，使用三层优先级链解析的 Effect 与 RenderState
        /// <br/>这是渲染器内部桶分类绘制路径与"开发者自己写 RenderHandle"路径共享的实现入口
        /// <br/><b>调用约定</b>：<see cref="SpriteBatch"/> 必须处于非 Active 状态；调用方需自行绑定 RT / 保存恢复 GraphicsDevice 状态
        /// </summary>
        /// <param name="graphicsDevice">绘制使用的设备</param>
        /// <param name="instance">要绘制的实例</param>
        /// <param name="view">视图矩阵；可通过 <see cref="BuildScreenViewMatrix"/> 获取标准屏幕视图</param>
        /// <param name="projection">投影矩阵；可通过 <see cref="BuildScreenProjection"/> 获取标准屏幕正交投影</param>
        /// <param name="layer">该实例归属的层（用于上下文，不影响绘制路径）</param>
        /// <param name="isTransparent">是否按透明桶约定绘制（影响默认 Blend / Depth）</param>
        /// <param name="effectOverride">显式 Effect 覆盖；非空时绕过实例 / 材质上挂的 Effect 与 Provider 链</param>
        public static void DrawInstance(GraphicsDevice graphicsDevice, Model3DInstance instance
            , Matrix view, Matrix projection, Model3DLayer layer = Model3DLayer.AfterTiles
            , bool isTransparent = false, Effect effectOverride = null) {
            if (graphicsDevice == null || instance == null || !instance.Visible) {
                return;
            }
            Vault3DModel model = instance.Model;
            if (model == null || !model.IsValid) {
                return;
            }

            Matrix world = BuildWorldMatrix(instance);
            Model3DLightingConfig lighting = ResolveLightingForInstance(instance);
            float time = (float)Main.timeForVisualEffects;

            //驱动动画：渲染线程统一推进时间并采样姿态，调用方一般无需手动调
            //per-player 追踪上次推进时刻，避免 multi-instance / 同帧多次 Draw 的"互相抢 delta"
            if (model.IsSkinned && instance.Animation != null) {
                float deltaSeconds = ResolveDeltaSeconds(instance.Animation);
                instance.Animation.Update(deltaSeconds);
                instance.Animation.SamplePose();
            }

            Model3DDrawContext instanceCtx = new Model3DDrawContext(graphicsDevice, instance, model, null, null
                , layer, world, view, projection, lighting, isTransparent, time);

            instance.PreDrawInstance?.Invoke(in instanceCtx);
            PreDrawInstance?.Invoke(in instanceCtx);

            //桶级默认状态（DrawInstance 自包含：调用方传 isTransparent 即可决定）
            BlendState defaultBlend = isTransparent ? BlendState.NonPremultiplied : BlendState.Opaque;
            DepthStencilState defaultDepth = isTransparent
                ? (instance.DepthEnabled ? DepthStencilState.DepthRead : DepthStencilState.None)
                : (instance.DepthEnabled ? DepthStencilState.Default : DepthStencilState.None);
            RasterizerState defaultRast = instance.CullBackface ? RasterizerState.CullCounterClockwise : RasterizerState.CullNone;
            SamplerState defaultSampler = SamplerState.LinearClamp;

            for (int g = 0; g < model.Groups.Count; g++) {
                Model3DMeshGroup group = model.Groups[g];
                if (group == null || group.Vertices.Length == 0 || group.Indices.Length == 0) {
                    continue;
                }
                Model3DMaterial material = group.Material;
                Model3DDrawContext groupCtx = instanceCtx.WithGroup(group, material);

                //渲染状态解析：实例 → 材质 → 桶默认
                Model3DRenderState.ApplyResolved(graphicsDevice
                    , instance.RenderStateOverride, material?.RenderStateOverride
                    , defaultBlend, defaultDepth, defaultRast, defaultSampler);

                //Effect 解析：显式覆盖 > 实例 Effect > 实例 Provider > 材质 Effect > 材质 Provider > 默认 BasicEffect
                Effect effect = effectOverride
                    ?? instance.Effect
                    ?? instance.EffectProvider?.Resolve(in groupCtx)
                    ?? material?.Effect
                    ?? material?.EffectProvider?.Resolve(in groupCtx);

                if (effect == null) {
                    //默认 BasicEffect 路径
                    BasicEffect basic = GetOrCreateDefaultEffect(graphicsDevice);
                    if (basic == null) {
                        continue;
                    }
                    effect = basic;
                    ConfigureBasicEffectFor(basic, instance, material, world, view, projection, lighting);
                }
                else {
                    //自定义 Effect 路径：渲染器不写光照/Tint/Texture（不知道 shader 参数名），由 Provider/Configure 负责
                    //但矩阵是 3D 绘制的通用前置条件，如果 Effect 实现了 IEffectMatrices 就帮忙写一遍以减少样板代码
                    //Provider/Configure 之后仍然可以覆盖这些矩阵
                    if (effect is IEffectMatrices m) {
                        m.World = world;
                        m.View = view;
                        m.Projection = projection;
                    }
                    instance.EffectProvider?.Configure(in groupCtx, effect);
                    material?.EffectProvider?.Configure(in groupCtx, effect);
                    instance.ConfigureEffect?.Invoke(in groupCtx, effect);
                    material?.ConfigureEffect?.Invoke(in groupCtx, effect);
                }

                instance.PreDrawGroup?.Invoke(in groupCtx);
                material?.PreDrawGroup?.Invoke(in groupCtx);
                PreDrawGroup?.Invoke(in groupCtx);

                VertexPositionNormalTexture[] vertSource = ResolveVertexSource(instance, group);
                DrawMeshPrimitives(graphicsDevice, vertSource, group.Indices, effect);

                material?.PostDrawGroup?.Invoke(in groupCtx);
                instance.PostDrawGroup?.Invoke(in groupCtx);
                PostDrawGroup?.Invoke(in groupCtx);
            }

            PostDrawInstance?.Invoke(in instanceCtx);
            instance.PostDrawInstance?.Invoke(in instanceCtx);
        }

        /// <summary>
        /// 用给定 <see cref="Effect"/> 画一个 <see cref="Model3DMeshGroup"/>，自动遍历该 effect 当前 technique 的所有 pass
        /// <br/>如果 <paramref name="effect"/> 实现了 <see cref="IEffectMatrices"/>，矩阵会被自动写入；否则不写
        /// <br/>调用方负责设置 BlendState / DepthStencilState / RasterizerState / Sampler
        /// </summary>
        public static void DrawMeshGroup(GraphicsDevice graphicsDevice, Model3DMeshGroup group
            , Effect effect, Matrix world, Matrix view, Matrix projection) {
            if (graphicsDevice == null || group == null || effect == null) {
                return;
            }
            if (group.Vertices.Length == 0 || group.Indices.Length == 0) {
                return;
            }
            if (effect is IEffectMatrices matrices) {
                matrices.World = world;
                matrices.View = view;
                matrices.Projection = projection;
            }
            DrawMeshPrimitives(graphicsDevice, group.Vertices, group.Indices, effect);
        }

        /// <summary>
        /// 最低层原子：把给定顶点 / 索引 / Effect 直接送入 GPU
        /// <br/>调用方需保证 effect 的 <c>CurrentTechnique</c> 与参数都已就绪
        /// </summary>
        public static void DrawMeshPrimitives(GraphicsDevice graphicsDevice
            , VertexPositionNormalTexture[] vertices, int[] indices, Effect effect) {
            if (graphicsDevice == null || effect == null || vertices == null || indices == null) {
                return;
            }
            if (vertices.Length == 0 || indices.Length == 0) {
                return;
            }
            int triangleCount = indices.Length / 3;
            if (triangleCount <= 0) {
                return;
            }
            EffectPassCollection passes = effect.CurrentTechnique.Passes;
            for (int p = 0; p < passes.Count; p++) {
                passes[p].Apply();
                graphicsDevice.DrawUserIndexedPrimitives(
                    PrimitiveType.TriangleList,
                    vertices,
                    0,
                    vertices.Length,
                    indices,
                    0,
                    triangleCount);
            }
        }

        /// <summary>
        /// 由 <see cref="Model3DInstance.Position"/> / <see cref="Model3DInstance.Rotation"/> / <see cref="Model3DInstance.Scale"/>
        /// / <see cref="Model3DInstance.Depth"/> 构造世界矩阵；自动减去 <see cref="Terraria.Main.screenPosition"/> 以适配 Terraria 屏幕坐标
        /// </summary>
        public static Matrix BuildWorldMatrix(Model3DInstance instance) {
            if (instance == null) {
                return Matrix.Identity;
            }
            Vector3 worldOffset = new Vector3(
                instance.Position.X - Main.screenPosition.X,
                instance.Position.Y - Main.screenPosition.Y,
                instance.Depth);
            Vault3DModel model = instance.Model;
            Vector3 pivot = model?.Pivot ?? Vector3.Zero;
            //RootTransform 在最内层左乘进 World：先把模型从源空间（如 glTF 蒙皮空间）抬到"轴/缩放都已应用"的中间空间，
            //再做 pivot 平移、实例缩放/旋转、最后屏幕偏移。非蒙皮模型 RootTransform 是 Identity，行为完全等价旧路径
            Matrix rootTransform = model?.RootTransform ?? Matrix.Identity;
            return rootTransform
                * Matrix.CreateTranslation(-pivot)
                * Matrix.CreateScale(instance.Scale)
                * Matrix.CreateRotationX(instance.Rotation.X)
                * Matrix.CreateRotationY(instance.Rotation.Y)
                * Matrix.CreateRotationZ(instance.Rotation.Z)
                * Matrix.CreateTranslation(worldOffset);
        }

        //蒙皮 group 走实例 scratch（CPU 蒙皮已在 SamplePose 期间填好调色板），其余 group 直接用 group.Vertices
        private static VertexPositionNormalTexture[] ResolveVertexSource(Model3DInstance instance, Model3DMeshGroup group) {
            if (group == null) {
                return null;
            }
            if (!group.IsSkinned) {
                return group.Vertices;
            }
            AnimationPlayer player = instance.Animation;
            if (player == null) {
                //实例没有播放头：退回 bind pose（视觉上等同绑定姿态）
                return group.BindVertices;
            }
            SkinningPalette palette = player.GetPalette(group.SkinIndex);
            if (palette == null) {
                return group.BindVertices;
            }
            VertexPositionNormalTexture[] dst = instance.SkinScratch.GetOrCreateGroupBuffer(group);
            if (dst == null) {
                return group.BindVertices;
            }
            SkinningPalette.ApplyToVertices(group, palette, dst);
            return dst;
        }

        //渲染线程的 delta time：用 Main.gameTimeCache.TotalGameTime 的差值（单调递增、稳定可靠）
        //不用 ElapsedGameTime —— 实测在 Draw 阶段它经常是几微秒级别的脏值，会让动画几乎不前进
        //per-player 追踪：同一 player 同帧被 Draw 多次时第二次起返回 0；不同 player 各自独立计算
        //首帧 / 长卡顿 / Main.gameTimeCache 不可用时回退到 1/60 秒固定步长
        private static float ResolveDeltaSeconds(Animation.AnimationPlayer player) {
            const float FixedFallback = 1f / 60f;
            GameTime gt = Main.gameTimeCache;
            if (gt == null || player == null) {
                return FixedFallback;
            }
            double total = gt.TotalGameTime.TotalSeconds;
            double last = player.LastDriverTotalSeconds;
            //首次调用：仅记录时刻，下一帧才开始推进，避免把"加载阶段累积的虚假大 delta"灌进动画
            if (last < 0.0) {
                player.LastDriverTotalSeconds = total;
                return FixedFallback;
            }
            double delta = total - last;
            //同帧（multi-Draw）或游戏暂停（TotalGameTime 不前进）→ 0，避免重复推进
            if (delta <= 0.0) {
                return 0f;
            }
            player.LastDriverTotalSeconds = total;
            //长时间停留在加载界面 / 切换世界后单帧巨大跳变 → 兜底，避免一帧跨过整段动画
            if (delta > 1.0) {
                return FixedFallback;
            }
            return (float)delta;
        }

        /// <summary>
        /// 当前帧的标准屏幕视图矩阵；等价于 <see cref="Terraria.Main.GameViewMatrix"/>.TransformationMatrix
        /// </summary>
        public static Matrix BuildScreenViewMatrix() {
            return Main.GameViewMatrix.TransformationMatrix;
        }

        /// <summary>
        /// 当前屏幕尺寸下使用的标准正交投影；Z 范围 [-10000, 10000]
        /// </summary>
        public static Matrix BuildScreenProjection() {
            return Matrix.CreateOrthographicOffCenter(0f, Main.screenWidth, Main.screenHeight, 0f, -10000f, 10000f);
        }

        /// <summary>
        /// 把 <see cref="Model3DLightingConfig"/> 写入任意实现 <see cref="IEffectLights"/> 的 Effect（含 <see cref="BasicEffect"/>）
        /// <br/>只写 Ambient + 三盏方向光；<see cref="Model3DLightingConfig.EmissiveColor"/> 与 <see cref="Model3DLightingConfig.SpecularPower"/>
        /// 不在 <see cref="IEffectLights"/> 接口上，自定义 shader 若需要请通过自身参数另行设置
        /// </summary>
        public static void ApplyLighting(IEffectLights effect, Model3DLightingConfig cfg) {
            if (effect == null || cfg == null) {
                return;
            }
            effect.AmbientLightColor = cfg.AmbientColor;
            ApplyDirLight(effect.DirectionalLight0, cfg.Light0);
            ApplyDirLight(effect.DirectionalLight1, cfg.Light1);
            ApplyDirLight(effect.DirectionalLight2, cfg.Light2);
        }

        //========================================================================
        // 内部：默认 BasicEffect 路径的参数填写
        //========================================================================

        //内部桶绘制路径调用入口；现在等价于直接调用公开静态 DrawInstance
        private void DrawSingle(GraphicsDevice graphicsDevice, Model3DInstance instance, Matrix view, Matrix projection, bool isTransparent) {
            DrawInstance(graphicsDevice, instance, view, projection, GetInstanceLayer(instance), isTransparent);
        }

        //尝试反向找出实例所在层（便于上下文回填）；找不到时回落到默认层
        private Model3DLayer GetInstanceLayer(Model3DInstance instance) {
            if (instance != null) {
                return instance.Layer;
            }
            return Model3DLayer.AfterTiles;
        }

        //内部静态：拿到（必要时构造）默认 BasicEffect
        private static BasicEffect GetOrCreateDefaultEffect(GraphicsDevice graphicsDevice) {
            Model3DRenderer self = Instance;
            if (self == null) {
                return null;
            }
            if (!self.EnsureEffect(graphicsDevice)) {
                return null;
            }
            return self._effect;
        }

        //解析光照配置到静态 scratch 并触发订阅者
        private static Model3DLightingConfig ResolveLightingForInstance(Model3DInstance instance) {
            Model3DLightingConfig source = instance.LightingOverride ?? GlobalLighting;
            if (source != null) {
                source.CopyTo(_scratchLighting);
            }
            else {
                Model3DLightingConfig.CreateDefault().CopyTo(_scratchLighting);
            }
            ResolveLighting?.Invoke(instance, _scratchLighting);
            return _scratchLighting;
        }

        //按当前默认 BasicEffect 路径填入材质 / Tint / 光照 / 矩阵参数
        private static void ConfigureBasicEffectFor(BasicEffect effect, Model3DInstance instance, Model3DMaterial material
            , Matrix world, Matrix view, Matrix projection, Model3DLightingConfig lighting) {
            effect.World = world;
            effect.View = view;
            effect.Projection = projection;

            Color materialColor = material != null ? material.DiffuseColor : Color.White;
            float materialOpacity = material != null ? material.Opacity : 1f;
            Color combined = MultiplyColor(materialColor, instance.Tint);
            float finalAlpha = MathHelper.Clamp(materialOpacity * instance.Opacity * (instance.Tint.A / 255f), 0f, 1f);

            effect.DiffuseColor = combined.ToVector3();
            effect.Alpha = finalAlpha;

            if (material != null && material.HasTexture) {
                effect.TextureEnabled = true;
                effect.Texture = material.DiffuseTexture;
            }
            else {
                effect.TextureEnabled = false;
                effect.Texture = null;
            }

            if (instance.LightingEnabled) {
                effect.LightingEnabled = true;
                effect.EmissiveColor = lighting.EmissiveColor;
                effect.SpecularPower = lighting.SpecularPower;
                ApplyLighting(effect, lighting);
            }
            else {
                effect.LightingEnabled = false;
            }
        }

        /// <summary>
        /// 清空所有静态事件 / 委托订阅，避免下次加载时残留外部 mod 的引用
        /// <br/>仅供 <see cref="Model3DSystem"/> 卸载流程调用
        /// </summary>
        internal static void ClearAllSubscriptions() {
            ResolveLighting = null;
            PreDrawInstance = null;
            PostDrawInstance = null;
            PreDrawGroup = null;
            PostDrawGroup = null;
            OnLayerRendered = null;
            CompositeOverride = null;
        }

        /// <summary>
        /// 释放渲染器持有的 GPU 资源（仅供卸载时调用）
        /// </summary>
        internal void DisposeEffect() {
            if (_effect != null && !_effect.IsDisposed) {
                try {
                    _effect.Dispose();
                } catch {
                    //GPU 卸载阶段忽略异常
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
                    //GPU 卸载阶段忽略异常
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
                //PreserveContents 让我们清楚地控制每层都从 Clear 开始；用 Depth24 才能真正启用深度测试
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
