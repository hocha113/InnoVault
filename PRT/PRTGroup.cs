using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.Graphics.Shaders;

namespace InnoVault.PRT
{
    /// <summary>
    /// 粒子本地集合
    /// </summary>
    public class PRTGroup : IEnumerable<BasePRT>
    {
        /// <summary>
        /// 一个用于粒子绘制的委托类型
        /// </summary>
        /// <param name="spriteBatch"></param>
        /// <param name="basePRT"></param>
        public delegate void drawDelegate(SpriteBatch spriteBatch, BasePRT basePRT);
        /// <summary>
        /// 本地粒子实例集合
        /// </summary>
        protected List<BasePRT> _particles = [];
        /// <inheritdoc/>
        public BasePRT this[int i] => _particles[i];
        /// <inheritdoc/>
        public IEnumerator<BasePRT> GetEnumerator() => _particles.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => _particles.GetEnumerator();
        /// <inheritdoc/>
        public T NewParticle<T>(Vector2 center, Vector2 velocity, Color newColor = default, float Scale = 1f) where T : BasePRT {
            if (Main.dedServ) {
                return null;
            }
            T p = PRTLoader.GetPRTInstance<T>();
            p.active = true;
            p.Color = newColor;
            p.Position = center;
            p.Velocity = velocity;
            p.Scale = Scale;
            p.SetProperty();
            _particles.Add(p);
            return p;
        }
        /// <inheritdoc/>
        public BasePRT NewParticle(Vector2 center, Vector2 velocity, int type, Color newColor = default, float Scale = 1f) {
            if (Main.dedServ) {
                return null;
            }
            BasePRT p = PRTLoader.GetPRTInstance(type);
            p.active = true;
            p.Color = newColor;
            p.Position = center;
            p.Velocity = velocity;
            p.Scale = Scale;
            p.SetProperty();
            _particles.Add(p);
            return p;
        }
        /// <inheritdoc/>
        public void Clear() => _particles.Clear();
        /// <inheritdoc/>
        public void Add(BasePRT particle) => _particles.Add(particle);
        /// <inheritdoc/>
        public bool Any(int id) {
            int num = 0;
            foreach (BasePRT particle in _particles) {
                if (particle.ID != id) {
                    continue;
                }
                num++;
            }
            return num > 0;
        }
        /// <inheritdoc/>
        public bool Any() => _particles.Count > 0;
        /// <inheritdoc/>
        public virtual void Update() {
            if (Main.dedServ)//不要在服务器上更新逻辑
            {
                return;
            }

            foreach (var particle in _particles) {
                if (particle == null || !particle.active) {
                    continue;
                }

                try {
                    if (particle.ShouldUpdatePosition()) {
                        particle.Position += particle.Velocity;
                    }
                    particle.Time++;
                    particle.AI();
                } catch (Exception) {
                    VaultMod.Instance.Logger.Info($"ERROR:In Group {GetType().Name}{particle} IS UPDATA");
                    particle.active = false;
                    continue;
                }

                if (particle.Time >= particle.Lifetime && particle.SetLifetime) {
                    particle.active = false;
                }
            }

            _particles.RemoveAll(p => p is null || !p.active);
        }

        /// <summary>
        /// 根据指定的绘制模式 <see cref="PRTDrawModeEnum"/>，为 <see cref="SpriteBatch"/> 设置适当的渲染状态并开始绘制
        /// </summary>
        /// <param name="drawMode">绘制模式枚举 <see cref="PRTDrawModeEnum"/></param>
        /// <param name="spriteBatch">用于进行绘制操作的 <see cref="SpriteBatch"/></param>
        /// <param name="matrix">画布模式</param>
        public static void BeginDrawingWithMode(PRTDrawModeEnum drawMode, SpriteBatch spriteBatch, Matrix matrix) {
            var rasterizer = Main.Rasterizer;
            rasterizer.ScissorTestEnable = true;
            Main.instance.GraphicsDevice.RasterizerState.ScissorTestEnable = true;
            Main.instance.GraphicsDevice.ScissorRectangle = new Rectangle(0, 0, Main.screenWidth, Main.screenHeight);

            switch (drawMode) {
                case PRTDrawModeEnum.AlphaBlend:
                    spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState
                    , DepthStencilState.None, rasterizer, null, matrix);
                    break;
                case PRTDrawModeEnum.AdditiveBlend:
                    spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Additive, SamplerState.PointClamp
                    , DepthStencilState.Default, rasterizer, null, matrix);
                    break;
                case PRTDrawModeEnum.NonPremultiplied:
                    spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.PointClamp
                    , DepthStencilState.Default, rasterizer, null, matrix);
                    break;
            }
        }

        /// <summary>
        /// 用于绘制使用Shader效果的粒子集合
        /// </summary>
        /// <param name="spriteBatch">画布实例</param>
        /// <param name="particles">传入的粒子集合，其中所有的粒子要求<see cref="BasePRT.shader"/>不为<see langword="null"/></param>
        /// <param name="draw">绘制模式</param>
        /// <param name="matrix">画布模式</param>
        public static void HanderHasShaderPRTDrawList(SpriteBatch spriteBatch, List<BasePRT> particles, drawDelegate draw, Matrix matrix) {
            IEnumerable<IGrouping<ArmorShaderData, BasePRT>> groupedParticles = particles.GroupBy(p => p.shader);
            foreach (IGrouping<ArmorShaderData, BasePRT> group in groupedParticles) {
                spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.PointClamp
                    , DepthStencilState.None, RasterizerState.CullNone, null, matrix);
                group.Key?.Apply(null);
                foreach (BasePRT particle in group) {
                    draw.Invoke(spriteBatch, particle);
                }
                spriteBatch.End();
            }
        }
        /// <inheritdoc/>
        public void DoDraw(SpriteBatch spriteBatch, drawDelegate draw, Matrix matrix) {
            List<BasePRT> hasShaders = [];
            List<BasePRT> noShaders = [];
            Vector2 screenPosition = Main.screenPosition;
            if (matrix == Main.UIScaleMatrix) {
                screenPosition = Vector2.Zero;
            }
            foreach (BasePRT particle in _particles) {
                if (particle == null || !particle.active) {
                    continue;
                }

                if (particle.ShouldKillWhenOffScreen && !VaultUtils.IsPointOnScreen(particle.Position - screenPosition)) {
                    continue;
                }

                if (particle.shader != null) {
                    hasShaders.Add(particle);
                }
                else {
                    noShaders.Add(particle);
                }
            }

            if (noShaders.Count > 0) {
                Dictionary<PRTDrawModeEnum, List<BasePRT>> prtGroups = new Dictionary<PRTDrawModeEnum, List<BasePRT>>();
                foreach (PRTDrawModeEnum mode in PRTLoader.allDrawModes) {
                    prtGroups[mode] = new List<BasePRT>();
                }

                foreach (BasePRT particle in noShaders) {
                    if (prtGroups.ContainsKey(particle.PRTDrawMode)) {
                        prtGroups[particle.PRTDrawMode].Add(particle);
                    }
                }

                foreach (KeyValuePair<PRTDrawModeEnum, List<BasePRT>> group in prtGroups) {
                    if (group.Value.Count <= 0) {
                        continue;
                    }

                    BeginDrawingWithMode(group.Key, spriteBatch, matrix);
                    foreach (BasePRT particle in group.Value) {
                        draw.Invoke(spriteBatch, particle);
                    }
                    spriteBatch.End();
                }
            }

            if (hasShaders.Count > 0) {
                if (noShaders.Count <= 0) {
                    spriteBatch.End();
                }
                HanderHasShaderPRTDrawList(spriteBatch, hasShaders, draw, matrix);
            }
        }

        private void doDrawInUI(SpriteBatch spriteBatch, BasePRT basePRT) => basePRT.DrawInUI(spriteBatch);
        /// <inheritdoc/>
        public virtual void Draw(SpriteBatch spriteBatch) => DoDraw(spriteBatch, PRTLoader.PRTInstanceDraw, Main.GameViewMatrix.TransformationMatrix);
        /// <inheritdoc/>
        public virtual void DrawInUI(SpriteBatch spriteBatch) => DoDraw(spriteBatch, doDrawInUI, Main.UIScaleMatrix);
    }
}
