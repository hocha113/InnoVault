using InnoVault.Narrative.Core;
using InnoVault.Narrative.Services;
using InnoVault.Narrative.Styling;
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
        private const int PopupIntentNone = 0;
        private const int PopupIntentClaim = 1;
        private const int PopupIntentDismiss = 2;
        private const int PopupIntentTimeout = 3;

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
        private bool _completionPending;
        private bool _completionDeferred;

        //—— UI 输入意图（由视图设置，运行时消费）——
        private bool _advanceRequested;
        private bool _skipRequested;
        private bool _skipToNextStopRequested;
        private int _selectedChoiceIndex = -1;
        private int _popupIntent;
        private bool _toggleAuto;
        private bool _toggleFast;
        private int _lastTypedSoundChar;
        /// <summary>选项悬停下标（视图写入，仅供皮肤高亮，不影响逻辑）</summary>
        public int ChoiceHoverIndex { get; set; } = -1;

        /// <summary>由视图注入：为 true 时阻止推进到下一句</summary>
        public Func<bool> BlocksAdvance { get; set; }

        /// <summary>由视图注入：为 true 时推迟场景完成（如全身立绘 burn-out）</summary>
        public Func<bool> BlocksCompletion { get; set; }

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
        /// <summary>
        /// 请求跳到下一个停顿点。普通对话会被补全并跳过；遇到选项、弹窗、命令、
        /// 分支、等待、限时行或带回调的节点时停止，把控制权交还给玩家
        /// </summary>
        public void RequestSkipToNextStop() => _skipToNextStopRequested = true;
        /// <summary>选择某个选项（按下标）</summary>
        public void SelectChoice(int index) => _selectedChoiceIndex = index;
        /// <summary>切换自动播放</summary>
        public void ToggleAuto() => _toggleAuto = true;
        /// <summary>切换快进</summary>
        public void ToggleFast() => _toggleFast = true;
        /// <summary>领取 / 确认当前弹窗</summary>
        public void ClaimPopup() => _popupIntent = PopupIntentClaim;
        /// <summary>关闭 / 取消当前弹窗</summary>
        public void DismissPopup() => _popupIntent = PopupIntentDismiss;

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

            if (_completionDeferred && !IsCompletionBlocked()) {
                _completionDeferred = false;
                CompleteNow();
                return;
            }

            if (_toggleAuto) {
                _toggleAuto = false;
                Options.AutoMode = !Options.AutoMode;
                Options.FastMode = false;
                _autoTimer = 0f;
                StyleRegistry.GetDialogue(Style).PlayToggleAutoSound(Options.AutoMode);
            }
            if (_toggleFast) {
                _toggleFast = false;
                Options.FastMode = !Options.FastMode;
                Options.AutoMode = false;
                _autoTimer = 0f;
                StyleRegistry.GetDialogue(Style).PlayToggleFastSound(Options.FastMode);
            }

            if (ActivePopup != null) {
                TickActivePopup(frames);
            }
            if (IsFinished) {
                return;
            }

            //场景已到达结尾但仍在等待非阻塞弹窗领取 / 关闭：等其解析后再真正完成
            if (_completionPending) {
                if (ActivePopup == null) {
                    CompleteNow();
                }
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

        private bool IsAdvanceBlocked() => BlocksAdvance?.Invoke() == true;

        private bool IsCompletionBlocked() => BlocksCompletion?.Invoke() == true;

        private void Transition(int nextIndex) {
            SafeInvoke(CurrentNode?.OnExit, "OnExit");
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

            SafeInvoke(node.OnEnter, "OnEnter");

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
                    SafeInvoke(command.Command, "Command");
                    Transition(_currentIndex + 1);
                    break;
                case BranchNode branch:
                    bool result = SafeEvaluate(branch.Predicate, true, "Branch.Predicate");
                    GoToTarget(result ? branch.IfTrue : branch.IfFalse);
                    break;
            }
        }

        private void BeginLine(CharacterId speaker, ExpressionId expression, string text, TimedSettings timed) {
            Line.Begin(speaker, expression, text, timed);
            _lastTypedSoundChar = 0;
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
                        SafeInvoke(CurrentNode?.OnExit, "OnExit");
                        Complete();
                    }
                    break;
                case NarrativeTarget.TargetKind.GotoScenario:
                    SafeInvoke(CurrentNode?.OnExit, "OnExit");
                    RequestedScenario = target.ScenarioKey;
                    Complete();
                    break;
                case NarrativeTarget.TargetKind.End:
                    SafeInvoke(CurrentNode?.OnExit, "OnExit");
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
                TryPlayTypingSounds(line);
            }

            //点击 / 跳过：未打完则补全
            if (_advanceRequested && line.LayoutReady && !line.Finished) {
                _advanceRequested = false;
                line.RevealAll();
                SyncTypingSoundAfterReveal(line);
            }
            if (_skipRequested) {
                _skipRequested = false;
                if (line.LayoutReady && !line.Finished) {
                    line.RevealAll();
                    SyncTypingSoundAfterReveal(line);
                }
            }
            if (_skipToNextStopRequested && line.LayoutReady && !line.Finished) {
                line.RevealAll();
                SyncTypingSoundAfterReveal(line);
            }

            if (!line.Finished) {
                return;
            }

            if (_skipToNextStopRequested && TryAdvanceSkipToNextStop()) {
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
                    if (IsAdvanceBlocked()) {
                        return;
                    }
                    Transition(_currentIndex + 1);
                    return;
                }
                if (line.TimedRemainingTicks <= 0f) {
                    if (IsAdvanceBlocked()) {
                        return;
                    }
                    SafeInvoke((CurrentNode as SayNode)?.Timed?.OnExpired, "Timed.OnExpired");
                    Transition(_currentIndex + 1);
                }
                return;
            }

            if (_advanceRequested) {
                _advanceRequested = false;
                if (IsAdvanceBlocked()) {
                    return;
                }
                Transition(_currentIndex + 1);
                return;
            }

            if (Options.AutoMode || Options.FastMode) {
                if (IsAdvanceBlocked()) {
                    _autoTimer = 0f;
                    return;
                }
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

        private bool TryAdvanceSkipToNextStop() {
            int guard = 0;
            bool advanced = false;

            while (_skipToNextStopRequested && guard++ < 512) {
                if (Phase is NarrativeSessionPhase.AwaitingPopup or NarrativeSessionPhase.AwaitingChoice) {
                    _skipToNextStopRequested = false;
                    return advanced;
                }

                NarrativeNode current = CurrentNode;
                if (current == null) {
                    _skipToNextStopRequested = false;
                    return advanced;
                }

                if (current is ChoiceNode choice) {
                    OpenChoices(choice);
                    _skipToNextStopRequested = false;
                    return true;
                }

                if (IsSkipStopNode(current)) {
                    _skipToNextStopRequested = false;
                    if (Line.LayoutReady && !Line.Finished && current is SayNode) {
                        Line.RevealAll();
                    }
                    return advanced;
                }

                if (IsAdvanceBlocked()) {
                    _skipToNextStopRequested = false;
                    return advanced;
                }

                if (Graph?.Get(_currentIndex + 1) == null) {
                    _skipToNextStopRequested = false;
                    return advanced;
                }

                Transition(_currentIndex + 1);
                advanced = true;

                if (Phase == NarrativeSessionPhase.AwaitingPopup) {
                    _skipToNextStopRequested = false;
                    return true;
                }
            }

            if (guard >= 512) {
                VaultMod.Instance.Logger.Warn($"Narrative scenario '{Key}' skip guard tripped.");
            }

            return advanced;
        }

        private static bool IsSkipStopNode(NarrativeNode node) {
            if (node == null) {
                return true;
            }

            if (node.OnEnter != null || node.OnExit != null) {
                return true;
            }

            return node switch {
                SayNode say => say.Timed != null,
                ChoiceNode => true,
                PopupNode => true,
                CommandNode => true,
                BranchNode => true,
                WaitNode => true,
                _ => true,
            };
        }

        private void OpenChoices(ChoiceNode choice) {
            if (choice.Options == null || choice.Options.Count == 0) {
                VaultMod.Instance.Logger.Warn($"Narrative scenario '{Key}' choice has no options; continuing.");
                GoToTarget(NarrativeTarget.Continue);
                return;
            }
            if (choice.Timed == null && !choice.Options.Any(IsChoiceOptionEnabled)) {
                VaultMod.Instance.Logger.Warn($"Narrative scenario '{Key}' choice has no enabled options and no timeout; continuing.");
                GoToTarget(NarrativeTarget.Continue);
                return;
            }

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
            if (choice.Options == null || choice.Options.Count == 0) {
                PendingChoice = null;
                GoToTarget(NarrativeTarget.Continue);
                return;
            }

            if (choice.Timed != null) {
                _choiceTimedRemaining -= frames;
                if (_choiceTimedRemaining <= 0f) {
                    SafeInvoke(choice.Timed.OnExpired, "Choice.Timed.OnExpired");
                    ResolveTimeout(choice);
                    return;
                }
            }

            if (_selectedChoiceIndex >= 0) {
                int index = _selectedChoiceIndex;
                _selectedChoiceIndex = -1;
                if (index < choice.Options.Count) {
                    ChoiceOption option = choice.Options[index];
                    if (IsChoiceOptionEnabled(option)) {
                        ResolveChoice(option);
                    }
                }
            }
        }

        private void ResolveTimeout(ChoiceNode choice) {
            ChoiceOption option = null;
            if (choice.DefaultChoice.HasValue) {
                option = choice.Options.FirstOrDefault(o => o.Id.Equals(choice.DefaultChoice.Value) && IsChoiceOptionEnabled(o));
            }
            if (option == null) {
                List<ChoiceOption> enabled = choice.Options.Where(IsChoiceOptionEnabled).ToList();
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
            SafeInvoke(option.OnSelect, "Choice.OnSelect");
            GoToTarget(option.Target ?? NarrativeTarget.Continue);
        }

        private bool IsChoiceOptionEnabled(ChoiceOption option)
            => option != null && option.IsEnabled;

        private void TickActivePopup(float frames) {
            //非必领弹窗的自动保持
            if (_popupIntent == PopupIntentNone && !ActivePopup.RequireClaim && ActivePopup.AutoHoldSeconds >= 0f) {
                _popupTimer += frames;
                if (_popupTimer >= ActivePopup.AutoHoldSeconds * 60f) {
                    _popupIntent = PopupIntentTimeout;
                }
            }

            if (_popupIntent == PopupIntentNone) {
                return;
            }

            int intent = _popupIntent;
            _popupIntent = PopupIntentNone;
            PopupPayload payload = ActivePopup;
            bool wasBlocking = _popupBlocking;

            if (intent == PopupIntentClaim) {
                SafeInvoke(() => payload.OnClaimed(LocalPlayer), "Popup.OnClaimed");
                LastPopupResult = PopupResolution.Claimed;
                if (payload is RewardPayload reward && reward.ItemType > 0) {
                    StyleRegistry.GetPopup(Style).PlayGrantSound();
                }
            }
            else if (intent == PopupIntentDismiss) {
                SafeInvoke(() => payload.OnDismissed(LocalPlayer), "Popup.OnDismissed");
                LastPopupResult = PopupResolution.Dismissed;
            }
            else {
                SafeInvoke(() => payload.OnTimedOut(LocalPlayer), "Popup.OnTimedOut");
                LastPopupResult = PopupResolution.Timeout;
            }

            ActivePopup = null;
            if (wasBlocking) {
                Phase = NarrativeSessionPhase.Playing;
                Transition(_currentIndex + 1);
            }
        }

        private void Complete() {
            if (IsCompletionBlocked()) {
                _completionDeferred = true;
                return;
            }

            //若仍有未解析的非阻塞弹窗在展示，则推迟真正完成，等其领取 / 关闭后再结束。
            //（阻塞弹窗不会走到这里：它把流程挂在 AwaitingPopup，解析后才继续推进）
            if (ActivePopup != null) {
                _completionPending = true;
                PendingChoice = null;
                DialogueVisible = false;
                return;
            }
            CompleteNow();
        }

        private void CompleteNow() {
            _completionDeferred = false;
            _completionPending = false;
            Phase = NarrativeSessionPhase.Completed;
            PendingChoice = null;
            DialogueVisible = false;
        }

        /// <summary>中止会话（世界切换 / 强制关闭），不会写入完成标记</summary>
        public void Abort() {
            if (Phase == NarrativeSessionPhase.Completed) {
                return;
            }
            _completionPending = false;
            _completionDeferred = false;
            Phase = NarrativeSessionPhase.Aborted;
            PendingChoice = null;
            ActivePopup = null;
            DialogueVisible = false;
        }

        private void SafeInvoke(Action action, string context) {
            if (action == null) {
                return;
            }
            try {
                action();
            } catch (Exception ex) {
                VaultMod.Instance.Logger.Error($"Narrative scenario '{Key}' {context} threw: {ex}");
            }
        }

        private bool SafeEvaluate(Func<bool> predicate, bool fallback, string context) {
            if (predicate == null) {
                return fallback;
            }
            try {
                return predicate();
            } catch (Exception ex) {
                VaultMod.Instance.Logger.Error($"Narrative scenario '{Key}' {context} threw: {ex}");
                return fallback;
            }
        }

        private void TryPlayTypingSounds(LinePresentation line) {
            int visible = line.VisibleCharCount;
            if (visible <= _lastTypedSoundChar) {
                return;
            }

            DialogueSkin skin = StyleRegistry.GetDialogue(Style);
            int interval = skin.TypingSoundInterval;
            if (interval <= 0) {
                _lastTypedSoundChar = visible;
                return;
            }

            for (int c = _lastTypedSoundChar + 1; c <= visible; c++) {
                if (c % interval == 0) {
                    skin.PlayTypingSound();
                }
            }

            _lastTypedSoundChar = visible;
        }

        private void SyncTypingSoundAfterReveal(LinePresentation line) {
            _lastTypedSoundChar = line.VisibleCharCount;
        }
    }
}
