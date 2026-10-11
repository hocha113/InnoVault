using InnoVault.GameSystem;
using Microsoft.Xna.Framework;

namespace InnoVault.Scenarios
{
    /// <summary>
    /// 区域还原组件：场景激活时记录一块矩形的完整状态，停用时原样写回。叠加场景借它在当前舞台里临时改建一块地方（例如搭竞技场）<br/>
    /// 记录内容：所有已注册的物块数据（物块、墙、液体、油漆、涂层、电线、促动器，以及模组自定义的格数据）、箱子与箱内物品、告示牌、
    /// 原版与模组的物块实体、TP<br/>
    /// 激活时同时写一份还原日志：改建期间保存退出、自动存档或强退，都可能把临时改建写进存档，下次读入这块舞台时（玩家出生之前）
    /// 先按日志还原。日志里模组物块与墙按名字记录；读回时找不到的类型那几格置空，并记日志<br/>
    /// 挂了它的场景不能放在会话层。停用时舞台已经存过盘（绕过暂停菜单的保存退出）的情况下不在内存里写回，留给下次读档补还原
    /// </summary>
    public class ScenarioRestore : ScenarioComponent
    {
        /// <summary>还原范围（物块坐标）；为空时取场景 <see cref="ScenarioArea"/> 的外接框，两者都没有时组合不合法</summary>
        public Rectangle? Bounds { get; init; }

        /// <summary>记录之后立即放进还原范围的结构（SaveStructure 区域数据），停用时随还原一起撤掉；为空表示不放</summary>
        public RegionSaveData? Structure { get; init; }

        /// <summary><see cref="Structure"/> 的左上角（物块坐标），为空时取还原范围的左上角</summary>
        public Point? StructureOrigin { get; init; }

        /// <summary>是否写还原日志，缺省为真。一次性舞台上的还原区不写日志（舞台离开时整块删除）</summary>
        public bool Journal { get; init; } = true;

        internal override bool BindsToStage => true;

        //平台后端持有的快照
        internal object Snapshot { get; set; }

        //平台后端的日志路径，写回后用于标记"已还原，等下次存盘"
        internal string JournalPath { get; set; }

        /// <summary>实际的还原范围（物块坐标）</summary>
        public Rectangle ResolveBounds() => Bounds ?? Scenario?.Area?.Bounds ?? Rectangle.Empty;

        /// <inheritdoc/>
        protected override void OnActivate(ScenarioTransition transition) {
            if (ScenarioPlatform.RestoreBackend == null) {
                ScenarioPlatform.Error($"Scenario.Restore.{Scenario?.Key}", $"[Scenarios] {Scenario?.Key}: no restore backend on this endpoint; the area will not be restored");
                return;
            }
            ScenarioPlatform.RestoreBackend.Capture(this, transition);
        }

        /// <inheritdoc/>
        protected override void OnDeactivate(ScenarioTransition transition) {
            ScenarioPlatform.RestoreBackend?.Restore(this, transition);
        }

        /// <inheritdoc/>
        protected override string Validate() {
            Rectangle bounds = ResolveBounds();
            if (bounds.Width <= 0 || bounds.Height <= 0) {
                return "ScenarioRestore needs Bounds or a ScenarioArea on the same scenario";
            }
            return null;
        }
    }
}
