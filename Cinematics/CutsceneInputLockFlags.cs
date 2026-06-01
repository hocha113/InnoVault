using System;

namespace InnoVault.Cinematics
{
    /// <summary>
    /// 过场演出期间需要屏蔽的本地玩家输入
    /// </summary>
    [Flags]
    public enum CutsceneInputLockFlags
    {
        /// <summary>不屏蔽任何输入</summary>
        None = 0,
        /// <summary>屏蔽水平和垂直移动</summary>
        Movement = 1 << 0,
        /// <summary>屏蔽跳跃</summary>
        Jump = 1 << 1,
        /// <summary>屏蔽物品使用</summary>
        UseItem = 1 << 2,
        /// <summary>屏蔽右键交互</summary>
        UseTile = 1 << 3,
        /// <summary>屏蔽钩爪、丢弃、坐骑等辅助动作</summary>
        Utility = 1 << 4,
        /// <summary>屏蔽常用动作输入</summary>
        All = Movement | Jump | UseItem | UseTile | Utility
    }
}
