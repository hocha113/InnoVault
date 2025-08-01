using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;

namespace InnoVault.GameSystem
{
    /// <summary>
    /// 玩家行为覆盖
    /// 该基类以单实例形式存在
    /// </summary>
    public abstract class PlayerOverride : VaultType
    {
        /// <summary>
        /// 所有修改的实例集合
        /// </summary>
        public static List<PlayerOverride> Instances { get; internal set; } = [];
        /// <summary>
        /// 从类型映射到实例
        /// </summary>
        public static Dictionary<Type, PlayerOverride> TypeToInstance { get; internal set; } = [];
        /// <summary>
        /// 所要操纵的玩家实例
        /// </summary>
        public Player Player { get; internal set; }
        /// <summary>
        /// 生效在对于关于物品的钩子函数上，如果为默认值<see cref="ItemID.None"/>则对所有物品生效
        /// </summary>
        public virtual int TargetItemID => ItemID.None;

        /// <summary>
        /// 封闭内容
        /// </summary>
        protected override void Register() {
            if (!CanLoad()) {
                return;
            }

            Instances.Add(this);
            TypeToInstance.Add(GetType(), this);
        }

        /// <summary>
        /// 加载内容
        /// </summary>
        public sealed override void SetupContent() {
            if (!CanLoad()) {
                return;
            }

            SetStaticDefaults();
        }

        /// <summary>
        /// 筛选并返回重制节点实例
        /// </summary>
        /// <param name="player"></param>
        /// <param name="values"></param>
        /// <returns></returns>
        public static bool TryFetchByPlayer(Player player, out Dictionary<Type, PlayerOverride> values) {
            values = [];

            foreach (var pCC in Instances) {
                pCC.Player = player;
                if (!pCC.CanOverride()) {
                    continue;
                }
                values.Add(pCC.GetType(), pCC);
            }

            return values.Count > 0;
        }

        /// <summary>
        /// 获取对应类型的玩家重制节点实例S
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static T GetOverride<T>() where T : PlayerOverride => TypeToInstance[typeof(T)] as T;

        /// <summary>
        /// 修改玩家物品击中NPC时的伤害数据
        /// </summary>
        /// <param name="item"></param>
        /// <param name="target"></param>
        /// <param name="modifiers"></param>
        /// <returns></returns>
        public virtual bool On_ModifyHitNPCWithItem(Item item, NPC target, ref NPC.HitModifiers modifiers) {
            return true;
        }

        /// <summary>
        /// 修改玩家弹幕击中NPC时的伤害数据
        /// </summary>
        /// <param name="proj"></param>
        /// <param name="target"></param>
        /// <param name="modifiers"></param>
        /// <returns></returns>
        public virtual bool On_ModifyHitNPCWithProj(Projectile proj, NPC target, ref NPC.HitModifiers modifiers) {
            return true;
        }

        /// <summary>
        /// 修改玩家是否可以击中NPC
        /// </summary>
        /// <param name="target"></param>
        /// <returns></returns>
        public virtual bool? On_CanHitNPC(NPC target) {
            return null;
        }

        /// <summary>
        /// 玩家击中NPC时运行
        /// </summary>
        /// <param name="target"></param>
        /// <param name="hit"></param>
        /// <param name="damageDone"></param>
        /// <returns></returns>
        public virtual bool On_OnHitNPC(NPC target, in NPC.HitInfo hit, int damageDone) {
            return true;
        }

        /// <summary>
        /// 运行在<see cref="Player.GiveImmuneTimeForCollisionAttack"/>之前
        /// </summary>
        /// <returns></returns>
        public virtual bool On_GiveImmuneTimeForCollisionAttack(int time) {
            return true;
        }

        /// <summary>
        /// 玩家是否可以切换物品，默认返回<see langword="null"/>，即不进行拦截
        /// 返回有效值可以阻止或者允许玩家切换物品，优先级大于<see cref="ItemOverride.CanSwitchWeapon"/>
        /// </summary>
        /// <returns></returns>
        public virtual bool? CanSwitchWeapon() {
            return null;
        }

        /// <summary>
        /// 物品发射时调用该函数，优先级大于<see cref="ItemOverride.Shoot"/>
        /// </summary>
        /// <param name="item"></param>
        /// <param name="source"></param>
        /// <param name="position"></param>
        /// <param name="velocity"></param>
        /// <param name="type"></param>
        /// <param name="damage"></param>
        /// <param name="knockback"></param>
        /// <returns></returns>
        public virtual bool ItemShoot(Item item, EntitySource_ItemUse_WithAmmo source
            , Vector2 position, Vector2 velocity, int type, int damage, float knockback) {
            return true;
        }

        /// <summary>
        /// 是否可以使用物品，优先级大于<see cref="ItemOverride.CanUseItem"/>
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public virtual bool CanUseItem(Item item) {
            return true;
        }
    }
}
