using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using Terraria.ModLoader.IO;

namespace InnoVault.JsonDatas
{
    ///<summary>
    ///在 NBT 存档系统的 <see cref="TagCompound"/> 与 <see cref="JObject"/> 之间互转的桥接工具<br/>
    ///主要用于存档内容的调试查看、导出导入或与外部 Json 配置互通<br/>
    ///<br/>
    ///⚠注意：该转换为"尽力而为"，存在已知的有损情形，不适用于对 NBT 的精确往返：<br/>
    ///1. NBT 区分 byte/short/int/long 等多种数值类型，转为 Json 后统一为数字，回转时整数取 <see cref="long"/>、小数取 <see cref="double"/>；<br/>
    ///2. <c>byte[]</c> / <c>int[]</c> 会被展开为 Json 数组，回转时按元素类型重建为同构列表；<br/>
    ///3. Json 的 <see langword="null"/> 值在回转时会被丢弃（NBT 不存储 null）
    ///</summary>
    public static class JsonTagBridge
    {
        ///<summary>
        ///将 <see cref="TagCompound"/> 递归转换为 <see cref="JObject"/>，失败时记录日志并返回 <see langword="null"/>
        ///</summary>
        public static JObject ToJObject(TagCompound tag) {
            if (tag == null) {
                return null;
            }
            try {
                return ToJObjectInternal(tag);
            } catch (Exception ex) {
                VaultMod.Instance.Logger.Error($"[JsonTagBridge] Failed to convert TagCompound to JObject: {ex}");
                return null;
            }
        }

        ///<summary>
        ///将 <see cref="JObject"/> 递归转换为 <see cref="TagCompound"/>，失败时记录日志并返回 <see langword="null"/>
        ///</summary>
        public static TagCompound ToTagCompound(JObject json) {
            if (json == null) {
                return null;
            }
            try {
                return ToTagCompoundInternal(json);
            } catch (Exception ex) {
                VaultMod.Instance.Logger.Error($"[JsonTagBridge] Failed to convert JObject to TagCompound: {ex}");
                return null;
            }
        }

        private static JObject ToJObjectInternal(TagCompound tag) {
            JObject result = [];
            foreach (KeyValuePair<string, object> pair in tag) {
                result[pair.Key] = ConvertValueToJToken(pair.Value);
            }
            return result;
        }

        private static TagCompound ToTagCompoundInternal(JObject json) {
            TagCompound tag = [];
            foreach (KeyValuePair<string, JToken> pair in json) {
                object value = ConvertJTokenToValue(pair.Value);
                if (value != null) {//NBT 不存储 null，跳过空值
                    tag[pair.Key] = value;
                }
            }
            return tag;
        }

        //将 NBT 侧的值（可能是基础类型、数组、列表或嵌套 TagCompound）转换为对应的 Json 节点
        private static JToken ConvertValueToJToken(object value) {
            switch (value) {
                case null:
                    return JValue.CreateNull();
                case TagCompound tag:
                    return ToJObjectInternal(tag);
                case string s:
                    return new JValue(s);
                case bool b:
                    return new JValue(b);
                case byte by:
                    return new JValue(by);
                case sbyte sb:
                    return new JValue(sb);
                case short sh:
                    return new JValue(sh);
                case ushort ush:
                    return new JValue(ush);
                case int i:
                    return new JValue(i);
                case uint ui:
                    return new JValue(ui);
                case long l:
                    return new JValue(l);
                case ulong ul:
                    return new JValue(ul);
                case float f:
                    return new JValue(f);
                case double d:
                    return new JValue(d);
                case decimal dec:
                    return new JValue(dec);
                case IList list://同时覆盖 byte[]、int[] 与各类 List<T>
                    return ConvertListToJArray(list);
                default:
                    return new JValue(value.ToString());
            }
        }

        private static JArray ConvertListToJArray(IEnumerable source) {
            JArray array = [];
            foreach (object item in source) {
                array.Add(ConvertValueToJToken(item));
            }
            return array;
        }

        //将 Json 节点转换回 NBT 侧可接受的值
        private static object ConvertJTokenToValue(JToken token) {
            switch (token.Type) {
                case JTokenType.Object:
                    return ToTagCompoundInternal((JObject)token);
                case JTokenType.Array:
                    return ConvertJArrayToList((JArray)token);
                case JTokenType.Integer:
                    return token.Value<long>();
                case JTokenType.Float:
                    return token.Value<double>();
                case JTokenType.Boolean:
                    return token.Value<bool>();
                case JTokenType.Null:
                case JTokenType.Undefined:
                    return null;
                case JTokenType.String:
                default:
                    return token.Value<string>() ?? token.ToString();
            }
        }

        //尽量把 Json 数组重建为同构列表，便于 NBT 序列化；混合或无法判定类型时回退为字符串列表
        private static object ConvertJArrayToList(JArray array) {
            if (array.Count == 0) {
                return new List<TagCompound>();
            }

            bool allObjects = true;
            bool allIntegers = true;
            bool allNumbers = true;
            bool allBooleans = true;
            foreach (JToken token in array) {
                JTokenType type = token.Type;
                allObjects &= type == JTokenType.Object;
                allIntegers &= type == JTokenType.Integer;
                allNumbers &= type is JTokenType.Integer or JTokenType.Float;
                allBooleans &= type == JTokenType.Boolean;
            }

            if (allObjects) {
                List<TagCompound> list = [];
                foreach (JToken token in array) {
                    list.Add(ToTagCompoundInternal((JObject)token));
                }
                return list;
            }
            if (allIntegers) {
                //统一取 long，与标量整数路径保持一致，并避免 JSON 中超出 int 范围的整数（如毫秒时间戳）触发溢出异常导致整次转换失败
                List<long> list = [];
                foreach (JToken token in array) {
                    list.Add(token.Value<long>());
                }
                return list;
            }
            if (allNumbers) {
                List<double> list = [];
                foreach (JToken token in array) {
                    list.Add(token.Value<double>());
                }
                return list;
            }
            if (allBooleans) {
                List<bool> list = [];
                foreach (JToken token in array) {
                    list.Add(token.Value<bool>());
                }
                return list;
            }

            List<string> strings = [];
            foreach (JToken token in array) {
                //NBT 无法写入 null 字符串（TagIO 的 BinaryWriter.Write((string)null) 会抛异常），故 null 元素一律落为空串，保证返回列表永不含 null
                strings.Add(token.Type == JTokenType.Null ? string.Empty : token.ToString());
            }
            return strings;
        }
    }
}
