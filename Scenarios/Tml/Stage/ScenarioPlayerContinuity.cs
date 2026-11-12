using System;
using System.IO;
using System.Reflection;
using System.Text;
using Terraria;
using Terraria.GameContent.Creative;

namespace InnoVault.Scenarios
{
    //角色贯穿整个会话：换舞台时不换角色对象，没写进存档的状态（模组的 ModPlayer 字段、选中的快捷栏、坐骑、增益）原样带过去。
    //原版进世界用的总是刚从文件读出来的角色，有两件事靠这一点顺带做掉，沿用对象时要自己做（对照 Terraria/Player.cs）：
    //①Spawn(SpawningIntoWorld) 末尾的 SetPlayerDataToOutOfClassFields 会按读取角色文件那一刻的快照覆盖光标、旅途研究槽、向导槽、重铸槽里的物品，
    //  以及逐玩家的旅途能力（无敌、放置距离、刷怪倍率）：进新舞台之前把快照换成当前的内容，否则物品会复制或丢失；
    //②角色身上指向旧舞台的东西（打开的箱子、告示牌、对话中的 NPC、物块实体锚点、跟踪着的弹幕、回归药水的传送门、挖到一半的物块记录）要清掉：
    //  它们记的是编号与坐标，到了新舞台会指到不相干的对象上
    internal static class ScenarioPlayerContinuity
    {
        //Player._temporaryItemSlots 是 internal：读角色文件那一刻光标、旅途研究槽、向导槽、重铸槽里的物品
        private static readonly FieldInfo _temporaryItemSlots = typeof(Player).GetField("_temporaryItemSlots", BindingFlags.NonPublic | BindingFlags.Instance);

        //离开旧舞台之前在主线程调用，此刻光标物品与旅途能力都还是离开那一刻的样子（清空世界会把旅途能力复位）。
        //返回假表示物品快照换不了，调用方应当退回到从存档重新读取角色
        internal static bool Snapshot(Player player) {
            if (player == null || _temporaryItemSlots?.GetValue(player) is not Item[] { Length: >= 4 } slots) {
                ScenarioPlatform.Error("Scenario.TemporarySlots", "[Scenarios] Player._temporaryItemSlots was not found; the character cannot be carried across stages as it is");
                return false;
            }
            slots[0] = CloneOrNull(Main.mouseItem);
            slots[1] = CloneOrNull(Main.CreativeMenu.GetItemByIndex(0));
            slots[2] = CloneOrNull(Main.guideItem);
            slots[3] = CloneOrNull(Main.reforgeItem);
            SnapshotCreativePowers(player);
            return true;
        }

        //旧舞台已经卸载、新舞台还没进入时在主线程调用。多次调用无害
        internal static void Detach(Player player) {
            if (player == null) {
                return;
            }
            player.chest = -1;
            player.editedChestName = false;
            player.sign = -1;
            player.SetTalkNPC(-1);
            player.tileEntityAnchor.Clear();
            player.piggyBankProjTracker.Clear();
            player.voidLensChest.Clear();
            player.PotionOfReturnOriginalUsePosition = null;
            player.PotionOfReturnHomePosition = null;
            player.showLastDeath = false;
            player.hitTile = new HitTile();
            player.hitReplace = new HitTile();
            player.channel = false;
            player.heldProj = -1;
            player.itemAnimation = 0;
            player.itemTime = 0;
            player.reuseDelay = 0;
            player.grappling[0] = -1;
            player.grapCount = 0;
            player.pulley = false;
            //倒下期间被带走时，原版进世界会按“距上次存档过了多久”扣复活倒计时，扣到零就直接改回活着（不派发复活钩子）。
            //这个时刻记到以后去，那一步就什么都不做：倒计时原样带到新舞台，复活由抵达流程或倒计时自己走完，钩子照常派发。这个字段只在那一处读取，存角色时写的是当时的时刻
            player.lastTimePlayerWasSaved = DateTime.UtcNow.AddDays(1).ToBinary();
            if (player.whoAmI == Main.myPlayer) {
                Main.npcChatText = string.Empty;
                Main.npcChatCornerItem = 0;
                Main.editSign = false;
                Main.editChest = false;
            }
        }

        //逐玩家的旅途能力记在能力管理器里，出生时按角色上的快照套用：照存读角色文件的格式在内存里走一遍，把快照换成现状
        private static void SnapshotCreativePowers(Player player) {
            try {
                player.savedPerPlayerFieldsThatArentInThePlayerClass ??= new Player.SavedPlayerDataWithAnnoyingRules();
                using MemoryStream stream = new();
                using (BinaryWriter writer = new(stream, Encoding.UTF8, leaveOpen: true)) {
                    CreativePowerManager.Instance.SaveToPlayer(player, writer);
                }
                stream.Position = 0;
                using BinaryReader reader = new(stream);
                CreativePowerManager.Instance.LoadToPlayer(player, reader, Main.curRelease);
            } catch (Exception ex) {
                ScenarioPlatform.Error("Scenario.CreativePowers", $"[Scenarios] Carrying the per-player journey powers across the stage change threw: {ex}");
            }
        }

        private static Item CloneOrNull(Item item) => item == null || item.IsAir ? null : item.Clone();
    }
}
