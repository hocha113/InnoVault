using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using Terraria.Graphics.Shaders;
using Terraria.ModLoader;

namespace InnoVault.PRT
{
    /// <summary>
    /// 粒子基类，继承它用于实现各种高度自定义的粒子效果
    /// </summary>
    public abstract class BasePRT
    {
        /// <summary>
        /// 这个粒子使用什么纹理
        /// </summary>
        public virtual string Texture => "";
        /// <summary>
        /// 这种粒子在世界的最大存在数量是多少，默认4000，不要将其设置为大于20000的值
        /// 因为存在<see cref="PRTLoader.InGame_World_MaxPRTCount"/>的全局上限
        /// </summary>
        public virtual int InGame_World_MaxCount => 4000;
        /// <summary>
        /// 这个粒子来自什么模组
        /// </summary>
        public virtual Mod Mod => PRTLoader.PRT_TypeToMod[GetType()];
        /// <summary>
        /// 获取加载的粒子纹理资源
        /// </summary>
        public Texture2D TexValue => PRTLoader.PRT_IDToTexture[ID];
        /// <summary>
        /// 一个通用的全局帧索引
        /// </summary>
        public Rectangle Frame = default;
        /// <summary>
        /// 该粒子是否活跃，如果为<see langword="false"/>，那么将在下一次更新中被删除
        /// </summary>
        public bool active = false;
        /// <summary>
        /// 由一般粒子处理程序注册的粒子类型的ID,这是在粒子处理器loadsl时自动设置的
        /// </summary>
        public int ID;
        /// <summary>
        /// 这个粒子已经存在的帧数,一般情况下,不需要手动更新它
        /// </summary>
        public int Time;
        /// <summary>
        /// 如果你想让你的粒子在达到其最大寿命时自动移除,将此设置为<see langword="true"/>
        /// </summary>
        public bool SetLifetime = false;
        /// <summary>
        /// 一个粒子可以存活的最大时间,单位为tick,一般如果想让其有效需要先将<see cref="SetLifetime"/>设置为<see langword="true"/>
        /// </summary>
        public int Lifetime = 0;
        /// <summary>
        /// 存活时间比例
        /// </summary>
        public float LifetimeCompletion => Lifetime != 0 ? Time / (float)Lifetime : 0;
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
        /// 这个粒子将使用的着色器数据，默认为<see langword="null"/>
        /// </summary>
        public ArmorShaderData shader;
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
        /// 克隆这个实例，注意，克隆出的新对象与原实例将不再具有任何引用关系
        /// </summary>
        /// <returns></returns>
        public BasePRT Clone() => (BasePRT)Activator.CreateInstance(GetType());

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
