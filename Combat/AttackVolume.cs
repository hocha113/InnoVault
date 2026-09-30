using InnoVault.Rigs2D.Runtime;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Terraria;
using Terraria.DataStructures;

namespace InnoVault.Combat
{
    /// <summary>
    /// 攻击体积打谁
    /// </summary>
    [Flags]
    public enum AttackTeam
    {
        /// <summary>谁也不打（暂存）</summary>
        None = 0,
        /// <summary>打玩家（敌方的攻击）</summary>
        Hostile = 1,
        /// <summary>打 NPC（玩家一方的攻击）</summary>
        Friendly = 2,
        /// <summary>都打</summary>
        Both = Hostile | Friendly,
    }

    /// <summary>
    /// 冲量怎么定方向
    /// </summary>
    public enum AttackImpulseMode
    {
        /// <summary>固定世界向量 <see cref="AttackVolume.Impulse"/></summary>
        Fixed,
        /// <summary>从攻击者中心指向接触点，大小 <see cref="AttackVolume.ImpulseMagnitude"/>，再叠 <see cref="AttackVolume.Impulse"/>（常用来加一点上挑）</summary>
        AwayFromOwner,
        /// <summary>沿接触法线（把受击方推出攻击体），大小 <see cref="AttackVolume.ImpulseMagnitude"/>，再叠 <see cref="AttackVolume.Impulse"/></summary>
        AlongNormal,
        /// <summary>沿扫掠方向（<see cref="AttackVolume.Sweep"/> 在命中点的速度方向），大小 <see cref="AttackVolume.ImpulseMagnitude"/>，再叠 <see cref="AttackVolume.Impulse"/></summary>
        AlongSweep,
    }

    /// <summary>
    /// 常用的受击反应码（<see cref="AttackVolume.Reaction"/> 是消费方自定义的整数，这几个只是约定俗成的起点）
    /// </summary>
    public static class AttackReaction
    {
        /// <summary>轻击：闪白、受击弹簧</summary>
        public const int Flinch = 0;
        /// <summary>踉跄：打断动作、后滑</summary>
        public const int Stagger = 1;
        /// <summary>击飞：整身刚体接管</summary>
        public const int Launch = 2;
        /// <summary>击倒：原地倒地</summary>
        public const int Knockdown = 3;
        /// <summary>抓取 / 固定</summary>
        public const int Grab = 4;
    }

    /// <summary>
    /// 一次命中：哪个攻击体积、打在哪、推向哪、带什么反应
    /// </summary>
    public struct AttackHit
    {
        /// <summary>命中的攻击体积</summary>
        public AttackVolume Volume;
        /// <summary>接触（点在两表面之间；法线从攻击体指向受击方 = 被推的方向）</summary>
        public Rig2DContact Contact;
        /// <summary>按规则解析后的冲量（世界，像素 / 帧）</summary>
        public Vector2 Impulse;
        /// <summary>受击方被命中的形状下标（胶囊组时为下标，矩形为 -1）</summary>
        public int VictimIndex;
        /// <summary>伤害</summary>
        public readonly int Damage => Volume?.Damage ?? 0;
        /// <summary>反应码</summary>
        public readonly int Reaction => Volume?.Reaction ?? 0;
        /// <summary>顿帧</summary>
        public readonly int HitStop => Volume?.HitStop ?? 0;
        /// <summary>能否格挡</summary>
        public readonly bool Blockable => Volume?.Blockable ?? false;
    }

    /// <summary>
    /// 攻击体积：攻击方每帧公开"此刻哪里有伤害、带什么力"，受击方来查——受击判定留在受击方本机（原版 NPC 接触伤害与敌弹就是这样判的），
    /// 伤害以外的信息（冲量、反应、顿帧、能否格挡、攻击 id）也带得过去，不再需要手搓一个短命敌弹当伤害窗
    /// <br/>用法（攻击方，每帧在骨架步进之后）：<c>volume.Clear(); volume.AddGroup(rig, "kickFoot"); volume.AttackId = 本招 id; volume.Publish();</c>
    /// ——窗口期内每帧都公开，窗口外不公开即可。<see cref="AttackId"/> 每招换一个（同一招多帧命中只算一次，靠受击方的 <see cref="AttackHitMemory"/>）
    /// <br/>联机：攻击方的形状由已同步的状态在各端算出，受击方本机判定，不发包；需要服务端权威处理的反应（NPC 被踹飞）走 <see cref="HitEvents"/>
    /// <br/>普通玩家（没有自己接管受击的）会由 InnoVault 按原版 <c>Player.Hurt</c> 结算 <see cref="AttackTeam.Hostile"/> 的体积，见 <see cref="IAttackVolumeReceiver"/>
    /// </summary>
    public sealed class AttackVolume
    {
        private readonly List<Rig2DCapsule> shape = [];
        internal uint expireTick;
        internal bool published;

        /// <summary>
        /// 建一个攻击体积
        /// </summary>
        /// <param name="owner">攻击者（NPC / 弹幕 / 玩家）</param>
        /// <param name="slot">同一攻击者的第几个体积（左拳、右脚……）</param>
        public AttackVolume(Entity owner = null, int slot = 0) {
            Owner = owner;
            Slot = slot;
        }

        /// <summary>攻击者（死亡原因、身份、方向都从它来）</summary>
        public Entity Owner { get; set; }
        /// <summary>同一攻击者的体积槽位</summary>
        public int Slot { get; set; }
        /// <summary>攻击 id：每一招换一个，受击方按 (攻击者, 槽位, id) 记"这一招打过我了"</summary>
        public int AttackId { get; set; }
        /// <summary>打谁</summary>
        public AttackTeam Team { get; set; } = AttackTeam.Hostile;
        /// <summary>伤害</summary>
        public int Damage { get; set; }
        /// <summary>原版击退（普通玩家走 <c>Player.Hurt</c> 时用）</summary>
        public float Knockback { get; set; } = 6f;
        /// <summary>冲量（<see cref="AttackImpulseMode.Fixed"/> 时为全部，其余模式下叠加在方向冲量上）</summary>
        public Vector2 Impulse { get; set; }
        /// <summary>冲量方向规则</summary>
        public AttackImpulseMode ImpulseMode { get; set; } = AttackImpulseMode.Fixed;
        /// <summary>方向冲量的大小（像素 / 帧）</summary>
        public float ImpulseMagnitude { get; set; }
        /// <summary>反应码（见 <see cref="AttackReaction"/>，消费方自定义）</summary>
        public int Reaction { get; set; }
        /// <summary>顿帧（帧）</summary>
        public int HitStop { get; set; }
        /// <summary>能否格挡</summary>
        public bool Blockable { get; set; } = true;
        /// <summary>扫掠：快速挥动的刃 / 脚一帧走几十像素，给了就按帧间插值测（形状列表可以同时有）</summary>
        public Rig2DBoneSweep Sweep { get; set; }
        /// <summary>扫掠线段的半径（世界像素）</summary>
        public float SweepRadius { get; set; } = 16f;
        /// <summary>扫掠刃段起点（占线段长比例）</summary>
        public float SweepFromFrac { get; set; }
        /// <summary>普通玩家结算时的免疫槽（<c>ImmunityCooldownID</c>，-1 = 通用无敌帧）</summary>
        public int CooldownSlot { get; set; } = -1;
        /// <summary>死亡原因（空 = 按攻击者类型自动取）</summary>
        public PlayerDeathReason DeathReason { get; set; }
        /// <summary>是否允许 InnoVault 按原版 <c>Player.Hurt</c> 直接结算普通玩家（关掉则只供查询）</summary>
        public bool HurtsPlayers { get; set; } = true;
        /// <summary>消费方附带的任意数据</summary>
        public object Payload { get; set; }
        /// <summary>形状（世界胶囊）</summary>
        public IReadOnlyList<Rig2DCapsule> Shape => shape;
        /// <summary>本帧是否已公开</summary>
        public bool Published => published && AttackVolumes.IsLive(this);

        //==================== 形状 ====================

        /// <summary>清空形状（每帧重建前）</summary>
        public AttackVolume Clear() {
            shape.Clear();
            return this;
        }

        /// <summary>加一个胶囊</summary>
        public AttackVolume Add(in Rig2DCapsule capsule) {
            shape.Add(capsule);
            return this;
        }

        /// <summary>加一个胶囊</summary>
        public AttackVolume Add(Vector2 a, Vector2 b, float radius) => Add(new Rig2DCapsule(a, b, radius, shape.Count));

        /// <summary>加一个圆</summary>
        public AttackVolume AddCircle(Vector2 center, float radius) => Add(Rig2DCapsule.Circle(center, radius, shape.Count));

        /// <summary>
        /// 把骨架某个胶囊组本帧的世界形状加进来（画布空间骨架给 <paramref name="transform"/> 与 <paramref name="radiusScale"/>）
        /// </summary>
        public AttackVolume AddGroup(Rig2DInstance rig, string group, Func<Vector2, Vector2> transform = null, float radiusScale = 1f) {
            Rig2DHit.GatherGroup(rig, group, shape, transform, radiusScale);
            return this;
        }

        /// <summary>
        /// 公开到本帧（保留 <paramref name="lifeFrames"/> 个游戏帧：攻击方在 NPC 更新里公开，玩家更新在下一帧才来查）
        /// </summary>
        public void Publish(int lifeFrames = 2) => AttackVolumes.Publish(this, lifeFrames);

        /// <summary>收回（窗口提前结束）</summary>
        public void Retract() => AttackVolumes.Retract(this);

        //==================== 测试 ====================

        /// <summary>
        /// 与一组受击胶囊测最深的接触（扫掠优先：它能抓到帧间穿过的命中）
        /// </summary>
        public bool Test(ReadOnlySpan<Rig2DCapsule> victim, out AttackHit hit) {
            hit = default;
            hit.Volume = this;
            hit.VictimIndex = -1;
            if (victim.Length == 0) {
                return false;
            }
            if (Sweep != null && Sweep.Count > 0 && Sweep.SweepContact(victim, SweepRadius, SweepFromFrac, out Rig2DSweepHit sh)) {
                hit.Contact = sh.Contact;
                hit.VictimIndex = sh.TargetIndex;
                hit.Impulse = ResolveImpulse(hit.Contact);
                return true;
            }
            if (shape.Count == 0) {
                return false;
            }
            //法线约定：从攻击体指向受击方 → 以受击方为 A
            if (Rig2DHit.CapsulesContact(victim, CollectionsMarshal.AsSpan(shape), out Rig2DGroupContact gc)) {
                hit.Contact = gc.Contact;
                hit.VictimIndex = gc.IndexA;
                hit.Impulse = ResolveImpulse(hit.Contact);
                return true;
            }
            return false;
        }

        /// <summary>
        /// 与一个受击矩形（玩家 / 普通 NPC 的 hitbox）测接触
        /// </summary>
        public bool Test(Rectangle victim, out AttackHit hit) {
            hit = default;
            hit.Volume = this;
            hit.VictimIndex = -1;
            if (Sweep != null && Sweep.Count > 0 && Sweep.SweepIntersects(victim, SweepRadius, SweepFromFrac, out _)) {
                Vector2 center = new(victim.X + victim.Width * 0.5f, victim.Y + victim.Height * 0.5f);
                Vector2 tip = Sweep.History[^1].Tip;
                Vector2 n = center - tip;
                hit.Contact = new Rig2DContact(center, n.LengthSquared() > 1e-4f ? Vector2.Normalize(n) : Vector2.UnitX, SweepRadius);
                hit.Impulse = ResolveImpulse(hit.Contact);
                return true;
            }
            float deepest = float.MinValue;
            bool any = false;
            for (int i = 0; i < shape.Count; i++) {
                if (Rig2DHit.CapsuleRectContact(shape[i], victim, out Rig2DContact c) && c.Depth > deepest) {
                    deepest = c.Depth;
                    //胶囊对矩形的法线是"从矩形指向胶囊"，翻成"从攻击体指向受击方"
                    hit.Contact = new Rig2DContact(c.Point, -c.Normal, c.Depth);
                    any = true;
                }
            }
            if (any) {
                hit.Impulse = ResolveImpulse(hit.Contact);
            }
            return any;
        }

        /// <summary>
        /// 按 <see cref="ImpulseMode"/> 解析一处接触的冲量
        /// </summary>
        public Vector2 ResolveImpulse(in Rig2DContact contact) {
            Vector2 dir;
            switch (ImpulseMode) {
                case AttackImpulseMode.AwayFromOwner:
                    dir = Owner != null ? contact.Point - Owner.Center : contact.Normal;
                    break;
                case AttackImpulseMode.AlongNormal:
                    dir = contact.Normal;
                    break;
                case AttackImpulseMode.AlongSweep:
                    dir = Sweep != null ? Sweep.PointVelocity(1f) : contact.Normal;
                    break;
                default:
                    return Impulse;
            }
            if (dir.LengthSquared() < 1e-6f) {
                dir = contact.Normal;
            }
            if (dir.LengthSquared() > 1e-6f) {
                dir.Normalize();
            }
            return dir * ImpulseMagnitude + Impulse;
        }

        /// <summary>
        /// 攻击者身份键（受击记忆用）：类别 + 下标 + 类型
        /// </summary>
        internal long OwnerKey {
            get {
                return Owner switch {
                    NPC npc => (1L << 60) | ((long)npc.whoAmI << 32) | (uint)npc.type,
                    Projectile p => (2L << 60) | ((long)p.owner << 40) | ((long)p.identity << 16) | (uint)(p.type & 0xFFFF),
                    Player pl => (3L << 60) | (uint)pl.whoAmI,
                    null => 0L,
                    _ => (4L << 60) | (uint)Owner.whoAmI,
                };
            }
        }
    }
}
