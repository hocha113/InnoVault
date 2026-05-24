using System;
using System.Collections.Generic;

namespace InnoVault.StateMachines
{
    /// <summary>
    /// 阶段控制器：把"按 HP 阈值递降切换状态"、"伴生死亡触发暴怒"等<b>批量、有序</b>的<see cref="PhaseTrigger{TContext}"/>
    /// 配置以更显式的形式聚合到一起<br/>
    /// 单条<see cref="PhaseTrigger{TContext}"/>仍是底层原语，本类只是"声明 N 条阶段并按 HP 阈值降序排好序"的语义糖<br/><br/>
    /// 典型用法：
    /// <code>
    /// PhaseController.For(machine)
    ///     .OnHpBelow(ctx =&gt; (float)ctx.Npc.life / ctx.Npc.lifeMax, 0.66f, () =&gt; new Phase2State())
    ///     .OnHpBelow(ctx =&gt; (float)ctx.Npc.life / ctx.Npc.lifeMax, 0.33f, () =&gt; new Phase3State())
    ///     .Apply();
    /// </code>
    /// 阀值降序保证：当 HP 从 70% 跌到 30% 时，<b>只</b>有 0.33f 的阶段会触发（即最严格的那个），
    /// 不会被"先命中 0.66f"的较弱阶段抢先吃掉
    /// </summary>
    /// <typeparam name="TContext">承载状态机的上下文类型</typeparam>
    public sealed class PhaseController<TContext>
    {
        private readonly VaultStateMachine<TContext> _machine;
        private readonly List<PhaseEntry> _entries = [];

        internal PhaseController(VaultStateMachine<TContext> machine) {
            _machine = machine ?? throw new ArgumentNullException(nameof(machine));
        }

        /// <summary>
        /// 声明一条"HP 比例低于<paramref name="threshold"/>"的阶段触发，仅一次性触发<br/>
        /// <paramref name="hpFraction"/>用来从上下文中取出当前 HP/MaxHP 比例（0..1），框架不假定具体字段
        /// </summary>
        /// <param name="hpFraction">从上下文取得当前 HP 比例（典型实现：<c>ctx =&gt; (float)ctx.Npc.life / ctx.Npc.lifeMax</c>）</param>
        /// <param name="threshold">触发阈值，例如 0.5f 表示当 HP &lt;= 50% 时触发</param>
        /// <param name="target">目标状态工厂；<see langword="null"/>表示只跑<paramref name="onFire"/>不切状态</param>
        /// <param name="onFire">可选副作用回调（屏幕震动、音效），<b>仅</b>在服务端/单机端运行</param>
        /// <param name="label">调试标签</param>
        public PhaseController<TContext> OnHpBelow(
            Func<TContext, float> hpFraction,
            float threshold,
            Func<IVaultState<TContext>> target,
            Action<TContext> onFire = null,
            string label = null) {
            if (hpFraction == null) {
                throw new ArgumentNullException(nameof(hpFraction));
            }
            _entries.Add(new PhaseEntry {
                Threshold = threshold,
                Trigger = new PhaseTrigger<TContext> {
                    When = ctx => hpFraction(ctx) <= threshold,
                    Transition = target,
                    OnFire = onFire,
                    Label = label ?? $"HpBelow<={threshold:0.###}",
                },
            });
            return this;
        }

        /// <summary>
        /// 声明一条自定义谓词的一次性阶段触发<br/>
        /// 与<see cref="VaultStateMachineBuilder{TContext}.Phase{TTarget}"/>不同的是，目标用工厂表达（可携带闭包参数）
        /// </summary>
        public PhaseController<TContext> OnCondition(
            Func<TContext, bool> when,
            Func<IVaultState<TContext>> target,
            Action<TContext> onFire = null,
            string label = null) {
            if (when == null) {
                throw new ArgumentNullException(nameof(when));
            }
            _entries.Add(new PhaseEntry {
                //自定义条件无 HP 阀值概念；用 float.NaN 占位，排序时排到末尾
                Threshold = float.NaN,
                Trigger = new PhaseTrigger<TContext> {
                    When = when,
                    Transition = target,
                    OnFire = onFire,
                    Label = label,
                },
            });
            return this;
        }

        /// <summary>
        /// 把已声明的阶段触发批量提交到<see cref="VaultStateMachine{TContext}.PhaseTriggers"/><br/>
        /// HP 阈值条目按阈值升序排列（严格阈值优先评估），保证 HP 急速下降时直接命中最严格阶段；
        /// 自定义条件条目按声明顺序排在 HP 条目之后
        /// </summary>
        public void Apply() {
            _entries.Sort(static (a, b) => {
                //把 NaN 排到最后（NaN 与任何值比较都返回 false，所以单纯 CompareTo 不可靠）
                bool aNaN = float.IsNaN(a.Threshold);
                bool bNaN = float.IsNaN(b.Threshold);
                if (aNaN && bNaN) {
                    return 0;
                }
                if (aNaN) {
                    return 1;
                }
                if (bNaN) {
                    return -1;
                }
                return a.Threshold.CompareTo(b.Threshold);
            });
            for (int i = 0; i < _entries.Count; i++) {
                _machine.PhaseTriggers.Add(_entries[i].Trigger);
            }
            _entries.Clear();
        }

        private struct PhaseEntry
        {
            public float Threshold;
            public PhaseTrigger<TContext> Trigger;
        }
    }

    /// <summary>
    /// <see cref="PhaseController{TContext}"/>的入口；用法见<see cref="PhaseController{TContext}"/>注释
    /// </summary>
    public static class PhaseController
    {
        /// <summary>给指定的<see cref="VaultStateMachine{TContext}"/>开始声明一组阶段触发</summary>
        public static PhaseController<TContext> For<TContext>(VaultStateMachine<TContext> machine)
            => new(machine);
    }
}
