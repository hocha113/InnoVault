using Microsoft.Xna.Framework;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace InnoVault.Scenarios
{
    /// <summary>
    /// 锚点组件：一组具名点位（物块坐标，玩家站在这一格上方），只在所属舞台里有效<br/>
    /// 用途：经 <see cref="ScenarioDirector.Travel(VaultScenario, string, Action{VaultScenario})"/> 进入舞台时的落点、
    /// <see cref="ScenarioRules.RecallAnchor"/> / <see cref="ScenarioRules.RespawnAnchor"/>、区域约束的传回点<br/>
    /// 可以在 Compose 里写死（集合初始化器），也可以在生成步骤里经 <see cref="Set"/> 写入：长期保存的舞台会把生成期间写入的锚点
    /// 与舞台文件存在一起，之后每次进入都读回来。锚点名不区分大小写。挂了它的场景不能放在会话层
    /// </summary>
    public class ScenarioAnchors : ScenarioComponent, IEnumerable<KeyValuePair<string, Point>>
    {
        /// <summary>缺省入口锚点的名字</summary>
        public const string Entry = "Entry";

        //一个组件随网络状态发送的锚点数上限
        private const int MaxSyncedAnchors = 512;

        private readonly Dictionary<string, Point> _points = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>全部锚点</summary>
        public IReadOnlyDictionary<string, Point> Points => _points;

        internal override bool BindsToStage => true;

        /// <summary>写入或覆盖一个锚点，供集合初始化器使用</summary>
        public void Add(string name, Point tile) => Set(name, tile);

        /// <summary>写入或覆盖一个锚点，供集合初始化器使用</summary>
        public void Add(string name, int x, int y) => Set(name, new Point(x, y));

        /// <summary>写入或覆盖一个锚点。生成步骤里调用时，长期保存的舞台会记住这个值</summary>
        /// <param name="name">锚点名</param>
        /// <param name="tile">物块坐标</param>
        public void Set(string name, Point tile) {
            ArgumentException.ThrowIfNullOrEmpty(name);
            _points[name] = tile;
        }

        /// <summary>移除一个锚点</summary>
        public bool Remove(string name) => name != null && _points.Remove(name);

        /// <summary>按名字取锚点</summary>
        public bool TryGet(string name, out Point tile) {
            if (name != null && _points.TryGetValue(name, out tile)) {
                return true;
            }
            tile = default;
            return false;
        }

        /// <summary>是否有这个锚点</summary>
        public bool Contains(string name) => name != null && _points.ContainsKey(name);

        internal Dictionary<string, Point> Snapshot() => new(_points, StringComparer.OrdinalIgnoreCase);

        //生成期间写下、或从舞台元数据读回的锚点只存在于服务器，整张表随实例状态发给客户端
        /// <inheritdoc/>
        protected override void NetSend(BinaryWriter writer) {
            writer.Write((ushort)Math.Min(_points.Count, MaxSyncedAnchors));
            int written = 0;
            foreach (var pair in _points) {
                if (written++ >= MaxSyncedAnchors) {
                    break;
                }
                writer.Write(pair.Key);
                writer.Write(pair.Value.X);
                writer.Write(pair.Value.Y);
            }
        }

        /// <inheritdoc/>
        protected override void NetReceive(BinaryReader reader) {
            int count = reader.ReadUInt16();
            if (count > MaxSyncedAnchors) {
                throw new InvalidDataException($"{count} anchors in one scenario state");
            }
            _points.Clear();
            for (int i = 0; i < count; i++) {
                string name = reader.ReadString();
                int x = reader.ReadInt32();
                int y = reader.ReadInt32();
                if (name.Length > 0) {
                    _points[name] = new Point(x, y);
                }
            }
        }

        /// <inheritdoc/>
        public IEnumerator<KeyValuePair<string, Point>> GetEnumerator() => _points.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
