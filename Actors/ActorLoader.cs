using InnoVault.Concurrent;
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
        //更新期对活跃列表的快照，容忍 AI 中途的生成 / 销毁（仅串行回退路径使用）
        private static readonly List<Actor> updateBuffer = new();
        //并行分桶（每帧重建）：独立桶并行更新，串行桶（含 SolidActor）在主线程更新
        private static readonly List<Actor> serialBucket = new(256);
        private static readonly List<Actor> independentBucket = new(256);
        //并行/串行统一读取的整帧上下文，避免逐实体重复计算与闭包分配
        private static Rectangle parallelMouseRec;
        private static bool parallelClient;
        //因调度级异常而自动禁用的标志，仅作用于Actor子系统，与TP子系统互不影响
        //它独立于用户手动的 VaultParallel.EnableParallel 总开关：换世界时复位，使新世界可重新尝试并行
        private static bool actorAutoDisabledByError;
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
            serialBucket.Clear();
            independentBucket.Clear();
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
            //并行阶段禁止结构性增删槽位/活跃列表与网络发送，延迟到主线程执行（此时无法同步返回槽位，返回 -1）
            if (VaultParallel.InParallelPhase) {
                VaultParallel.Defer(() => NewActor(type, position, velocity));
                return -1;
            }

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
            //并行阶段禁止结构性释放槽位与网络发送，延迟到主线程执行
            if (VaultParallel.InParallelPhase) {
                VaultParallel.Defer(() => KillActor(whoAmI, network));
                return;
            }

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
        /// 每帧更新所有活跃的Actor，采用"分桶 + 三阶段"并行管线：<br/>
        /// Phase 0 主线程分桶（独立桶 / 串行桶，SolidActor 归串行）；<br/>
        /// Phase 1 并行更新独立桶；Phase 1b 主线程更新串行桶；Phase 2 主线程排空延迟副作用（生成 / 销毁 / 发包）<br/>
        /// 当并行被禁用或实体数量过少时，完全回退到历史的单线程路径
        /// </summary>
        public override void PostUpdateEverything() {
            if (Actors == null) {
                return;
            }

            //整帧不变的上下文，提前算好
            parallelMouseRec = Main.MouseWorld.GetRectangle(1);
            parallelClient = VaultUtils.isClient;

            //需同时满足"全局主开关开启"与"Actor未因异常被自动禁用"才走并行
            if (actorAutoDisabledByError || !VaultParallel.ShouldRunParallel(activeActors.Count)) {
                //串行回退：快照活跃列表，使 AI 中途的即时生成 / 销毁不破坏本帧遍历
                updateBuffer.Clear();
                updateBuffer.AddRange(activeActors);
                for (int i = 0; i < updateBuffer.Count; i++) {
                    UpdateOneActor(updateBuffer[i]);
                }
            }
            else {
                try {
                    //Phase 0：分桶（并行期 activeActors 只读，增删全部延迟，故桶即快照）
                    ClassifyBuckets();

                    //Phase 1：并行更新独立桶
                    VaultParallel.BeginPhase(0);
                    try {
                        VaultParallel.RunBatch(independentBucket, UpdateOneActor);
                        VaultParallel.EndPhase();

                        //Phase 1b：串行桶在主线程更新（含 SolidActor 的跳过逻辑），副作用即时
                        for (int i = 0; i < serialBucket.Count; i++) {
                            UpdateOneActor(serialBucket[i]);
                        }
                    }
                    finally {
                        //Phase 2：无论并行体/串行桶是否抛异常，都必须退出并行阶段并排空延迟操作
                        //（生成 / 销毁 / 发包 / 报错文本）并回收命令缓冲到对象池；否则调度级异常会
                        //永久丢弃本帧延迟副作用（多人状态不一致、Actor 残留幽灵）。EndPhase 幂等
                        VaultParallel.EndPhase();
                        try {
                            VaultParallel.DrainActionsAndErrors();
                        } catch (Exception drainEx) {
                            //排空本身可能抛异常，独立捕获以免掩盖原始异常或二次泄漏
                            VaultMod.Instance.Logger.Error($"[ActorParallel] Drain after parallel update failed: {drainEx}");
                        }
                    }
                } catch (Exception ex) {
                    //调度级别的异常：仅自动禁用Actor并行并回退到串行（不波及TP子系统），优先保证可用性
                    actorAutoDisabledByError = true;
                    VaultMod.Instance.Logger.Error($"[ActorParallel] Parallel update failed, disabled and fell back to serial: {ex}");
                }
            }

            //服务器权威: 在延迟增删排空之后，统一进行节流的增量广播
            if (VaultUtils.isServer) {
                ActorNetWork.ServerBroadcastTick(activeActors);
            }
        }

        /// <summary>
        /// 按<see cref="Actor.ParallelKind"/>把活跃 Actor 分入独立桶 / 串行桶<br/>
        /// 仅 <see cref="ParallelExecutionKind.Independent"/> 进独立桶；其余（含 Serial 与 SolidActor）走串行桶
        /// </summary>
        private static void ClassifyBuckets() {
            serialBucket.Clear();
            independentBucket.Clear();
            for (int i = 0; i < activeActors.Count; i++) {
                Actor actor = activeActors[i];
                if (actor == null || !actor.Active) {
                    continue;
                }
                if (actor.ParallelKind == ParallelExecutionKind.Independent) {
                    independentBucket.Add(actor);
                }
                else {
                    serialBucket.Add(actor);
                }
            }
        }

        /// <summary>
        /// 单个 Actor 的统一更新体，串行与并行两种调度共用<br/>
        /// 顺序：悬停状态 → PreAI 钩子 → (Solid 跳过 / AI + 位移 + 客户端重对齐) → PostAI 钩子<br/>
        /// 异常按单个 Actor 隔离：并行期延迟释放槽位（结构性修改），串行期立即释放<br/>
        /// 并发约束：当 Actor 为 <see cref="ParallelExecutionKind.Independent"/> 时，本方法会在多个工作线程上并发执行，
        /// 故全局钩子 <see cref="GlobalActor.PreAI"/>/<see cref="GlobalActor.PostAI"/> 会被并发调用于同一个 GlobalActor 单例，
        /// 其实现必须线程安全、不得读写可变实例字段
        /// </summary>
        internal static void UpdateOneActor(Actor actor) {
            if (actor == null || !actor.Active) {
                return;
            }

            try {
                //悬停状态也纳入防御范围：HitBox 若被重写抛出，按单个 Actor 隔离，绝不上浮到调度层触发整套并行回退
                actor.HoverTP = actor.InScreen && actor.HitBox.Intersects(parallelMouseRec);

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
                        if (parallelClient) {
                            actor.ApplyClientReconciliation();
                        }
                    }
                }

                foreach (var global in HookPostAI.Enumerate()) {
                    global.PostAI(actor);
                }
            } catch (Exception ex) {
                VaultMod.Instance.Logger.Error($"Error updating actor {actor.WhoAmI}: {ex}");
                //并行阶段不可立即释放槽位（结构性修改 activeActors），延迟到主线程
                if (VaultParallel.InParallelPhase) {
                    VaultParallel.Defer(() => FreeSlot(actor));
                }
                else {
                    FreeSlot(actor);
                }
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
            //清理共享并行引擎的瞬态状态（与 TP 共用，幂等）
            VaultParallel.Clear();
            //复位"因异常自动禁用"，使新世界可重新尝试并行（避免一次异常永久降级）
            actorAutoDisabledByError = false;
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
