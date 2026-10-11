using InnoVault.Scenarios;
using Microsoft.Xna.Framework;
using System;
using System.Linq;
using System.Security.Cryptography;
using Terraria;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace InnoVault.Debugs
{
    /// <summary>
    /// InnoVault 开发者指令<br/>
    /// <c>/vaultdebug</c>：打开开发者调试面板<br/>
    /// <c>/vaultdebug scenario list|status|takeovers|travel &lt;key&gt; [anchor]|home [anchor]|activate &lt;key&gt;|deactivate &lt;key&gt;|anchors|tilehash &lt;x&gt; &lt;y&gt; &lt;w&gt; &lt;h&gt;</c>：
    /// 查看与操作场景框架，key 可以写完整的 <c>ModName/TypeName</c>，也可以只写类型名（不区分大小写）；tilehash 给区域还原做逐格比对
    /// </summary>
    public class VaultDebugCommand : ModCommand
    {
        private const string ScenarioUsage = "/vaultdebug scenario list|status|takeovers|travel <key> [anchor]|home [anchor]|activate <key>|deactivate <key>|anchors|tilehash <x> <y> <w> <h>";

        /// <inheritdoc/>
        public override string Command => "vaultdebug";
        /// <inheritdoc/>
        public override string Description => "vaultdebug";
        /// <inheritdoc/>
        public override string Usage => "/vaultdebug [scenario list|status|takeovers|travel <key> [anchor]|home [anchor]|activate <key>|deactivate <key>|anchors]";
        /// <inheritdoc/>
        public override CommandType Type => CommandType.Chat;
        /// <inheritdoc/>
        public override void Action(CommandCaller caller, string input, string[] args) {
            if (Main.dedServ) {
                return;
            }
            if (args.Length == 0) {
                DeveloperPanelUI.Instance?.Toggle();
                return;
            }
            if (args[0].Equals("scenario", StringComparison.OrdinalIgnoreCase)) {
                ScenarioAction(caller, args.Skip(1).ToArray());
                return;
            }
            caller.Reply(Usage, Color.Orange);
        }

        private static void ScenarioAction(CommandCaller caller, string[] args) {
            string verb = args.Length > 0 ? args[0].ToLowerInvariant() : "status";
            string key = args.Length > 1 ? args[1] : null;
            switch (verb) {
                case "list":
                    foreach (VaultScenario template in VaultScenario.Templates.OrderBy(t => t.Key)) {
                        caller.Reply(template.Key, Color.LightGray);
                    }
                    break;
                case "status":
                    caller.Reply($"session={ScenarioDirector.InSession} traveling={ScenarioDirector.IsTraveling} phase={ScenarioDirector.TravelPhase} availability={ScenarioDirector.Availability}", Color.LightGray);
                    caller.Reply($"stage={ScenarioDirector.Stage?.Key ?? "-"} root={ScenarioDirector.Root?.Key ?? "-"}", Color.LightGray);
                    foreach (VaultScenario active in ScenarioDirector.Active) {
                        string area = active.Area == null ? string.Empty : $" area={active.Area.Bounds} engaged={active.Area.Engaged}";
                        caller.Reply($"  {active.Key} [{active.Status}] parent={active.Parent?.Key ?? "-"} ticks={active.ActiveTicks}{area}", Color.LightGray);
                    }
                    break;
                case "travel":
                    Report(caller, ScenarioDirector.Travel(Resolve(caller, key), args.Length > 2 ? args[2] : null));
                    break;
                case "home":
                    Report(caller, ScenarioDirector.ReturnHome(key));
                    break;
                case "takeovers":
                    foreach (ScenarioTakeover takeover in ScenarioTakeovers.All) {
                        string detail = takeover.Failure == null ? string.Empty : $" ({takeover.Failure})";
                        caller.Reply($"{takeover.Name}: {takeover.State}{detail}",
                            takeover.State == ScenarioTakeoverState.Failed ? Color.Orange : Color.LightGray);
                    }
                    break;
                case "activate":
                    Report(caller, ScenarioDirector.Activate(Resolve(caller, key)));
                    break;
                case "deactivate": {
                    VaultScenario template = Resolve(caller, key);
                    VaultScenario instance = template == null
                        ? null
                        : ScenarioDirector.Active.LastOrDefault(s => ReferenceEquals(s.Template, template));
                    Report(caller, ScenarioDirector.Deactivate(instance));
                    break;
                }
                case "anchors":
                    foreach (VaultScenario active in ScenarioDirector.Active) {
                        if (active.GetComponent<ScenarioAnchors>() is ScenarioAnchors anchors) {
                            foreach (var pair in anchors.Points) {
                                caller.Reply($"  {active.Key}: {pair.Key} = {pair.Value}", Color.LightGray);
                            }
                        }
                    }
                    break;
                case "tilehash":
                    TileHash(caller, args);
                    break;
                default:
                    caller.Reply(ScenarioUsage, Color.Orange);
                    break;
            }
        }

        //区域还原的逐格比对：对一块范围里全部已注册的格数据算哈希，附带箱子、告示牌、物块实体、TP 的数量
        private static void TileHash(CommandCaller caller, string[] args) {
            if (args.Length < 5 || !int.TryParse(args[1], out int x) || !int.TryParse(args[2], out int y)
                || !int.TryParse(args[3], out int w) || !int.TryParse(args[4], out int h)) {
                caller.Reply("/vaultdebug scenario tilehash <x> <y> <width> <height>", Color.Orange);
                return;
            }
            Rectangle rect = Rectangle.Intersect(new Rectangle(x, y, w, h), new Rectangle(0, 0, Main.maxTilesX, Main.maxTilesY));
            if (rect.Width <= 0 || rect.Height <= 0) {
                caller.Reply("the area is outside the stage", Color.Orange);
                return;
            }
            TagCompound snapshot = ScenarioRestoreSnapshot.Capture(rect);
            using SHA256 sha = SHA256.Create();
            foreach (TagCompound layer in snapshot.GetList<TagCompound>("data")) {
                byte[] bytes = layer.GetByteArray("bytes");
                sha.TransformBlock(bytes, 0, bytes.Length, null, 0);
            }
            sha.TransformFinalBlock([], 0, 0);
            string hash = Convert.ToHexString(sha.Hash)[..16];
            caller.Reply($"tilehash {rect} = {hash} layers={snapshot.GetList<TagCompound>("data").Count} chests={snapshot.GetList<TagCompound>("chests").Count} "
                + $"signs={snapshot.GetList<TagCompound>("signs").Count} entities={snapshot.GetList<TagCompound>("entities").Count} tps={snapshot.GetList<TagCompound>("tps").Count}",
                Color.LightGray);
        }

        private static VaultScenario Resolve(CommandCaller caller, string key) {
            if (string.IsNullOrEmpty(key)) {
                caller.Reply("missing scenario key", Color.Orange);
                return null;
            }
            VaultScenario template = VaultScenario.FindTemplate(key)
                ?? VaultScenario.Templates.FirstOrDefault(t => t.Name.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (template == null) {
                caller.Reply($"no scenario named '{key}'", Color.Orange);
            }
            return template;
        }

        private static void Report(CommandCaller caller, ScenarioResult result) {
            if (!result.Accepted && result.Reason == ScenarioRejectReason.MultiplayerUnsupported) {
                caller.Reply(ScenarioText.MultiplayerUnsupported.Value, Color.Orange);
                return;
            }
            caller.Reply(result.ToString(), result.Accepted ? Color.LightGreen : Color.Orange);
        }
    }
}
