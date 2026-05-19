using InnoVault.GameSystem;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Terraria;
using Terraria.ModLoader;

namespace InnoVault.PRT
{
    /// <summary>
    /// 粒子或者说尘埃实体的基类，简称为PRT实体，继承它用于实现一些高自定义化的特殊粒子效果
    /// PRTLoader 负责管理游戏中粒子的加载、初始化以及纹理管理，支持通过Mod扩展粒子系统
    /// </summary>
    /// <remarks>
    /// 该类提供了一种全局的粒子系统管理方式通过各种静态字典和列表，PRTLoader 能够有效管理不同类型的粒子，
    /// 包括它们的ID、纹理、所属Mod以及实例数量
    /// </remarks>
    public class PRTLoader : ModSystem, IVaultLoader
    {
        #region Data
        /// <summary>
        /// 游戏中每个世界最多允许存在的粒子数量用于限制游戏中的粒子实体数量，防止性能问题
        /// </summary>
        public const int InGame_World_MaxPRTCount = short.MaxValue;
        /// <summary>
        /// 一个字典，用于将粒子类型（Type）映射到粒子ID每个粒子类型都会有一个唯一的ID，方便在系统中进行管理
        /// </summary>
        public static Dictionary<Type, int> PRT_TypeToID { get; private set; } = [];
        /// <summary>
        /// 一个字典，用于将粒子类型（Type）映射到其所属的Mod用于追踪哪些Mod添加了特定类型的粒子
        /// </summary>
        public static Dictionary<Type, Mod> PRT_TypeToMod { get; private set; } = [];
        /// <summary>
        /// 一个字典，将粒子ID映射到其对应的纹理（Texture2D）每个粒子都有一个与其ID对应的纹理，用于渲染粒子的外观
        /// </summary>
        public static Dictionary<int, Texture2D> PRT_IDToTexture { get; private set; } = [];
        /// <summary>
        /// 一个字典，将粒子ID映射到当前游戏世界中的实例数量用于记录每种粒子在当前世界中存在的数量，确保不超过最大限制
        /// </summary>
        public static Dictionary<int, int> PRT_IDToInGame_World_Count { get; private set; } = [];
        /// <summary>
        /// 一个字典，将粒子ID映射到其对应的粒子实例（BasePRT）用于管理每个粒子的实例对象，以便进行粒子的更新和渲染
        /// </summary>
        public static Dictionary<int, BasePRT> PRT_IDToInstances { get; private set; } = [];
        /// <summary>
        /// 一个字典，将粒子<see cref="Type"/>映射到其无参构造工厂委托
        /// 在<see cref="BasePRT.DoRegister"/>注册阶段编译生成，用于代替反射 <see cref="Activator.CreateInstance(Type)"/>
        /// </summary>
        internal static Dictionary<Type, Func<BasePRT>> PRT_TypeToFactory { get; private set; } = [];
        /// <summary>
        /// 每种粒子<see cref="BasePRT.ID"/>对应的对象池仅当<see cref="BasePRT.CanPool"/>为<see langword="true"/>时启用
        /// 池在<see cref="Load"/>中按已注册类型数量分配，在<see cref="Unload"/>中清空
        /// </summary>
        private static List<BasePRT>[] _prtPool;
        /// <summary>
        /// 单一类型在对象池中的最大缓存数量，过大占用内存，过小则池命中率不足
        /// </summary>
        /// <remarks>
        /// 总体内存上界 ≈ 启用 <see cref="BasePRT.CanPool"/> 的类型数 × <see cref="MaxPoolPerType"/> × 单实例字节
        /// 池满时多出的死亡实例会被丢弃给 GC，<see cref="TryReturnToPool"/> 会直接 <see langword="return"/>
        /// </remarks>
        internal const int MaxPoolPerType = 4096;
        /// <summary>
        /// 一个列表，存储所有活跃的粒子实例（BasePRT）用于批量管理和更新粒子实体
        /// </summary>
        public static List<BasePRT> PRTInstances { get; private set; } = [];
        /// <inheritdoc/>
        public static List<BasePRT> PRT_InGame_World_Inds;
        /// <inheritdoc/>
        public static List<BasePRT> PRT_AlphaBlend_Draw;
        /// <inheritdoc/>
        public static List<BasePRT> PRT_AdditiveBlend_Draw;
        /// <inheritdoc/>
        public static List<BasePRT> PRT_NonPremultiplied_Draw;
        /// <inheritdoc/>
        public static List<BasePRT> PRT_HasShader_Draw;

        internal static readonly PRTDrawModeEnum[] allDrawModes = (PRTDrawModeEnum[])Enum.GetValues(typeof(PRTDrawModeEnum));

        private static readonly List<VaultHookMethodCache<GlobalPRT>> hooks = [];
        internal static VaultHookMethodCache<GlobalPRT> HookOnSpawn;
        internal static VaultHookMethodCache<GlobalPRT> HookPreUpdatePRTAll;
        internal static VaultHookMethodCache<GlobalPRT> HookPostUpdatePRTAll;
        internal static VaultHookMethodCache<GlobalPRT> HookPreDrawPRT;
        internal static VaultHookMethodCache<GlobalPRT> HookPostDrawPRT;

        #endregion
        /// <summary>
        /// 加载和初始化数据
        /// </summary>
        public override void Load() {
            PRT_TypeToID = [];
            PRT_TypeToFactory = [];
            PRT_IDToTexture = [];
            PRT_IDToInGame_World_Count = [];
            PRT_IDToInstances = [];
            PRTInstances = [];
            PRT_InGame_World_Inds = [];

            PRTInstances = VaultUtils.GetDerivedInstances<BasePRT>(null, true);
            PRTInstances.RemoveAll(prt => !prt.CanLoad());

            foreach (var prt in PRTInstances) {
                prt.DoRegister();
            }
            VaultTypeRegistry<BasePRT>.CompleteLoading();

            //在所有ID分配完成后，按类型数量分配池槽，仅 CanPool 的类型实际会写入元素
            int typeCount = PRTInstances.Count;
            _prtPool = new List<BasePRT>[typeCount];
            for (int i = 0; i < typeCount; i++) {
                _prtPool[i] = [];
            }

            //初始化分层渲染桶，并把旧公开字段（PRT_AlphaBlend_Draw 等）指向默认层的桶以保持向后兼容
            //渲染时机由 PRTRenderHandle 通过 RenderHandleLoader 的多个钩子触发，不再注册 On_Main.DrawInfernoRings
            PRTRender.Initialize();
        }

        void IVaultLoader.SetupData() {
            HookOnSpawn = AddHook<Action<BasePRT>>(d => d.OnSpawn);
            HookPreUpdatePRTAll = AddHook<Func<bool>>(d => d.PreUpdatePRTAll);
            HookPostUpdatePRTAll = AddHook<Action>(d => d.PostUpdatePRTAll);
            HookPreDrawPRT = AddHook<Func<SpriteBatch, BasePRT, bool>>(d => d.PreDrawPRT);
            HookPostDrawPRT = AddHook<Action<SpriteBatch, BasePRT>>(d => d.PostDrawPRT);

            foreach (var prt in PRTInstances) {
                try {
                    prt.SetStaticDefaults();
                } catch (Exception ex) {
                    VaultMod.Instance.Logger.Error(ex);
                }
            }
        }

        void IVaultLoader.LoadAsset() {
            foreach (var prt in PRTInstances) {
                Type type = prt.GetType();
                string texturePath = type.Namespace.Replace('.', '/') + "/" + type.Name;
                if (prt.Texture != "") {
                    texturePath = prt.Texture;
                }
                if (ModContent.HasAsset(texturePath)) {
                    PRT_IDToTexture[PRT_TypeToID[type]] = ModContent.Request<Texture2D>(texturePath, AssetRequestMode.ImmediateLoad).Value;
                }
                else {
                    VaultMod.Instance.Logger.Warn($"Cannot find texture for PRT type {type.FullName} at path {texturePath}. Using placeholder texture.");
                    PRT_IDToTexture[PRT_TypeToID[type]] = VaultAsset.placeholder3.Value;
                }
            }
        }

        /// <summary>
        /// 卸载数据
        /// </summary>
        public override void Unload() {
            PRT_TypeToID = null;
            PRT_TypeToFactory = null;
            PRT_IDToTexture = null;
            PRT_IDToInGame_World_Count = null;
            PRT_IDToInstances = null;
            PRTInstances = null;
            PRT_InGame_World_Inds = null;
            PRT_AlphaBlend_Draw = null;
            PRT_AdditiveBlend_Draw = null;
            PRT_NonPremultiplied_Draw = null;
            PRT_HasShader_Draw = null;
            if (_prtPool != null) {
                for (int i = 0; i < _prtPool.Length; i++) {
                    _prtPool[i]?.Clear();
                }
                _prtPool = null;
            }
            PRTRender.Dispose();

            GlobalPRT.Instances.Clear();

            hooks.Clear();
            HookOnSpawn = null;
            HookPreUpdatePRTAll = null;
            HookPostUpdatePRTAll = null;
            HookPreDrawPRT = null;
            HookPostDrawPRT = null;
            VaultTypeRegistry<GlobalPRT>.ClearRegisteredVaults();
            VaultType<GlobalPRT>.TypeToMod.Clear();
            VaultTypeRegistry<BasePRT>.ClearRegisteredVaults();
            VaultType<BasePRT>.TypeToMod.Clear();
        }

        private static VaultHookMethodCache<GlobalPRT> AddHook<F>(Expression<Func<GlobalPRT, F>> func) where F : System.Delegate {
            VaultHookMethodCache<GlobalPRT> hook = VaultHookMethodCache<GlobalPRT>.Create(func);
            hooks.Add(hook);
            return hook;
        }

        /// <summary>
        /// 初始化所有粒子相关的列表和计数器
        /// </summary>
        public static void InitializeWorldPRT() {
            //确保所有与世界相关的列表是全新的
            PRT_InGame_World_Inds.Clear();
            //渲染桶（包括默认层指向的 PRT_AlphaBlend_Draw 等兼容字段）由 PRTRender 统一清空
            PRTRender.Reset();
            //重置粒子计数器，仅修改值不修改键，无需 ToList()
            foreach (var key in PRT_IDToInGame_World_Count.Keys) {
                PRT_IDToInGame_World_Count[key] = 0;
            }
            //世界切换时丢弃池中残留的旧粒子，避免跨世界对象复用导致的不一致
            if (_prtPool != null) {
                for (int i = 0; i < _prtPool.Length; i++) {
                    _prtPool[i]?.Clear();
                }
            }
        }

        /// <summary>
        /// 内部分配接口：按ID获取一个可用的<see cref="BasePRT"/>实例
        /// 当对应类型的<see cref="BasePRT.CanPool"/>为<see langword="true"/>且池非空时，从池中复用；否则使用工厂委托新建
        /// 仅用于<see cref="NewParticle(int, Vector2, Vector2, Color, float, int, int, int)"/>等内部生成路径
        /// 公开出口<see cref="GetPRTInstance{T}"/>/<see cref="GetPRTInstance(int)"/>/<see cref="BasePRT.Clone"/>始终返回全新对象，不参与池化
        /// </summary>
        /// <remarks>
        /// 与<see cref="TryReturnToPool"/>配合实现"出池<c>_fromPool=true</c> / 在池<c>_fromPool=false</c>"的两态机
        /// 取出时必须把<see cref="BasePRT._fromPool"/>设回 <see langword="true"/>，
        /// 否则该实例死亡时会被当作"非池来源"丢弃给GC，破坏复用语义
        /// </remarks>
        private static BasePRT Spawn(int id) {
            BasePRT template = PRT_IDToInstances[id];
            if (template.CanPool && _prtPool != null) {
                List<BasePRT> bucket = _prtPool[id];
                int last = bucket.Count - 1;
                if (last >= 0) {
                    BasePRT pooled = bucket[last];
                    bucket.RemoveAt(last);
                    //从池中取出后即视为"在用中"，归还时再切回 false——保证"在池"与"在用"互斥
                    pooled._fromPool = true;
                    return pooled;
                }
            }
            BasePRT created = template.Clone();
            //标记此实例归属于池系统，使得它在销毁时能被回收，而非交给GC
            if (template.CanPool) {
                created._fromPool = true;
            }
            return created;
        }

        /// <summary>
        /// 内部回收接口：将一个已死亡的<see cref="BasePRT"/>实例放回所属类型的池中
        /// 仅当实例确实来自池系统（<see cref="BasePRT._fromPool"/>为<see langword="true"/>）且未超过池容量上限时执行
        /// 调用<see cref="BasePRT.Reset"/>清理实例字段使其回到接近"全新"的状态供后续<see cref="Spawn(int)"/>复用
        /// </summary>
        /// <remarks>
        /// 防御性把<see cref="BasePRT._fromPool"/>置回 <see langword="false"/>后再 <see cref="List{T}.Add"/>，
        /// 这样即使外层逻辑因为重复 <see cref="AddParticle(BasePRT)"/> 等错误使用导致同一实例被回收两次，
        /// 第二次进入此函数时会因 <c>_fromPool==false</c> 而提前返回，避免同一物理对象在池中出现多份引用
        /// 池满时直接丢弃实例（交GC），<see cref="BasePRT.CanPool"/>注释中已说明此妥协
        /// </remarks>
        private static void TryReturnToPool(BasePRT particle) {
            if (particle == null || !particle._fromPool || _prtPool == null) {
                return;
            }
            int id = particle.ID;
            if ((uint)id >= (uint)_prtPool.Length) {
                return;
            }
            List<BasePRT> bucket = _prtPool[id];
            if (bucket.Count >= MaxPoolPerType) {
                //池满：保持 _fromPool=true 不变（此实例本帧反正会被GC回收，不影响下一次循环）
                return;
            }
            //先切到"在池"态再 Reset/Add：哪怕 Reset 抛异常，已切的状态也能阻止二次入池
            particle._fromPool = false;
            particle.Reset();
            bucket.Add(particle);
        }

        /// <inheritdoc/>
        public override void OnWorldLoad() => InitializeWorldPRT();
        /// <inheritdoc/>
        public override void OnWorldUnload() => InitializeWorldPRT();

        /// <summary>
        /// 根据指定的粒子绘制模式，返回对应的粒子实例列表
        /// 兼容性入口：返回的是 <see cref="PRTRenderLayer.BeforeInfernoRings"/> 默认层中对应 <paramref name="drawMode"/> 的桶
        /// 这与历史单层渲染时的语义一致；若需访问其它层级的桶请直接使用 <see cref="PRTRender"/> 接口
        /// </summary>
        /// <param name="drawMode">指定的粒子绘制模式 <see cref="PRTDrawModeEnum"/></param>
        /// <returns>与指定绘制模式对应的粒子实例列表，如果模式未定义则返回 null</returns>
        public static List<BasePRT> GetPRTInstancesByDrawMode(PRTDrawModeEnum drawMode) {
            return drawMode switch {
                PRTDrawModeEnum.AlphaBlend => PRT_AlphaBlend_Draw,
                PRTDrawModeEnum.AdditiveBlend => PRT_AdditiveBlend_Draw,
                PRTDrawModeEnum.NonPremultiplied => PRT_NonPremultiplied_Draw,
                _ => null
            };
        }

        /// <summary>
        /// 生成提供给世界的粒子实例
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>不推荐直接调用此方法</b><br/>
        /// 此接口接受一个由外部自行构造的实例，绕过了 <see cref="Spawn(int)"/> 的对象池与工厂委托路径，
        /// 导致每次生成都会触发 GC 分配，无法享受 <see cref="BasePRT.CanPool"/> 和 <see cref="PRT_TypeToFactory"/> 带来的复用优化
        /// </para>
        /// <para>
        /// <b>推荐用法：</b>改用 <see cref="NewParticle{T}(Vector2, Vector2, Color, float)"/> 系列方法，
        /// 它们内部经由 <see cref="Spawn(int)"/> 分发，自动命中对象池。如有额外字段需要初始化，
        /// 请在 <see cref="BasePRT"/> 子类上定义链式 <c>Configure(...)</c> 方法并在生成后调用：
        /// <code>
        /// PRTLoader.NewParticle&lt;MyPRT&gt;(position, velocity, color, scale)
        ///          .Configure(lifetime, extraParam);
        /// </code>
        /// </para>
        /// </remarks>
        public static void AddParticle(BasePRT particle) {
            if (Main.gamePaused || Main.dedServ || PRT_InGame_World_Inds == null) {
                return;
            }

            if (particle.PRTLayersMode == PRTLayersModeEnum.None) {
                return;
            }

            int id = GetParticleID(particle.GetType());

            if (PRT_IDToInGame_World_Count[id] >= particle.InGame_World_MaxCount
                || PRT_InGame_World_Inds.Count >= InGame_World_MaxPRTCount) {
                return;
            }

            particle.active = true;
            particle.ID = id;
            particle.SetProperty();

            PRT_InGame_World_Inds.Add(particle);

            foreach (var global in HookOnSpawn.Enumerate()) {
                global.OnSpawn(particle);
            }
        }

        /// <summary>
        /// 生成提供给世界的粒子实例
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>不推荐直接调用此方法</b><br/>
        /// 此接口接受一个由外部自行构造的实例，绕过了 <see cref="Spawn(int)"/> 的对象池与工厂委托路径，
        /// 导致每次生成都会触发 GC 分配，无法享受 <see cref="BasePRT.CanPool"/> 和 <see cref="PRT_TypeToFactory"/> 带来的复用优化
        /// </para>
        /// <para>
        /// <b>推荐用法：</b>改用 <see cref="NewParticle{T}(Vector2, Vector2, Color, float)"/> 系列方法，
        /// 它们内部经由 <see cref="Spawn(int)"/> 分发，自动命中对象池。如有额外字段需要初始化，
        /// 请在 <see cref="BasePRT"/> 子类上定义链式 <c>Configure(...)</c> 方法并在生成后调用：
        /// <code>
        /// PRTLoader.NewParticle&lt;MyPRT&gt;(position, velocity, color, scale)
        ///          .Configure(lifetime, extraParam);
        /// </code>
        /// </para>
        /// </remarks>
        public static void AddParticle(BasePRT particle, bool setProperty) {
            if (Main.gamePaused || Main.dedServ || PRT_InGame_World_Inds == null) {
                return;
            }

            if (particle.PRTLayersMode == PRTLayersModeEnum.None) {
                return;
            }

            int id = GetParticleID(particle.GetType());

            if (PRT_IDToInGame_World_Count[id] >= particle.InGame_World_MaxCount
                || PRT_InGame_World_Inds.Count >= InGame_World_MaxPRTCount) {
                return;
            }

            particle.active = true;
            particle.ID = id;

            if (setProperty) {
                particle.SetProperty();
            }

            PRT_InGame_World_Inds.Add(particle);

            foreach (var global in HookOnSpawn.Enumerate()) {
                global.OnSpawn(particle);
            }
        }

        /// <summary>
        /// 使用指定的属性初始化并添加一个新粒子到粒子系统中
        /// </summary>
        /// <param name="prtEntity">要初始化和添加的粒子实例</param>
        /// <param name="position">粒子在二维空间中的初始位置</param>
        /// <param name="velocity">粒子的初始速度向量</param>
        /// <param name="color">粒子的颜色，默认为默认颜色</param>
        /// <param name="scale">粒子的缩放比例，默认为1</param>
        /// <param name="ai0">粒子的自定义属性 ai0，默认为0</param>
        /// <param name="ai1">粒子的自定义属性 ai1，默认为0</param>
        /// <param name="ai2">粒子的自定义属性 ai2，默认为0</param>
        public static BasePRT NewParticle(BasePRT prtEntity, Vector2 position, Vector2 velocity
            , Color color = default, float scale = 1f, int ai0 = 0, int ai1 = 0, int ai2 = 0) {
            prtEntity.Position = position;
            prtEntity.Velocity = velocity;
            prtEntity.Scale = scale;
            prtEntity.Color = color;
            prtEntity.ai[0] = ai0;
            prtEntity.ai[1] = ai1;
            prtEntity.ai[2] = ai2;
            AddParticle(prtEntity);
            return prtEntity;
        }

        /// <summary>
        /// 使用指定的属性初始化并添加一个新粒子到粒子系统中
        /// </summary>
        /// <param name="prtID">要初始化和添加的粒子ID</param>
        /// <param name="position">粒子在二维空间中的初始位置</param>
        /// <param name="velocity">粒子的初始速度向量</param>
        /// <param name="color">粒子的颜色，默认为默认颜色</param>
        /// <param name="scale">粒子的缩放比例，默认为1</param>
        /// <param name="ai0">粒子的自定义属性 ai0，默认为0</param>
        /// <param name="ai1">粒子的自定义属性 ai1，默认为0</param>
        /// <param name="ai2">粒子的自定义属性 ai2，默认为0</param>
        public static BasePRT NewParticle(int prtID, Vector2 position, Vector2 velocity
            , Color color = default, float scale = 1f, int ai0 = 0, int ai1 = 0, int ai2 = 0) {
            BasePRT prtEntity = Spawn(prtID);
            prtEntity.Position = position;
            prtEntity.Velocity = velocity;
            prtEntity.Scale = scale;
            prtEntity.Color = color;
            prtEntity.ai[0] = ai0;
            prtEntity.ai[1] = ai1;
            prtEntity.ai[2] = ai2;
            AddParticle(prtEntity);
            return prtEntity;
        }

        /// <summary>
        /// 使用指定的属性初始化并添加一个新粒子到粒子系统中
        /// </summary>
        /// <param name="center"></param>
        /// <param name="velocity"></param>
        /// <param name="type"></param>
        /// <param name="newColor"></param>
        /// <param name="Scale"></param>
        /// <returns></returns>
        public static BasePRT NewParticle(Vector2 center, Vector2 velocity, int type, Color newColor = default, float Scale = 1f) {
            BasePRT prtEntity = Spawn(type);
            prtEntity.Position = center;
            prtEntity.Velocity = velocity;
            prtEntity.Color = newColor;
            prtEntity.Scale = Scale;
            AddParticle(prtEntity);
            return prtEntity;
        }

        /// <summary>
        /// 使用指定的属性初始化并添加一个新粒子到粒子系统中
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="center"></param>
        /// <param name="velocity"></param>
        /// <param name="newColor"></param>
        /// <param name="Scale"></param>
        /// <returns></returns>
        public static T NewParticle<T>(Vector2 center, Vector2 velocity, Color newColor = default, float Scale = 1f) where T : BasePRT {
            T prtEntity = (T)Spawn(GetParticleID<T>());
            prtEntity.Position = center;
            prtEntity.Velocity = velocity;
            prtEntity.Color = newColor;
            prtEntity.Scale = Scale;
            AddParticle(prtEntity);
            return prtEntity;
        }

        /// <summary>
        /// 获得目标粒子的实例克隆
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static T GetPRTInstance<T>() where T : BasePRT => PRT_IDToInstances[GetParticleID<T>()].Clone() as T;
        /// <summary>
        /// 获得目标粒子的实例克隆
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public static BasePRT GetPRTInstance(int id) => PRT_IDToInstances[id].Clone();

        /// <summary>
        /// 初始化目标粒子实例并设置其属性
        /// </summary>
        /// <typeparam name="T">粒子类型，必须继承自 BasePRT</typeparam>
        /// <param name="position">粒子的位置</param>
        /// <param name="velocity">粒子的速度</param>
        /// <param name="color">粒子的颜色</param>
        /// <param name="scale">粒子的缩放比例</param>
        /// <returns>带有指定属性的粒子实例</returns>
        /// <remarks>
        /// 此方法不仅会克隆一个目标粒子的实例，还会对其进行初始化，
        /// 包括设置位置、速度、颜色和缩放比例等属性
        /// 使用此方法可以快速创建和设置粒子对象，适用于需要动态生成粒子效果的场景
        /// </remarks>
        public static T CreateAndInitializePRT<T>(Vector2 position, Vector2 velocity, Color color, float scale) where T : BasePRT {
            T prt = GetPRTInstance<T>();
            prt.active = true;
            prt.ID = GetParticleID<T>();
            prt.SetProperty();
            prt.Position = position;
            prt.Velocity = velocity;
            prt.Color = color;
            prt.Scale = scale;
            return prt;
        }

        /// <summary>
        /// 更新在所有实体之前，这个进行独立的PRT粒子数量计数，在更新进行加法计数，这样才能保证弹幕、玩家、等程序可以获取正确的粒子数量
        /// </summary>
        public override void PreUpdateEntities() {
            if (Main.dedServ) {//不要在服务器上更新逻辑
                return;
            }

            foreach (BasePRT particle in PRT_InGame_World_Inds) {
                if (particle == null || !particle.active) {
                    continue;
                }

                PRT_IDToInGame_World_Count[particle.ID]++;
            }
        }

        /// <summary>
        /// 在最后调用更新逻辑，进行CG机制，并重置粒子计数
        /// </summary>
        public override void PostUpdateEverything() {
            if (Main.dedServ) {//不要在服务器上更新逻辑
                return;
            }

            bool result = true;
            foreach (var global in HookPreUpdatePRTAll.Enumerate()) {
                if (!global.PreUpdatePRTAll()) {
                    result = false;
                }
            }

            if (result) {
                for (int i = 0; i < PRT_InGame_World_Inds.Count; i++) {
                    BasePRT particle = PRT_InGame_World_Inds[i];

                    if (particle == null || !particle.active) {
                        continue;
                    }

                    try {
                        UpdateParticleVelocity(particle);
                        UpdateParticleTime(particle);
                        particle.AI();
                    } catch (Exception) {
                        VaultMod.Instance.Logger.Info($"ERROR:{particle} IS UPDATA");
                        particle.active = false;
                        continue;
                    }

                    if (particle.Lifetime >= 0 && particle.Time >= particle.Lifetime) {
                        particle.active = false;
                        continue;
                    }

                    if (particle.ShouldKillWhenOffScreen && !VaultUtils.IsPointOnScreen(particle.Position - Main.screenPosition, 160)) {
                        particle.active = false;
                    }
                }
            }

            foreach (var global in HookPostUpdatePRTAll.Enumerate()) {
                global.PostUpdatePRTAll();
            }

            foreach (var particle in PRTInstances) {
                PRT_IDToInGame_World_Count[particle.ID] = 0;
            }

            //手写双指针剔除非活跃粒子，并把可池化的实例归还池系统
            //相比 RemoveAll(lambda) 省去委托分配、避免 null 比较，并把"回收"合并到这一次扫描里
            List<BasePRT> list = PRT_InGame_World_Inds;
            int write = 0;
            for (int read = 0; read < list.Count; read++) {
                BasePRT p = list[read];
                if (p != null && p.active) {
                    list[write++] = p;
                    continue;
                }
                TryReturnToPool(p);
            }
            if (write < list.Count) {
                list.RemoveRange(write, list.Count - write);
            }

            //粒子集合在本帧已发生变化（新增、销毁、AI 改写字段），通知 PRTRender 在下一次绘制时重建桶
            PRTRender.MarkBucketsDirty();
        }

        private static void UpdateParticleVelocity(BasePRT particle) {
            if (particle.ShouldUpdatePosition()) {
                particle.Position += particle.Velocity;
            }
        }

        private static void UpdateParticleTime(BasePRT particle) => particle.Time++;

        /// <summary>
        /// 获取这个<see cref="BasePRT"/>类型的ID，每一个PRT类型都拥有一个独一无二的ID
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static int GetParticleID<T>() where T : BasePRT => PRT_TypeToID[typeof(T)];
        /// <summary>
        /// 获取这个<see cref="BasePRT"/>类型的ID，每一个PRT类型都拥有一个独一无二的ID
        /// </summary>
        /// <param name="sType"></param>
        /// <returns></returns>
        public static int GetParticleID(Type sType) => PRT_TypeToID[sType];

        /// <summary>
        /// 根据 <see cref="PRTDrawModeEnum"/> 获取对应的 <see cref="BlendState"/>
        /// 兼容性入口转发到 <see cref="PRTRender.GetBlendStateFor"/>
        /// </summary>
        /// <param name="drawMode">粒子的绘制模式</param>
        /// <returns>对应的 BlendState 实例</returns>
        public static BlendState GetBlendStateFor(PRTDrawModeEnum drawMode) => PRTRender.GetBlendStateFor(drawMode);

        /// <summary>
        /// 根据指定的绘制模式 <see cref="PRTDrawModeEnum"/>，为 <see cref="SpriteBatch"/> 设置适当的渲染状态并开始绘制
        /// 兼容性入口转发到 <see cref="PRTRender.BeginDrawingWithMode"/>
        /// </summary>
        /// <param name="drawMode">绘制模式枚举 <see cref="PRTDrawModeEnum"/></param>
        /// <param name="spriteBatch">用于进行绘制操作的 <see cref="SpriteBatch"/></param>
        /// <param name="spriteSortMode">是否立即应用绘制，默认为 <see cref="SpriteSortMode.Deferred"/></param>
        public static void BeginDrawingWithMode(PRTDrawModeEnum drawMode, SpriteBatch spriteBatch, SpriteSortMode spriteSortMode = SpriteSortMode.Deferred)
            => PRTRender.BeginDrawingWithMode(drawMode, spriteBatch, spriteSortMode);

        /// <summary>
        /// 完整的处理一个粒子的绘制操作
        /// 兼容性入口转发到 <see cref="PRTRender.PRTInstanceDraw"/>，被 <see cref="PRTGroup.Draw"/> 等处使用
        /// </summary>
        /// <param name="spriteBatch"></param>
        /// <param name="particle"></param>
        public static void PRTInstanceDraw(SpriteBatch spriteBatch, BasePRT particle) => PRTRender.PRTInstanceDraw(spriteBatch, particle);

        /// <summary>
        /// 用于绘制使用Shader效果的粒子集合
        /// 兼容性入口转发到 <see cref="PRTRender.HandleShaderPRTDrawList"/>
        /// </summary>
        /// <param name="spriteBatch">画布实例</param>
        /// <param name="particles">传入的粒子集合，其中所有的粒子要求<see cref="BasePRT.shader"/>不为<see langword="null"/></param>
        public static void HanderHasShaderPRTDrawList(SpriteBatch spriteBatch, List<BasePRT> particles)
            => PRTRender.HandleShaderPRTDrawList(spriteBatch, particles);

        /// <summary>
        /// 所有PRT的绘制更新都在这里
        /// 兼容性入口：现在 PRT 的渲染由 <see cref="PRTRender"/> 在 <see cref="RenderHandles.RenderHandleLoader"/> 的多个钩子上分层触发
        /// 此方法转发到 <see cref="PRTRender.DrawAll"/>，一次性绘制所有层级，供旧调用方仍可手动触发
        /// </summary>
        /// <param name="spriteBatch"></param>
        public static void Draw(SpriteBatch spriteBatch) => PRTRender.DrawAll(spriteBatch);

        /// <summary>
        /// 给出可用粒子槽的数量。当一次需要多个粒子来制作效果，并且不希望由于缺乏粒子槽而只绘制一半时非常有用
        /// </summary>
        /// <returns></returns>
        public static int NumberUsablePRT() {
            return Main.dedServ || PRT_InGame_World_Inds == null ? 0 : InGame_World_MaxPRTCount - PRT_InGame_World_Inds.Count;
        }
    }
}
