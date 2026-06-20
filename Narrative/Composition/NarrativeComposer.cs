using System;

namespace InnoVault.Narrative
{
    /// <summary>
    /// 叙事内容构建器，是内容作者编写场景的<b>唯一推荐入口</b>。<br/>
    /// 以链式调用把对话、选项、功能弹窗、命令、分支、跳转组织成一张 <see cref="NarrativeGraph"/>，<br/>
    /// 避免旧实现中多套等价构建 API 并存、以及为分支堆叠嵌套类的问题
    /// </summary>
    public sealed class NarrativeComposer
    {
        private readonly NarrativeGraph _graph;
        private string _pendingLabel;

        /// <summary>基于一张图创建构建器</summary>
        public NarrativeComposer(NarrativeGraph graph)
        {
            _graph = graph;
        }

        private T AddNode<T>(T node) where T : NarrativeNode
        {
            if (_pendingLabel != null)
            {
                node.Label = _pendingLabel;
                _pendingLabel = null;
            }
            _graph.Add(node);
            return node;
        }

        /// <summary>为下一个加入的节点附加跳转标签（用于 Hub 菜单 / 返回主菜单）</summary>
        public NarrativeComposer Label(string label)
        {
            _pendingLabel = label;
            return this;
        }

        /// <summary>添加一句对话（默认表情）</summary>
        public NarrativeComposer Say(CharacterId speaker, string text, Action onEnter = null, Action onExit = null)
            => Say(speaker, ExpressionId.Default, text, onEnter, onExit);

        /// <summary>添加一句对话（指定表情）</summary>
        public NarrativeComposer Say(CharacterId speaker, ExpressionId expression, string text, Action onEnter = null, Action onExit = null)
        {
            AddNode(new SayNode { Speaker = speaker, Expression = expression, Text = text, OnEnter = onEnter, OnExit = onExit });
            return this;
        }

        /// <summary>添加一句限时对话（到时自动推进）</summary>
        public NarrativeComposer SayTimed(CharacterId speaker, string text, float seconds, Action onEnter = null, Action onExit = null)
        {
            AddNode(new SayNode { Speaker = speaker, Text = text, Timed = TimedSettings.Of(seconds), OnEnter = onEnter, OnExit = onExit });
            return this;
        }

        /// <summary>添加一个带选项的对话</summary>
        public NarrativeComposer Choice(CharacterId speaker, string prompt, Action<ChoiceBuilder> build)
            => Choice(speaker, ExpressionId.Default, prompt, build);

        /// <summary>添加一个带选项的对话（指定表情）</summary>
        public NarrativeComposer Choice(CharacterId speaker, ExpressionId expression, string prompt, Action<ChoiceBuilder> build)
        {
            var node = AddNode(new ChoiceNode { Speaker = speaker, Expression = expression, Prompt = prompt });
            build?.Invoke(new ChoiceBuilder(node));
            return this;
        }

        /// <summary>添加一个功能弹窗节点</summary>
        public NarrativeComposer Popup(PopupPayload payload, bool blocking = true, Action onEnter = null)
        {
            AddNode(new PopupNode { Payload = payload, Blocking = blocking, OnEnter = onEnter });
            return this;
        }

        /// <summary>添加一个物品奖励弹窗（便捷写法）</summary>
        public NarrativeComposer Reward(int itemType, int stack = 1, string title = null, bool blocking = true)
            => Popup(Popups.Reward(itemType, stack, title), blocking);

        /// <summary>添加一个执行宿主命令的节点</summary>
        public NarrativeComposer Command(Action command)
        {
            AddNode(new CommandNode { Command = command });
            return this;
        }

        /// <summary>等待固定 tick 数</summary>
        public NarrativeComposer Wait(int ticks)
        {
            AddNode(new WaitNode { Ticks = ticks });
            return this;
        }

        /// <summary>等待固定秒数</summary>
        public NarrativeComposer WaitSeconds(float seconds)
            => Wait((int)(seconds * 60f));

        /// <summary>添加一个运行期条件跳转</summary>
        public NarrativeComposer Branch(Func<bool> predicate, NarrativeTarget ifTrue, NarrativeTarget ifFalse)
        {
            AddNode(new BranchNode { Predicate = predicate, IfTrue = ifTrue, IfFalse = ifFalse });
            return this;
        }

        /// <summary>无条件跳转到图内标签</summary>
        public NarrativeComposer Goto(string label)
        {
            AddNode(new BranchNode { Predicate = null, IfTrue = NarrativeTarget.Goto(label) });
            return this;
        }

        /// <summary>显式结束当前场景</summary>
        public NarrativeComposer End()
        {
            AddNode(new BranchNode { Predicate = null, IfTrue = NarrativeTarget.End });
            return this;
        }

        /// <summary>
        /// 构建期条件块：在 <paramref name="condition"/> 为真时插入 <paramref name="then"/> 的节点，<br/>
        /// 否则插入 <paramref name="otherwise"/>。用于替代内容脚本里的 <c>if (hasX) {...}</c> 写法
        /// </summary>
        public NarrativeComposer When(Func<bool> condition, Action<NarrativeComposer> then, Action<NarrativeComposer> otherwise = null)
        {
            if (condition != null && condition())
            {
                then?.Invoke(this);
            }
            else
            {
                otherwise?.Invoke(this);
            }
            return this;
        }
    }
}
