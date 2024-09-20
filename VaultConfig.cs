using System.ComponentModel;
using Terraria.ModLoader.Config;

namespace InnoVault
{
    internal class VaultClientConfig : ModConfig
    {
        public static VaultClientConfig Instance { get; private set; }

        public override ConfigScope Mode => ConfigScope.ClientSide;
        
        public override void OnLoaded() => Instance = this;

        [BackgroundColor(45, 175, 225, 255)]
        [DefaultValue(false)]
        public bool TileProcessorBoxSizeDraw { get; set; }
    }
}
