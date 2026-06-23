namespace InnoVault.Narrative.Core
{
    /// <summary>
    /// 叙事 id 的模组作用域解析<br/>
    /// <see cref="NarrativeScenario"/> 继承 <see cref="VaultType{T}"/>，构建期已知所属 Mod；<br/>
    /// 内容作者可写短名（如 <c>"Helen"</c>），框架自动补全为 <c>ModName/Helen</c>
    /// </summary>
    internal static class NarrativeIdScope
    {
        /// <summary>解析角色 id；已含 <c>/</c> 或 Mod 未知时原样返回</summary>
        public static CharacterId Character(string modName, CharacterId id) {
            if (string.IsNullOrEmpty(modName) || id.IsEmpty || IsScoped(id.Value)) {
                return id;
            }

            return CharacterId.ForMod(modName, id.Value);
        }

        /// <summary>解析样式 id；已是全局默认或已含 <c>/</c> 时原样返回</summary>
        public static StyleId Style(string modName, StyleId id) {
            if (string.IsNullOrEmpty(modName) || id.IsEmpty || id.Value == StyleId.Default.Value || IsScoped(id.Value)) {
                return id;
            }

            return StyleId.ForMod(modName, id.Value);
        }

        private static bool IsScoped(string value) => !string.IsNullOrEmpty(value) && value.Contains('/');
    }
}
