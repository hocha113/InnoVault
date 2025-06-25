using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;

namespace InnoVault.GameSystem
{
    /// <summary>
    /// 提供一个强行覆盖目标弹幕行为性质的基类，通过On钩子为基础运行
    /// </summary>
    public abstract class ProjOverride : ModType
    {
        /// <summary>
        /// 所有修改的实例集合
        /// </summary>
        public static List<ProjOverride> Instances { get; internal set; } = [];
        /// <summary>
        /// 一个字典，可以根据目标ID来获得对应的修改实例
        /// </summary>
        public static Dictionary<int, Dictionary<Type, ProjOverride>> ByID { get; internal set; } = [];
        /// <summary>
        /// 要修改的Proj的ID值
        /// </summary>
        public virtual int TargetID => NPCID.None;
        /// <summary>
        /// 对应的弹幕实例
        /// </summary>
        public Projectile projectile { get; private set; }
        /// <summary>
        /// 封闭加载
        /// </summary>
        protected override void Register() {
            if (!CanLoad()) {
                return;
            }

            Instances.Add(this);
        }
        /// <summary>
        /// 克隆这个实例，注意，克隆出的新对象与原实例将不再具有任何引用关系
        /// </summary>
        /// <returns></returns>
        public ProjOverride Clone() => (ProjOverride)Activator.CreateInstance(GetType());
        /// <summary>
        /// 是否加载这个实例，默认返回<see langword="true"/>
        /// </summary>
        /// <returns></returns>
        public virtual bool CanLoad() { return true; }
        /// <summary>
        /// 是否修改该npc
        /// </summary>
        /// <returns></returns>
        public virtual bool CanOverride() {
            return true;
        }
        /// <summary>
        /// 加载内容
        /// </summary>
        public override void SetupContent() {
            if (!CanLoad()) {
                return;
            }

            SetStaticDefaults();

            if (TargetID <= ItemID.None) {
                return;
            }

            //嵌套字典需要提前挖坑
            ByID.TryAdd(TargetID, []);
            ByID[TargetID][GetType()] = this;
        }
        /// <summary>
        /// 寻找对应弹幕实例的重载实例
        /// </summary>
        /// <param name="id"></param>
        /// <param name="projOverrides"></param>
        /// <returns></returns>
        public static bool TryFetchByID(int id, out Dictionary<Type, ProjOverride> projOverrides) {
            projOverrides = null;

            if (!ByID.TryGetValue(id, out Dictionary<Type, ProjOverride> projResults)) {
                return false;
            }

            projOverrides = [];
            foreach (var projOverrideInstance in projResults.Values) {
                if (projOverrideInstance.CanOverride()) {
                    projOverrides[projOverrideInstance.GetType()] = projOverrideInstance.Clone();
                }
            }

            return projOverrides.Count > 0;
        }

        /// <summary>
        /// 加载并初始化重制节点到对应的弹幕实例上
        /// </summary>
        /// <param name="proj"></param>
        public static void SetDefaults(Projectile proj) {
            if (Main.gameMenu) {
                return;
            }

            if (!TryFetchByID(proj.type, out Dictionary<Type, ProjOverride> inds) || inds == null) {
                return;
            }

            foreach (var npcOverrideInstance in inds.Values) {
                npcOverrideInstance.projectile = proj;
                npcOverrideInstance.SetProperty();
            }

            if (proj.TryGetGlobalProjectile(out ProjRebuildLoader globalInstance)) {
                globalInstance.ProjOverrides = inds;
            }
        }

        /// <summary>
        /// 在弹幕生成的时候调用一次，用于初始化一些实例数据
        /// </summary>
        public virtual void SetProperty() { }
        /// <summary>
        /// 在弹幕生成到世界上后被调用，该函数不会在服务端上运行
        /// </summary>
        /// <param name="source"></param>
        public virtual void OnSpawn(IEntitySource source) { }
        /// <summary>
        /// 是否自动根据速度进行位置更新
        /// </summary>
        /// <returns></returns>
        public virtual bool? ShouldUpdatePosition() => null;
        /// <summary>
        /// 弹幕的AI逻辑，返回<see langword="false"/>可以阻断后续所有AI逻辑的运行，默认返回<see langword="true"/>
        /// </summary>
        /// <returns></returns>
        public virtual bool AI() => true;
        /// <summary>
        /// 击中NPC时调用该函数
        /// </summary>
        /// <param name="target"></param>
        /// <param name="hit"></param>
        /// <param name="damageDone"></param>
        public virtual void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone) { }
        /// <summary>
        /// 击中玩家时调用该函数
        /// </summary>
        /// <param name="target"></param>
        /// <param name="info"></param>
        public virtual void OnHitPlayer(Player target, Player.HurtInfo info) { }
        /// <summary>
        /// 弹幕死亡时调用
        /// </summary>
        /// <param name="timeLeft"></param>
        public virtual void OnKill(int timeLeft) { }
        /// <summary>
        /// 弹幕的绘制逻辑，返回有效值可以阻断后续所有绘制逻辑的运行，默认返回<see langword="null"/>
        /// 不能影响<see cref="ProjectileLoader.PostDraw(Projectile, Color)"/>
        /// </summary>
        /// <param name="lightColor"></param>
        /// <returns></returns>
        public virtual bool? Draw(ref Color lightColor) => null;
        /// <summary>
        /// 弹幕的后层绘制逻辑，返回<see langword="false"/>可以阻断后续
        /// <see cref="ProjectileLoader.PostDraw(Projectile, Color)"/>逻辑的运行，默认返回<see langword="true"/>
        /// </summary>
        /// <param name="lightColor"></param>
        /// <returns></returns>
        public virtual bool PostDraw(Color lightColor) => true;
    }
}
