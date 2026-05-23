using Microsoft.Xna.Framework;
using System.Collections.Generic;

namespace InnoVault.Models3D.Wavefront
{
    /// <summary>
    /// 表示一个已经解析完成的 OBJ 静态网格模型
    /// <br/>包含网格分组、材质表、统计信息以及加载诊断
    /// <br/>资源由 <see cref="ObjModelLoadenHandle"/> 在 <see cref="VaultLoadenAttribute"/> 加载流程中产出
    /// </summary>
    public sealed class VaultObjModel
    {
        /// <summary>
        /// 一个空模型实例，用于资源加载失败时的占位
        /// </summary>
        public static VaultObjModel Empty { get; } = new VaultObjModel("(empty)", string.Empty);

        /// <summary>
        /// 模型名（一般为 OBJ 路径或资源句柄名）
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// 模型加载源的相对路径（不含模组名前缀）
        /// </summary>
        public string SourcePath { get; }

        /// <summary>
        /// 按材质聚合的网格分组列表
        /// </summary>
        public IReadOnlyList<ObjMeshGroup> Groups => _groups;

        /// <summary>
        /// 模型用到的材质表（key 为材质名）
        /// </summary>
        public IReadOnlyDictionary<string, ObjMaterial> Materials => _materials;

        /// <summary>
        /// 加载阶段产生的诊断信息（可用于调试或日志输出）
        /// </summary>
        public ObjDiagnostic Diagnostic { get; }

        /// <summary>
        /// 模型在导入空间下的轴向包围盒
        /// </summary>
        public BoundingBox Bounds { get; internal set; }

        /// <summary>
        /// 顶点总数
        /// </summary>
        public int VertexCount { get; internal set; }
        /// <summary>
        /// 三角形总数
        /// </summary>
        public int TriangleCount { get; internal set; }

        /// <summary>
        /// 是否是有效模型（至少存在一个分组且包含三角形）
        /// </summary>
        public bool IsValid => TriangleCount > 0;

        private readonly List<ObjMeshGroup> _groups;
        private readonly Dictionary<string, ObjMaterial> _materials;

        /// <summary>
        /// 构造一个全空的占位模型，仅供 <see cref="Empty"/> 与失败回退使用
        /// </summary>
        internal VaultObjModel(string name, string sourcePath) {
            Name = name ?? string.Empty;
            SourcePath = sourcePath ?? string.Empty;
            _groups = new List<ObjMeshGroup>();
            _materials = new Dictionary<string, ObjMaterial>();
            Diagnostic = new ObjDiagnostic();
            Bounds = new BoundingBox(Vector3.Zero, Vector3.Zero);
        }

        /// <summary>
        /// 构造一个完整模型
        /// </summary>
        internal VaultObjModel(string name, string sourcePath, List<ObjMeshGroup> groups
            , Dictionary<string, ObjMaterial> materials, ObjDiagnostic diagnostic) {
            Name = name ?? string.Empty;
            SourcePath = sourcePath ?? string.Empty;
            _groups = groups ?? new List<ObjMeshGroup>();
            _materials = materials ?? new Dictionary<string, ObjMaterial>();
            Diagnostic = diagnostic ?? new ObjDiagnostic();
        }
    }
}
