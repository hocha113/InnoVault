using System;
using System.Collections.Generic;

namespace InnoVault.Narrative
{
    /// <summary>
    /// 节点类型，显式标记取代旧实现里靠"是否含回调 / 选项"推断特殊节点的脆弱做法
    /// </summary>
    public enum NodeKind
    {
        /// <summary>一句对话</summary>
        Say,
        /// <summary>带选项的对话</summary>
        Choice,
        /// <summary>功能弹窗（奖励 / 提示等）</summary>
        Popup,
        /// <summary>执行一次宿主回调</summary>
        Command,
        /// <summary>条件跳转</summary>
        Branch,
        /// <summary>等待固定时长</summary>
        Wait,
    }

    /// <summary>
    /// 流程跳转目标，用于选项分支 / 条件分支 / 章节衔接，<br/>
    /// 让分支可以声明式表达，而不必为每个分支堆叠嵌套场景类
    /// </summary>
    public sealed class NarrativeTarget
    {
        /// <summary>跳转种类</summary>
        public enum TargetKind
        {
            /// <summary>继续当前图的下一个节点</summary>
            Continue,
            /// <summary>跳转到当前图内的某个标签</summary>
            GotoLabel,
            /// <summary>结束当前场景并启动另一个场景</summary>
            GotoScenario,
            /// <summary>结束当前场景</summary>
            End,
        }

        /// <summary>本目标的种类</summary>
        public TargetKind Kind { get; private init; }
        /// <summary>当 <see cref="Kind"/> 为 <see cref="TargetKind.GotoLabel"/> 时的目标标签</summary>
        public string Label { get; private init; }
        /// <summary>当 <see cref="Kind"/> 为 <see cref="TargetKind.GotoScenario"/> 时的目标场景 Key</summary>
        public string ScenarioKey { get; private init; }

        /// <summary>继续到下一节点</summary>
        public static NarrativeTarget Continue { get; } = new() { Kind = TargetKind.Continue };
        /// <summary>结束当前场景</summary>
        public static NarrativeTarget End { get; } = new() { Kind = TargetKind.End };
        /// <summary>跳转到图内标签</summary>
        public static NarrativeTarget Goto(string label) => new() { Kind = TargetKind.GotoLabel, Label = label };
        /// <summary>跳转到另一个场景</summary>
        public static NarrativeTarget Scenario(string scenarioKey) => new() { Kind = TargetKind.GotoScenario, ScenarioKey = scenarioKey };
    }

    /// <summary>
    /// 定时设置，用于"限时对话"与"限时选择"。计时由运行时统一推进，不散落到各 UI 控件
    /// </summary>
    public sealed class TimedSettings
    {
        /// <summary>持续秒数</summary>
        public float Seconds { get; set; } = 6f;
        /// <summary>是否允许玩家手动提前推进 / 选择</summary>
        public bool AllowManualAdvance { get; set; } = true;
        /// <summary>是否绘制倒计时指示（具体表现交给皮肤）</summary>
        public bool ShowIndicator { get; set; } = true;
        /// <summary>限时结束时的回调（限时选择可在此选择默认项）</summary>
        public Action OnExpired { get; set; }
        /// <summary>创建一个简单定时设置</summary>
        public static TimedSettings Of(float seconds) => new() { Seconds = seconds };
    }

    /// <summary>
    /// 一个选项。拥有稳定 <see cref="ChoiceId"/>、启用条件、禁用提示、副作用回调与跳转目标，<br/>
    /// 选择后的流程收尾由运行时统一处理，内容作者无需手动关闭对话或防止重复触发
    /// </summary>
    public sealed class ChoiceOption
    {
        /// <summary>稳定标识</summary>
        public ChoiceId Id { get; set; }
        /// <summary>显示文本</summary>
        public string Text { get; set; }
        /// <summary>启用判定，<see langword="null"/> 表示始终启用</summary>
        public Func<bool> Enabled { get; set; }
        /// <summary>禁用时的提示文本</summary>
        public string DisabledHint { get; set; }
        /// <summary>选择时的副作用回调（在应用 <see cref="Target"/> 之前执行）</summary>
        public Action OnSelect { get; set; }
        /// <summary>选择后的流程跳转，默认继续下一节点</summary>
        public NarrativeTarget Target { get; set; } = NarrativeTarget.Continue;

        /// <summary>当前是否可用</summary>
        public bool IsEnabled => Enabled == null || Enabled();
    }

    /// <summary>
    /// 叙事节点基类。所有具体节点都是不可变的"构建期数据"，运行期状态由 <see cref="NarrativeSession"/> 单独持有
    /// </summary>
    public abstract class NarrativeNode
    {
        /// <summary>节点类型</summary>
        public abstract NodeKind Kind { get; }
        /// <summary>可选的跳转标签，供 <see cref="NarrativeTarget.Goto"/> 定位（例如 Hub 菜单）</summary>
        public string Label { get; set; }
        /// <summary>进入该节点时的宿主回调</summary>
        public Action OnEnter { get; set; }
        /// <summary>离开该节点时的宿主回调</summary>
        public Action OnExit { get; set; }
    }

    /// <summary>一句对话</summary>
    public sealed class SayNode : NarrativeNode
    {
        /// <inheritdoc/>
        public override NodeKind Kind => NodeKind.Say;
        /// <summary>说话角色</summary>
        public CharacterId Speaker { get; set; }
        /// <summary>表情</summary>
        public ExpressionId Expression { get; set; }
        /// <summary>已本地化的文本（本地化是消费者职责，框架只接收最终字符串）</summary>
        public string Text { get; set; }
        /// <summary>定时设置，<see langword="null"/> 表示普通对话</summary>
        public TimedSettings Timed { get; set; }
    }

    /// <summary>带选项的对话：先播放提示句，打字完成后弹出选项</summary>
    public sealed class ChoiceNode : NarrativeNode
    {
        /// <inheritdoc/>
        public override NodeKind Kind => NodeKind.Choice;
        /// <summary>提示句的说话角色</summary>
        public CharacterId Speaker { get; set; }
        /// <summary>提示句表情</summary>
        public ExpressionId Expression { get; set; }
        /// <summary>提示句文本</summary>
        public string Prompt { get; set; }
        /// <summary>选项列表</summary>
        public List<ChoiceOption> Options { get; set; } = [];
        /// <summary>定时设置，<see langword="null"/> 表示无限时</summary>
        public TimedSettings Timed { get; set; }
        /// <summary>限时结束默认选择的选项 id，<see langword="null"/> 时若超时则随机选择一个可用项</summary>
        public ChoiceId? DefaultChoice { get; set; }
    }

    /// <summary>功能弹窗节点（奖励 / 提示等）</summary>
    public sealed class PopupNode : NarrativeNode
    {
        /// <inheritdoc/>
        public override NodeKind Kind => NodeKind.Popup;
        /// <summary>弹窗载荷</summary>
        public PopupPayload Payload { get; set; }
        /// <summary>是否阻塞：阻塞弹窗在被领取 / 关闭前，场景不会进入完成状态</summary>
        public bool Blocking { get; set; } = true;
    }

    /// <summary>执行一次宿主命令然后继续</summary>
    public sealed class CommandNode : NarrativeNode
    {
        /// <inheritdoc/>
        public override NodeKind Kind => NodeKind.Command;
        /// <summary>命令体</summary>
        public Action Command { get; set; }
    }

    /// <summary>条件跳转节点</summary>
    public sealed class BranchNode : NarrativeNode
    {
        /// <inheritdoc/>
        public override NodeKind Kind => NodeKind.Branch;
        /// <summary>条件判定</summary>
        public Func<bool> Predicate { get; set; }
        /// <summary>条件为真时的跳转</summary>
        public NarrativeTarget IfTrue { get; set; } = NarrativeTarget.Continue;
        /// <summary>条件为假时的跳转</summary>
        public NarrativeTarget IfFalse { get; set; } = NarrativeTarget.Continue;
    }

    /// <summary>等待固定 tick 数后继续</summary>
    public sealed class WaitNode : NarrativeNode
    {
        /// <inheritdoc/>
        public override NodeKind Kind => NodeKind.Wait;
        /// <summary>等待的 tick 数（60 tick = 1 秒）</summary>
        public int Ticks { get; set; }
    }
}
