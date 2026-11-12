using Microsoft.Xna.Framework;
using System;
using Terraria;
using Terraria.ID;

namespace InnoVault.Scenarios
{
    //城镇 NPC 入住：规则禁止时不调用 orig。该方法只负责入住判定，尾部没有模组钩子的派发点
    internal sealed class TownNPCTakeover : ScenarioTakeover
    {
        internal override string Name => "TownNPCs";

        internal override string Source => "Terraria/Main.cs UpdateTime_SpawnTownNPCs (private static, called from the night branch of UpdateTime)";

        protected override void Install() => On_Main.UpdateTime_SpawnTownNPCs += Hook;

        protected override void Uninstall() => On_Main.UpdateTime_SpawnTownNPCs -= Hook;

        private static void Hook(On_Main.orig_UpdateTime_SpawnTownNPCs orig) {
            if (ScenarioRuntime.Rules.Pick(static r => r.NoTownNPCs) == true) {
                return;
            }
            orig();
        }
    }

    //昼夜交替时的事件：stopEvents 只拦血月、日食、开局首领、入侵等；陨石、派对、灯笼夜在检查之前就决定了，由每帧规则清掉
    internal sealed class WorldEventTakeover : ScenarioTakeover
    {
        internal override string Name => "WorldEvents";

        internal override string Source => "Terraria/Main.cs UpdateTime_StartDay / UpdateTime_StartNight (ref bool stopEvents)";

        protected override void Install() {
            On_Main.UpdateTime_StartDay += StartDay;
            On_Main.UpdateTime_StartNight += StartNight;
        }

        protected override void Uninstall() {
            On_Main.UpdateTime_StartDay -= StartDay;
            On_Main.UpdateTime_StartNight -= StartNight;
        }

        private static void StartDay(On_Main.orig_UpdateTime_StartDay orig, ref bool stopEvents) {
            if (ScenarioRuntime.Rules.Pick(static r => r.NoEvents) == true) {
                stopEvents = true;
            }
            orig(ref stopEvents);
        }

        private static void StartNight(On_Main.orig_UpdateTime_StartNight orig, ref bool stopEvents) {
            if (ScenarioRuntime.Rules.Pick(static r => r.NoEvents) == true) {
                stopEvents = true;
            }
            orig(ref stopEvents);
        }
    }

    //回城与复活落点：先调用 orig（SpawningIntoWorld 分支末尾有 EnterWorld 等派发，不能跳过），再按规则改落点
    internal sealed class SpawnPlacementTakeover : ScenarioTakeover
    {
        internal override string Name => "SpawnPlacement";

        internal override string Source => "Terraria/Player.cs Spawn(PlayerSpawnContext)";

        protected override void Install() => On_Player.Spawn += Hook;

        protected override void Uninstall() => On_Player.Spawn -= Hook;

        private static void Hook(On_Player.orig_Spawn orig, Player self, PlayerSpawnContext context) {
            orig(self, context);
            //落点由出生的那个玩家自己的客户端决定，服务器与其他客户端收它的位置同步
            if (Main.dedServ || self.whoAmI != Main.myPlayer) {
                return;
            }
            //换舞台抵达时的那一次复活是换舞台的一部分，落点由换舞台决定，不套用复活落点规则
            if (ScenarioPlacement.ArrivalRevive) {
                return;
            }
            ScenarioLayers<ScenarioRules> rules = ScenarioRuntime.Rules;
            if (!rules.Any) {
                return;
            }
            ScenarioSpawnBehavior behavior;
            string anchor;
            Type destination;
            switch (context) {
                case PlayerSpawnContext.RecallFromItem:
                    behavior = rules.Pick(static r => r.RecallBehavior) ?? ScenarioSpawnBehavior.Vanilla;
                    anchor = rules.PickRef(static r => r.RecallAnchor);
                    destination = rules.PickRef(static r => r.RecallDestination);
                    break;
                case PlayerSpawnContext.ReviveFromDeath:
                    behavior = rules.Pick(static r => r.RespawnBehavior) ?? ScenarioSpawnBehavior.Vanilla;
                    anchor = rules.PickRef(static r => r.RespawnAnchor);
                    destination = rules.PickRef(static r => r.RespawnDestination);
                    break;
                default:
                    return;
            }
            switch (behavior) {
                case ScenarioSpawnBehavior.Anchor:
                    if (ScenarioDirector.TryFindAnchor(anchor ?? ScenarioAnchors.Entry, out Point tile)) {
                        ScenarioPlacement.PlaceAtTile(self, tile);
                        if (Main.netMode == NetmodeID.MultiplayerClient) {
                            NetMessage.SendData(MessageID.PlayerControls, -1, -1, null, self.whoAmI);
                        }
                    }
                    break;
                case ScenarioSpawnBehavior.ReturnHome:
                    if (ScenarioRuntime.InScenarioStage) {
                        ScenarioPlacement.QueueTravel(null, anchor);
                    }
                    break;
                case ScenarioSpawnBehavior.Travel:
                    if (destination != null) {
                        ScenarioPlacement.QueueTravel(destination, anchor);
                    }
                    break;
            }
        }
    }

    internal static class ScenarioPlacement
    {
        //换舞台抵达时后端正在让倒下的玩家复活：这一次 Spawn 不套用复活落点规则。只在主线程读写
        internal static bool ArrivalRevive { get; set; }

        //规则要求的换舞台：destination 为空是回主世界，否则换到那个场景类型的舞台；anchor 是目的地里的落点锚点
        internal static ScenarioResult StartTravel(Type destination, string anchor)
            => destination == null ? ScenarioDirector.ReturnHome(anchor) : ScenarioDirector.Travel(VaultScenario.FindTemplate(destination), anchor);

        //Spawn 可能在一帧更新的中途被调用，推到本帧结束后再发起切换。多人下这是发给服务器的请求，换的是所有人的舞台
        internal static void QueueTravel(Type destination, string anchor) {
            Main.QueueMainThreadAction(() => {
                ScenarioResult result = StartTravel(destination, anchor);
                if (!result.Accepted && !result.Pending) {
                    ScenarioPlatform.Info($"[Scenarios] The travel required by the spawn rule was rejected: {result}");
                }
            });
        }

        //玩家站在 tile 这一格上方
        internal static void PlaceAtTile(Player player, Point tile)
            => PlaceAt(player, new Vector2(tile.X * 16 + 8 - player.width / 2f, tile.Y * 16 - player.height));

        internal static void PlaceAt(Player player, Vector2 position) {
            player.position = position;
            player.oldPosition = position;
            player.velocity = Vector2.Zero;
            player.fallStart = player.fallStart2 = (int)(position.Y / 16f);
            Main.screenPosition = player.Center - new Vector2(Main.screenWidth, Main.screenHeight) / 2f;
            player.ForceUpdateBiomes();
        }
    }
}
