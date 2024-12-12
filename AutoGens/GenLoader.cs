using InnoVault.AutoGens;
using System;
using System.Collections.Generic;
using Terraria.GameContent.Generation;
using Terraria.ModLoader;
using Terraria.WorldBuilding;

namespace InnoVault.InnoGens
{
    internal class GenLoader : ModSystem, IVaultLoader
    {
        internal static List<AutoGen> GlobalAutoGens { get; private set; } = [];
        internal static Dictionary<Type, Mod> Gen_Type_To_Mod { get; private set; } = [];
        void IVaultLoader.LoadData() {
            GlobalAutoGens = VaultUtils.GetSubclassInstances<AutoGen>();
            foreach (var gen in GlobalAutoGens) {
                VaultUtils.AddTypeModAssociation(Gen_Type_To_Mod, gen.GetType(), ModLoader.Mods);
            }
        }
        void IVaultLoader.UnLoadData() {
            GlobalAutoGens?.Clear();
            Gen_Type_To_Mod?.Clear();
        }
        public override void ModifyWorldGenTasks(List<GenPass> tasks, ref double totalWeight) {
            foreach (var gen in GlobalAutoGens) {
                int index = tasks.FindIndex((GenPass genpass) => genpass.Name.Equals(gen.IndexName));
                if (index > -1) {
                    tasks.Insert(index + 1, new PassLegacy(gen.GenName, gen.Pass));
                }
            }
        }
    }
}
