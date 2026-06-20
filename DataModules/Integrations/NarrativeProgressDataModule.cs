using InnoVault.Narrative;
using System;
using System.Collections.Generic;
using System.Linq;
using Terraria.ModLoader.IO;

namespace InnoVault.DataModules
{
    /// <summary>
    /// 一个示范性的桥接模块：它既是可被 <see cref="DataModuleStore"/> 持久化的 <see cref="DataModule"/>，<br/>
    /// 又实现了叙事框架的 <see cref="INarrativeProgressStore"/>。<br/>
    /// 这样消费者就能把"叙事进度"纳入统一的模块化存档，而无需 Narrative 自身依赖 DataModules——<br/>
    /// 依赖方向是 DataModules → Narrative（消费契约），符合分层边界。<br/>
    /// <br/>
    /// 用法：在自己的 <c>ModPlayer</c> 持有一个 <see cref="DataModuleStore"/>，于存档钩子调用其 Save/Load，<br/>
    /// 并设置 <c>NarrativeServices.Progress = store.Get&lt;NarrativeProgressDataModule&gt;();</c>
    /// </summary>
    public sealed class NarrativeProgressDataModule : DataModule, INarrativeProgressStore
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
        public void SetProgress(string scenarioKey, ScenarioProgress progress) {
            if (!string.IsNullOrEmpty(scenarioKey)) {
                _progress[scenarioKey] = progress;
            }
        }

        /// <inheritdoc/>
        public bool TryGetChoice(string scenarioKey, out string choiceId) {
            if (scenarioKey != null && _choices.TryGetValue(scenarioKey, out choiceId)) {
                return true;
            }
            choiceId = null;
            return false;
        }

        /// <inheritdoc/>
        public void SetChoice(string scenarioKey, string choiceId) {
            if (!string.IsNullOrEmpty(scenarioKey)) {
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

        /// <inheritdoc/>
        public override void Reset() {
            _progress.Clear();
            _choices.Clear();
            _flags.Clear();
            _counters.Clear();
            _strings.Clear();
        }

        /// <inheritdoc/>
        public override void SaveData(TagCompound tag) {
            tag["progress"] = _progress.Select(kv => $"{kv.Key}={(int)kv.Value}").ToList();
            tag["choices"] = _choices.Select(kv => $"{kv.Key}={kv.Value}").ToList();
            tag["flags"] = _flags.Where(kv => kv.Value).Select(kv => kv.Key).ToList();
            tag["counters"] = _counters.Select(kv => $"{kv.Key}={kv.Value}").ToList();
            tag["strings"] = _strings.Select(kv => $"{kv.Key}={kv.Value}").ToList();
        }

        /// <inheritdoc/>
        public override void LoadData(TagCompound tag, int loadedVersion) {
            Reset();
            if (tag.TryGet("progress", out List<string> progress)) {
                foreach (string entry in progress) {
                    int eq = entry.LastIndexOf('=');
                    if (eq > 0 && int.TryParse(entry[(eq + 1)..], out int v)) {
                        _progress[entry[..eq]] = (ScenarioProgress)v;
                    }
                }
            }
            LoadStringMap(tag, "choices", _choices);
            if (tag.TryGet("flags", out List<string> flags)) {
                foreach (string flag in flags) {
                    _flags[flag] = true;
                }
            }
            if (tag.TryGet("counters", out List<string> counters)) {
                foreach (string entry in counters) {
                    int eq = entry.LastIndexOf('=');
                    if (eq > 0 && int.TryParse(entry[(eq + 1)..], out int v)) {
                        _counters[entry[..eq]] = v;
                    }
                }
            }
            LoadStringMap(tag, "strings", _strings);
        }

        private static void LoadStringMap(TagCompound tag, string key, Dictionary<string, string> target) {
            if (tag.TryGet(key, out List<string> entries)) {
                foreach (string entry in entries) {
                    int eq = entry.IndexOf('=');
                    if (eq >= 0) {
                        target[entry[..eq]] = entry[(eq + 1)..];
                    }
                }
            }
        }
    }
}
