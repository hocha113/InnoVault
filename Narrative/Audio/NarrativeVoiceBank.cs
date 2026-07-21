using System;
using Terraria.Audio;
using Terraria.ModLoader;

namespace InnoVault.Narrative.Audio
{
    /// <summary>
    /// 场景台词配音库：按约定路径生成 <see cref="SoundStyle"/>，供 <see cref="Composition.NarrativeComposer"/> 显式绑定<br/>
    /// 默认约定：<c>{mod}/{root}/L1.ogg</c>、<c>L2.ogg</c>…（无扩展名写入 SoundStyle 路径）<br/>
    /// 不按节点下标自动灌轨——条件图 / 插入 Wait 会打乱序号，绑轨须在 <c>Build</c> 里写明
    /// </summary>
    public sealed class NarrativeVoiceBank
    {
        private readonly SoundStyle[] _lines;
        private readonly int _startIndex;

        private NarrativeVoiceBank(SoundStyle[] lines, int startIndex) {
            _lines = lines;
            _startIndex = startIndex;
        }

        /// <summary>已登记的句数</summary>
        public int Count => _lines.Length;

        /// <summary>首句编号（默认 1，对应 <c>L1</c>）</summary>
        public int StartIndex => _startIndex;

        /// <summary>
        /// 按句号取配音（默认 1-based：<c>bank[1]</c> → <c>L1</c>）
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">句号不在库内</exception>
        public SoundStyle this[int lineNumber] {
            get {
                if (!TryGet(lineNumber, out SoundStyle style)) {
                    throw new ArgumentOutOfRangeException(nameof(lineNumber), lineNumber,
                        $"Voice line {lineNumber} is outside bank range {_startIndex}..{_startIndex + _lines.Length - 1}.");
                }
                return style;
            }
        }

        /// <summary>尝试按句号取配音</summary>
        public bool TryGet(int lineNumber, out SoundStyle style) {
            int index = lineNumber - _startIndex;
            if (index < 0 || index >= _lines.Length) {
                style = default;
                return false;
            }
            style = _lines[index];
            return true;
        }

        /// <summary>
        /// 从资源根路径创建配音库<br/>
        /// <paramref name="rootPath"/> 不含模组名与扩展名，例如 <c>Content/Scenarios/Himayo/Lines/FirstMetHimayo</c>
        /// </summary>
        /// <param name="mod">所属模组</param>
        /// <param name="rootPath">台词目录（相对模组根）</param>
        /// <param name="count">句数</param>
        /// <param name="prefix">文件名前缀，默认 <c>L</c></param>
        /// <param name="startIndex">首句编号，默认 1</param>
        /// <param name="volume">默认音量</param>
        public static NarrativeVoiceBank Create(
            Mod mod,
            string rootPath,
            int count,
            string prefix = "L",
            int startIndex = 1,
            float volume = 1f) {
            ArgumentNullException.ThrowIfNull(mod);
            if (string.IsNullOrWhiteSpace(rootPath)) {
                throw new ArgumentException("Voice root path is required.", nameof(rootPath));
            }
            if (count <= 0) {
                throw new ArgumentOutOfRangeException(nameof(count), count, "Voice bank count must be positive.");
            }
            if (string.IsNullOrEmpty(prefix)) {
                throw new ArgumentException("File name prefix is required.", nameof(prefix));
            }

            string root = rootPath.Trim().TrimEnd('/');
            var lines = new SoundStyle[count];
            for (int i = 0; i < count; i++) {
                int lineNumber = startIndex + i;
                lines[i] = new SoundStyle($"{mod.Name}/{root}/{prefix}{lineNumber}") {
                    Volume = volume,
                    MaxInstances = 1,
                    SoundLimitBehavior = SoundLimitBehavior.ReplaceOldest,
                };
            }

            return new NarrativeVoiceBank(lines, startIndex);
        }

        /// <summary>用已有 <see cref="SoundStyle"/> 序列包装为库（顺序对应 <paramref name="startIndex"/> 起的句号）</summary>
        public static NarrativeVoiceBank FromStyles(SoundStyle[] styles, int startIndex = 1) {
            ArgumentNullException.ThrowIfNull(styles);
            if (styles.Length == 0) {
                throw new ArgumentException("Voice style array must not be empty.", nameof(styles));
            }
            var copy = new SoundStyle[styles.Length];
            Array.Copy(styles, copy, styles.Length);
            return new NarrativeVoiceBank(copy, startIndex);
        }
    }
}
