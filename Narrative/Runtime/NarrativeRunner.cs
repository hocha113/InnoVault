using InnoVault.Narrative.Core;
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
        private readonly struct PendingScenario(string key, Action onCompleted)
        {
            public string Key { get; } = key;
            public Action OnCompleted { get; } = onCompleted;
        }

        private static readonly Queue<PendingScenario> _pending = new();

        /// <summary>当前活动会话，<see langword="null"/> 表示空闲</summary>
        public static NarrativeSession Active { get; private set; }

        /// <summary>是否有会话在运行或排队（含被阻塞弹窗挂起）</summary>
        public static bool IsBusy => Active != null || _pending.Count > 0;

        /// <summary>指定场景是否正在运行或排队</summary>
        public static bool IsScenarioActiveOrPending(string key)
            => Active != null && Active.Key == key || PendingContains(key);

        private static bool PendingContains(string key)
        {
            foreach (PendingScenario pending in _pending)
            {
                if (pending.Key == key)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>启动一个场景；若当前忙则入队，等空闲后自动启动</summary>
        public static bool Begin(NarrativeScenario scenario) => Begin(scenario, null);

        /// <summary>启动一个场景并附加完成回调</summary>
        public static bool Begin(NarrativeScenario scenario, Action onCompleted) {
            if (scenario == null) {
                return false;
            }
            if (Active != null) {
                if (!PendingContains(scenario.Key)) {
                    _pending.Enqueue(new PendingScenario(scenario.Key, onCompleted));
                }
                return true;
            }
            StartScenario(scenario, onCompleted);
            return true;
        }

        /// <summary>按 Key 启动一个场景</summary>
        public static bool Begin(string key) {
            NarrativeScenario scenario = NarrativeScenario.Find(key);
            return scenario != null && Begin(scenario);
        }

        /// <summary>按场景类型启动一个场景，避免手写字符串 Key</summary>
        public static bool Begin<T>() where T : NarrativeScenario {
            T scenario = NarrativeScenario.Find<T>();
            return scenario != null && Begin(scenario);
        }

        private static void StartScenario(NarrativeScenario scenario, Action onCompleted) {
            NarrativeGraph graph;
            try {
                graph = scenario.BuildGraph();
            } catch (Exception ex) {
                VaultMod.Instance.Logger.Error($"Narrative scenario '{scenario.Key}' Build threw: {ex}");
                return;
            }

            NarrativeSession session = new(scenario, graph, scenario.DefaultStyle) { OnCompleted = onCompleted };
            Active = session;
            NarrativeServices.Progress?.SetProgress(scenario.Key, ScenarioProgress.Started);
            SafeInvoke(scenario.InvokeStarted, $"scenario '{scenario.Key}' OnStarted");
            session.Start();
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
                    StartScenario(next, entry.OnCompleted);
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

            if (!string.IsNullOrEmpty(session.RequestedScenario)) {
                Begin(session.RequestedScenario);
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
