using Terraria.Audio;
using Terraria.ID;

namespace InnoVault.Narrative.Styling
{
    /// <summary>
    /// Narrative 默认音效参数，对齐 CWR ADV 原型（<c>DialogueBoxBase</c> / <c>ADVRewardPopup</c> / <c>ADVChoiceBox</c>）<br/>
    /// Consumer 可在 <see cref="DialogueSkin"/> / <see cref="PopupSkin"/> / <see cref="ChoiceSkin"/> 中重载播放方法以替换音色
    /// </summary>
    public static class NarrativeAudioDefaults
    {
        /// <summary>打字机 tick（每 N 字）</summary>
        public static SoundStyle Typing => SoundID.MenuTick with { Pitch = -0.45f };

        /// <summary>自动播放切换（开启 / 关闭）</summary>
        public static SoundStyle ToggleAuto(bool enabled)
            => SoundID.MenuTick with { Pitch = enabled ? 0.5f : 0.1f };

        /// <summary>快进切换（开启 / 关闭）</summary>
        public static SoundStyle ToggleFast(bool enabled)
            => SoundID.MenuTick with { Pitch = enabled ? 0.65f : 0.1f };

        /// <summary>跳过至下一停顿点</summary>
        public static SoundStyle Skip => SoundID.MenuTick with { Pitch = 0.35f };

        /// <summary>打开 / 关闭历史对话面板</summary>
        public static SoundStyle Backlog => SoundID.MenuOpen with { Volume = 0.45f, Pitch = 0.15f };

        /// <summary>功能弹窗出现</summary>
        public static SoundStyle PopupOpen => SoundID.Item4 with { Volume = 0.4f, Pitch = -0.2f };

        /// <summary>弹窗领取 / 确认点击</summary>
        public static SoundStyle PopupClaim => SoundID.MenuOpen with { Volume = 0.5f, Pitch = 0.3f };

        /// <summary>奖励物品发放</summary>
        public static SoundStyle RewardGrant => SoundID.Grab with { Volume = 0.55f, Pitch = 0.15f };

        /// <summary>选项有效点击</summary>
        public static SoundStyle ChoiceSelect => SoundID.MenuTick;

        /// <summary>选项禁用点击</summary>
        public static SoundStyle ChoiceDisabled => SoundID.MenuClose with { Pitch = -0.3f };

        /// <summary>播放音效</summary>
        public static void Play(SoundStyle style) => SoundEngine.PlaySound(style);
    }
}
