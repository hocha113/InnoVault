using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.Graphics.Shaders;
using Terraria.ModLoader;

namespace InnoVault.PRT
{
    /// <summary>
    /// 粒子或者说尘埃实体的基类，简称为PRT实体，继承它用于实现一些高自定义化的特殊粒子效果
    /// PRTLoader 负责管理游戏中粒子的加载、初始化以及纹理管理，支持通过Mod扩展粒子系统
    /// </summary>
    /// <remarks>
    /// 该类提供了一种全局的粒子系统管理方式通过各种静态字典和列表，PRTLoader 能够有效管理不同类型的粒子，
    /// 包括它们的ID、纹理、所属Mod以及实例数量
    /// </remarks>
    public class PRTLoader : ModSystem, IVaultLoader
    {
        #region Data
        /// <summary>
        /// 游戏中每个世界最多允许存在的粒子数量用于限制游戏中的粒子实体数量，防止性能问题
        /// </summary>
        public const int InGame_World_MaxPRTCount = 20000;
        /// <summary>
        /// 一个字典，用于将粒子类型（Type）映射到粒子ID每个粒子类型都会有一个唯一的ID，方便在系统中进行管理
        /// </summary>
        public static Dictionary<Type, int> PRT_TypeToID { get; private set; } = [];
        /// <summary>
        /// 一个字典，用于将粒子类型（Type）映射到其所属的Mod用于追踪哪些Mod添加了特定类型的粒子
        /// </summary>
        public static Dictionary<Type, Mod> PRT_TypeToMod { get; private set; } = [];
        /// <summary>
        /// 一个字典，将粒子ID映射到其对应的纹理（Texture2D）每个粒子都有一个与其ID对应的纹理，用于渲染粒子的外观
        /// </summary>
        public static Dictionary<int, Texture2D> PRT_IDToTexture { get; private set; } = [];
        /// <summary>
        /// 一个字典，将粒子ID映射到当前游戏世界中的实例数量用于记录每种粒子在当前世界中存在的数量，确保不超过最大限制
        /// </summary>
        public static Dictionary<int, int> PRT_IDToInGame_World_Count { get; private set; } = [];
        /// <summary>
        /// 一个字典，将粒子ID映射到其对应的粒子实例（BasePRT）用于管理每个粒子的实例对象，以便进行粒子的更新和渲染
        /// </summary>
        public static Dictionary<int, BasePRT> PRT_IDToInstances { get; private set; } = [];
        /// <summary>
        /// 一个列表，存储所有活跃的粒子实例（BasePRT）用于批量管理和更新粒子实体
        /// </summary>
        public static List<BasePRT> PRTInstances { get; private set; } = [];
        /// <inheritdoc/>
        public static List<BasePRT> PRT_InGame_World_Inds;
        /// <inheritdoc/>
        public static List<BasePRT> PRT_AlphaBlend_Draw;
        /// <inheritdoc/>
        public static List<BasePRT> PRT_AdditiveBlend_Draw;
        /// <inheritdoc/>
        public static List<BasePRT> PRT_NonPremultiplied_Draw;
        /// <inheritdoc/>
        public static List<BasePRT> PRT_HasShader_Draw;

        internal static readonly PRTDrawModeEnum[] allDrawModes = (PRTDrawModeEnum[])Enum.GetValues(typeof(PRTDrawModeEnum));
        #endregion
        /// <summary>
        /// 加载和初始化数据
        /// </summary>
        public override void Load() {
            PRT_TypeToID = [];
            PRT_TypeToMod = [];
            PRT_IDToTexture = [];
            PRT_IDToInGame_World_Count = [];
            PRT_IDToInstances = [];
            PRTInstances = [];
            PRT_InGame_World_Inds = [];
            PRT_AlphaBlend_Draw = [];
            PRT_AdditiveBlend_Draw = [];
            PRT_NonPremultiplied_Draw = [];
            PRT_HasShader_Draw = [];

            PRTInstances = VaultUtils.GetSubclassInstances<BasePRT>(false);

            foreach (var particle in PRTInstances) {
                Type type = particle.GetType();
                int ID = PRT_TypeToID.Count;
                PRT_TypeToID[type] = ID;
                particle.ID = ID;
                PRT_IDToInstances.Add(ID, particle);
                PRT_IDToInGame_World_Count.Add(ID, 0);
                VaultUtils.AddTypeModAssociation(PRT_TypeToMod, type, ModLoader.Mods);
            }

            On_Main.DrawInfernoRings += DrawHook;
        }
        /// <summary>
        /// 卸载数据
        /// </summary>
        public override void Unload() {
            PRT_TypeToID = null;
            PRT_TypeToMod = null;
            PRT_IDToTexture = null;
            PRT_IDToInGame_World_Count = null;
            PRT_IDToInstances = null;
            PRTInstances = null;
            PRT_InGame_World_Inds = null;
            PRT_AlphaBlend_Draw = null;
            PRT_AdditiveBlend_Draw = null;
            PRT_NonPremultiplied_Draw = null;
            PRT_HasShader_Draw = null;
            On_Main.DrawInfernoRings -= DrawHook;
        }

        void IVaultLoader.LoadAsset() {
            foreach (var prt in PRTInstances) {
                Type type = prt.GetType();
                string texturePath = type.Namespace.Replace('.', '/') + "/" + type.Name;
                if (prt.Texture != "") {
                    texturePath = prt.Texture;
                }
                PRT_IDToTexture[PRT_TypeToID[type]] = ModContent.Request<Texture2D>(texturePath, AssetRequestMode.ImmediateLoad).Value;
            }
        }

        /// <summary>
        /// 根据指定的粒子绘制模式，返回对应的粒子实例列表
        /// </summary>
        /// <param name="drawMode">指定的粒子绘制模式 <see cref="PRTDrawModeEnum"/></param>
        /// <returns>与指定绘制模式对应的粒子实例列表，如果模式未定义则返回 null</returns>
        /// <exception cref="ArgumentOutOfRangeException">如果传入的绘制模式不在已定义的范围内</exception>
        public static List<BasePRT> GetPRTInstancesByDrawMode(PRTDrawModeEnum drawMode) {
            return drawMode switch {
                PRTDrawModeEnum.AlphaBlend => PRT_AlphaBlend_Draw,
                PRTDrawModeEnum.AdditiveBlend => PRT_AdditiveBlend_Draw,
                PRTDrawModeEnum.NonPremultiplied => PRT_NonPremultiplied_Draw,
                _ => null
            };
        }

        /// <summary>
        /// 生成提供给世界的粒子实例
        /// </summary>
        public static void AddParticle(BasePRT particle) {
            if (Main.gamePaused || Main.dedServ || PRT_InGame_World_Inds == null) {
                return;
            }

            if (particle.PRTLayersMode == PRTLayersModeEnum.None) {
                return;
            }

            int id = GetParticleID(particle.GetType());

            if (PRT_IDToInGame_World_Count[id] >= particle.InGame_World_MaxCount
                || PRT_InGame_World_Inds.Count >= InGame_World_MaxPRTCount) {
                return;
            }

            particle.active = true;
            particle.ID = id;
            particle.SetProperty();

            PRT_InGame_World_Inds.Add(particle);
        }

        /// <summary>
        /// 使用指定的属性初始化并添加一个新粒子到粒子系统中
        /// </summary>
        /// <param name="prtEntity">要初始化和添加的粒子实例</param>
        /// <param name="position">粒子在二维空间中的初始位置</param>
        /// <param name="velocity">粒子的初始速度向量</param>
        /// <param name="color">粒子的颜色，默认为默认颜色</param>
        /// <param name="scale">粒子的缩放比例，默认为1</param>
        /// <param name="ai0">粒子的自定义属性 ai0，默认为0</param>
        /// <param name="ai1">粒子的自定义属性 ai1，默认为0</param>
        /// <param name="ai2">粒子的自定义属性 ai2，默认为0</param>
        public static BasePRT NewParticle(BasePRT prtEntity, Vector2 position, Vector2 velocity
            , Color color = default, float scale = 1f, int ai0 = 0, int ai1 = 0, int ai2 = 0) {
            prtEntity.Position = position;
            prtEntity.Velocity = velocity;
            prtEntity.Scale = scale;
            prtEntity.Color = color;
            prtEntity.ai[0] = ai0;
            prtEntity.ai[1] = ai1;
            prtEntity.ai[2] = ai2;
            AddParticle(prtEntity);
            return prtEntity;
        }

        /// <summary>
        /// 使用指定的属性初始化并添加一个新粒子到粒子系统中
        /// </summary>
        /// <param name="prtID">要初始化和添加的粒子ID</param>
        /// <param name="position">粒子在二维空间中的初始位置</param>
        /// <param name="velocity">粒子的初始速度向量</param>
        /// <param name="color">粒子的颜色，默认为默认颜色</param>
        /// <param name="scale">粒子的缩放比例，默认为1</param>
        /// <param name="ai0">粒子的自定义属性 ai0，默认为0</param>
        /// <param name="ai1">粒子的自定义属性 ai1，默认为0</param>
        /// <param name="ai2">粒子的自定义属性 ai2，默认为0</param>
        public static BasePRT NewParticle(int prtID, Vector2 position, Vector2 velocity
            , Color color = default, float scale = 1f, int ai0 = 0, int ai1 = 0, int ai2 = 0) {
            BasePRT prtEntity = PRT_IDToInstances[prtID].Clone();
            prtEntity.Position = position;
            prtEntity.Velocity = velocity;
            prtEntity.Scale = scale;
            prtEntity.Color = color;
            prtEntity.ai[0] = ai0;
            prtEntity.ai[1] = ai1;
            prtEntity.ai[2] = ai2;
            AddParticle(prtEntity);
            return prtEntity;
        }

        /// <summary>
        /// 使用指定的属性初始化并添加一个新粒子到粒子系统中
        /// </summary>
        /// <param name="center"></param>
        /// <param name="velocity"></param>
        /// <param name="type"></param>
        /// <param name="newColor"></param>
        /// <param name="Scale"></param>
        /// <returns></returns>
        public static BasePRT NewParticle(Vector2 center, Vector2 velocity, int type, Color newColor = default, float Scale = 1f) {
            BasePRT prtEntity = PRT_IDToInstances[type].Clone();
            prtEntity.Position = center;
            prtEntity.Velocity = velocity;
            prtEntity.Color = newColor;
            prtEntity.Scale = Scale;
            AddParticle(prtEntity);
            return prtEntity;
        }

        /// <summary>
        /// 使用指定的属性初始化并添加一个新粒子到粒子系统中
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="center"></param>
        /// <param name="velocity"></param>
        /// <param name="newColor"></param>
        /// <param name="Scale"></param>
        /// <returns></returns>
        public static T NewParticle<T>(Vector2 center, Vector2 velocity, Color newColor = default, float Scale = 1f) where T : BasePRT {
            T prtEntity = GetPRTInstance<T>();
            prtEntity.Position = center;
            prtEntity.Velocity = velocity;
            prtEntity.Color = newColor;
            prtEntity.Scale = Scale;
            AddParticle(prtEntity);
            return prtEntity;
        }

        /// <summary>
        /// 获得目标粒子的实例克隆
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static T GetPRTInstance<T>() where T : BasePRT => PRT_IDToInstances[GetParticleID<T>()].Clone() as T;
        /// <summary>
        /// 获得目标粒子的实例克隆
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public static BasePRT GetPRTInstance(int id) => PRT_IDToInstances[id].Clone();

        /// <summary>
        /// 初始化目标粒子实例并设置其属性
        /// </summary>
        /// <typeparam name="T">粒子类型，必须继承自 BasePRT</typeparam>
        /// <param name="position">粒子的位置</param>
        /// <param name="velocity">粒子的速度</param>
        /// <param name="color">粒子的颜色</param>
        /// <param name="scale">粒子的缩放比例</param>
        /// <returns>带有指定属性的粒子实例</returns>
        /// <remarks>
        /// 此方法不仅会克隆一个目标粒子的实例，还会对其进行初始化，
        /// 包括设置位置、速度、颜色和缩放比例等属性
        /// 使用此方法可以快速创建和设置粒子对象，适用于需要动态生成粒子效果的场景
        /// </remarks>
        public static T CreateAndInitializePRT<T>(Vector2 position, Vector2 velocity, Color color, float scale) where T : BasePRT {
            T prt = GetPRTInstance<T>();
            prt.active = true;
            prt.ID = GetParticleID<T>();
            prt.SetProperty();
            prt.Position = position;
            prt.Velocity = velocity;
            prt.Color = color;
            prt.Scale = scale;
            return prt;
        }

        /// <summary>
        /// 更新在所有实体之前，这个进行独立的PRT粒子数量计数，在更新进行加法计数，这样才能保证弹幕、玩家、等程序可以获取正确的粒子数量
        /// </summary>
        public override void PreUpdateEntities() {
            if (Main.dedServ) {//不要在服务器上更新逻辑
                return;
            }

            foreach (BasePRT particle in PRT_InGame_World_Inds) {
                if (particle == null || !particle.active) {
                    continue;
                }

                PRT_IDToInGame_World_Count[particle.ID]++;
            }
        }

        /// <summary>
        /// 在最后调用更新逻辑，进行CG机制，并重置粒子计数
        /// </summary>
        public override void PostUpdateEverything() {
            if (Main.dedServ) {//不要在服务器上更新逻辑
                return;
            }

            for (int i = 0; i < PRT_InGame_World_Inds.Count; i++) {
                BasePRT particle = PRT_InGame_World_Inds[i];

                if (particle == null || !particle.active) {
                    continue;
                }

                try {
                    UpdateParticleVelocity(particle);
                    UpdateParticleTime(particle);
                    particle.AI();
                } catch (Exception) {
                    VaultMod.Instance.Logger.Info($"ERROR:{particle} IS UPDATA");
                    particle.active = false;
                    continue;
                }
                if (particle.Time >= particle.Lifetime && particle.SetLifetime) {
                    particle.active = false;
                    continue;
                }
                if (particle.ShouldKillWhenOffScreen && !VaultUtils.IsPointOnScreen(particle.Position - Main.screenPosition)) {
                    particle.active = false;
                }
            }

            foreach (var particle in PRTInstances) {
                PRT_IDToInGame_World_Count[particle.ID] = 0;
            }

            PRT_InGame_World_Inds.RemoveAll(p => p == null || !p.active);
        }

        private static void UpdateParticleVelocity(BasePRT particle) {
            if (particle.ShouldUpdatePosition()) {
                particle.Position += particle.Velocity;
            }
        }

        private static void UpdateParticleTime(BasePRT particle) => particle.Time++;

        /// <summary>
        /// 获取这个<see cref="BasePRT"/>类型的ID，每一个PRT类型都拥有一个独一无二的ID
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static int GetParticleID<T>() where T : BasePRT => PRT_TypeToID[typeof(T)];
        /// <summary>
        /// 获取这个<see cref="BasePRT"/>类型的ID，每一个PRT类型都拥有一个独一无二的ID
        /// </summary>
        /// <param name="sType"></param>
        /// <returns></returns>
        public static int GetParticleID(Type sType) => PRT_TypeToID[sType];

        private static void AddDrawHander() {
            foreach (BasePRT particle in PRT_InGame_World_Inds) {
                if (particle == null || !particle.active) {
                    continue;
                }

                if (particle.PRTLayersMode == PRTLayersModeEnum.NoDraw) {
                    continue;
                }

                if (particle.shader != null) {
                    PRT_HasShader_Draw.Add(particle);
                    continue;
                }

                GetPRTInstancesByDrawMode(particle.PRTDrawMode).Add(particle);
            }
        }

        private static void DrawHook(Terraria.On_Main.orig_DrawInfernoRings orig, Main self) {
            Draw(Main.spriteBatch);
            orig(self);
        }

        private static void defaultDraw(SpriteBatch spriteBatch, BasePRT particle) {
            Texture2D value = PRT_IDToTexture[particle.ID];
            if (particle.Frame == default) {
                particle.Frame = new Rectangle(0, 0, value.Width, value.Height);
            }
            spriteBatch.Draw(value, particle.Position - Main.screenPosition, particle.Frame, particle.Color
                , particle.Rotation, particle.Frame.Size() * 0.5f, particle.Scale, SpriteEffects.None, 0f);
        }

        /// <summary>
        /// 根据指定的绘制模式 <see cref="PRTDrawModeEnum"/>，为 <see cref="SpriteBatch"/> 设置适当的渲染状态并开始绘制
        /// </summary>
        /// <param name="drawMode">绘制模式枚举 <see cref="PRTDrawModeEnum"/></param>
        /// <param name="spriteBatch">用于进行绘制操作的 <see cref="SpriteBatch"/></param>
        public static void BeginDrawingWithMode(PRTDrawModeEnum drawMode, SpriteBatch spriteBatch) {
            var rasterizer = Main.Rasterizer;
            rasterizer.ScissorTestEnable = true;
            Main.instance.GraphicsDevice.RasterizerState.ScissorTestEnable = true;
            Main.instance.GraphicsDevice.ScissorRectangle = new Rectangle(0, 0, Main.screenWidth, Main.screenHeight);

            switch (drawMode) {
                case PRTDrawModeEnum.AlphaBlend:
                    spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState
                    , DepthStencilState.None, rasterizer, null, Main.GameViewMatrix.TransformationMatrix);
                    break;
                case PRTDrawModeEnum.AdditiveBlend:
                    spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Additive, SamplerState.PointClamp
                    , DepthStencilState.Default, rasterizer, null, Main.GameViewMatrix.TransformationMatrix);
                    break;
                case PRTDrawModeEnum.NonPremultiplied:
                    spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.PointClamp
                    , DepthStencilState.Default, rasterizer, null, Main.GameViewMatrix.TransformationMatrix);
                    break;
            }
        }

        /// <summary>
        /// 完整的处理一个粒子的绘制操作
        /// </summary>
        /// <param name="spriteBatch"></param>
        /// <param name="particle"></param>
        public static void PRTInstanceDraw(SpriteBatch spriteBatch, BasePRT particle) {
            if (particle.PreDraw(spriteBatch)) {
                defaultDraw(spriteBatch, particle);
            }
            particle.PostDraw(spriteBatch);
        }

        /// <summary>
        /// 用于绘制使用Shader效果的粒子集合
        /// </summary>
        /// <param name="spriteBatch">画布实例</param>
        /// <param name="particles">传入的粒子集合，其中所有的粒子要求<see cref="BasePRT.shader"/>不为<see langword="null"/></param>
        public static void HanderHasShaderPRTDrawList(SpriteBatch spriteBatch, List<BasePRT> particles) {
            IEnumerable<IGrouping<ArmorShaderData, BasePRT>> groupedParticles = particles.GroupBy(p => p.shader);
            foreach (IGrouping<ArmorShaderData, BasePRT> group in groupedParticles) {
                spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.PointClamp
                    , DepthStencilState.None, RasterizerState.CullNone, null, Main.Transform);
                group.Key?.Apply(null);
                foreach (BasePRT particle in group) {
                    PRTInstanceDraw(spriteBatch, particle);
                }
                spriteBatch.End();
            }
        }

        /// <summary>
        /// 所有PRT的绘制更新都在这里
        /// </summary>
        /// <param name="spriteBatch"></param>
        public static void Draw(SpriteBatch spriteBatch) {
            if (PRT_InGame_World_Inds.Count <= 0) {
                return;
            }

            spriteBatch.End();
            AddDrawHander();

            foreach (PRTDrawModeEnum drawMode in allDrawModes) {
                List<BasePRT> targetPRTs = GetPRTInstancesByDrawMode(drawMode);
                if (targetPRTs.Count <= 0) {
                    continue;
                }

                BeginDrawingWithMode(drawMode, spriteBatch);
                for (int i = 0; i < targetPRTs.Count; i++) {
                    PRTInstanceDraw(spriteBatch, targetPRTs[i]);
                }
                spriteBatch.End();
            }

            if (PRT_HasShader_Draw.Count > 0) {
                HanderHasShaderPRTDrawList(spriteBatch, PRT_HasShader_Draw);
            }

            PRT_AlphaBlend_Draw.Clear();
            PRT_NonPremultiplied_Draw.Clear();
            PRT_AdditiveBlend_Draw.Clear();
            PRT_HasShader_Draw.Clear();

            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState, DepthStencilState.None, Main.Rasterizer, null, Main.Transform);
        }

        /// <summary>
        /// 给出可用粒子槽的数量。当一次需要多个粒子来制作效果，并且不希望由于缺乏粒子槽而只绘制一半时非常有用
        /// </summary>
        /// <returns></returns>
        public static int NumberUsablePRT() {
            return Main.dedServ || PRT_InGame_World_Inds == null ? 0 : InGame_World_MaxPRTCount - PRT_InGame_World_Inds.Count;
        }
    }
}
