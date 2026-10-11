using InnoVault.Cinematics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using Terraria;
using Terraria.ModLoader;

namespace InnoVault.Scenarios
{
    //区域进出判定：每个游戏帧按本地玩家中心所在的格子更新各区域的 Engaged，进出改变了参与合成的组件时重建快照，
    //再派发进入、离开回调。约束型区域（Contain）进入后不再离开
    internal static class ScenarioAreaTracker
    {
        private static readonly List<(ScenarioArea Area, bool Entered)> _events = [];

        internal static void Update() {
            if (!ScenarioDirector.InSession || ScenarioDirector.InBackendTravel) {
                return;
            }
            Player player = Main.LocalPlayer;
            if (player == null || !player.active) {
                return;
            }
            Point tile = player.Center.ToTileCoordinates();
            ScenarioArea[] areas = ScenarioRuntime.Areas;
            _events.Clear();
            for (int i = 0; i < areas.Length; i++) {
                ScenarioArea area = areas[i];
                if (area.Scenario?.Status != ScenarioStatus.Active) {
                    continue;
                }
                bool inside = area.Engaged
                    ? area.ExitBehavior == ScenarioAreaExit.Contain || area.IsNear(tile.X, tile.Y, area.ExitMargin)
                    : area.Contains(tile);
                if (inside != area.Engaged) {
                    area.Engaged = inside;
                    _events.Add((area, inside));
                }
            }
            if (_events.Count == 0) {
                return;
            }
            ScenarioDirector.RefreshRuntime();
            foreach (var (area, entered) in _events) {
                VaultScenario scenario = area.Scenario;
                if (entered) {
                    try {
                        area.InvokePlayerEnter(player);
                    } catch (Exception ex) {
                        ScenarioPlatform.Error($"Scenario.{scenario?.Key}.OnPlayerEnter", $"[Scenarios] {scenario?.Key} OnPlayerEnter threw: {ex}");
                    }
                    if (scenario?.GetComponent<ScenarioCinematics>()?.AreaEnterClip is Type clip) {
                        CutsceneDirector.Play(clip, player);
                    }
                    continue;
                }
                try {
                    area.InvokePlayerExit(player);
                } catch (Exception ex) {
                    ScenarioPlatform.Error($"Scenario.{scenario?.Key}.OnPlayerExit", $"[Scenarios] {scenario?.Key} OnPlayerExit threw: {ex}");
                }
                if (area.ExitBehavior == ScenarioAreaExit.Deactivate && scenario?.Status == ScenarioStatus.Active) {
                    ScenarioResult result = ScenarioDirector.Deactivate(scenario);
                    if (!result.Accepted) {
                        ScenarioPlatform.Info($"[Scenarios] Deactivating {scenario.Key} on area exit was rejected: {result}");
                    }
                }
            }
            _events.Clear();
        }
    }

    //本地玩家：离场期间锁住输入；约束型区域把玩家留在里面
    internal sealed class ScenarioAreaPlayer : ModPlayer
    {
        //上一帧仍在所有约束区域里的位置
        private Vector2? _lastInside;

        public override void SetControls() {
            if (Player.whoAmI != Main.myPlayer || ScenarioDirector.TravelPhase != ScenarioTravelPhase.Departing) {
                return;
            }
            Player.controlLeft = false;
            Player.controlRight = false;
            Player.controlUp = false;
            Player.controlDown = false;
            Player.controlJump = false;
            Player.controlUseItem = false;
            Player.controlUseTile = false;
            Player.controlHook = false;
            Player.controlThrow = false;
            Player.controlMount = false;
            Player.controlQuickHeal = false;
            Player.controlQuickMana = false;
            Player.controlSmart = false;
            Player.controlTorch = false;
        }

        //PostUpdate 在碰撞与位置更新之后
        public override void PostUpdate() {
            if (Player.whoAmI != Main.myPlayer || !ScenarioDirector.InSession || Player.dead) {
                return;
            }
            ScenarioArea violated = null;
            Point tile = Player.Center.ToTileCoordinates();
            ScenarioArea[] areas = ScenarioRuntime.Areas;
            for (int i = 0; i < areas.Length; i++) {
                ScenarioArea area = areas[i];
                if (area.ExitBehavior == ScenarioAreaExit.Contain && area.Engaged
                    && area.Scenario?.Status == ScenarioStatus.Active && !area.Contains(tile)) {
                    violated = area;
                    break;
                }
            }
            if (violated == null) {
                _lastInside = Player.position;
                return;
            }
            if (violated.ContainMode == ScenarioContainMode.ReturnToAnchor
                && ScenarioDirector.TryFindAnchor(violated.ContainAnchor ?? ScenarioAnchors.Entry, out Point anchor)) {
                ScenarioPlacement.PlaceAtTile(Player, anchor);
                _lastInside = Player.position;
                return;
            }
            if (_lastInside is Vector2 back) {
                Player.position = back;
                Player.velocity = Vector2.Zero;
            }
        }

        public override void OnEnterWorld() => _lastInside = null;
    }
}
