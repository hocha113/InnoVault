using InnoVault.GameSystem;
using Terraria.ModLoader.IO;

namespace InnoVault.UIHandles
{
    internal class UIDataSave : SaveMod
    {
        public override void SaveData(TagCompound tag) {
            UIHandleLoader.SaveUIData(tag);
        }

        public override void LoadData(TagCompound tag) {
            UIHandleLoader.LoadUIData(tag);
        }
    }
}
