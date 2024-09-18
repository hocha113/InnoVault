namespace InnoVault.UIHanders
{
    /// <summary>
    /// LayersModeEnum 枚举表示不同的界面层，用于控制游戏中的各类界面元素绘制和逻辑处理
    /// </summary>
    public enum LayersModeEnum
    {
        /// <summary>
        /// 绘制鼠标的文本信息，负责处理鼠标提示文本的设置和显示
        /// </summary>
        Vanilla_Mouse_Text,
        /// <summary>
        /// 处理与使用鼠标在持有物品时相关的逻辑，例如当物品被放置在鼠标上时的操作
        /// </summary>
        Vanilla_Interface_Logic_1,
        /// <summary>
        /// 绘制其他玩家的名字、距离、生命值文本以及玩家的头像图标
        /// </summary>
        Vanilla_MP_Player_Names,
        /// <summary>
        /// 处理UI隐藏开关的逻辑，根据“隐藏UI”选项切换界面显示状态
        /// </summary>
        Vanilla_Hide_UI_Toggle,
        /// <summary>
        /// 绘制玩家的生命值、魔力值、呼吸条以及状态增益图标
        /// </summary>
        Vanilla_Resource_Bars,
        /// <summary>
        /// 绘制并处理游戏内的选项菜单逻辑
        /// </summary>
        Vanilla_Ingame_Options,
        /// <summary>
        /// 绘制网络诊断信息，通常用于显示网络连接的状态和调试数据
        /// </summary>
        Vanilla_Diagnose_Net,
        /// <summary>
        /// 游戏开始菜单，初始界面，通常用于显示公告类信息
        /// </summary>
        Mod_MenuLoad,
    }
}
