using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Terraria;
using Terraria.ModLoader.IO;

namespace InnoVault
{
    /// <summary>
    /// 管理网络同步变量的通用类
    /// </summary>
    public static class SyncVarManager
    {
        private static readonly Dictionary<Type, List<MemberInfo>> _syncVarsCache = new();
        private static readonly Dictionary<Type, Action<BinaryWriter, object>> _typeWriters = new();
        private static readonly Dictionary<Type, Func<BinaryReader, object>> _typeReaders = new();

        static SyncVarManager() {
            RegisterSyncType((w, v) => w.Write(v), r => r.ReadInt32());
            RegisterSyncType((w, v) => w.Write(v), r => r.ReadSingle());
            RegisterSyncType((w, v) => w.Write(v), r => r.ReadBoolean());
            RegisterSyncType((w, v) => w.Write(v), r => r.ReadString());
            RegisterSyncType((w, v) => w.Write(v), r => r.ReadByte());
            RegisterSyncType((w, v) => w.Write(v), r => r.ReadInt16());
            RegisterSyncType((w, v) => w.Write(v), r => r.ReadInt64());
            RegisterSyncType((w, v) => w.Write(v), r => r.ReadDouble());
            RegisterSyncType((w, v) => w.Write(v.PackedValue), r => new Color { PackedValue = r.ReadUInt32() });
            RegisterSyncType((w, v) => { w.WriteVector2(v); }, r => r.ReadVector2());
            RegisterSyncType((w, v) => { w.WritePoint16(v); }, r => r.ReadPoint16());
            RegisterSyncType((w, v) => { w.WritePoint(v); }, r => r.ReadPoint());
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

        /// <summary>
        /// 获取对象的同步变量列表
        /// </summary>
        public static List<MemberInfo> GetSyncVars(Type type) {
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
        /// 发送对象的同步数据
        /// </summary>
        public static void Send(object obj, BinaryWriter writer) {
            var members = GetSyncVars(obj.GetType());
            foreach (var member in members) {
                object value = member is FieldInfo f ? f.GetValue(obj) : ((PropertyInfo)member).GetValue(obj);
                Type type = member is FieldInfo fi ? fi.FieldType : ((PropertyInfo)member).PropertyType;
                WriteValue(writer, value, type);
            }
        }

        /// <summary>
        /// 接收对象的同步数据
        /// </summary>
        public static void Receive(object obj, BinaryReader reader) {
            var members = GetSyncVars(obj.GetType());
            foreach (var member in members) {
                Type type = member is FieldInfo fi ? fi.FieldType : ((PropertyInfo)member).PropertyType;
                object value = ReadValue(reader, type);
                if (value != null) {
                    if (member is FieldInfo f) f.SetValue(obj, value);
                    else ((PropertyInfo)member).SetValue(obj, value);
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
    }
}
