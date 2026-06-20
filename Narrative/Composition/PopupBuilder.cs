using InnoVault.Narrative.Core;

namespace InnoVault.Narrative.Composition
{
    /// <summary>
    /// 功能弹窗载荷的便捷工厂。返回的载荷可继续链式配置（如 <c>Reward(id).Titled("...").Claimable()</c>），<br/>
    /// 再传入 <see cref="NarrativeComposer.Popup(PopupPayload, bool, System.Action)"/>
    /// </summary>
    public static class Popups
    {
        /// <summary>构造一个物品奖励载荷</summary>
        public static RewardPayload Reward(int itemType, int stack = 1, string title = null)
            => new() { ItemType = itemType, Stack = stack <= 0 ? 1 : stack, Title = title };

        /// <summary>构造一个文本提示载荷</summary>
        public static MessagePopupPayload Message(string title, string body = null)
            => new() { Title = title, Body = body };
    }
}
