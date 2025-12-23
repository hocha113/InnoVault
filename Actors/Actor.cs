using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;

namespace InnoVault.Actors
{
    /// <summary>
    /// 可自定义行为的实体基类
    /// </summary>
    public abstract class Actor : VaultType<Actor>
    {
        #region Data
        /// <summary>
        /// 从类型映射到实例
        /// </summary>
        public static Dictionary<int, Actor> IDToInstance { get; internal set; } = [];
        /// <summary>
        /// 从类型映射到实例
        /// </summary>
        public static Dictionary<Type, int> TypeToID { get; internal set; } = [];
        /// <summary>
        /// 该实体的全局唯一ID
        /// </summary>
        public int ID;
        /// <summary>
        /// 该实体在其特定数组中的索引，这些数组跟踪世界中的实体
        /// </summary>
        public int WhoAmI;
        /// <summary>
        /// 如果为 true，则实体实际上存在于游戏世界中。在特定的实体数组中，如果 active 为 false，则该实体是垃圾数据
        /// </summary>
        [SyncVar]
        public bool Active;
        /// <summary>
        /// 该实体的命中箱的高度，以像素为单位
        /// </summary>
        [SyncVar]
        public int Width;
        /// <summary>
        /// 该实体的命中箱的高度，以像素为单位
        /// </summary>
        [SyncVar]
        public int Height;
        /// <summary>
        /// 该实体的旋转角度，以弧度为单位
        /// </summary>
        [SyncVar]
        public float Rotation;
        /// <summary>
        /// 该实体的缩放比例
        /// </summary>
        [SyncVar]
        public float Scale = 1f;
        /// <summary>
        /// 该实体在世界坐标中的位置，注意这对应于实体的左上角。对于需要实体中心位置的逻辑，请改用 Center
        /// </summary>
        [SyncVar]
        public Vector2 Position;
        /// <summary>
        /// 该实体在每个刻度的世界坐标中的速度
        /// </summary>
        [SyncVar]
        public Vector2 Velocity;
        /// <summary>
        /// 该实体的命中箱的大小
        /// </summary>
        public Vector2 Size => new Vector2(Width, Height) * Scale;
        /// <summary>
        /// 该实体的命中箱
        /// </summary>
        public virtual Rectangle HitBox => Position.GetRectangle(Size);
        /// <summary>
        /// 该实体在世界坐标中的中心位置
        /// </summary>
        public virtual Vector2 Center => Position + Size / 2;
        /// <summary>
        /// 如果为 true，则在下一次网络更新时同步此实体的数据
        /// </summary>
        public bool NetUpdate;
        /// <summary>
        /// 该实体的绘制层级
        /// </summary>
        public ActorDrawLayer DrawLayer = ActorDrawLayer.Default;
        #endregion
        /// <summary>
        /// 注册内容
        /// </summary>
        protected sealed override void VaultRegister() {
            ID = Instances.Count;
            Instances.Add(this);
            TypeToID[GetType()] = ID;
            TypeToInstance[GetType()] = this;
            IDToInstance[ID] = this;
            ByID[ID] = new Dictionary<Type, Actor> {
                { GetType(), this }
            };
        }
        /// <summary>
        /// 封闭内容
        /// </summary>
        public sealed override void VaultSetup() {
            SetStaticDefaults();
        }
        /// <summary>
        /// 克隆这个Actor实例
        /// </summary>
        /// <returns>克隆的Actor实例</returns>
        public Actor Clone() => (Actor)Activator.CreateInstance(GetType());
        /// <summary>
        /// 每帧调用以处理实体的AI逻辑
        /// </summary>
        public virtual void AI() {

        }
        /// <summary>
        /// 在实体生成到世界中时调用，可用于初始化数据
        /// </summary>
        /// <param name="obj"></param>
        public virtual void OnSpawn(object obj) {

        }
        /// <summary>
        /// 以中心为基准调整缩放，自动补偿 Position 以防止碰撞箱偏移
        /// </summary>
        public void SetScaleCentered(float newScale) {
            Vector2 oldSize = Size;
            Scale = newScale;
            Vector2 newSize = Size;
            Position -= (newSize - oldSize) / 2f;
        }
        /// <summary>
        /// 在实体绘制之前调用，可用于修改绘制颜色或执行其他操作
        /// </summary>
        /// <param name="spriteBatch"></param>
        /// <param name="drawColor"></param>
        /// <returns></returns>
        public virtual bool PreDraw(SpriteBatch spriteBatch, ref Color drawColor) {
            return true;
        }
        /// <summary>
        /// 在实体绘制之后调用，可用于添加额外的绘制效果
        /// </summary>
        /// <param name="spriteBatch"></param>
        /// <param name="drawColor"></param>
        public virtual void PostDraw(SpriteBatch spriteBatch, Color drawColor) {

        }
        #region Synchronization
        /// <summary>
        /// 发送同步数据
        /// </summary>
        /// <param name="writer"></param>
        public void SendSyncData(BinaryWriter writer) => SyncVarManager.Send(this, writer);
        /// <summary>
        /// 接收同步数据
        /// </summary>
        /// <param name="reader"></param>
        public void ReceiveSyncData(BinaryReader reader) => SyncVarManager.Receive(this, reader);
        #endregion
    }
}
