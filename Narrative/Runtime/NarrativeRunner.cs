using InnoVault.Narrative.Core;
using InnoVault.Narrative.History;
using InnoVault.Narrative.Presentation;
using InnoVault.Narrative.Progress;
using InnoVault.Narrative.Services;
using System;
using System.Collections.Generic;
using Terraria;

namespace InnoVault.Narrative.Runtime
{
    /// <summary>
    /// 叙事运行总控。持有当前会话与待启动队列，统一推进会话、处理两阶段完成、衔接后续场景，<br/>
    /// 并把当前会话状态同步给已注册的视图。由 <see cref="NarrativeSystem"/> 在 UpdateUI 中驱动（仅客户端）
    /// </summary>
    public static class NarrativeRunner
    {
        private sealed class PendingScenario
        {
            private readonly List<Action> _completionCallbacks = [];

            public string Key { get; }

            public PendingScenario(string key, Action onCompleted) {
                Key = key;
                AddCompletionCallback(onCompleted);
            }

            public void AddCompletionCallback(Action onCompleted) {
                if (onCompleted != null) {
                    _completionCallbacks.Add(onCompleted);
                }
            }

            public Action CreateCompletionCallback() {
                if (_completionCallbacks.Count == 0) {
                    return null;
                }
                return () => {
                    for (int i = 0; i < _completionCallbacks.Count; i++) {
                        SafeInvoke(_completionCallbacks[i], $"scenario '{Key}' merged completion callback #{i}");
                    }
                };
            }
        }

        private static readonly Queue<PendingScenario> _pending = new();

        /// <summary>当前活动会话，<see langword="null"/> 表示空闲</summary>
        public static NarrativeSession Active { get; private set; }

        /// <summary>是否有会话在运行或排队（含被阻塞弹窗挂起）</summary>
        public static bool IsBusy => Active != null || _pending.Count > 0;

        /// <summary>指定场景是否正在运行或排队</summary>
        public static bool IsScenarioActiveOrPending(string key)
            => (Active != null && Active.Key == key) || PendingContains(key);

        private static bool PendingContains(string key) => TryGetPending(key, out _);

        private static bool TryGetPending(string key, out PendingScenario match) {
            foreach (PendingScenario pending in _pending) {
                if (pending.Key == key) {
                    match = pending;
                    return true;
                }
            }
            match = null;
            return false;
        }

        /// <summary>启动一个场景；若当前忙则入队，等空闲后自动启动</summary>
        public static bool Begin(NarrativeScenario scenario) => Begin(scenario, null);

        /// <summary>启动一个场景并附加完成回调</summary>
        public static bool Begin(NarrativeScenario scenario, Action onCompleted) {
            if (scenario == null) {
                VaultMod.Instance.Logger.Warn("Narrative Begin ignored: scenario is null.");
                return false;
            }
            if (Active != null) {
                if (!TryGetPending(scenario.Key, out PendingScenario pending)) {
                    _pending.Enqueue(new PendingScenario(scenario.Key, onCompleted));
                }
                else {
                    if (onCompleted != null) {
                        pending.AddCompletionCallback(onCompleted);
                        VaultMod.Instance.Logger.Warn($"Narrative scenario '{scenario.Key}' is already pending; merged completion callback.");
                    }
                    else {
                        VaultMod.Instance.Logger.Warn($"Narrative scenario '{scenario.Key}' is already pending; duplicate begin request ignored.");
                    }
                }
                return true;
            }
            return StartScenario(scenario, onCompleted);
        }

        /// <summary>按 Key 启动一个场景</summary>
        public static bool Begin(string key) {
            NarrativeScenario scenario = NarrativeScenario.Find(key);
            if (scenario == null) {
                VaultMod.Instance.Logger.Warn($"Narrative Begin ignored: scenario key '{key}' was not found.");
                return false;
            }
            return Begin(scenario);
        }

        /// <summary>按场景类型启动一个场景，避免手写字符串 Key</summary>
        public static bool Begin<T>() where T : NarrativeScenario {
            T scenario = NarrativeScenario.Find<T>();
            if (scenario == null) {
                VaultMod.Instance.Logger.Warn($"Narrative Begin ignored: scenario type '{typeof(T).FullName}' was not registered.");
                return false;
            }
            return Begin(scenario);
        }

        private static bool StartScenario(NarrativeScenario scenario, Action onCompleted) {
            NarrativeGraph graph;
            try {
                graph = scenario.BuildGraph();
            } catch (Exception ex) {
                VaultMod.Instance.Logger.Error($"Narrative scenario '{scenario.Key}' Build threw: {ex}");
                return false;
            }

            NarrativeSession session = new(scenario, graph, scenario.ResolvedDefaultStyle) { OnCompleted = onCompleted };
            Active = session;
            NarrativeServices.Progress?.SetProgress(scenario.Key, ScenarioProgress.Started);
            SafeInvoke(scenario.InvokeStarted, $"scenario '{scenario.Key}' OnStarted");
            session.Start();
            return true;
        }

        private static void SafeInvoke(Action action, string context) {
            if (action == null) {
                return;
            }
            try {
                action();
            } catch (Exception ex) {
                VaultMod.Instance.Logger.Error($"Narrative {context} threw: {ex}");
            }
        }

        /// <summary>每帧推进（客户端、游戏内）</summary>
        public static void Update(float frames) {
            if (Main.dedServ) {
                return;
            }

            if (Active != null) {
                try {
                    Active.Tick(frames);
                } catch (Exception ex) {
                    VaultMod.Instance.Logger.Error($"Narrative session '{Active.Key}' Tick threw: {ex}");
                    Active.Abort();
                }
                if (Active.Phase == NarrativeSessionPhase.Completed) {
                    FinalizeCompleted();
                }
                else if (Active.Phase == NarrativeSessionPhase.Aborted) {
                    FinalizeAborted();
                }
            }

            if (Active == null && _pending.Count > 0) {
                PendingScenario entry = _pending.Dequeue();
                NarrativeScenario next = NarrativeScenario.Find(entry.Key);
                if (next != null) {
                    if (!StartScenario(next, entry.CreateCompletionCallback())) {
                        VaultMod.Instance.Logger.Warn($"Narrative pending scenario '{entry.Key}' failed to start and was dropped.");
                    }
                }
                else {
                    VaultMod.Instance.Logger.Warn($"Narrative pending scenario '{entry.Key}' was not found and was dropped.");
                }
            }

            NarrativeViews.Sync(Active);
        }

        private static void FinalizeCompleted() {
            NarrativeSession session = Active;
            Active = null;

            NarrativeServices.Progress?.SetProgress(session.Key, ScenarioProgress.Completed);
            NarrativeServices.Sync?.SyncProgress(session.Key, ScenarioProgress.Completed);
            if (session.Scenario != null) {
                SafeInvoke(session.Scenario.InvokeCompleted, $"scenario '{session.Key}' OnCompleted");
            }
            SafeInvoke(session.OnCompleted, $"scenario '{session.Key}' completion callback");

            //每段对话完成后落盘一次，降低崩溃 / 异常退出导致历史丢失的风险
            NarrativeHistory.Save();

            if (!string.IsNullOrEmpty(session.RequestedScenario)) {
                if (!Begin(session.RequestedScenario)) {
                    VaultMod.Instance.Logger.Warn($"Narrative scenario '{session.Key}' requested missing scenario '{session.RequestedScenario}'.");
                }
            }
        }

        private static void FinalizeAborted() {
            NarrativeSession session = Active;
            Active = null;
            SafeInvoke(session.OnAborted, $"scenario '{session.Key}' abort callback");
        }

        /// <summary>中止当前会话并清空队列（世界切换 / 卸载时使用）</summary>
        public static void Reset() {
            Active?.Abort();
            Active = null;
            _pending.Clear();
            NarrativeViews.Sync(null);
        }
    }
}
