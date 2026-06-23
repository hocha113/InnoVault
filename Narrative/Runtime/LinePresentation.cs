using InnoVault.Narrative.Core;
using Microsoft.Xna.Framework;

namespace InnoVault.Narrative.Runtime
{
    /// <summary>
    /// 当前正在展示的一行对话的运行期状态<br/>
    /// 它在对话框层与运行时层之间充当共享黑板：布局后的总字符数由视图回填，<br/>
    /// 打字进度与限时倒计时由运行时推进，从而把"布局"留在视图、"计时"留在运行时
    /// </summary>
    public sealed class LinePresentation
    {
        /// <summary>说话角色</summary>
        public CharacterId Speaker { get; private set; }
        /// <summary>表情</summary>
        public ExpressionId Expression { get; private set; }
        /// <summary>文本</summary>
        public string Text { get; private set; } = string.Empty;

        /// <summary>已显示字符数（浮点，按 tick 推进）</summary>
        public float VisibleChars { get; set; }
        /// <summary>折行布局后的总可见字符数，由视图回填</summary>
        public int TotalChars { get; set; }
        /// <summary>布局是否就绪（视图至少完成过一次折行）</summary>
        public bool LayoutReady { get; set; }

        /// <summary>是否限时</summary>
        public bool IsTimed { get; private set; }
        /// <summary>限时总 tick</summary>
        public float TimedTotalTicks { get; private set; }
        /// <summary>限时剩余 tick</summary>
        public float TimedRemainingTicks { get; set; }
        /// <summary>限时是否允许手动提前推进</summary>
        public bool AllowManualAdvance { get; private set; } = true;
        /// <summary>是否绘制限时指示</summary>
        public bool ShowTimedIndicator { get; private set; } = true;

        /// <summary>是否已打完字（空文本在布局就绪后即视为完成，避免空 prompt 选项卡死）</summary>
        public bool Finished => LayoutReady && VisibleChars >= TotalChars;
        /// <summary>打字进度 0~1</summary>
        public float TypeProgress => TotalChars <= 0 ? 0f : MathHelper.Clamp(VisibleChars / TotalChars, 0f, 1f);
        /// <summary>限时剩余比例 0~1（1 为刚开始）</summary>
        public float TimedProgress => TimedTotalTicks <= 0f ? 0f : MathHelper.Clamp(TimedRemainingTicks / TimedTotalTicks, 0f, 1f);
        /// <summary>当前应显示的字符数（取整）</summary>
        public int VisibleCharCount => (int)VisibleChars;

        /// <summary>开始展示新的一行，重置打字与限时状态</summary>
        public void Begin(CharacterId speaker, ExpressionId expression, string text, TimedSettings timed) {
            Speaker = speaker;
            Expression = expression;
            Text = text ?? string.Empty;
            VisibleChars = 0f;
            TotalChars = 0;
            LayoutReady = false;

            IsTimed = timed != null;
            if (IsTimed) {
                TimedTotalTicks = timed.Seconds * 60f;
                TimedRemainingTicks = TimedTotalTicks;
                AllowManualAdvance = timed.AllowManualAdvance;
                ShowTimedIndicator = timed.ShowIndicator;
            }
            else {
                TimedTotalTicks = 0f;
                TimedRemainingTicks = 0f;
                AllowManualAdvance = true;
                ShowTimedIndicator = true;
            }
        }

        /// <summary>立即显示全部字符</summary>
        public void RevealAll() => VisibleChars = TotalChars;

        /// <summary>是否有内容可显示</summary>
        public bool HasContent => !string.IsNullOrEmpty(Text);
    }
}
