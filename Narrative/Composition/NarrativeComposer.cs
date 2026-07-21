using InnoVault.Narrative.Core;
using System;
using Terraria.Audio;

namespace InnoVault.Narrative.Composition
{
    /// <summary>
    /// 叙事内容构建器，是内容作者编写场景的<b>唯一推荐入口</b><br/>
    /// 以链式调用把对话、选项、功能弹窗、命令、分支、跳转组织成一张 <see cref="NarrativeGraph"/>，<br/>
    /// 避免旧实现中多套等价构建 API 并存、以及为分支堆叠嵌套类的问题
    /// </summary>
    public sealed class NarrativeComposer
    {
        private readonly NarrativeGraph _graph;
        private readonly string _modName;
        private string _pendingLabel;

        /// <summary>基于一张图创建构建器</summary>
        /// <param name="graph">目标内容图</param>
        public NarrativeComposer(NarrativeGraph graph) : this(graph, null) { }

        /// <summary>基于一张图创建构建器，并按所属 Mod 自动补全未前缀的角色 id</summary>
        /// <param name="graph">目标内容图</param>
        /// <param name="modName">所属模组名；用于补全短名 <see cref="CharacterId"/></param>
        public NarrativeComposer(NarrativeGraph graph, string modName) {
            _graph = graph;
            _modName = modName;
        }

        private CharacterId Resolve(CharacterId speaker) => NarrativeIdScope.Character(_modName, speaker);

        private T AddNode<T>(T node) where T : NarrativeNode {
            if (_pendingLabel != null) {
                node.Label = _pendingLabel;
                _pendingLabel = null;
            }
            _graph.Add(node);
            return node;
        }

        /// <summary>为下一个加入的节点附加跳转标签（用于 Hub 菜单 / 返回主菜单）</summary>
        /// <param name="label">标签名，供 <see cref="NarrativeTarget.Goto"/> 使用</param>
        public NarrativeComposer Label(string label) {
            _pendingLabel = label;
            return this;
        }

        /// <summary>添加一句对话（默认表情）</summary>
        /// <param name="speaker">说话角色</param>
        /// <param name="text">已本地化的台词文本</param>
        /// <param name="onEnter">进入该句时的回调（如换脸）；默认会挡 Skip，除非 <paramref name="allowSkipThrough"/></param>
        /// <param name="onExit">离开该句时的回调（如发奖 / 接任务）</param>
        /// <param name="allowSkipThrough">为 true 时，即使有 onEnter/onExit 也可被 Skip 飞过（仅装饰性回调）</param>
        public NarrativeComposer Say(
            CharacterId speaker,
            string text,
            Action onEnter = null,
            Action onExit = null,
            bool allowSkipThrough = false)
            => Say(speaker, ExpressionId.Default, text, onEnter, onExit, allowSkipThrough);

        /// <summary>添加一句对话（指定表情）</summary>
        /// <param name="speaker">说话角色</param>
        /// <param name="expression">表情 id</param>
        /// <param name="text">已本地化的台词文本</param>
        /// <param name="onEnter">进入该句时的回调</param>
        /// <param name="onExit">离开该句时的回调</param>
        /// <param name="allowSkipThrough">为 true 时，即使有 onEnter/onExit 也可被 Skip 飞过（仅装饰性回调）</param>
        public NarrativeComposer Say(
            CharacterId speaker,
            ExpressionId expression,
            string text,
            Action onEnter = null,
            Action onExit = null,
            bool allowSkipThrough = false) {
            AddNode(new SayNode {
                Speaker = Resolve(speaker),
                Expression = expression,
                Text = text,
                OnEnter = onEnter,
                OnExit = onExit,
                AllowSkipThrough = allowSkipThrough,
            });
            return this;
        }

        /// <summary>添加一句带配音的对话（默认表情；有配音时默认静音打字机音）</summary>
        /// <param name="speaker">说话角色</param>
        /// <param name="text">已本地化的台词文本</param>
        /// <param name="voice">本句配音，由会话统一播停</param>
        /// <param name="onEnter">进入该句时的回调</param>
        /// <param name="onExit">离开该句时的回调</param>
        /// <param name="allowSkipThrough">为 true 时，即使有 onEnter/onExit 也可被 Skip 飞过（仅装饰性回调）</param>
        public NarrativeComposer Say(
            CharacterId speaker,
            string text,
            SoundStyle voice,
            Action onEnter = null,
            Action onExit = null,
            bool allowSkipThrough = false)
            => Say(speaker, ExpressionId.Default, text, voice, muteTypingSound: true, onEnter, onExit, allowSkipThrough);

        /// <summary>添加一句带配音的对话（指定表情；有配音时默认静音打字机音）</summary>
        /// <param name="speaker">说话角色</param>
        /// <param name="expression">表情 id</param>
        /// <param name="text">已本地化的台词文本</param>
        /// <param name="voice">本句配音，由会话统一播停</param>
        /// <param name="onEnter">进入该句时的回调</param>
        /// <param name="onExit">离开该句时的回调</param>
        /// <param name="allowSkipThrough">为 true 时，即使有 onEnter/onExit 也可被 Skip 飞过（仅装饰性回调）</param>
        public NarrativeComposer Say(
            CharacterId speaker,
            ExpressionId expression,
            string text,
            SoundStyle voice,
            Action onEnter = null,
            Action onExit = null,
            bool allowSkipThrough = false)
            => Say(speaker, expression, text, voice, muteTypingSound: true, onEnter, onExit, allowSkipThrough);

        /// <summary>添加一句带配音的对话（可控制是否静音打字机音）</summary>
        /// <param name="speaker">说话角色</param>
        /// <param name="expression">表情 id</param>
        /// <param name="text">已本地化的台词文本</param>
        /// <param name="voice">本句配音，由会话统一播停</param>
        /// <param name="muteTypingSound">是否静音打字机音</param>
        /// <param name="onEnter">进入该句时的回调</param>
        /// <param name="onExit">离开该句时的回调</param>
        /// <param name="allowSkipThrough">为 true 时，即使有 onEnter/onExit 也可被 Skip 飞过（仅装饰性回调）</param>
        public NarrativeComposer Say(
            CharacterId speaker,
            ExpressionId expression,
            string text,
            SoundStyle voice,
            bool muteTypingSound,
            Action onEnter = null,
            Action onExit = null,
            bool allowSkipThrough = false) {
            AddNode(new SayNode {
                Speaker = Resolve(speaker),
                Expression = expression,
                Text = text,
                Voice = voice,
                MuteTypingSound = muteTypingSound,
                OnEnter = onEnter,
                OnExit = onExit,
                AllowSkipThrough = allowSkipThrough,
            });
            return this;
        }

        /// <summary>添加一句限时对话（到时自动推进）</summary>
        /// <param name="speaker">说话角色</param>
        /// <param name="text">已本地化的台词文本</param>
        /// <param name="seconds">限时秒数</param>
        /// <param name="onEnter">进入该句时的回调</param>
        /// <param name="onExit">离开该句时的回调</param>
        /// <param name="allowSkipThrough">为 true 时，即使有 onEnter/onExit 也可被 Skip 飞过（仅装饰性回调）</param>
        public NarrativeComposer SayTimed(
            CharacterId speaker,
            string text,
            float seconds,
            Action onEnter = null,
            Action onExit = null,
            bool allowSkipThrough = false) {
            AddNode(new SayNode {
                Speaker = Resolve(speaker),
                Text = text,
                Timed = TimedSettings.Of(seconds),
                OnEnter = onEnter,
                OnExit = onExit,
                AllowSkipThrough = allowSkipThrough,
            });
            return this;
        }

        /// <summary>添加一句限时对话（默认表情，完整定时配置）</summary>
        /// <param name="speaker">说话角色</param>
        /// <param name="text">已本地化的台词文本</param>
        /// <param name="timed">限时配置</param>
        /// <param name="onEnter">进入该句时的回调</param>
        /// <param name="onExit">离开该句时的回调</param>
        /// <param name="allowSkipThrough">为 true 时，即使有 onEnter/onExit 也可被 Skip 飞过（仅装饰性回调）</param>
        public NarrativeComposer SayTimed(
            CharacterId speaker,
            string text,
            TimedSettings timed,
            Action onEnter = null,
            Action onExit = null,
            bool allowSkipThrough = false)
            => SayTimed(speaker, ExpressionId.Default, text, timed, onEnter, onExit, allowSkipThrough);

        /// <summary>添加一句限时对话（指定表情，完整定时配置）</summary>
        /// <param name="speaker">说话角色</param>
        /// <param name="expression">表情 id</param>
        /// <param name="text">已本地化的台词文本</param>
        /// <param name="timed">限时配置</param>
        /// <param name="onEnter">进入该句时的回调</param>
        /// <param name="onExit">离开该句时的回调</param>
        /// <param name="allowSkipThrough">为 true 时，即使有 onEnter/onExit 也可被 Skip 飞过（仅装饰性回调）</param>
        public NarrativeComposer SayTimed(
            CharacterId speaker,
            ExpressionId expression,
            string text,
            TimedSettings timed,
            Action onEnter = null,
            Action onExit = null,
            bool allowSkipThrough = false) {
            AddNode(new SayNode {
                Speaker = Resolve(speaker),
                Expression = expression,
                Text = text,
                Timed = timed,
                OnEnter = onEnter,
                OnExit = onExit,
                AllowSkipThrough = allowSkipThrough,
            });
            return this;
        }

        /// <summary>添加一句带配音的限时对话</summary>
        /// <param name="speaker">说话角色</param>
        /// <param name="text">已本地化的台词文本</param>
        /// <param name="seconds">限时秒数</param>
        /// <param name="voice">本句配音，由会话统一播停</param>
        /// <param name="onEnter">进入该句时的回调</param>
        /// <param name="onExit">离开该句时的回调</param>
        /// <param name="allowSkipThrough">为 true 时，即使有 onEnter/onExit 也可被 Skip 飞过（仅装饰性回调）</param>
        public NarrativeComposer SayTimed(
            CharacterId speaker,
            string text,
            float seconds,
            SoundStyle voice,
            Action onEnter = null,
            Action onExit = null,
            bool allowSkipThrough = false)
            => SayTimed(speaker, ExpressionId.Default, text, TimedSettings.Of(seconds), voice, muteTypingSound: true, onEnter, onExit, allowSkipThrough);

        /// <summary>添加一句带配音的限时对话（完整定时配置）</summary>
        /// <param name="speaker">说话角色</param>
        /// <param name="expression">表情 id</param>
        /// <param name="text">已本地化的台词文本</param>
        /// <param name="timed">限时配置</param>
        /// <param name="voice">本句配音，由会话统一播停</param>
        /// <param name="muteTypingSound">是否静音打字机音</param>
        /// <param name="onEnter">进入该句时的回调</param>
        /// <param name="onExit">离开该句时的回调</param>
        /// <param name="allowSkipThrough">为 true 时，即使有 onEnter/onExit 也可被 Skip 飞过（仅装饰性回调）</param>
        public NarrativeComposer SayTimed(
            CharacterId speaker,
            ExpressionId expression,
            string text,
            TimedSettings timed,
            SoundStyle voice,
            bool muteTypingSound = true,
            Action onEnter = null,
            Action onExit = null,
            bool allowSkipThrough = false) {
            AddNode(new SayNode {
                Speaker = Resolve(speaker),
                Expression = expression,
                Text = text,
                Timed = timed,
                Voice = voice,
                MuteTypingSound = muteTypingSound,
                OnEnter = onEnter,
                OnExit = onExit,
                AllowSkipThrough = allowSkipThrough,
            });
            return this;
        }

        /// <summary>添加一个带选项的对话</summary>
        /// <param name="speaker">提示句说话角色</param>
        /// <param name="prompt">提示句文本</param>
        /// <param name="build">选项构建委托</param>
        public NarrativeComposer Choice(CharacterId speaker, string prompt, Action<ChoiceBuilder> build)
            => Choice(speaker, ExpressionId.Default, prompt, build);

        /// <summary>添加一个带选项的对话（指定表情）</summary>
        /// <param name="speaker">提示句说话角色</param>
        /// <param name="expression">提示句表情</param>
        /// <param name="prompt">提示句文本</param>
        /// <param name="build">选项构建委托</param>
        public NarrativeComposer Choice(CharacterId speaker, ExpressionId expression, string prompt, Action<ChoiceBuilder> build) {
            var node = AddNode(new ChoiceNode { Speaker = Resolve(speaker), Expression = expression, Prompt = prompt });
            build?.Invoke(new ChoiceBuilder(node));
            return this;
        }

        /// <summary>添加一个功能弹窗节点</summary>
        /// <param name="payload">弹窗载荷</param>
        /// <param name="blocking">是否阻塞后续节点</param>
        /// <param name="onEnter">进入该节点时的回调</param>
        public NarrativeComposer Popup(PopupPayload payload, bool blocking = true, Action onEnter = null) {
            AddNode(new PopupNode { Payload = payload, Blocking = blocking, OnEnter = onEnter });
            return this;
        }

        /// <summary>添加一个物品奖励弹窗（便捷写法）</summary>
        /// <param name="itemType">物品类型 id</param>
        /// <param name="stack">数量</param>
        /// <param name="title">弹窗标题；<see langword="null"/> 用默认</param>
        /// <param name="blocking">是否阻塞后续节点</param>
        public NarrativeComposer Reward(int itemType, int stack = 1, string title = null, bool blocking = true)
            => Popup(Popups.Reward(itemType, stack, title), blocking);

        /// <summary>
        /// 在该句台词开始时弹出物品奖励，并展示同一说话者的对话<br/>
        /// 等价于 <c>Popup(Reward(...), blocking); Say(...)</c>（旧 ADV <c>AddReward</c> 语义）
        /// </summary>
        /// <param name="speaker">说话角色</param>
        /// <param name="text">已本地化的台词文本</param>
        /// <param name="itemType">奖励物品类型 id</param>
        /// <param name="stack">数量</param>
        /// <param name="title">弹窗标题</param>
        /// <param name="blocking">奖励弹窗是否阻塞对话</param>
        /// <param name="anchorGap">锚定间距；与 <paramref name="anchorYOffset"/> 同时为 0 时不改锚定</param>
        /// <param name="anchorYOffset">锚定纵向偏移</param>
        /// <param name="onEnter">进入台词句时的回调</param>
        /// <param name="onExit">离开台词句时的回调</param>
        /// <param name="allowSkipThrough">为 true 时，即使有 onEnter/onExit 也可被 Skip 飞过（仅装饰性回调）</param>
        public NarrativeComposer SayReward(
            CharacterId speaker,
            string text,
            int itemType,
            int stack = 1,
            string title = null,
            bool blocking = false,
            float anchorGap = 0f,
            float anchorYOffset = 0f,
            Action onEnter = null,
            Action onExit = null,
            bool allowSkipThrough = false)
            => SayReward(speaker, ExpressionId.Default, text, itemType, stack, title, blocking, anchorGap, anchorYOffset, onEnter, onExit, allowSkipThrough);

        /// <summary>在该句台词（指定表情）开始时弹出物品奖励，并展示对话</summary>
        /// <param name="speaker">说话角色</param>
        /// <param name="expression">表情 id</param>
        /// <param name="text">已本地化的台词文本</param>
        /// <param name="itemType">奖励物品类型 id</param>
        /// <param name="stack">数量</param>
        /// <param name="title">弹窗标题</param>
        /// <param name="blocking">奖励弹窗是否阻塞对话</param>
        /// <param name="anchorGap">锚定间距；与 <paramref name="anchorYOffset"/> 同时为 0 时不改锚定</param>
        /// <param name="anchorYOffset">锚定纵向偏移</param>
        /// <param name="onEnter">进入台词句时的回调</param>
        /// <param name="onExit">离开台词句时的回调</param>
        /// <param name="allowSkipThrough">为 true 时，即使有 onEnter/onExit 也可被 Skip 飞过（仅装饰性回调）</param>
        public NarrativeComposer SayReward(
            CharacterId speaker,
            ExpressionId expression,
            string text,
            int itemType,
            int stack = 1,
            string title = null,
            bool blocking = false,
            float anchorGap = 0f,
            float anchorYOffset = 0f,
            Action onEnter = null,
            Action onExit = null,
            bool allowSkipThrough = false) {
            RewardPayload payload = Popups.Reward(itemType, stack, title);
            if (anchorGap > 0f || anchorYOffset != 0f) {
                payload.Anchored(anchorGap > 0f ? anchorGap : 70f, anchorYOffset);
            }

            Popup(payload, blocking);
            return Say(speaker, expression, text, onEnter, onExit, allowSkipThrough);
        }

        /// <summary>添加一个执行宿主命令的节点</summary>
        /// <param name="command">命令体</param>
        public NarrativeComposer Command(Action command) {
            AddNode(new CommandNode { Command = command });
            return this;
        }

        /// <summary>等待固定 tick 数</summary>
        /// <param name="ticks">等待 tick（60 tick ≈ 1 秒）</param>
        public NarrativeComposer Wait(int ticks) {
            AddNode(new WaitNode { Ticks = ticks });
            return this;
        }

        /// <summary>等待固定秒数</summary>
        /// <param name="seconds">秒数</param>
        public NarrativeComposer WaitSeconds(float seconds)
            => Wait((int)(seconds * 60f));

        /// <summary>添加一个运行期条件跳转</summary>
        /// <param name="predicate">条件；为真走 <paramref name="ifTrue"/></param>
        /// <param name="ifTrue">条件为真时的跳转</param>
        /// <param name="ifFalse">条件为假时的跳转</param>
        public NarrativeComposer Branch(Func<bool> predicate, NarrativeTarget ifTrue, NarrativeTarget ifFalse) {
            AddNode(new BranchNode { Predicate = predicate, IfTrue = ifTrue, IfFalse = ifFalse });
            return this;
        }

        /// <summary>无条件跳转到图内标签</summary>
        /// <param name="label">目标标签</param>
        public NarrativeComposer Goto(string label) {
            AddNode(new BranchNode { Predicate = null, IfTrue = NarrativeTarget.Goto(label) });
            return this;
        }

        /// <summary>显式结束当前场景</summary>
        public NarrativeComposer End() {
            AddNode(new BranchNode { Predicate = null, IfTrue = NarrativeTarget.End });
            return this;
        }

        /// <summary>
        /// 构建期条件块：在 <paramref name="condition"/> 为真时插入 <paramref name="then"/> 的节点，<br/>
        /// 否则插入 <paramref name="otherwise"/>。用于替代内容脚本里的 <c>if (hasX) {...}</c> 写法
        /// </summary>
        /// <param name="condition">构建期条件</param>
        /// <param name="then">条件为真时插入的内容</param>
        /// <param name="otherwise">条件为假时插入的内容；可为 <see langword="null"/></param>
        public NarrativeComposer When(Func<bool> condition, Action<NarrativeComposer> then, Action<NarrativeComposer> otherwise = null) {
            if (condition != null && condition()) {
                then?.Invoke(this);
            }
            else {
                otherwise?.Invoke(this);
            }
            return this;
        }
    }
}
