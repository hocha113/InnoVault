using System.Collections.Generic;
using System.Linq;
using Terraria.ModLoader.IO;

namespace InnoVault.Narrative.Progress
{
    /// <summary>
    /// 一个响应式叙事事件。取代旧实现把多种事件挤进一个 int 位域的做法，<br/>
    /// 让事件携带类型、负载、入队时间，从而可被结构化保存、过期与按需消费
    /// </summary>
    public readonly record struct NarrativeEvent(string Type, int Payload, int EnqueuedTick, int ExpireTick)
    {
        /// <summary>是否已过期（<see cref="ExpireTick"/> 小于等于 0 表示永不过期）</summary>
        public bool IsExpired(int currentTick) => ExpireTick > 0 && currentTick >= ExpireTick;
    }

    /// <summary>
    /// 响应式事件队列。例如"击败某 Boss / 进入某环境"产生事件入队，<br/>
    /// 待玩家打开对应对话时再消费，支持过期清理与存档
    /// </summary>
    public sealed class NarrativeEventQueue
    {
        private readonly List<NarrativeEvent> _events = [];

        /// <summary>当前队列长度</summary>
        public int Count => _events.Count;

        /// <summary>入队一个事件</summary>
        /// <param name="type">事件类型</param>
        /// <param name="payload">负载（如 Boss 的 NPC 类型）</param>
        /// <param name="currentTick">当前 tick</param>
        /// <param name="lifeTicks">存活 tick 数，小于等于 0 表示永不过期</param>
        public void Enqueue(string type, int payload, int currentTick, int lifeTicks = 0) {
            int expire = lifeTicks > 0 ? currentTick + lifeTicks : 0;
            _events.Add(new NarrativeEvent(type, payload, currentTick, expire));
        }

        /// <summary>查看队首事件而不移除</summary>
        public bool TryPeek(out NarrativeEvent evt) {
            if (_events.Count > 0) {
                evt = _events[0];
                return true;
            }
            evt = default;
            return false;
        }

        /// <summary>取出并移除队首事件</summary>
        public bool TryDequeue(out NarrativeEvent evt) {
            if (_events.Count > 0) {
                evt = _events[0];
                _events.RemoveAt(0);
                return true;
            }
            evt = default;
            return false;
        }

        /// <summary>是否存在指定类型的事件</summary>
        public bool Contains(string type) => _events.Any(e => e.Type == type);

        /// <summary>消费（移除）所有指定类型的事件，返回移除数量</summary>
        public int Consume(string type) => _events.RemoveAll(e => e.Type == type);

        /// <summary>清除已过期的事件</summary>
        public void PruneExpired(int currentTick) => _events.RemoveAll(e => e.IsExpired(currentTick));

        /// <summary>清空队列</summary>
        public void Clear() => _events.Clear();

        /// <summary>序列化</summary>
        public void Save(TagCompound tag) {
            tag["events"] = _events.Select(e => $"{e.Type}|{e.Payload}|{e.EnqueuedTick}|{e.ExpireTick}").ToList();
        }

        /// <summary>反序列化</summary>
        public void Load(TagCompound tag) {
            _events.Clear();
            if (!tag.TryGet("events", out List<string> raw)) {
                return;
            }
            foreach (string entry in raw) {
                string[] parts = entry.Split('|');
                if (parts.Length == 4
                    && int.TryParse(parts[1], out int payload)
                    && int.TryParse(parts[2], out int enq)
                    && int.TryParse(parts[3], out int exp)) {
                    _events.Add(new NarrativeEvent(parts[0], payload, enq, exp));
                }
            }
        }
    }
}
