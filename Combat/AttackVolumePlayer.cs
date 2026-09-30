using System.Collections.Generic;
using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;

namespace InnoVault.Combat
{
    /// <summary>
    /// 由 <c>ModPlayer</c> 实现：本地玩家被 <see cref="AttackTeam.Hostile"/> 的攻击体积碰到时先问它。
    /// 返回 <see langword="true"/> = 已由你处理（法相这类替身接管了受击），InnoVault 不再按原版结算；
    /// 每招只问一次（同一招的后续帧不再问）
    /// <br/>替身体型比真身大得多时，受击方通常自己用替身的胶囊去 <see cref="AttackVolumes.Query(System.ReadOnlySpan{Rigs2D.Runtime.Rig2DCapsule}, AttackTeam, List{AttackHit}, Entity)"/>，
    /// 再在这里对真身的命中一律返回 <see langword="true"/>（替身在时真身不挨打）
    /// </summary>
    public interface IAttackVolumeReceiver
    {
        /// <summary>
        /// 真身 hitbox 被一个体积碰到（本地玩家、这一招第一次）
        /// </summary>
        bool OnAttackVolume(in AttackHit hit);
    }

    /// <summary>
    /// 普通玩家的缺省结算：本地玩家每帧用 hitbox 查询敌方攻击体积，每招一次，先问 <see cref="IAttackVolumeReceiver"/>，
    /// 没人接管就走原版 <c>Player.Hurt</c>（无敌帧、闪避、减伤、联机同步都由原版处理）
    /// </summary>
    internal sealed class AttackVolumePlayer : ModPlayer
    {
        private readonly List<AttackHit> hits = [];

        /// <summary>这名玩家的"这一招打过我了"记忆</summary>
        public AttackHitMemory Memory { get; } = new();

        public override void OnEnterWorld() => Memory.Clear();

        public override void PostUpdate() {
            if (Player.whoAmI != Main.myPlayer || Player.dead || !Player.active || Main.gameMenu) {
                return;
            }
            IReadOnlyList<AttackVolume> live = AttackVolumes.Live;
            if (live.Count == 0) {
                return;
            }
            hits.Clear();
            AttackVolumes.Query(Player.Hitbox, AttackTeam.Hostile, hits, Player);
            for (int i = 0; i < hits.Count; i++) {
                AttackHit hit = hits[i];
                if (!hit.Volume.HurtsPlayers && !HasReceiver()) {
                    continue;
                }
                if (!Memory.TryConsume(hit)) {
                    continue;
                }
                if (Intercepted(hit) || !hit.Volume.HurtsPlayers || hit.Damage <= 0) {
                    continue;
                }
                int dir = hit.Impulse.X != 0f ? (hit.Impulse.X > 0f ? 1 : -1) : (hit.Contact.Normal.X >= 0f ? 1 : -1);
                PlayerDeathReason reason = hit.Volume.DeathReason ?? DefaultReason(hit.Volume.Owner);
                Player.Hurt(reason, hit.Damage, dir, cooldownCounter: hit.Volume.CooldownSlot, knockback: hit.Volume.Knockback);
            }
        }

        private bool HasReceiver() {
            foreach (ModPlayer mp in Player.ModPlayers) {
                if (mp is IAttackVolumeReceiver) {
                    return true;
                }
            }
            return false;
        }

        private bool Intercepted(in AttackHit hit) {
            bool handled = false;
            foreach (ModPlayer mp in Player.ModPlayers) {
                if (mp is IAttackVolumeReceiver r) {
                    try {
                        handled |= r.OnAttackVolume(in hit);
                    } catch (System.Exception ex) {
                        VaultMod.LoggerError($"[AttackVolume:{mp.GetType().Name}]", $"OnAttackVolume threw: {ex.Message}");
                    }
                }
            }
            return handled;
        }

        private static PlayerDeathReason DefaultReason(Entity owner) => owner switch {
            NPC npc => PlayerDeathReason.ByNPC(npc.whoAmI),
            Projectile p => PlayerDeathReason.ByProjectile(p.owner, p.whoAmI),
            Player pl => PlayerDeathReason.ByPlayerItem(pl.whoAmI, pl.HeldItem),
            _ => PlayerDeathReason.LegacyDefault(),
        };
    }
}
