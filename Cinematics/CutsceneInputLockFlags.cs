using System;

namespace InnoVault.Cinematics
{
    /// <summary>
    /// 过场演出期间需要屏蔽的本地玩家输入
    /// <br/>前五项由 InnoVault 在 <c>SetControls</c> 里直接清掉原版控制位；模组自己的键位（技能、变身、召回一类）原版管不到，
    /// 由模组在自己的 <c>ProcessTriggers</c> 里查 <see cref="CutsceneDirector.IsInputLocked"/>（<see cref="Abilities"/>）
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
        /// <summary>模组自定义键位（技能、变身、收回法相一类）：InnoVault 不代为拦截，模组查询后自行忽略</summary>
        Abilities = 1 << 5,
        /// <summary>屏蔽常用动作输入（含模组键位）</summary>
        All = Movement | Jump | UseItem | UseTile | Utility | Abilities
    }
}
