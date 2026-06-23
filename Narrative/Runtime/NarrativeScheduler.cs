using InnoVault.Narrative.Core;
using InnoVault.Narrative.Progress;
using InnoVault.Narrative.Services;
using System;
using System.Collections.Generic;
using Terraria;

namespace InnoVault.Narrative.Runtime
{
    /// <summary>
    /// 声明式场景调度器。每帧评估各场景的 <see cref="NarrativePolicy"/>，挑选优先级最高且满足条件者触发<br/>
    /// 阻塞采用谓词式（消费者可注册任意阻塞条件，如"Boss 战中"），框架自身始终把<br/>
    /// <see cref="NarrativeRunner.IsBusy"/> 视为阻塞——因此活动的对话 / 选择 / 阻塞弹窗都会自然阻止新场景触发
    /// </summary>
    public static class NarrativeScheduler
    {
        private const int FailedStartRetryDelayTicks = 300;

        private static readonly List<Func<bool>> _blockers = [];
        private static readonly Dictionary<string, int> _failedStartCooldowns = new(StringComparer.Ordinal);

        /// <summary>注册一个全局阻塞条件（返回 <see langword="true"/> 时本帧不触发任何场景）</summary>
        public static void RegisterBlocker(Func<bool> blocker) {
            if (blocker != null) {
                _blockers.Add(blocker);
            }
        }

        /// <summary>当前是否被阻塞</summary>
        public static bool IsBlocked() {
            if (NarrativeRunner.IsBusy) {
                return true;
            }
            for (int i = 0; i < _blockers.Count; i++) {
                if (SafeEvaluate(_blockers[i], true, $"blocker #{i}")) {
                    return true;
                }
            }
            return false;
        }

        /// <summary>每帧评估并触发（客户端、游戏内）</summary>
        public static void Tick() {
            if (Main.dedServ || IsBlocked()) {
                return;
            }

            INarrativeProgressStore store = NarrativeServices.Progress;
            NarrativeScenario best = null;
            int bestPriority = int.MinValue;
            TickFailedStartCooldowns();

            foreach (NarrativeScenario scenario in NarrativeScenario.All) {
                NarrativePolicy policy = scenario.Policy;
                if (policy == null) {
                    continue;
                }
                if (_failedStartCooldowns.ContainsKey(scenario.Key)) {
                    continue;
                }

                bool completed = policy.IsCompleted != null
                    ? SafeEvaluate(() => policy.IsCompleted(store), true, $"scenario '{scenario.Key}' IsCompleted")
                    : store != null && store.GetProgress(scenario.Key) == ScenarioProgress.Completed;
                if (completed && !policy.Repeatable) {
                    continue;
                }
                if (NarrativeRunner.IsScenarioActiveOrPending(scenario.Key)) {
                    continue;
                }
                if (policy.Blocked != null && SafeEvaluate(policy.Blocked, true, $"scenario '{scenario.Key}' Blocked")) {
                    continue;
                }
                if (policy.CanTrigger != null && !SafeEvaluate(() => policy.CanTrigger(store, Main.LocalPlayer), false, $"scenario '{scenario.Key}' CanTrigger")) {
                    continue;
                }
                if (policy.Priority > bestPriority) {
                    bestPriority = policy.Priority;
                    best = scenario;
                }
            }

            if (best == null) {
                return;
            }

            NarrativePolicy chosen = best.Policy;
            //两阶段：先标记触发，真正完成由 NarrativeRunner 写入并回调 OnCompleted
            ScenarioProgress previous = store?.GetProgress(best.Key) ?? ScenarioProgress.None;
            store?.SetProgress(best.Key, ScenarioProgress.Triggered);
            if (!NarrativeRunner.Begin(best, () => SafeInvoke(() => chosen.OnCompleted?.Invoke(store), $"scenario '{best.Key}' policy OnCompleted"))) {
                store?.SetProgress(best.Key, previous);
                _failedStartCooldowns[best.Key] = FailedStartRetryDelayTicks;
                VaultMod.Instance.Logger.Warn($"Narrative scenario '{best.Key}' start failed; scheduler will retry after {FailedStartRetryDelayTicks} ticks.");
                return;
            }
            SafeInvoke(() => chosen.OnTriggered?.Invoke(store), $"scenario '{best.Key}' policy OnTriggered");
        }

        /// <summary>清空注册的阻塞器（卸载时调用）</summary>
        public static void Reset() {
            _blockers.Clear();
            _failedStartCooldowns.Clear();
        }

        private static void TickFailedStartCooldowns() {
            if (_failedStartCooldowns.Count == 0) {
                return;
            }

            List<string> keys = new(_failedStartCooldowns.Keys);
            for (int i = 0; i < keys.Count; i++) {
                string key = keys[i];
                int remaining = _failedStartCooldowns[key] - 1;
                if (remaining <= 0) {
                    _failedStartCooldowns.Remove(key);
                }
                else {
                    _failedStartCooldowns[key] = remaining;
                }
            }
        }

        private static bool SafeEvaluate(Func<bool> predicate, bool fallback, string context) {
            if (predicate == null) {
                return fallback;
            }
            try {
                return predicate();
            } catch (Exception ex) {
                VaultMod.Instance.Logger.Error($"Narrative scheduler {context} threw: {ex}");
                return fallback;
            }
        }

        private static void SafeInvoke(Action action, string context) {
            if (action == null) {
                return;
            }
            try {
                action();
            } catch (Exception ex) {
                VaultMod.Instance.Logger.Error($"Narrative scheduler {context} threw: {ex}");
            }
        }
    }
}
