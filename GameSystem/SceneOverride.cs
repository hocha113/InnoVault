using System;
using System.Collections.Generic;
using Terraria;
using Terraria.ModLoader;

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
        /// <inheritdoc/>
        protected sealed override bool AutoVaultRegistryRegister => false;
        /// <inheritdoc/>
        protected sealed override bool AutoVaultRegistryFinishLoading => false;
        /// <summary>
        /// 所有修改的实例集合
        /// </summary>
        public new static List<SceneOverride> Instances { get; internal set; } = [];
        /// <summary>
        /// 从类型映射到实例
        /// </summary>
        public new static Dictionary<Type, SceneOverride> TypeToInstance { get; internal set; } = [];
        /// <summary>
        /// 封闭内容
        /// </summary>
        protected sealed override void VaultRegister() {
            Instances.Add(this);
            TypeToInstance[GetType()] = this;
        }
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
        /// <summary>
        /// 返回要作用的<see cref="ModSceneEffect"/>的内部名数组<br/>
        /// 这些场景效果会被 <see cref="PreIsSceneEffectActive"/> 修改，同时 <see cref="PostIsSceneEffectActive"/> 也将运行<br/>
        /// </summary>
        /// <returns></returns>
        public virtual IEnumerable<string> GetActiveSceneEffectFullNames() => [];
        /// <summary>
        /// 如果要使这个钩子生效，必须重写 <see cref="GetActiveSceneEffectFullNames"/> 并返回有效的内部名数组<br/>
        /// 在<see cref="ModSceneEffect.IsSceneEffectActive(Player)"/>调用前运行，用于决定该场景效果是否生效<br/>
        /// 返回有效值可以阻止后续逻辑运行，默认返回<see langword="null"/><br/>
        /// </summary>
        /// <param name="modSceneEffect"></param>
        /// <param name="player"></param>
        /// <returns></returns>
        public virtual bool? PreIsSceneEffectActive(ModSceneEffect modSceneEffect, Player player) { return null; }
        /// <summary>
        /// 如果要使这个钩子生效，必须重写 <see cref="GetActiveSceneEffectFullNames"/> 并返回有效的内部名数组<br/>
        /// 在<see cref="ModSceneEffect.IsSceneEffectActive(Player)"/>调用后运行，用于在场景效果生效后执行额外逻辑
        /// </summary>
        /// <param name="modSceneEffect"></param>
        /// <param name="player"></param>
        public virtual void PostIsSceneEffectActive(ModSceneEffect modSceneEffect, Player player) { }
    }
}
