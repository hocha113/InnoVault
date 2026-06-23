using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;
using Terraria;
using Terraria.Graphics.Renderers;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace InnoVault.GameSystem
{
    /// <summary>
    /// 提供一个强行覆盖目标NPC行为性质的基类，通过On钩子为基础运行
    /// <br>该API的使用介绍:<see href="https://innovault.wiki/cn/overrides/npc/"/></br>
    /// </summary>
    public class NPCOverride : VaultType<NPCOverride>
    {
        #region Data
        /// <summary>
        /// 所有修改的实例集合
        /// </summary>
        public new static List<NPCOverride> Instances { get; internal set; } = [];
        /// <summary>
        /// 从ID映射到实例
        /// </summary>
        public static Dictionary<ushort, NPCOverride> OverrideIDToInstances { get; internal set; } = [];
        /// <summary>
        /// 从类型映射到ID
        /// </summary>
        public static Dictionary<Type, ushort> TypeToOverrideID { get; internal set; } = [];
        /// <summary>
        /// 从ID映射到类型
        /// </summary>
        public static Dictionary<ushort, Type> OverrideIDToType { get; internal set; } = [];
        /// <summary>
        /// 一个字典，可以根据目标ID来获得对应的修改实例
        /// </summary>
        public new static Dictionary<int, Dictionary<Type, NPCOverride>> ByID { get; internal set; } = [];
        /// <summary>
        /// 通用修改实例列表，这些实例会应用到所有目标上
        /// </summary>
        public new static List<NPCOverride> UniversalInstances { get; internal set; } = [];
        /// <summary>
        /// 该重制阶段所属的Mod
        /// </summary>
        public new Mod Mod => TypeToMod[GetType()];
        /// <summary>
        /// 要修改的NPC的ID值
        /// </summary>
        public virtual int TargetID => NPCID.None;
        /// <summary>
        /// 最大AI槽位数量，12个槽位，那么最大的数组索引就是11
        /// </summary>
        internal const int MaxAISlot = 12;
        /// <summary>
        /// ai槽位，使用<see cref="NetAISend()"/>来在多人游戏中同步这个字段的内容，在合适的时机调用它
        /// </summary>
        public float[] ai = new float[MaxAISlot];
        /// <summary>
        /// 本地ai槽位，这个值不会自动多人同步，如果有需要，重载<see cref="OtherNetWorkReceive(BinaryReader)"/>来同步它
        /// </summary>
        public float[] localAI = new float[MaxAISlot];
        /// <summary>
        /// 这个实例对应的NPC实例
        /// </summary>
        public NPC npc { get; private set; }
        /// <summary>
        /// 这个重制节点的ID，在多人模式中共享服务端的结果
        /// </summary>
        public ushort OverrideID;
        //服务端待发送的脏标记，仅承载 Other/SyncVar（ai 直接走 npc.netUpdate 搭车 vanilla 同步）
        private NPCOverrideSyncField _pendingSync = NPCOverrideSyncField.None;
        /// <summary>
        /// 用于网络同步，只能在服务端进行设置，其他端口永远返回<see langword="false"/>，
        /// 设置为<see langword="true"/>时会在本帧末尾经 Event 通道同步 <see cref="OtherNetWorkSend"/> 与 <see cref="SyncVarAttribute"/> 数据
        /// <br/>兼容保留，等价于 <see cref="RequestSync"/>(<see cref="NPCOverrideSyncField.Other"/> | <see cref="NPCOverrideSyncField.SyncVar"/>)
        /// </summary>
        public bool NetOtherWorkSend {
            get => VaultUtils.isServer && (_pendingSync & (NPCOverrideSyncField.Other | NPCOverrideSyncField.SyncVar)) != NPCOverrideSyncField.None;
            set {
                if (value) {
                    if (VaultUtils.isServer) {
                        _pendingSync |= NPCOverrideSyncField.Other | NPCOverrideSyncField.SyncVar;
                    }
                }
                else {
                    _pendingSync &= ~(NPCOverrideSyncField.Other | NPCOverrideSyncField.SyncVar);
                }
            }
        }
        /// <summary>
        /// 用于网络同步，只能在服务端进行设置，其他端口永远返回<see langword="false"/>，
        /// 设置为<see langword="true"/>时等价于 <c>npc.netUpdate = true</c>，通过 vanilla SyncNPC 同步 <see cref="ai"/>
        /// <br/>兼容保留，等价于 <see cref="RequestSync"/>(<see cref="NPCOverrideSyncField.Ai"/>)
        /// </summary>
        public bool NetAIWorkSend {
            get => VaultUtils.isServer && npc != null && npc.netUpdate;
            set {
                if (value && VaultUtils.isServer && npc != null) {
                    npc.netUpdate = true;
                }
            }
        }
        /// <summary>
        /// 是否为该重制节点启用高频增量(delta)流式同步通道，默认<see langword="false"/>。
        /// <br/>启用后其 <see cref="ai"/> 会以受控 cadence、按距离向附近客户端推送，且只发送相对上次快照变化的槽位，
        /// 适合每帧连续变化的自定义 ai 值；它是 <c>npc.netUpdate</c> 的替代品，不要对同一组 ai 同时高频使用两者以免重复发送
        /// </summary>
        public bool StreamNetDelta;
        #endregion
        /// <summary>
        /// 封闭加载
        /// </summary>
        protected sealed override void VaultRegister() {
            OverrideID = (ushort)Instances.Count;//从0开始的ID
            OverrideIDToInstances.Add(OverrideID, this);
            TypeToOverrideID.Add(GetType(), OverrideID);
            OverrideIDToType.Add(OverrideID, GetType());
            Instances.Add(this);
        }
        /// <summary>
        /// 加载内容
        /// </summary>
        public sealed override void VaultSetup() {
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
        public NPCOverride Clone() => (NPCOverride)Activator.CreateInstance(GetType());
        /// <summary>
        /// 寻找对应NPC实例的重载实例
        /// </summary>
        /// <param name="id"></param>
        /// <param name="npcOverrides"></param>
        /// <returns></returns>
        public static bool TryFetchByID(int id, out Dictionary<Type, NPCOverride> npcOverrides) {
            npcOverrides = null;

            if (!ByID.TryGetValue(id, out Dictionary<Type, NPCOverride> npcResults)) {
                return false;
            }

            Dictionary<Type, NPCOverride> result = null;

            foreach (var npcOverrideInstance in npcResults.Values) {
                if (!npcOverrideInstance.CanOverride()) {
                    continue;
                }

                result ??= [];
                result[npcOverrideInstance.GetType()] = npcOverrideInstance.Clone();
            }

            if (result == null) {
                return false;
            }

            npcOverrides = result;
            return true;
        }
        /// <summary>
        /// 仅用于全局重制节点设置临时NPC实例
        /// </summary>
        /// <param name="setNPC"></param>
        internal void UniversalSetNPCInstance(NPC setNPC) => npc = setNPC;
        /// <summary>
        /// 加载并初始化重制节点到对应的NPC实例上
        /// </summary>
        /// <param name="npc"></param>
        public static void SetDefaults(NPC npc) {
            if (!VaultLoad.LoadenContent) {
                return;
            }

            if (!TryFetchByID(npc.type, out Dictionary<Type, NPCOverride> inds) || inds == null) {
                return;
            }

            if (!npc.TryGetGlobalNPC(out NPCRebuildLoader globalInstance)) {
                return;
            }

            globalInstance.NPCOverrides = inds;

            //遍历所有克隆出的实例
            foreach (var overrideInstance in inds.Values) {
                //为实例设置NPC上下文并初始化
                overrideInstance.OverrideID = TypeToOverrideID[overrideInstance.GetType()];
                overrideInstance.ai = new float[MaxAISlot];
                overrideInstance.localAI = new float[MaxAISlot];
                overrideInstance.npc = npc;
                overrideInstance.SetProperty();

                //使用已加载的静态钩子列表的高效查询能力，将实例分发到对应的专属列表中
                if (NPCRebuildLoader.HookAI.HookOverrideQuery.HasOverride(overrideInstance)) {
                    globalInstance.AIOverrides.Add(overrideInstance);
                }
                if (NPCRebuildLoader.HookPostAI.HookOverrideQuery.HasOverride(overrideInstance)) {
                    globalInstance.PostAIOverrides.Add(overrideInstance);
                }
                if (NPCRebuildLoader.HookOn_PreKill.HookOverrideQuery.HasOverride(overrideInstance)) {
                    globalInstance.On_PreKillOverrides.Add(overrideInstance);
                }
                if (NPCRebuildLoader.HookCheckActive.HookOverrideQuery.HasOverride(overrideInstance)) {
                    globalInstance.CheckActiveOverrides.Add(overrideInstance);
                }
                if (NPCRebuildLoader.HookCheckDead.HookOverrideQuery.HasOverride(overrideInstance)) {
                    globalInstance.CheckDeadOverrides.Add(overrideInstance);
                }
                if (NPCRebuildLoader.HookSpecialOnKill.HookOverrideQuery.HasOverride(overrideInstance)) {
                    globalInstance.SpecialOnKillOverrides.Add(overrideInstance);
                }
                if (NPCRebuildLoader.HookOnCheckDead.HookOverrideQuery.HasOverride(overrideInstance)) {
                    globalInstance.OnCheckActiveOverrides.Add(overrideInstance);
                }
                if (NPCRebuildLoader.HookDraw.HookOverrideQuery.HasOverride(overrideInstance)) {
                    globalInstance.DrawOverrides.Add(overrideInstance);
                }
                if (NPCRebuildLoader.HookPostDraw.HookOverrideQuery.HasOverride(overrideInstance)) {
                    globalInstance.PostDrawOverrides.Add(overrideInstance);
                }
                if (NPCRebuildLoader.HookFindFrame.HookOverrideQuery.HasOverride(overrideInstance)) {
                    globalInstance.FindFrameOverrides.Add(overrideInstance);
                }
                if (NPCRebuildLoader.HookModifyNPCLoot.HookOverrideQuery.HasOverride(overrideInstance)) {
                    globalInstance.ModifyNPCLootOverrides.Add(overrideInstance);
                }
                if (NPCRebuildLoader.HookOnHitByItem.HookOverrideQuery.HasOverride(overrideInstance)) {
                    globalInstance.OnHitByItemOverrides.Add(overrideInstance);
                }
                if (NPCRebuildLoader.HookOnHitByProjectile.HookOverrideQuery.HasOverride(overrideInstance)) {
                    globalInstance.OnHitByProjectileOverrides.Add(overrideInstance);
                }
                if (NPCRebuildLoader.HookModifyHitByItem.HookOverrideQuery.HasOverride(overrideInstance)) {
                    globalInstance.ModifyHitByItemOverrides.Add(overrideInstance);
                }
                if (NPCRebuildLoader.HookModifyHitByProjectile.HookOverrideQuery.HasOverride(overrideInstance)) {
                    globalInstance.ModifyHitByProjectileOverrides.Add(overrideInstance);
                }
                if (NPCRebuildLoader.HookCanBeHitByItem.HookOverrideQuery.HasOverride(overrideInstance)) {
                    globalInstance.CanBeHitByItemOverrides.Add(overrideInstance);
                }
                if (NPCRebuildLoader.HookCanBeHitByNPC.HookOverrideQuery.HasOverride(overrideInstance)) {
                    globalInstance.CanBeHitByNPCOverrides.Add(overrideInstance);
                }
                if (NPCRebuildLoader.HookCanBeHitByProjectile.HookOverrideQuery.HasOverride(overrideInstance)) {
                    globalInstance.CanBeHitByProjectileOverrides.Add(overrideInstance);
                }
            }
        }
        /// <summary>
        /// 用于 <see cref="On_OnHitByProjectile"/> 的前置条件判断，
        /// 返回 <see langword="true"/> 时，才会执行相应的覆盖逻辑，通常用于筛选特定的弹幕ID
        /// </summary>
        public virtual bool On_OnHitByProjectile_IfSpan(Projectile proj) => false;
        /// <summary>
        /// 用于覆盖NPC的受击伤害计算公式
        /// </summary>
        /// <param name="npc"></param>
        /// <param name="modifiers"></param>
        /// <returns>返回<see langword="null"/>会继续执行原来的方法，包括原ModNPC方法与G方法
        /// 返回<see langword="true"/>仅仅会继续执行原ModNPC方法而阻止全局NPC类的额外修改运行
        /// 返回<see langword="false"/>阻止后续所有修改的运行</returns>
        public virtual bool? On_ModifyIncomingHit(NPC npc, ref NPC.HitModifiers modifiers) {
            return null;
        }
        //用于调用针对实例的On_ModifyIncomingHit
        internal bool DoModifyIncomingHitByInstance(ref NPC.HitModifiers modifiers) {
            bool? shouldOverride = On_ModifyIncomingHit(npc, ref modifiers);

            if (!shouldOverride.HasValue) {
                return true;
            }

            if (shouldOverride.Value) {
                npc.ModNPC?.ModifyIncomingHit(ref modifiers);
            }

            return false;
        }
        /// <summary>
        /// 用于覆盖NPC的弹幕受击行为
        /// </summary>
        /// <param name="npc"></param>
        /// <param name="projectile"></param>
        /// <param name="hit"></param>
        /// <param name="damageDone"></param>
        /// <returns>返回<see langword="null"/>会继续执行原来的方法，包括原ModNPC方法与G方法
        /// 返回<see langword="true"/>仅仅会继续执行原ModNPC方法而阻止全局NPC类的额外修改运行
        /// 返回<see langword="false"/>阻止后续所有修改的运行</returns>
        public virtual bool? On_OnHitByProjectile(NPC npc, Projectile projectile, in NPC.HitInfo hit, int damageDone) {
            return null;
        }
        //用于调用针对实例的On_OnHitByProjectile
        internal bool DoHitByProjectileByInstance(Projectile projectile, in NPC.HitInfo hit, int damageDone) {
            bool? shouldOverride = null;
            if (On_OnHitByProjectile_IfSpan(projectile)) {
                shouldOverride = On_OnHitByProjectile(npc, projectile, hit, damageDone);
            }

            if (!shouldOverride.HasValue) {
                return true;
            }

            if (shouldOverride.Value) {
                npc.ModNPC?.OnHitByProjectile(projectile, hit, damageDone);
            }

            return false;
        }
        /// <summary>
        /// 用于覆盖NPC的物品受击行为
        /// </summary>
        /// <param name="player"></param>
        /// <param name="item"></param>
        /// <param name="hit"></param>
        /// <param name="damageDone"></param>
        /// <returns></returns>
        public virtual bool On_OnHitByItem(Player player, Item item, in NPC.HitInfo hit, int damageDone) {
            return true;
        }
        /// <summary>
        /// 在npc生成的时候调用一次，用于初始化一些实例数据
        /// </summary>
        public virtual void SetProperty() { }
        /// <summary>
        /// 允许编辑ai或者阻断ai运行，返回默认值<see langword="true"/>则会继续运行后续ai行为
        /// ，返回<see langword="false"/>会阻断所有后续ai行为的调用
        /// </summary>
        /// <returns></returns>
        public virtual bool AI() { return true; }
        /// <summary>
        /// 允许在ai运行后进行一些操作
        /// </summary>
        public virtual void PostAI() { }
        /// <summary>
        /// 允许编辑死亡事件，返回非null值可以阻断后续逻辑的运行
        /// </summary>
        /// <returns></returns>
        public virtual bool? On_PreKill() { return null; }
        /// <summary>
        /// 允许编辑死亡检测逻辑，返回非null值可以阻断后续逻辑的运行
        /// </summary>
        /// <returns></returns>
        public virtual bool? CheckDead() { return null; }
        /// <summary>
        /// 允许编辑击杀事件，返回非null值可以阻断后续逻辑的运行
        /// </summary>
        /// <returns></returns>
        public virtual bool? SpecialOnKill() { return null; }
        /// <summary>
        /// 允许编辑活跃检测逻辑
        /// </summary>
        /// <returns></returns>
        public virtual bool CheckActive() => true;
        /// <summary>
        /// 允许编辑活跃检测逻辑，返回非null值可以阻断后续逻辑的运行
        /// </summary>
        /// <returns></returns>
        public virtual bool? On_CheckActive() => null;
        /// <summary>
        /// 编辑NPC在地图上的图标ID
        /// </summary>
        /// <param name="index"></param>
        public virtual void BossHeadSlot(ref int index) { }
        /// <summary>
        /// 编辑NPC在地图上的头像旋转角
        /// </summary>
        /// <param name="rotation"></param>
        public virtual void BossHeadRotation(ref float rotation) { }
        /// <summary>
        /// 编辑NPC在地图上的头像翻转状态
        /// </summary>
        /// <param name="spriteEffects"></param>
        public virtual void BossHeadSpriteEffects(ref SpriteEffects spriteEffects) { }
        /// <summary>
        /// 运行在 <see cref="BossHeadSlot"/> 和 <see cref="NPC.GetBossHeadTextureIndex"/> 之前<br/>
        /// 如果返回大于0的值，将阻止后续所有逻辑的运行，默认返回 -1
        /// </summary>
        public virtual int GetBossHeadTextureIndex() { return -1; }
        /// <summary>
        /// 运行在 <see cref="BossHeadRotation"/> 和 <see cref="NPC.GetBossHeadRotation"/> 之前<br/>
        /// 如果返回有效值，将阻止后续所有逻辑的运行，默认返回 <see langword="null"/>
        /// </summary>
        public virtual float? GetBossHeadRotation() { return null; }
        /// <summary>
        /// 运行在 <see cref="BossHeadSpriteEffects"/> 和 <see cref="NPC.GetBossHeadSpriteEffects"/> 之前<br/>
        /// 如果返回有效值，将阻止后续所有逻辑的运行，默认返回 <see langword="null"/>
        /// </summary>
        public virtual SpriteEffects? GetBossHeadSpriteEffects() { return null; }
        /// <summary>
        /// 允许编辑Boss头像绘制的数据
        /// </summary>
        /// <param name="x">绘制横坐标</param>
        /// <param name="y">绘制纵坐标</param>
        /// <param name="bossHeadId">Boss头像的资源ID</param>
        /// <param name="alpha">绘制时的透明度值 范围0-255</param>
        /// <param name="headScale">头像绘制的缩放比例</param>
        /// <param name="rotation">头像绘制的旋转角度</param>
        /// <param name="effects">绘制时使用的精灵翻转效果</param>
        public virtual void ModifyDrawNPCHeadBoss(ref float x, ref float y, ref int bossHeadId
            , ref byte alpha, ref float headScale, ref float rotation, ref SpriteEffects effects) { }
        /// <summary>
        /// 允许在Boss头像绘制前进行覆盖绘制 返回<see langword="false"/>可以阻止原版Boss头像的绘制
        /// </summary>
        /// <param name="renderer">用于绘制Boss头像的渲染器</param>
        /// <param name="drawPos">绘制位置的屏幕坐标</param>
        /// <param name="bossHeadId">Boss头像的资源ID</param>
        /// <param name="alpha">绘制时的透明度值 范围0-255</param>
        /// <param name="headScale">头像绘制的缩放比例</param>
        /// <param name="rotation">头像绘制的旋转角度</param>
        /// <param name="effects">绘制时使用的精灵翻转效果</param>
        /// <returns>返回true继续执行原版逻辑 返回false阻止原版Boss头像绘制</returns>
        public virtual bool PreDrawNPCHeadBoss(NPCHeadRenderer renderer, Vector2 drawPos, int bossHeadId
            , byte alpha, float headScale, float rotation, SpriteEffects effects) { return true; }
        /// <summary>
        /// 编辑此NPC的血条绘制状态，返回<see langword="false"/>可以阻止后续逻辑运行
        /// </summary>
        /// <param name="hbPosition"></param>
        /// <param name="scale"></param>
        /// <param name="position"></param>
        public virtual bool? DrawHealthBar(byte hbPosition, ref float scale, ref Vector2 position) { return null; }
        /// <summary>
        /// 修改NPC的鼠标悬浮碰撞判定
        /// </summary>
        public virtual void ModifyHoverBoundingBox(ref Rectangle boundingBox) { }
        /// <summary>
        /// 编辑NPC的掉落
        /// </summary>
        /// <param name="thisNPC"></param>
        /// <param name="npcLoot"></param>
        public virtual void ModifyNPCLoot(NPC thisNPC, NPCLoot npcLoot) { }
        /// <summary>
        /// 是否允许NPC通过平台
        /// </summary>
        /// <returns></returns>
        public virtual bool? CanFallThroughPlatforms() { return null; }
        /// <summary>
        /// 在NPC商店打开时修改货架内容
        /// </summary>
        /// <param name="shopName">商店名</param>
        /// <param name="items">货架物品列表</param>
        public virtual void ModifyActiveShop(string shopName, Item[] items) { }
        /// <summary>
        /// 用于判定该友好NPC是否传送到雕像处
        /// </summary>
        /// <param name="toKingStatue">是否是国王雕像，否则是女皇雕像</param>
        /// <returns></returns>
        public virtual bool? CanGoToStatue(bool toKingStatue) { return null; }
        /// <summary>
        /// 修改聊天内容，每次打开聊天框时会调用一次该钩子
        /// </summary>
        /// <param name="chat"></param>
        public virtual void GetChat(ref string chat) { }
        /// <summary>
        /// 允许修改NPC聊天栏中按钮的名称，在 <see cref="Player.talkNPC"/>不为 -1 时调用<br/>
        /// 不建议在这里使用硬编码字符，应当使用本地化
        /// </summary>
        /// <param name="button"></param>
        /// <param name="button2"></param>
        /// <returns></returns>
        public virtual bool SetChatButtons(ref string button, ref string button2) { return true; }
        /// <summary>
        /// 在派对期间，这个友好NPC是否应该戴上派对帽<br/>
        /// 运行在 <see cref="UsesPartyHat"/> 与 <see cref="NPC.UsesPartyHat()"/> 之前<br/>
        /// 返回有效值会阻止原版逻辑的运行
        /// </summary>
        /// <returns></returns>
        public virtual bool? PreUsesPartyHat() { return null; }
        /// <summary>
        /// 在派对期间，这个友好NPC是否应该戴上派对帽<br/>
        /// 返回有效值会阻止原版逻辑的运行
        /// </summary>
        /// <returns></returns>
        public virtual bool? UsesPartyHat() { return null; }
        /// <summary>
        /// 在NPC聊天栏中点击按钮时调用，运行在 <see cref="OnChatButtonClicked"/> 之前，返回<see langword="false"/>可以阻止其运行
        /// </summary>
        /// <param name="firstButton">是否是第一个按钮</param>
        public virtual bool PreChatButtonClicked(bool firstButton) { return true; }
        /// <summary>
        /// 在NPC聊天栏中点击按钮时调用
        /// </summary>
        /// <param name="firstButton">是否是第一个按钮</param>
        public virtual void OnChatButtonClicked(bool firstButton) { }
        /// <summary>
        /// 修改被物品击中的伤害
        /// </summary>
        /// <param name="player"></param>
        /// <param name="item"></param>
        /// <param name="modifiers"></param>
        public virtual void ModifyHitByItem(Player player, Item item, ref NPC.HitModifiers modifiers) { }
        /// <summary>
        /// 修改被弹幕击中的伤害
        /// </summary>
        /// <param name="projectile"></param>
        /// <param name="modifiers"></param>
        public virtual void ModifyHitByProjectile(Projectile projectile, ref NPC.HitModifiers modifiers) { }
        /// <summary>
        /// 是否可以被物品击中
        /// </summary>
        /// <param name="player"></param>
        /// <param name="item"></param>
        /// <returns></returns>
        public virtual bool? CanBeHitByItem(Player player, Item item) { return null; }
        /// <summary>
        /// 这个友好NPC是否可以被其他敌对NPC击中
        /// </summary>
        /// <param name="attacker"></param>
        /// <returns></returns>
        public virtual bool? CanBeHitByNPC(NPC attacker) { return null; }
        /// <summary>
        /// 这个友好NPC是否可以被敌对弹幕击中
        /// </summary>
        /// <param name="projectile"></param>
        /// <returns></returns>
        public virtual bool? CanBeHitByProjectile(Projectile projectile) { return null; }
        /// <summary>
        /// 保存该NPC的实例数据
        /// </summary>
        /// <param name="tag"></param>
        public virtual void SaveData(TagCompound tag) { }
        /// <summary>
        /// 加载该NPC的实例数据，如果<see cref="SaveData(TagCompound)"/>没有存入任何数据，则不会调用这个方法
        /// </summary>
        /// <param name="tag"></param>
        public virtual void LoadData(TagCompound tag) { }
        /// <summary>
        /// 是否需要保存该NPC的实例数据，默认返回<see langword="false"/><br/>
        /// 该函数使用短路逻辑，如果前面的钩子返回了<see langword="true"/>，则不会被调用
        /// </summary>
        /// <returns></returns>
        public virtual bool NeedSaving() { return false; }
        /// <summary>
        /// 更新NPC的帧数据，返回 <see langword="false"/> 可以阻止后续逻辑运行
        /// </summary>
        /// <param name="frameHeight"></param>
        public virtual bool FindFrame(int frameHeight) { return true; }
        /// <summary>
        /// 修改绘制
        /// </summary>
        /// <param name="spriteBatch"></param>
        /// <param name="screenPos"></param>
        /// <param name="drawColor"></param>
        /// <returns></returns>
        public virtual bool? Draw(SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor) { return null; }
        /// <summary>
        /// 修改后层绘制
        /// </summary>
        /// <param name="spriteBatch"></param>
        /// <param name="screenPos"></param>
        /// <param name="drawColor"></param>
        /// <returns></returns>
        public virtual bool PostDraw(SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor) { return true; }


        #region NetWork
        /// <summary>
        /// 用于网络同步 <see cref="ai"/>，搭车 vanilla 的 SyncNPC(ExtraAI) 通道，保证与位置/速度原子到达。
        /// 使用位掩码仅发送非零的 ai 槽位以减少带宽。重写时务必与 <see cref="NetReceive"/> 对应
        /// </summary>
        /// <param name="writer"></param>
        public virtual void NetSend(BinaryWriter writer) => NPCOverrideNetWork.WriteAiMasked(writer, ai);
        /// <summary>
        /// 用于网络同步 <see cref="ai"/>，与 <see cref="NetSend"/> 对应，未发送的槽位会被清零
        /// </summary>
        /// <param name="reader"></param>
        public virtual void NetReceive(BinaryReader reader) => NPCOverrideNetWork.ReadAiMasked(reader, ai);
        /// <summary>
        /// 统一的网络同步入口，请求同步该重制节点的指定内容
        /// <br/>服务端：<see cref="NPCOverrideSyncField.Ai"/> 通过 <c>npc.netUpdate</c> 搭车 vanilla 同步；
        /// <see cref="NPCOverrideSyncField.Other"/>/<see cref="NPCOverrideSyncField.SyncVar"/> 在本帧末尾经 Event 通道广播
        /// <br/>客户端：立即将数据发往服务端，由服务端校验后广播给其他客户端
        /// </summary>
        /// <param name="fields">要同步的数据种类，默认全部</param>
        public void RequestSync(NPCOverrideSyncField fields = NPCOverrideSyncField.All) {
            if (VaultUtils.isSinglePlayer || npc == null || fields == NPCOverrideSyncField.None) {
                return;
            }
            if (VaultUtils.isClient) {
                NPCOverrideNetWork.SendEvent(this, fields);
                return;
            }
            if ((fields & NPCOverrideSyncField.Ai) != 0) {
                npc.netUpdate = true;
            }
            _pendingSync |= fields & (NPCOverrideSyncField.Other | NPCOverrideSyncField.SyncVar);
        }
        /// <summary>
        /// 服务端每帧调用，刷新待发送的 Other/SyncVar 脏数据。调用方已保证处于服务端
        /// </summary>
        internal void DoNetWork() {
            if (_pendingSync == NPCOverrideSyncField.None) {
                return;
            }
            NPCOverrideNetWork.SendEvent(this, _pendingSync);
            _pendingSync = NPCOverrideSyncField.None;
        }

        /// <summary>
        /// 发送网络数据，同步额外的网络数据，重载编写时需要注意与<see cref="OtherNetWorkReceive"/>对应
        /// ，将<see cref="NetOtherWorkSend"/>设置为<see langword="true"/>后自动进行一次发包
        /// </summary>
        public virtual void OtherNetWorkSend(ModPacket netMessage) { }

        /// <summary>
        /// 接受网络数据，同步额外的网络数据，重载编写时需要注意与<see cref="OtherNetWorkSend"/>对应
        /// </summary>
        public virtual void OtherNetWorkReceive(BinaryReader reader) { }

        /// <summary>
        /// 发送网络数据，同步<see cref="ai"/>的值，只会在服务端上运行
        /// </summary>
        [Obsolete("已经过时，如果需要同步，直接设置 `npc.netUpdate = true`")]
        public void NetAISend() {
            if (!VaultUtils.isServer) {
                return;
            }

            npc.netUpdate = true;//强制更新NPC
        }

        /// <summary>
        /// 发送网络数据，同步<see cref="ai"/>的值，只会在服务端上运行
        /// </summary>
        /// <param name="npc"></param>
        [Obsolete("已经过时，如果需要同步，直接设置 `npc.netUpdate = true`")]
        public static void NetAISend(NPC npc) {
            if (!VaultUtils.isServer) {
                return;
            }

            npc.netUpdate = true;//强制更新NPC
        }

        /// <summary>
        /// 允许客户端主动将数据发送到服务器并由服务器广播给其他客户端
        /// </summary>
        [Obsolete("请使用 RequestSync(NPCOverrideSyncField.All)")]
        public void SendNetworkData() => RequestSync(NPCOverrideSyncField.All);

        /// <summary>
        /// 接收网络数据的兼容入口，内部转发到统一的 Event 处理
        /// </summary>
        /// <param name="reader"></param>
        /// <param name="whoAmI"></param>
        [Obsolete("内部处理入口，请使用 RequestSync")]
        public static void ReceiveNetworkData(BinaryReader reader, int whoAmI) => NPCOverrideNetWork.HandleEvent(reader, whoAmI);

        /// <summary>
        /// 请求服务器补发所有已加载的NPC重制节点数据
        /// </summary>
        [Obsolete("进入世界时框架会自动请求，可改用进图同步流程 NPCOverrideNetWork.OnEnterWorld")]
        public static void GetSever_NPCOverrideRequestAllData() => NPCOverrideNetWork.RequestBulk();
        #endregion
    }
}
