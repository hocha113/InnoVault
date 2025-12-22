using InnoVault.GameSystem;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Terraria;
using Terraria.ModLoader;
using Terraria.Graphics.Renderers;
using Terraria.Graphics;

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
        public const int MaxActorCount = 10000;
        /// <summary>
        /// 游戏世界中所有活跃的Actor实例
        /// </summary>
        public static Actor[] Actors { get; private set; }
        /// <summary>
        /// 下一个可用的Actor槽位索引
        /// </summary>
        private static int nextFreeSlot = 0;

        private static readonly List<VaultHookMethodCache<GlobalActor>> hooks = [];
        internal static VaultHookMethodCache<GlobalActor> HookPreAI;
        internal static VaultHookMethodCache<GlobalActor> HookPostAI;
        internal static VaultHookMethodCache<GlobalActor> HookOnSpawn;
        internal static VaultHookMethodCache<GlobalActor> HookPreDraw;
        internal static VaultHookMethodCache<GlobalActor> HookPostDraw;
        #endregion

        #region Load and Unload
        void IVaultLoader.LoadData() {
            Actors = new Actor[MaxActorCount];

            VaultTypeRegistry<Actor>.CompleteLoading();

            HookPreAI = AddHook<Func<Actor, bool>>(a => a.PreAI);
            HookPostAI = AddHook<Action<Actor>>(a => a.PostAI);
            HookOnSpawn = AddHook<Action<Actor>>(a => a.OnSpawn);
            HookPreDraw = AddHook<Func<SpriteBatch, Actor, Color, bool>>(a => a.PreDraw);
            HookPostDraw = AddHook<Action<SpriteBatch, Actor, Color>>(a => a.PostDraw);

            On_Main.DrawBackgroundBlackFill += DrawBeforeTilesHook;
            On_LegacyPlayerRenderer.DrawPlayers += DrawPlayersHook;
            On_Main.DrawInfernoRings += DrawDefaultHook;
        }

        void IVaultLoader.UnLoadData() {
            Actors = null;

            hooks.Clear();
            HookPreAI = null;
            HookPostAI = null;
            HookOnSpawn = null;
            HookPreDraw = null;
            HookPostDraw = null;

            On_Main.DrawBackgroundBlackFill -= DrawBeforeTilesHook;
            On_LegacyPlayerRenderer.DrawPlayers -= DrawPlayersHook;
            On_Main.DrawInfernoRings -= DrawDefaultHook;

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
            if (VaultUtils.isClient) {
                ActorNetWork.SendNewActor(type, -1, position, velocity);
                return -1;
            }

            int slot = FindNextFreeSlot();
            if (slot == -1) {
                return -1;
            }

            AddActor(type, slot, position, velocity);
            ActorNetWork.SendNewActor(type, slot, position, velocity);

            return slot;
        }

        /// <summary>
        /// 直接添加一个Actor到指定槽位
        /// </summary>
        /// <param name="type"></param>
        /// <param name="slot"></param>
        /// <param name="position"></param>
        /// <param name="velocity"></param>
        public static void AddActor(int type, int slot, Vector2 position, Vector2 velocity) {
            Actor actor = Actor.IDToInstance[type].Clone();
            actor.WhoAmI = slot;
            actor.Active = true;
            actor.Position = position;
            actor.Velocity = velocity;

            Actors[slot] = actor;

            actor.OnSpawn(null);

            foreach (var global in HookOnSpawn.Enumerate()) {
                global.OnSpawn(actor);
            }
        }

        /// <summary>
        /// 查找下一个可用的Actor槽位
        /// </summary>
        /// <returns>可用槽位的索引，如果没有可用槽位返回-1</returns>
        public static int FindNextFreeSlot() {
            int startIndex = nextFreeSlot;

            for (int i = 0; i < MaxActorCount; i++) {
                int index = (startIndex + i) % MaxActorCount;
                if (Actors[index] == null || !Actors[index].Active) {
                    nextFreeSlot = (index + 1) % MaxActorCount;
                    return index;
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
            if (actor != null && actor.Active) {
                actor.Active = false;

                if (network) {
                    ActorNetWork.SendKillActor(whoAmI);
                }
            }
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
            for (int i = 0; i < MaxActorCount; i++) {
                Actor actor = Actors[i];
                if (actor == null || !actor.Active) {
                    continue;
                }

                try {
                    bool shouldUpdate = true;
                    foreach (var global in HookPreAI.Enumerate()) {
                        if (!global.PreAI(actor)) {
                            shouldUpdate = false;
                        }
                    }

                    if (shouldUpdate) {
                        actor.AI();
                        actor.Position += actor.Velocity;
                    }

                    foreach (var global in HookPostAI.Enumerate()) {
                        global.PostAI(actor);
                    }

                    if (actor.NetUpdate) {
                        actor.NetUpdate = false;
                        ActorNetWork.SendActorData(actor);
                    }
                } catch (Exception ex) {
                    VaultMod.Instance.Logger.Error($"Error updating actor {i}: {ex}");
                    actor.Active = false;
                }
            }
        }
        #endregion

        #region Draw
        /// <summary>
        /// 在物块绘制之后绘制所有活跃的Actor
        /// </summary>
        public override void PostDrawTiles() {
            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState
                , DepthStencilState.None, Main.Rasterizer, null, Main.GameViewMatrix.TransformationMatrix);

            DrawActors(Main.spriteBatch, ActorDrawLayer.AfterTiles);

            Main.spriteBatch.End();
        }

        private static void DrawBeforeTilesHook(On_Main.orig_DrawBackgroundBlackFill orig, Main self) {
            orig(self);

            if (Main.gameMenu || Main.mapFullscreen) {
                return;
            }

            Main.spriteBatch.End();
            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState
                , DepthStencilState.None, Main.Rasterizer, null, Main.GameViewMatrix.TransformationMatrix);

            DrawActors(Main.spriteBatch, ActorDrawLayer.BeforeTiles);

            Main.spriteBatch.End();
            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState
                , DepthStencilState.None, Main.Rasterizer, null, Main.GameViewMatrix.TransformationMatrix);
        }

        private static void DrawPlayersHook(On_LegacyPlayerRenderer.orig_DrawPlayers orig, LegacyPlayerRenderer self, Camera camera, IEnumerable<Player> players) {
            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState
                , DepthStencilState.None, Main.Rasterizer, null, Main.GameViewMatrix.TransformationMatrix);

            DrawActors(Main.spriteBatch, ActorDrawLayer.BeforePlayers);

            Main.spriteBatch.End();

            orig(self, camera, players);

            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState
                , DepthStencilState.None, Main.Rasterizer, null, Main.GameViewMatrix.TransformationMatrix);

            DrawActors(Main.spriteBatch, ActorDrawLayer.AfterPlayers);

            Main.spriteBatch.End();
        }

        private static void DrawDefaultHook(Terraria.On_Main.orig_DrawInfernoRings orig, Main self) {
            Main.spriteBatch.End();
            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState
                , DepthStencilState.None, Main.Rasterizer, null, Main.GameViewMatrix.TransformationMatrix);

            DrawActors(Main.spriteBatch, ActorDrawLayer.Default);

            Main.spriteBatch.End();
            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState
                , DepthStencilState.None, Main.Rasterizer, null, Main.GameViewMatrix.TransformationMatrix);

            orig(self);
        }

        /// <summary>
        /// 绘制所有活跃的Actor
        /// </summary>
        /// <param name="spriteBatch">用于绘制的SpriteBatch</param>
        /// <param name="layer">要绘制的层级</param>
        public static void DrawActors(SpriteBatch spriteBatch, ActorDrawLayer layer = ActorDrawLayer.Default) {
            if (Main.dedServ) {
                return;
            }

            for (int i = 0; i < MaxActorCount; i++) {
                Actor actor = Actors[i];
                if (actor == null || !actor.Active || actor.DrawLayer != layer) {
                    continue;
                }

                if (!VaultUtils.IsPointOnScreen(actor.Position - Main.screenPosition, 160)) {
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
            nextFreeSlot = 0;
        }
        /// <summary>
        /// 在游戏世界卸载时清理所有Actor实例
        /// </summary>
        public override void OnWorldUnload() {
            if (Actors != null) {
                for (int i = 0; i < MaxActorCount; i++) {
                    if (Actors[i] != null) {
                        Actors[i].Active = false;
                        Actors[i] = null;
                    }
                }
            }
            nextFreeSlot = 0;
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
        public static int GetActiveActorCount() {
            int count = 0;
            for (int i = 0; i < MaxActorCount; i++) {
                if (Actors[i] != null && Actors[i].Active) {
                    count++;
                }
            }
            return count;
        }

        /// <summary>
        /// 获取指定类型的所有活跃Actor
        /// </summary>
        /// <typeparam name="T">Actor类型</typeparam>
        /// <returns>活跃Actor列表</returns>
        public static List<T> GetActiveActors<T>() where T : Actor {
            List<T> result = [];
            for (int i = 0; i < MaxActorCount; i++) {
                if (Actors[i] != null && Actors[i].Active && Actors[i] is T actor) {
                    result.Add(actor);
                }
            }
            return result;
        }
        #endregion
    }
}
