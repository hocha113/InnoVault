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

            for (int i = 0; i < _particles.Count; i++) {
                BasePRT particle = _particles[i];

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

        /// <inheritdoc/>
        public void DoDraw(SpriteBatch spriteBatch, drawDelegate draw, Matrix matrix) {
            List<BasePRT> drawPRTs = [];
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

                drawPRTs.Add(particle);
            }

            if (drawPRTs.Count > 0) {
                foreach (BasePRT particle in drawPRTs) {
                    draw.Invoke(spriteBatch, particle);
                }
            }
        }

        private void doDrawInUI(SpriteBatch spriteBatch, BasePRT basePRT) => basePRT.DrawInUI(spriteBatch);
        /// <inheritdoc/>
        public virtual void Draw(SpriteBatch spriteBatch) => DoDraw(spriteBatch, PRTLoader.PRTInstanceDraw, Main.GameViewMatrix.TransformationMatrix);
        /// <inheritdoc/>
        public virtual void DrawInUI(SpriteBatch spriteBatch) => DoDraw(spriteBatch, doDrawInUI, Main.UIScaleMatrix);
    }
}
