using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

#pragma warning disable CS1591

namespace InnoVault.Models3D.Runtime
{
    /// <summary>
    /// 格式无关的 3D 材质描述
    /// </summary>
    public class Model3DMaterial
    {
        public string Name { get; }
        public Color DiffuseColor { get; set; } = Color.White;
        public float Opacity { get; set; } = 1f;
        public string DiffuseTexturePath { get; set; }
        public Texture2D DiffuseTexture { get; set; }
        public bool HasTexture => DiffuseTexture != null;

        public Effect Effect { get; set; }
        public IModel3DEffectProvider EffectProvider { get; set; }
        public Model3DConfigureEffect ConfigureEffect { get; set; }
        public Model3DRenderState RenderStateOverride { get; set; }
        public Model3DDrawCallback PreDrawGroup { get; set; }
        public Model3DDrawCallback PostDrawGroup { get; set; }

        public Model3DMaterial(string name) {
            Name = name ?? string.Empty;
        }
    }
}
#pragma warning restore CS1591
