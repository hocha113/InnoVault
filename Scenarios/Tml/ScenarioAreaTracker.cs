using InnoVault.Cinematics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using Terraria;
using Terraria.ModLoader;

namespace InnoVault.Scenarios
{
    //区域进出判定：每个游戏帧按各玩家中心所在的格子更新各区域的参与状态，进出改变了参与合成的组件时重建快照，
    //再派发进入、离开回调。约束型区域（Contain）进入后不再离开。
    //多人下每一端都按自己看到的位置判定所有玩家：服务器据此执行逐玩家的规则，客户端据此推算其他玩家，不经网络同步
    internal static class ScenarioAreaTracker
    {
        private static readonly List<(ScenarioArea Area, int Player, bool Entered)> _events = [];

        internal static void Update() {
            if (!ScenarioDirector.InSession || ScenarioDirector.InBackendTravel) {
                return;
            }
            ScenarioArea[] areas = ScenarioRuntime.Areas;
            if (areas.Length == 0) {
                return;
            }
            int local = Main.dedServ ? -1 : Main.myPlayer;
            _events.Clear();
            for (int p = 0; p < Main.maxPlayers; p++) {
                Player player = Main.player[p];
                bool present = player != null && player.active;
                Point tile = present ? player.Center.ToTileCoordinates() : default;
                for (int i = 0; i < areas.Length; i++) {
                    ScenarioArea area = areas[i];
                    if (area.Scenario?.Status != ScenarioStatus.Active) {
                        continue;
                    }
                    bool engaged = area.IsEngaged(p);
                    if (!present && !engaged) {
                        continue;
                    }
                    bool inside = present && (engaged
                        ? area.ExitBehavior == ScenarioAreaExit.Contain || area.IsNear(tile.X, tile.Y, area.ExitMargin)
                        : area.Contains(tile));
                    if (inside != engaged) {
                        area.SetEngaged(p, inside);
                        _events.Add((area, p, inside));
                    }
                }
            }
            if (_events.Count == 0) {
                return;
            }
            ScenarioDirector.RefreshRuntime();
            foreach (var (area, index, entered) in _events) {
                VaultScenario scenario = area.Scenario;
                Player player = Main.player[index];
                if (entered) {
                    try {
                        area.InvokePlayerEnter(player);
                    } catch (Exception ex) {
                        ScenarioPlatform.Error($"Scenario.{scenario?.Key}.OnPlayerEnter", $"[Scenarios] {scenario?.Key} OnPlayerEnter threw: {ex}");
                    }
                    if (index == local && scenario?.GetComponent<ScenarioCinematics>()?.AreaEnterClip is Type clip) {
                        CutsceneDirector.Play(clip, player);
                    }
                    continue;
                }
                try {
                    area.InvokePlayerExit(player);
                } catch (Exception ex) {
                    ScenarioPlatform.Error($"Scenario.{scenario?.Key}.OnPlayerExit", $"[Scenarios] {scenario?.Key} OnPlayerExit threw: {ex}");
                }
                //离开即停用：由裁决端决定，等最后一个玩家也离开之后才停用
                if (area.ExitBehavior == ScenarioAreaExit.Deactivate && scenario?.Status == ScenarioStatus.Active
                    && ScenarioDirector.IsAuthority && !area.AnyEngaged) {
                    ScenarioResult result = ScenarioDirector.Deactivate(scenario);
                    if (!result.Accepted) {
                        ScenarioPlatform.Info($"[Scenarios] Deactivating {scenario.Key} on area exit was rejected: {result}");
                    }
                }
            }
            _events.Clear();
        }
    }

    //本地玩家：换舞台期间锁住输入；约束型区域把玩家留在里面
    internal sealed class ScenarioAreaPlayer : ModPlayer
    {
        //上一帧仍在所有约束区域里的位置
        private Vector2? _lastInside;

        public override void SetControls() {
            if (Player.whoAmI != Main.myPlayer || !ScenarioDirector.InputLocked) {
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

        //多人换舞台时本地玩家要盖着幕布等其他玩家（离场时等所有人合上幕布，抵达后等所有人到齐）：这段时间看不见也动不了，不该受伤。
        //伤害由受击玩家自己的客户端判定，所以只在本地玩家身上做
        public override void PreUpdate() {
            if (Player.whoAmI != Main.myPlayer || Main.dedServ || !ScenarioDirector.Blindfolded) {
                return;
            }
            Player.immune = true;
            Player.immuneNoBlink = true;
            Player.immuneTime = Math.Max(Player.immuneTime, 2);
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
