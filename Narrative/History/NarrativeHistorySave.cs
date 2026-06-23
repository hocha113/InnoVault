using InnoVault.GameSystem;
using InnoVault.Narrative.Services;
using System;
using System.IO;
using System.Text;
using Terraria;
using Terraria.ModLoader.IO;

namespace InnoVault.Narrative.History
{
    /// <summary>
    /// 对话历史的<b>按角色、客户端本地</b>持久化。基于框架 <see cref="SaveContent{T}"/> 体系，<br/>
    /// 存档文件按当前角色（<see cref="Main.ActivePlayerFileData"/>）区分，落在 InnoVault 存档根目录下的独立 NBT 文件<br/>
    /// 叙事本就只在客户端运行，故历史也由客户端按本机角色读写，不经服务器世界存档<br/>
    /// 读写时机由 <see cref="NarrativeSystem"/> 驱动，调用入口为 <see cref="NarrativeHistory.Save"/> / <see cref="NarrativeHistory.Load"/>
    /// </summary>
    public sealed class NarrativeHistorySave : SaveContent<NarrativeHistorySave>
    {
        /// <inheritdoc/>
        public override string SavePath => Path.Combine(VaultSave.RootPath, "Narrative", $"history_{ResolveIdentity()}.nbt");

        /// <inheritdoc/>
        public override void SaveData(TagCompound tag) {
            if (NarrativeServices.History is MemoryNarrativeHistoryStore store) {
                store.Save(tag);
            }
        }

        /// <inheritdoc/>
        public override void LoadData(TagCompound tag) {
            if (NarrativeServices.History is MemoryNarrativeHistoryStore store) {
                store.Load(tag);
            }
        }

        /// <summary>把当前内存历史写入当前角色的存档文件</summary>
        internal static void Persist() => DoSave<NarrativeHistorySave>();

        /// <summary>从当前角色的存档文件读回历史（强制读盘，避免读到上一个角色的缓存）</summary>
        internal static void Restore() => DoLoad<NarrativeHistorySave>(forceReload: true);

        private static string ResolveIdentity() {
            string raw = Main.ActivePlayerFileData?.Name;
            if (string.IsNullOrWhiteSpace(raw)) {
                raw = Main.LocalPlayer?.name;
            }
            return string.IsNullOrWhiteSpace(raw) ? "default" : Sanitize(raw);
        }

        private static string Sanitize(string name) {
            char[] invalid = Path.GetInvalidFileNameChars();
            StringBuilder sb = new(name.Length);
            foreach (char c in name) {
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            }
            return sb.ToString();
        }
    }
}
