using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Terraria;
using Terraria.ModLoader.IO;

namespace InnoVault.DataModules
{
    /// <summary>
    /// <see cref="DataModule"/> 默认使用的反射序列化助手。支持公共可读写属性与公共可写字段，<br/>
    /// 类型覆盖 <see cref="bool"/> / <see cref="int"/> / <see cref="long"/> / <see cref="float"/> /
    /// <see cref="double"/> / <see cref="string"/> / <see cref="Item"/> / <see cref="TagCompound"/> 以及任意枚举（按 int 存储），<br/>
    /// 同时支持这些类型的 <see cref="IList{T}"/> 集合。<see cref="Item"/> 通过 <see cref="ItemIO"/> 保存。<br/>
    /// 反射元数据按类型缓存，避免每次保存 / 读取重复扫描
    /// </summary>
    internal static class DataModuleReflector
    {
        private sealed class Accessor(string name, Type type, Type elementType, bool isList, Func<object, object> get, Action<object, object> set)
        {
            public string Name { get; } = name;
            public Type Type { get; } = type;
            public Type ElementType { get; } = elementType;
            public bool IsList { get; } = isList;
            public Func<object, object> Get { get; } = get;
            public Action<object, object> Set { get; } = set;
        }

        private static readonly Dictionary<Type, Accessor[]> _cache = [];

        public static void ClearCache() => _cache.Clear();

        private static bool IsScalarSupported(Type t)
            => t == typeof(bool) || t == typeof(int) || t == typeof(long)
            || t == typeof(float) || t == typeof(double) || t == typeof(string) || t.IsEnum
            || t == typeof(Item) || t == typeof(TagCompound);

        private static bool TryGetIListElementType(Type type, out Type elementType) {
            elementType = null;
            if (type == typeof(string)) {
                return false;
            }
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IList<>)) {
                elementType = type.GetGenericArguments()[0];
                return true;
            }
            foreach (Type iface in type.GetInterfaces()) {
                if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IList<>)) {
                    elementType = iface.GetGenericArguments()[0];
                    return true;
                }
            }
            return false;
        }

        private static bool TryBuildAccessor(string name, Type memberType, Func<object, object> get, Action<object, object> set, out Accessor accessor) {
            if (IsScalarSupported(memberType)) {
                accessor = new Accessor(name, memberType, null, false, get, set);
                return true;
            }
            if (TryGetIListElementType(memberType, out Type elementType) && IsScalarSupported(elementType)) {
                accessor = new Accessor(name, memberType, elementType, true, get, set);
                return true;
            }
            accessor = null;
            return false;
        }

        private static Accessor[] GetAccessors(Type type) {
            if (_cache.TryGetValue(type, out Accessor[] cached)) {
                return cached;
            }

            List<Accessor> list = [];
            for (Type current = type; current != null && current != typeof(DataModule); current = current.BaseType) {
                foreach (PropertyInfo prop in current.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)) {
                    if (prop.CanRead && prop.CanWrite && prop.GetIndexParameters().Length == 0
                        && TryBuildAccessor(prop.Name, prop.PropertyType, prop.GetValue, prop.SetValue, out Accessor accessor)) {
                        list.Add(accessor);
                    }
                }
                foreach (FieldInfo field in current.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)) {
                    if (!field.IsInitOnly && !field.IsLiteral
                        && TryBuildAccessor(field.Name, field.FieldType, field.GetValue, field.SetValue, out Accessor accessor)) {
                        list.Add(accessor);
                    }
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
                if (accessor.IsList) {
                    tag[accessor.Name] = SaveList(accessor.ElementType, (IEnumerable)value);
                }
                else if (accessor.Type == typeof(Item)) {
                    tag[accessor.Name] = ItemIO.Save((Item)value);
                }
                else if (accessor.Type == typeof(TagCompound)) {
                    tag[accessor.Name] = CloneTag((TagCompound)value);
                }
                else if (accessor.Type.IsEnum) {
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
                object value = accessor.IsList
                    ? ReadList(accessor, module, tag)
                    : ReadValue(accessor.Type, accessor.Name, tag);
                if (value != null) {
                    accessor.Set(module, value);
                }
            }
        }

        public static void Copy(DataModule from, DataModule to) {
            foreach (Accessor accessor in GetAccessors(from.GetType())) {
                object value = accessor.Get(from);
                if (accessor.IsList) {
                    value = CloneList(accessor.ElementType, value as IEnumerable, accessor.Type);
                }
                else if (accessor.Type == typeof(Item) && value is Item item) {
                    value = item.Clone();
                }
                else if (accessor.Type == typeof(TagCompound) && value is TagCompound subTag) {
                    value = CloneTag(subTag);
                }
                accessor.Set(to, value);
            }
        }

        public static void Reset(DataModule module) {
            foreach (Accessor accessor in GetAccessors(module.GetType())) {
                if (accessor.IsList) {
                    object value = accessor.Get(module);
                    if (value is IList list) {
                        list.Clear();
                    }
                    else {
                        accessor.Set(module, CreateList(accessor.ElementType, accessor.Type));
                    }
                }
                else if (accessor.Type == typeof(TagCompound)) {
                    accessor.Set(module, new TagCompound());
                }
                else {
                    accessor.Set(module, accessor.Type.IsValueType ? Activator.CreateInstance(accessor.Type) : null);
                }
            }
        }

        private static IList SaveList(Type elementType, IEnumerable values) {
            if (elementType == typeof(TagCompound)) {
                List<TagCompound> tags = [];
                foreach (object value in values) {
                    if (value is TagCompound subTag) {
                        tags.Add(CloneTag(subTag));
                    }
                }
                return tags;
            }
            if (elementType == typeof(Item)) {
                List<TagCompound> items = [];
                foreach (object value in values) {
                    if (value is Item item) {
                        items.Add(ItemIO.Save(item));
                    }
                }
                return items;
            }
            if (elementType.IsEnum) {
                List<int> enums = [];
                foreach (object value in values) {
                    enums.Add(Convert.ToInt32(value));
                }
                return enums;
            }
            IList list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType));
            foreach (object value in values) {
                list.Add(value);
            }
            return list;
        }

        private static object ReadList(Accessor accessor, DataModule module, TagCompound tag) {
            IList target = accessor.Get(module) as IList ?? CreateList(accessor.ElementType, accessor.Type);
            target.Clear();

            if (accessor.ElementType == typeof(Item)) {
                foreach (TagCompound itemTag in tag.GetList<TagCompound>(accessor.Name)) {
                    target.Add(ItemIO.Load(itemTag));
                }
                return target;
            }
            if (accessor.ElementType == typeof(TagCompound)) {
                foreach (TagCompound subTag in tag.GetList<TagCompound>(accessor.Name)) {
                    target.Add(CloneTag(subTag));
                }
                return target;
            }
            if (accessor.ElementType.IsEnum) {
                foreach (int value in tag.GetList<int>(accessor.Name)) {
                    target.Add(Enum.ToObject(accessor.ElementType, value));
                }
                return target;
            }

            MethodInfo getListMethod = typeof(TagCompound).GetMethod(nameof(TagCompound.GetList))?.MakeGenericMethod(accessor.ElementType);
            object source = getListMethod?.Invoke(tag, [accessor.Name]);
            if (source is IEnumerable values) {
                foreach (object value in values) {
                    target.Add(value);
                }
            }
            return target;
        }

        private static IList CreateList(Type elementType, Type requestedType) {
            if (!requestedType.IsInterface && !requestedType.IsAbstract && requestedType.GetConstructor(Type.EmptyTypes) != null) {
                return (IList)Activator.CreateInstance(requestedType);
            }
            return (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType));
        }

        private static object CloneList(Type elementType, IEnumerable values, Type requestedType) {
            IList clone = CreateList(elementType, requestedType);
            if (values == null) {
                return clone;
            }
            foreach (object value in values) {
                if (elementType == typeof(Item) && value is Item item) {
                    clone.Add(item.Clone());
                }
                else if (elementType == typeof(TagCompound) && value is TagCompound subTag) {
                    clone.Add(CloneTag(subTag));
                }
                else {
                    clone.Add(value);
                }
            }
            return clone;
        }

        private static object ReadValue(Type type, string name, TagCompound tag) {
            if (type == typeof(Item)) {
                return ItemIO.Load(tag.GetCompound(name));
            }
            if (type == typeof(TagCompound)) {
                return CloneTag(tag.GetCompound(name));
            }
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

        private static TagCompound CloneTag(TagCompound source) {
            TagCompound clone = [];
            if (source == null) {
                return clone;
            }
            foreach (KeyValuePair<string, object> entry in source) {
                clone[entry.Key] = CloneTagValue(entry.Value);
            }
            return clone;
        }

        private static object CloneTagValue(object value) {
            if (value is TagCompound tag) {
                return CloneTag(tag);
            }
            if (value is Item item) {
                return ItemIO.Save(item);
            }
            if (value is IList<TagCompound> tagList) {
                List<TagCompound> result = [];
                foreach (TagCompound sub in tagList) {
                    result.Add(CloneTag(sub));
                }
                return result;
            }
            if (value is IList<Item> itemList) {
                List<TagCompound> result = [];
                foreach (Item listItem in itemList) {
                    result.Add(ItemIO.Save(listItem));
                }
                return result;
            }
            return value;
        }
    }
}
