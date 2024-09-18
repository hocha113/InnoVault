﻿using Microsoft.Xna.Framework;
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
    /// <summary>
    /// 粒子或者说尘埃实体的基类，简称为PRT实体继承它用于实现一些高自定义化的特殊粒子效果
    /// PRTLoader 负责管理游戏中粒子的加载、初始化以及纹理管理，支持通过Mod扩展粒子系统
    /// </summary>
    /// <remarks>
    /// 该类提供了一种全局的粒子系统管理方式通过各种静态字典和列表，PRTLoader 能够有效管理不同类型的粒子，
    /// 包括它们的ID、纹理、所属Mod以及实例数量
    /// </remarks>
    public class PRTLoader : ModSystem, IVaultLoader
    {
        /// <summary>
        /// 游戏中每个世界最多允许存在的粒子数量用于限制游戏中的粒子实体数量，防止性能问题
        /// </summary>
        public const int InGame_World_MaxPRTCount = 10000;
        /// <summary>
        /// 一个字典，用于将粒子类型（Type）映射到粒子ID每个粒子类型都会有一个唯一的ID，方便在系统中进行管理
        /// </summary>
        public static Dictionary<Type, int> PRT_TypeToID;
        /// <summary>
        /// 一个字典，用于将粒子类型（Type）映射到其所属的Mod用于追踪哪些Mod添加了特定类型的粒子
        /// </summary>
        public static Dictionary<Type, Mod> PRT_TypeToMod;
        /// <summary>
        /// 一个字典，将粒子ID映射到其对应的纹理（Texture2D）每个粒子都有一个与其ID对应的纹理，用于渲染粒子的外观
        /// </summary>
        public static Dictionary<int, Texture2D> PRT_IDToTexture;
        /// <summary>
        /// 一个字典，将粒子ID映射到当前游戏世界中的实例数量用于记录每种粒子在当前世界中存在的数量，确保不超过最大限制
        /// </summary>
        public static Dictionary<int, int> PRT_IDToInGame_World_Count;
        /// <summary>
        /// 一个字典，将粒子ID映射到其对应的粒子实例（BasePRT）用于管理每个粒子的实例对象，以便进行粒子的更新和渲染
        /// </summary>
        public static Dictionary<int, BasePRT> PRT_IDToInstances;
        /// <summary>
        /// 一个列表，存储所有活跃的粒子实例（BasePRT）用于批量管理和更新粒子实体
        /// </summary>
        public static List<BasePRT> PRTInstances;

        private static List<BasePRT> PRT_InGame_World_Inds;
        private static List<BasePRT> PRT_InGame_ToKill_Inds;
        private static List<BasePRT> PRT_AlphaBlend_Draw;
        private static List<BasePRT> PRT_AdditiveBlend_Draw;
        private static List<BasePRT> PRT_NonPremultiplied_Draw;

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
            particle.SetProperty();

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

        /// <summary>
        /// 更新在所有实体之前，这个进行独立的PRT粒子数量计数，在更新进行加法计数，这样才能保证弹幕、玩家、等程序可以获取正确的粒子数量
        /// </summary>
        public override void PreUpdateEntities() {
            if (Main.dedServ) {//不要在服务器上更新逻辑
                return;
            }

            foreach (BasePRT particle in PRT_InGame_World_Inds) {
                if (particle == null) {
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

            foreach (BasePRT particle in PRT_InGame_World_Inds) {
                if (particle == null) {
                    continue;
                }
                UpdateParticleVelocity(particle);
                UpdateParticleTime(particle);
                particle.AI();
            }

            foreach (var particle in PRTInstances) {
                PRT_IDToInGame_World_Count[particle.ID] = 0;
            }

            ParticleGarbageCollection(ref PRT_InGame_World_Inds);
            PRT_InGame_World_Inds.RemoveAll(particle => particle.Time >= particle.Lifetime && particle.SetLifetime || PRT_InGame_ToKill_Inds.Contains(particle));
            PRT_InGame_ToKill_Inds.Clear();
        }

        private static void ParticleGarbageCollection(ref List<BasePRT> particles) {
            bool isGC(BasePRT p) => p.Time >= p.Lifetime && p.SetLifetime || PRT_InGame_ToKill_Inds.Contains(p);
            particles.RemoveAll(isGC);
        }
        private static void UpdateParticleVelocity(BasePRT particle) => particle.Position += particle.Velocity;
        private static void UpdateParticleTime(BasePRT particle) => particle.Time++;
        /// <summary>
        /// 移除对应的粒子实例
        /// </summary>
        /// <param name="particle"></param>
        public static void RemoveParticle(BasePRT particle) => PRT_InGame_ToKill_Inds.Add(particle);

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

        private static void DrawHook(Terraria.On_Main.orig_DrawInfernoRings orig, Main self) {
            Draw(Main.spriteBatch);
            orig(self);
        }

        private static void defaultDraw(SpriteBatch spriteBatch, BasePRT particle) {
            Rectangle frame = PRT_IDToTexture[particle.ID].Frame(1, particle.Frame, 0, particle.Variant);
            spriteBatch.Draw(PRT_IDToTexture[particle.ID], particle.Position - Main.screenPosition, frame, particle.Color
                , particle.Rotation, frame.Size() * 0.5f, particle.Scale, SpriteEffects.None, 0f);
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
            var rasterizer = Main.Rasterizer;
            rasterizer.ScissorTestEnable = true;
            Main.instance.GraphicsDevice.RasterizerState.ScissorTestEnable = true;
            Main.instance.GraphicsDevice.ScissorRectangle = new Rectangle(0, 0, Main.screenWidth, Main.screenHeight);

            AddDrawHander();

            if (PRT_AlphaBlend_Draw.Count > 0) {
                spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState
                    , DepthStencilState.None, rasterizer, null, Main.GameViewMatrix.TransformationMatrix);

                foreach (BasePRT particle in PRT_AlphaBlend_Draw) {
                    if (particle.PreDraw(spriteBatch)) {
                        defaultDraw(spriteBatch, particle);
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
                        defaultDraw(spriteBatch, particle);
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
                        defaultDraw(spriteBatch, particle);
                    }
                    particle.PostDraw(spriteBatch);
                }
                spriteBatch.End();
            }

            PRT_AlphaBlend_Draw.Clear();
            PRT_NonPremultiplied_Draw.Clear();
            PRT_AdditiveBlend_Draw.Clear();

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
