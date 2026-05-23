using Microsoft.Xna.Framework;

#pragma warning disable CS1591

namespace InnoVault.Models3D.Gltf
{
    /// <summary>
    /// glTF 静态模型导入选项
    /// </summary>
    public sealed class GltfImportOptions
    {
        public static GltfImportOptions Default => new GltfImportOptions();

        public Vector3 ImportScale { get; set; } = Vector3.One;
        public bool ApplyNodeTransforms { get; set; } = true;
        public bool GenerateMissingNormals { get; set; } = true;
        public bool FlipTextureV { get; set; } = false;

        public Vector3 ApplyImportScale(Vector3 value) {
            return value * ImportScale;
        }
    }
}
#pragma warning restore CS1591
