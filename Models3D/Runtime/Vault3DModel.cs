using Microsoft.Xna.Framework;
using System.Collections.Generic;

#pragma warning disable CS1591

namespace InnoVault.Models3D.Runtime
{
    /// <summary>
    /// 格式无关的静态 3D 模型资源
    /// </summary>
    public class Vault3DModel
    {
        public static Vault3DModel Empty { get; } = new Vault3DModel("(empty)", string.Empty);

        public string Name { get; }
        public string SourcePath { get; }
        public IReadOnlyList<Model3DMeshGroup> Groups => _groups;
        public IReadOnlyDictionary<string, Model3DMaterial> Materials => _materials;
        public Model3DDiagnostic Diagnostic { get; }
        public BoundingBox Bounds { get; internal set; }
        public int VertexCount { get; internal set; }
        public int TriangleCount { get; internal set; }
        public bool IsValid => TriangleCount > 0;

        private readonly List<Model3DMeshGroup> _groups;
        private readonly Dictionary<string, Model3DMaterial> _materials;

        internal Vault3DModel(string name, string sourcePath) {
            Name = name ?? string.Empty;
            SourcePath = sourcePath ?? string.Empty;
            _groups = new List<Model3DMeshGroup>();
            _materials = new Dictionary<string, Model3DMaterial>();
            Diagnostic = new Model3DDiagnostic();
            Bounds = new BoundingBox(Vector3.Zero, Vector3.Zero);
        }

        internal Vault3DModel(string name, string sourcePath, List<Model3DMeshGroup> groups
            , Dictionary<string, Model3DMaterial> materials, Model3DDiagnostic diagnostic) {
            Name = name ?? string.Empty;
            SourcePath = sourcePath ?? string.Empty;
            _groups = groups ?? new List<Model3DMeshGroup>();
            _materials = materials ?? new Dictionary<string, Model3DMaterial>();
            Diagnostic = diagnostic ?? new Model3DDiagnostic();
        }
    }
}
#pragma warning restore CS1591
