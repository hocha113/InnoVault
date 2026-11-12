using System;
using System.Collections.Generic;

namespace InnoVault.Scenarios
{
    //一类节点当前参与合成的各层，从下到上排列。每个属性取最上层已设置的值，没设置的看下一层
    internal sealed class ScenarioLayers<T> where T : ScenarioNode
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

    //当前生效的节点快照。场景树变化、节点开关、区域进出时由导演层重建；接管代码与每帧规则只读这里，不遍历场景树。
    //各项整体替换，读档线程上读到的始终是完整的一份。
    //参与条件：场景处于激活中，节点连同所有祖先都启用；跟随玩家的节点还要求那个玩家身在节点的作用范围里（ScenarioNode.ScopeArea）
    internal static class ScenarioRuntime
    {
        //没有玩家视角：只有作用于整块舞台的节点参与
        private const int NoPlayer = -1;

        private static IReadOnlyList<VaultScenario> _active = [];
        //其他玩家视角下的规则层，用到时才合成，重建时作废
        private static ScenarioLayers<ScenarioRules>[] _rulesByPlayer = new ScenarioLayers<ScenarioRules>[256];

        //当前舞台场景，会话之外与换舞台途中为空
        internal static VaultScenario Stage { get; private set; }

        //当前舞台是不是一块场景舞台（而不是根世界）
        internal static bool InScenarioStage => Stage != null && Stage is not RootScenario;

        //跟随玩家的节点，本地视角：节点没有作用范围，或本地玩家在范围里时参与。
        //专用服务器上没有本地玩家，这里只剩作用于整块舞台的节点：时间、天气、事件这类整块舞台共享的状态在服务器上只听它们的
        internal static ScenarioLayers<ScenarioRules> Rules { get; private set; } = ScenarioLayers<ScenarioRules>.Empty;

        internal static ScenarioLayers<ScenarioCamera> Camera { get; private set; } = ScenarioLayers<ScenarioCamera>.Empty;

        internal static ScenarioLayers<ScenarioEnvironment> Environment { get; private set; } = ScenarioLayers<ScenarioEnvironment>.Empty;

        internal static ScenarioLayers<ScenarioMap> Map { get; private set; } = ScenarioLayers<ScenarioMap>.Empty;

        //按位置的节点：只要启用就参与，由节点自己按坐标判断范围
        internal static ScenarioProtection[] Protections { get; private set; } = [];

        //所有启用中的区域（无论玩家在不在里面），区域进出判定与边界约束用
        internal static ScenarioArea[] Areas { get; private set; } = [];

        //所有启用中的规则节点（不看玩家在不在作用范围里），从下到上：给 NPC、掉落物这类不跟玩家走的对象按位置取值
        internal static ScenarioRules[] RulesByLayer { get; private set; } = [];

        //某个玩家视角下的规则层：逐玩家计算的规则（刷怪、重力、区域判定）在服务器上、以及在客户端上推算其他玩家时用
        internal static ScenarioLayers<ScenarioRules> RulesFor(int player) {
            if (player == ScenarioPlatform.LocalPlayerIndex || Areas.Length == 0) {
                return Rules;
            }
            ScenarioLayers<ScenarioRules>[] cache = _rulesByPlayer;
            if ((uint)player >= (uint)cache.Length) {
                return Rules;
            }
            return cache[player] ??= new ScenarioLayers<ScenarioRules>(Collect<ScenarioRules>(_active, player, scoped: true));
        }

        //某一格上的规则值：从上到下找第一个作用范围覆盖这一格、又设置了这个属性的规则节点
        internal static TValue? PickRuleAt<TValue>(int tileX, int tileY, Func<ScenarioRules, TValue?> pick) where TValue : struct {
            ScenarioRules[] all = RulesByLayer;
            for (int i = all.Length - 1; i >= 0; i--) {
                ScenarioArea scope = all[i].ScopeArea;
                if (scope != null && !scope.Contains(tileX, tileY)) {
                    continue;
                }
                TValue? value = pick(all[i]);
                if (value.HasValue) {
                    return value;
                }
            }
            return null;
        }

        internal static void Rebuild(VaultScenario stage, IReadOnlyList<VaultScenario> active) {
            int local = ScenarioPlatform.LocalPlayerIndex;
            Stage = stage;
            _active = active;
            _rulesByPlayer = new ScenarioLayers<ScenarioRules>[256];
            Rules = Layers(Collect<ScenarioRules>(active, local, scoped: true));
            Camera = Layers(Collect<ScenarioCamera>(active, local, scoped: true));
            Environment = Layers(Collect<ScenarioEnvironment>(active, local, scoped: true));
            Map = Layers(Collect<ScenarioMap>(active, local, scoped: true));
            RulesByLayer = Collect<ScenarioRules>(active, NoPlayer, scoped: false);
            Protections = CollectAll<ScenarioProtection>(active);
            Areas = CollectAll<ScenarioArea>(active);
        }

        internal static void Clear() {
            Stage = null;
            _active = [];
            _rulesByPlayer = new ScenarioLayers<ScenarioRules>[256];
            Rules = ScenarioLayers<ScenarioRules>.Empty;
            Camera = ScenarioLayers<ScenarioCamera>.Empty;
            Environment = ScenarioLayers<ScenarioEnvironment>.Empty;
            Map = ScenarioLayers<ScenarioMap>.Empty;
            RulesByLayer = [];
            Protections = [];
            Areas = [];
        }

        private static ScenarioLayers<T> Layers<T>(T[] items) where T : ScenarioNode
            => items.Length == 0 ? ScenarioLayers<T>.Empty : new ScenarioLayers<T>(items);

        //节点对这个玩家是否生效：没有作用范围，或者范围启用着、玩家身在其中。player 为 NoPlayer（专用服务器的本地视角）时只认没有作用范围的节点
        private static bool InScope(ScenarioNode node, int player) {
            ScenarioArea scope = node.ScopeArea;
            return scope == null || (player != NoPlayer && scope.EnabledInTree && scope.IsEngaged(player));
        }

        //从下到上排好的一类节点。scoped 为假时不看作用范围，只要启用就收
        private static T[] Collect<T>(IReadOnlyList<VaultScenario> active, int player, bool scoped) where T : ScenarioNode {
            List<(T Node, int Order)> found = null;
            int order = 0;
            for (int i = 0; i < active.Count; i++) {
                VaultScenario scenario = active[i];
                if (scenario.Status != ScenarioStatus.Active) {
                    continue;
                }
                IReadOnlyList<ScenarioNode> nodes = scenario.AllNodes;
                for (int k = 0; k < nodes.Count; k++) {
                    if (nodes[k] is T match && match.EnabledInTree && (!scoped || InScope(match, player))) {
                        (found ??= []).Add((match, order++));
                    }
                }
            }
            if (found == null) {
                return [];
            }
            //同级按激活顺序：List.Sort 不稳定，带上收集顺序比较
            found.Sort(static (a, b) => a.Node.LayerPriority != b.Node.LayerPriority
                ? a.Node.LayerPriority.CompareTo(b.Node.LayerPriority)
                : a.Order.CompareTo(b.Order));
            T[] items = new T[found.Count];
            for (int i = 0; i < items.Length; i++) {
                items[i] = found[i].Node;
            }
            return items;
        }

        private static T[] CollectAll<T>(IReadOnlyList<VaultScenario> active) where T : ScenarioNode {
            List<T> found = null;
            for (int i = 0; i < active.Count; i++) {
                VaultScenario scenario = active[i];
                if (scenario.Status != ScenarioStatus.Active) {
                    continue;
                }
                IReadOnlyList<ScenarioNode> nodes = scenario.AllNodes;
                for (int k = 0; k < nodes.Count; k++) {
                    if (nodes[k] is T match && match.EnabledInTree) {
                        (found ??= []).Add(match);
                    }
                }
            }
            return found == null ? [] : [.. found];
        }
    }
}
