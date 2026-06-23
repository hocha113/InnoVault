using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Terraria;
using Terraria.ModLoader.IO;

namespace InnoVault
{
    /// <summary>
    /// 管理网络同步变量的通用类
    /// </summary>
    public static class SyncVarManager
    {
        //单个同步成员的元数据，访问器通过 DynamicMethod 编译，避免每次同步走反射 + 装箱反射调用
        private sealed class SyncMember
        {
            public MemberInfo Member;
            public Type ValueType;
            public Func<object, object> Getter;
            public Action<object, object> Setter;
        }

        private static readonly Dictionary<Type, List<SyncMember>> _syncVarsCache = new();
        private static readonly Dictionary<Type, Action<BinaryWriter, object>> _typeWriters = new();
        private static readonly Dictionary<Type, Func<BinaryReader, object>> _typeReaders = new();
        //发送端按对象保存的"上次已发送基线"，用于增量(delta)同步；对象被回收后自动随之释放
        private static readonly ConditionalWeakTable<object, object[]> _baselines = new();

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
        public static List<MemberInfo> GetSyncVars(Type type) => GetSyncMembers(type).Select(m => m.Member).ToList();

        private static List<SyncMember> GetSyncMembers(Type type) {
            if (_syncVarsCache.TryGetValue(type, out var members)) {
                return members;
            }
            members = [];

            //获取继承链
            //这样可以确保基类的数据先被序列化，结构更清晰
            var hierarchy = new List<Type>();
            var current = type;
            while (current != null && current != typeof(object)) {
                hierarchy.Add(current);
                current = current.BaseType;
            }
            hierarchy.Reverse();

            //逐层获取并排序
            foreach (var t in hierarchy) {
                var levelMembers = new List<MemberInfo>();

                var fields = t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .Where(f => f.GetCustomAttribute<SyncVarAttribute>() != null);
                levelMembers.AddRange(fields);

                var props = t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .Where(p => p.GetCustomAttribute<SyncVarAttribute>() != null && p.CanRead && p.CanWrite);
                levelMembers.AddRange(props);

                //只在当前层级内按名称排序
                //这样即使基类和子类有同名私有字段，它们的相对顺序也是固定的。基类在前，子类在后
                levelMembers.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));

                foreach (var member in levelMembers) {
                    members.Add(new SyncMember {
                        Member = member,
                        ValueType = member is FieldInfo f ? f.FieldType : ((PropertyInfo)member).PropertyType,
                        Getter = BuildGetter(member),
                        Setter = BuildSetter(member),
                    });
                }
            }

            _syncVarsCache[type] = members;
            return members;
        }

        /// <summary>
        /// 发送对象的同步数据
        /// </summary>
        public static void Send(object obj, BinaryWriter writer) {
            foreach (var member in GetSyncMembers(obj.GetType())) {
                WriteValue(writer, member.Getter(obj), member.ValueType);
            }
        }

        /// <summary>
        /// 接收对象的同步数据
        /// </summary>
        public static void Receive(object obj, BinaryReader reader) {
            foreach (var member in GetSyncMembers(obj.GetType())) {
                object value = ReadValue(reader, member.ValueType);
                if (value != null) {
                    member.Setter(obj, value);
                }
            }
        }

        #region Stateful delta (baseline) sync
        //带基线的增量同步：写入端用位掩码标记变化的字段，仅发送变化值；读取端按掩码套用，
        //跳过端在身份不匹配时按掩码读弃以保持批量流对齐。掩码字节数 = ceil(成员数 / 8)

        /// <summary>
        /// 重置某对象的增量基线，使下一次写入视为全量（通常在生成/重新建立同步关系时调用）
        /// </summary>
        public static void ResetBaseline(object obj) => _baselines.Remove(obj);

        /// <summary>
        /// 写入对象的全量同步数据（所有同步字段），并刷新基线，可由 <see cref="ReadState"/> 读取
        /// </summary>
        public static void WriteFull(object obj, BinaryWriter writer) => WriteStateCore(obj, writer, true);

        /// <summary>
        /// 写入对象的增量同步数据：<paramref name="forceFull"/> 为真时写全量，否则只写自上次基线以来变化的字段
        /// 没有任何变化且非全量时返回 <see langword="false"/>，调用方可据此跳过发送
        /// </summary>
        public static bool WriteState(object obj, BinaryWriter writer, bool forceFull) => WriteStateCore(obj, writer, forceFull);

        private static bool WriteStateCore(object obj, BinaryWriter writer, bool forceFull) {
            List<SyncMember> members = GetSyncMembers(obj.GetType());
            int count = members.Count;
            object[] baseline = GetOrCreateBaseline(obj, count, out bool fresh);
            bool full = forceFull || fresh;

            int maskBytes = MaskByteCount(count);
            object[] current = count > 0 ? new object[count] : [];
            Span<byte> mask = stackalloc byte[maskBytes];
            bool any = false;

            for (int i = 0; i < count; i++) {
                object value = members[i].Getter(obj);
                current[i] = value;
                if (full || !ValueEquals(baseline[i], value)) {
                    mask[i >> 3] |= (byte)(1 << (i & 7));
                    any = true;
                }
            }

            if (!any && !full) {
                return false;
            }

            for (int b = 0; b < maskBytes; b++) {
                writer.Write(mask[b]);
            }
            for (int i = 0; i < count; i++) {
                if ((mask[i >> 3] & (1 << (i & 7))) != 0) {
                    WriteValue(writer, current[i], members[i].ValueType);
                    baseline[i] = current[i];
                }
            }
            return true;
        }

        /// <summary>
        /// 读取并套用一次（全量或增量）同步数据到目标对象，与 <see cref="WriteFull"/>/<see cref="WriteState"/> 对应
        /// </summary>
        public static void ReadState(object obj, BinaryReader reader) => ReadMaskedState(GetSyncMembers(obj.GetType()), reader, obj);

        /// <summary>
        /// 按指定类型读弃一次同步数据（用于批量流中身份不匹配的条目，保持后续读取对齐）
        /// </summary>
        public static void SkipState(Type type, BinaryReader reader) => ReadMaskedState(GetSyncMembers(type), reader, null);

        private static void ReadMaskedState(List<SyncMember> members, BinaryReader reader, object target) {
            int count = members.Count;
            int maskBytes = MaskByteCount(count);
            Span<byte> mask = stackalloc byte[maskBytes];
            for (int b = 0; b < maskBytes; b++) {
                mask[b] = reader.ReadByte();
            }
            for (int i = 0; i < count; i++) {
                if ((mask[i >> 3] & (1 << (i & 7))) == 0) {
                    continue;
                }
                object value = ReadValue(reader, members[i].ValueType);
                if (target != null && value != null) {
                    members[i].Setter(target, value);
                }
            }
        }

        private static int MaskByteCount(int count) => (count + 7) >> 3;

        private static bool ValueEquals(object a, object b) => a == null ? b == null : a.Equals(b);

        private static object[] GetOrCreateBaseline(object obj, int count, out bool fresh) {
            if (_baselines.TryGetValue(obj, out object[] arr) && arr.Length == count) {
                fresh = false;
                return arr;
            }
            arr = new object[count];
            _baselines.AddOrUpdate(obj, arr);
            fresh = true;
            return arr;
        }
        #endregion

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

        //通过 DynamicMethod 生成访问器，skipVisibility 允许访问私有 [SyncVar] 成员
        private static Func<object, object> BuildGetter(MemberInfo member) {
            DynamicMethod dm = new($"__syncget_{member.DeclaringType.Name}_{member.Name}"
                , typeof(object), [typeof(object)], member.DeclaringType, true);
            ILGenerator il = dm.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, member.DeclaringType);

            Type valueType;
            if (member is FieldInfo f) {
                il.Emit(OpCodes.Ldfld, f);
                valueType = f.FieldType;
            }
            else {
                PropertyInfo p = (PropertyInfo)member;
                il.Emit(OpCodes.Callvirt, p.GetGetMethod(true));
                valueType = p.PropertyType;
            }

            if (valueType.IsValueType) {
                il.Emit(OpCodes.Box, valueType);
            }
            il.Emit(OpCodes.Ret);
            return (Func<object, object>)dm.CreateDelegate(typeof(Func<object, object>));
        }

        private static Action<object, object> BuildSetter(MemberInfo member) {
            DynamicMethod dm = new($"__syncset_{member.DeclaringType.Name}_{member.Name}"
                , null, [typeof(object), typeof(object)], member.DeclaringType, true);
            ILGenerator il = dm.GetILGenerator();
            Type valueType = member is FieldInfo f ? f.FieldType : ((PropertyInfo)member).PropertyType;

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, member.DeclaringType);
            il.Emit(OpCodes.Ldarg_1);
            if (valueType.IsValueType) {
                il.Emit(OpCodes.Unbox_Any, valueType);
            }
            else {
                il.Emit(OpCodes.Castclass, valueType);
            }

            if (member is FieldInfo ff) {
                il.Emit(OpCodes.Stfld, ff);
            }
            else {
                il.Emit(OpCodes.Callvirt, ((PropertyInfo)member).GetSetMethod(true));
            }
            il.Emit(OpCodes.Ret);
            return (Action<object, object>)dm.CreateDelegate(typeof(Action<object, object>));
        }
    }
}
