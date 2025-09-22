﻿using System;
using System.Collections.Generic;
using Terraria;

namespace InnoVault.GameSystem
{
    /// <summary>
    /// 用于控制音乐的接口
    /// </summary>
    public interface IUpdateAudio
    {
        /// <summary>
        /// 用于决定选择音乐的结果，但在某些情况下可能被覆盖
        /// 需要注意的是，该钩子对应的实例是加载阶段所生成的单实例，而非进入游戏后的真实实例
        /// 所以在编写逻辑时应当尽量使用静态数据，防止发生意料之外的结果
        /// </summary>
        public void DecideMusic() { }
        /// <summary>
        /// 运行在<see cref="Main.UpdateAudio"/>之后，用于覆盖音乐判定结果
        /// 需要注意的是，该钩子对应的实例是加载阶段所生成的单实例，而非进入游戏后的真实实例
        /// 所以在编写逻辑时应当尽量使用静态数据，防止发生意料之外的结果
        /// </summary>
        public void PostUpdateAudio() { }
    }

    /// <summary>
    /// 该基类用于控制和覆盖场景的元素，比如音乐
    /// </summary>
    public class SceneOverride : VaultType<SceneOverride>
    {
        /// <summary>
        /// 封闭内容
        /// </summary>
        protected sealed override void VaultRegister() { }
        /// <summary>
        /// 加载内容
        /// </summary>
        public sealed override void VaultSetup() {
            SetStaticDefaults();
        }
        /// <summary>
        /// 用于决定选择音乐的结果，但在某些情况下可能被覆盖
        /// </summary>
        public virtual void DecideMusic() {

        }
        /// <summary>
        /// 运行在<see cref="Main.UpdateAudio"/>之后，用于覆盖音乐判定结果
        /// </summary>
        public virtual void PostUpdateAudio() {

        }
    }
}
