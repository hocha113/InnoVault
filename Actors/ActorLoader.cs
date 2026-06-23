using InnoVault.Debugs;
using InnoVault.GameSystem;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq.Expressions;
using Terraria;
using Terraria.GameContent;
using Terraria.ModLoader;

namespace InnoVault.Actors
{
    /// <summary>
    /// Actor系统的全局管理器，负责管理所有Actor实例的生命周期、更新和绘制
    /// </summary>
    public class ActorLoader : ModSystem, IVaultLoader
    {
        #region Data
        /// <summary>
        /// 游戏世界中最多允许存在的Actor数量
        /// </summary>
        public const int MaxActorCount = short.MaxValue;
        /// <summary>
        /// 游戏世界中所有活跃的Actor实例
        /// </summary>
        public static Actor[] Actors { get; private set; }
        //O(1) 槽位分配器: 优先复用已回收槽位，否则从未使用区段递增取号；generation 保证复用安全
        private static int nextNewSlot;
        private static readonly Stack<int> recycledSlots = new();
        //每个槽位的"代"计数器(服务器递增)，客户端不参与计算，仅使用包内 generation
        private static ushort[] slotGenerations;
        //活跃实体的稠密列表，热路径(更新 / 绘制 / 查询 / 广播)遍历它以避免 O(short.MaxValue) 全量扫描
        private static readonly List<Actor> activeActors = new();
        //更新期对活跃列表的快照，容忍 AI 中途的生成 / 销毁
        private static readonly List<Actor> updateBuffer = new();
        /// <summary>
        /// 当前世界中所有活跃的 <see cref="Actor"/> 稠密列表(内部维护，请勿缓存引用或在外部修改)
        /// </summary>
        internal static IReadOnlyList<Actor> ActiveActors => activeActors;
        /// <summary>
        /// 一个字典，将<see cref="Actor"/>类型映射到其无参构造工厂委托
        /// 在<see cref="Actor.VaultRegister"/>注册阶段编译生成，用于代替反射 <see cref="Activator.CreateInstance(Type)"/>
        /// 使用字段初始化器保证<see cref="Actor.VaultRegister"/>调用时此字典已可用（注册阶段早于<see cref="IVaultLoader.LoadData"/>）
        /// </summary>
        internal static Dictionary<Type, Func<Actor>> ActorFactory { get; private set; } = [];

        private static readonly List<VaultHookMethodCache<GlobalActor>> hooks = [];
        internal static VaultHookMethodCache<GlobalActor> HookPreAI;
        internal static VaultHookMethodCache<GlobalActor> HookPostAI;
        internal static VaultHookMethodCache<GlobalActor> HookOnSpawn;
        internal static VaultHookMethodCache<GlobalActor> HookPreDraw;
        internal static VaultHookMethodCache<GlobalActor> HookPostDraw;
        internal static VaultHookMethodCache<GlobalActor> HookCarryPlayer;
        #endregion

        #region Load and Unload
        void IVaultLoader.LoadData() {
            Actors = new Actor[MaxActorCount];
            slotGenerations = new ushort[MaxActorCount];
            ResetSlots();

            VaultTypeRegistry<Actor>.CompleteLoading();

            HookPreAI = AddHook<Func<Actor, bool>>(a => a.PreAI);
            HookPostAI = AddHook<Action<Actor>>(a => a.PostAI);
            HookOnSpawn = AddHook<Action<Actor>>(a => a.OnSpawn);
            HookPreDraw = AddHook<Func<SpriteBatch, Actor, Color, bool>>(a => a.PreDraw);
            HookPostDraw = AddHook<Action<SpriteBatch, Actor, Color>>(a => a.PostDraw);
            HookCarryPlayer = AddHook<CarryPlayerHook>(a => a.CarryPlayer);
        }

        void IVaultLoader.UnLoadData() {
            Actors = null;
            slotGenerations = null;
            ResetSlots();

            hooks.Clear();
            HookPreAI = null;
            HookPostAI = null;
            HookOnSpawn = null;
            HookPreDraw = null;
            HookPostDraw = null;
            HookCarryPlayer = null;

            //仅清空内容而非置 null，保证下一次注册阶段（早于 LoadData）字典立即可用
            ActorFactory.Clear();

            GlobalActor.Instances?.Clear();
            VaultTypeRegistry<GlobalActor>.ClearRegisteredVaults();
            VaultType<GlobalActor>.TypeToMod.Clear();
            VaultTypeRegistry<Actor>.ClearRegisteredVaults();
            VaultType<Actor>.TypeToMod.Clear();
        }

        private static VaultHookMethodCache<GlobalActor> AddHook<F>(Expression<Func<GlobalActor, F>> func) where F : Delegate {
            VaultHookMethodCache<GlobalActor> hook = VaultHookMethodCache<GlobalActor>.Create(func);
            hooks.Add(hook);
            return hook;
        }

        private static void ResetSlots() {
            nextNewSlot = 0;
            recycledSlots.Clear();
            activeActors.Clear();
            updateBuffer.Clear();
        }
        #endregion

        #region 生成和周期管理
        /// <summary>
        /// 生成一个新的Actor到世界中
        /// </summary>
        /// <typeparam name="T">Actor类型</typeparam>
        /// <param name="position">生成位置</param>
        /// <param name="velocity">初始速度</param>
        /// <returns>生成的Actor实例的WhoAmI索引，如果生成失败返回-1</returns>
        public static int NewActor<T>(Vector2 position, Vector2 velocity = default) where T : Actor {
            return NewActor(GetActorID<T>(), position, velocity);
        }

        /// <summary>
        /// 生成一个新的Actor到世界中
        /// </summary>
        /// <param name="type">Actor类型ID</param>
        /// <param name="position">生成位置</param>
        /// <param name="velocity">初始速度</param>
        /// <returns>生成的Actor实例的WhoAmI索引，如果生成失败返回-1</returns>
        public static int NewActor(int type, Vector2 position, Vector2 velocity = default) {
            //多人客户端无权生成: 仅向服务器发送请求，由服务器集中分配槽位与 generation 后广播
            if (VaultUtils.isClient) {
                ActorNetWork.SendActorSpawnRequest(type, position, velocity);
                return -1;
            }

            return ServerSpawn(type, position, velocity);
        }

        /// <summary>
        /// (服务器 / 单机)权威地生成一个Actor: 分配槽位与 generation，实例化并广播生成包
        /// </summary>
        internal static int ServerSpawn(int type, Vector2 position, Vector2 velocity) {
            int slot = FindNextFreeSlot();
            if (slot == -1) {
                return -1;
            }

            ushort generation = NextGeneration(slot);
            Actor actor = InstantiateAt(type, slot, generation);
            actor.Position = position;
            actor.Velocity = velocity;
            //初始化上一帧位置，避免生成首帧 FrameVelocity 取到 (Position - Vector2.Zero) 的巨大伪位移
            if (actor is SolidActor solid) {
                solid.LastPosition = position;
            }

            RunSpawn(actor);

            if (!VaultUtils.isSinglePlayer) {
                ActorNetWork.BroadcastActorSpawn(actor);
            }

            return slot;
        }

        /// <summary>
        /// (客户端)根据服务器生成包在指定槽位创建实体，并应用全量状态与附加数据
        /// </summary>
        internal static Actor NetworkSpawn(int type, int slot, ushort generation, BinaryReader reader) {
            //槽位若被旧实体占用(陈旧 / 竞态)，先本地释放以免活跃列表泄漏
            Actor existing = Actors[slot];
            if (existing != null && existing.Active) {
                FreeSlot(existing);
            }

            Actor actor = InstantiateAt(type, slot, generation);
            SyncVarManager.ReadState(actor, reader);
            if (actor is SolidActor solid) {
                solid.LastPosition = actor.Position;
            }

            RunSpawn(actor);

            //附加数据在 OnSpawn 之后应用，用权威内部状态覆盖由当前位置反推得到的(可能错误的)值
            actor.ReceiveExtraData(reader);
            actor.InitNetTarget();

            return actor;
        }

        private static Actor InstantiateAt(int type, int slot, ushort generation) {
            Actor actor = Actor.IDToInstance[type].Clone();
            actor.ID = type;
            actor.WhoAmI = slot;
            actor.Generation = generation;
            actor.Active = true;
            Actors[slot] = actor;
            RegisterActive(actor);
            return actor;
        }

        private static void RunSpawn(Actor actor) {
            actor.OnSpawn([]);

            foreach (var global in HookOnSpawn.Enumerate()) {
                global.OnSpawn(actor);
            }
        }

        private static ushort NextGeneration(int slot) {
            if (slotGenerations == null) {
                return 1;
            }
            ushort next = (ushort)(slotGenerations[slot] + 1);
            if (next == 0) {
                next = 1; //0 保留为"无效代"
            }
            slotGenerations[slot] = next;
            return next;
        }

        /// <summary>
        /// 查找下一个可用的Actor槽位
        /// </summary>
        /// <returns>可用槽位的索引，如果没有可用槽位返回-1</returns>
        public static int FindNextFreeSlot() {
            if (recycledSlots.Count > 0) {
                return recycledSlots.Pop();
            }
            if (nextNewSlot < MaxActorCount) {
                return nextNewSlot++;
            }

            //兜底: 线性扫描一个空闲槽位(正常情况下不会触发)
            for (int i = 0; i < MaxActorCount; i++) {
                if (Actors[i] == null || !Actors[i].Active) {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// 杀死指定的Actor
        /// </summary>
        /// <param name="whoAmI"></param>
        /// <param name="network"></param>
        public static void KillActor(int whoAmI, bool network = true) {
            if (whoAmI < 0 || whoAmI >= MaxActorCount) {
                return;
            }

            Actor actor = Actors[whoAmI];
            if (actor == null || !actor.Active) {
                return;
            }

            //多人客户端无权销毁: 仅向服务器请求，实际释放等待服务器的销毁广播
            if (VaultUtils.isClient) {
                if (network) {
                    ActorNetWork.SendActorKillRequest(whoAmI, actor.Generation);
                }
                return;
            }

            ushort generation = actor.Generation;
            FreeSlot(actor);

            if (network && !VaultUtils.isSinglePlayer) {
                ActorNetWork.SendActorKill(whoAmI, generation);
            }
        }

        /// <summary>
        /// (客户端)根据服务器销毁广播在校验 generation 后本地释放实体
        /// </summary>
        internal static void NetworkKill(int slot, ushort generation) {
            if (slot < 0 || slot >= MaxActorCount) {
                return;
            }
            Actor actor = Actors[slot];
            if (actor != null && actor.Active && actor.Generation == generation) {
                FreeSlot(actor);
            }
        }

        private static void FreeSlot(Actor actor) {
            actor.Active = false;
            UnregisterActive(actor);

            int slot = actor.WhoAmI;
            if (slot >= 0 && slot < MaxActorCount && Actors[slot] == actor) {
                Actors[slot] = null;
                //仅服务器复用槽位; 客户端槽位由服务器指派，无需回收，避免无界增长
                if (!VaultUtils.isClient) {
                    recycledSlots.Push(slot);
                }
            }
        }

        private static void RegisterActive(Actor actor) {
            actor.ActiveIndex = activeActors.Count;
            activeActors.Add(actor);
        }

        private static void UnregisterActive(Actor actor) {
            int index = actor.ActiveIndex;
            if (index < 0 || index >= activeActors.Count || activeActors[index] != actor) {
                actor.ActiveIndex = -1;
                return;
            }

            int lastIndex = activeActors.Count - 1;
            Actor moved = activeActors[lastIndex];
            activeActors[index] = moved;
            moved.ActiveIndex = index;
            activeActors.RemoveAt(lastIndex);
            actor.ActiveIndex = -1;
        }

        /// <summary>
        /// 获取Actor类型的ID
        /// </summary>
        /// <typeparam name="T">Actor类型</typeparam>
        /// <returns>Actor类型ID</returns>
        public static int GetActorID<T>() where T : Actor => Actor.TypeToID[typeof(T)];

        /// <summary>
        /// 获取Actor类型的ID
        /// </summary>
        /// <param name="type">Actor类型</param>
        /// <returns>Actor类型ID</returns>
        public static int GetActorID(Type type) => Actor.TypeToID[type];
        #endregion

        #region Update
        /// <summary>
        /// 每帧更新所有活跃的Actor
        /// </summary>
        public override void PostUpdateEverything() {
            if (Actors == null) {
                return;
            }

            Rectangle mouseRec = Main.MouseWorld.GetRectangle(1);
            bool client = VaultUtils.isClient;

            //快照活跃列表，使 AI 中途的生成 / 销毁不破坏本帧遍历
            updateBuffer.Clear();
            updateBuffer.AddRange(activeActors);

            for (int i = 0; i < updateBuffer.Count; i++) {
                Actor actor = updateBuffer[i];
                if (actor == null || !actor.Active) {
                    continue;
                }

                actor.HoverTP = actor.InScreen && actor.HitBox.Intersects(mouseRec);

                try {
                    bool shouldUpdate = true;
                    foreach (var global in HookPreAI.Enumerate()) {
                        if (!global.PreAI(actor)) {
                            shouldUpdate = false;
                        }
                    }

                    if (shouldUpdate) {
                        //SolidActor 已在 PreUpdateEntities 中提前更新(含客户端重对齐)，此处跳过避免重复
                        if (actor is SolidActor solid && solid.PreUpdatedThisFrame) {
                            solid.PreUpdatedThisFrame = false;
                        }
                        else {
                            actor.AI();
                            actor.Position += actor.Velocity;
                            if (client) {
                                actor.ApplyClientReconciliation();
                            }
                        }
                    }

                    foreach (var global in HookPostAI.Enumerate()) {
                        global.PostAI(actor);
                    }
                } catch (Exception ex) {
                    VaultMod.Instance.Logger.Error($"Error updating actor {actor.WhoAmI}: {ex}");
                    FreeSlot(actor);
                }
            }

            //服务器权威: 统一在本帧末尾进行节流的增量广播
            if (VaultUtils.isServer) {
                ActorNetWork.ServerBroadcastTick(activeActors);
            }
        }
        #endregion

        #region Draw
        /// <summary>
        /// 绘制所有活跃的Actor
        /// </summary>
        /// <param name="spriteBatch">用于绘制的SpriteBatch</param>
        /// <param name="layer">要绘制的层级</param>
        public static void DrawActors(SpriteBatch spriteBatch, ActorDrawLayer layer = ActorDrawLayer.Default) {
            if (Main.dedServ) {
                return;
            }

            for (int i = 0; i < activeActors.Count; i++) {
                Actor actor = activeActors[i];
                if (actor == null || !actor.Active || actor.DrawLayer != layer) {
                    continue;
                }

                actor.InScreen = VaultUtils.IsPointOnScreen(actor.Position - Main.screenPosition, actor.DrawExtendMode);
                if (!actor.InScreen) {
                    continue;
                }

                Color drawColor = Lighting.GetColor((int)(actor.Position.X / 16f), (int)(actor.Position.Y / 16f));

                bool shouldDraw = true;
                foreach (var global in HookPreDraw.Enumerate()) {
                    if (!global.PreDraw(spriteBatch, actor, drawColor)) {
                        shouldDraw = false;
                    }
                }

                if (shouldDraw && actor.PreDraw(spriteBatch, ref drawColor)) {
                    DefaultDraw(spriteBatch, actor, drawColor);
                }

                actor.PostDraw(spriteBatch, drawColor);

                foreach (var global in HookPostDraw.Enumerate()) {
                    global.PostDraw(spriteBatch, actor, drawColor);
                }

                //绘制调试信息
                DrawActorDebugInfo(spriteBatch, actor);
            }
        }

        /// <summary>
        /// 绘制Actor的调试信息
        /// </summary>
        private static void DrawActorDebugInfo(SpriteBatch spriteBatch, Actor actor) {
            //如果没有启用任何调试选项，直接返回
            if (!DebugSettings.ActorBoxSizeDraw &&
                !DebugSettings.ActorShowName &&
                !DebugSettings.ActorShowPosition &&
                !DebugSettings.ActorShowID &&
                !DebugSettings.ActorShowVelocity) {
                return;
            }

            Vector2 drawPos = actor.Position - Main.screenPosition;

            //绘制碰撞箱
            if (DebugSettings.ActorBoxSizeDraw) {
                Main.spriteBatch.Draw(VaultAsset.placeholder2.Value, drawPos,
                    new Rectangle(0, 0, (int)actor.Size.X, (int)actor.Size.Y),
                    Color.Cyan * 0.3f, 0, Vector2.Zero, 1, SpriteEffects.None, 0);

                Main.spriteBatch.Draw(VaultAsset.placeholder2.Value, actor.Center - Main.screenPosition,
                    new Rectangle(0, 0, 4, 4), Color.Yellow * 0.8f, 0, new Vector2(2, 2), 1, SpriteEffects.None, 0);
            }

            //构建调试信息文本
            float yOffset = -20;
            if (DebugSettings.ActorShowName) {
                string nameText = actor.GetType().Name;
                Utils.DrawBorderStringFourWay(spriteBatch, FontAssets.ItemStack.Value, nameText,
                    drawPos.X, drawPos.Y + yOffset, Color.Cyan, Color.Black, Vector2.Zero, 0.9f);
                yOffset -= 18;
            }

            if (DebugSettings.ActorShowPosition) {
                string posText = $"Pos: {(int)actor.Position.X}, {(int)actor.Position.Y}";
                Utils.DrawBorderStringFourWay(spriteBatch, FontAssets.ItemStack.Value, posText,
                    drawPos.X, drawPos.Y + yOffset, Color.LightGreen, Color.Black, Vector2.Zero, 0.8f);
                yOffset -= 16;
            }

            if (DebugSettings.ActorShowID) {
                string idText = $"ID: {actor.ID} | WhoAmI: {actor.WhoAmI}";
                Utils.DrawBorderStringFourWay(spriteBatch, FontAssets.ItemStack.Value, idText,
                    drawPos.X, drawPos.Y + yOffset, Color.Yellow, Color.Black, Vector2.Zero, 0.8f);
                yOffset -= 16;
            }

            if (DebugSettings.ActorShowVelocity) {
                string velText = $"Vel: {actor.Velocity.X:F1}, {actor.Velocity.Y:F1}";
                Utils.DrawBorderStringFourWay(spriteBatch, FontAssets.ItemStack.Value, velText,
                    drawPos.X, drawPos.Y + yOffset, Color.Orange, Color.Black, Vector2.Zero, 0.8f);
            }
        }

        private static void DefaultDraw(SpriteBatch spriteBatch, Actor actor, Color drawColor) {
            Rectangle frame = new Rectangle(0, 0, actor.Width, actor.Height);
            spriteBatch.Draw(
                VaultAsset.placeholder3.Value,
                actor.Position - Main.screenPosition,
                frame,
                drawColor,
                0f,
                Vector2.Zero,
                1f,
                SpriteEffects.None,
                0f
            );
        }
        #endregion

        #region World Events
        /// <summary>
        /// 在游戏世界加载时初始化Actor数组
        /// </summary>
        public override void OnWorldLoad() {
            Actors = new Actor[MaxActorCount];
            slotGenerations = new ushort[MaxActorCount];
            ResetSlots();
        }
        /// <summary>
        /// 在游戏世界卸载时清理所有Actor实例
        /// </summary>
        public override void OnWorldUnload() {
            if (Actors != null) {
                for (int i = 0; i < MaxActorCount; i++) {
                    if (Actors[i] != null) {
                        Actors[i].Active = false;
                        Actors[i].ActiveIndex = -1;
                        Actors[i] = null;
                    }
                }
            }
            ResetSlots();
        }
        #endregion

        #region Helper
        /// <summary>
        /// 克隆一个Actor实例
        /// </summary>
        /// <typeparam name="T">Actor类型</typeparam>
        /// <returns>克隆的Actor实例</returns>
        public static T CloneActor<T>() where T : Actor => Actor.IDToInstance[GetActorID<T>()].Clone() as T;

        /// <summary>
        /// 克隆一个Actor实例
        /// </summary>
        /// <param name="id">Actor类型ID</param>
        /// <returns>克隆的Actor实例</returns>
        public static Actor CloneActor(int id) => Actor.IDToInstance[id].Clone();

        /// <summary>
        /// 获取所有活跃的Actor数量
        /// </summary>
        /// <returns>活跃Actor数量</returns>
        public static int GetActiveActorCount() => activeActors.Count;

        /// <summary>
        /// 获取指定类型的所有活跃Actor
        /// </summary>
        /// <typeparam name="T">Actor类型</typeparam>
        /// <returns>活跃Actor列表</returns>
        public static List<T> GetActiveActors<T>() where T : Actor {
            List<T> result = [];
            for (int i = 0; i < activeActors.Count; i++) {
                if (activeActors[i] is T actor && actor.Active) {
                    result.Add(actor);
                }
            }
            return result;
        }
        #endregion
    }
}
