using InnoVault.GameSystem;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Linq.Expressions;
using Terraria.Graphics.Shaders;
using Terraria.ModLoader;
using static InnoVault.PRT.PRTLoader;

namespace InnoVault.PRT
{
    /// <summary>
    /// 粒子基类，继承它用于实现各种高度自定义的粒子效果
    /// <br>该API的使用介绍:<see href="https://innovault.wiki/cn/content/prt/"/></br>
    /// </summary>
    public abstract class BasePRT : VaultType<BasePRT>
    {
        #region Data
        /// <inheritdoc/>
        protected override bool AutoMapMod => false;
        /// <inheritdoc/>
        protected override bool AutoVaultRegistryFinishLoading => false;
        /// <inheritdoc/>
        protected override bool AutoVaultRegistryRegister => false;
        /// <summary>
        /// 这个粒子使用什么纹理
        /// </summary>
        public virtual string Texture => "";
        /// <summary>
        /// 这种粒子在世界的最大存在数量是多少，默认4000，不要将其设置为大于20000的值
        /// 因为存在<see cref="InGame_World_MaxPRTCount"/>的全局上限
        /// </summary>
        public virtual int InGame_World_MaxCount => 4000;
        /// <summary>
        /// 获取加载的粒子纹理资源
        /// </summary>
        public Texture2D TexValue => PRT_IDToTexture[ID];
        /// <summary>
        /// 该粒子所来自的模组的实例
        /// </summary>
        public new Mod Mod => TypeToMod[GetType()];
        /// <summary>
        /// 这个粒子的内部填充名
        /// </summary>
        public new string FullName => GetFullName(Mod.Name, Name);
        /// <summary>
        /// 该粒子的全局唯一ID
        /// </summary>
        public int ID;
        /// <summary>
        /// 一个通用的全局帧索引
        /// </summary>
        public Rectangle Frame = default;
        /// <summary>
        /// 该粒子是否活跃，如果为<see langword="false"/>，那么将在下一次更新中被删除
        /// </summary>
        public bool active = false;
        /// <summary>
        /// 粒子离开屏幕后是否自动销毁，默认为<see langword="true"/>
        /// </summary>
        public bool ShouldKillWhenOffScreen = true;
        /// <summary>
        /// 这个粒子已经存在的帧数,一般情况下,不需要手动更新它
        /// </summary>
        public int Time;
        /// <summary>
        /// 一个粒子可以存活的最大时间,单位为tick,如果为默认值-1或者是其他小于0的值，则认定不启用寿命计时
        /// </summary>
        public int Lifetime = -1;
        /// <summary>
        /// 存活时间比例
        /// </summary>
        public float LifetimeCompletion {
            get {
                if (Lifetime <= 0) {
                    return 0;
                }
                return Time / (float)Lifetime;
            }
        }
        /// <summary>
        /// 渐变值，在多数情况下用于插值计算，意义上等同于"sengs"
        /// </summary>
        public float Opacity;
        /// <summary>
        /// 一个粒子在世界中的位置，这不是在粒子集的上下文中使用的，因为所有的粒子都是根据它们相对于集合原点的位置来计算的
        /// </summary>
        public Vector2 Position;
        /// <summary>
        /// 这个粒子的客观移动速度，一般用于位置更新
        /// </summary>
        public Vector2 Velocity;
        /// <summary>
        /// 应该取得的中心值
        /// </summary>
        public Vector2 Origin;
        /// <summary>
        /// 绘制所通用的全局颜色
        /// </summary>
        public Color Color;
        /// <summary>
        /// 旋转角度
        /// </summary>
        public float Rotation;
        /// <summary>
        /// 体积缩放，并不推荐使用这个属性来控制粒子的死亡
        /// </summary>
        public float Scale;
        /// <summary>
        /// 粒子的AI数值，用于交互数据，便于实现更加复杂的行为
        /// </summary>
        public float[] ai = new float[3];
        /// <summary>
        /// 历史位置
        /// </summary>
        public Vector2[] oldPositions;
        /// <summary>
        /// 历史旋转点
        /// </summary>
        public float[] oldRotations;
        /// <summary>
        /// 绘制模式，默认为<see cref="PRTDrawModeEnum.AlphaBlend"/>
        /// </summary>
        public PRTDrawModeEnum PRTDrawMode = PRTDrawModeEnum.AlphaBlend;
        /// <summary>
        /// 更新模式，默认为<see cref="PRTLayersModeEnum.InWorld"/>
        /// </summary>
        public PRTLayersModeEnum PRTLayersMode = PRTLayersModeEnum.InWorld;
        /// <summary>
        /// 渲染层级，决定该粒子由 <see cref="PRTRender"/> 在哪个绘制阶段交给 <see cref="PRTRender.DrawLayer"/> 渲染
        /// 默认为 <see cref="PRTRenderLayer.BeforeInfernoRings"/>，与历史 PRT 单点渲染时机一致
        /// 与决定混合方式的 <see cref="PRTDrawMode"/> 正交，可独立配置
        /// </summary>
        /// <remarks>
        /// 桶按帧懒加载：只有 <see cref="PRTLoader.PostUpdateEverything"/> 末尾会标脏并在下一帧首个渲染钩子里重建桶
        /// 因此<b>在 <see cref="AI"/> 或更晚的钩子里修改本字段，要等到下一帧才会真正生效</b>
        /// 推荐在 <see cref="SetProperty"/> 中（粒子刚生成时）一次性设定，避免出现"切换帧"视觉断层
        /// </remarks>
        public PRTRenderLayer RenderLayer = PRTRenderLayer.BeforeInfernoRings;
        /// <summary>
        /// 这个粒子将使用的着色器数据，默认为<see langword="null"/>
        /// </summary>
        public ArmorShaderData shader;
        /// <summary>
        /// 是否启用对象池复用本实例的子类型默认为<see langword="false"/>
        /// 子类显式重写为<see langword="true"/>表明自身的所有可变字段都会在<see cref="SetProperty"/>或<see cref="Reset"/>中被正确初始化
        /// 不重写则保持现状语义（每次<see cref="PRTLoader.NewParticle(int, Vector2, Vector2, Color, float, int, int, int)"/>都会通过工厂委托新建对象）
        /// </summary>
        /// <remarks>
        /// 启用池化的子类有两点<b>额外约束</b>，违反会出现脏数据或不工作：
        /// <list type="number">
        /// <item><b>缓存数组类初始化必须放 <see cref="SetProperty"/>，不能放构造函数</b>
        ///     被池回收过的实例不会再次走构造函数；<see cref="Reset"/> 会把 <see cref="oldPositions"/>/<see cref="oldRotations"/>
        ///     置为 <see langword="null"/>，依赖它们的拖尾/插值代码必须在 <see cref="SetProperty"/> 中重新调用
        ///     <see cref="InitializePositionCache"/>/<see cref="InitializeRotationCache"/>/<see cref="InitializeCaches"/></item>
        /// <item><b>新增的字段必须在重写 <see cref="Reset"/> 时清零</b>
        ///     基类 <see cref="Reset"/> 只覆盖 <see cref="BasePRT"/> 自身的字段，子类自定义字段需要 <c>base.Reset()</c> 后自行复位</item>
        /// </list>
        /// 池容量为 <see cref="PRTLoader.MaxPoolPerType"/>（默认 4096），超过部分的"死亡"实例会被直接丢弃给 GC
        /// 这意味着在大量同类粒子集中死亡时，一旦池满，多余实例的复用收益就退化为普通分配/回收的开销
        /// 总体内存上界 ≈ 池化类型数 × <see cref="PRTLoader.MaxPoolPerType"/> × 单实例字节，按需评估
        /// </remarks>
        public virtual bool CanPool => false;
        /// <summary>
        /// 标记此实例由池系统创建当死亡被<see cref="PRTLoader"/>移除时会回收到池中
        /// 用户自行<see langword="new"/>构造并通过<see cref="PRTLoader.AddParticle(BasePRT)"/>提交的实例不会被归池
        /// </summary>
        internal bool _fromPool;

        #endregion
        /// <summary>
        /// 封闭内容
        /// </summary>
        protected sealed override void VaultRegister() {

        }

        /// <summary>
        /// 加载内容
        /// </summary>
        public sealed override void VaultSetup() {

        }

        //因为粒子在设计理念中会包含含参数构造函数，这些会让默认的 Register 自动加载钩子无法捕获实例，所以这里自己写一个子类型捕获
        internal void DoRegister() {
            Type type = GetType();
            ID = PRT_TypeToID.Count;
            PRT_TypeToID[type] = ID;
            TypeToMod[type] = VaultUtils.FindModByType(type);
            PRT_IDToInstances.Add(ID, this);
            PRT_IDToInGame_World_Count.Add(ID, 0);
            //预编译无参构造为委托，避免在 Clone/Spawn 热路径上反复触发 Activator.CreateInstance 的反射开销
            //如果类型缺少公共无参构造，这里会在编译期抛出异常，与原 Activator 报错时机一致
            try {
                PRT_TypeToFactory[type] = Expression.Lambda<Func<BasePRT>>(Expression.New(type)).Compile();
            } catch (Exception ex) {
                VaultMod.Instance.Logger.Warn($"PRT factory compile failed for {type.FullName}, will fall back to Activator: {ex.Message}");
            }
            VaultTypeRegistry<BasePRT>.Register(this);//这里提取手动加载好所有的粒子实例
        }

        /// <summary>
        /// 仅仅在生成粒子的时候被执行一次，用于简单的内部初始化数据
        /// </summary>
        public virtual void SetProperty() { }
        /// <summary>
        /// 每次更新粒子处理程序时调用。粒子的速度会自动添加到它的位置，它的时间也会自动增加
        /// </summary>
        public virtual void AI() { }
        /// <summary>
        /// 从处理程序中移除粒子
        /// </summary>
        public void Kill() => active = false;
        /// <summary>
        /// 运行在默认绘制之前
        /// </summary>
        /// <param name="spriteBatch"></param>
        public virtual bool PreDraw(SpriteBatch spriteBatch) { return true; }
        /// <summary>
        /// 运行在默认绘制之后
        /// </summary>
        /// <param name="spriteBatch"></param>
        public virtual void PostDraw(SpriteBatch spriteBatch) { }
        /// <summary>
        /// 不会自动调用，用于在某些情景下手动调用以处理UI效果的渲染
        /// </summary>
        /// <param name="spriteBatch"></param>
        public virtual void DrawInUI(SpriteBatch spriteBatch) { }
        /// <summary>
        /// 克隆这个实例，注意，克隆出的新对象与原实例将不再具有任何引用关系
        /// 该方法始终返回一个全新的、字段为类型默认值的实例（不会从对象池获取脏对象），保持公开 API 语义不变
        /// 内部实现优先使用<see cref="PRTLoader.PRT_TypeToFactory"/>中预编译好的工厂委托；若类型未注册则回退到<see cref="Activator.CreateInstance(Type)"/>以保证健壮性
        /// </summary>
        /// <returns></returns>
        public BasePRT Clone() {
            Type type = GetType();
            if (PRT_TypeToFactory != null && PRT_TypeToFactory.TryGetValue(type, out Func<BasePRT> factory)) {
                return factory();
            }
            return (BasePRT)Activator.CreateInstance(type);
        }
        /// <summary>
        /// 当本实例被对象池回收前调用，用于把<see cref="BasePRT"/>自身的所有可变字段重置为构造默认值
        /// 默认实现已经覆盖了基类全部字段子类如果引入了自己的状态字段，且声明<see cref="CanPool"/>为<see langword="true"/>，
        /// 则<b>必须</b>重写此方法把这些字段也复位（推荐先<see langword="base"/>.<see cref="Reset"/>，再恢复自身字段），
        /// 否则下一次从池中取出时会保留上一辈子的脏数据
        /// </summary>
        public virtual void Reset() {
            Frame = default;
            active = false;
            ShouldKillWhenOffScreen = true;
            Time = 0;
            Lifetime = -1;
            Opacity = 0;
            Position = default;
            Velocity = default;
            Origin = default;
            Color = default;
            Rotation = 0;
            Scale = 0;
            //ai 数组保留，仅清零，避免每次回收都重新分配
            if (ai != null) {
                for (int i = 0; i < ai.Length; i++) {
                    ai[i] = 0;
                }
            }
            //历史轨迹缓存所有权可能与具体子类相关，统一释放即可，需要的子类会在 SetProperty 中重新初始化
            oldPositions = null;
            oldRotations = null;
            PRTDrawMode = PRTDrawModeEnum.AlphaBlend;
            PRTLayersMode = PRTLayersModeEnum.InWorld;
            RenderLayer = PRTRenderLayer.BeforeInfernoRings;
            shader = null;
            //ID 与 _fromPool 不在此处重置：ID 用于回到正确的池槽，_fromPool 是身份标记
        }
        /// <summary>
        /// 粒子是否应该在逻辑更新中自动更新位置数据，默认为<see langword="true"/>
        /// </summary>
        /// <returns></returns>
        public virtual bool ShouldUpdatePosition() => true;

        /// <summary>
        /// 初始化位置缓存数组，将所有元素初始化为当前的位置
        /// </summary>
        /// <param name="length">缓存数组的长度</param>
        public void InitializePositionCache(int length) {
            oldPositions = new Vector2[length];
            for (int i = 0; i < length; i++) {
                oldPositions[i] = Position;
            }
        }
        /// <summary>
        /// 初始化旋转角缓存数组，将所有元素初始化为当前的旋转角度
        /// </summary>
        /// <param name="length">缓存数组的长度</param>
        public void InitializeRotationCache(int length) {
            oldRotations = new float[length];
            for (int i = 0; i < length; i++) {
                oldRotations[i] = Rotation;
            }
        }
        /// <summary>
        /// 初始化位置和旋转角缓存数组，将所有位置元素初始化为当前位置，旋转角度元素初始化为当前旋转角度
        /// </summary>
        /// <param name="length">缓存数组的长度</param>
        public void InitializeCaches(int length) {
            oldPositions = new Vector2[length];
            oldRotations = new float[length];
            for (int i = 0; i < length; i++) {
                oldPositions[i] = Position;
                oldRotations[i] = Rotation;
            }
        }
        /// <summary>
        /// 更新位置缓存数组，将缓存中的数据向前移一位，并在末尾记录当前位置
        /// </summary>
        /// <param name="length">缓存数组中有效记录的长度</param>
        public void UpdatePositionCache(int length) {
            if (oldPositions is null || length > oldPositions.Length) {
                return;
            }

            for (int i = 0; i < length - 1; i++) {
                oldPositions[i] = oldPositions[i + 1];
            }

            oldPositions[length - 1] = Position;
        }
        /// <summary>
        /// 更新旋转角缓存数组，将缓存中的数据向前移一位，并在末尾记录当前的旋转角度
        /// </summary>
        /// <param name="length">缓存数组中有效记录的长度</param>
        public void UpdateRotationCache(int length) {
            if (oldRotations is null || length > oldRotations.Length) {
                return;
            }

            for (int i = 0; i < length - 1; i++) {
                oldRotations[i] = oldRotations[i + 1];
            }

            oldRotations[length - 1] = Rotation;
        }
    }
}
