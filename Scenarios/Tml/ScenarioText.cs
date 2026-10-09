using Terraria.Localization;
using Terraria.ModLoader;

namespace InnoVault.Scenarios
{
    /// <summary>
    /// 场景框架面向玩家的提示文本
    /// </summary>
    public sealed class ScenarioText : ModType, ILocalizedModType
    {
        /// <inheritdoc/>
        public string LocalizationCategory => "Scenarios";

        /// <summary>请求因 <see cref="ScenarioRejectReason.MultiplayerUnsupported"/> 被拒绝时，可以向玩家展示的提示</summary>
        public static LocalizedText MultiplayerUnsupported { get; private set; }

        /// <inheritdoc/>
        protected override void Register() { }

        /// <inheritdoc/>
        public override void SetupContent() => SetStaticDefaults();

        /// <inheritdoc/>
        public override void SetStaticDefaults() {
            MultiplayerUnsupported = this.GetLocalization(nameof(MultiplayerUnsupported), () => "Scenarios are not supported in multiplayer yet.");
        }
    }
}
