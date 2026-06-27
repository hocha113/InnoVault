using Newtonsoft.Json.Linq;
using System;

namespace InnoVault.JsonDatas
{
    ///<summary>
    ///针对 <see cref="JToken"/> 的安全取值与路径导航扩展<br/>
    ///所有方法均为防御式实现：当目标不存在、类型不匹配或转换失败时返回给定的回退值，而不会抛出异常<br/>
    ///这套接口是对 GltfDocument 内部 ReadInt/ReadBool/ReadString/ReadFloatArray 的通用化提炼，方便项目内统一读取 Json
    ///</summary>
    public static class JsonExtensions
    {
        //从一个 Json 节点中取出指定键对应的子节点，仅当节点为对象时有效，否则返回 null
        private static JToken GetChild(JToken token, string key) {
            if (token is JObject obj && key != null) {
                return obj[key];
            }
            return null;
        }

        //判断一个节点是否等价于"空"，包括引用为 null、Json 的 null 以及未定义三种情况
        private static bool IsNullToken(JToken token)
            => token == null || token.Type == JTokenType.Null || token.Type == JTokenType.Undefined;

        //以标量方式转换节点，失败时回退，绝不抛出
        private static T ConvertScalar<T>(JToken token, T fallback) {
            if (IsNullToken(token)) {
                return fallback;
            }
            try {
                return token.Value<T>();
            } catch {
                return fallback;
            }
        }

        #region 自身转换 (As*)：直接把当前节点转换为目标类型
        ///<summary>
        ///将当前节点转换为 <see cref="int"/>，失败或为空时返回 <paramref name="fallback"/>
        ///</summary>
        public static int AsInt(this JToken token, int fallback = 0) => ConvertScalar(token, fallback);
        ///<summary>
        ///将当前节点转换为 <see cref="long"/>，失败或为空时返回 <paramref name="fallback"/>
        ///</summary>
        public static long AsLong(this JToken token, long fallback = 0) => ConvertScalar(token, fallback);
        ///<summary>
        ///将当前节点转换为 <see cref="float"/>，失败或为空时返回 <paramref name="fallback"/>
        ///</summary>
        public static float AsFloat(this JToken token, float fallback = 0f) => ConvertScalar(token, fallback);
        ///<summary>
        ///将当前节点转换为 <see cref="double"/>，失败或为空时返回 <paramref name="fallback"/>
        ///</summary>
        public static double AsDouble(this JToken token, double fallback = 0) => ConvertScalar(token, fallback);
        ///<summary>
        ///将当前节点转换为 <see cref="bool"/>，失败或为空时返回 <paramref name="fallback"/>
        ///</summary>
        public static bool AsBool(this JToken token, bool fallback = false) => ConvertScalar(token, fallback);
        ///<summary>
        ///将当前节点转换为 <see cref="string"/>，失败或为空时返回 <paramref name="fallback"/>（默认空字符串）
        ///</summary>
        public static string AsString(this JToken token, string fallback = "") => ConvertScalar(token, fallback);
        ///<summary>
        ///将当前节点反序列化为目标类型 <typeparamref name="T"/>，可用于标量或复杂对象<br/>
        ///失败或为空时返回 <paramref name="fallback"/>
        ///</summary>
        public static T As<T>(this JToken token, T fallback = default) {
            if (IsNullToken(token)) {
                return fallback;
            }
            try {
                return token.ToObject<T>();
            } catch {
                return fallback;
            }
        }
        #endregion

        #region 按键取值 (Get*)：从对象节点中取出指定键并转换
        ///<summary>
        ///读取键 <paramref name="key"/> 对应的 <see cref="int"/>，缺失或类型不符时返回 <paramref name="fallback"/>
        ///</summary>
        public static int GetInt(this JToken token, string key, int fallback = 0) => ConvertScalar(GetChild(token, key), fallback);
        ///<summary>
        ///读取键 <paramref name="key"/> 对应的 <see cref="long"/>，缺失或类型不符时返回 <paramref name="fallback"/>
        ///</summary>
        public static long GetLong(this JToken token, string key, long fallback = 0) => ConvertScalar(GetChild(token, key), fallback);
        ///<summary>
        ///读取键 <paramref name="key"/> 对应的 <see cref="float"/>，缺失或类型不符时返回 <paramref name="fallback"/>
        ///</summary>
        public static float GetFloat(this JToken token, string key, float fallback = 0f) => ConvertScalar(GetChild(token, key), fallback);
        ///<summary>
        ///读取键 <paramref name="key"/> 对应的 <see cref="double"/>，缺失或类型不符时返回 <paramref name="fallback"/>
        ///</summary>
        public static double GetDouble(this JToken token, string key, double fallback = 0) => ConvertScalar(GetChild(token, key), fallback);
        ///<summary>
        ///读取键 <paramref name="key"/> 对应的 <see cref="bool"/>，缺失或类型不符时返回 <paramref name="fallback"/>
        ///</summary>
        public static bool GetBool(this JToken token, string key, bool fallback = false) => ConvertScalar(GetChild(token, key), fallback);
        ///<summary>
        ///读取键 <paramref name="key"/> 对应的 <see cref="string"/>，缺失或类型不符时返回 <paramref name="fallback"/>（默认空字符串）
        ///</summary>
        public static string GetString(this JToken token, string key, string fallback = "") => ConvertScalar(GetChild(token, key), fallback);
        ///<summary>
        ///读取键 <paramref name="key"/> 对应的枚举值，支持数字与字符串两种写法（字符串不区分大小写），失败时返回 <paramref name="fallback"/>
        ///</summary>
        public static TEnum GetEnum<TEnum>(this JToken token, string key, TEnum fallback = default) where TEnum : struct, Enum {
            JToken child = GetChild(token, key);
            if (IsNullToken(child)) {
                return fallback;
            }
            if (child.Type == JTokenType.Integer) {
                try {
                    return (TEnum)Enum.ToObject(typeof(TEnum), child.Value<long>());
                } catch {
                    return fallback;
                }
            }
            string text = child.AsString(null);
            if (!string.IsNullOrEmpty(text) && Enum.TryParse(text, true, out TEnum result)) {
                return result;
            }
            return fallback;
        }
        ///<summary>
        ///读取键 <paramref name="key"/> 对应的值并反序列化为 <typeparamref name="T"/>，缺失或失败时返回 <paramref name="fallback"/>
        ///</summary>
        public static T Get<T>(this JToken token, string key, T fallback = default) => As(GetChild(token, key), fallback);
        ///<summary>
        ///读取键 <paramref name="key"/> 对应的对象节点，缺失或类型不符时返回 <see langword="null"/>
        ///</summary>
        public static JObject GetObject(this JToken token, string key) => GetChild(token, key) as JObject;
        ///<summary>
        ///读取键 <paramref name="key"/> 对应的数组节点，缺失或类型不符时返回 <see langword="null"/>
        ///</summary>
        public static JArray GetArray(this JToken token, string key) => GetChild(token, key) as JArray;
        ///<summary>
        ///读取键 <paramref name="key"/> 对应的整数数组<br/>
        ///当 <paramref name="expected"/> 为非负数且数组长度不匹配时返回 <see langword="null"/>，缺失或类型不符同样返回 <see langword="null"/>
        ///</summary>
        public static int[] GetIntArray(this JToken token, string key, int expected = -1) {
            if (GetChild(token, key) is not JArray array) {
                return null;
            }
            if (expected >= 0 && array.Count != expected) {
                return null;
            }
            int[] result = new int[array.Count];
            for (int i = 0; i < array.Count; i++) {
                result[i] = array[i].AsInt();
            }
            return result;
        }
        ///<summary>
        ///读取键 <paramref name="key"/> 对应的浮点数组<br/>
        ///当 <paramref name="expected"/> 为非负数且数组长度不匹配时返回 <see langword="null"/>，缺失或类型不符同样返回 <see langword="null"/>
        ///</summary>
        public static float[] GetFloatArray(this JToken token, string key, int expected = -1) {
            if (GetChild(token, key) is not JArray array) {
                return null;
            }
            if (expected >= 0 && array.Count != expected) {
                return null;
            }
            float[] result = new float[array.Count];
            for (int i = 0; i < array.Count; i++) {
                result[i] = array[i].AsFloat();
            }
            return result;
        }
        ///<summary>
        ///读取键 <paramref name="key"/> 对应的字符串数组，缺失或类型不符时返回 <see langword="null"/>
        ///</summary>
        public static string[] GetStringArray(this JToken token, string key) {
            if (GetChild(token, key) is not JArray array) {
                return null;
            }
            string[] result = new string[array.Count];
            for (int i = 0; i < array.Count; i++) {
                result[i] = array[i].AsString();
            }
            return result;
        }
        #endregion

        #region 路径导航：以点号 / JSONPath 形式访问深层节点
        ///<summary>
        ///尝试按路径 <paramref name="path"/> 取出节点，支持形如 <c>a.b.c</c> 与 <c>a.b[0]</c> 的写法（基于 <see cref="JToken.SelectToken(string)"/>）<br/>
        ///成功时输出节点并返回 <see langword="true"/>，否则输出 <see langword="null"/> 并返回 <see langword="false"/>
        ///</summary>
        public static bool TryGetToken(this JToken root, string path, out JToken token) {
            token = null;
            if (root == null || string.IsNullOrEmpty(path)) {
                return false;
            }
            try {
                token = root.SelectToken(path);
            } catch {
                token = null;
            }
            return token != null;
        }
        ///<summary>
        ///按路径取出节点，失败时返回 <see langword="null"/>
        ///</summary>
        public static JToken GetToken(this JToken root, string path) {
            root.TryGetToken(path, out JToken token);
            return token;
        }
        ///<summary>
        ///按路径取值并转换为 <typeparamref name="T"/>，缺失或失败时返回 <paramref name="fallback"/>
        ///</summary>
        public static T GetByPath<T>(this JToken root, string path, T fallback = default) {
            if (!root.TryGetToken(path, out JToken token)) {
                return fallback;
            }
            return token.As(fallback);
        }
        ///<summary>
        ///按点号路径写入值，路径中缺失的中间对象会被自动创建<br/>
        ///仅支持对象层级（不处理数组下标）；<paramref name="value"/> 可直接传入基础类型（依赖 <see cref="JToken"/> 的隐式转换）
        ///</summary>
        public static void SetByPath(this JObject root, string path, JToken value) {
            if (root == null || string.IsNullOrEmpty(path)) {
                return;
            }
            string[] segments = path.Split('.');
            JObject current = root;
            for (int i = 0; i < segments.Length - 1; i++) {
                string segment = segments[i];
                if (current[segment] is not JObject next) {
                    next = new JObject();
                    current[segment] = next;
                }
                current = next;
            }
            current[segments[^1]] = value;
        }
        #endregion
    }
}
