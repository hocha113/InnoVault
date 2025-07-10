using InnoVault.GameContent;
using InnoVault.TileProcessors;
using System.Collections.Generic;
using System.IO;
using Terraria;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace InnoVault.GameSystem
{
    internal class VaultSave : ModSystem
    {
        public const string RootFilesName = "InnoVaultData";
        public static string RootPath => Path.Combine(Main.SavePath, RootFilesName);
        public static string SaveWorldPath => Path.Combine(RootPath, "WorldDatas", $"{Main.worldName}.nbt");
        public static string SaveTPDataPath => Path.Combine(RootPath, "TPDatas", $"{Main.worldName}.nbt");
        //首先我们要明白一点，在多人模式的情况下，只有服务器会加载这两个钩子，其他客户端并不会正常运行
        //所以，如果想数据正常加载，就需要发一个巨大的数据包来让其他的端同步，Save的时候要保证世界数据同步，而Load的时候要保证其他端也被加载
        public override void SaveWorldData(TagCompound tag) {
            //先尝试寻找nbt存档，如果没有存档的话就利用tag保存以此写入到原版存档位置中，以便下一次加载时读取进NBT
            if (!File.Exists(SaveTPDataPath)) {
                TileProcessorLoader.SaveWorldData(tag);
            }
            DoSaveWorld();
        }

        public override void LoadWorldData(TagCompound tag) {
            DoLoadWorld();
            TileProcessorLoader.ActiveWorldTagData = tag;
        }

        public static void DoSaveWorld() {
            if (SaveWorld.ModToSaves.Count == 0) {
                return;
            }

            TagCompound rootTag = new TagCompound();

            foreach (var kv in SaveWorld.ModToSaves) {
                Mod mod = kv.Key;
                List<SaveWorld> saves = kv.Value;

                TagCompound modTag = new TagCompound();
                foreach (var save in saves) {
                    save.SaveData(modTag); // 将数据写入 modTag
                }

                rootTag[$"mod:{mod.Name}"] = modTag;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(SaveWorldPath)!);
            using FileStream fileStream = File.Create(SaveWorldPath);
            TagIO.ToStream(rootTag, fileStream);

            var tps = TileProcessorLoader.TP_InWorld.FindAll(tp => tp != null && tp.Active);
            if (tps.Count <= 0) {
                return;
            }

            rootTag = new TagCompound();
            TileProcessorLoader.SaveWorldData(rootTag);
            Directory.CreateDirectory(Path.GetDirectoryName(SaveTPDataPath)!);
            using FileStream fileStream2 = File.Create(SaveTPDataPath);
            TagIO.ToStream(rootTag, fileStream2);
        }

        public static void DoLoadWorld() {
            if (SaveWorld.ModToSaves.Count == 0) {
                return;
            }

            TagCompound rootTag = LoadRootTag(SaveWorldPath);

            foreach (var kv in SaveWorld.ModToSaves) {
                Mod mod = kv.Key;
                List<SaveWorld> saves = kv.Value;

                if (!rootTag.ContainsKey($"mod:{mod.Name}"))
                    continue;

                TagCompound modTag = rootTag.Get<TagCompound>($"mod:{mod.Name}");

                foreach (var save in saves) {
                    save.LoadData(modTag);
                }
            }
        }

        public static TagCompound LoadRootTag(string filePath) {
            if (!File.Exists(filePath)) {
                throw new FileNotFoundException("NBT file not found：", filePath);
            }
            using FileStream fs = File.OpenRead(filePath);
            return TagIO.FromStream(fs);
        }
    }
}
