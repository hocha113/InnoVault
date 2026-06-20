using InnoVault.Narrative.Services;
using Terraria;

namespace InnoVault.Narrative.Core
{
    /// <summary>
    /// 功能弹窗的解析结果
    /// </summary>
    public enum PopupResolution
    {
        /// <summary>尚未解析</summary>
        Pending,
        /// <summary>已领取 / 确认</summary>
        Claimed,
        /// <summary>已关闭 / 取消</summary>
        Dismissed,
        /// <summary>因超时自动结束</summary>
        Timeout,
    }

    /// <summary>
    /// 功能弹窗载荷基类。弹窗本身只是"展示 + 解析"通道，真正的副作用（如发放物品）<br/>
    /// 由载荷在 <see cref="OnClaimed"/> 中委托给宿主注入的服务执行，框架不直接处理业务逻辑<br/>
    /// 这使奖励只是众多弹窗形态中的一种，而不是被写死的"礼物盒"
    /// </summary>
    public abstract class PopupPayload
    {
        /// <summary>标题文本（通常是物品名或提示标题）</summary>
        public string Title { get; set; }
        /// <summary>是否必须点击领取（否则可在悬停 / 超时后自动结束）</summary>
        public bool RequireClaim { get; set; } = true;
        /// <summary>自动保持秒数，小于 0 表示一直等待玩家操作</summary>
        public float AutoHoldSeconds { get; set; } = -1f;

        /// <summary>
        /// 默认皮肤会在该值大于 0 时绘制对应物品图标，避免皮肤层做类型判断
        /// </summary>
        public virtual int IconItemType => 0;
        /// <summary>默认皮肤会在该值非空时绘制正文文本</summary>
        public virtual string BodyText => null;

        /// <summary>被领取 / 确认时调用，子类在此通过宿主服务执行副作用</summary>
        public virtual void OnClaimed(Player player) { }
        /// <summary>被关闭 / 取消时调用</summary>
        public virtual void OnDismissed(Player player) { }
        /// <summary>因自动保持时间结束而关闭时调用，默认等同于取消</summary>
        public virtual void OnTimedOut(Player player) => OnDismissed(player);

        /// <summary>链式设置标题</summary>
        public PopupPayload Titled(string title) {
            Title = title;
            return this;
        }

        /// <summary>链式设置是否必须点击领取</summary>
        public PopupPayload Claimable(bool require = true) {
            RequireClaim = require;
            return this;
        }

        /// <summary>链式设置自动保持秒数（小于 0 表示一直等待玩家操作）</summary>
        public PopupPayload Hold(float seconds) {
            AutoHoldSeconds = seconds;
            return this;
        }
    }

    /// <summary>
    /// 物品奖励弹窗。展示一个物品，领取时通过 <see cref="NarrativeServices.RewardGrant"/> 实际发放，<br/>
    /// 框架自身不调用 <see cref="Player.QuickSpawnItem(Terraria.DataStructures.IEntitySource, int, int)"/>
    /// </summary>
    public sealed class RewardPayload : PopupPayload
    {
        /// <summary>物品类型</summary>
        public int ItemType { get; set; }
        /// <summary>数量</summary>
        public int Stack { get; set; } = 1;
        /// <inheritdoc/>
        public override int IconItemType => ItemType;
        /// <inheritdoc/>
        public override void OnClaimed(Player player) => NarrativeServices.RewardGrant?.Grant(this, player);
    }

    /// <summary>纯文本提示弹窗（标题 + 正文），用于教程提示、确认信息等</summary>
    public sealed class MessagePopupPayload : PopupPayload
    {
        /// <summary>正文文本</summary>
        public string Body { get; set; }
        /// <inheritdoc/>
        public override string BodyText => Body;
    }
}
