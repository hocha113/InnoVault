﻿using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace InnoVault.GameSystem
{
    /// <summary>
    /// 物品行为覆盖
    /// 该基类以单实例形式存在
    /// </summary>
    public abstract class ItemOverride : VaultType, ILocalizedModType
    {
        #region Data
        /// <summary>
        /// 所有修改的实例集合
        /// </summary>
        public static List<ItemOverride> Instances { get; internal set; } = [];
        /// <summary>
        /// 从类型映射到实例
        /// </summary>
        public static Dictionary<Type, ItemOverride> TypeToInstance { get; internal set; } = [];
        /// <summary>
        /// 一个字典，可以根据目标ID来获得对应的修改实例
        /// </summary>
        public static Dictionary<int, Dictionary<Type, ItemOverride>> ByID { get; internal set; } = [];
        /// <summary>
        /// 控制指定物品类型是否允许任意前缀<br/>
        /// 该属性只在游戏加载阶段生效，应当在<see cref="ModType.SetStaticDefaults"/>或者其他调用时机合适的函数中修改<br/>
        /// 值为 <see langword="true"/>：强制允许所有前缀<br/>
        /// 值为 <see langword="false"/>：强制禁止所有前缀<br/>
        /// 值为 <see langword="null"/>：使用默认逻辑
        /// </summary>
        public static Dictionary<int, bool?> ItemAllowPrefixDic { get; set; } = [];
        /// <summary>
        /// 控制指定物品类型是否允许近战前缀<br/>
        /// 该属性只在游戏加载阶段生效，应当在<see cref="ModType.SetStaticDefaults"/>或者其他调用时机合适的函数中修改<br/>
        /// 值为 <see langword="true"/>：强制允许所有近战前缀<br/>
        /// 值为 <see langword="false"/>：强制禁止所有近战前缀<br/>
        /// 值为 <see langword="null"/>：使用默认逻辑
        /// </summary>
        public static Dictionary<int, bool?> ItemMeleePrefixDic { get; set; } = [];
        /// <summary>
        /// 控制指定物品类型是否允许远程前缀<br/>
        /// 该属性只在游戏加载阶段生效，应当在<see cref="ModType.SetStaticDefaults"/>或者其他调用时机合适的函数中修改<br/>
        /// 值为 <see langword="true"/>：强制允许所有远程前缀<br/>
        /// 值为 <see langword="false"/>：强制禁止所有远程前缀<br/>
        /// 值为 <see langword="null"/>：使用默认逻辑
        /// </summary>
        public static Dictionary<int, bool?> ItemRangedPrefixDic { get; set; } = [];
        /// <summary>
        /// 重置对象的目标ID，默认为<see cref="ItemID.None"/>，即什么都不做
        /// </summary>
        public virtual int TargetID => ItemID.None;
        /// <summary>
        /// 是否是一个关于原版物品的重制节点
        /// [未使用]
        /// </summary>
        public virtual bool IsVanilla => TargetID < ItemID.Count;
        /// <summary>
        /// 是否参与配方替换，默认为<see langword="true"/>，如果<see cref="IsVanilla"/>为<see langword="true"/>，那么该属性自动返回<see langword="false"/>
        /// [未使用]
        /// </summary>
        [Obsolete("该属性已经弃用，不再具备任何实际用途，如果需要检测配方合成，请直接判定ID或者使用IsVanilla")]
        public virtual bool FormulaSubstitution => !IsVanilla;
        /// <summary>
        /// 该重置节点是否会加载进图鉴中，默认为<see langword="true"/>
        /// [未使用]
        /// </summary>
        public virtual bool DrawingInfo => true;
        /// <summary>
        /// 在指定的键里面添加本地化
        /// </summary>
        public virtual string LocalizationCategory => "RemakeItems";
        /// <summary>
        /// 是否加载这个重制节点的本地化信息
        /// </summary>
        public virtual bool CanLoadLocalization => true;
        /// <summary>
        /// 名字
        /// </summary>
        public virtual LocalizedText DisplayName {
            get {
                LocalizedText content;
                string path;
                if (TargetID < ItemID.Count) {
                    path = "ItemName." + ItemID.Search.GetName(TargetID);
                    content = Language.GetText(path);
                    if (content.Value == path) {//防止找不到键时输出路径
                        return this.GetLocalization(nameof(DisplayName), () => Name);
                    }
                }
                else {
                    content = ItemLoader.GetItem(TargetID).GetLocalization("DisplayName");
                }
                return this.GetLocalization(nameof(DisplayName), () => content.Value);
            }
        }
        /// <summary>
        /// 描述
        /// </summary>
        public virtual LocalizedText Tooltip {
            get {
                LocalizedText content;
                string path;
                if (TargetID < ItemID.Count) {
                    path = "ItemTooltip." + ItemID.Search.GetName(TargetID);
                    content = Language.GetText(path);
                    if (content.Value == path) {//防止找不到键时输出路径
                        return this.GetLocalization(nameof(Tooltip), () => "");
                    }
                }
                else {
                    content = ItemLoader.GetItem(TargetID).GetLocalization("Tooltip");
                }
                return this.GetLocalization(nameof(Tooltip), () => content.Value);
            }
        }
        #endregion
        /// <summary>
        /// 封闭内容
        /// </summary>
        protected override void Register() {
            if (CanLoad() && TargetID > ItemID.None) {
                Instances.Add(this);
                TypeToInstance.Add(GetType(), this);
            }
        }

        /// <summary>
        /// 加载内容
        /// </summary>
        public sealed override void SetupContent() {
            if (!CanLoad()) {
                return;
            }

            SetStaticDefaults();
            if (CanLoadLocalization) {
                _ = DisplayName;
                _ = Tooltip;
            }

            if (TargetID <= ItemID.None) {
                return;
            }

            //嵌套字典需要提前挖坑
            ByID.TryAdd(TargetID, []);
            ByID[TargetID][GetType()] = this;
            PostSetStaticDefaults();
        }

        /// <summary>
        /// 执行在内容加载的靠后部分，此时本地化设置、实例添加已经完成
        /// </summary>
        public virtual void PostSetStaticDefaults() {

        }

        /// <summary>
        /// 尝试通过物品 ID 获取所有已注册的 <see cref="ItemOverride"/> 实例
        /// 如果所有实例的 <c>CanOverride()</c> 均返回 <c>null</c>，则默认返回 <see langword="true"/>
        /// </summary>
        /// <param name="id">目标物品的类型 ID</param>
        /// <param name="itemOverrides">输出对应 ID 下的覆盖器集合；如果未找到，则为 <c>null</c></param>
        /// <returns>
        /// 返回 <see langword="true"/> 表示存在至少一个允许使用的覆盖器；
        /// 否则返回 <see langword="false"/>
        /// </returns>
        public static bool TryFetchByID(int id, out Dictionary<Type, ItemOverride> itemOverrides) {
            itemOverrides = null;

            if (!ByID.TryGetValue(id, out var itemResults)) {
                return false;
            }

            Dictionary<Type, ItemOverride> result = null;

            foreach (var item in itemResults.Values) {
                if (!item.CanOverride()) {
                    continue;
                }
                result ??= [];
                result[item.GetType()] = item;
            }

            if (result is null) {
                return false;
            }

            itemOverrides = result;
            return true;
        }

        /// <summary>
        /// 尝试通过物品 ID 获取一个可用的 <see cref="ItemOverride"/> 实例
        /// 若多个覆盖器存在，返回最后一个通过验证的实例
        /// 如果所有 <c>CanOverride()</c> 都为 <c>null</c>，则默认接受，返回最后一个注册的实例
        /// </summary>
        /// <param name="id">目标物品的类型 ID</param>
        /// <param name="itemOverride">输出找到的覆盖器实例；如果未找到或被拒绝，则为 <c>null</c></param>
        /// <returns>
        /// 如果找到并允许使用某个覆盖器，返回 <see langword="true"/>；否则返回<c>false</c>
        /// </returns>
        public static bool TryFetchByID(int id, out ItemOverride itemOverride) {
            itemOverride = null;
            if (!ByID.TryGetValue(id, out var keyValuePairs)) {
                return false;//通过ID查找ItemOverride，如果未找到，直接返回
            }

            bool? reset = null;//调用该ItemOverride的CanOverride方法，判断是否允许覆盖
            foreach (var item in keyValuePairs.Values) {
                itemOverride = item;
                reset = item.CanOverride();
            }

            return reset ?? true;
        }

        /// <summary>
        /// 获取对应物品的重制节点实例
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static T GetOverride<T>() where T : ItemOverride => TypeToInstance[typeof(T)] as T;

        /// <summary>
        /// 进行背包中的物品绘制，这个函数会执行在Draw之后
        /// </summary>
        /// <param name="item"></param>
        /// <param name="spriteBatch"></param>
        /// <param name="position"></param>
        /// <param name="frame"></param>
        /// <param name="drawColor"></param>
        /// <param name="itemColor"></param>
        /// <param name="origin"></param>
        /// <param name="scale"></param>
        public virtual void PostDrawInInventory(Item item, SpriteBatch spriteBatch, Vector2 position, Rectangle frame, Color drawColor, Color itemColor, Vector2 origin, float scale) {

        }
        /// <summary>
        /// 进行背包中的物品绘制，这个函数会执行在Draw之前
        /// </summary>
        /// <returns>返回默认值<see langword="null"/>会继续执行该物品的原默认方法</returns>
        public virtual bool? PreDrawInInventory(Item item, SpriteBatch spriteBatch, Vector2 position, Rectangle frame, Color drawColor, Color itemColor, Vector2 origin, float scale) {
            return null;
        }
        /// <summary>
        /// 进行背包中的物品绘制，这个函数会执行在Draw之前
        /// </summary>
        /// <returns>返回默认值<see langword="null"/>会继续执行该物品的原默认方法，如果返回有效值则会阻断后续逻辑的运行</returns>
        public virtual bool? On_PreDrawInInventory(Item item, SpriteBatch spriteBatch, Vector2 position, Rectangle frame, Color drawColor, Color itemColor, Vector2 origin, float scale) {
            return null;
        }
        /// <summary>
        /// 返回<see langword="false"/>可以强制滚动更新前缀
        /// </summary>
        /// <returns>返回默认值<see langword="null"/>会继续执行该物品的原默认方法</returns>
        public virtual bool? AllowPrefix(Item item, int pre) {
            return null;
        }
        /// <summary>
        /// 返回<see langword="true"/>将允许该物品可以被右键使用
        /// </summary>
        /// <returns>返回默认值<see langword="null"/>会继续执行该物品的原默认方法</returns>
        public virtual bool? AltFunctionUse(Item item, Player player) {
            return null;
        }
        /// <summary>
        /// 返回<see langword="true"/>将允许该物品可以被右键使用
        /// </summary>
        /// <returns>返回默认值<see langword="null"/>会继续执行该物品的原默认方法，否则将阻断后续所有判定的执行</returns>
        public virtual bool? On_AltFunctionUse(Item item, Player player) {
            return null;
        }
        /// <summary>
        /// 觉定这个饰品在什么情况下可以被装载
        /// </summary>
        /// <returns>返回默认值<see langword="null"/>会继续执行该物品的原默认方法</returns>
        public virtual bool? CanAccessoryBeEquippedWith(Item equippedItem, Item incomingItem, Player player) {
            return null;
        }
        /// <summary>
        /// 决定一个弹药是否对该物品有效，如果返回<see langword="true"/>将让该物品可以将弹药视作可装填目标
        /// </summary>
        /// <param name="ammo"></param>
        /// <param name="weapon"></param>
        /// <param name="player"></param>
        /// <returns>返回默认值<see langword="null"/>会继续执行该物品的原默认方法</returns>
        public virtual bool? CanBeChosenAsAmmo(Item ammo, Item weapon, Player player) {
            return null;
        }
        /// <summary>
        /// 给定的弹药是否会被消耗，如果返回<see langword="false"/>将防止这次的弹药被消耗掉
        /// </summary>
        /// <param name="ammo"></param>
        /// <param name="weapon"></param>
        /// <param name="player"></param>
        /// <returns>返回默认值<see langword="null"/>会继续执行该物品的原默认方法</returns>
        public virtual bool? CanBeConsumedAsAmmo(Item ammo, Item weapon, Player player) {
            return null;
        }
        /// <summary>
        /// 决定是否可以捕获该NPC
        /// </summary>
        /// <param name="item"></param>
        /// <param name="target"></param>
        /// <param name="player"></param>
        /// <returns>返回默认值<see langword="null"/>会继续执行该物品的原默认方法</returns>
        public virtual bool? CanCatchNPC(Item item, NPC target, Player player) {
            return null;
        }
        /// <summary>
        /// 用于禁止玩家装备该饰品，返回<see langword="false"/>表示禁止装配，默认返回<see langword="true"/>
        /// </summary>
        /// <param name="item"></param>
        /// <param name="player"></param>
        /// <param name="slot"></param>
        /// <param name="modded"></param>
        /// <returns>返回默认值<see langword="null"/>会继续执行该物品的原默认方法</returns>
        public virtual bool? CanEquipAccessory(Item item, Player player, int slot, bool modded) {
            return null;
        }
        /// <summary>
        /// 该物品是否可以击中NPC
        /// </summary>
        /// <param name="item"></param>
        /// <param name="player"></param>
        /// <param name="target"></param>
        /// <returns>返回默认值<see langword="null"/>会继续执行该物品的原默认方法</returns>
        public virtual bool? CanHitNPC(Item item, Player player, NPC target) {
            return null;
        }
        /// <summary>
        /// 该物品是否可以击中目标玩家
        /// </summary>
        /// <param name="item"></param>
        /// <param name="player"></param>
        /// <param name="target"></param>
        /// <returns>返回默认值<see langword="null"/>会继续执行该物品的原默认方法</returns>
        public virtual bool? CanHitPvp(Item item, Player player, Player target) {
            return null;
        }
        /// <summary>
        /// 决定该物品的挥舞是否可以击中目标
        /// </summary>
        /// <param name="item">物品对象</param>
        /// <param name="meleeAttackHitbox">物品的碰撞箱体</param>
        /// <param name="player">玩家对象</param>
        /// <param name="target">受击对象</param>
        /// <returns>返回默认值<see langword="null"/>会继续执行该物品的原默认方法</returns>
        public virtual bool? CanMeleeAttackCollideWithNPC(Item item, Rectangle meleeAttackHitbox, Player player, NPC target) {
            return null;
        }
        /// <summary>
        /// 决定这个项目是否可以被玩家拾取
        /// </summary>
        /// <param name="item"></param>
        /// <param name="player"></param>
        /// <returns>返回默认值<see langword="null"/>会继续执行该物品的原默认方法</returns>
        public virtual bool? CanPickup(Item item, Player player) {
            return null;
        }
        /// <summary>
        /// 决定这个项目是否可以被锻造，返回<see langword="false"/>阻止该物品被锻造
        /// </summary>
        /// <param name="item"></param>
        /// <returns>返回默认值<see langword="null"/>会继续执行该物品的原默认方法</returns>
        public virtual bool? CanReforge(Item item) {
            return null;
        }
        /// <summary>
        /// 决定这个项目是否可以被研究，返回<see langword="false"/>阻止该物品被研究
        /// </summary>
        /// <param name="item"></param>
        /// <returns>返回默认值<see langword="null"/>会继续执行该物品的原默认方法</returns>
        public virtual bool? CanResearch(Item item) {
            return null;
        }
        /// <summary>
        /// 决定这个项目在库存中被右键时是否执行某些操作，返回<see langword="false"/>阻止该物品的库内右击事件，默认返回<see langword="false"/>
        /// </summary>
        /// <param name="item"></param>
        /// <returns>返回默认值<see langword="null"/>会继续执行该物品的原默认方法</returns>
        public virtual bool? CanRightClick(Item item) {
            return null;
        }
        /// <summary>
        /// 决定这个项目在使用时是否可以射出射弹，返回<see langword="false"/>阻止该物品调用<see cref="GlobalItem.Shoot(Item, Player, EntitySource_ItemUse_WithAmmo, Vector2, Vector2, int, int, float)"/>，默认返回<see langword="true"/>
        /// </summary>
        /// <returns>返回默认值<see langword="null"/>会继续执行该物品的原默认方法</returns>
        public virtual bool? CanShoot(Item item, Player player) {
            return null;
        }
        /// <summary>
        /// 当物品试图互相堆叠合并时会调用这个方法，无论是在世界中还是在玩家库存中，返回<see langword="false"/>阻止该物品进行堆叠
        /// </summary>
        /// <returns>返回默认值<see langword="null"/>会继续执行该物品的原默认方法</returns>
        public virtual bool? CanStack(Item destination, Item source) {
            return null;
        }
        /// <summary>
        /// 当物品试图互相堆叠合并时会调用这个方法，在世界中时会调用，返回<see langword="false"/>阻止该物品进行堆叠
        /// </summary>
        /// <returns>返回默认值<see langword="null"/>会继续执行该物品的原默认方法</returns>
        public virtual bool? CanStackInWorld(Item destination, Item source) {
            return null;
        }
        /// <summary>
        /// 是否可以使用该物品，默认返回<see langword="true"/>
        /// </summary>
        /// <returns>返回默认值<see langword="null"/>会继续执行该物品的原默认方法</returns>
        public virtual bool? CanUseItem(Item item, Player player) {
            return null;
        }
        /// <summary>
        /// 是否可以使用该物品
        /// </summary>
        /// <returns>返回<see langword="null"/>继续执行该物品的原<see cref="ItemLoader.CanUseItem(Item, Player)"/>方法，返回<see langword="false"/>禁用该物品的这次使用
        /// ，返回<see langword="true"/>则让物品正常使用，默认返回<see langword="null"/></returns>
        public virtual bool? On_CanUseItem(Item item, Player player) {
            return null;
        }
        /// <summary>
        /// 这个物品在使用时是否会被消耗
        /// </summary>
        /// <returns>返回默认值<see langword="null"/>会继续执行该物品的原默认方法</returns>
        public virtual bool? ConsumeItem(Item item, Player player) {
            return null;
        }
        /// <summary>
        /// 这个物品在使用时是否会被消耗
        /// </summary>
        /// <returns>返回默认值<see langword="null"/>会继续执行该物品的原默认方法</returns>
        public virtual bool? On_ConsumeItem(Item item, Player player) {
            return null;
        }
        /// <summary>
        /// 手持这个物品时会执行的行为
        /// </summary>
        /// <param name="item"></param>
        /// <param name="player"></param>
        public virtual void HoldItem(Item item, Player player) {

        }
        /// <summary>
        /// 用于在手持时修改该物品的动画
        /// </summary>
        /// <param name="item"></param>
        /// <param name="player"></param>
        public virtual void HoldItemFrame(Item item, Player player) {

        }
        /// <summary>
        /// 用于加载物品的自定义额外数据
        /// </summary>
        /// <param name="item"></param>
        /// <param name="tag"></param>
        public virtual void LoadData(Item item, TagCompound tag) {

        }
        /// <summary>
        /// 挥舞该物品时会调用该方法，一般用于给物品创建近战特殊效果
        /// </summary>
        /// <param name="item"></param>
        /// <param name="player"></param>
        /// <param name="hitbox"></param>
        public virtual void MeleeEffects(Item item, Player player, Rectangle hitbox) {

        }
        /// <summary>
        /// 当物品击中NPC时，用于修改造成的伤害数据
        /// </summary>
        /// <param name="item"></param>
        /// <param name="player"></param>
        /// <param name="target"></param>
        /// <param name="modifiers"></param>
        public virtual void ModifyHitNPC(Item item, Player player, NPC target, ref NPC.HitModifiers modifiers) {

        }
        /// <summary>
        /// 当物品击中NPC时，用于修改造成的伤害数据
        /// </summary>
        /// <param name="item"></param>
        /// <param name="player"></param>
        /// <param name="target"></param>
        /// <param name="modifiers"></param>  
        /// <returns>返回<see langword="null"/>会继续执行原来的方法，包括原ModItem方法与G方法。
        /// 返回<see langword="true"/>仅仅会继续执行原ModItem方法而阻止全局Item类的额外修改运行。
        /// 返回<see langword="false"/>阻止后续所有修改的运行</returns>
        public virtual bool? On_ModifyHitNPC(Item item, Player player, NPC target, ref NPC.HitModifiers modifiers) {
            return null;
        }
        /// <summary>
        /// 当物品击中目标玩家时，用于修改造成的伤害数据
        /// </summary>
        /// <param name="item"></param>
        /// <param name="player"></param>
        /// <param name="target"></param>
        /// <param name="modifiers"></param>
        public virtual void ModifyHitPvp(Item item, Player player, Player target, ref Player.HurtModifiers modifiers) {

        }
        /// <summary>
        /// 用于添加该物品的战利品池内容
        /// </summary>
        /// <param name="item"></param>
        /// <param name="itemLoot"></param>
        public virtual void ModifyItemLoot(Item item, ItemLoot itemLoot) {

        }
        /// <summary>
        /// 用于添加该物品的战利品池内容
        /// </summary>
        /// <param name="item"></param>
        /// <param name="itemLoot"></param>
        /// <returns>返回<see langword="null"/>会继续执行原来的方法，包括原ModItem方法与G方法。
        /// 返回<see langword="true"/>仅仅会继续执行原ModItem方法而阻止全局Item类的额外修改运行。
        /// 返回<see langword="false"/>阻止后续所有修改的运行</returns>
        public virtual bool? On_ModifyItemLoot(Item item, ItemLoot itemLoot) {
            return null;
        }
        /// <summary>
        /// 用于动态修改该物品的体积大小，这一般直接关联影响其碰撞箱体
        /// </summary>
        /// <param name="item"></param>
        /// <param name="player"></param>
        /// <param name="scale"></param>
        public virtual void ModifyItemScale(Item item, Player player, ref float scale) {

        }
        /// <summary>
        /// 用于动态修改该物品的使用魔耗
        /// </summary>
        /// <param name="item"></param>
        /// <param name="player"></param>
        /// <param name="reduce"></param>
        /// <param name="mult"></param>
        public virtual void ModifyManaCost(Item item, Player player, ref float reduce, ref float mult) {

        }

        /// <summary>
        /// 包含关于物品的一些基本射击参数
        /// </summary>
        public class ItemShootState
        {
            /// <summary>
            /// 发射位置
            /// </summary>
            public Vector2 Position;
            /// <summary>
            /// 发射速度
            /// </summary>
            public Vector2 Velocity;
            /// <summary>
            /// 射弹类型
            /// </summary>
            public int Type;
            /// <summary>
            /// 射弹伤害
            /// </summary>
            public int Damage;
            /// <summary>
            /// 射弹击退
            /// </summary>
            public float Knockback;
        }

        /// <summary>
        /// 这个物品射击前进行一些属性修改，比如调整伤害
        /// </summary>
        /// <param name="item"></param>
        /// <param name="player"></param>
        /// <param name="position"></param>
        /// <param name="velocity"></param>
        /// <param name="type"></param>
        /// <param name="damage"></param>
        /// <param name="knockback"></param>
        public virtual bool? On_ModifyShootStats(Item item, Player player, ref Vector2 position, ref Vector2 velocity, ref int type, ref int damage, ref float knockback) {
            return null;
        }
        /// <summary>
        /// 这个物品射击前进行一些属性修改，比如调整伤害
        /// </summary>
        /// <param name="item"></param>
        /// <param name="player"></param>
        /// <param name="shootStats"></param>
        public virtual void ModifyShootStats(Item item, Player player, ref ItemShootState shootStats) {
            ModifyShootStats(item, player, ref shootStats.Position, ref shootStats.Velocity, ref shootStats.Type, ref shootStats.Damage, ref shootStats.Knockback);
        }
        /// <summary>
        /// 这个物品射击前进行一些属性修改，比如调整伤害
        /// </summary>
        /// <param name="item"></param>
        /// <param name="player"></param>
        /// <param name="position"></param>
        /// <param name="velocity"></param>
        /// <param name="type"></param>
        /// <param name="damage"></param>
        /// <param name="knockback"></param>
        public virtual void ModifyShootStats(Item item, Player player, ref Vector2 position, ref Vector2 velocity, ref int type, ref int damage, ref float knockback) {

        }
        /// <summary>
        /// 修改该物品的介绍栏位
        /// </summary>
        /// <param name="item"></param>
        /// <param name="tooltips"></param>
        public virtual void ModifyTooltips(Item item, List<TooltipLine> tooltips) {

        }
        /// <summary>
        /// 修改该物品的介绍栏位
        /// </summary>
        /// <param name="item"></param>
        /// <param name="tooltips"></param>
        /// <returns>返回<see langword="null"/>会继续执行原来的方法，包括原ModItem方法与G方法。
        /// 返回<see langword="true"/>仅仅会继续执行原ModItem方法而阻止全局Item类的额外修改运行。
        /// 返回<see langword="false"/>阻止后续所有修改的运行</returns>
        public virtual bool? On_ModifyTooltips(Item item, List<TooltipLine> tooltips) {
            return null;
        }
        /// <summary>
        /// 修改物品暴击
        /// </summary>
        /// <param name="item"></param>
        /// <param name="player"></param>
        /// <param name="crit"></param>
        public virtual void ModifyWeaponCrit(Item item, Player player, ref float crit) {

        }
        /// <summary>
        /// 修改物品暴击
        /// </summary>
        /// <param name="item"></param>
        /// <param name="player"></param>
        /// <param name="crit"></param>
        /// <returns>返回<see langword="null"/>会继续执行原来的方法，包括原ModItem方法与G方法。
        /// 返回<see langword="true"/>仅仅会继续执行原ModItem方法而阻止全局Item类的额外修改运行。
        /// 返回<see langword="false"/>阻止后续所有修改的运行</returns>
        public virtual bool? On_ModifyWeaponCrit(Item item, Player player, ref float crit) {
            return null;
        }
        /// <summary>
        /// 修改物品伤害数据
        /// </summary>
        /// <param name="item"></param>
        /// <param name="player"></param>
        /// <param name="damage"></param>
        public virtual void ModifyWeaponDamage(Item item, Player player, ref StatModifier damage) {

        }
        /// <summary>
        /// 修改物品伤害数据
        /// </summary>
        /// <returns>返回<see langword="true"/>仅仅会继续执行原ModItem方法与全局Item类的额外修改运行。
        /// 返回<see langword="false"/>阻止后续所有修改的运行</returns>
        public virtual bool On_ModifyWeaponDamage(Item item, Player player, ref StatModifier damage) {
            return true;
        }
        /// <summary>
        /// 修改物品击退数据
        /// </summary>
        /// <param name="item"></param>
        /// <param name="player"></param>
        /// <param name="knockback"></param>
        public virtual void ModifyWeaponKnockback(Item item, Player player, ref StatModifier knockback) {

        }
        /// <summary>
        /// 当武器消耗弹药时让一些事情发生
        /// </summary>
        /// <param name="weapon"></param>
        /// <param name="ammo"></param>
        /// <param name="player"></param>
        public virtual void OnConsumeAmmo(Item weapon, Item ammo, Player player) {

        }
        /// <summary>
        /// 当武器消耗弹药时让一些事情发生
        /// </summary>
        /// <param name="weapon"></param>
        /// <param name="ammo"></param>
        /// <param name="player"></param>
        public virtual void OnConsumedAsAmmo(Item ammo, Item weapon, Player player) {

        }
        /// <summary>
        /// 该次射击是否消耗弹药
        /// </summary>
        /// <param name="weapon"></param>
        /// <param name="ammo"></param>
        /// <param name="player"></param>
        /// <returns>返回<see langword="null"/>会继续执行原来的方法，包括原ModItem方法与G方法。
        /// 返回<see langword="true"/>阻断后续香草代码的判定并让该次射击消耗弹药。
        /// 返回<see langword="false"/>阻断后续香草代码的判定并让该次射击不会消耗弹药</returns>
        /// <returns></returns>
        public virtual bool? On_CanConsumeAmmo(Item weapon, Item ammo, Player player) {
            return null;
        }
        /// <summary>
        /// 让这个物品被消耗时让一些事情发生
        /// </summary>
        /// <param name="item"></param>
        /// <param name="player"></param>
        public virtual void OnConsumeItem(Item item, Player player) {

        }
        /// <summary>
        /// 让这个物品消耗魔力时让一些事情发生，进行消耗玩家魔力的修改
        /// </summary>
        /// <param name="item"></param>
        /// <param name="player"></param>
        /// <param name="manaConsumed"></param>
        public virtual void OnConsumeMana(Item item, Player player, int manaConsumed) {

        }
        /// <summary>
        /// 击中NPC时会发生的事情
        /// </summary>
        /// <param name="item"></param>
        /// <param name="player"></param>
        /// <param name="target"></param>
        /// <param name="hit"></param>
        /// <param name="damageDone"></param>
        public virtual void OnHitNPC(Item item, Player player, NPC target, NPC.HitInfo hit, int damageDone) {

        }

        /// <summary>
        /// 击中NPC时会发生的事情
        /// </summary>
        /// <param name="item"></param>
        /// <param name="player"></param>
        /// <param name="target"></param>
        /// <param name="hit"></param>
        /// <param name="damageDone"></param>
        /// <returns>返回<see langword="true"/>会继续执行该物品的原默认方法，返回<see langword="false"/>禁用原方法的行为，默认返回<see langword="null"/></returns>
        public virtual bool On_OnHitNPC(Item item, Player player, NPC target, NPC.HitInfo hit, int damageDone) {
            return true;
        }

        /// <summary>
        /// 击中目标玩家时会发生的事情
        /// </summary>
        /// <param name="item"></param>
        /// <param name="player"></param>
        /// <param name="target"></param>
        /// <param name="hurtInfo"></param>
        public virtual void OnHitPvp(Item item, Player player, Player target, Player.HurtInfo hurtInfo) {

        }

        /// <summary>
        /// 击中目标玩家时会发生的事情
        /// </summary>
        /// <param name="item"></param>
        /// <param name="player"></param>
        /// <param name="target"></param>
        /// <param name="hurtInfo"></param>
        /// <returns>返回<see langword="true"/>会继续执行该物品的原默认方法，返回<see langword="false"/>禁用原方法的行为，默认返回<see langword="null"/></returns>
        public virtual bool On_OnHitPvp(Item item, Player player, Player target, Player.HurtInfo hurtInfo) {
            return true;
        }

        /// <summary>
        /// 当玩家魔力值不够时仍旧试图使用物品时让一些事情发生
        /// </summary>
        /// <param name="item"></param>
        /// <param name="player"></param>
        /// <param name="neededMana"></param>
        public virtual void OnMissingMana(Item item, Player player, int neededMana) {

        }
        /// <summary>
        /// 允许这个物品在被玩家拾取时做出一些特殊的事情
        /// </summary>
        /// <param name="item"></param>
        /// <param name="player"></param>
        /// <returns>返回默认值<see langword="null"/>会继续执行该物品的原默认方法</returns>
        public virtual bool? OnPickup(Item item, Player player) {
            return null;
        }
        /// <summary>
        /// 当这个物品出现在世界中时让一些事情发生
        /// </summary>
        /// <param name="item"></param>
        /// <param name="source"></param>
        public virtual void OnSpawn(Item item, IEntitySource source) {

        }
        /// <summary>
        /// 当物品堆叠到一起时，让一些事情发生，这个钩子函数会在物品从开始堆叠到完成堆叠为一个新的物品的过程中调用
        /// </summary>
        /// <param name="destination">目标源</param>
        /// <param name="source">源物品</param>
        /// <param name="numToTransfer">堆叠的数量</param>
        public virtual void OnStack(Item destination, Item source, int numToTransfer) {

        }
        /// <summary>
        /// 用根据使用的弹药来修改弹药的各种属性
        /// </summary>
        /// <param name="weapon"></param>
        /// <param name="ammo"></param>
        /// <param name="player"></param>
        /// <param name="type"></param>
        /// <param name="speed"></param>
        /// <param name="damage"></param>
        /// <param name="knockback"></param>
        public virtual void PickAmmo(Item weapon, Item ammo, Player player, ref int type, ref float speed, ref StatModifier damage, ref float knockback) {

        }
        /// <summary>
        /// 右键该物品时让一些事情发生
        /// </summary>
        /// <param name="item"></param>
        /// <param name="player"></param>
        public virtual void RightClick(Item item, Player player) {

        }
        /// <summary>
        /// 保存该物品的一些自定义特殊数据
        /// </summary>
        /// <param name="item"></param>
        /// <param name="tag"></param>
        public virtual void SaveData(Item item, TagCompound tag) {

        }
        /// <summary>
        /// 创建这个物品时设置他的各种实例
        /// </summary>
        /// <param name="item"></param>
        public virtual void SetDefaults(Item item) {

        }
        /// <summary>
        /// 该物品在进行射击行为时会调用的方法，返回返回<see langword="false"/>将阻止对物品的射击行为
        /// </summary>
        /// <param name="item"></param>
        /// <param name="player"></param>
        /// <param name="source"></param>
        /// <param name="position"></param>
        /// <param name="velocity"></param>
        /// <param name="type"></param>
        /// <param name="damage"></param>
        /// <param name="knockback"></param>
        /// <returns>返回默认值<see langword="null"/>会继续执行该物品的原默认方法</returns>
        public virtual bool? Shoot(Item item, Player player, EntitySource_ItemUse_WithAmmo source, Vector2 position, Vector2 velocity, int type, int damage, float knockback) {
            return null;
        }
        /// <summary>
        /// 该物品在进行射击行为时会调用的方法，返回返回<see langword="false"/>将阻止对物品的射击行为
        /// </summary>
        /// <param name="item"></param>
        /// <param name="player"></param>
        /// <param name="source"></param>
        /// <param name="position"></param>
        /// <param name="velocity"></param>
        /// <param name="type"></param>
        /// <param name="damage"></param>
        /// <param name="knockback"></param>
        /// <returns>返回<see langword="null"/>会继续执行原来的方法，包括原ModNPC方法与G方法。
        /// 返回<see langword="true"/>将会阻断后续TML方法的运行，但执行原版默认的射击行为
        /// 返回<see langword="false"/>阻止后续所有修改的运行</returns>
        public virtual bool? On_Shoot(Item item, Player player, EntitySource_ItemUse_WithAmmo source, Vector2 position, Vector2 velocity, int type, int damage, float knockback) {
            return null;
        }
        /// <summary>
        /// 拆分这个物品时让一些事情发生
        /// </summary>
        /// <param name="destination"></param>
        /// <param name="source"></param>
        /// <param name="numToTransfer"></param>
        public virtual void SplitStack(Item destination, Item source, int numToTransfer) {

        }
        /// <summary>
        /// 这个物品在玩家库存中会执行的函数
        /// </summary>
        /// <param name="item"></param>
        /// <param name="gravity"></param>
        /// <param name="maxFallSpeed"></param>
        public virtual void Update(Item item, ref float gravity, ref float maxFallSpeed) {

        }
        /// <summary>
        /// 该饰品被玩家装备时会执行的函数
        /// </summary>
        /// <param name="item"></param>
        /// <param name="player"></param>
        /// <param name="hideVisual"></param>
        public virtual void UpdateAccessory(Item item, Player player, bool hideVisual) {

        }
        /// <summary>
        /// 用于设置盔甲套装效果，这个钩子只在头盔上生效
        /// </summary>
        public virtual void UpdateArmorByHead(Player player, Item body, Item legs) {

        }
        /// <summary>
        /// 用于设置装备效果，比如伤害加成
        /// </summary>
        public virtual void UpdateEquip(Item item, Player player) {

        }
        /// <summary>
        /// 该饰品被玩家装备时会执行的函数
        /// </summary>
        /// <param name="item"></param>
        /// <param name="player"></param>
        /// <param name="hideVisual"></param>
        /// <returns>返回默认值<see langword="true"/>会继续执行TML的默认行为，
        /// 返回<see langword="false"/>将会直接阻断后续所有修改的运行</returns>
        public virtual bool On_UpdateAccessory(Item item, Player player, bool hideVisual) {
            return true;
        }
        /// <summary>
        /// 这个物品在玩家库存中会执行的函数
        /// </summary>
        /// <param name="item"></param>
        /// <param name="player"></param>
        public virtual void UpdateInventory(Item item, Player player) {

        }
        /// <summary>
        /// 当物品的使用动画开始时会执行的函数
        /// </summary>
        /// <param name="item"></param>
        /// <param name="player"></param>
        public virtual void UseAnimation(Item item, Player player) {

        }
        /// <summary>
        /// 当物品的使用动画开始时会执行的函数，这个钩子的优先级大于TML的默认钩子
        /// </summary>
        /// <param name="item"></param>
        /// <param name="player"></param>
        /// <returns>返回默认值<see langword="null"/>会继续执行TML的默认行为，
        /// 返回<see langword="true"/>将只会原物品的<see cref="ModItem.UseAnimation(Player)"/>
        /// 而阻断<see cref="GlobalItem.UseAnimation(Item, Player)"/>的运行，
        /// 返回<see langword="false"/>将会直接阻断后续所有修改的运行</returns>
        public virtual bool? On_UseAnimation(Item item, Player player) {
            return null;
        }
        /// <summary>
        /// 使用物品时会调用的函数
        /// </summary>
        /// <param name="item"></param>
        /// <param name="player"></param>
        /// <returns>返回默认值<see langword="null"/>会继续执行该物品的原默认方法</returns>
        public virtual bool? UseItem(Item item, Player player) {
            return null;
        }
        /// <summary>
        /// 使用物品时会调用的函数，该方法执行优先级大于TML的装载钩子
        /// </summary>
        /// <param name="item"></param>
        /// <param name="player"></param>
        /// <returns>返回默认值<see langword="null"/>会继续执行TML的默认行为，返回非空值将会直接阻断后续所有修改的运行</returns>
        public virtual bool? On_UseItem(Item item, Player player) {
            return null;
        }
        /// <summary>
        /// 使用物品时会调用的函数，用于修改物品动画
        /// </summary>
        /// <param name="item"></param>
        /// <param name="player"></param>
        /// <returns>返回默认值<see langword="null"/>会继续执行该物品的原默认方法</returns>
        public virtual void UseItemFrame(Item item, Player player) {

        }
        /// <summary>
        /// 用于修改近战物品的使用碰撞箱体
        /// </summary>
        /// <param name="item"></param>
        /// <param name="player"></param>
        /// <param name="hitbox"></param>
        /// <param name="noHitbox"></param>
        public virtual void UseItemHitbox(Item item, Player player, ref Rectangle hitbox, ref bool noHitbox) {

        }
        /// <summary>
        /// 修改物品使用过程中的位置和中心偏移
        /// </summary>
        /// <param name="item"></param>
        /// <param name="player"></param>
        /// <param name="heldItemFrame"></param>
        public virtual void UseStyle(Item item, Player player, Rectangle heldItemFrame) {

        }
        /// <summary>
        /// 修改翅膀的各种移动数据
        /// </summary>
        /// <param name="item"></param>
        /// <param name="player"></param>
        /// <param name="ascentWhenFalling"></param>
        /// <param name="ascentWhenRising"></param>
        /// <param name="maxCanAscendMultiplier"></param>
        /// <param name="maxAscentMultiplier"></param>
        /// <param name="constantAscend"></param>
        public virtual void VerticalWingSpeeds(Item item, Player player, ref float ascentWhenFalling, ref float ascentWhenRising, ref float maxCanAscendMultiplier, ref float maxAscentMultiplier, ref float constantAscend) {

        }
        /// <summary>
        /// 当玩家按下Up键时让一些翅膀事件发生
        /// </summary>
        /// <param name="wings"></param>
        /// <param name="player"></param>
        /// <param name="inUse"></param>
        /// <returns>返回默认值<see langword="null"/>会继续执行该物品的原默认方法</returns>
        public virtual bool? WingUpdate(int wings, Player player, bool inUse) {
            return null;
        }
        /// <summary>
        /// 返回<see langword="false"/>时禁止玩家切换该物品
        /// </summary>
        /// <param name="item"></param>
        /// <param name="player"></param>
        /// <returns></returns>
        public virtual bool? CanSwitchWeapon(Item item, Player player) {
            return null;
        }
        /// <summary>
        /// 修改这个物品的配方，注意，添加类操作不建议写在此处，因为这个函数在物品有多个同结果配方的情况下可能会被调用多次
        /// </summary>
        /// <param name="recipe"></param>
        public virtual void ModifyRecipe(Recipe recipe) {

        }
        /// <summary>
        /// 添加这个物品的配方，如果需要进行修改操作，建议使用<see cref="ModifyRecipe"/>
        /// </summary>
        public virtual void AddRecipe() {

        }
        /// <summary>
        /// 快捷调用一个创建配方的窗口
        /// </summary>
        /// <param name="amount">需求数量，默认为1</param>
        /// <returns></returns>
        public Recipe CreateRecipe(int amount = 1) => Recipe.Create(TargetID, amount);
    }
}
