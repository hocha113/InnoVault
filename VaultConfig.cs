using System.ComponentModel;
using Terraria.ModLoader.Config;

namespace InnoVault
{
    internal class VaultClientConfig : ModConfig
    {
        public static VaultClientConfig Instance { get; private set; }

        public override ConfigScope Mode => ConfigScope.ClientSide;

        public override void OnLoaded() => Instance = this;

        [BackgroundColor(60, 130, 180, 155)]
        [DefaultValue(false)]
        public bool HideWorldLoadingScreen { get; set; }
    }
}
