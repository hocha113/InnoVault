using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using static InnoVault.VaultNetWork;

namespace InnoVault.GameSystem
{
    public class NPCOverrideGlobalInstance : GlobalNPC
    {
        public override bool InstancePerEntity => true;
        public Dictionary<Type, NPCOverride> NPCOverrides { get; set; }
        public override bool AppliesToEntity(NPC entity, bool lateInstantiation) => lateInstantiation && NPCOverride.ByID.ContainsKey(entity.type);
        public override void SetDefaults(NPC npc) => NPCOverride.SetDefaults(npc);
    }

    /// <summary>
    /// 提供一个强行覆盖目标NPC行为性质的基类，通过On钩子为基础运行
    /// </summary>
    public class NPCOverride : ModType
    {
        #region Data
        /// <summary>
        /// 所有修改的实例集合
        /// </summary>
        public static List<NPCOverride> Instances { get; internal set; } = [];
        /// <summary>
        /// 一个字典，可以根据目标ID来获得对应的修改实例
        /// </summary>
        public static Dictionary<int, Dictionary<Type, NPCOverride>> ByID { get; internal set; } = [];
        /// <summary>
        /// 要修改的NPC的ID值，在目前为止，每一个类型的NPC只能有一个实例对应
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
        //不要直接设置这个
        private bool _netOtherWorkSend;
        //不要直接设置这个
        private bool _netAIWorkSend;
        /// <summary>
        /// 用于网络同步，只能在服务端进行设置，其他端口永远返回<see langword="false"/>，
        /// 当设置为<see langword="true"/>时，会自动调用<see cref="OtherNetWorkReceiveHander(BinaryReader)"/>进行网络数据同步
        /// </summary>
        public bool NetOtherWorkSend {
            get {
                if (!VaultUtils.isServer) {
                    return false;
                }
                return _netOtherWorkSend;
            }
            set => _netOtherWorkSend = value;
        }
        /// <summary>
        /// 用于网络同步，只能在服务端进行设置，其他端口永远返回<see langword="false"/>，
        /// 当设置为<see langword="true"/>时，会自动调用<see cref="NetAISend()"/>进行网络数据同步
        /// </summary>
        public bool NetAIWorkSend {
            get {
                if (!VaultUtils.isServer) {
                    return false;
                }
                return _netAIWorkSend;
            }
            set => _netAIWorkSend = value;
        }
        #endregion
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
        public NPCOverride Clone() => (NPCOverride)Activator.CreateInstance(GetType());
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
        /// 寻找对应NPC实例的重载实例
        /// </summary>
        /// <param name="id"></param>
        /// <param name="npcOverrides"></param>
        /// <returns></returns>
        public static bool TryFetchByID(int id, out Dictionary<Type, NPCOverride> npcOverrides) {
            npcOverrides = null;

            if (ByID.TryGetValue(id, out Dictionary<Type, NPCOverride> npcResults)) {
                return false;//通过ID查找NPCOverride，如果未找到，直接返回
            }

            bool result = true;//调用该NPCOverride的CanOverride方法，判断是否允许覆盖
            foreach (var npcOverrideInstance in npcResults.Values) {
                result = npcOverrideInstance.CanOverride();
            }

            if (result) {
                npcOverrides = [];
                foreach (var npcOverrideInstance in npcResults.Values) {
                    npcOverrides[npcOverrideInstance.GetType()] = npcOverrideInstance.Clone();
                }
                return true;
            }

            return false;
        }
        /// <summary>
        /// 加载并初始化重制节点到对应的NPC实例上
        /// </summary>
        /// <param name="npc"></param>
        public static void SetDefaults(NPC npc) {
            if (Main.gameMenu) {
                return;
            }

            if (!TryFetchByID(npc.type, out Dictionary<Type, NPCOverride> inds) || inds == null) {
                return;
            }

            foreach (var npcOverrideInstance in inds.Values) {
                npcOverrideInstance.ai = new float[MaxAISlot];
                npcOverrideInstance.localAI = new float[MaxAISlot];
                npcOverrideInstance.npc = npc;
                npcOverrideInstance.SetProperty();
            }

            if (npc.TryGetGlobalNPC(out NPCOverrideGlobalInstance globalInstance)) {
                globalInstance.NPCOverrides = inds;
            }
        }
        /// <summary>
        /// 这个属性用于<see cref="On_OnHitByProjectile"/>的实现，编辑方法生效的条件，一般判断会生效在那些目标弹幕ID之上
        /// </summary>
        public virtual bool On_OnHitByProjectile_IfSpan(Projectile proj) => false;
        /// <summary>
        /// 用于覆盖NPC的受击伤害计算公式
        /// </summary>
        /// <param name="npc"></param>
        /// <param name="modifiers"></param>
        /// <returns>返回<see langword="null"/>会继续执行原来的方法，包括原ModNPC方法与G方法。
        /// 返回<see langword="true"/>仅仅会继续执行原ModNPC方法而阻止全局NPC类的额外修改运行。
        /// 返回<see langword="false"/>阻止后续所有修改的运行</returns>
        public virtual bool? On_ModifyIncomingHit(NPC npc, ref NPC.HitModifiers modifiers) {
            return null;
        }
        /// <summary>
        /// 用于覆盖NPC的弹幕受击行为
        /// </summary>
        /// <param name="npc"></param>
        /// <param name="projectile"></param>
        /// <param name="hit"></param>
        /// <param name="damageDone"></param>
        /// <returns>返回<see langword="null"/>会继续执行原来的方法，包括原ModNPC方法与G方法。
        /// 返回<see langword="true"/>仅仅会继续执行原ModNPC方法而阻止全局NPC类的额外修改运行。
        /// 返回<see langword="false"/>阻止后续所有修改的运行</returns>
        public virtual bool? On_OnHitByProjectile(NPC npc, Projectile projectile, in NPC.HitInfo hit, int damageDone) {
            return null;
        }
        /// <summary>
        /// 用于覆盖NPC的物品受击行为
        /// </summary>
        /// <param name="npc"></param>
        /// <param name="projectile"></param>
        /// <param name="hit"></param>
        /// <param name="damageDone"></param>
        /// <returns>返回<see langword="null"/>会继续执行原来的方法，包括原ModNPC方法与G方法。
        /// 返回<see langword="true"/>仅仅会继续执行原ModNPC方法而阻止全局NPC类的额外修改运行。
        /// 返回<see langword="false"/>阻止后续所有修改的运行</returns>
        public virtual bool? On_OnHitByItem(NPC npc, Projectile projectile, in NPC.HitInfo hit, int damageDone) {
            return null;
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
        /// 允许编辑活跃检测逻辑
        /// </summary>
        /// <returns></returns>
        public virtual bool CheckActive() => true;
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
        /// 编辑NPC的掉落，注意，这个方法不会被生物AI设置阻止，注意，如果需要使用NPC实例，必须使用给出的参数thisNPC，而不是尝试访问<see cref="npc"/>
        /// </summary>
        /// <param name="thisNPC"></param>
        /// <param name="npcLoot"></param>
        public virtual void ModifyNPCLoot(NPC thisNPC, NPCLoot npcLoot) { }
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
        //统一管理的网络行为，自动运行在更新后
        internal void DoNet() {
            if (!VaultUtils.isServer) {
                return;
            }

            if (NetAIWorkSend) {
                NetAISend();
                NetAIWorkSend = false;
            }
            if (NetOtherWorkSend) {
                OtherNetWorkSendHander();
                NetOtherWorkSend = false;
            }
        }

        internal static void OtherNetWorkReceiveHander(BinaryReader reader) {
            NPC npc = Main.npc[reader.ReadInt32()];
            if (npc.TryGetOverride(out var values)) {
                foreach (var npcOverrideInstance in values.Values) {
                    npcOverrideInstance.OtherNetWorkReceive(reader);
                }
            }
        }

        internal void OtherNetWorkSendHander() {
            if (!VaultUtils.isServer) {
                return;
            }

            ModPacket netMessage = Mod.GetPacket();
            netMessage.Write((byte)MessageType.NPCOverrideOtherAI);
            netMessage.Write(npc.whoAmI);
            OtherNetWorkSend(netMessage);
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
        public void NetAISend() {
            if (!VaultUtils.isServer) {
                return;
            }

            var netMessage = Mod.GetPacket();
            netMessage.Write((byte)MessageType.NPCOverrideAI);
            netMessage.Write(npc.whoAmI);
            netMessage.Write(FullName);
            foreach (var aiValue in ai) {
                netMessage.Write(aiValue);
            }

            netMessage.Send();
        }

        /// <summary>
        /// 发送网络数据，同步<see cref="ai"/>的值，只会在服务端上运行
        /// </summary>
        /// <param name="npc"></param>
        public static void NetAISend(NPC npc) {
            if (!VaultUtils.isServer) {
                return;
            }

            if (!npc.TryGetOverride(out var values)) {
                return;
            }

            foreach (var value in values.Values) {
                var netMessage = value.Mod.GetPacket();
                netMessage.Write((byte)MessageType.NPCOverrideAI);
                netMessage.Write(npc.whoAmI);
                netMessage.Write(value.FullName);
                foreach (var aiValue in value.ai) {
                    netMessage.Write(aiValue);
                }

                netMessage.Send();
            }
        }

        /// <summary>
        /// 接收网络数据，同步<see cref="ai"/>的值
        /// </summary>
        /// <param name="reader"></param>
        internal static void NetAIReceive(BinaryReader reader) {
            NPC npc = Main.npc[reader.ReadInt32()];
            string fullName = reader.ReadString();
            float[] receiveAI = new float[MaxAISlot];
            for (int i = 0; i < MaxAISlot; i++) {
                receiveAI[i] = reader.ReadSingle();
            }

            if (!npc.active) {
                return;
            }

            if (!npc.TryGetOverride(out var values)) {
                return;
            }

            foreach (var value in values.Values) {
                if (value.FullName != fullName) {
                    continue;
                }
                for (int i = 0; i < MaxAISlot; i++) {
                    value.ai[i] = receiveAI[i];
                }
                break;
            }
        }

        #endregion
    }
}
