using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq.Expressions;
using System.Reflection;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using static InnoVault.GameSystem.NPCOverride;

namespace InnoVault.GameSystem
{
    /// <summary>
    /// 标记一个 <see cref="NPCOverride"/> 子类重写了哪些钩子，在加载期对模板预计算一次，
    /// 生成 NPC 时直接读掩码分发，避免每次生成重复 HasOverride 查询（其内部每次调用都会分配一个绑定委托）
    /// </summary>
    [Flags]
    internal enum NPCHookFlags : uint
    {
        None = 0,
        AI = 1 << 0,
        PostAI = 1 << 1,
        On_PreKill = 1 << 2,
        CheckActive = 1 << 3,
        CheckDead = 1 << 4,
        SpecialOnKill = 1 << 5,
        On_CheckActive = 1 << 6,
        Draw = 1 << 7,
        PostDraw = 1 << 8,
        FindFrame = 1 << 9,
        ModifyNPCLoot = 1 << 10,
        OnHitByItem = 1 << 11,
        OnHitByProjectile = 1 << 12,
        ModifyHitByItem = 1 << 13,
        ModifyHitByProjectile = 1 << 14,
        CanBeHitByItem = 1 << 15,
        CanBeHitByNPC = 1 << 16,
        CanBeHitByProjectile = 1 << 17,
    }

    /// <summary>
    /// 所有关于NPC行为覆盖和性质加载的钩子在此处挂载
    /// </summary>
    public class NPCRebuildLoader : GlobalNPC, IVaultLoader
    {
#pragma warning disable CS1591 //缺少对公共可见类型或成员的 XML 注释
        #region Data
        public delegate void On_NPCDelegate(NPC npc);
        public delegate bool On_NPCDelegate2(NPC npc);
        public delegate bool On_DrawDelegate(NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor);
        public delegate void On_DrawDelegate2(NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor);
        public delegate void On_OnHitByItemDelegate(NPC npc, Player player, Item item, in NPC.HitInfo hit, int damageDone);
        public delegate void On_OnHitByProjectileDelegate(NPC npc, Projectile projectile, in NPC.HitInfo hit, int damageDone);
        public delegate void On_ModifyIncomingHitDelegate(NPC npc, ref NPC.HitModifiers modifiers);
        public delegate void On_FindFrameDelegate(NPC npc, int frameHeight);
        public delegate void On_SetChatButtonsDelegate(ref string button, ref string button2);
        public delegate void On_NPCSetDefaultDelegate();
        public delegate bool DelegateOn_OnHitByItem(Player player, Item item, in NPC.HitInfo hit, int damageDone);
        public delegate bool? DelegateOn_OnHitByProjectile(NPC npc, Projectile projectile, in NPC.HitInfo hit, int damageDone);
        public delegate void DelegateModifyHitByItem(Player player, Item item, ref NPC.HitModifiers modifiers);
        public delegate void DelegateModifyHitByProjectile(Projectile projectile, ref NPC.HitModifiers modifiers);
        public delegate bool? DelegateCanBeHitByNPC(NPC attacker);
        public static event On_NPCDelegate PreSetDefaultsEvent;
        public static event On_NPCDelegate PostSetDefaultsEvent;
        public static Type npcLoaderType;
        public static MethodInfo onHitByItem_Method;
        public static MethodInfo onHitByProjectile_Method;
        public static MethodInfo modifyIncomingHit_Method;
        public static MethodInfo onFindFrame_Method;
        public static MethodInfo onSetChatButtons_Method;
        public static MethodInfo onNPCUsesPartyHat_Method;
        public static MethodInfo onUsesPartyHat_Method;
        public static MethodInfo onNPCAI_Method;
        public static MethodInfo onPreKill_Method;
        public static MethodInfo onPreDraw_Method;
        public static MethodInfo onPostDraw_Method;
        public static MethodInfo onCheckDead_Method;
        public override bool InstancePerEntity => true;
        //本次加载是否注册了任何重制节点，为假时所有 On_ 钩子直通原逻辑，免除每 NPC 每帧的空转查询
        internal static bool hasAnyOverrides;
        private static readonly List<VaultHookMethodCache<NPCOverride>> hooks = [];
        internal static VaultHookMethodCache<NPCOverride> HookAI;
        internal static VaultHookMethodCache<NPCOverride> HookPostAI;
        internal static VaultHookMethodCache<NPCOverride> HookOn_PreKill;
        internal static VaultHookMethodCache<NPCOverride> HookCheckActive;
        internal static VaultHookMethodCache<NPCOverride> HookCheckDead;
        internal static VaultHookMethodCache<NPCOverride> HookSpecialOnKill;
        internal static VaultHookMethodCache<NPCOverride> HookOnCheckDead;
        internal static VaultHookMethodCache<NPCOverride> HookDraw;
        internal static VaultHookMethodCache<NPCOverride> HookPostDraw;
        internal static VaultHookMethodCache<NPCOverride> HookFindFrame;
        internal static VaultHookMethodCache<NPCOverride> HookModifyNPCLoot;
        internal static VaultHookMethodCache<NPCOverride> HookOnHitByItem;
        internal static VaultHookMethodCache<NPCOverride> HookOnHitByProjectile;
        internal static VaultHookMethodCache<NPCOverride> HookModifyHitByItem;
        internal static VaultHookMethodCache<NPCOverride> HookModifyHitByProjectile;
        internal static VaultHookMethodCache<NPCOverride> HookCanBeHitByItem;
        internal static VaultHookMethodCache<NPCOverride> HookCanBeHitByNPC;
        internal static VaultHookMethodCache<NPCOverride> HookCanBeHitByProjectile;
        public Dictionary<Type, NPCOverride> NPCOverrides { get; internal set; }
        //以下 18 个列表按需惰性创建：多数重制节点只重写少数钩子，避免每次生成都分配 18 个空列表；
        //内部分发直接读后备字段并判空，属性访问永不返回 null，公开 API 形状不变
        private List<NPCOverride> aiOverrides;
        private List<NPCOverride> postAIOverrides;
        private List<NPCOverride> on_PreKillOverrides;
        private List<NPCOverride> checkActiveOverrides;
        private List<NPCOverride> checkDeadOverrides;
        private List<NPCOverride> specialOnKillOverrides;
        private List<NPCOverride> onCheckActiveOverrides;
        private List<NPCOverride> drawOverrides;
        private List<NPCOverride> postDrawOverrides;
        private List<NPCOverride> findFrameOverrides;
        private List<NPCOverride> modifyNPCLootOverrides;
        private List<NPCOverride> onHitByItemOverrides;
        private List<NPCOverride> onHitByProjectileOverrides;
        private List<NPCOverride> modifyHitByItemOverrides;
        private List<NPCOverride> modifyHitByProjectileOverrides;
        private List<NPCOverride> canBeHitByItemOverrides;
        private List<NPCOverride> canBeHitByNPCOverrides;
        private List<NPCOverride> canBeHitByProjectileOverrides;
        public List<NPCOverride> AIOverrides => aiOverrides ??= [];
        public List<NPCOverride> PostAIOverrides => postAIOverrides ??= [];
        public List<NPCOverride> On_PreKillOverrides => on_PreKillOverrides ??= [];
        public List<NPCOverride> CheckActiveOverrides => checkActiveOverrides ??= [];
        public List<NPCOverride> CheckDeadOverrides => checkDeadOverrides ??= [];
        public List<NPCOverride> SpecialOnKillOverrides => specialOnKillOverrides ??= [];
        public List<NPCOverride> OnCheckActiveOverrides => onCheckActiveOverrides ??= [];
        public List<NPCOverride> DrawOverrides => drawOverrides ??= [];
        public List<NPCOverride> PostDrawOverrides => postDrawOverrides ??= [];
        public List<NPCOverride> FindFrameOverrides => findFrameOverrides ??= [];
        public List<NPCOverride> ModifyNPCLootOverrides => modifyNPCLootOverrides ??= [];
        public List<NPCOverride> OnHitByItemOverrides => onHitByItemOverrides ??= [];
        public List<NPCOverride> OnHitByProjectileOverrides => onHitByProjectileOverrides ??= [];
        public List<NPCOverride> ModifyHitByItemOverrides => modifyHitByItemOverrides ??= [];
        public List<NPCOverride> ModifyHitByProjectileOverrides => modifyHitByProjectileOverrides ??= [];
        public List<NPCOverride> CanBeHitByItemOverrides => canBeHitByItemOverrides ??= [];
        public List<NPCOverride> CanBeHitByNPCOverrides => canBeHitByNPCOverrides ??= [];
        public List<NPCOverride> CanBeHitByProjectileOverrides => canBeHitByProjectileOverrides ??= [];
        #endregion

        void IVaultLoader.LoadData() {
            npcLoaderType = typeof(NPCLoader);
            Instances ??= [];
            ByID ??= [];
            UniversalInstances ??= [];
            LoaderMethodAndHook();

            On_Main.DrawNPCHeadBoss += OnDrawNPCHeadBossHook;
            On_NPC.GetBossHeadTextureIndex += OnGetBossHeadTextureIndexHook;
            On_NPC.GetBossHeadRotation += OnGetBossHeadRotationHook;
            On_NPC.GetBossHeadSpriteEffects += OnGetBossHeadSpriteEffectsHook;
        }

        void IVaultLoader.SetupData() {
            HookAI = AddHook<Func<bool>>(n => n.AI);
            HookPostAI = AddHook<Action>(n => n.PostAI);
            HookOn_PreKill = AddHook<Func<bool?>>(n => n.On_PreKill);
            HookCheckActive = AddHook<Func<bool>>(n => n.CheckActive);
            HookCheckDead = AddHook<Func<bool?>>(n => n.CheckDead);
            HookSpecialOnKill = AddHook<Func<bool?>>(n => n.SpecialOnKill);
            HookOnCheckDead = AddHook<Func<bool?>>(n => n.On_CheckActive);
            HookDraw = AddHook<Func<SpriteBatch, Vector2, Color, bool?>>(n => n.Draw);
            HookPostDraw = AddHook<Func<SpriteBatch, Vector2, Color, bool>>(n => n.PostDraw);
            HookFindFrame = AddHook<Func<int, bool>>(n => n.FindFrame);
            HookModifyNPCLoot = AddHook<Action<NPC, NPCLoot>>(n => n.ModifyNPCLoot);
            HookOnHitByItem = AddHook<DelegateOn_OnHitByItem>(n => n.On_OnHitByItem);
            HookOnHitByProjectile = AddHook<DelegateOn_OnHitByProjectile>(n => n.On_OnHitByProjectile);
            HookModifyHitByItem = AddHook<DelegateModifyHitByItem>(n => n.ModifyHitByItem);
            HookModifyHitByProjectile = AddHook<DelegateModifyHitByProjectile>(n => n.ModifyHitByProjectile);
            HookCanBeHitByItem = AddHook<Func<Player, Item, bool?>>(n => n.CanBeHitByItem);
            HookCanBeHitByNPC = AddHook<DelegateCanBeHitByNPC>(n => n.CanBeHitByNPC);
            HookCanBeHitByProjectile = AddHook<Func<Projectile, bool?>>(n => n.CanBeHitByProjectile);

            //为每个模板实例预计算一次钩子重写掩码：HasOverride 每次调用都会分配绑定委托，
            //只允许在加载期出现，生成 NPC 时直接读取克隆体上复制的掩码
            (VaultHookMethodCache<NPCOverride> hook, NPCHookFlags flag)[] hookFlagPairs = [
                (HookAI, NPCHookFlags.AI),
                (HookPostAI, NPCHookFlags.PostAI),
                (HookOn_PreKill, NPCHookFlags.On_PreKill),
                (HookCheckActive, NPCHookFlags.CheckActive),
                (HookCheckDead, NPCHookFlags.CheckDead),
                (HookSpecialOnKill, NPCHookFlags.SpecialOnKill),
                (HookOnCheckDead, NPCHookFlags.On_CheckActive),
                (HookDraw, NPCHookFlags.Draw),
                (HookPostDraw, NPCHookFlags.PostDraw),
                (HookFindFrame, NPCHookFlags.FindFrame),
                (HookModifyNPCLoot, NPCHookFlags.ModifyNPCLoot),
                (HookOnHitByItem, NPCHookFlags.OnHitByItem),
                (HookOnHitByProjectile, NPCHookFlags.OnHitByProjectile),
                (HookModifyHitByItem, NPCHookFlags.ModifyHitByItem),
                (HookModifyHitByProjectile, NPCHookFlags.ModifyHitByProjectile),
                (HookCanBeHitByItem, NPCHookFlags.CanBeHitByItem),
                (HookCanBeHitByNPC, NPCHookFlags.CanBeHitByNPC),
                (HookCanBeHitByProjectile, NPCHookFlags.CanBeHitByProjectile),
            ];
            foreach (NPCOverride overrideInstance in Instances) {
                NPCHookFlags flags = NPCHookFlags.None;
                foreach ((VaultHookMethodCache<NPCOverride> hook, NPCHookFlags flag) in hookFlagPairs) {
                    if (hook.HookOverrideQuery.HasOverride(overrideInstance)) {
                        flags |= flag;
                    }
                }
                overrideInstance.hookFlags = flags;
            }

            //所有重制节点注册完毕后，按 FullName 字典序确定稳定的网络 ID（两端一致，免运行时重排）
            NPCOverrideNetWork.BuildStableIDs();

            //记录本次加载是否存在任何重制节点，热路径钩子在无内容时整体直通原逻辑
            hasAnyOverrides = ByID.Count > 0 || UniversalInstances.Count > 0;
        }

        void IVaultLoader.UnLoadData() {
            hasAnyOverrides = false;
            NPCOverride.FactoryCache.Clear();
            NPCOverrideNetWork.Clear();
            Instances?.Clear();
            OverrideIDToInstances?.Clear();
            TypeToOverrideID?.Clear();
            OverrideIDToType?.Clear();
            ByID?.Clear();
            UniversalInstances?.Clear();
            PreSetDefaultsEvent = null;
            PostSetDefaultsEvent = null;
            npcLoaderType = null;
            onHitByProjectile_Method = null;
            modifyIncomingHit_Method = null;
            onFindFrame_Method = null;
            onSetChatButtons_Method = null;
            onNPCUsesPartyHat_Method = null;
            onUsesPartyHat_Method = null;
            onNPCAI_Method = null;
            onPreKill_Method = null;
            onPreDraw_Method = null;
            onPostDraw_Method = null;
            onCheckDead_Method = null;
            On_Main.DrawNPCHeadBoss -= OnDrawNPCHeadBossHook;
            On_NPC.GetBossHeadTextureIndex -= OnGetBossHeadTextureIndexHook;
            On_NPC.GetBossHeadRotation -= OnGetBossHeadRotationHook;
            On_NPC.GetBossHeadSpriteEffects -= OnGetBossHeadSpriteEffectsHook;
            hooks.Clear();
            HookAI = null;
            HookPostAI = null;
            HookOn_PreKill = null;
            HookCheckActive = null;
            HookCheckDead = null;
            HookDraw = null;
            HookPostDraw = null;
            HookFindFrame = null;
            HookModifyNPCLoot = null;
            HookOnHitByItem = null;
            HookOnHitByProjectile = null;
            HookModifyHitByItem = null;
            HookModifyHitByProjectile = null;
            HookCanBeHitByItem = null;
            HookCanBeHitByNPC = null;
            HookCanBeHitByProjectile = null;
            VaultTypeRegistry<NPCOverride>.ClearRegisteredVaults();
            VaultType<NPCOverride>.TypeToMod.Clear();
        }

        private static VaultHookMethodCache<NPCOverride> AddHook<F>(Expression<Func<NPCOverride, F>> func) where F : Delegate {
            VaultHookMethodCache<NPCOverride> hook = VaultHookMethodCache<NPCOverride>.Create(func);
            hooks.Add(hook);
            return hook;
        }

        public override GlobalNPC Clone(NPC from, NPC to) {
            NPCRebuildLoader rebuildLoader = (NPCRebuildLoader)base.Clone(from, to);
            //克隆时确保新的GlobalNPC实例拥有自己独立的列表集合，空引用保持为空，按需再建
            rebuildLoader.aiOverrides = CopyList(aiOverrides);
            rebuildLoader.postAIOverrides = CopyList(postAIOverrides);
            rebuildLoader.on_PreKillOverrides = CopyList(on_PreKillOverrides);
            rebuildLoader.checkActiveOverrides = CopyList(checkActiveOverrides);
            rebuildLoader.checkDeadOverrides = CopyList(checkDeadOverrides);
            rebuildLoader.specialOnKillOverrides = CopyList(specialOnKillOverrides);
            rebuildLoader.onCheckActiveOverrides = CopyList(onCheckActiveOverrides);
            rebuildLoader.drawOverrides = CopyList(drawOverrides);
            rebuildLoader.postDrawOverrides = CopyList(postDrawOverrides);
            rebuildLoader.findFrameOverrides = CopyList(findFrameOverrides);
            rebuildLoader.modifyNPCLootOverrides = CopyList(modifyNPCLootOverrides);
            rebuildLoader.onHitByItemOverrides = CopyList(onHitByItemOverrides);
            rebuildLoader.onHitByProjectileOverrides = CopyList(onHitByProjectileOverrides);
            rebuildLoader.modifyHitByItemOverrides = CopyList(modifyHitByItemOverrides);
            rebuildLoader.modifyHitByProjectileOverrides = CopyList(modifyHitByProjectileOverrides);
            rebuildLoader.canBeHitByItemOverrides = CopyList(canBeHitByItemOverrides);
            rebuildLoader.canBeHitByNPCOverrides = CopyList(canBeHitByNPCOverrides);
            rebuildLoader.canBeHitByProjectileOverrides = CopyList(canBeHitByProjectileOverrides);
            rebuildLoader.NPCOverrides = NPCOverrides;
            return rebuildLoader;
        }

        private static List<NPCOverride> CopyList(List<NPCOverride> overrides) => overrides is null ? null : [.. overrides];

        public void InitializeNPC() {
            //当GlobalNPC实例被创建时，重置它的列表字段；列表由掩码命中的钩子在挂载实例时按需创建
            aiOverrides = null;
            postAIOverrides = null;
            on_PreKillOverrides = null;
            checkActiveOverrides = null;
            checkDeadOverrides = null;
            specialOnKillOverrides = null;
            onCheckActiveOverrides = null;
            drawOverrides = null;
            postDrawOverrides = null;
            findFrameOverrides = null;
            modifyNPCLootOverrides = null;
            onHitByItemOverrides = null;
            onHitByProjectileOverrides = null;
            modifyHitByItemOverrides = null;
            modifyHitByProjectileOverrides = null;
            canBeHitByItemOverrides = null;
            canBeHitByNPCOverrides = null;
            canBeHitByProjectileOverrides = null;
        }

        public override bool AppliesToEntity(NPC entity, bool lateInstantiation) => lateInstantiation && ByID.ContainsKey(entity.type);

        public static void UniversalForEach(NPC npc, Action<NPCOverride> action) {
            foreach (var inds in UniversalInstances) {
                inds.UniversalSetNPCInstance(npc);
                action(inds);
            }
        }

        public static bool UniversalForEach(NPC npc, Func<NPCOverride, bool> action, bool startBool = true) {
            bool result = startBool;
            foreach (var inds in UniversalInstances) {
                inds.UniversalSetNPCInstance(npc);
                bool newResult = action(inds);
                if (newResult != startBool) {
                    result = newResult;
                }
            }
            return result;
        }

        public static bool? UniversalForEach(NPC npc, Func<NPCOverride, bool?> action) {
            bool? result = null;
            foreach (var inds in UniversalInstances) {
                inds.UniversalSetNPCInstance(npc);
                bool? newResult = action(inds);
                if (newResult.HasValue) {
                    result = newResult;
                }
            }
            return result;
        }

        public override void SetDefaults(NPC npc) {
            if (npc.Alives()) {
                PreSetDefaultsEvent?.Invoke(npc);
            }
            InitializeNPC();
            NPCOverride.SetDefaults(npc);
            if (npc.Alives()) {
                PostSetDefaultsEvent?.Invoke(npc);
            }
        }

        public override void SendExtraAI(NPC npc, BitWriter bitWriter, BinaryWriter binaryWriter) {
            if (!npc.TryGetOverride(out var values)) {
                binaryWriter.Write((byte)0);
                return;
            }

            //写入重制实例的数量，防止两端不一致读取越界
            binaryWriter.Write((byte)values.Count);
            Stream stream = binaryWriter.BaseStream;
            bool canSeek = stream.CanSeek;
            foreach (var overrideInstance in values.Values) {
                //必须写入 ID 以便接收端知道是哪个 Override
                binaryWriter.Write(overrideInstance.OverrideID);
                //每个 override 的负载用长度前缀包裹，使其自描述、可被接收端安全跳过，
                //避免两端 override 集合不一致时错位污染同一 NPC 后续 override 乃至其它 mod 的 ExtraAI 流
                //这里只同步最核心的热状态(ai)，不要塞入大数据
                if (canSeek) {
                    //可寻址流：先写长度占位，负载直接写入主流，写完回填实际长度，免除中间缓冲分配
                    long lengthPos = stream.Position;
                    binaryWriter.Write((ushort)0);
                    long payloadStart = stream.Position;
                    overrideInstance.NetSend(binaryWriter);
                    long payloadEnd = stream.Position;
                    long payloadLength = payloadEnd - payloadStart;
                    if (payloadLength > ushort.MaxValue) {
                        //长度前缀无法表达的超大负载按空负载回滚并报错，避免静默截断污染整个流；
                        //Length 必须一并截断，否则打包端 ToArray 会把回滚区间的过期字节带上
                        VaultMod.LoggerError("NPCOverride.SendExtraAI",
                            $"NetSend payload too large ({payloadLength} bytes) from '{overrideInstance.FullName}', payload dropped.");
                        stream.SetLength(payloadStart);
                        stream.Position = payloadStart;
                        continue;
                    }
                    stream.Position = lengthPos;
                    binaryWriter.Write((ushort)payloadLength);
                    stream.Position = payloadEnd;
                }
                else {
                    //不可寻址流回退双缓冲路径
                    using MemoryStream ms = new();
                    using (BinaryWriter w = new(ms)) {
                        overrideInstance.NetSend(w);
                    }
                    byte[] payload = ms.ToArray();
                    if (payload.Length > ushort.MaxValue) {
                        VaultMod.LoggerError("NPCOverride.SendExtraAI",
                            $"NetSend payload too large ({payload.Length} bytes) from '{overrideInstance.FullName}', payload dropped.");
                        binaryWriter.Write((ushort)0);
                        continue;
                    }
                    binaryWriter.Write((ushort)payload.Length);
                    binaryWriter.Write(payload);
                }
            }
        }

        public override void ReceiveExtraAI(NPC npc, BitReader bitReader, BinaryReader binaryReader) {
            int count = binaryReader.ReadByte();
            Stream stream = binaryReader.BaseStream;
            bool canSeek = stream.CanSeek;
            for (int i = 0; i < count; i++) {
                ushort id = binaryReader.ReadUInt16();
                ushort len = binaryReader.ReadUInt16();

                NPCOverride instance = null;
                if (OverrideIDToType.TryGetValue(id, out var type)
                    && npc.TryGetOverride(out var values)) {
                    values.TryGetValue(type, out instance);
                }

                if (canSeek) {
                    //可寻址流：原地读取负载，结束后强制对齐到块尾，无论解析成败都不会错位污染后续数据
                    long end = stream.Position + len;
                    try {
                        if (instance != null) {
                            instance.NetReceive(binaryReader);
                            if (stream.Position != end) {
                                //读取字节数与负载长度不符，说明两端 NetSend/NetReceive 不对称，对齐后继续
                                VaultMod.LoggerError("NPCOverride.ReceiveExtraAI",
                                    $"NetReceive of '{type?.FullName}' consumed {stream.Position - end + len} bytes, expected {len}; realigned.");
                            }
                        }
                    } catch (Exception ex) {
                        VaultMod.LoggerError("NPCOverride.ReceiveExtraAI", $"Failed to receive NPCOverride ExtraAI: {ex}");
                    } finally {
                        stream.Position = end;
                    }
                }
                else {
                    //不可寻址流回退旧路径：先把负载完整读出，保证无论能否解析都对齐到下一块
                    byte[] payload = binaryReader.ReadBytes(len);
                    try {
                        if (instance != null) {
                            using MemoryStream ms = new(payload);
                            using BinaryReader r = new(ms);
                            instance.NetReceive(r);
                        }
                    } catch (Exception ex) {
                        VaultMod.LoggerError("NPCOverride.ReceiveExtraAI", $"Failed to receive NPCOverride ExtraAI: {ex}");
                    }
                }
            }
        }

        public override void ModifyHitByItem(NPC npc, Player player, Item item, ref NPC.HitModifiers modifiers) {
            if (modifyHitByItemOverrides == null) {
                return;
            }
            foreach (var value in modifyHitByItemOverrides) {
                value.ModifyHitByItem(player, item, ref modifiers);
            }
        }

        public override void ModifyHitByProjectile(NPC npc, Projectile projectile, ref NPC.HitModifiers modifiers) {
            if (modifyHitByProjectileOverrides == null) {
                return;
            }
            foreach (var value in modifyHitByProjectileOverrides) {
                value.ModifyHitByProjectile(projectile, ref modifiers);
            }
        }

        public override bool CheckActive(NPC npc) {
            if (checkActiveOverrides == null) {
                return true;
            }
            bool result = true;
            foreach (var value in checkActiveOverrides) {
                if (!value.CheckActive()) {
                    result = false;
                }
            }
            return result;
        }

        public override void BossHeadSlot(NPC npc, ref int index) {
            if (npc.TryGetOverride(out var values)) {
                foreach (var value in values.Values) {
                    value.BossHeadSlot(ref index);
                }
            }
        }

        public override void BossHeadRotation(NPC npc, ref float rotation) {
            if (npc.TryGetOverride(out var values)) {
                foreach (var value in values.Values) {
                    value.BossHeadRotation(ref rotation);
                }
            }
        }

        public override void BossHeadSpriteEffects(NPC npc, ref SpriteEffects spriteEffects) {
            if (npc.TryGetOverride(out var values)) {
                foreach (var value in values.Values) {
                    value.BossHeadSpriteEffects(ref spriteEffects);
                }
            }
        }

        public override bool? DrawHealthBar(NPC npc, byte hbPosition, ref float scale, ref Vector2 position) {
            if (npc.TryGetOverride(out var values)) {
                bool? reset = null;
                foreach (var value in values.Values) {
                    bool? newReset = value.DrawHealthBar(hbPosition, ref scale, ref position);
                    if (newReset.HasValue) {
                        reset = newReset.Value;
                    }
                }
                if (reset.HasValue) {
                    return reset.Value;
                }
            }
            return null;
        }

        public override void ModifyHoverBoundingBox(NPC npc, ref Rectangle boundingBox) {
            if (npc.TryGetOverride(out var values)) {
                foreach (var value in values.Values) {
                    value.ModifyHoverBoundingBox(ref boundingBox);
                }
            }
        }

        public override void ModifyNPCLoot(NPC npc, NPCLoot npcLoot) {
            foreach (var value in Instances) {
                if (value.TargetID == -1 || value.TargetID == npc.type) {
                    value.ModifyNPCLoot(npc, npcLoot);
                }
            }
        }

        public override bool? CanFallThroughPlatforms(NPC npc) {
            if (npc.TryGetOverride(out var values)) {
                bool? reset = null;
                foreach (var value in values.Values) {
                    bool? newReset = value.CanFallThroughPlatforms();
                    if (newReset.HasValue) {
                        reset = newReset.Value;
                    }
                }
                if (reset.HasValue) {
                    return reset.Value;
                }
            }
            return null;
        }

        public override void ModifyActiveShop(NPC npc, string shopName, Item[] items) {
            if (npc.TryGetOverride(out var values)) {
                foreach (var value in values.Values) {
                    value.ModifyActiveShop(shopName, items);
                }
            }
        }

        public override bool? CanGoToStatue(NPC npc, bool toKingStatue) {
            if (npc.TryGetOverride(out var values)) {
                bool? reset = null;
                foreach (var value in values.Values) {
                    bool? newReset = value.CanGoToStatue(toKingStatue);
                    if (newReset.HasValue) {
                        reset = newReset.Value;
                    }
                }
                if (reset.HasValue) {
                    return reset.Value;
                }
            }
            return null;
        }

        public override void GetChat(NPC npc, ref string chat) {
            if (npc.TryGetOverride(out var values)) {
                foreach (var value in values.Values) {
                    value.GetChat(ref chat);
                }
            }
        }

        public override bool PreChatButtonClicked(NPC npc, bool firstButton) {
            if (npc.TryGetOverride(out var values)) {
                bool reset = true;
                foreach (var value in values.Values) {
                    if (!value.PreChatButtonClicked(firstButton)) {
                        reset = false;
                    }
                }
                if (!reset) {
                    return false;
                }
            }
            return true;
        }

        public override void OnChatButtonClicked(NPC npc, bool firstButton) {
            if (npc.TryGetOverride(out var values)) {
                foreach (var value in values.Values) {
                    value.OnChatButtonClicked(firstButton);
                }
            }
        }

        public override bool? CanBeHitByItem(NPC npc, Player player, Item item) {
            if (canBeHitByItemOverrides == null) {
                return null;
            }
            bool? reset = null;
            foreach (var value in canBeHitByItemOverrides) {
                bool? newReset = value.CanBeHitByItem(player, item);
                if (newReset.HasValue) {
                    reset = newReset.Value;
                }
            }
            if (reset.HasValue) {
                return reset.Value;
            }
            return null;
        }

        public override bool CanBeHitByNPC(NPC npc, NPC attacker) {
            if (canBeHitByNPCOverrides == null) {
                return true;
            }
            bool? reset = null;
            foreach (var value in canBeHitByNPCOverrides) {
                bool? newReset = value.CanBeHitByNPC(attacker);
                if (newReset.HasValue) {
                    reset = newReset.Value;
                }
            }
            if (reset.HasValue) {
                return reset.Value;
            }
            return true;
        }

        public override bool? CanBeHitByProjectile(NPC npc, Projectile projectile) {
            if (canBeHitByProjectileOverrides == null) {
                return null;
            }
            bool? reset = null;
            foreach (var value in canBeHitByProjectileOverrides) {
                bool? newReset = value.CanBeHitByProjectile(projectile);
                if (newReset.HasValue) {
                    reset = newReset.Value;
                }
            }
            if (reset.HasValue) {
                return reset.Value;
            }
            return null;
        }

        public override void SaveData(NPC npc, TagCompound tag) {
            if (npc.TryGetOverride(out var values)) {
                foreach (var value in values.Values) {
                    try {
                        //每个 override 的数据写入以其 FullName 命名的独立子标签，避免多个 override 之间键名冲突相互覆盖
                        TagCompound sub = [];
                        value.SaveData(sub);
                        if (sub.Count > 0) {
                            tag[value.FullName] = sub;
                        }
                    } catch (Exception ex) {
                        LogOverrideDataError(npc, value, ex);
                    }
                }
            }
        }

        public override void LoadData(NPC npc, TagCompound tag) {
            if (npc.TryGetOverride(out var values)) {
                foreach (var value in values.Values) {
                    try {
                        //优先读取按 FullName 隔离的子标签；旧存档没有子标签则回退到扁平标签，保持向下兼容
                        TagCompound dataTag = tag.TryGet(value.FullName, out TagCompound sub) ? sub : tag;
                        value.LoadData(dataTag);
                    } catch (Exception ex) {
                        LogOverrideDataError(npc, value, ex);
                    }
                }
            }
        }

        public override bool NeedSaving(NPC npc) {
            if (npc.TryGetOverride(out var values)) {
                foreach (var value in values.Values) {
                    try {
                        if (value.NeedSaving()) {
                            return true;
                        }
                    } catch (Exception ex) {
                        LogOverrideDataError(npc, value, ex);
                    }
                }
            }
            return false;
        }

        //存档读写阶段的异常只记录日志，不再停用 NPC：数据错误不应导致 NPC 直接从世界消失
        internal static void LogOverrideDataError(NPC npc, NPCOverride value, Exception ex) {
            VaultMod.Instance.Logger.Error($"[NPCOverride] Save/Load data threw for '{value?.FullName}' on NPC {npc?.type}: {ex}");
        }

        private static MethodInfo GetMethodInfo(string key) => npcLoaderType.GetMethod(key, BindingFlags.Public | BindingFlags.Static);

        private static void DompLog(string name) => VaultMod.Instance.Logger.Info($"ERROR:Fail To Load! {name} Is Null!");

        private static void LoaderMethodAndHook() {
            {
                onHitByItem_Method = GetMethodInfo("OnHitByItem");
                if (onHitByItem_Method != null) {
                    VaultHook.Add(onHitByItem_Method, On_OnHitByItemHook);
                }
                else {
                    DompLog("onHitByItem_Method");
                }
            }
            {
                onHitByProjectile_Method = GetMethodInfo("OnHitByProjectile");
                if (onHitByProjectile_Method != null) {
                    VaultHook.Add(onHitByProjectile_Method, On_OnHitByProjectileHook);
                }
                else {
                    DompLog("onHitByProjectile_Method");
                }
            }
            {
                modifyIncomingHit_Method = GetMethodInfo("ModifyIncomingHit");
                if (modifyIncomingHit_Method != null) {
                    VaultHook.Add(modifyIncomingHit_Method, ModifyIncomingHitHook);
                }
                else {
                    DompLog("modifyIncomingHit_Method");
                }
            }
            {
                onFindFrame_Method = GetMethodInfo("FindFrame");
                if (onFindFrame_Method != null) {
                    VaultHook.Add(onFindFrame_Method, OnFindFrameHook);
                }
                else {
                    DompLog("onFindFrame_Method");
                }
            }
            {
                onSetChatButtons_Method = GetMethodInfo("SetChatButtons");
                if (onSetChatButtons_Method != null) {
                    VaultHook.Add(onSetChatButtons_Method, OnSetChatButtonsHook);
                }
                else {
                    DompLog("onSetChatButtons_Method");
                }
            }
            {
                onNPCUsesPartyHat_Method = typeof(NPC).GetMethod("UsesPartyHat", BindingFlags.Public | BindingFlags.Instance);
                if (onNPCUsesPartyHat_Method != null) {
                    VaultHook.Add(onNPCUsesPartyHat_Method, OnPreUsesPartyHatHook);
                }
                else {
                    DompLog("onNPCUsesPartyHat_Method");
                }
            }
            {
                onUsesPartyHat_Method = GetMethodInfo("UsesPartyHat");
                if (onUsesPartyHat_Method != null) {
                    VaultHook.Add(onUsesPartyHat_Method, OnUsesPartyHatHook);
                }
                else {
                    DompLog("onUsesPartyHat_Method");
                }
            }
            {
                onNPCAI_Method = GetMethodInfo("NPCAI");
                if (onNPCAI_Method != null) {
                    //提升到最外层，避免其他模组（如 MEAC）后挂的无配置钩子包裹在外层并跳过 orig，导致 NPCOverride 的 AI 派发被整体绕过
                    VaultHook.Add(onNPCAI_Method, OnNPCAIHook, VaultHook.DefaultHookPriority);
                }
                else {
                    DompLog("onNPCAI_Method");
                }
            }
            {
                onPreDraw_Method = GetMethodInfo("PreDraw");
                if (onPreDraw_Method != null) {
                    VaultHook.Add(onPreDraw_Method, OnPreDrawHook);
                }
                else {
                    DompLog("onPreDraw_Method");
                }
            }
            {
                onPostDraw_Method = GetMethodInfo("PostDraw");
                if (onPostDraw_Method != null) {
                    VaultHook.Add(onPostDraw_Method, OnPostDrawHook);
                }
                else {
                    DompLog("onPostDraw_Method");
                }
            }
            {
                onCheckDead_Method = GetMethodInfo("CheckDead");
                if (onCheckDead_Method != null) {
                    VaultHook.Add(onCheckDead_Method, OnCheckDeadHook);
                }
                else {
                    DompLog("onCheckDead_Method");
                }
            }
            MethodInfo methodInfo;
            {
                methodInfo = GetMethodInfo("SpecialOnKill");
                if (methodInfo != null) {
                    VaultHook.Add(methodInfo, OnSpecialOnKillHook);
                }
                else {
                    DompLog("onSpecialOnKill_Method");
                }
            }
            {
                methodInfo = GetMethodInfo("CheckActive");
                if (methodInfo != null) {
                    VaultHook.Add(methodInfo, OnCheckActiveHook);
                }
                else {
                    DompLog("onCheckActive_Method");
                }
            }
            {
                onPreKill_Method = GetMethodInfo("PreKill");
                if (onPreKill_Method != null) {
                    VaultHook.Add(onPreKill_Method, OnPreKillHook);
                }
                else {
                    DompLog("onPreKill_Method");
                }
            }
        }

        internal static void LogAndDeactivateNPC(NPC npc, Exception ex) {
            if (npc == null) {
                string nullNpcMsg = "An error occurred: NPC was null and could not be processed.";
                VaultUtils.Text($"{nullNpcMsg} For detailed error information, please refer to the log file", Color.Red);
                VaultMod.Instance.Logger.Error($"{nullNpcMsg} Error: {ex}");
                return;
            }

            string npcMsg = $"An error occurred in original AI for NPC {npc.GetType().FullName}. Deactivating it.";
            VaultUtils.Text($"{npcMsg} For detailed error information, please refer to the log file", Color.Red);
            VaultMod.Instance.Logger.Error($"{npcMsg} Error: {ex}");
            npc.active = false;
        }

        public static bool OnPreKillHook(On_NPCDelegate2 orig, NPC npc) {
            if (!hasAnyOverrides || npc.type == NPCID.None || !npc.active) {
                return orig.Invoke(npc);
            }

            if (npc.TryGetGlobalNPC(out NPCRebuildLoader gNpc) && gNpc.on_PreKillOverrides != null) {
                bool? result = null;
                foreach (var value in gNpc.on_PreKillOverrides) {
                    bool? newResult = value.On_PreKill();
                    if (newResult.HasValue) {
                        result = newResult.Value;
                    }
                }
                if (result.HasValue) {
                    NPCLoader.blockLoot?.Clear();//这里因为提前返回，所以手动清理一下物品ban位
                    return result.Value;
                }
            }

            return orig.Invoke(npc);
        }

        public static bool OnCheckDeadHook(On_NPCDelegate2 orig, NPC npc) {
            if (!hasAnyOverrides || npc.type == NPCID.None || !npc.active) {
                return orig.Invoke(npc);
            }

            if (npc.TryGetGlobalNPC(out NPCRebuildLoader gNpc) && gNpc.checkDeadOverrides != null) {
                bool? result = null;
                foreach (var value in gNpc.checkDeadOverrides) {
                    bool? newResult = value.CheckDead();
                    if (newResult.HasValue) {
                        result = newResult.Value;
                    }
                }
                if (result.HasValue) {
                    return result.Value;
                }
            }
            return orig.Invoke(npc);
        }

        public static bool OnSpecialOnKillHook(On_NPCDelegate2 orig, NPC npc) {
            if (!hasAnyOverrides || npc.type == NPCID.None || !npc.active) {
                return orig.Invoke(npc);
            }

            if (npc.TryGetGlobalNPC(out NPCRebuildLoader gNpc) && gNpc.specialOnKillOverrides != null) {
                bool? result = null;
                foreach (var value in gNpc.specialOnKillOverrides) {
                    bool? newResult = value.SpecialOnKill();
                    if (newResult.HasValue) {
                        result = newResult.Value;
                    }
                }
                if (result.HasValue) {
                    return result.Value;
                }
            }
            return orig.Invoke(npc);
        }

        public static bool OnCheckActiveHook(On_NPCDelegate2 orig, NPC npc) {
            if (!hasAnyOverrides || npc.type == NPCID.None || !npc.active) {
                return orig.Invoke(npc);
            }

            if (npc.TryGetGlobalNPC(out NPCRebuildLoader gNpc) && gNpc.onCheckActiveOverrides != null) {
                bool? result = null;
                foreach (var value in gNpc.onCheckActiveOverrides) {
                    bool? newResult = value.On_CheckActive();
                    if (newResult.HasValue) {
                        result = newResult.Value;
                    }
                }
                if (result.HasValue) {
                    return result.Value;
                }
            }
            return orig.Invoke(npc);
        }

        public static void OnNPCAIHook(On_NPCDelegate orig, NPC npc) {
            if (!hasAnyOverrides || npc.type == NPCID.None || !npc.active) {
                orig.Invoke(npc);
                return;
            }

            //这里是全 NPC 每帧路径，直接循环通用节点，绕开 UniversalForEach 的委托间接层
            bool universalRunAI = true;
            foreach (var inds in UniversalInstances) {
                inds.UniversalSetNPCInstance(npc);
                if (!inds.AI()) {
                    universalRunAI = false;
                }
            }
            if (!universalRunAI) {
                return;
            }

            if (npc.TryGetGlobalNPC(out NPCRebuildLoader gNpc)) {
                bool result = true;
                if (gNpc.aiOverrides != null) {
                    foreach (var value in gNpc.aiOverrides) {
                        if (!value.AI()) {
                            result = false;
                        }
                    }
                }
                if (result) {
                    try {
                        orig.Invoke(npc);
                    } catch (Exception ex) {
                        LogAndDeactivateNPC(npc, ex);
                    }
                }

                if (gNpc.postAIOverrides != null) {
                    foreach (var value in gNpc.postAIOverrides) {
                        value.PostAI();
                    }
                }

                //所有逻辑处理完成后，统一在服务端做一次网络同步，客户端无需空转遍历
                if (VaultUtils.isServer && gNpc.NPCOverrides != null) {
                    foreach (var npcOverrideInstance in gNpc.NPCOverrides.Values) {
                        npcOverrideInstance.DoNetWork();
                    }
                }
            }
            else {
                orig.Invoke(npc);
            }

            foreach (var inds in UniversalInstances) {
                inds.UniversalSetNPCInstance(npc);
                inds.PostAI();
            }
        }

        public static bool OnPreDrawHook(On_DrawDelegate orig, NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor) {
            if (!hasAnyOverrides || npc.type == NPCID.None || !npc.active) {
                return orig.Invoke(npc, spriteBatch, screenPos, drawColor);
            }

            if (npc.TryGetGlobalNPC(out NPCRebuildLoader gNpc) && gNpc.drawOverrides != null) {
                bool? result = null;
                foreach (var value in gNpc.drawOverrides) {
                    bool? newResult = value.Draw(spriteBatch, screenPos, drawColor);
                    if (newResult.HasValue) {
                        result = newResult.Value;
                    }
                }
                if (result.HasValue) {
                    return result.Value;
                }
            }

            return orig.Invoke(npc, spriteBatch, screenPos, drawColor);
        }

        public static void OnPostDrawHook(On_DrawDelegate2 orig, NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor) {
            if (npc.type == NPCID.None || !npc.active) {
                return;
            }

            if (hasAnyOverrides && npc.TryGetGlobalNPC(out NPCRebuildLoader gNpc) && gNpc.postDrawOverrides != null) {
                bool reset = true;
                foreach (var value in gNpc.postDrawOverrides) {
                    if (!value.PostDraw(spriteBatch, screenPos, drawColor)) {
                        reset = false;
                    }
                }
                if (!reset) {
                    return;
                }
            }

            orig.Invoke(npc, spriteBatch, screenPos, drawColor);
        }

        public static void On_OnHitByItemHook(On_OnHitByItemDelegate orig, NPC npc, Player player, Item item, in NPC.HitInfo hit, int damageDone) {
            if (!hasAnyOverrides) {
                orig.Invoke(npc, player, item, hit, damageDone);
                return;
            }

            if (npc.TryGetGlobalNPC(out NPCRebuildLoader rebuildLoader) && rebuildLoader.onHitByItemOverrides != null) {
                bool reset = true;
                foreach (var inds in rebuildLoader.onHitByItemOverrides) {
                    if (!inds.On_OnHitByItem(player, item, hit, damageDone)) {
                        reset = false;
                    }
                }
                if (!reset) {
                    return;
                }
            }

            orig.Invoke(npc, player, item, hit, damageDone);
        }

        public static void On_OnHitByProjectileHook(On_OnHitByProjectileDelegate orig, NPC npc, Projectile projectile, in NPC.HitInfo hit, int damageDone) {
            if (!hasAnyOverrides) {
                orig.Invoke(npc, projectile, hit, damageDone);
                return;
            }

            if (npc.TryGetGlobalNPC(out NPCRebuildLoader rebuildLoader) && rebuildLoader.onHitByProjectileOverrides != null) {
                foreach (var inds in rebuildLoader.onHitByProjectileOverrides) {
                    if (!inds.DoHitByProjectileByInstance(projectile, in hit, damageDone)) {
                        return;
                    }
                }
            }

            foreach (var inds in UniversalInstances) {
                inds.UniversalSetNPCInstance(npc);
                if (!inds.DoHitByProjectileByInstance(projectile, in hit, damageDone)) {
                    return;
                }
            }

            orig.Invoke(npc, projectile, hit, damageDone);
        }

        public static void ModifyIncomingHitHook(On_ModifyIncomingHitDelegate orig, NPC npc, ref NPC.HitModifiers modifiers) {
            if (!hasAnyOverrides) {
                orig.Invoke(npc, ref modifiers);
                return;
            }

            if (npc.TryGetOverride(out var npcOverrides)) {
                foreach (var inds in npcOverrides.Values) {
                    if (!inds.DoModifyIncomingHitByInstance(ref modifiers)) {
                        return;
                    }
                }
            }

            foreach (var inds in UniversalInstances) {
                inds.UniversalSetNPCInstance(npc);
                if (!inds.DoModifyIncomingHitByInstance(ref modifiers)) {
                    return;
                }
            }

            orig.Invoke(npc, ref modifiers);
        }

        public static void OnFindFrameHook(On_FindFrameDelegate orig, NPC npc, int frameHeight) {
            if (!hasAnyOverrides || npc.type == NPCID.None || !npc.active) {
                orig.Invoke(npc, frameHeight);
                return;
            }

            if (npc.TryGetGlobalNPC(out NPCRebuildLoader gNpc) && gNpc.findFrameOverrides != null) {
                bool reset = true;
                foreach (var value in gNpc.findFrameOverrides) {
                    if (!value.FindFrame(frameHeight)) {
                        reset = false;
                    }
                }
                if (!reset) {
                    return;
                }
            }

            orig.Invoke(npc, frameHeight);
        }

        public static void OnSetChatButtonsHook(On_SetChatButtonsDelegate orig, ref string button, ref string button2) {
            NPC npc = hasAnyOverrides ? Main.LocalPlayer.TalkNPC : null;
            if (npc == null) {
                orig.Invoke(ref button, ref button2);
                return;
            }

            if (npc.TryGetOverride(out var npcOverrides)) {
                bool reset = true;
                foreach (var npcOverrideInstance in npcOverrides.Values) {
                    if (!npcOverrideInstance.SetChatButtons(ref button, ref button2)) {
                        reset = false;
                    }
                }
                if (!reset) {
                    return;
                }
            }

            orig.Invoke(ref button, ref button2);
        }

        public static bool OnPreUsesPartyHatHook(On_NPCDelegate2 orig, NPC npc) {
            if (!hasAnyOverrides || npc.type == NPCID.None || !npc.active) {
                return orig.Invoke(npc);
            }

            if (npc.TryGetOverride(out var npcOverrides)) {
                bool? result = null;

                foreach (var npcOverrideInstance in npcOverrides.Values) {
                    bool? newResult = npcOverrideInstance.PreUsesPartyHat();
                    if (newResult.HasValue) {
                        result = newResult.Value;
                    }
                }

                if (result.HasValue) {
                    return result.Value;
                }
            }

            return orig.Invoke(npc);
        }

        public static bool OnUsesPartyHatHook(On_NPCDelegate2 orig, NPC npc) {
            if (!hasAnyOverrides || npc.type == NPCID.None || !npc.active) {
                return orig.Invoke(npc);
            }

            if (npc.TryGetOverride(out var npcOverrides)) {
                bool? result = null;

                foreach (var npcOverrideInstance in npcOverrides.Values) {
                    bool? newResult = npcOverrideInstance.UsesPartyHat();
                    if (newResult.HasValue) {
                        result = newResult.Value;
                    }
                }

                if (result.HasValue) {
                    return result.Value;
                }
            }

            return orig.Invoke(npc);
        }

        public static void OnDrawNPCHeadBossHook(On_Main.orig_DrawNPCHeadBoss orig, Entity theNPC, byte alpha
            , float headScale, float rotation, SpriteEffects effects, int bossHeadId, float x, float y) {
            if (!hasAnyOverrides || !theNPC.active || theNPC is not NPC npc) {
                orig.Invoke(theNPC, alpha, headScale, rotation, effects, bossHeadId, x, y);
                return;
            }

            if (npc.TryGetOverride(out var npcOverrides)) {
                foreach (var npcOverrideInstance in npcOverrides.Values) {
                    npcOverrideInstance.ModifyDrawNPCHeadBoss(ref x, ref y, ref bossHeadId, ref alpha, ref headScale, ref rotation, ref effects);
                }

                bool reset = true;
                foreach (var npcOverrideInstance in npcOverrides.Values) {
                    if (!npcOverrideInstance.PreDrawNPCHeadBoss(Main.BossNPCHeadRenderer, new Vector2(x, y), bossHeadId, alpha, headScale, rotation, effects)) {
                        reset = false;
                    }
                }
                if (!reset) {
                    return;
                }
            }

            if (UniversalInstances.Count > 0) {
                foreach (var inds in UniversalInstances) {
                    inds.UniversalSetNPCInstance(npc);
                    inds.ModifyDrawNPCHeadBoss(ref x, ref y, ref bossHeadId, ref alpha, ref headScale, ref rotation, ref effects);
                }

                bool universalReset = true;

                foreach (var inds in UniversalInstances) {
                    inds.UniversalSetNPCInstance(npc);
                    if (!inds.PreDrawNPCHeadBoss(Main.BossNPCHeadRenderer, new Vector2(x, y), bossHeadId, alpha, headScale, rotation, effects)) {
                        universalReset = false;
                    }
                }

                if (!universalReset) {
                    return;
                }
            }

            orig.Invoke(theNPC, alpha, headScale, rotation, effects, bossHeadId, x, y);
        }

        public static int OnGetBossHeadTextureIndexHook(On_NPC.orig_GetBossHeadTextureIndex orig, NPC npc) {
            if (!hasAnyOverrides || Main.gameMenu || !npc.active) {//不需要判定ID
                return orig.Invoke(npc);
            }

            if (npc.TryGetOverride(out var npcOverrides)) {
                int index = -1;
                foreach (var npcOverrideInstance in npcOverrides.Values) {
                    int newIndex = npcOverrideInstance.GetBossHeadTextureIndex();
                    if (newIndex >= 0) {
                        index = newIndex;
                    }
                }
                if (index >= 0) {
                    return index;
                }
            }

            if (UniversalInstances.Count > 0) {
                int index = -1;
                foreach (var inds in UniversalInstances) {
                    inds.UniversalSetNPCInstance(npc);
                    int newIndex = inds.GetBossHeadTextureIndex();
                    if (newIndex >= 0) {
                        index = newIndex;
                    }
                }
                if (index >= 0) {
                    return index;
                }
            }

            return orig.Invoke(npc);
        }

        public static float OnGetBossHeadRotationHook(On_NPC.orig_GetBossHeadRotation orig, NPC npc) {
            if (!hasAnyOverrides || Main.gameMenu || !npc.active) {//不需要判定ID
                return orig.Invoke(npc);
            }

            if (npc.TryGetOverride(out var npcOverrides)) {
                float? rotation = null;
                foreach (var npcOverrideInstance in npcOverrides.Values) {
                    float? newRotation = npcOverrideInstance.GetBossHeadRotation();
                    if (newRotation.HasValue) {
                        rotation = newRotation.Value;
                    }
                }
                if (rotation.HasValue) {
                    return rotation.Value;
                }
            }

            if (UniversalInstances.Count > 0) {
                float? rotation = null;
                foreach (var inds in UniversalInstances) {
                    float? newRotation = inds.GetBossHeadRotation();
                    if (newRotation.HasValue) {
                        rotation = newRotation.Value;
                    }
                }
                if (rotation.HasValue) {
                    return rotation.Value;
                }
            }

            return orig.Invoke(npc);
        }

        public static SpriteEffects OnGetBossHeadSpriteEffectsHook(On_NPC.orig_GetBossHeadSpriteEffects orig, NPC npc) {
            if (!hasAnyOverrides || Main.gameMenu || !npc.active) {//不需要判定ID
                return orig.Invoke(npc);
            }

            if (npc.TryGetOverride(out var npcOverrides)) {
                SpriteEffects? spriteEffects = null;
                foreach (var npcOverrideInstance in npcOverrides.Values) {
                    SpriteEffects? newSpriteEffects = npcOverrideInstance.GetBossHeadSpriteEffects();
                    if (newSpriteEffects.HasValue) {
                        spriteEffects = newSpriteEffects.Value;
                    }
                }
                if (spriteEffects.HasValue) {
                    return spriteEffects.Value;
                }
            }

            if (UniversalInstances.Count > 0) {
                SpriteEffects? spriteEffects = null;
                foreach (var inds in UniversalInstances) {
                    SpriteEffects? newSpriteEffects = inds.GetBossHeadSpriteEffects();
                    if (newSpriteEffects.HasValue) {
                        spriteEffects = newSpriteEffects.Value;
                    }
                }
                if (spriteEffects.HasValue) {
                    return spriteEffects.Value;
                }
            }

            return orig.Invoke(npc);
        }
#pragma warning restore CS1591 //缺少对公共可见类型或成员的 XML 注释
    }
}
