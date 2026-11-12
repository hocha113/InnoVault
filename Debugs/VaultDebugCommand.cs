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
    /// <c>/vaultdebug scenario list|status|tree|takeovers [dump]|travel &lt;key&gt; [anchor]|home [anchor]|activate &lt;key&gt;|deactivate &lt;key&gt;|anchors|node &lt;path&gt; on|off|option [name on|off]|tilehash &lt;x&gt; &lt;y&gt; &lt;w&gt; &lt;h&gt;</c>：
    /// 查看与操作场景框架，key 可以写完整的 <c>ModName/TypeName</c>，也可以只写类型名（不区分大小写）；tree 打印场景树与各场景的节点树，
    /// takeovers 列出各项原版接管的状态，加 dump 时重装一遍并把每一项 IL 改写的最终指令转储到日志目录，
    /// node 按路径开关一个节点，option 查看或切换换舞台后端的开发者开关（directentry、keepplayer），tilehash 给区域还原做逐格比对
    /// </summary>
    public class VaultDebugCommand : ModCommand
    {
        private const string ScenarioUsage = "/vaultdebug scenario list|status|tree|takeovers [dump]|travel <key> [anchor]|home [anchor]|activate <key>|deactivate <key>|anchors|node <path> on|off|option [name on|off]|tilehash <x> <y> <w> <h>";

        /// <inheritdoc/>
        public override string Command => "vaultdebug";
        /// <inheritdoc/>
        public override string Description => "vaultdebug";
        /// <inheritdoc/>
        public override string Usage => "/vaultdebug [scenario list|status|tree|takeovers [dump]|travel <key> [anchor]|home [anchor]|activate <key>|deactivate <key>|anchors|node <path> on|off|option [name on|off]]";
        /// <inheritdoc/>
        public override CommandType Type => CommandType.Chat;
        /// <summary>
        /// 保留输入的大小写：tML 缺省把整句指令转成小写，节点路径是区分大小写的，转了就对不上。子指令、场景名与开关词在下面各自按不区分大小写比较
        /// </summary>
        public override bool IsCaseSensitive => true;
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
                    caller.Reply($"session={ScenarioDirector.InSession} authority={ScenarioDirector.IsAuthority} traveling={ScenarioDirector.IsTraveling} phase={ScenarioDirector.TravelPhase} availability={ScenarioDirector.Availability}", Color.LightGray);
                    caller.Reply($"stage={ScenarioDirector.Stage?.Key ?? "-"} loaded={ScenarioDirector.LoadedStage?.Key ?? "-"} root={ScenarioDirector.Root?.Key ?? "-"} "
                        + $"world={Main.maxTilesX}x{Main.maxTilesY} bottomRow={(int)(Main.bottomWorld / 16f)} seed={ScenarioDirector.Stage?.Stage?.ResolvedSeed ?? 0}", Color.LightGray);
                    foreach (VaultScenario active in ScenarioDirector.Active) {
                        string area = active.Area == null ? string.Empty : $" area={active.Area.Bounds} engaged={active.Area.Engaged} anyEngaged={active.Area.AnyEngaged}";
                        caller.Reply($"  {active.Key} #{active.NetId} [{active.Status}] parent={active.Parent?.Key ?? "-"} ticks={active.ActiveTicks}{area}", Color.LightGray);
                    }
                    break;
                case "tree":
                    foreach (VaultScenario active in ScenarioDirector.Active) {
                        if (active.Parent != null) {
                            continue;
                        }
                        PrintScenario(caller, active, 0);
                    }
                    break;
                case "node": {
                    if (args.Length < 3 || !TryParseSwitch(args[2], out bool enable)) {
                        caller.Reply("/vaultdebug scenario node <path> on|off", Color.Orange);
                        break;
                    }
                    ScenarioNode node = null;
                    foreach (VaultScenario active in ScenarioDirector.Active) {
                        node = active.GetNode<ScenarioNode>(key);
                        if (node != null) {
                            break;
                        }
                    }
                    if (node == null) {
                        caller.Reply($"no node at '{key}' in any active scenario (paths are case-sensitive; see /vaultdebug scenario tree)", Color.Orange);
                        break;
                    }
                    node.Enabled = enable;
                    //节点开关由权威端同步：联机客户端上改的只是本地这一份，服务器下次同步这个场景时会盖回去
                    string local = ScenarioDirector.IsAuthority ? string.Empty : " (local only: the server owns node switches)";
                    caller.Reply($"{node.Scenario?.Key}: {node.Path} enabled={node.Enabled}{local}", Color.LightGreen);
                    break;
                }
                case "option": {
                    if (args.Length >= 3 && TryParseSwitch(args[2], out bool value)) {
                        switch (key.ToLowerInvariant()) {
                            case "directentry":
                                ScenarioTuning.DirectEntry = value;
                                break;
                            case "keepplayer":
                                ScenarioTuning.KeepPlayer = value;
                                break;
                            default:
                                caller.Reply("/vaultdebug scenario option directentry|keepplayer on|off", Color.Orange);
                                break;
                        }
                    }
                    caller.Reply($"directentry={(ScenarioTuning.DirectEntry ? "on" : "off")} keepplayer={(ScenarioTuning.KeepPlayer ? "on" : "off")}", Color.LightGray);
                    break;
                }
                case "travel":
                    Report(caller, ScenarioDirector.Travel(Resolve(caller, key), args.Length > 2 ? args[2] : null));
                    break;
                case "home":
                    Report(caller, ScenarioDirector.ReturnHome(key));
                    break;
                case "takeovers":
                    //dump：全部卸下重装一遍，每一项 IL 改写的最终指令转储到日志目录的 ILDumps/InnoVault 下
                    if (key != null && key.Equals("dump", StringComparison.OrdinalIgnoreCase)) {
                        caller.Reply(ScenarioTakeovers.ReinstallWithDump()
                            ? "takeovers reinstalled; IL written to tModLoader-Logs/ILDumps/InnoVault"
                            : "takeovers are not installed yet", Color.LightGray);
                    }
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
                        foreach (ScenarioAnchors anchors in active.GetNodes<ScenarioAnchors>()) {
                            string state = anchors.EnabledInTree ? string.Empty : " (off)";
                            foreach (var pair in anchors.Points) {
                                caller.Reply($"  {active.Key}: {pair.Key} = {pair.Value}{state}", Color.LightGray);
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

        //场景树：场景、它的节点树，然后是子场景
        private static void PrintScenario(CommandCaller caller, VaultScenario scenario, int depth) {
            string indent = new(' ', depth * 2);
            caller.Reply($"{indent}{scenario.Key} #{scenario.NetId} [{scenario.Status}]", Color.LightGreen);
            foreach (ScenarioNode node in scenario.Nodes) {
                PrintNode(caller, node, depth + 1);
            }
            foreach (VaultScenario child in scenario.Children) {
                PrintScenario(caller, child, depth + 1);
            }
        }

        private static void PrintNode(CommandCaller caller, ScenarioNode node, int depth) {
            string scope = node.ScopeArea == null ? string.Empty : $" scope={node.ScopeArea.Path}";
            string state = node.EnabledInTree ? string.Empty : node.Enabled ? " (off: an ancestor is off)" : " (off)";
            caller.Reply($"{new string(' ', depth * 2)}{node.Name} : {node.GetType().Name}{scope}{state}", node.EnabledInTree ? Color.LightGray : Color.Gray);
            foreach (ScenarioNode child in node.Children) {
                PrintNode(caller, child, depth + 1);
            }
        }

        private static bool TryParseSwitch(string text, out bool value) {
            switch (text?.ToLowerInvariant()) {
                case "on":
                case "true":
                case "1":
                    value = true;
                    return true;
                case "off":
                case "false":
                case "0":
                    value = false;
                    return true;
                default:
                    value = false;
                    return false;
            }
        }

        private static VaultScenario Resolve(CommandCaller caller, string key) {
            if (string.IsNullOrEmpty(key)) {
                caller.Reply("missing scenario key", Color.Orange);
                return null;
            }
            VaultScenario template = VaultScenario.FindTemplate(key)
                ?? VaultScenario.Templates.FirstOrDefault(t => t.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                ?? VaultScenario.Templates.FirstOrDefault(t => t.Name.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (template == null) {
                caller.Reply($"no scenario named '{key}'", Color.Orange);
            }
            return template;
        }

        private static bool _echoingRejections;

        private static void Report(CommandCaller caller, ScenarioResult result) {
            if (result.Pending) {
                //多人客户端：请求已经发给服务器，结果随场景树的同步到达；被拒绝时由下面订阅的事件回显
                EchoRejections();
                caller.Reply(result.ToString(), Color.LightGray);
                return;
            }
            if (!result.Accepted && result.Reason == ScenarioRejectReason.MultiplayerUnsupported) {
                caller.Reply(ScenarioText.MultiplayerUnsupported.Value, Color.Orange);
                return;
            }
            caller.Reply(result.ToString(), result.Accepted ? Color.LightGreen : Color.Orange);
        }

        //导演层在模组卸载时清空订阅者，这里的静态标记随程序集一起重置
        private static void EchoRejections() {
            if (_echoingRejections) {
                return;
            }
            _echoingRejections = true;
            ScenarioDirector.RequestRejected += static (kind, template, result)
                => Main.NewText($"[vaultdebug] {kind} {template?.Key ?? "?"}: {result}", Color.Orange);
        }
    }
}
