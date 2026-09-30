using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using Terraria;
using Terraria.ModLoader;

namespace InnoVault.Combat
{
    /// <summary>
    /// 一次富受击事件：原版 <c>StrikeNPC</c> 只带伤害和一个标量击退，这里带齐冲量、接触点、反应码、顿帧、攻击 id
    /// </summary>
    public struct HitEvent
    {
        /// <summary>攻击方玩家（服务端按真实发送者改写）</summary>
        public int Attacker;
        /// <summary>目标 NPC 下标</summary>
        public int Target;
        /// <summary>目标 NPC 类型（下标不是身份，两端对类型）</summary>
        public int TargetType;
        /// <summary>攻击 id（同一招只处理一次时用）</summary>
        public int AttackId;
        /// <summary>接触点（世界）</summary>
        public Vector2 Contact;
        /// <summary>冲量（世界，像素 / 帧）</summary>
        public Vector2 Impulse;
        /// <summary>反应码（见 <see cref="AttackReaction"/>）</summary>
        public int Reaction;
        /// <summary>伤害（仅供参考：真正的伤害走原版 <c>StrikeNPC</c> / <c>ApplyDamageToNPC</c>，照常同步）</summary>
        public int Damage;
        /// <summary>顿帧</summary>
        public int HitStop;
        /// <summary>消费方自定义标志位</summary>
        public int Flags;
        /// <summary>消费方自定义数据</summary>
        public int Custom;
    }

    /// <summary>
    /// 事件在哪一端被处理
    /// </summary>
    public enum HitEventSide
    {
        /// <summary>权威端（服务端；单人即本机）：在这里改 AI 状态、决定被踹飞，再由 NPC 自己的同步把结果发出去</summary>
        Authority,
        /// <summary>发起方本机（客户端，发包的同时立即回调）：只做表现（火花、顿帧、震屏），不改权威状态</summary>
        Local,
        /// <summary>其他客户端（服务端转发）：只做表现</summary>
        Remote,
    }

    /// <summary>
    /// 由 <c>ModNPC</c> 实现：接收富受击事件
    /// </summary>
    public interface IHitEventReceiver
    {
        /// <summary>
        /// 收到一次富受击事件
        /// </summary>
        void OnHitEvent(in HitEvent hit, HitEventSide side);
    }

    /// <summary>
    /// 富受击事件的收发：攻击方本机判定命中（法相踹巨人）后调 <see cref="Send"/>——伤害照常走原版 <c>ApplyDamageToNPC</c>，
    /// 冲量 / 接触点 / 反应码经这条包送到服务端，由目标 NPC（<see cref="IHitEventReceiver"/>）在权威端决定怎么反应，
    /// 再按需转发给其他客户端做表现
    /// <br/>服务端校验：发送者即攻击方、目标活着且类型对得上、数值钳在合理范围、每名玩家每秒最多 <see cref="MaxEventsPerSecond"/> 条；拒绝会记一条日志
    /// </summary>
    public static class HitEvents
    {
        private static readonly Dictionary<int, (uint second, int count)> rate = [];

        /// <summary>冲量大小上限（像素 / 帧）</summary>
        public const float MaxImpulse = 256f;
        /// <summary>顿帧上限</summary>
        public const int MaxHitStop = 120;
        /// <summary>每名玩家每秒最多几条</summary>
        public static int MaxEventsPerSecond { get; set; } = 60;

        /// <summary>
        /// 全局监听（不方便实现 <see cref="IHitEventReceiver"/> 的原版 NPC 改造、统计、调试）：(NPC, 事件, 端)。
        /// 订阅方在自己卸载时退订；InnoVault 卸载时清空
        /// </summary>
        public static event HitEventHandler Received;

        /// <summary>
        /// 全局监听的签名
        /// </summary>
        public delegate void HitEventHandler(NPC npc, in HitEvent hit, HitEventSide side);

        /// <summary>
        /// 发送一次富受击事件（攻击方本机调用）。单人：直接在本机按权威端处理；客户端：本机先回调一次 <see cref="HitEventSide.Local"/>，
        /// 再发给服务端；服务端调用：直接按权威端处理并（可选）广播
        /// </summary>
        /// <param name="target">目标 NPC</param>
        /// <param name="hit">事件（<see cref="HitEvent.Target"/> / <see cref="HitEvent.TargetType"/> / <see cref="HitEvent.Attacker"/> 由这里填）</param>
        /// <param name="broadcast">服务端处理后是否转发给其他客户端</param>
        public static void Send(NPC target, HitEvent hit, bool broadcast = true) {
            if (target == null || !target.active) {
                return;
            }
            hit.Target = target.whoAmI;
            hit.TargetType = target.type;
            if (Main.netMode == Terraria.ID.NetmodeID.SinglePlayer) {
                hit.Attacker = Main.myPlayer;
                Dispatch(target, in hit, HitEventSide.Authority);
                return;
            }
            if (Main.netMode == Terraria.ID.NetmodeID.Server) {
                Dispatch(target, in hit, HitEventSide.Authority);
                if (broadcast) {
                    Write(hit, broadcast: false, toClient: -1, ignoreClient: -1);
                }
                return;
            }
            hit.Attacker = Main.myPlayer;
            Dispatch(target, in hit, HitEventSide.Local);
            Write(hit, broadcast, toClient: -1, ignoreClient: -1);
        }

        private static void Write(in HitEvent hit, bool broadcast, int toClient, int ignoreClient) {
            ModPacket packet = VaultMod.Instance.GetPacket();
            packet.Write((byte)MessageType.HitEvent);
            packet.Write((byte)Math.Clamp(hit.Attacker, 0, 255));
            packet.Write((short)hit.Target);
            packet.Write(hit.TargetType);
            packet.Write(hit.AttackId);
            packet.WriteVector2(hit.Contact);
            packet.WriteVector2(hit.Impulse);
            packet.Write(hit.Reaction);
            packet.Write(hit.Damage);
            packet.Write((byte)Math.Clamp(hit.HitStop, 0, 255));
            packet.Write(hit.Flags);
            packet.Write(hit.Custom);
            packet.Write(broadcast);
            packet.Send(toClient, ignoreClient);
        }

        internal static void Handle(BinaryReader reader, int whoAmI) {
            //先把整条读完，再校验（读一半就返回会把后面的包读歪）
            HitEvent hit = new() {
                Attacker = reader.ReadByte(),
                Target = reader.ReadInt16(),
                TargetType = reader.ReadInt32(),
                AttackId = reader.ReadInt32(),
                Contact = reader.ReadVector2(),
                Impulse = reader.ReadVector2(),
                Reaction = reader.ReadInt32(),
                Damage = reader.ReadInt32(),
                HitStop = reader.ReadByte(),
                Flags = reader.ReadInt32(),
                Custom = reader.ReadInt32(),
            };
            bool broadcast = reader.ReadBoolean();

            if (Main.netMode == Terraria.ID.NetmodeID.Server) {
                if (!Validate(ref hit, whoAmI, out NPC npc, out string reason)) {
                    VaultMod.LoggerError($"[HitEvents:reject:{whoAmI}]", $"rejected hit event from player {whoAmI}: {reason}");
                    return;
                }
                Dispatch(npc, in hit, HitEventSide.Authority);
                if (broadcast) {
                    Write(hit, broadcast: false, toClient: -1, ignoreClient: whoAmI);
                }
                return;
            }
            //客户端：服务端转发来的表现事件
            if ((uint)hit.Target < (uint)Main.maxNPCs) {
                NPC npc = Main.npc[hit.Target];
                if (npc.active && npc.type == hit.TargetType) {
                    Dispatch(npc, in hit, HitEventSide.Remote);
                }
            }
        }

        private static bool Validate(ref HitEvent hit, int sender, out NPC npc, out string reason) {
            npc = null;
            if ((uint)sender >= Main.maxPlayers || !Main.player[sender].active) {
                reason = "sender is not an active player";
                return false;
            }
            //攻击方以真实发送者为准
            hit.Attacker = sender;
            if ((uint)hit.Target >= (uint)Main.maxNPCs) {
                reason = $"target index {hit.Target} out of range";
                return false;
            }
            npc = Main.npc[hit.Target];
            if (!npc.active || npc.type != hit.TargetType) {
                reason = $"target {hit.Target} inactive or type {npc.type} != {hit.TargetType}";
                return false;
            }
            if (!float.IsFinite(hit.Contact.X) || !float.IsFinite(hit.Contact.Y) || !float.IsFinite(hit.Impulse.X) || !float.IsFinite(hit.Impulse.Y)) {
                reason = "non-finite vector";
                return false;
            }
            uint second = Main.GameUpdateCount / 60u;
            rate.TryGetValue(sender, out (uint second, int count) r);
            if (r.second != second) {
                r = (second, 0);
            }
            if (r.count >= Math.Max(MaxEventsPerSecond, 1)) {
                reason = $"rate limit {MaxEventsPerSecond}/s exceeded";
                return false;
            }
            rate[sender] = (second, r.count + 1);
            //数值钳制：冲量长度、顿帧、接触点离目标太远的拉回目标外接框附近
            float len = hit.Impulse.Length();
            if (len > MaxImpulse) {
                hit.Impulse *= MaxImpulse / len;
            }
            hit.HitStop = Math.Clamp(hit.HitStop, 0, MaxHitStop);
            hit.Damage = Math.Max(hit.Damage, 0);
            Rectangle near = npc.Hitbox;
            near.Inflate(Math.Max(npc.width, 256), Math.Max(npc.height, 256));
            hit.Contact = Vector2.Clamp(hit.Contact, near.TopLeft(), near.BottomRight());
            reason = null;
            return true;
        }

        private static void Dispatch(NPC npc, in HitEvent hit, HitEventSide side) {
            try {
                if (npc.ModNPC is IHitEventReceiver receiver) {
                    receiver.OnHitEvent(in hit, side);
                }
                Received?.Invoke(npc, in hit, side);
            } catch (Exception ex) {
                VaultMod.LoggerError($"[HitEvents:{npc.type}]", $"hit event handler threw: {ex.Message}");
            }
        }

        internal static void Clear() {
            Received = null;
            rate.Clear();
        }
    }
}
