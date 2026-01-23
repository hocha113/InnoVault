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

        /// <summary>
        /// 检查是否有任何调试选项被启用
        /// </summary>
        public static bool AnyDebugEnabled =>
            TileProcessorBoxSizeDraw || TileProcessorShowName || TileProcessorShowPosition || TileProcessorShowID ||
            ActorBoxSizeDraw || ActorShowName || ActorShowPosition || ActorShowID || ActorShowVelocity;

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
        /// 重置所有调试设置为默认值
        /// </summary>
        public static void ResetAll() {
            ResetTileProcessor();
            ResetActor();
        }
    }
}
