using System;
using System.Collections.Generic;

namespace InnoVault.Scenarios
{
    //一类组件当前参与合成的各层，从下到上排列。每个属性取最上层已设置的值，没设置的看下一层
    internal sealed class ScenarioLayers<T> where T : ScenarioComponent
    {
        internal static readonly ScenarioLayers<T> Empty = new([]);

        private readonly T[] _items;

        internal ScenarioLayers(T[] items) {
            _items = items;
        }

        internal int Count => _items.Length;

        internal bool Any => _items.Length > 0;

        //从下到上
        internal T this[int index] => _items[index];

        internal TValue? Pick<TValue>(Func<T, TValue?> pick) where TValue : struct {
            for (int i = _items.Length - 1; i >= 0; i--) {
                TValue? value = pick(_items[i]);
                if (value.HasValue) {
                    return value;
                }
            }
            return null;
        }

        internal TValue PickRef<TValue>(Func<T, TValue> pick) where TValue : class {
            for (int i = _items.Length - 1; i >= 0; i--) {
                TValue value = pick(_items[i]);
                if (value != null) {
                    return value;
                }
            }
            return null;
        }

        internal T TopWhere(Func<T, bool> predicate) {
            for (int i = _items.Length - 1; i >= 0; i--) {
                if (predicate(_items[i])) {
                    return _items[i];
                }
            }
            return null;
        }
    }

    //当前生效的组件快照。场景树变化、区域进出时由导演层重建；接管代码与每帧规则只读这里，不遍历场景树。
    //各项整体替换，读档线程上读到的始终是完整的一份
    internal static class ScenarioRuntime
    {
        //当前舞台场景，会话之外与换舞台途中为空
        internal static VaultScenario Stage { get; private set; }

        //当前舞台是不是一块场景舞台（而不是根世界）
        internal static bool InScenarioStage => Stage != null && Stage is not RootScenario;

        //跟随玩家的组件：场景没有区域，或本地玩家在区域里时参与
        internal static ScenarioLayers<ScenarioRules> Rules { get; private set; } = ScenarioLayers<ScenarioRules>.Empty;

        internal static ScenarioLayers<ScenarioCamera> Camera { get; private set; } = ScenarioLayers<ScenarioCamera>.Empty;

        internal static ScenarioLayers<ScenarioEnvironment> Environment { get; private set; } = ScenarioLayers<ScenarioEnvironment>.Empty;

        internal static ScenarioLayers<ScenarioMap> Map { get; private set; } = ScenarioLayers<ScenarioMap>.Empty;

        //按位置的组件：只要场景处于激活中就参与，由组件自己按坐标判断范围
        internal static ScenarioProtection[] Protections { get; private set; } = [];

        //所有处于激活中的场景的区域（无论玩家在不在里面），区域进出判定与边界约束用
        internal static ScenarioArea[] Areas { get; private set; } = [];

        internal static void Rebuild(VaultScenario stage, IReadOnlyList<VaultScenario> active) {
            Stage = stage;
            Rules = Collect<ScenarioRules>(active);
            Camera = Collect<ScenarioCamera>(active);
            Environment = Collect<ScenarioEnvironment>(active);
            Map = Collect<ScenarioMap>(active);
            Protections = CollectAll<ScenarioProtection>(active);
            List<ScenarioArea> areas = [];
            for (int i = 0; i < active.Count; i++) {
                VaultScenario scenario = active[i];
                if (scenario.Status == ScenarioStatus.Active && scenario.Area != null) {
                    areas.Add(scenario.Area);
                }
            }
            Areas = [.. areas];
        }

        internal static void Clear() {
            Stage = null;
            Rules = ScenarioLayers<ScenarioRules>.Empty;
            Camera = ScenarioLayers<ScenarioCamera>.Empty;
            Environment = ScenarioLayers<ScenarioEnvironment>.Empty;
            Map = ScenarioLayers<ScenarioMap>.Empty;
            Protections = [];
            Areas = [];
        }

        private static ScenarioLayers<T> Collect<T>(IReadOnlyList<VaultScenario> active) where T : ScenarioComponent {
            List<(T Component, int Order)> found = null;
            int order = 0;
            for (int i = 0; i < active.Count; i++) {
                VaultScenario scenario = active[i];
                if (scenario.Status != ScenarioStatus.Active || scenario.Area is { Engaged: false }) {
                    continue;
                }
                IReadOnlyList<ScenarioComponent> components = scenario.Components;
                for (int k = 0; k < components.Count; k++) {
                    if (components[k] is T match) {
                        (found ??= []).Add((match, order++));
                    }
                }
            }
            if (found == null) {
                return ScenarioLayers<T>.Empty;
            }
            //同级按激活顺序：List.Sort 不稳定，带上收集顺序比较
            found.Sort(static (a, b) => a.Component.LayerPriority != b.Component.LayerPriority
                ? a.Component.LayerPriority.CompareTo(b.Component.LayerPriority)
                : a.Order.CompareTo(b.Order));
            T[] items = new T[found.Count];
            for (int i = 0; i < items.Length; i++) {
                items[i] = found[i].Component;
            }
            return new ScenarioLayers<T>(items);
        }

        private static T[] CollectAll<T>(IReadOnlyList<VaultScenario> active) where T : ScenarioComponent {
            List<T> found = null;
            for (int i = 0; i < active.Count; i++) {
                VaultScenario scenario = active[i];
                if (scenario.Status != ScenarioStatus.Active) {
                    continue;
                }
                IReadOnlyList<ScenarioComponent> components = scenario.Components;
                for (int k = 0; k < components.Count; k++) {
                    if (components[k] is T match) {
                        (found ??= []).Add(match);
                    }
                }
            }
            return found == null ? [] : [.. found];
        }
    }
}
