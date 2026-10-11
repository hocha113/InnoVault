using Microsoft.Xna.Framework;
using System;
using Terraria;

namespace InnoVault.Scenarios
{
    /// <summary>
    /// 物块保护组件：禁止挖掘、放置、锤击、爆炸与液体、电线工具改动范围内的物块与墙<br/>
    /// 按位置生效：范围是 <see cref="Bounds"/>，为空时取场景的 <see cref="ScenarioArea"/>（逐格判断形状），再没有时是整块舞台；
    /// 与玩家在不在区域里无关，只要场景处于激活中就生效。多个场景的保护同时生效时取并集<br/>
    /// 场景自己的建造代码与区域还原用 <see cref="Bypass"/> 绕过；单格例外重写 <see cref="CanModify"/>
    /// </summary>
    public class ScenarioProtection : ScenarioComponent
    {
        [ThreadStatic]
        private static int _bypassDepth;

        /// <summary>禁止挖掘物块（镐、钻头，以及弹幕、NPC 造成的物块破坏）</summary>
        public bool NoMining { get; init; }

        /// <summary>禁止放置物块与墙，以及用新物块替换旧物块</summary>
        public bool NoPlacing { get; init; }

        /// <summary>禁止锤掉墙</summary>
        public bool NoWallBreaking { get; init; }

        /// <summary>禁止把物块敲成斜坡或半砖</summary>
        public bool NoHammering { get; init; }

        /// <summary>物块与墙不受爆炸影响</summary>
        public bool NoExplosions { get; init; }

        /// <summary>禁止在范围内使用桶与海绵</summary>
        public bool NoLiquidTools { get; init; }

        /// <summary>禁止在范围内使用扳手、剪线钳、促动器、促动杆与电路设计图</summary>
        public bool NoWiring { get; init; }

        /// <summary>保护范围（物块坐标）；为空时取场景的区域，再没有时是整块舞台</summary>
        public Rectangle? Bounds { get; init; }

        /// <summary>全部开关都打开的保护</summary>
        public static ScenarioProtection Full() => new() {
            NoMining = true,
            NoPlacing = true,
            NoWallBreaking = true,
            NoHammering = true,
            NoExplosions = true,
            NoLiquidTools = true,
            NoWiring = true,
        };

        /// <summary>当前线程是否处于 <see cref="Bypass"/> 作用域内</summary>
        public static bool Bypassed => _bypassDepth > 0;

        /// <summary>
        /// 返回一个作用域，在它释放之前当前线程对物块的改动不受任何保护组件限制。用 <c>using</c> 包住场景自己的建造代码
        /// </summary>
        public static IDisposable Bypass() {
            _bypassDepth++;
            return new BypassScope();
        }

        /// <summary>某一格是否在保护范围内</summary>
        public bool Covers(int i, int j) {
            if (Bounds is Rectangle bounds) {
                return bounds.Contains(i, j);
            }
            return Scenario?.Area?.Contains(i, j) ?? true;
        }

        /// <summary>
        /// 范围内的单格例外：返回 <see langword="true"/> 放行这次改动。<paramref name="player"/> 在非玩家造成的改动（爆炸、弹幕）里为空
        /// </summary>
        public virtual bool CanModify(Player player, int i, int j) => false;

        private sealed class BypassScope : IDisposable
        {
            private bool _disposed;

            public void Dispose() {
                if (_disposed) {
                    return;
                }
                _disposed = true;
                _bypassDepth--;
            }
        }
    }
}
