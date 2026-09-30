using InnoVault.Rigs2D.Runtime;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using Terraria;

namespace InnoVault.Combat
{
    /// <summary>
    /// 攻击体积登记表：攻击方公开、受击方查询。每个体积带过期帧（缺省公开后 2 个游戏帧内有效），过期自动剔除
    /// <br/>纯本端数据，不联机同步（各端由同步的状态自己算出同一份体积）；卸世界清空
    /// </summary>
    public static class AttackVolumes
    {
        private static readonly List<AttackVolume> live = [];

        /// <summary>
        /// 公开（或续期）一个体积
        /// </summary>
        public static void Publish(AttackVolume volume, int lifeFrames = 2) {
            if (volume == null) {
                return;
            }
            volume.expireTick = Main.GameUpdateCount + (uint)Math.Max(lifeFrames, 1);
            if (!volume.published) {
                volume.published = true;
                live.Add(volume);
            }
        }

        /// <summary>
        /// 收回一个体积
        /// </summary>
        public static void Retract(AttackVolume volume) {
            if (volume != null && volume.published) {
                volume.published = false;
                live.Remove(volume);
            }
        }

        /// <summary>
        /// 收回某攻击者的全部体积（死亡、瞬移、招式被打断）
        /// </summary>
        public static void RetractAll(Entity owner) {
            for (int i = live.Count - 1; i >= 0; i--) {
                if (live[i].Owner == owner) {
                    live[i].published = false;
                    live.RemoveAt(i);
                }
            }
        }

        internal static bool IsLive(AttackVolume volume) => volume.published && Main.GameUpdateCount < volume.expireTick;

        private static void Prune() {
            uint now = Main.GameUpdateCount;
            for (int i = live.Count - 1; i >= 0; i--) {
                AttackVolume v = live[i];
                bool ownerGone = v.Owner != null && !v.Owner.active;
                if (now >= v.expireTick || ownerGone) {
                    v.published = false;
                    live.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// 当前有效的体积
        /// </summary>
        public static IReadOnlyList<AttackVolume> Live {
            get {
                Prune();
                return live;
            }
        }

        /// <summary>
        /// 用一组受击胶囊查询：把与之接触、且会打 <paramref name="team"/> 的体积逐个放进 <paramref name="results"/>（每个体积一条，取最深接触），返回条数。
        /// <paramref name="exclude"/> 是受击方自己（不被自己的体积打）。一招只结算一次交给 <see cref="AttackHitMemory"/>
        /// </summary>
        public static int Query(ReadOnlySpan<Rig2DCapsule> victim, AttackTeam team, List<AttackHit> results, Entity exclude = null) {
            Prune();
            int added = 0;
            for (int i = 0; i < live.Count; i++) {
                AttackVolume v = live[i];
                if ((v.Team & team) == 0 || exclude != null && v.Owner == exclude) {
                    continue;
                }
                if (v.Test(victim, out AttackHit hit)) {
                    results?.Add(hit);
                    added++;
                }
            }
            return added;
        }

        /// <summary>
        /// 用一个受击矩形查询（玩家、普通 NPC）
        /// </summary>
        public static int Query(Rectangle victim, AttackTeam team, List<AttackHit> results, Entity exclude = null) {
            Prune();
            int added = 0;
            for (int i = 0; i < live.Count; i++) {
                AttackVolume v = live[i];
                if ((v.Team & team) == 0 || exclude != null && v.Owner == exclude) {
                    continue;
                }
                if (v.Test(victim, out AttackHit hit)) {
                    results?.Add(hit);
                    added++;
                }
            }
            return added;
        }

        internal static void Clear() {
            for (int i = 0; i < live.Count; i++) {
                live[i].published = false;
            }
            live.Clear();
        }
    }

    /// <summary>
    /// 受击方的"这一招打过我了"记忆：按 (攻击者, 槽位, 攻击 id) 去重，一招多帧接触只结算一次；条目超过 <see cref="Lifetime"/> 帧自动忘掉。
    /// 每个受击方各持一份（玩家的放在 ModPlayer 里，NPC 的放在自己的 AI 状态里）
    /// </summary>
    public sealed class AttackHitMemory
    {
        private readonly List<(long owner, int slot, int id, uint tick)> entries = [];

        /// <summary>条目保留帧数</summary>
        public int Lifetime { get; set; } = 600;

        /// <summary>
        /// 第一次见到这一招返回 <see langword="true"/> 并记下；之后同一招返回 <see langword="false"/>
        /// </summary>
        public bool TryConsume(AttackVolume volume) {
            if (volume == null) {
                return false;
            }
            uint now = Main.GameUpdateCount;
            long owner = volume.OwnerKey;
            for (int i = entries.Count - 1; i >= 0; i--) {
                var e = entries[i];
                if (now - e.tick > (uint)Math.Max(Lifetime, 1)) {
                    entries.RemoveAt(i);
                    continue;
                }
                if (e.owner == owner && e.slot == volume.Slot && e.id == volume.AttackId) {
                    return false;
                }
            }
            entries.Add((owner, volume.Slot, volume.AttackId, now));
            return true;
        }

        /// <summary>
        /// 同上（按命中）
        /// </summary>
        public bool TryConsume(in AttackHit hit) => TryConsume(hit.Volume);

        /// <summary>
        /// 全部忘掉
        /// </summary>
        public void Clear() => entries.Clear();
    }
}
