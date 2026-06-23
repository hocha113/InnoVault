using System;
using System.Collections.Generic;

namespace InnoVault.StateMachines
{
    /// <summary>
    /// 通用有限状态机运行时，参考 Unity Animator 与 BrutalNPCs (Destroyer/Twins) 的复制实现统一抽象<br/>
    /// 关键约定：
    /// <list type="bullet">
    /// <item>当<see cref="ServerAuthoritative"/>为<see langword="true"/>（默认）时，仅服务端/单机端可主动驱动状态切换，客户端通过<see cref="NetSync"/>反推</item>
    /// <item><see cref="Transitions"/>包含普通转移与 Any-State 转移；后者会在每帧<see cref="IVaultState{TContext}.OnUpdate"/>之前优先评估</item>
    /// <item><see cref="PhaseTriggers"/>是"一次性宏观切换"的语义糖（HP阈值、伴生死亡等），独立维护一个 Fired 标记</item>
    /// <item><see cref="Blackboard"/>同时被本状态机的转移条件和<see cref="BehaviorTrees"/>子树共享</item>
    /// </list>
    /// </summary>
    /// <typeparam name="TContext">承载状态机的上下文类型，通常封装一个 NPC/Projectile/Actor 引用以及辅助字段</typeparam>
    public class VaultStateMachine<TContext>
    {
        /// <summary>
        /// 状态机所绑定的上下文实例
        /// </summary>
        public TContext Context { get; }
        /// <summary>
        /// 当前正在驱动的状态。<see cref="SetInitialState"/>之前可能为<see langword="null"/>
        /// </summary>
        public IVaultState<TContext> CurrentState { get; private set; }
        /// <summary>
        /// 上一次<see cref="CurrentState"/>切换前持有的状态，用于"回退"模式以及调试展示
        /// </summary>
        public IVaultState<TContext> PreviousState { get; private set; }
        /// <summary>
        /// 状态机内部的强类型参数存储（FSM 转移条件与<c>BehaviorTrees</c>谓词共用）<br/>
        /// 构造时可通过参数注入外部<see cref="StateMachines.Blackboard"/>实例，以便与外层<see cref="InnoVault.BehaviorTrees.BTNode{TContext}"/>共享黑板
        /// </summary>
        public Blackboard Blackboard { get; }
        /// <summary>
        /// 可插拔的网络同步策略（默认<see langword="null"/>表示纯本地，常用实现为<see cref="AiSlotNetSync{TContext}"/>）<br/>
        /// 服务端在<see cref="ChangeState(IVaultState{TContext}, string)"/>时写出当前<see cref="IVaultState{TContext}.StateId"/>，<br/>
        /// 客户端每帧<see cref="Update"/>读入并对照本地状态进行补正
        /// </summary>
        public INetStateSync<TContext> NetSync { get; set; }
        /// <summary>
        /// 是否以服务端为权威；<see langword="true"/>时客户端不会主动驱动转移（默认）<br/>
        /// 极少数纯客户端可视化用途的状态机可关闭此项
        /// </summary>
        public bool ServerAuthoritative { get; set; } = true;
        /// <summary>
        /// 当前已注册的状态转移规则（含 Any-State）<br/>
        /// 一般通过<see cref="VaultStateMachineBuilder{TContext}"/>初始化，运行时也可手动增删
        /// </summary>
        public List<VaultStateTransition<TContext>> Transitions { get; } = [];
        /// <summary>
        /// 当前已注册的阶段触发器（HP阈值等），按注册顺序评估
        /// </summary>
        public List<PhaseTrigger<TContext>> PhaseTriggers { get; } = [];
        /// <summary>
        /// 状态切换事件，参数依次为(上一状态, 新状态, 触发原因)<br/>
        /// 适合接入调试器与上层游戏特效（屏幕震动 / 音效），<see cref="StateChangeReason.NetSync"/>意味着客户端的被动同步
        /// </summary>
        public event Action<IVaultState<TContext>, IVaultState<TContext>, StateChangeReason> OnStateChanged;
        /// <summary>
        /// 状态机是否已终止；<see cref="InnoVault.BehaviorTrees.VaultStateMachineAsBtLeaf{TContext}"/>等桥接节点据此判断 BT 叶的 Success/Failure 返回时机<br/>
        /// 默认<see langword="false"/>；调用<see cref="MarkTerminated"/>后变为<see langword="true"/>
        /// </summary>
        public bool IsTerminated { get; private set; }

        /// <summary>
        /// 构造一个新的状态机实例并绑定上下文。<see cref="CurrentState"/>仍为<see langword="null"/>，<br/>
        /// 直到调用<see cref="SetInitialState"/>后才会进入初始状态
        /// </summary>
        /// <param name="context">状态机的上下文实例</param>
        /// <param name="blackboard">
        /// 可选的外部<see cref="Blackboard"/>实例；为<see langword="null"/>时内部新建一份私有黑板<br/>
        /// 当状态机要作为<see cref="InnoVault.BehaviorTrees.VaultStateMachineAsBtLeaf{TContext}"/>嵌入到行为树中时，<br/>
        /// 应当传入与外层 BT 同一份黑板，避免"FSM 与 BT 各写各的"语义割裂
        /// </param>
        public VaultStateMachine(TContext context, Blackboard blackboard = null) {
            Context = context;
            Blackboard = blackboard ?? new Blackboard();
        }

        /// <summary>
        /// 设置状态机的初始状态并立即调用其<see cref="IVaultState{TContext}.OnEnter"/>，<br/>
        /// 同时通过<see cref="NetSync"/>写出初始 StateId（服务端/单机端）<br/>
        /// 仅应在状态机构建后调用一次；若<see cref="CurrentState"/>已存在会被视为误用并打出警告后忽略——<br/>
        /// 需要重置状态机时请改用<see cref="Restart(IVaultState{TContext})"/>
        /// </summary>
        public void SetInitialState(IVaultState<TContext> state) {
            if (state == null) {
                return;
            }
            if (CurrentState != null) {
                VaultMod.LoggerError(
                    $"VaultStateMachine<{typeof(TContext).Name}>:set_initial_twice",
                    $"SetInitialState was called while CurrentState ({CurrentState.GetType().FullName}) " +
                    $"is still set. The second call is ignored to avoid skipping OnExit. Call Restart(...) instead.");
                return;
            }
            CurrentState = state;
            state.OnEnter(this, Context);
            if (CanWriteAuthoritative()) {
                TryWriteAuthoritativeState(state);
            }
            OnStateChanged?.Invoke(null, state, StateChangeReason.Initial);
        }

        /// <summary>
        /// 重启状态机：依次执行当前状态的<see cref="IVaultState{TContext}.OnExit"/>、清空<see cref="IsTerminated"/>，<br/>
        /// 然后进入<paramref name="newInitial"/>所代表的新初始状态<br/>
        /// 适合从<see cref="MarkTerminated"/>后或外层桥接节点（<c>BehaviorTreeAsState</c> / <c>VaultStateMachineAsBtLeaf</c>）重置子状态机时使用
        /// </summary>
        /// <param name="newInitial">新的初始状态实例；为<see langword="null"/>时仅清理而不重新进入</param>
        public void Restart(IVaultState<TContext> newInitial) {
            IVaultState<TContext> oldState = CurrentState;
            oldState?.OnExit(this, Context);
            PreviousState = oldState;
            CurrentState = null;
            IsTerminated = false;
            if (newInitial != null) {
                SetInitialState(newInitial);
            }
        }

        /// <summary>
        /// 仅清除<see cref="IsTerminated"/>标记，不动当前状态<br/>
        /// 用于"外层桥接节点 Reset"等需要让<see cref="Update"/>恢复工作但又不希望重入<see cref="IVaultState{TContext}.OnEnter"/>的场景
        /// </summary>
        public void ResetTerminated() {
            IsTerminated = false;
        }

        /// <summary>
        /// 强制切换到指定状态：依次执行<see cref="IVaultState{TContext}.OnExit"/>→替换→<see cref="IVaultState{TContext}.OnEnter"/>，<br/>
        /// 并在服务端/单机端写出新的 StateId 与<c>netUpdate</c><br/>
        /// 通常用于显式中断（受击打断、玩家死亡等），声明式转移请走<see cref="Transitions"/>或<see cref="PhaseTriggers"/>
        /// </summary>
        /// <param name="newState">目标状态实例；为<see langword="null"/>时直接忽略，避免误清空</param>
        /// <param name="label">可选的调试标签，会随<see cref="OnStateChanged"/>分发到调试器</param>
        public void ChangeState(IVaultState<TContext> newState, string label = null)
            => DoChangeState(newState, StateChangeReason.Manual, label);

        /// <summary>
        /// 通过<see cref="VaultStateRegistry{TContext}"/>查表后切换到指定 ID 的状态<br/>
        /// 该重载存在的意义是消除"<c>CreateStateFromIndex</c>" 类的手写工厂 switch
        /// </summary>
        /// <param name="stateId">目标状态在注册表中的 ID</param>
        public void ChangeState(int stateId) {
            IVaultState<TContext> next = VaultStateRegistry<TContext>.Create(stateId);
            if (next != null) {
                DoChangeState(next, StateChangeReason.Manual, null);
            }
        }

        /// <summary>
        /// 显式将状态机置为已终止；外层（如<c>BehaviorTreeBridges</c>的<see cref="InnoVault.BehaviorTrees.VaultStateMachineAsBtLeaf{TContext}"/>叶节点）会据此结束<br/>
        /// 不会调用当前状态的<see cref="IVaultState{TContext}.OnExit"/>——若需要可在 Mark 之前手动 ChangeState 到一个"死亡态"
        /// </summary>
        public void MarkTerminated() {
            IsTerminated = true;
        }

        /// <summary>
        /// 框架主循环：先做被动同步（客户端），再评估 Any-State / Phase，最后驱动当前状态的<see cref="IVaultState{TContext}.OnUpdate"/><br/>
        /// 时序与 <c>CalamityOverhaul/BrutalDestroyer/Core/DestroyerStateMachine.cs:34-53</c> 严格一致<br/>
        /// 调用方应在每帧的 AI 钩子里调用一次
        /// </summary>
        public void Update() {
            if (CurrentState == null || IsTerminated) {
                return;
            }

            //客户端先听取服务端权威，确保后续本地评估不会"逆驱"
            if (ServerAuthoritative && VaultUtils.isClient && NetSync != null) {
                int serverId = NetSync.ReadState(this);
                if (serverId != CurrentState.StateId && serverId >= 0) {
                    IVaultState<TContext> next = VaultStateRegistry<TContext>.Create(serverId);
                    if (next != null) {
                        DoChangeState(next, StateChangeReason.NetSync, null, skipNetWrite: true);
                    }
                }
            }

            //仅服务端/单机端做声明式切换
            if (CanWriteAuthoritative()) {
                if (TryEvaluatePhaseTriggers()) {
                    return; //一帧只让一个 Phase 触发，避免链式跳转
                }
                if (TryEvaluateTransitions()) {
                    return;
                }
            }

            //本地驱动 OnUpdate；服务端/单机端可根据返回值切换；客户端则忽略以避免与服务端冲突
            IVaultState<TContext> nextFromUpdate = CurrentState.OnUpdate(this, Context);
            if (nextFromUpdate != null && nextFromUpdate != CurrentState && CanWriteAuthoritative()) {
                DoChangeState(nextFromUpdate, StateChangeReason.UpdateReturn, null);
            }
        }

        /// <summary>
        /// 当前是否可作为"权威端"写出状态：服务端、单机端均算；客户端不算
        /// </summary>
        private bool CanWriteAuthoritative() => !ServerAuthoritative || !VaultUtils.isClient;

        private bool TryEvaluatePhaseTriggers() {
            for (int i = 0; i < PhaseTriggers.Count; i++) {
                PhaseTrigger<TContext> trigger = PhaseTriggers[i];
                if (trigger.Fired && !trigger.Repeatable) {
                    continue;
                }
                if (trigger.When == null || !trigger.When(Context)) {
                    continue;
                }
                trigger.OnFire?.Invoke(Context);
                if (!trigger.Repeatable) {
                    trigger.Fired = true;
                }
                IVaultState<TContext> target = trigger.Transition?.Invoke();
                if (target != null) {
                    DoChangeState(target, StateChangeReason.Phase, trigger.Label);
                    return true;
                }
            }
            return false;
        }

        private bool TryEvaluateTransitions() {
            //依赖<see cref="VaultStateMachineBuilder{TContext}.Build"/>已经把转移列表按"AnyState(优先级降序) → 普通转移(注册顺序)"排好
            //因此线性扫描"第一个命中即胜"就同时满足：
            //  1) Any-State 一定先于普通转移评估，普通转移无法覆盖任何已命中的 Any-State
            //  2) 多条 Any-State 之间按 Priority 决出胜者（同优先级按注册顺序）
            //  3) 多条普通转移之间按注册顺序决出胜者
            Type currentType = CurrentState.GetType();
            for (int i = 0; i < Transitions.Count; i++) {
                VaultStateTransition<TContext> t = Transitions[i];
                if (t.Fired && t.Once) {
                    continue;
                }
                //普通转移要求源类型匹配；AnyState 转移 FromState == null 始终参与评估
                if (t.FromState != null && t.FromState != currentType && !t.FromState.IsAssignableFrom(currentType)) {
                    continue;
                }
                if (t.Condition == null || !t.Condition(Context)) {
                    continue;
                }

                IVaultState<TContext> target = t.TargetFactory?.Invoke();
                if (target == null) {
                    //目标工厂未生效（例如返回 null），按"未触发"处理；继续往后扫
                    //注意：此时不应当把 Once 标记为已触发，否则一次失败的目标构造会永久吃掉该转移
                    continue;
                }
                if (t.Once) {
                    t.Fired = true;
                }
                DoChangeState(target, StateChangeReason.Transition, t.Label);
                return true;
            }
            return false;
        }

        private void DoChangeState(IVaultState<TContext> newState, StateChangeReason reason, string label, bool skipNetWrite = false) {
            if (newState == null || newState == CurrentState) {
                return;
            }

            IVaultState<TContext> oldState = CurrentState;
            oldState?.OnExit(this, Context);
            PreviousState = oldState;
            CurrentState = newState;
            newState.OnEnter(this, Context);

            if (!skipNetWrite && CanWriteAuthoritative()) {
                TryWriteAuthoritativeState(newState);
            }

            OnStateChanged?.Invoke(oldState, newState, reason);
            _ = label; //预留给将来更细的调试事件签名
        }

        /// <summary>
        /// 写出当前权威端的状态 ID<br/>
        /// 当<see cref="NetSync"/>已配置但<see cref="IVaultState{TContext}.StateId"/>为负（未注册到<see cref="VaultStateRegistry{TContext}"/>）时，<br/>
        /// 会跳过写入并打出一次警告——避免把<c>-1</c>写进<c>ai[slot]</c>导致客户端忽略状态、出现静默不同步
        /// </summary>
        private void TryWriteAuthoritativeState(IVaultState<TContext> state) {
            if (NetSync == null) {
                return;
            }
            int id = state.StateId;
            if (id < 0) {
                VaultMod.LoggerError(
                    $"VaultStateMachine<{typeof(TContext).Name}>:bad_state_id:{state.GetType().FullName}",
                    $"State {state.GetType().FullName} has StateId={id}; NetSync write is skipped. " +
                    "Tag the state with [VaultState(id, contextType)] or override StateId for synced states.");
                return;
            }
            NetSync.WriteState(this, id);
        }
    }
}
