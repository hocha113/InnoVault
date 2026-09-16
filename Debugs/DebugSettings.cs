namespace InnoVault.Debugs
{
    /// <summary>
    /// 开发者调试设置
    /// 通过指令 /vaultdebug 打开调试面板进行配置
    /// </summary>
    public static class DebugSettings
    {
        #region TileProcessor调试选项
        /// <summary>
        /// 是否绘制物块处理器(TP)的碰撞箱
        /// </summary>
        public static bool TileProcessorBoxSizeDraw { get; set; }

        /// <summary>
        /// 是否显示TP实体的名称信息
        /// </summary>
        public static bool TileProcessorShowName { get; set; }

        /// <summary>
        /// 是否显示TP实体的位置信息
        /// </summary>
        public static bool TileProcessorShowPosition { get; set; }

        /// <summary>
        /// 是否显示TP实体的ID信息
        /// </summary>
        public static bool TileProcessorShowID { get; set; }
        #endregion

        #region Actor调试选项
        /// <summary>
        /// 是否绘制Actor的碰撞箱
        /// </summary>
        public static bool ActorBoxSizeDraw { get; set; }

        /// <summary>
        /// 是否显示Actor的名称信息
        /// </summary>
        public static bool ActorShowName { get; set; }

        /// <summary>
        /// 是否显示Actor的位置信息
        /// </summary>
        public static bool ActorShowPosition { get; set; }

        /// <summary>
        /// 是否显示Actor的ID和WhoAmI信息
        /// </summary>
        public static bool ActorShowID { get; set; }

        /// <summary>
        /// 是否显示Actor的速度信息
        /// </summary>
        public static bool ActorShowVelocity { get; set; }
        #endregion

        #region 状态机/行为树调试选项
        /// <summary>
        /// 是否在屏幕左侧叠加显示当前所有<see cref="StateMachines.StateMachineProbe{TContext}"/>的<br/>
        /// 当前状态 / Blackboard 快照 / 最近转移历史
        /// </summary>
        public static bool StateMachineShowOverlay { get; set; }

        /// <summary>
        /// 是否在屏幕叠加显示所有<see cref="BehaviorTrees.BehaviorTreeProbe{TContext}"/>的<br/>
        /// 当前根节点递归 LastStatus 路径
        /// </summary>
        public static bool BehaviorTreeShowOverlay { get; set; }
        #endregion

        #region Rig2D调试选项
        /// <summary>
        /// 是否叠加显示本帧步进过的所有 2D 骨架：骨线、关节、贴图锚点、各求解器的目标与可达圈
        /// </summary>
        public static bool Rig2DShowOverlay { get; set; }

        /// <summary>
        /// 是否轮询 ModSources 里的 <c>.rig.json</c> 源文件并在修改后原地热重载（仅开发机有源文件时有效）
        /// </summary>
        public static bool Rig2DHotReload { get; set; }
        #endregion

        /// <summary>
        /// 检查是否有任何调试选项被启用
        /// </summary>
        public static bool AnyDebugEnabled =>
            TileProcessorBoxSizeDraw || TileProcessorShowName || TileProcessorShowPosition || TileProcessorShowID ||
            ActorBoxSizeDraw || ActorShowName || ActorShowPosition || ActorShowID || ActorShowVelocity ||
            StateMachineShowOverlay || BehaviorTreeShowOverlay || Rig2DShowOverlay || Rig2DHotReload;

        /// <summary>
        /// 获取当前启用的调试选项数量
        /// </summary>
        public static int EnabledCount {
            get {
                int count = 0;
                if (TileProcessorBoxSizeDraw) count++;
                if (TileProcessorShowName) count++;
                if (TileProcessorShowPosition) count++;
                if (TileProcessorShowID) count++;
                if (ActorBoxSizeDraw) count++;
                if (ActorShowName) count++;
                if (ActorShowPosition) count++;
                if (ActorShowID) count++;
                if (ActorShowVelocity) count++;
                if (StateMachineShowOverlay) count++;
                if (BehaviorTreeShowOverlay) count++;
                if (Rig2DShowOverlay) count++;
                if (Rig2DHotReload) count++;
                return count;
            }
        }

        /// <summary>
        /// 重置TileProcessor调试设置
        /// </summary>
        public static void ResetTileProcessor() {
            TileProcessorBoxSizeDraw = false;
            TileProcessorShowName = false;
            TileProcessorShowPosition = false;
            TileProcessorShowID = false;
        }

        /// <summary>
        /// 重置Actor调试设置
        /// </summary>
        public static void ResetActor() {
            ActorBoxSizeDraw = false;
            ActorShowName = false;
            ActorShowPosition = false;
            ActorShowID = false;
            ActorShowVelocity = false;
        }

        /// <summary>
        /// 重置状态机 / 行为树调试设置
        /// </summary>
        public static void ResetStateMachine() {
            StateMachineShowOverlay = false;
            BehaviorTreeShowOverlay = false;
        }

        /// <summary>
        /// 重置 Rig2D 调试设置
        /// </summary>
        public static void ResetRig2D() {
            Rig2DShowOverlay = false;
            Rig2DHotReload = false;
        }

        /// <summary>
        /// 重置所有调试设置为默认值
        /// </summary>
        public static void ResetAll() {
            ResetTileProcessor();
            ResetActor();
            ResetStateMachine();
            ResetRig2D();
        }
    }
}
