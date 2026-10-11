using InnoVault.Actors;
using System.Collections.Generic;
using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;

namespace InnoVault.Scenarios
{
    //NPC 的场景归属。实例跟着 NPC 走：槽位被复用时 SetDefaults 重建全局实例，旧归属自然消失
    internal sealed class ScenarioOwnedNPC : GlobalNPC
    {
        public override bool InstancePerEntity => true;

        internal VaultScenario Owner { get; set; }

        public override void OnSpawn(NPC npc, IEntitySource source) {
            Owner = ScenarioEntities.OwnerOf(source);
        }
    }

    //弹幕的场景归属，规则同 NPC
    internal sealed class ScenarioOwnedProjectile : GlobalProjectile
    {
        public override bool InstancePerEntity => true;

        internal VaultScenario Owner { get; set; }

        public override void OnSpawn(Projectile projectile, IEntitySource source) {
            Owner = ScenarioEntities.OwnerOf(source);
        }
    }

    internal static class ScenarioEntities
    {
        //场景自己的生成来源，或者父实体归某个仍在激活中的场景所有
        internal static VaultScenario OwnerOf(IEntitySource source) {
            VaultScenario owner = source switch {
                ScenarioEntitySource scenario => scenario.Scenario,
                EntitySource_Parent { Entity: NPC npc } when npc.TryGetGlobalNPC(out ScenarioOwnedNPC owned) => owned.Owner,
                EntitySource_Parent { Entity: Projectile projectile } when projectile.TryGetGlobalProjectile(out ScenarioOwnedProjectile owned) => owned.Owner,
                _ => null,
            };
            return owner?.Status is ScenarioStatus.Activating or ScenarioStatus.Active ? owner : null;
        }

        //场景停用完毕时由导演层经平台接缝调用。菜单期间（换舞台的存盘线程上、读档失败的回退里）世界已经卸载，不碰实体
        internal static void CleanUp(VaultScenario scenario) {
            if (Main.gameMenu || Main.dedServ) {
                return;
            }
            bool despawn = scenario.OwnedCleanup == ScenarioOwnedCleanup.Despawn;
            for (int i = 0; i < Main.maxNPCs; i++) {
                NPC npc = Main.npc[i];
                if (npc.active && npc.TryGetGlobalNPC(out ScenarioOwnedNPC owned) && ReferenceEquals(owned.Owner, scenario)) {
                    owned.Owner = null;
                    if (despawn) {
                        npc.active = false;
                        npc.netUpdate = true;
                    }
                }
            }
            for (int i = 0; i < Main.maxProjectiles; i++) {
                Projectile projectile = Main.projectile[i];
                if (projectile.active && projectile.TryGetGlobalProjectile(out ScenarioOwnedProjectile owned) && ReferenceEquals(owned.Owner, scenario)) {
                    owned.Owner = null;
                    if (despawn) {
                        projectile.active = false;
                        projectile.netUpdate = true;
                    }
                }
            }
            List<Actor> actors = scenario.OwnedActors;
            if (actors == null) {
                return;
            }
            if (despawn) {
                foreach (Actor actor in actors) {
                    if (actor.Active && ActorLoader.Actors != null && actor.WhoAmI >= 0 && actor.WhoAmI < ActorLoader.Actors.Length
                        && ReferenceEquals(ActorLoader.Actors[actor.WhoAmI], actor)) {
                        ActorLoader.KillActor(actor.WhoAmI, network: false);
                    }
                }
            }
            actors.Clear();
        }
    }
}
