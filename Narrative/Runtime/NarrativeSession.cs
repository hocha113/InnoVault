using InnoVault.Narrative.Core;
using InnoVault.Narrative.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;

namespace InnoVault.Narrative.Runtime
{
    /// <summary>
    /// 一次叙事播放的运行期真相。持有当前节点、播放状态、等待选择 / 等待弹窗、完成状态等，<br/>
    /// 所有流程推进都集中在这里（单一职责的"大脑"），UI 控件只读取它并回传输入意图。<br/>
    /// 这从根本上避免了旧实现中"选择后父对话段未收尾""换肤丢状态""启动即完成"等问题
    /// </summary>
    public sealed class NarrativeSession
    {
        /// <summary>来源场景</summary>
        public NarrativeScenario Scenario { get; }
        /// <summary>场景 Key</summary>
        public string Key { get; }
        /// <summary>本次播放使用的内容图（每次启动重建）</summary>
        public NarrativeGraph Graph { get; }
        /// <summary>样式 id</summary>
        public StyleId Style { get; }
        /// <summary>播放参数</summary>
        public DialoguePlaybackOptions Options { get; } = new();

        /// <summary>当前阶段</summary>
        public NarrativeSessionPhase Phase { get; private set; } = NarrativeSessionPhase.Inactive;
        /// <summary>当前 / 最近展示的一行对话状态（在弹窗 / 等待节点期间持续展示）</summary>
        public LinePresentation Line { get; } = new();
        /// <summary>对话面板当前是否应显示</summary>
        public bool DialogueVisible { get; private set; }
        /// <summary>等待选择时的选项节点</summary>
        public ChoiceNode PendingChoice { get; private set; }
        /// <summary>当前活动的弹窗载荷（阻塞或非阻塞）</summary>
        public PopupPayload ActivePopup { get; private set; }
        /// <summary>最近一次弹窗的解析结果</summary>
        public PopupResolution LastPopupResult { get; private set; } = PopupResolution.Pending;
        /// <summary>若本场景结束时请求衔接另一个场景，则为其 Key</summary>
        public string RequestedScenario { get; private set; }

        /// <summary>场景完成回调（由启动者设置，用于写完成标记 / 触发后续）</summary>
        public Action OnCompleted { get; set; }
        /// <summary>场景中止回调</summary>
        public Action OnAborted { get; set; }

        //—— 运行期内部状态 ——
        private int _currentIndex = -1;
        private float _autoTimer;
        private float _waitRemaining;
        private float _choiceTimedTotal;
        private float _choiceTimedRemaining;
        private float _popupTimer;
        private bool _popupBlocking;
        private int _settleGuard;

        //—— UI 输入意图（由视图设置，运行时消费）——
        private bool _advanceRequested;
        private bool _skipRequested;
        private int _selectedChoiceIndex = -1;
        private int _popupIntent; //0 无 1 领取 2 关闭
        private bool _toggleAuto;
        private bool _toggleFast;
        /// <summary>选项悬停下标（视图写入，仅供皮肤高亮，不影响逻辑）</summary>
        public int ChoiceHoverIndex { get; set; } = -1;

        private static Player LocalPlayer => Main.LocalPlayer;

        /// <summary>创建一次会话</summary>
        public NarrativeSession(NarrativeScenario scenario, NarrativeGraph graph, StyleId style) {
            Scenario = scenario;
            Key = scenario?.Key ?? string.Empty;
            Graph = graph;
            Style = style;
        }

        #region 视图读取属性

        /// <summary>是否处于选项等待</summary>
        public bool IsAwaitingChoice => Phase == NarrativeSessionPhase.AwaitingChoice;
        /// <summary>当前选项列表</summary>
        public IReadOnlyList<ChoiceOption> ChoiceOptions => PendingChoice?.Options;
        /// <summary>选项是否限时</summary>
        public bool ChoiceIsTimed => PendingChoice?.Timed != null;
        /// <summary>选项限时剩余比例 0~1</summary>
        public float ChoiceTimedProgress => _choiceTimedTotal <= 0f ? 0f : Math.Clamp(_choiceTimedRemaining / _choiceTimedTotal, 0f, 1f);
        /// <summary>是否已结束（完成或中止）</summary>
        public bool IsFinished => Phase is NarrativeSessionPhase.Completed or NarrativeSessionPhase.Aborted;

        #endregion

        #region 视图输入意图入口

        /// <summary>请求推进（点击对话框：未打完则补全，已打完则进入下一节点）</summary>
        public void RequestAdvance() => _advanceRequested = true;
        /// <summary>请求补全当前行打字</summary>
        public void RequestSkipLine() => _skipRequested = true;
        /// <summary>选择某个选项（按下标）</summary>
        public void SelectChoice(int index) => _selectedChoiceIndex = index;
        /// <summary>切换自动播放</summary>
        public void ToggleAuto() => _toggleAuto = true;
        /// <summary>切换快进</summary>
        public void ToggleFast() => _toggleFast = true;
        /// <summary>领取 / 确认当前弹窗</summary>
        public void ClaimPopup() => _popupIntent = 1;
        /// <summary>关闭 / 取消当前弹窗</summary>
        public void DismissPopup() => _popupIntent = 2;

        #endregion

        /// <summary>启动会话，进入首个节点</summary>
        public void Start() {
            _currentIndex = -1;
            Phase = NarrativeSessionPhase.Playing;
            if (Graph == null || Graph.Count == 0) {
                Complete();
                return;
            }
            Transition(0);
        }

        /// <summary>每帧推进（由 <see cref="NarrativeRunner"/> 在 UpdateUI 驱动）</summary>
        public void Tick(float frames) {
            if (IsFinished) {
                return;
            }
            _settleGuard = 0;

            if (_toggleAuto) {
                _toggleAuto = false;
                Options.AutoMode = !Options.AutoMode;
                Options.FastMode = false;
                _autoTimer = 0f;
            }
            if (_toggleFast) {
                _toggleFast = false;
                Options.FastMode = !Options.FastMode;
                Options.AutoMode = false;
                _autoTimer = 0f;
            }

            if (ActivePopup != null) {
                TickActivePopup(frames);
            }
            if (IsFinished) {
                return;
            }

            switch (Phase) {
                case NarrativeSessionPhase.Playing:
                    TickPlaying(frames);
                    break;
                case NarrativeSessionPhase.AwaitingChoice:
                    TickChoice(frames);
                    break;
            }
        }

        private NarrativeNode CurrentNode => Graph?.Get(_currentIndex);

        private void Transition(int nextIndex) {
            CurrentNode?.OnExit?.Invoke();
            _currentIndex = nextIndex;
            EnterCurrent();
        }

        private void EnterCurrent() {
            if (++_settleGuard > 5000) {
                VaultMod.Instance.Logger.Warn($"Narrative scenario '{Key}' aborted: too many instant transitions (possible Goto loop).");
                Abort();
                return;
            }

            NarrativeNode node = CurrentNode;
            if (node == null) {
                Complete();
                return;
            }

            node.OnEnter?.Invoke();

            switch (node) {
                case SayNode say:
                    BeginLine(say.Speaker, say.Expression, say.Text, say.Timed);
                    break;
                case ChoiceNode choice:
                    BeginLine(choice.Speaker, choice.Expression, choice.Prompt, choice.Timed);
                    break;
                case WaitNode wait:
                    _waitRemaining = wait.Ticks;
                    Phase = NarrativeSessionPhase.Playing;
                    _settleGuard = 0;
                    break;
                case PopupNode popup:
                    if (popup.Payload == null) {
                        Transition(_currentIndex + 1);
                        return;
                    }
                    ActivePopup = popup.Payload;
                    _popupBlocking = popup.Blocking;
                    _popupTimer = 0f;
                    LastPopupResult = PopupResolution.Pending;
                    if (popup.Blocking) {
                        Phase = NarrativeSessionPhase.AwaitingPopup;
                        _settleGuard = 0;
                    }
                    else {
                        Transition(_currentIndex + 1);
                    }
                    break;
                case CommandNode command:
                    command.Command?.Invoke();
                    Transition(_currentIndex + 1);
                    break;
                case BranchNode branch:
                    bool result = branch.Predicate == null || branch.Predicate();
                    GoToTarget(result ? branch.IfTrue : branch.IfFalse);
                    break;
            }
        }

        private void BeginLine(CharacterId speaker, ExpressionId expression, string text, TimedSettings timed) {
            Line.Begin(speaker, expression, text, timed);
            DialogueVisible = true;
            Phase = NarrativeSessionPhase.Playing;
            _autoTimer = 0f;
            _settleGuard = 0;
        }

        private void GoToTarget(NarrativeTarget target) {
            target ??= NarrativeTarget.Continue;
            switch (target.Kind) {
                case NarrativeTarget.TargetKind.Continue:
                    Transition(_currentIndex + 1);
                    break;
                case NarrativeTarget.TargetKind.GotoLabel:
                    if (Graph.TryGetLabelIndex(target.Label, out int idx)) {
                        Transition(idx);
                    }
                    else {
                        VaultMod.Instance.Logger.Warn($"Narrative scenario '{Key}' goto unknown label '{target.Label}', completing.");
                        CurrentNode?.OnExit?.Invoke();
                        Complete();
                    }
                    break;
                case NarrativeTarget.TargetKind.GotoScenario:
                    CurrentNode?.OnExit?.Invoke();
                    RequestedScenario = target.ScenarioKey;
                    Complete();
                    break;
                case NarrativeTarget.TargetKind.End:
                    CurrentNode?.OnExit?.Invoke();
                    Complete();
                    break;
            }
        }

        private void TickPlaying(float frames) {
            //等待节点
            if (_waitRemaining > 0f) {
                _waitRemaining -= frames;
                if (_waitRemaining <= 0f) {
                    _waitRemaining = 0f;
                    Transition(_currentIndex + 1);
                }
                return;
            }

            LinePresentation line = Line;

            //空文本节点直接推进
            if (!line.HasContent && CurrentNode is not ChoiceNode) {
                Transition(_currentIndex + 1);
                return;
            }

            //打字机推进
            if (line.LayoutReady && !line.Finished) {
                float perChar = Options.FastMode ? Options.FastTicksPerChar : Options.TicksPerChar;
                if (perChar <= 0f) {
                    perChar = 0.0001f;
                }
                line.VisibleChars += frames / perChar;
                if (line.VisibleChars > line.TotalChars) {
                    line.VisibleChars = line.TotalChars;
                }
            }

            //点击 / 跳过：未打完则补全
            if (_advanceRequested && line.LayoutReady && !line.Finished) {
                _advanceRequested = false;
                line.RevealAll();
            }
            if (_skipRequested) {
                _skipRequested = false;
                if (line.LayoutReady && !line.Finished) {
                    line.RevealAll();
                }
            }

            if (!line.Finished) {
                return;
            }

            //已打完字
            if (CurrentNode is ChoiceNode choice) {
                OpenChoices(choice);
                return;
            }

            if (line.IsTimed) {
                line.TimedRemainingTicks -= frames;
                if (line.AllowManualAdvance && _advanceRequested) {
                    _advanceRequested = false;
                    Transition(_currentIndex + 1);
                    return;
                }
                if (line.TimedRemainingTicks <= 0f) {
                    (CurrentNode as SayNode)?.Timed?.OnExpired?.Invoke();
                    Transition(_currentIndex + 1);
                }
                return;
            }

            if (_advanceRequested) {
                _advanceRequested = false;
                Transition(_currentIndex + 1);
                return;
            }

            if (Options.AutoMode || Options.FastMode) {
                _autoTimer += frames;
                float delay = Options.FastMode ? Options.FastAutoAdvanceDelay : Options.GetAutoDelay(line.TotalChars);
                if (_autoTimer >= delay) {
                    _autoTimer = 0f;
                    Transition(_currentIndex + 1);
                }
            }
            else {
                _autoTimer = 0f;
            }
        }

        private void OpenChoices(ChoiceNode choice) {
            PendingChoice = choice;
            Phase = NarrativeSessionPhase.AwaitingChoice;
            _selectedChoiceIndex = -1;
            ChoiceHoverIndex = -1;
            _autoTimer = 0f;
            if (choice.Timed != null) {
                _choiceTimedTotal = choice.Timed.Seconds * 60f;
                _choiceTimedRemaining = _choiceTimedTotal;
            }
            else {
                _choiceTimedTotal = 0f;
                _choiceTimedRemaining = 0f;
            }
        }

        private void TickChoice(float frames) {
            ChoiceNode choice = PendingChoice;
            if (choice == null) {
                Phase = NarrativeSessionPhase.Playing;
                return;
            }

            if (choice.Timed != null) {
                _choiceTimedRemaining -= frames;
                if (_choiceTimedRemaining <= 0f) {
                    choice.Timed.OnExpired?.Invoke();
                    ResolveTimeout(choice);
                    return;
                }
            }

            if (_selectedChoiceIndex >= 0) {
                int index = _selectedChoiceIndex;
                _selectedChoiceIndex = -1;
                if (index < choice.Options.Count) {
                    ChoiceOption option = choice.Options[index];
                    if (option.IsEnabled) {
                        ResolveChoice(option);
                    }
                }
            }
        }

        private void ResolveTimeout(ChoiceNode choice) {
            ChoiceOption option = null;
            if (choice.DefaultChoice.HasValue) {
                option = choice.Options.FirstOrDefault(o => o.Id.Equals(choice.DefaultChoice.Value) && o.IsEnabled);
            }
            if (option == null) {
                List<ChoiceOption> enabled = choice.Options.Where(o => o.IsEnabled).ToList();
                if (enabled.Count > 0) {
                    option = enabled[Main.rand.Next(enabled.Count)];
                }
            }
            if (option != null) {
                ResolveChoice(option);
            }
            else {
                PendingChoice = null;
                GoToTarget(NarrativeTarget.Continue);
            }
        }

        private void ResolveChoice(ChoiceOption option) {
            //只解析一次：解析后离开等待选择阶段，杜绝重复弹出
            if (Phase != NarrativeSessionPhase.AwaitingChoice) {
                return;
            }
            PendingChoice = null;
            NarrativeServices.Progress?.SetChoice(Key, option.Id.Value);
            option.OnSelect?.Invoke();
            GoToTarget(option.Target ?? NarrativeTarget.Continue);
        }

        private void TickActivePopup(float frames) {
            //非必领弹窗的自动保持
            if (_popupIntent == 0 && !ActivePopup.RequireClaim && ActivePopup.AutoHoldSeconds >= 0f) {
                _popupTimer += frames;
                if (_popupTimer >= ActivePopup.AutoHoldSeconds * 60f) {
                    _popupIntent = 1;
                }
            }

            if (_popupIntent == 0) {
                return;
            }

            int intent = _popupIntent;
            _popupIntent = 0;
            PopupPayload payload = ActivePopup;
            bool wasBlocking = _popupBlocking;

            if (intent == 1) {
                payload.OnClaimed(LocalPlayer);
                LastPopupResult = PopupResolution.Claimed;
            }
            else {
                payload.OnDismissed(LocalPlayer);
                LastPopupResult = PopupResolution.Dismissed;
            }

            ActivePopup = null;
            if (wasBlocking) {
                Phase = NarrativeSessionPhase.Playing;
                Transition(_currentIndex + 1);
            }
        }

        private void Complete() {
            Phase = NarrativeSessionPhase.Completed;
            PendingChoice = null;
            DialogueVisible = false;
        }

        /// <summary>中止会话（世界切换 / 强制关闭），不会写入完成标记</summary>
        public void Abort() {
            if (Phase == NarrativeSessionPhase.Completed) {
                return;
            }
            Phase = NarrativeSessionPhase.Aborted;
            PendingChoice = null;
            ActivePopup = null;
            DialogueVisible = false;
        }
    }
}
