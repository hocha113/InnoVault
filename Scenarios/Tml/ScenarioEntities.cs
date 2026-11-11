using InnoVault.Actors;
using System.Collections.Generic;
using System.IO;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace InnoVault.Scenarios
{
    //NPC 的场景归属。实例跟着 NPC 走：槽位被复用时 SetDefaults 重建全局实例，旧归属自然消失。
    //多人下 NPC 由服务器生成，归属跟着 NPC 的同步包发给客户端：一个比特表示有没有归属，有的话再跟场景实例的网络编号
    internal sealed class ScenarioOwnedNPC : GlobalNPC
    {
        private VaultScenario _owner;
        //客户端收到的归属编号。NPC 的同步包可能先于场景的激活消息到达，第一次读取归属时再按编号查找
        private int _pendingOwner;

        public override bool InstancePerEntity => true;

        internal VaultScenario Owner {
            get {
                if (_owner == null && _pendingOwner != 0) {
                    _owner = ScenarioDirector.FindByNetId(_pendingOwner);
                    if (_owner != null) {
                        _pendingOwner = 0;
                    }
                }
                return _owner;
            }
            set {
                _owner = value;
                _pendingOwner = 0;
            }
        }

        public override void OnSpawn(NPC npc, IEntitySource source) {
            Owner = ScenarioEntities.OwnerOf(source);
        }

        public override void SendExtraAI(NPC npc, BitWriter bitWriter, BinaryWriter binaryWriter) {
            int id = _owner?.NetId ?? 0;
            bitWriter.WriteBit(id != 0);
            if (id != 0) {
                binaryWriter.Write7BitEncodedInt(id);
            }
        }

        public override void ReceiveExtraAI(NPC npc, BitReader bitReader, BinaryReader binaryReader) {
            int id = bitReader.ReadBit() ? binaryReader.Read7BitEncodedInt() : 0;
            //归属只由服务器说了算
            if (Main.netMode != NetmodeID.MultiplayerClient) {
                return;
            }
            _owner = null;
            _pendingOwner = id;
        }
    }

    //弹幕的场景归属。归属只记在生成它的那一端，不随弹幕同步：弹幕的同步包极其频繁，而清除弹幕本来就只能由生成它的那一端来做
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

        //场景停用完毕时由导演层经平台接缝调用，每一端都会到这里。
        //换舞台途中不碰实体：单人与客户端在菜单里，世界已经卸载；服务器马上就要清空整个世界。
        //多人下 NPC 与 Actor 由服务器清除后同步，弹幕由生成它的那一端清除后同步
        internal static void CleanUp(VaultScenario scenario) {
            if (Main.gameMenu || (Main.dedServ && ScenarioDirector.InBackendTravel)) {
                return;
            }
            bool despawn = scenario.OwnedCleanup == ScenarioOwnedCleanup.Despawn;
            bool multiplayer = Main.netMode != NetmodeID.SinglePlayer;
            bool client = Main.netMode == NetmodeID.MultiplayerClient;
            for (int i = 0; i < Main.maxNPCs; i++) {
                NPC npc = Main.npc[i];
                if (!npc.active || !npc.TryGetGlobalNPC(out ScenarioOwnedNPC owned) || !ReferenceEquals(owned.Owner, scenario)) {
                    continue;
                }
                owned.Owner = null;
                if (!despawn || client) {
                    continue;
                }
                npc.active = false;
                if (multiplayer) {
                    //与原版 NPC.CheckActive 的消失写法一致：客户端收到生命为 0 的同步包后直接移除，不触发死亡效果
                    npc.netSkip = -1;
                    npc.life = 0;
                    NetMessage.SendData(MessageID.SyncNPC, -1, -1, null, i);
                }
            }
            for (int i = 0; i < Main.maxProjectiles; i++) {
                Projectile projectile = Main.projectile[i];
                if (!projectile.active || !projectile.TryGetGlobalProjectile(out ScenarioOwnedProjectile owned) || !ReferenceEquals(owned.Owner, scenario)) {
                    continue;
                }
                owned.Owner = null;
                if (!despawn || (multiplayer && projectile.owner != Main.myPlayer)) {
                    continue;
                }
                projectile.active = false;
                if (multiplayer) {
                    //原版没有静默移除弹幕的消息，其他端经 KillProjectile 移除，会在那边播放它的消失效果
                    NetMessage.SendData(MessageID.KillProjectile, -1, -1, null, projectile.identity, projectile.owner);
                }
            }
            List<Actor> actors = scenario.OwnedActors;
            if (actors == null) {
                return;
            }
            if (despawn && !client) {
                foreach (Actor actor in actors) {
                    if (actor.Active && ActorLoader.Actors != null && actor.WhoAmI >= 0 && actor.WhoAmI < ActorLoader.Actors.Length
                        && ReferenceEquals(ActorLoader.Actors[actor.WhoAmI], actor)) {
                        ActorLoader.KillActor(actor.WhoAmI, network: multiplayer);
                    }
                }
            }
            actors.Clear();
        }
    }
}
