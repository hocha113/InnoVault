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

        /// <summary>换舞台期间的加载文字：正在保存当前世界</summary>
        public static LocalizedText Saving { get; private set; }

        /// <summary>换舞台期间的加载文字：正在生成场景</summary>
        public static LocalizedText Generating { get; private set; }

        /// <summary>换舞台期间的加载文字：正在进入场景</summary>
        public static LocalizedText Entering { get; private set; }

        /// <summary>换舞台期间的加载文字：正在返回主世界</summary>
        public static LocalizedText ReturningHome { get; private set; }

        /// <inheritdoc/>
        protected override void Register() { }

        /// <inheritdoc/>
        public override void SetupContent() => SetStaticDefaults();

        /// <inheritdoc/>
        public override void SetStaticDefaults() {
            MultiplayerUnsupported = this.GetLocalization(nameof(MultiplayerUnsupported), () => "Scenarios are not supported in multiplayer yet.");
            Saving = this.GetLocalization(nameof(Saving), () => "Saving world");
            Generating = this.GetLocalization(nameof(Generating), () => "Generating scenario");
            Entering = this.GetLocalization(nameof(Entering), () => "Entering scenario");
            ReturningHome = this.GetLocalization(nameof(ReturningHome), () => "Returning to the main world");
        }
    }
}
