using InnoVault.Actors;
using System.Collections.Generic;
using Terraria;
using Terraria.DataStructures;

namespace InnoVault.Scenarios
{
    /// <summary>
    /// 场景停用时怎样处理它拥有的实体
    /// </summary>
    public enum ScenarioOwnedCleanup
    {
        /// <summary>清除：NPC 与弹幕直接移除（不触发死亡与击杀效果），Actor 被杀死</summary>
        Despawn,
        /// <summary>保留，只解除归属</summary>
        Keep,
    }

    public abstract partial class VaultScenario
    {
        private ScenarioEntitySource _spawnSource;
        private List<Actor> _ownedActors;

        /// <summary>
        /// 生成来源：用它生成的 NPC 与弹幕自动归这个场景所有；归属实体再生成的子弹幕、子 NPC（来源为 <c>EntitySource_Parent</c>）也一并归属
        /// </summary>
        public IEntitySource SpawnSource => _spawnSource ??= new ScenarioEntitySource(this);

        /// <summary>场景停用时怎样处理它拥有的实体，缺省清除</summary>
        public virtual ScenarioOwnedCleanup OwnedCleanup => ScenarioOwnedCleanup.Despawn;

        /// <summary>把一个已经存在的 NPC 登记为这个场景所有</summary>
        public void Own(NPC npc) {
            if (npc != null && npc.active && npc.TryGetGlobalNPC(out ScenarioOwnedNPC owned)) {
                owned.Owner = this;
            }
        }

        /// <summary>把一个已经存在的弹幕登记为这个场景所有</summary>
        public void Own(Projectile projectile) {
            if (projectile != null && projectile.active && projectile.TryGetGlobalProjectile(out ScenarioOwnedProjectile owned)) {
                owned.Owner = this;
            }
        }

        /// <summary>把一个 Actor 登记为这个场景所有（<c>ActorLoader.NewActor</c> 不带生成来源，Actor 只能显式登记）</summary>
        public void Own(Actor actor) {
            if (actor != null && actor.Active) {
                (_ownedActors ??= []).Add(actor);
            }
        }

        /// <summary>这个 NPC 是否归这个场景所有</summary>
        public bool Owns(NPC npc) => npc != null && npc.active && npc.TryGetGlobalNPC(out ScenarioOwnedNPC owned) && ReferenceEquals(owned.Owner, this);

        /// <summary>这个弹幕是否归这个场景所有</summary>
        public bool Owns(Projectile projectile)
            => projectile != null && projectile.active && projectile.TryGetGlobalProjectile(out ScenarioOwnedProjectile owned) && ReferenceEquals(owned.Owner, this);

        internal List<Actor> OwnedActors => _ownedActors;
    }

    /// <summary>
    /// 场景的生成来源，见 <see cref="VaultScenario.SpawnSource"/>
    /// </summary>
    public sealed class ScenarioEntitySource : IEntitySource
    {
        internal ScenarioEntitySource(VaultScenario scenario) {
            Scenario = scenario;
        }

        /// <summary>生成这个实体的场景实例</summary>
        public VaultScenario Scenario { get; }

        /// <inheritdoc/>
        public string Context => Scenario?.Key;
    }
}
