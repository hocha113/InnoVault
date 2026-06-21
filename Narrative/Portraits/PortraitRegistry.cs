using InnoVault.Narrative.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;

namespace InnoVault.Narrative.Portraits
{
    /// <summary>
    /// 角色立绘注册表。以 <see cref="CharacterId"/> 为键集中管理 <see cref="SpeakerProfile"/>，<br/>
    /// 供对话框解析显示名与立绘。未注册的角色会返回一个以 id 为名的临时档案，保证健壮
    /// </summary>
    public static class PortraitRegistry
    {
        private static readonly Dictionary<CharacterId, SpeakerProfile> _profiles = [];

        /// <summary>注册（或获取已存在的）角色档案，返回档案以便链式配置</summary>
        public static SpeakerProfile Register(CharacterId id) {
            WarnIfUnprefixed(id);
            if (!_profiles.TryGetValue(id, out var profile)) {
                profile = new SpeakerProfile(id);
                _profiles[id] = profile;
            }
            return profile;
        }

        /// <summary>注册（或获取已存在的）带模组前缀角色档案（推荐消费者使用）</summary>
        public static SpeakerProfile Register(string modName, string name)
            => Register(CharacterId.ForMod(modName, name));

        /// <summary>注册一个已构建好的角色档案（同 id 覆盖）</summary>
        public static SpeakerProfile Register(SpeakerProfile profile) {
            if (profile != null) {
                WarnIfUnprefixed(profile.Id);
                if (_profiles.ContainsKey(profile.Id)) {
                    VaultMod.Instance.Logger.Warn($"Narrative speaker profile '{profile.Id}' is being replaced. Use ModName/CharacterName ids to avoid cross-mod conflicts.");
                }
                _profiles[profile.Id] = profile;
            }
            return profile;
        }

        /// <summary>尝试获取角色档案</summary>
        public static bool TryGet(CharacterId id, out SpeakerProfile profile) => _profiles.TryGetValue(id, out profile);

        /// <summary>获取角色档案，缺省时返回一个临时默认档案（显示名 = id）</summary>
        public static SpeakerProfile GetOrDefault(CharacterId id)
            => _profiles.TryGetValue(id, out var profile) ? profile : new SpeakerProfile(id);

        /// <summary>解析角色显示名</summary>
        public static string ResolveName(CharacterId id) => GetOrDefault(id).ResolveName();

        /// <summary>解析角色在指定表情下的立绘</summary>
        public static Texture2D ResolvePortrait(CharacterId id, ExpressionId expression)
            => _profiles.TryGetValue(id, out var profile) ? profile.ResolvePortrait(expression) : null;

        /// <summary>解析角色在指定表情下的立绘裁剪区域</summary>
        public static Rectangle? ResolvePortraitSource(CharacterId id, ExpressionId expression)
            => _profiles.TryGetValue(id, out var profile) ? profile.ResolvePortraitSource(expression) : null;

        /// <summary>是否为剪影绘制</summary>
        public static bool IsSilhouette(CharacterId id) => _profiles.TryGetValue(id, out var profile) && profile.Silhouette;

        /// <summary>清空注册表（卸载时调用）</summary>
        internal static void Clear() => _profiles.Clear();

        private static void WarnIfUnprefixed(CharacterId id) {
            if (!id.IsEmpty && !id.Value.Contains('/')) {
                VaultMod.Instance.Logger.Warn($"Narrative CharacterId '{id}' has no mod prefix. Prefer CharacterId.ForMod(modName, name) to avoid cross-mod conflicts.");
            }
        }
    }
}
