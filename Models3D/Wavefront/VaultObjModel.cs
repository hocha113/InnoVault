using InnoVault.Models3D.Runtime;
using Microsoft.Xna.Framework;
using System.Collections.Generic;

namespace InnoVault.Models3D.Wavefront
{
    /// <summary>
    /// 表示一个已经解析完成的 OBJ 静态网格模型
    /// <br/>包含网格分组、材质表、统计信息以及加载诊断
    /// <br/>资源由 <see cref="ObjModelLoadenHandle"/> 在 <see cref="VaultLoadenAttribute"/> 加载流程中产出
    /// </summary>
    public sealed class VaultObjModel : Vault3DModel
    {
        /// <summary>
        /// 一个空模型实例，用于资源加载失败时的占位
        /// </summary>
        public static new VaultObjModel Empty { get; } = new VaultObjModel("(empty)", string.Empty);

        /// <summary>
        /// 模型名（一般为 OBJ 路径或资源句柄名）
        /// </summary>
        /// <summary>
        /// 按材质聚合的网格分组列表
        /// </summary>
        public new IReadOnlyList<ObjMeshGroup> Groups => _groups;

        /// <summary>
        /// 模型用到的材质表（key 为材质名）
        /// </summary>
        public new IReadOnlyDictionary<string, ObjMaterial> Materials => _materials;

        /// <summary>
        /// 加载阶段产生的诊断信息（可用于调试或日志输出）
        /// </summary>
        public new ObjDiagnostic Diagnostic { get; }

        private readonly List<ObjMeshGroup> _groups;
        private readonly Dictionary<string, ObjMaterial> _materials;

        /// <summary>
        /// 构造一个全空的占位模型，仅供 <see cref="Empty"/> 与失败回退使用
        /// </summary>
        internal VaultObjModel(string name, string sourcePath) : base(name, sourcePath) {
            _groups = new List<ObjMeshGroup>();
            _materials = new Dictionary<string, ObjMaterial>();
            Diagnostic = new ObjDiagnostic();
        }

        /// <summary>
        /// 构造一个完整模型
        /// </summary>
        internal VaultObjModel(string name, string sourcePath, List<ObjMeshGroup> groups
            , Dictionary<string, ObjMaterial> materials, ObjDiagnostic diagnostic)
            : base(name, sourcePath, ToModelGroups(groups), ToModelMaterials(materials), diagnostic) {
            _groups = groups ?? new List<ObjMeshGroup>();
            _materials = materials ?? new Dictionary<string, ObjMaterial>();
            Diagnostic = diagnostic ?? new ObjDiagnostic();
        }

        private static List<Model3DMeshGroup> ToModelGroups(List<ObjMeshGroup> groups) {
            if (groups == null || groups.Count == 0) {
                return new List<Model3DMeshGroup>();
            }
            List<Model3DMeshGroup> result = new List<Model3DMeshGroup>(groups.Count);
            for (int i = 0; i < groups.Count; i++) {
                result.Add(groups[i]);
            }
            return result;
        }

        private static Dictionary<string, Model3DMaterial> ToModelMaterials(Dictionary<string, ObjMaterial> materials) {
            if (materials == null || materials.Count == 0) {
                return new Dictionary<string, Model3DMaterial>();
            }
            Dictionary<string, Model3DMaterial> result = new Dictionary<string, Model3DMaterial>(materials.Count);
            foreach (KeyValuePair<string, ObjMaterial> kv in materials) {
                result[kv.Key] = kv.Value;
            }
            return result;
        }
    }
}
