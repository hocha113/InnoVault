using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace InnoVault.Rigs2D.Data
{
    /// <summary>
    /// 一张命名姿态：按通道名给值，可继承另一张姿态
    /// <br/>解析后 <see cref="Resolved"/> 是完整的值表（通道缺省值 ← 继承链 ← 本表），<see cref="Specified"/> 标出继承链上显式给过的通道
    /// </summary>
    public sealed class Pose2DDef
    {
        /// <summary>
        /// 姿态名，同一骨架内唯一
        /// </summary>
        public string Name { get; set; } = string.Empty;
        /// <summary>
        /// 继承的姿态名（可空）
        /// </summary>
        public string Inherit { get; set; }
        /// <summary>
        /// 本表给出的通道值（标量取 X）
        /// </summary>
        public Dictionary<string, Vector2> Values { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// 在 <see cref="Rig2DDefinition.Poses"/> 中的索引，由 <see cref="Rig2DDefinition.Resolve"/> 填充
        /// </summary>
        public int Index { get; internal set; } = -1;
        /// <summary>
        /// 解析后的完整值表（下标同通道）
        /// </summary>
        public Vector2[] Resolved { get; internal set; } = [];
        /// <summary>
        /// 继承链上显式给过值的通道
        /// </summary>
        public bool[] Specified { get; internal set; } = [];

        /// <summary>
        /// 写入一个标量值（代码构建用）
        /// </summary>
        public Pose2DDef Set(string channel, float value) {
            Values[channel] = new Vector2(value, 0f);
            return this;
        }

        /// <summary>
        /// 写入一个向量值（代码构建用）
        /// </summary>
        public Pose2DDef Set(string channel, Vector2 value) {
            Values[channel] = value;
            return this;
        }

        /// <summary>
        /// 复制一份定义（不含解析结果）
        /// </summary>
        public Pose2DDef Clone() {
            Pose2DDef c = new() {
                Name = Name,
                Inherit = Inherit,
            };
            foreach (KeyValuePair<string, Vector2> kv in Values) {
                c.Values[kv.Key] = kv.Value;
            }
            return c;
        }
    }
}
