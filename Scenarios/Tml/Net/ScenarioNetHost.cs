using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace InnoVault.Scenarios
{
    //场景网络消息的收发：每条消息是 InnoVault 的一个 ModPacket，开头依次是 MessageType.Scenario 与消息编号
    internal sealed class TmlScenarioNetHost : IScenarioNetHost
    {
        public void SendToServer(ScenarioNetOp op, Action<BinaryWriter> body) {
            if (Main.netMode != NetmodeID.MultiplayerClient) {
                return;
            }
            ModPacket packet = Begin(op);
            body(packet);
            packet.Send();
        }

        //client 为 -1 时 ModPacket 只发给已经进入世界的客户端（NetMessage.buffer[i].broadcast），正在进服与换舞台在途的客户端收不到，它们在出生后经快照补齐
        public void SendToClient(int client, ScenarioNetOp op, Action<BinaryWriter> body) {
            if (Main.netMode != NetmodeID.Server || (client >= 0 && !IsConnected(client))) {
                return;
            }
            ModPacket packet = Begin(op);
            body(packet);
            packet.Send(client);
        }

        public void CollectClients(ICollection<int> clients) {
            if (Main.netMode != NetmodeID.Server) {
                return;
            }
            for (int i = 0; i < Main.maxPlayers; i++) {
                RemoteClient remote = Netplay.Clients[i];
                if (remote != null && remote.IsConnected() && remote.State == 10 && NetMessage.buffer[i].broadcast) {
                    clients.Add(i);
                }
            }
        }

        public bool IsConnected(int client)
            => client >= 0 && client < Main.maxPlayers && Netplay.Clients[client] is RemoteClient remote && remote.IsConnected();

        private static ModPacket Begin(ScenarioNetOp op) {
            ModPacket packet = VaultMod.Instance.GetPacket();
            packet.Write((byte)MessageType.Scenario);
            packet.Write((byte)op);
            return packet;
        }

        //VaultNetwork 读完 MessageType 之后交到这里
        internal static void Handle(BinaryReader reader, int whoAmI) {
            ScenarioNetOp op = (ScenarioNetOp)reader.ReadByte();
            //场景消息引发的物块改动（服务器执行请求时场景自己的搭建、客户端镜像的钩子）是场景代码在做，不算在发包的玩家头上
            ScenarioNetContext.Leave();
            ScenarioDirector.HandleNet(op, reader, whoAmI);
        }
    }

    //正在处理的网络包来自谁。物块钩子没有“是谁干的”这个参数：服务器靠它把客户端发来的改动算到那个玩家头上，
    //客户端靠它认出服务器已经裁决过、只需照做的改动
    internal static class ScenarioNetContext
    {
        //存的是发送方 + 1，缺省的 0 表示不在处理网络包
        [ThreadStatic]
        private static int _sender;

        internal static bool InPacket => _sender != 0;

        //服务器上是客户端索引，客户端上是 256（服务器）
        internal static int Sender => _sender - 1;

        internal static void Enter(int sender) => _sender = sender + 1;

        internal static void Leave() => _sender = 0;
    }

    internal sealed class ScenarioNetContextSystem : ModSystem
    {
        //每个网络包开始处理时记下来源。唯一会丢弃的包：服务器已经换了舞台、而客户端还没回报卸载完旧舞台的这段时间里，
        //它发来的原版游戏包。那些包说的是旧舞台里的事（物块改动、弹幕、对 NPC 的伤害），按坐标与槽位套到新舞台上就是破坏
        public override bool HijackGetData(ref byte messageType, ref BinaryReader reader, int playerNumber) {
            if (Main.netMode == NetmodeID.Server && ScenarioDirector.IsLeavingStage(playerNumber)) {
                if (Netplay.Clients[playerNumber].State != 10) {
                    //在途客户端的连接状态一直是 10：不是 10 说明这个槽位已经换了一个新连接
                    ScenarioDirector.ForgetClient(playerNumber);
                }
                else if (!SurvivesStageChange(messageType)) {
                    return true;
                }
            }
            ScenarioNetContext.Enter(playerNumber);
            return false;
        }

        //与舞台无关、换舞台途中也要收下的包：模组包与 tML 自己的包（这里无从判断它们的内容，场景框架自己的消息也在其中），
        //以及只描述玩家自身的包（外观与名字、物品栏、生命、魔力、增益、装备方案）——丢掉后者会让服务器上这个玩家的副本过时
        private static bool SurvivesStageChange(byte messageType) => messageType >= MessageID.InGameChangeConfig
            || messageType is MessageID.SyncPlayer or MessageID.SyncEquipment or MessageID.PlayerLifeMana
                or MessageID.PlayerMana or MessageID.PlayerBuffs or MessageID.SyncLoadout;

        //原版在每帧开头集中处理完网络包，之后才更新实体：从这里起的物块改动都是这一端自己的模拟产生的
        public override void PreUpdateEntities() {
            ScenarioProtectionSync.Flush();
            ScenarioNetContext.Leave();
        }
    }

    //服务器整块改写了一片物块（区域还原、放置结构、场景自己搭建）之后，把这片范围所在的区块重新发给已经收过它们的客户端。
    //原版的区块包里带着物块、箱子、告示牌与物块实体；还没收过这些区块的客户端以后走近时自然会拿到新的内容
    internal static class ScenarioRegionSync
    {
        internal static void Resend(Rectangle rect) {
            if (Main.netMode != NetmodeID.Server) {
                return;
            }
            rect = Rectangle.Intersect(rect, new Rectangle(0, 0, Main.maxTilesX, Main.maxTilesY));
            if (rect.Width <= 0 || rect.Height <= 0) {
                return;
            }
            int left = Netplay.GetSectionX(rect.Left);
            int right = Netplay.GetSectionX(rect.Right - 1);
            int top = Netplay.GetSectionY(rect.Top);
            int bottom = Netplay.GetSectionY(rect.Bottom - 1);
            for (int i = 0; i < Main.maxPlayers; i++) {
                RemoteClient client = Netplay.Clients[i];
                if (client == null || !client.IsConnected() || client.State != 10 || !NetMessage.buffer[i].broadcast) {
                    continue;
                }
                bool[,] sent = client.TileSections;
                bool any = false;
                for (int x = left; x <= right; x++) {
                    for (int y = top; y <= bottom; y++) {
                        if (x < 0 || y < 0 || x >= sent.GetLength(0) || y >= sent.GetLength(1) || !sent[x, y]) {
                            continue;
                        }
                        //SendSection 只发没标记过的区块
                        sent[x, y] = false;
                        NetMessage.SendSection(i, x, y);
                        any = true;
                    }
                }
                if (any) {
                    NetMessage.SendData(MessageID.TileFrameSection, i, -1, null, left, top, right, bottom);
                }
            }
        }
    }
}
