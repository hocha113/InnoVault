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
    public abstract class ProjOverride : VaultType<ProjOverride>
    {
        /// <summary>
        /// 所有修改的实例集合
        /// </summary>
        public new static List<ProjOverride> Instances { get; internal set; } = [];
        /// <summary>
        /// 一个字典，可以根据目标ID来获得对应的修改实例
        /// </summary>
        public new static Dictionary<int, Dictionary<Type, ProjOverride>> ByID { get; internal set; } = [];
        /// <summary>
        /// 所有负指向的实例集合，只包含<see cref="TargetID"/>为 -1 的实例
        /// </summary>
        public new static List<ProjOverride> UniversalInstances { get; internal set; } = [];
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
        protected override void VaultRegister() {
            Instances.Add(this);
        }
        /// <summary>
        /// 加载内容
        /// </summary>
        public override void VaultSetup() {
            SetStaticDefaults();

            if (TargetID > ItemID.None) {
                //嵌套字典需要提前挖坑
                ByID.TryAdd(TargetID, []);
                ByID[TargetID][GetType()] = this;
            }
            else if (TargetID == -1) {
                UniversalInstances.Add(this);
            }
        }
        /// <summary>
        /// 克隆这个实例，注意，克隆出的新对象与原实例将不再具有任何引用关系
        /// </summary>
        /// <returns></returns>
        public ProjOverride Clone() => (ProjOverride)Activator.CreateInstance(GetType());
        /// <summary>
        /// 寻找对应弹幕实例的重载实例
        /// </summary>
        /// <param name="id"></param>
        /// <param name="projOverrides"></param>
        /// <returns></returns>
        public static bool TryFetchByID(int id, out Dictionary<Type, ProjOverride> projOverrides) {
            projOverrides = null;

            if (!ByID.TryGetValue(id, out var projResults) || projResults.Count == 0) {
                return false;
            }

            Dictionary<Type, ProjOverride> result = null;

            foreach (var projOverrideInstance in projResults.Values) {
                if (!projOverrideInstance.CanOverride()) {
                    continue;
                }
                result ??= [];
                result[projOverrideInstance.GetType()] = projOverrideInstance.Clone();
            }

            if (result == null) {
                return false;
            }

            projOverrides = result;
            return true;
        }

        /// <summary>
        /// 仅用于全局重制节点设置临时Proj实例
        /// </summary>
        /// <param name="setProj"></param>
        internal void UniversalSetProjInstance(Projectile setProj) => projectile = setProj;

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

            if (!proj.TryGetGlobalProjectile(out ProjRebuildLoader globalInstance)) {
                return;
            }

            //遍历所有克隆出的实例
            foreach (var overrideInstance in inds.Values) {
                //为实例设置弹幕上下文并初始化
                overrideInstance.projectile = proj;
                overrideInstance.SetProperty();

                //使用已加载的静态钩子列表的高效查询能力，将实例分发到对应的专属列表中
                if (ProjRebuildLoader.HookAI.HookOverrideQuery.HasOverride(overrideInstance)) {
                    globalInstance.AIOverrides.Add(overrideInstance);
                }
                if (ProjRebuildLoader.HookPostAI.HookOverrideQuery.HasOverride(overrideInstance)) {
                    globalInstance.PostAIOverrides.Add(overrideInstance);
                }
                if (ProjRebuildLoader.HookOnSpawn.HookOverrideQuery.HasOverride(overrideInstance)) {
                    globalInstance.OnSpawnOverrides.Add(overrideInstance);
                }
                if (ProjRebuildLoader.HookShouldUpdatePosition.HookOverrideQuery.HasOverride(overrideInstance)) {
                    globalInstance.ShouldUpdatePositionOverrides.Add(overrideInstance);
                }
                if (ProjRebuildLoader.HookOnHitNPC.HookOverrideQuery.HasOverride(overrideInstance)) {
                    globalInstance.OnHitNPCOverrides.Add(overrideInstance);
                }
                if (ProjRebuildLoader.HookOnHitPlayer.HookOverrideQuery.HasOverride(overrideInstance)) {
                    globalInstance.OnHitPlayerOverrides.Add(overrideInstance);
                }
                if (ProjRebuildLoader.HookOnKill.HookOverrideQuery.HasOverride(overrideInstance)) {
                    globalInstance.OnKillOverrides.Add(overrideInstance);
                }
                if (ProjRebuildLoader.HookDraw.HookOverrideQuery.HasOverride(overrideInstance)) {
                    globalInstance.DrawOverrides.Add(overrideInstance);
                }
                if (ProjRebuildLoader.HookPostDraw.HookOverrideQuery.HasOverride(overrideInstance)) {
                    globalInstance.PostDrawOverrides.Add(overrideInstance);
                }
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
        /// 弹幕的AI逻辑，在<see cref="AI"/>和原版AI逻辑之后调用
        /// </summary>
        public virtual void PostAI() { }
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
