using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Terraria.ModLoader.IO;

namespace InnoVault.Actors
{
    /// <summary>
    /// 可自定义行为的实体基类
    /// </summary>
    public class Actor : VaultType<Actor>
    {
        #region Data
        /// <summary>
        /// 该实体在其特定数组中的索引，这些数组跟踪世界中的实体
        /// </summary>
        public int WhoAmI;
        /// <summary>
        /// 如果为 true，则实体实际上存在于游戏世界中。在特定的实体数组中，如果 active 为 false，则该实体是垃圾数据
        /// </summary>
        public bool Active;
        /// <summary>
        /// 该实体的命中箱的高度，以像素为单位
        /// </summary>
        public int Width;
        /// <summary>
        /// 该实体的命中箱的高度，以像素为单位
        /// </summary>
        public int Height;
        /// <summary>
        /// 该实体的命中箱
        /// </summary>
        public virtual Rectangle HitBox => new Rectangle((int)Position.X, (int)Position.Y, Width, Height);
        /// <summary>
        /// 该实体在世界坐标中的位置，注意这对应于实体的左上角。对于需要实体中心位置的逻辑，请改用 Center
        /// </summary>
        public Vector2 Position;
        /// <summary>
        /// 该实体在世界坐标中的中心位置
        /// </summary>
        public virtual Vector2 Center => new Vector2(Position.X + Width / 2f, Position.Y + Height / 2f);
        /// <summary>
        /// 该实体在每个刻度的世界坐标中的速度
        /// </summary>
        public Vector2 Velocity;
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
            TypeToInstance[GetType()] = this;
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
        private static readonly Dictionary<Type, List<MemberInfo>> _syncVarsCache = new();
        private static readonly Dictionary<Type, Action<BinaryWriter, object>> _typeWriters = new();
        private static readonly Dictionary<Type, Func<BinaryReader, object>> _typeReaders = new();

        static Actor() {
            RegisterSyncType((w, v) => w.Write(v), r => r.ReadInt32());
            RegisterSyncType((w, v) => w.Write(v), r => r.ReadSingle());
            RegisterSyncType((w, v) => w.Write(v), r => r.ReadBoolean());
            RegisterSyncType((w, v) => w.Write(v), r => r.ReadString());
            RegisterSyncType((w, v) => w.Write(v), r => r.ReadByte());
            RegisterSyncType((w, v) => w.Write(v), r => r.ReadInt16());
            RegisterSyncType((w, v) => w.Write(v), r => r.ReadInt64());
            RegisterSyncType((w, v) => w.Write(v), r => r.ReadDouble());
            RegisterSyncType((w, v) => { w.Write(v.X); w.Write(v.Y); }, r => new Vector2(r.ReadSingle(), r.ReadSingle()));
            RegisterSyncType((w, v) => w.Write(v.PackedValue), r => new Color { PackedValue = r.ReadUInt32() });
            RegisterSyncType((w, v) => ItemIO.Send(v, w, true), r => ItemIO.Receive(r, true));
        }

        /// <summary>
        /// 注册自定义同步类型处理程序
        /// </summary>
        /// <typeparam name="T">要支持的类型</typeparam>
        /// <param name="writer">写入逻辑</param>
        /// <param name="reader">读取逻辑</param>
        public static void RegisterSyncType<T>(Action<BinaryWriter, T> writer, Func<BinaryReader, T> reader) {
            _typeWriters[typeof(T)] = (w, obj) => writer(w, (T)obj);
            _typeReaders[typeof(T)] = r => reader(r);
        }

        private List<MemberInfo> GetSyncVars() {
            Type type = GetType();
            if (!_syncVarsCache.TryGetValue(type, out var members)) {
                members = new List<MemberInfo>();

                var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(f => f.GetCustomAttribute<SyncVarAttribute>() != null);
                members.AddRange(fields);

                var props = type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(p => p.GetCustomAttribute<SyncVarAttribute>() != null && p.CanRead && p.CanWrite);
                members.AddRange(props);

                _syncVarsCache[type] = members;
            }
            return members;
        }

        /// <summary>
        /// 发送同步数据
        /// </summary>
        /// <param name="writer"></param>
        public void SendSyncData(BinaryWriter writer) {
            var members = GetSyncVars();
            foreach (var member in members) {
                object value = member is FieldInfo f ? f.GetValue(this) : ((PropertyInfo)member).GetValue(this);
                Type type = member is FieldInfo fi ? fi.FieldType : ((PropertyInfo)member).PropertyType;
                WriteValue(writer, value, type);
            }
        }

        /// <summary>
        /// 接收同步数据
        /// </summary>
        /// <param name="reader"></param>
        public void ReceiveSyncData(BinaryReader reader) {
            var members = GetSyncVars();
            foreach (var member in members) {
                Type type = member is FieldInfo fi ? fi.FieldType : ((PropertyInfo)member).PropertyType;
                object value = ReadValue(reader, type);
                if (value != null) {
                    if (member is FieldInfo f) f.SetValue(this, value);
                    else ((PropertyInfo)member).SetValue(this, value);
                }
            }
        }

        private static void WriteValue(BinaryWriter writer, object value, Type type) {
            if (_typeWriters.TryGetValue(type, out var handler)) {
                handler(writer, value);
            }
            else {
                VaultMod.Instance.Logger.Error($"Type {type.Name} is not supported for SyncVar.");
                VaultUtils.Text($"Type {type.Name} is not supported for SyncVar.", Color.Red);
            }
        }

        private static object ReadValue(BinaryReader reader, Type type) {
            if (_typeReaders.TryGetValue(type, out var handler)) {
                return handler(reader);
            }
            return null;
        }
        #endregion
    }
}
