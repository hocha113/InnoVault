using System.Collections.Generic;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace InnoVault.GameContent
{
    internal class SaveWorld : VaultType
    {
        public static List<SaveWorld> SaveWorlds { get; private set; } = [];
        public static Dictionary<Mod, List<SaveWorld>> ModToSaves { get; private set; } = [];
        protected override void Register() {
            SaveWorlds.Add(this);
        }

        public override void SetupContent() {
            ModToSaves.TryAdd(Mod, []);
            ModToSaves[Mod].Add(this);
            SetStaticDefaults();
        }

        public virtual void SaveData(TagCompound tag) {

        }

        public virtual void LoadData(TagCompound tag) {

        }
    }
}
