using Microsoft.Xna.Framework;
using System.Collections.Generic;

namespace InnoVault.Dimensions
{
    /// <summary>
    /// 维度环境特性,定义维度的特殊环境效果
    /// </summary>
    public class DimensionEnvironment
    {
        /// <summary>
        /// 环境色调,影响整个维度的颜色滤镜
        /// </summary>
        public Color ColorTint { get; set; } = Color.White;

        /// <summary>
        /// 雾效强度(0-1)
        /// </summary>
        public float FogDensity { get; set; } = 0f;

        /// <summary>
        /// 雾效颜色
        /// </summary>
        public Color FogColor { get; set; } = Color.Gray;

        /// <summary>
        /// 环境粒子类型ID列表
        /// </summary>
        public List<int> AmbientParticles { get; set; } = new List<int>();

        /// <summary>
        /// 粒子生成频率(每帧生成概率)
        /// </summary>
        public float ParticleSpawnRate { get; set; } = 0.01f;

        /// <summary>
        /// 是否显示星空背景
        /// </summary>
        public bool ShowStars { get; set; } = true;

        /// <summary>
        /// 是否显示太阳/月亮
        /// </summary>
        public bool ShowCelestialBodies { get; set; } = true;
    }
}
