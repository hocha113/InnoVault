using System;
using System.Collections.Generic;
using System.Reflection;
using Terraria.ModLoader.IO;

namespace InnoVault.DataModules
{
    /// <summary>
    /// <see cref="DataModule"/> 默认使用的反射序列化助手。支持公共可读写属性与公共可写字段，<br/>
    /// 类型覆盖 <see cref="bool"/> / <see cref="int"/> / <see cref="long"/> / <see cref="float"/> /
    /// <see cref="double"/> / <see cref="string"/> 以及任意枚举（按 int 存储）。<br/>
    /// 反射元数据按类型缓存，避免每次保存 / 读取重复扫描
    /// </summary>
    internal static class DataModuleReflector
    {
        private sealed class Accessor(string name, Type type, Func<object, object> get, Action<object, object> set)
        {
            public string Name { get; } = name;
            public Type Type { get; } = type;
            public Func<object, object> Get { get; } = get;
            public Action<object, object> Set { get; } = set;
        }

        private static readonly Dictionary<Type, Accessor[]> _cache = [];

        public static void ClearCache() => _cache.Clear();

        private static bool IsSupported(Type t)
            => t == typeof(bool) || t == typeof(int) || t == typeof(long)
            || t == typeof(float) || t == typeof(double) || t == typeof(string) || t.IsEnum;

        private static Accessor[] GetAccessors(Type type) {
            if (_cache.TryGetValue(type, out Accessor[] cached)) {
                return cached;
            }

            List<Accessor> list = [];
            foreach (PropertyInfo prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
                if (prop.CanRead && prop.CanWrite && prop.GetIndexParameters().Length == 0 && IsSupported(prop.PropertyType)) {
                    list.Add(new Accessor(prop.Name, prop.PropertyType, prop.GetValue, prop.SetValue));
                }
            }
            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance)) {
                if (!field.IsInitOnly && !field.IsLiteral && IsSupported(field.FieldType)) {
                    list.Add(new Accessor(field.Name, field.FieldType, field.GetValue, field.SetValue));
                }
            }

            Accessor[] result = [.. list];
            _cache[type] = result;
            return result;
        }

        public static void Save(DataModule module, TagCompound tag) {
            foreach (Accessor accessor in GetAccessors(module.GetType())) {
                object value = accessor.Get(module);
                if (value == null) {
                    continue;
                }
                if (accessor.Type.IsEnum) {
                    tag[accessor.Name] = Convert.ToInt32(value);
                }
                else {
                    tag[accessor.Name] = value;
                }
            }
        }

        public static void Load(DataModule module, TagCompound tag) {
            foreach (Accessor accessor in GetAccessors(module.GetType())) {
                if (!tag.ContainsKey(accessor.Name)) {
                    continue;
                }
                object value = ReadValue(accessor.Type, accessor.Name, tag);
                if (value != null) {
                    accessor.Set(module, value);
                }
            }
        }

        public static void Copy(DataModule from, DataModule to) {
            foreach (Accessor accessor in GetAccessors(from.GetType())) {
                accessor.Set(to, accessor.Get(from));
            }
        }

        public static void Reset(DataModule module) {
            foreach (Accessor accessor in GetAccessors(module.GetType())) {
                accessor.Set(module, accessor.Type.IsValueType ? Activator.CreateInstance(accessor.Type) : null);
            }
        }

        private static object ReadValue(Type type, string name, TagCompound tag) {
            if (type.IsEnum) {
                return Enum.ToObject(type, tag.GetInt(name));
            }
            if (type == typeof(bool)) {
                return tag.GetBool(name);
            }
            if (type == typeof(int)) {
                return tag.GetInt(name);
            }
            if (type == typeof(long)) {
                return tag.GetLong(name);
            }
            if (type == typeof(float)) {
                return tag.GetFloat(name);
            }
            if (type == typeof(double)) {
                return tag.GetDouble(name);
            }
            if (type == typeof(string)) {
                return tag.GetString(name);
            }
            return null;
        }
    }
}
