using System;
using System.Collections.Generic;
using System.Linq;
using Terraria.ModLoader.IO;

namespace InnoVault.Narrative
{
    /// <summary>
    /// 叙事进度存储的窄接口。只暴露与"剧情语义"相关的读写（场景进度、选择分支、标记、计数器、字符串），<br/>
    /// 通用的模块化保存属于独立的数据系统职责，不应塞进叙事框架。<br/>
    /// 消费者可以用自己的 <c>ModPlayer</c> / <c>SaveContent</c> / 未来的 DataModules 实现本接口，<br/>
    /// 框架仅依赖该契约，并自带 <see cref="MemoryNarrativeProgressStore"/> 作为默认实现
    /// </summary>
    public interface INarrativeProgressStore
    {
        /// <summary>读取场景整体进度</summary>
        ScenarioProgress GetProgress(string scenarioKey);
        /// <summary>写入场景整体进度</summary>
        void SetProgress(string scenarioKey, ScenarioProgress progress);
        /// <summary>读取某场景上一次选择的选项 id</summary>
        bool TryGetChoice(string scenarioKey, out string choiceId);
        /// <summary>记录某场景的选择分支</summary>
        void SetChoice(string scenarioKey, string choiceId);
        /// <summary>读取布尔标记</summary>
        bool GetFlag(NarrativeProgressKey key);
        /// <summary>写入布尔标记</summary>
        void SetFlag(NarrativeProgressKey key, bool value);
        /// <summary>读取计数器</summary>
        int GetCounter(NarrativeProgressKey key);
        /// <summary>写入计数器</summary>
        void SetCounter(NarrativeProgressKey key, int value);
        /// <summary>读取字符串字段（可用于存放枚举名等）</summary>
        string GetString(NarrativeProgressKey key);
        /// <summary>写入字符串字段</summary>
        void SetString(NarrativeProgressKey key, string value);
    }

    /// <summary>
    /// 默认的内存 + 简单 <see cref="TagCompound"/> 实现。<br/>
    /// 适合单机本地剧情进度；消费者若需要与玩家存档 / 世界存档绑定，可在自己的存档钩子里调用 <see cref="Save"/> / <see cref="Load"/>
    /// </summary>
    public sealed class MemoryNarrativeProgressStore : INarrativeProgressStore
    {
        private readonly Dictionary<string, ScenarioProgress> _progress = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _choices = new(StringComparer.Ordinal);
        private readonly Dictionary<string, bool> _flags = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _counters = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _strings = new(StringComparer.Ordinal);

        /// <inheritdoc/>
        public ScenarioProgress GetProgress(string scenarioKey)
            => scenarioKey != null && _progress.TryGetValue(scenarioKey, out var p) ? p : ScenarioProgress.None;

        /// <inheritdoc/>
        public void SetProgress(string scenarioKey, ScenarioProgress progress)
        {
            if (!string.IsNullOrEmpty(scenarioKey))
            {
                _progress[scenarioKey] = progress;
            }
        }

        /// <inheritdoc/>
        public bool TryGetChoice(string scenarioKey, out string choiceId)
        {
            if (scenarioKey != null && _choices.TryGetValue(scenarioKey, out choiceId))
            {
                return true;
            }
            choiceId = null;
            return false;
        }

        /// <inheritdoc/>
        public void SetChoice(string scenarioKey, string choiceId)
        {
            if (!string.IsNullOrEmpty(scenarioKey))
            {
                _choices[scenarioKey] = choiceId ?? string.Empty;
            }
        }

        /// <inheritdoc/>
        public bool GetFlag(NarrativeProgressKey key) => _flags.TryGetValue(key.Flat, out bool v) && v;
        /// <inheritdoc/>
        public void SetFlag(NarrativeProgressKey key, bool value) => _flags[key.Flat] = value;
        /// <inheritdoc/>
        public int GetCounter(NarrativeProgressKey key) => _counters.TryGetValue(key.Flat, out int v) ? v : 0;
        /// <inheritdoc/>
        public void SetCounter(NarrativeProgressKey key, int value) => _counters[key.Flat] = value;
        /// <inheritdoc/>
        public string GetString(NarrativeProgressKey key) => _strings.TryGetValue(key.Flat, out string v) ? v : null;
        /// <inheritdoc/>
        public void SetString(NarrativeProgressKey key, string value) => _strings[key.Flat] = value ?? string.Empty;

        /// <summary>清空全部进度（世界切换 / 重新开始时使用）</summary>
        public void Clear()
        {
            _progress.Clear();
            _choices.Clear();
            _flags.Clear();
            _counters.Clear();
            _strings.Clear();
        }

        /// <summary>序列化到 <see cref="TagCompound"/></summary>
        public void Save(TagCompound tag)
        {
            tag["progress"] = _progress.Select(kv => $"{kv.Key}={(int)kv.Value}").ToList();
            tag["choices"] = _choices.Select(kv => $"{kv.Key}={kv.Value}").ToList();
            tag["flags"] = _flags.Where(kv => kv.Value).Select(kv => kv.Key).ToList();
            tag["counters"] = _counters.Select(kv => $"{kv.Key}={kv.Value}").ToList();
            tag["strings"] = _strings.Select(kv => $"{kv.Key}={kv.Value}").ToList();
        }

        /// <summary>从 <see cref="TagCompound"/> 反序列化</summary>
        public void Load(TagCompound tag)
        {
            Clear();
            if (tag.TryGet("progress", out List<string> progress))
            {
                foreach (string entry in progress)
                {
                    int eq = entry.LastIndexOf('=');
                    if (eq > 0 && int.TryParse(entry[(eq + 1)..], out int v))
                    {
                        _progress[entry[..eq]] = (ScenarioProgress)v;
                    }
                }
            }
            if (tag.TryGet("choices", out List<string> choices))
            {
                foreach (string entry in choices)
                {
                    int eq = entry.IndexOf('=');
                    if (eq >= 0)
                    {
                        _choices[entry[..eq]] = entry[(eq + 1)..];
                    }
                }
            }
            if (tag.TryGet("flags", out List<string> flags))
            {
                foreach (string flag in flags)
                {
                    _flags[flag] = true;
                }
            }
            if (tag.TryGet("counters", out List<string> counters))
            {
                foreach (string entry in counters)
                {
                    int eq = entry.LastIndexOf('=');
                    if (eq > 0 && int.TryParse(entry[(eq + 1)..], out int v))
                    {
                        _counters[entry[..eq]] = v;
                    }
                }
            }
            if (tag.TryGet("strings", out List<string> strings))
            {
                foreach (string entry in strings)
                {
                    int eq = entry.IndexOf('=');
                    if (eq >= 0)
                    {
                        _strings[entry[..eq]] = entry[(eq + 1)..];
                    }
                }
            }
        }
    }
}
