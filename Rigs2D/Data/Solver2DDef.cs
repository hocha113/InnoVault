using Microsoft.Xna.Framework;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace InnoVault.Rigs2D.Data
{
    /// <summary>
    /// 一个求解器的静态定义：类型名、命名、参与的骨骼与一袋参数
    /// <br/>参数以 <see cref="JObject"/> 原样保留，由具体求解器在 <c>Configure</c> 里按键读取，
    /// 因此新增求解器类型不需要改动数据层；类型名经 <c>Rig2DSolverRegistry</c> 查找工厂
    /// </summary>
    public sealed class Solver2DDef
    {
        /// <summary>
        /// 求解器实例名，同一骨架内唯一；运行时 <c>Rig2DInstance.Solver&lt;T&gt;(name)</c> 按此取用
        /// </summary>
        public string Name { get; set; } = string.Empty;
        /// <summary>
        /// 求解器类型名（注册表键，例如 <c>TwoBoneIK</c> / <c>ChainFollow</c>）
        /// </summary>
        public string Type { get; set; } = string.Empty;
        /// <summary>
        /// 参与的骨骼名，语义由求解器类型决定（IK 为链上各骨、步态为各腿髋锚等）
        /// </summary>
        public List<string> Bones { get; } = [];
        /// <summary>
        /// 参数袋；<see langword="null"/> 视为空
        /// </summary>
        public JObject Params { get; set; }

        /// <summary>
        /// 已解析的骨骼索引（顺序同 <see cref="Bones"/>），缺失的骨骼为 <c>-1</c>；由 <see cref="Rig2DDefinition.Resolve"/> 填充
        /// </summary>
        public int[] BoneIndices { get; internal set; } = [];

        /// <summary>
        /// 是否存在指定参数
        /// </summary>
        public bool Has(string key) => Params != null && Params.TryGetValue(key, out _);

        /// <summary>
        /// 读取浮点参数，缺失或类型不符返回默认值
        /// </summary>
        public float GetFloat(string key, float fallback) {
            JToken t = Token(key);
            if (t == null) {
                return fallback;
            }
            return t.Type is JTokenType.Float or JTokenType.Integer ? (float)t : fallback;
        }

        /// <summary>
        /// 读取整数参数
        /// </summary>
        public int GetInt(string key, int fallback) {
            JToken t = Token(key);
            if (t == null) {
                return fallback;
            }
            return t.Type is JTokenType.Integer or JTokenType.Float ? (int)t : fallback;
        }

        /// <summary>
        /// 读取布尔参数
        /// </summary>
        public bool GetBool(string key, bool fallback) {
            JToken t = Token(key);
            return t != null && t.Type == JTokenType.Boolean ? (bool)t : fallback;
        }

        /// <summary>
        /// 读取字符串参数
        /// </summary>
        public string GetString(string key, string fallback) {
            JToken t = Token(key);
            return t != null && t.Type == JTokenType.String ? (string)t : fallback;
        }

        /// <summary>
        /// 读取角度参数：优先 <c>key</c>（弧度），其次 <c>key + "Deg"</c>（角度）
        /// </summary>
        public float GetAngle(string key, float fallback) {
            if (Has(key)) {
                return GetFloat(key, fallback);
            }
            string degKey = key + "Deg";
            if (Has(degKey)) {
                return MathHelper.ToRadians(GetFloat(degKey, MathHelper.ToDegrees(fallback)));
            }
            return fallback;
        }

        /// <summary>
        /// 读取二维向量参数，接受 <c>[x, y]</c> 数组或 <c>{ "x": , "y": }</c> 对象
        /// </summary>
        public Vector2 GetVector2(string key, Vector2 fallback) {
            JToken t = Token(key);
            return Rig2DJson.ReadVector2(t, fallback);
        }

        /// <summary>
        /// 读取浮点数组；单个数值会被视为长度 1 的数组
        /// </summary>
        public float[] GetFloatArray(string key) {
            JToken t = Token(key);
            if (t == null) {
                return [];
            }
            if (t.Type is JTokenType.Float or JTokenType.Integer) {
                return [(float)t];
            }
            if (t is not JArray arr) {
                return [];
            }
            float[] result = new float[arr.Count];
            for (int i = 0; i < arr.Count; i++) {
                JToken e = arr[i];
                result[i] = e.Type is JTokenType.Float or JTokenType.Integer ? (float)e : 0f;
            }
            return result;
        }

        /// <summary>
        /// 读取浮点数组并按腿数展开：缺失返回全 <paramref name="fallback"/>，长度 1 广播，长度不足以最后一个补齐
        /// </summary>
        public float[] GetFloatArray(string key, int count, float fallback) {
            float[] raw = GetFloatArray(key);
            float[] result = new float[Math.Max(count, 0)];
            for (int i = 0; i < result.Length; i++) {
                if (raw.Length == 0) {
                    result[i] = fallback;
                }
                else {
                    result[i] = raw[Math.Min(i, raw.Length - 1)];
                }
            }
            return result;
        }

        /// <summary>
        /// 读取整数数组（规则同 <see cref="GetFloatArray(string, int, float)"/>）
        /// </summary>
        public int[] GetIntArray(string key, int count, int fallback) {
            float[] raw = GetFloatArray(key);
            int[] result = new int[Math.Max(count, 0)];
            for (int i = 0; i < result.Length; i++) {
                result[i] = raw.Length == 0 ? fallback : (int)raw[Math.Min(i, raw.Length - 1)];
            }
            return result;
        }

        /// <summary>
        /// 读取字符串数组
        /// </summary>
        public string[] GetStringArray(string key) {
            JToken t = Token(key);
            if (t == null) {
                return [];
            }
            if (t.Type == JTokenType.String) {
                return [(string)t];
            }
            if (t is not JArray arr) {
                return [];
            }
            string[] result = new string[arr.Count];
            for (int i = 0; i < arr.Count; i++) {
                result[i] = arr[i].Type == JTokenType.String ? (string)arr[i] : string.Empty;
            }
            return result;
        }

        /// <summary>
        /// 读取以骨骼名给出的参数并解析为索引，缺失返回 <c>-1</c>
        /// </summary>
        public int GetBone(Rig2DDefinition rig, string key) {
            string name = GetString(key, null);
            return string.IsNullOrEmpty(name) || rig == null ? -1 : rig.BoneIndex(name);
        }

        /// <summary>
        /// 读取以骨骼名数组给出的参数并解析为索引数组
        /// </summary>
        public int[] GetBones(Rig2DDefinition rig, string key) {
            string[] names = GetStringArray(key);
            int[] result = new int[names.Length];
            for (int i = 0; i < names.Length; i++) {
                result[i] = rig?.BoneIndex(names[i]) ?? -1;
            }
            return result;
        }

        /// <summary>
        /// 写入一个参数（代码构建时用）
        /// </summary>
        public Solver2DDef Set(string key, JToken value) {
            Params ??= new JObject();
            Params[key] = value;
            return this;
        }

        private JToken Token(string key) {
            if (Params == null || string.IsNullOrEmpty(key)) {
                return null;
            }
            return Params.TryGetValue(key, out JToken t) && t.Type != JTokenType.Null ? t : null;
        }

        /// <summary>
        /// 复制一份定义（参数袋深拷贝，不含已解析索引）
        /// </summary>
        public Solver2DDef Clone() {
            Solver2DDef c = new() {
                Name = Name,
                Type = Type,
                Params = Params != null ? (JObject)Params.DeepClone() : null,
            };
            c.Bones.AddRange(Bones);
            return c;
        }
    }
}
