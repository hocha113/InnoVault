using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq.Expressions;
using Terraria;

namespace InnoVault.Actors
{
    /// <summary>
    /// 可自定义行为的实体基类
    /// <br>该API的使用介绍:<see href="https://innovault.wiki/cn/persistence/actor/"/></br>
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
        /// 该实体在其所在槽位上的"代"标识，由服务器在生成时分配并随网络包传输
        /// <br>用于在槽位被复用后区分新旧实体，阻断延迟到达的过期数据包造成的张冠李戴</br>
        /// </summary>
        public ushort Generation;
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
        /// 玩家鼠标是否悬停在实体之上
        /// </summary>
        public bool HoverTP;
        /// <summary>
        /// 这个实体是否在玩家的画面内，该值在绘制函数中实时更新
        /// </summary>
        public bool InScreen;
        /// <summary>
        /// 这个实体在屏幕上绘制的扩张范围，默认为160
        /// </summary>
        public int DrawExtendMode = 160;
        /// <summary>
        /// 在服务器上置为 true 时，会在下一次网络心跳中对该实体强制进行一次全量同步
        /// <br>常规字段变化已由服务器自动检测并增量广播，通常无需手动设置；在客户端置位无效</br>
        /// </summary>
        public bool NetUpdate;
        /// <summary>
        /// 该实体的绘制层级
        /// </summary>
        public ActorDrawLayer DrawLayer = ActorDrawLayer.Default;
        /// <summary>
        /// (服务器)上次广播该实体增量状态的游戏帧
        /// </summary>
        internal long LastBroadcastTick;
        /// <summary>
        /// (服务器)上次对该实体进行强制全量同步(心跳)的游戏帧
        /// </summary>
        internal long LastFullSyncTick;
        /// <summary>
        /// 该实体在活跃列表中的稠密索引，不在列表时为 -1
        /// </summary>
        internal int ActiveIndex = -1;
        /// <summary>
        /// (客户端)是否已收到过权威状态，用于驱动插值收敛
        /// </summary>
        public bool HasNetTarget;
        /// <summary>
        /// (客户端)最近一次收到的权威位置
        /// </summary>
        public Vector2 NetTargetPosition;
        /// <summary>
        /// (客户端)最近一次收到的权威速度，用于航位推算预测插值目标
        /// </summary>
        public Vector2 NetTargetVelocity;
        /// <summary>
        /// (客户端)最近一次收到的权威旋转
        /// </summary>
        public float NetTargetRotation;
        /// <summary>
        /// (客户端)收到最近一次权威状态时的游戏帧
        /// </summary>
        public long NetTargetTick;
        #endregion
        /// <summary>
        /// 注册内容
        /// </summary>
        protected sealed override void VaultRegister() {
            Type type = GetType();
            ID = Instances.Count;
            Instances.Add(this);
            TypeToID[type] = ID;
            TypeToInstance[type] = this;
            IDToInstance[ID] = this;
            ByID[ID] = new Dictionary<Type, Actor> {
                { type, this }
            };
            //预编译无参构造为委托，避免在 Clone/AddActor 路径上反复触发 Activator.CreateInstance 的反射开销
            //失败时不抛出（例如缺少公共无参构造），Clone 会自动回退到 Activator.CreateInstance 兜底
            try {
                ActorLoader.ActorFactory[type] = Expression.Lambda<Func<Actor>>(Expression.New(type)).Compile();
            } catch (Exception ex) {
                VaultMod.Instance.Logger.Warn($"Actor factory compile failed for {type.FullName}, will fall back to Activator: {ex.Message}");
            }
        }
        /// <summary>
        /// 封闭内容
        /// </summary>
        public sealed override void VaultSetup() {
            SetStaticDefaults();
        }
        /// <summary>
        /// 克隆这个<see cref="Actor"/>实例，返回一个全新的、字段为类型默认值的对象
        /// 内部实现优先使用<see cref="ActorLoader.ActorFactory"/>中预编译好的工厂委托；若类型未注册或编译失败，则回退到<see cref="Activator.CreateInstance(Type)"/>以保证健壮性
        /// </summary>
        /// <returns>克隆的Actor实例</returns>
        public Actor Clone() {
            Type type = GetType();
            if (ActorLoader.ActorFactory.TryGetValue(type, out Func<Actor> factory)) {
                return factory();
            }
            return (Actor)Activator.CreateInstance(type);
        }
        /// <summary>
        /// 每帧调用以处理实体的AI逻辑
        /// </summary>
        public virtual void AI() {

        }
        /// <summary>
        /// 在实体生成到世界中时调用，可用于初始化数据
        /// </summary>
        /// <param name="args"></param>
        public virtual void OnSpawn(params object[] args) {

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
        /// 发送该实体的全部同步变量(无掩码全量)
        /// </summary>
        /// <param name="writer"></param>
        public void SendSyncData(BinaryWriter writer) => SyncVarManager.Send(this, writer);
        /// <summary>
        /// 接收该实体的全部同步变量(无掩码全量)
        /// </summary>
        /// <param name="reader"></param>
        public void ReceiveSyncData(BinaryReader reader) => SyncVarManager.Receive(this, reader);
        /// <summary>
        /// 写出生成 / 晚加入所需的附加数据，用于把"非 <see cref="SyncVarAttribute"/> 的内部 AI 状态"
        /// (例如运动相位、原点、计时器、朝向)纳入生成包与全量快照
        /// <br>这样晚加入的客户端可以正确重建该实体，而不必依赖出生位置反推不变量(那会导致永久错位)</br>
        /// <br>读写顺序必须与 <see cref="ReceiveExtraData"/> 完全一致</br>
        /// </summary>
        /// <param name="writer"></param>
        public virtual void SendExtraData(BinaryWriter writer) { }
        /// <summary>
        /// 读取由 <see cref="SendExtraData"/> 写出的附加数据，读取顺序必须与其完全一致
        /// </summary>
        /// <param name="reader"></param>
        public virtual void ReceiveExtraData(BinaryReader reader) { }
        /// <summary>
        /// (客户端)把当前状态记录为插值目标，在生成时调用以建立初始基准
        /// </summary>
        internal void InitNetTarget() {
            NetTargetPosition = Position;
            NetTargetVelocity = Velocity;
            NetTargetRotation = Rotation;
            NetTargetTick = (long)Main.GameUpdateCount;
            HasNetTarget = true;
        }
        /// <summary>
        /// (客户端)接收一次权威增量状态：位置与旋转会被改记为插值目标并保留本地预测值，
        /// 由 <see cref="ApplyClientReconciliation"/> 平滑收敛；其余字段(尺寸 / 缩放 / 速度等)立即套用
        /// </summary>
        /// <param name="reader"></param>
        internal void ClientReceiveState(BinaryReader reader) {
            Vector2 oldPosition = Position;
            float oldRotation = Rotation;

            SyncVarManager.ReadState(this, reader);

            NetTargetPosition = Position;
            NetTargetVelocity = Velocity;
            NetTargetRotation = Rotation;
            NetTargetTick = (long)Main.GameUpdateCount;
            HasNetTarget = true;

            //恢复本地预测的位置 / 旋转，交由重对齐平滑靠拢，避免硬跳变
            Position = oldPosition;
            Rotation = oldRotation;
        }
        /// <summary>
        /// (客户端)每帧在本地积分之后调用，把逻辑位置 / 旋转向权威目标平滑收敛
        /// <br>采用航位推算(目标随权威速度外推)以适配持续运动的实体；误差过大时直接吸附以防长时间拉扯</br>
        /// </summary>
        public void ApplyClientReconciliation() {
            if (!HasNetTarget) {
                return;
            }

            float elapsed = (long)Main.GameUpdateCount - NetTargetTick;
            if (elapsed < 0f) {
                elapsed = 0f;
            }

            Vector2 predicted = NetTargetPosition + NetTargetVelocity * elapsed;
            Vector2 error = predicted - Position;
            float distanceSq = error.LengthSquared();
            if (distanceSq > ActorNetWork.HardSnapDistanceSq) {
                Position = predicted;
            }
            else if (distanceSq > ActorNetWork.ReconcileEpsilonSq) {
                Position += error * ActorNetWork.PositionSmoothing;
            }

            float rotationError = MathHelper.WrapAngle(NetTargetRotation - Rotation);
            if (Math.Abs(rotationError) > ActorNetWork.RotationSnap) {
                Rotation = NetTargetRotation;
            }
            else if (Math.Abs(rotationError) > 0.0001f) {
                Rotation += rotationError * ActorNetWork.RotationSmoothing;
            }
        }
        #endregion
    }
}
