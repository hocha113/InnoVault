using InnoVault.Narrative.Core;
using System.Collections.Generic;
using Terraria.ModLoader.IO;

namespace InnoVault.Narrative.History
{
    /// <summary>
    /// 对话历史存储的窄接口。只负责"运行时内存里有哪些条目"的追加 / 读取 / 清空，<br/>
    /// 真正落盘由框架的 <c>NarrativeHistorySave</c>(<see cref="GameSystem.SaveContent{T}"/>) 承担。<br/>
    /// 消费者可以替换 <see cref="Services.NarrativeServices.History"/> 为自定义实现，框架仅依赖该契约，<br/>
    /// 并自带 <see cref="MemoryNarrativeHistoryStore"/> 作为默认实现
    /// </summary>
    public interface INarrativeHistoryStore
    {
        /// <summary>追加一条历史记录</summary>
        void Append(NarrativeLogEntry entry);
        /// <summary>按时间顺序读取全部历史记录（最旧在前）</summary>
        IReadOnlyList<NarrativeLogEntry> GetEntries();
        /// <summary>清空全部历史</summary>
        void Clear();
        /// <summary>当前记录条数</summary>
        int Count { get; }
    }

    /// <summary>
    /// 默认的内存实现，带容量上限（超出丢弃最旧），并提供 <see cref="Save"/> / <see cref="Load"/> 的
    /// <see cref="TagCompound"/> 序列化，供 <c>NarrativeHistorySave</c> 落盘 / 读回
    /// </summary>
    public sealed class MemoryNarrativeHistoryStore : INarrativeHistoryStore
    {
        private readonly List<NarrativeLogEntry> _entries = [];

        /// <summary>容量上限。超过后追加会丢弃最旧的记录，避免存档无限膨胀</summary>
        public int MaxEntries { get; set; } = 2000;

        /// <inheritdoc/>
        public int Count => _entries.Count;

        /// <inheritdoc/>
        public void Append(NarrativeLogEntry entry) {
            _entries.Add(entry);
            TrimToCapacity();
        }

        /// <inheritdoc/>
        public IReadOnlyList<NarrativeLogEntry> GetEntries() => _entries;

        /// <inheritdoc/>
        public void Clear() => _entries.Clear();

        private void TrimToCapacity() {
            if (MaxEntries <= 0) {
                return;
            }
            int overflow = _entries.Count - MaxEntries;
            if (overflow > 0) {
                _entries.RemoveRange(0, overflow);
            }
        }

        /// <summary>序列化到 <see cref="TagCompound"/></summary>
        public void Save(TagCompound tag) {
            List<TagCompound> list = new(_entries.Count);
            foreach (NarrativeLogEntry entry in _entries) {
                list.Add(new TagCompound {
                    ["k"] = (byte)entry.Kind,
                    ["s"] = entry.ScenarioKey ?? string.Empty,
                    ["sp"] = entry.Speaker.Value ?? string.Empty,
                    ["ex"] = entry.Expression.Value ?? string.Empty,
                    ["t"] = entry.Text ?? string.Empty,
                    ["st"] = entry.Style.Value ?? string.Empty,
                    ["c"] = entry.StartsConversation,
                });
            }
            tag["entries"] = list;
        }

        /// <summary>从 <see cref="TagCompound"/> 反序列化（会先清空当前内容）</summary>
        public void Load(TagCompound tag) {
            _entries.Clear();
            if (!tag.TryGet("entries", out List<TagCompound> list)) {
                return;
            }
            foreach (TagCompound entryTag in list) {
                _entries.Add(new NarrativeLogEntry(
                    (NarrativeLogKind)entryTag.GetByte("k"),
                    entryTag.GetString("s"),
                    new CharacterId(entryTag.GetString("sp")),
                    new ExpressionId(entryTag.GetString("ex")),
                    entryTag.GetString("t"),
                    new StyleId(entryTag.GetString("st")),
                    entryTag.GetBool("c")));
            }
            TrimToCapacity();
        }
    }
}
