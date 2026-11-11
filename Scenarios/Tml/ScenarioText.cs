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

        /// <summary>
        /// 请求因 <see cref="ScenarioRejectReason.MultiplayerUnsupported"/> 被拒绝时，可以向玩家展示的提示：当前端没有接上场景的网络后端
        /// </summary>
        public static LocalizedText MultiplayerUnsupported { get; private set; }

        /// <summary>换舞台期间的加载文字：正在保存当前世界</summary>
        public static LocalizedText Saving { get; private set; }

        /// <summary>换舞台期间的加载文字：正在生成场景</summary>
        public static LocalizedText Generating { get; private set; }

        /// <summary>换舞台期间的加载文字：正在进入场景</summary>
        public static LocalizedText Entering { get; private set; }

        /// <summary>换舞台期间的加载文字：正在返回主世界</summary>
        public static LocalizedText ReturningHome { get; private set; }

        /// <summary>多人模式下盖着幕布等其他玩家时画在幕布上的文字</summary>
        public static LocalizedText WaitingForPlayers { get; private set; }

        /// <summary>多人模式下正在进服的玩家赶上服务器换舞台、被请出去重新加入时看到的文字</summary>
        public static LocalizedText StageChanged { get; private set; }

        /// <inheritdoc/>
        protected override void Register() { }

        /// <inheritdoc/>
        public override void SetupContent() => SetStaticDefaults();

        /// <inheritdoc/>
        public override void SetStaticDefaults() {
            MultiplayerUnsupported = this.GetLocalization(nameof(MultiplayerUnsupported), () => "Scenarios are unavailable in multiplayer on this endpoint.");
            Saving = this.GetLocalization(nameof(Saving), () => "Saving world");
            Generating = this.GetLocalization(nameof(Generating), () => "Generating scenario");
            Entering = this.GetLocalization(nameof(Entering), () => "Entering scenario");
            ReturningHome = this.GetLocalization(nameof(ReturningHome), () => "Returning to the main world");
            WaitingForPlayers = this.GetLocalization(nameof(WaitingForPlayers), () => "Waiting for other players");
            StageChanged = this.GetLocalization(nameof(StageChanged), () => "The server moved to another scenario while you were joining. Please join again.");
        }
    }
}
