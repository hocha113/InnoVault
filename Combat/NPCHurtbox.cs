using InnoVault.Rigs2D.Runtime;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using Terraria;
using Terraria.ModLoader;

namespace InnoVault.Combat
{
    /// <summary>
    /// NPC 被胶囊命中的一次查询结果
    /// </summary>
    public struct NPCHurtboxHit
    {
        /// <summary>命中的 NPC</summary>
        public NPC NPC;
        /// <summary>接触（法线从攻击体指向 NPC）</summary>
        public Rig2DContact Contact;
        /// <summary>攻击体一侧的下标</summary>
        public int AttackIndex;
        /// <summary>NPC 受击胶囊的下标（按矩形判定时为 -1）</summary>
        public int HurtboxIndex;
    }

    /// <summary>
    /// NPC 胶囊受击盒：巨型 NPC 每帧公开自己的身体胶囊（<see cref="Publish(NPC, Rig2DInstance, string, Func{Vector2, Vector2}, float, bool)"/>），
    /// 之后玩家的弹幕、近战挥砍、NPC 的接触伤害都按胶囊判定，不再按包围矩形——槊扫过巨人两腿之间的空气不算命中，站在巨人裆下也不挨接触伤害
    /// <br/>弹幕：在 <c>CanBeHitByProjectile</c> 预筛（它在原版矩形碰撞之前跑，不用 IL）；自带碰撞的弹幕（激光、鞭子）用它自己的
    /// <c>Colliding</c> 去问胶囊切出的一串小方块。近战：<c>CanCollideWithPlayerMeleeAttack</c> 拿到挥砍矩形直接测。
    /// 接触伤害：<c>CanHitPlayer</c> 按玩家 hitbox 测
    /// <br/>各端自己算自己公开（巨人的骨架在客户端本来就要步进）；过期（超过 2 个游戏帧没公开）即回退到原版矩形
    /// </summary>
    public sealed class NPCHurtbox : GlobalNPC
    {
        private static readonly List<Rig2DCapsule>[] shapes = new List<Rig2DCapsule>[Main.maxNPCs];
        private static readonly uint[] ticks = new uint[Main.maxNPCs];
        private static readonly int[] types = new int[Main.maxNPCs];
        private static readonly bool[] contactByShape = new bool[Main.maxNPCs];
        private static Func<Projectile, Rectangle> damageHitbox;
        private static bool damageHitboxResolved;

        /// <summary>
        /// 公开一个 NPC 本帧的受击胶囊（世界）
        /// </summary>
        /// <param name="npc">NPC</param>
        /// <param name="capsules">受击胶囊</param>
        /// <param name="contactDamage">接触伤害是否也按胶囊判（缺省是）</param>
        public static void Publish(NPC npc, ReadOnlySpan<Rig2DCapsule> capsules, bool contactDamage = true) {
            if (npc == null || (uint)npc.whoAmI >= (uint)Main.maxNPCs) {
                return;
            }
            List<Rig2DCapsule> list = Begin(npc, contactDamage);
            for (int i = 0; i < capsules.Length; i++) {
                list.Add(capsules[i]);
            }
        }

        /// <summary>
        /// 从骨架胶囊组公开（画布空间骨架给 <paramref name="transform"/> 与 <paramref name="radiusScale"/>）
        /// </summary>
        public static void Publish(NPC npc, Rig2DInstance rig, string group, Func<Vector2, Vector2> transform = null, float radiusScale = 1f, bool contactDamage = true) {
            if (npc == null || (uint)npc.whoAmI >= (uint)Main.maxNPCs) {
                return;
            }
            Rig2DHit.GatherGroup(rig, group, Begin(npc, contactDamage), transform, radiusScale);
        }

        private static List<Rig2DCapsule> Begin(NPC npc, bool contactDamage) {
            int i = npc.whoAmI;
            List<Rig2DCapsule> list = shapes[i] ??= new List<Rig2DCapsule>(16);
            list.Clear();
            ticks[i] = Main.GameUpdateCount;
            types[i] = npc.type;
            contactByShape[i] = contactDamage;
            return list;
        }

        /// <summary>
        /// 撤掉某 NPC 的受击盒（回退到原版矩形）
        /// </summary>
        public static void Withdraw(NPC npc) {
            if (npc != null && (uint)npc.whoAmI < (uint)Main.maxNPCs) {
                shapes[npc.whoAmI]?.Clear();
                types[npc.whoAmI] = 0;
            }
        }

        /// <summary>
        /// 取某 NPC 当前有效的受击胶囊（2 个游戏帧内公开过、类型对得上）
        /// </summary>
        public static bool TryGet(NPC npc, out ReadOnlySpan<Rig2DCapsule> capsules) {
            capsules = default;
            if (npc == null || !npc.active || (uint)npc.whoAmI >= (uint)Main.maxNPCs) {
                return false;
            }
            int i = npc.whoAmI;
            List<Rig2DCapsule> list = shapes[i];
            if (list == null || list.Count == 0 || types[i] != npc.type || Main.GameUpdateCount - ticks[i] > 2u) {
                return false;
            }
            capsules = CollectionsMarshal.AsSpan(list);
            return true;
        }

        /// <summary>
        /// 矩形是否碰到 NPC 的身体（有受击盒按胶囊，没有按原版矩形）
        /// </summary>
        public static bool Intersects(NPC npc, Rectangle rect) {
            if (TryGet(npc, out ReadOnlySpan<Rig2DCapsule> caps)) {
                return Rig2DHit.CapsulesIntersect(caps, rect, out _);
            }
            return npc != null && npc.Hitbox.Intersects(rect);
        }

        /// <summary>
        /// 一组攻击胶囊与某 NPC 身体的最深接触（有受击盒按胶囊对胶囊，没有按胶囊对矩形）
        /// </summary>
        public static bool Contact(NPC npc, ReadOnlySpan<Rig2DCapsule> attack, out NPCHurtboxHit hit) {
            hit = default;
            hit.NPC = npc;
            hit.AttackIndex = hit.HurtboxIndex = -1;
            if (npc == null || !npc.active || attack.Length == 0) {
                return false;
            }
            if (TryGet(npc, out ReadOnlySpan<Rig2DCapsule> caps)) {
                if (Rig2DHit.CapsulesContact(caps, attack, out Rig2DGroupContact gc)) {
                    hit.Contact = gc.Contact;
                    hit.HurtboxIndex = gc.IndexA;
                    hit.AttackIndex = gc.IndexB;
                    return true;
                }
                return false;
            }
            if (Rig2DHit.CapsulesRectContact(attack, npc.Hitbox, out Rig2DContact c, out int index)) {
                hit.Contact = new Rig2DContact(c.Point, -c.Normal, c.Depth);
                hit.AttackIndex = index;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 一次扫掠（快速挥动的武器 / 脚）与某 NPC 身体的最早接触
        /// </summary>
        public static bool Contact(NPC npc, Rig2DBoneSweep sweep, float radius, float fromFrac, out NPCHurtboxHit hit) {
            hit = default;
            hit.NPC = npc;
            hit.AttackIndex = hit.HurtboxIndex = -1;
            if (npc == null || !npc.active || sweep == null || sweep.Count == 0) {
                return false;
            }
            if (TryGet(npc, out ReadOnlySpan<Rig2DCapsule> caps)) {
                if (sweep.SweepContact(caps, radius, fromFrac, out Rig2DSweepHit sh)) {
                    hit.Contact = sh.Contact;
                    hit.HurtboxIndex = sh.TargetIndex;
                    return true;
                }
                return false;
            }
            if (sweep.SweepIntersects(npc.Hitbox, radius, fromFrac, out _)) {
                Vector2 tip = sweep.History[^1].Tip;
                Vector2 n = npc.Center - tip;
                hit.Contact = new Rig2DContact(npc.Hitbox.ClosestPointInRect(tip), n.LengthSquared() > 1e-4f ? Vector2.Normalize(n) : Vector2.UnitX, radius);
                return true;
            }
            return false;
        }

        /// <summary>
        /// 攻击胶囊扫一遍全部 NPC，把碰到的放进 <paramref name="results"/>（每个 NPC 一条，最深接触），返回条数。
        /// <paramref name="filter"/> 为空时跳过友好 NPC 与不受伤的 NPC
        /// </summary>
        public static int Query(ReadOnlySpan<Rig2DCapsule> attack, List<NPCHurtboxHit> results, Func<NPC, bool> filter = null) {
            if (attack.Length == 0) {
                return 0;
            }
            Rectangle bounds = attack[0].Bounds;
            for (int i = 1; i < attack.Length; i++) {
                bounds = Rectangle.Union(bounds, attack[i].Bounds);
            }
            int added = 0;
            for (int i = 0; i < Main.maxNPCs; i++) {
                NPC npc = Main.npc[i];
                if (!npc.active) {
                    continue;
                }
                if (filter != null ? !filter(npc) : npc.friendly || npc.dontTakeDamage) {
                    continue;
                }
                if (!npc.Hitbox.Intersects(bounds)) {
                    continue;
                }
                if (Contact(npc, attack, out NPCHurtboxHit hit)) {
                    results?.Add(hit);
                    added++;
                }
            }
            return added;
        }

        //==================== 原版判定预筛 ====================

        /// <inheritdoc/>
        public override bool? CanBeHitByProjectile(NPC npc, Projectile projectile) {
            if (!TryGet(npc, out ReadOnlySpan<Rig2DCapsule> caps)) {
                return null;
            }
            Rectangle myRect = DamageHitbox(projectile);
            if (Rig2DHit.CapsulesIntersect(caps, myRect, out _)) {
                return null;
            }
            //矩形碰不到胶囊：再看弹幕自己的碰撞体（激光、鞭子、长柄）能不能够到——先问整个 NPC 矩形，够不到就直接否决
            if (!projectile.Colliding(myRect, npc.Hitbox)) {
                return false;
            }
            for (int c = 0; c < caps.Length; c++) {
                Rig2DCapsule cap = caps[c];
                float len = cap.Length;
                int n = Math.Max(1, (int)MathF.Ceiling(len / MathF.Max(cap.Radius, 1f)) + 1);
                int half = Math.Max(1, (int)(cap.Radius * 0.85f));
                for (int k = 0; k < n; k++) {
                    Vector2 p = n == 1 ? cap.A : Vector2.Lerp(cap.A, cap.B, k / (float)(n - 1));
                    Rectangle probe = new((int)p.X - half, (int)p.Y - half, half * 2, half * 2);
                    if (projectile.Colliding(myRect, probe)) {
                        return null;
                    }
                }
            }
            return false;
        }

        /// <inheritdoc/>
        public override bool? CanCollideWithPlayerMeleeAttack(NPC npc, Player player, Item item, Rectangle meleeAttackHitbox) {
            if (!TryGet(npc, out ReadOnlySpan<Rig2DCapsule> caps)) {
                return null;
            }
            return Rig2DHit.CapsulesIntersect(caps, meleeAttackHitbox, out _) ? null : false;
        }

        /// <inheritdoc/>
        public override bool CanHitPlayer(NPC npc, Player target, ref int cooldownSlot) {
            if (!contactByShape[npc.whoAmI] || !TryGet(npc, out ReadOnlySpan<Rig2DCapsule> caps)) {
                return true;
            }
            return Rig2DHit.CapsulesIntersect(caps, target.Hitbox, out _);
        }

        //原版 Projectile.Damage 用的伤害矩形（私有方法：部分弹种外扩 + ModifyDamageHitbox）
        private static Rectangle DamageHitbox(Projectile projectile) {
            if (!damageHitboxResolved) {
                damageHitboxResolved = true;
                MethodInfo m = typeof(Projectile).GetMethod("Damage_GetHitbox", BindingFlags.NonPublic | BindingFlags.Instance);
                if (m != null) {
                    try {
                        damageHitbox = m.CreateDelegate<Func<Projectile, Rectangle>>();
                    } catch {
                        damageHitbox = null;
                    }
                }
            }
            if (damageHitbox != null) {
                return damageHitbox(projectile);
            }
            Rectangle r = projectile.Hitbox;
            ProjectileLoader.ModifyDamageHitbox(projectile, ref r);
            return r;
        }

        internal static void ClearAll() {
            for (int i = 0; i < shapes.Length; i++) {
                shapes[i]?.Clear();
                types[i] = 0;
            }
        }

        /// <inheritdoc/>
        public override void Unload() {
            ClearAll();
            damageHitbox = null;
            damageHitboxResolved = false;
        }
    }
}
