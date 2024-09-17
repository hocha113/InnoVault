using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.Graphics.Renderers;
using Terraria.ModLoader;
using Terraria.ModLoader.Core;

namespace InnoVault.PRT
{
    public class PRTLoader : ModSystem, IVaultLoader
    {
        public const int InGame_World_MaxPRTCount = 10000;
        public static Dictionary<Type, int> PRT_TypeToID;
        public static Dictionary<Type, Mod> PRT_TypeToMod;
        public static Dictionary<int, Texture2D> PRT_IDToTexture;
        public static Dictionary<int, int> PRT_IDToInGame_World_Count;
        public static Dictionary<int, BasePRT> PRT_IDToInstances;
        public static List<BasePRT> PRTInstances;
        private static List<BasePRT> PRT_InGame_World_Inds;
        private static List<BasePRT> PRT_InGame_ToKill_Inds;
        private static List<BasePRT> PRT_AlphaBlend_Draw;
        private static List<BasePRT> PRT_AdditiveBlend_Draw;
        private static List<BasePRT> PRT_NonPremultiplied_Draw;
        public override void Load() {
            PRT_TypeToID = [];
            PRT_TypeToMod = [];
            PRT_IDToTexture = [];
            PRT_IDToInGame_World_Count = [];
            PRT_IDToInstances = [];
            PRTInstances = [];
            PRT_InGame_World_Inds = [];
            PRT_InGame_ToKill_Inds = [];
            PRT_AlphaBlend_Draw = [];
            PRT_AdditiveBlend_Draw = [];
            PRT_NonPremultiplied_Draw = [];

            PRTInstances = VaultUtils.HanderSubclass<BasePRT>(false);

            Mod[] mods = ModLoader.Mods;

            foreach (var particle in PRTInstances) {
                Type type = particle.GetType();
                int ID = PRT_TypeToID.Count;
                PRT_TypeToID[type] = ID;
                particle.ID = ID;
                PRT_IDToInstances.Add(ID, particle);
                PRT_IDToInGame_World_Count.Add(ID, 0);
                foreach (var mod in mods) {
                    Type[] fromModCodeTypes = AssemblyManager.GetLoadableTypes(mod.Code);
                    if (fromModCodeTypes.Contains(type)) {
                        PRT_TypeToMod.Add(type, mod);
                        break;
                    }
                }
            }

            On_Main.DrawInfernoRings += DrawHook;
        }
        public override void Unload() {
            PRT_TypeToID = null;
            PRT_TypeToMod = null;
            PRT_IDToTexture = null;
            PRT_IDToInGame_World_Count = null;
            PRT_IDToInstances = null;
            PRTInstances = null;
            PRT_InGame_World_Inds = null;
            PRT_InGame_ToKill_Inds = null;
            PRT_AlphaBlend_Draw = null;
            PRT_AdditiveBlend_Draw = null;
            PRT_NonPremultiplied_Draw = null;
            On_Main.DrawInfernoRings -= DrawHook;
        }

        void IVaultLoader.LoadAsset() {
            foreach (var prt in PRTInstances) {
                Type type = prt.GetType();
                string texturePath = type.Namespace.Replace('.', '/') + "/" + type.Name;
                if (prt.Texture != "") {
                    texturePath = prt.Texture;
                }
                PRT_IDToTexture[PRT_TypeToID[type]] = ModContent.Request<Texture2D>(texturePath).Value;
            }
        }

        /// <summary>
        /// 生成提供给世界的粒子实例。如果达到颗粒限值，但该颗粒被标记为重要，它将尝试替换不重要的颗粒
        /// </summary>
        public static void AddParticle(BasePRT particle) {
            if (Main.gamePaused || Main.dedServ || PRT_InGame_World_Inds == null) {
                return;
            }

            int id = GetParticleID(particle.GetType());

            if (PRT_IDToInGame_World_Count[particle.ID] >= particle.InGame_World_MaxCount) {
                return;
            }

            particle.ID = id;
            particle.SetPRT();

            PRT_InGame_World_Inds.Add(particle);
        }

        /// <summary>
        /// 使用指定的属性初始化并添加一个新粒子到粒子系统中
        /// </summary>
        /// <param name="particle">要初始化和添加的粒子实例</param>
        /// <param name="position">粒子在二维空间中的初始位置</param>
        /// <param name="velocity">粒子的初始速度向量</param>
        /// <param name="color">粒子的颜色，默认为默认颜色</param>
        /// <param name="scale">粒子的缩放比例，默认为1</param>
        /// <param name="ai0">粒子的自定义属性 ai0，默认为0</param>
        /// <param name="ai1">粒子的自定义属性 ai1，默认为0</param>
        /// <param name="ai2">粒子的自定义属性 ai2，默认为0</param>
        public static void NewParticle(BasePRT particle, Vector2 position, Vector2 velocity
            , Color color = default, float scale = 1f, int ai0 = 0, int ai1 = 0, int ai2 = 0) {
            particle.Position = position;
            particle.Velocity = velocity;
            particle.Scale = scale;
            particle.Color = color;
            particle.ai[0] = ai0;
            particle.ai[1] = ai1;
            particle.ai[2] = ai2;
            AddParticle(particle);
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
        public static void NewParticle(int prtID, Vector2 position, Vector2 velocity
            , Color color = default, float scale = 1f, int ai0 = 0, int ai1 = 0, int ai2 = 0) {
            BasePRT particle = PRT_IDToInstances[prtID].Clone();
            particle.Position = position;
            particle.Velocity = velocity;
            particle.Scale = scale;
            particle.Color = color;
            particle.ai[0] = ai0;
            particle.ai[1] = ai1;
            particle.ai[2] = ai2;
            AddParticle(particle);
        }

        public override void PostUpdateEverything() {
            if (Main.dedServ) {//不要在服务器上更新逻辑
                return;
            }

            foreach (BasePRT particle in PRT_InGame_World_Inds) {
                if (particle == null) {
                    continue;
                }
                UpdateParticleVelocity(particle);
                UpdateParticleTime(particle);
                particle.AI();
                PRT_IDToInGame_World_Count[particle.ID]++;
            }
            ParticleGarbageCollection(ref PRT_InGame_World_Inds);
            PRT_InGame_World_Inds.RemoveAll(particle => particle.Time >= particle.Lifetime && particle.SetLifetime || PRT_InGame_ToKill_Inds.Contains(particle));
            PRT_InGame_ToKill_Inds.Clear();
        }

        public static void ParticleGarbageCollection(ref List<BasePRT> particles) {
            bool isGC(BasePRT p) => p.Time >= p.Lifetime && p.SetLifetime || PRT_InGame_ToKill_Inds.Contains(p);
            particles.RemoveAll(isGC);
        }
        public static void UpdateParticleVelocity(BasePRT particle) => particle.Position += particle.Velocity;
        public static void UpdateParticleTime(BasePRT particle) => particle.Time++;
        public static void RemoveParticle(BasePRT particle) => PRT_InGame_ToKill_Inds.Add(particle);
        public static int GetParticleID<T>() where T : BasePRT => PRT_TypeToID[typeof(T)];
        public static int GetParticleID(Type sType) => PRT_TypeToID[sType];

        private static void AddDrawHander() {
            foreach (BasePRT particle in PRT_InGame_World_Inds) {
                if (particle == null) {
                    continue;
                }

                switch (particle.PRTDrawMode) {
                    case PRTDrawModeEnum.AlphaBlend:
                        PRT_AlphaBlend_Draw.Add(particle);
                        break;
                    case PRTDrawModeEnum.AdditiveBlend:
                        PRT_AdditiveBlend_Draw.Add(particle);
                        break;
                    case PRTDrawModeEnum.NonPremultiplied:
                        PRT_NonPremultiplied_Draw.Add(particle);
                        break;
                }
            }
        }

        public static void DrawHook(Terraria.On_Main.orig_DrawInfernoRings orig, Main self) {
            Draw(Main.spriteBatch);
            orig(self);
        }

        public static void Draw(SpriteBatch spriteBatch) {
            if (PRT_InGame_World_Inds.Count <= 0) {
                return;
            }

            spriteBatch.End();
            var rasterizer = Main.Rasterizer;
            rasterizer.ScissorTestEnable = true;
            Main.instance.GraphicsDevice.RasterizerState.ScissorTestEnable = true;
            Main.instance.GraphicsDevice.ScissorRectangle = new Rectangle(0, 0, Main.screenWidth, Main.screenHeight);

            AddDrawHander();

            void defaultDraw(BasePRT particle) {
                Rectangle frame = PRT_IDToTexture[particle.ID].Frame(1, particle.Frame, 0, particle.Variant);
                spriteBatch.Draw(PRT_IDToTexture[particle.ID], particle.Position - Main.screenPosition, frame, particle.Color
                    , particle.Rotation, frame.Size() * 0.5f, particle.Scale, SpriteEffects.None, 0f);
            }

            if (PRT_AlphaBlend_Draw.Count > 0) {
                spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState
                    , DepthStencilState.None, rasterizer, null, Main.GameViewMatrix.TransformationMatrix);

                foreach (BasePRT particle in PRT_AlphaBlend_Draw) {
                    if (particle.PreDraw(spriteBatch)) {
                        defaultDraw(particle);
                    }
                    particle.PostDraw(spriteBatch);
                }
                spriteBatch.End();
            }

            if (PRT_AdditiveBlend_Draw.Count > 0) {
                rasterizer = Main.Rasterizer;
                rasterizer.ScissorTestEnable = true;
                Main.instance.GraphicsDevice.RasterizerState.ScissorTestEnable = true;
                Main.instance.GraphicsDevice.ScissorRectangle = new Rectangle(0, 0, Main.screenWidth, Main.screenHeight);
                spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Additive, SamplerState.PointClamp
                    , DepthStencilState.Default, rasterizer, null, Main.GameViewMatrix.TransformationMatrix);

                foreach (BasePRT particle in PRT_AdditiveBlend_Draw) {
                    if (particle.PreDraw(spriteBatch)) {
                        defaultDraw(particle);
                    }
                    particle.PostDraw(spriteBatch);
                }
                spriteBatch.End();
            }

            if (PRT_NonPremultiplied_Draw.Count > 0) {
                rasterizer = Main.Rasterizer;
                rasterizer.ScissorTestEnable = true;
                Main.instance.GraphicsDevice.RasterizerState.ScissorTestEnable = true;
                Main.instance.GraphicsDevice.ScissorRectangle = new Rectangle(0, 0, Main.screenWidth, Main.screenHeight);
                spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.PointClamp
                    , DepthStencilState.Default, rasterizer, null, Main.GameViewMatrix.TransformationMatrix);

                foreach (BasePRT particle in PRT_NonPremultiplied_Draw) {
                    if (particle.PreDraw(spriteBatch)) {
                        defaultDraw(particle);
                    }
                    particle.PostDraw(spriteBatch);
                }
                spriteBatch.End();
            }

            PRT_AlphaBlend_Draw.Clear();
            PRT_NonPremultiplied_Draw.Clear();
            PRT_AdditiveBlend_Draw.Clear();
            foreach (var particle in PRTInstances) {
                PRT_IDToInGame_World_Count[particle.ID] = 0;
            }

            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState, DepthStencilState.None, Main.Rasterizer, null, Main.Transform);
        }

        /// <summary>
        /// 给出可用粒子槽的数量。当一次需要多个粒子来制作效果，并且不希望由于缺乏粒子槽而只绘制一半时非常有用
        /// </summary>
        /// <returns></returns>
        public static int NumberUsablePRT() {
            return Main.dedServ || PRT_InGame_World_Inds == null ? 0 : InGame_World_MaxPRTCount - PRT_InGame_World_Inds.Count();
        }
    }
}
