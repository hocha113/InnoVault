using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections;
using System.Collections.Generic;
using Terraria;

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
        /// <inheritdoc/>
        IEnumerator IEnumerable.GetEnumerator() => _particles.GetEnumerator();
        /// <summary>
        /// 创建一个新的粒子实例，并初始化其属性
        /// </summary>
        /// <typeparam name="T">粒子的具体类型，必须继承自 <see cref="BasePRT"/></typeparam>
        /// <param name="center">粒子的初始位置（中心坐标）</param>
        /// <param name="velocity">粒子的初始速度</param>
        /// <param name="newColor">粒子的颜色，如果未指定则使用默认颜色</param>
        /// <param name="Scale">粒子的缩放比例，默认值为 1f</param>
        /// <returns>返回新创建的粒子实例，如果在服务器环境下运行，返回<see langword="null"/></returns>
        public T NewParticle<T>(Vector2 center, Vector2 velocity, Color newColor = default, float Scale = 1f) where T : BasePRT {
            if (Main.dedServ) {
                return null;
            }
            T p = PRTLoader.GetPRTInstance<T>();
            p.active = true;
            p.ID = PRTLoader.GetParticleID(p.GetType());
            p.Color = newColor;
            p.Position = center;
            p.Velocity = velocity;
            p.Scale = Scale;
            p.SetProperty();
            _particles.Add(p);
            return p;
        }
        /// <summary>
        /// 创建一个新的粒子实例，并初始化其属性
        /// </summary>
        /// <param name="type">粒子的id</param>
        /// <param name="center">粒子的初始位置（中心坐标）</param>
        /// <param name="velocity">粒子的初始速度</param>
        /// <param name="newColor">粒子的颜色，如果未指定则使用默认颜色</param>
        /// <param name="Scale">粒子的缩放比例，默认值为 1f</param>
        /// <returns>返回新创建的粒子实例，如果在服务器环境下运行，返回<see langword="null"/></returns>
        public BasePRT NewParticle(Vector2 center, Vector2 velocity, int type, Color newColor = default, float Scale = 1f) {
            if (Main.dedServ) {
                return null;
            }
            BasePRT p = PRTLoader.GetPRTInstance(type);
            p.active = true;
            p.ID = PRTLoader.GetParticleID(p.GetType());
            p.Color = newColor;
            p.Position = center;
            p.Velocity = velocity;
            p.Scale = Scale;
            p.SetProperty();
            _particles.Add(p);
            return p;
        }
        /// <summary>
        /// 清空本地粒子实例集合
        /// </summary>
        public void Clear() => _particles.Clear();
        /// <summary>
        /// 向粒子集合中添加新的粒子实例
        /// </summary>
        /// <param name="particle"></param>
        public void Add(BasePRT particle) {
            particle.active = true;
            particle.ID = PRTLoader.GetParticleID(particle.GetType());
            _particles.Add(particle);
        }
        /// <summary>
        /// 返回指定id的所有粒子存在
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
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
        /// <summary>
        /// 是否存在粒子
        /// </summary>
        /// <returns></returns>
        public bool Any() => _particles.Count > 0;
        /// <summary>
        /// 更新所有本地粒子集合
        /// </summary>
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

        /// <summary>
        /// 一般不手动调用该方法，而是使用<see cref="Draw(SpriteBatch)"/>与<see cref="DrawInUI(SpriteBatch)"/>
        /// </summary>
        /// <param name="spriteBatch"></param>
        /// <param name="draw"></param>
        /// <param name="matrix"></param>
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
                for (int i = 0; i < drawPRTs.Count; i++) {
                    draw.Invoke(spriteBatch, drawPRTs[i]);
                }
            }
        }
        /// <inheritdoc/>
        private void doDrawInUI(SpriteBatch spriteBatch, BasePRT basePRT) => basePRT.DrawInUI(spriteBatch);
        /// <summary>
        /// 绘制所有本地集合中的粒子实例，在世界中
        /// </summary>
        /// <param name="spriteBatch"></param>
        public virtual void Draw(SpriteBatch spriteBatch) => DoDraw(spriteBatch, PRTLoader.PRTInstanceDraw, Main.GameViewMatrix.TransformationMatrix);
        /// <summary>
        /// 绘制所有本地集合中的粒子实例，在UI中
        /// </summary>
        /// <param name="spriteBatch"></param>
        public virtual void DrawInUI(SpriteBatch spriteBatch) => DoDraw(spriteBatch, doDrawInUI, Main.UIScaleMatrix);
    }
}
