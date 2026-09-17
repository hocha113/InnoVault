using System;
using System.Collections.Generic;

namespace InnoVault.Rigs2D.Solvers
{
    /// <summary>
    /// 求解器类型注册表：类型名 → 工厂
    /// <br/>内建类型在首次访问时注册；消费方可用 <see cref="Register"/> 接入自己的求解器，之后即可在 JSON 里按类型名引用
    /// </summary>
    public static class Rig2DSolverRegistry
    {
        private static readonly Dictionary<string, Func<Rig2DSolver>> factories = new(StringComparer.OrdinalIgnoreCase);
        private static bool builtinsRegistered;

        /// <summary>
        /// 注册一种求解器类型；同名覆盖
        /// </summary>
        public static void Register(string typeName, Func<Rig2DSolver> factory) {
            if (string.IsNullOrEmpty(typeName) || factory == null) {
                return;
            }
            EnsureBuiltins();
            factories[typeName] = factory;
        }

        /// <summary>
        /// 注销一种类型（模组卸载时清理自己注册的类型）
        /// </summary>
        public static void Unregister(string typeName) {
            if (!string.IsNullOrEmpty(typeName)) {
                factories.Remove(typeName);
            }
        }

        /// <summary>
        /// 按类型名创建求解器；未知类型返回 <see langword="null"/>
        /// </summary>
        public static Rig2DSolver Create(string typeName) {
            EnsureBuiltins();
            if (string.IsNullOrEmpty(typeName) || !factories.TryGetValue(typeName, out Func<Rig2DSolver> f)) {
                return null;
            }
            return f();
        }

        /// <summary>
        /// 是否已注册某类型
        /// </summary>
        public static bool Contains(string typeName) {
            EnsureBuiltins();
            return !string.IsNullOrEmpty(typeName) && factories.ContainsKey(typeName);
        }

        /// <summary>
        /// 全部已注册类型名
        /// </summary>
        public static IEnumerable<string> TypeNames {
            get {
                EnsureBuiltins();
                return factories.Keys;
            }
        }

        internal static void ResetForUnload() {
            factories.Clear();
            builtinsRegistered = false;
        }

        private static void EnsureBuiltins() {
            if (builtinsRegistered) {
                return;
            }
            builtinsRegistered = true;
            factories["TwoBoneIK"] = () => new TwoBoneIKSolver();
            factories["ThreeBoneLeg"] = () => new ThreeBoneLegSolver();
            factories["ChainFollow"] = () => new ChainFollowSolver();
            factories["Fabrik"] = () => new FabrikChainSolver();
            factories["PointAt"] = () => new PointAtSolver();
            factories["VerletStrand"] = () => new VerletStrandSolver();
            factories["FootPlantGait"] = () => new FootPlantGaitSolver();
            factories["ArcChain"] = () => new ArcChainSolver();
            factories["HangChain"] = () => new HangChainSolver();
            factories["BezierChain"] = () => new BezierChainSolver();
        }
    }
}
