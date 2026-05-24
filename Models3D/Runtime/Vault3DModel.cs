using InnoVault.Models3D.Animation;
using InnoVault.Models3D.Skinning;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace InnoVault.Models3D.Runtime
{
    /// <summary>
    /// 静态 3D 模型资源
    /// <br/>由 OBJ、glTF 等导入器产出，运行时渲染器只依赖此通用结构
    /// <br/>包含可选的骨架与动画 Clip，蒙皮动画通过 <see cref="Model3DInstance.Animation"/> 在实例级驱动
    /// </summary>
    public class Vault3DModel
    {
        /// <summary>
        /// 空模型占位
        /// <br/>资源加载失败或服务器环境下使用，调用方可通过 <see cref="IsValid"/> 判断是否可绘制
        /// </summary>
        public static Vault3DModel Empty { get; } = new Vault3DModel("(empty)", string.Empty);

        /// <summary>
        /// 模型名称
        /// <br/>通常来自源文件名，用于日志和调试显示
        /// </summary>
        public string Name { get; }
        /// <summary>
        /// 模型来源路径
        /// <br/>保持模组内相对路径，便于定位导入诊断
        /// </summary>
        public string SourcePath { get; }
        /// <summary>
        /// 按材质拆分的网格分组
        /// <br/>渲染器会逐组绘制，同一模型可能产生多次 draw call
        /// </summary>
        public IReadOnlyList<Model3DMeshGroup> Groups => _groups;
        /// <summary>
        /// 模型材质表
        /// <br/>key 为导入时保留的材质名，分组上的材质引用来自此表
        /// </summary>
        public IReadOnlyDictionary<string, Model3DMaterial> Materials => _materials;
        /// <summary>
        /// 模型骨架表
        /// <br/>对应 glTF <c>skins[]</c>；Scroll 等模型会有多个独立骨架
        /// <br/>非蒙皮模型为空列表
        /// </summary>
        public IReadOnlyList<Model3DSkeleton> Skeletons => _skeletons;
        /// <summary>
        /// 动画 Clip 表
        /// <br/>对应 glTF <c>animations[]</c>；按定义顺序排列
        /// </summary>
        public IReadOnlyList<Model3DAnimationClip> Clips => _clips;
        /// <summary>
        /// 导入诊断信息
        /// <br/>包含缺失资源、不支持特性、格式异常等导入阶段消息
        /// </summary>
        public Model3DDiagnostic Diagnostic { get; }
        /// <summary>
        /// 导入空间下的包围盒
        /// <br/>已包含导入器应用的轴向、缩放和节点变换
        /// </summary>
        public BoundingBox Bounds { get; internal set; }
        /// <summary>
        /// 绘制时使用的旋转中心
        /// <br/>渲染器会先平移到此点再应用实例缩放与旋转
        /// </summary>
        public Vector3 Pivot { get; internal set; }
        /// <summary>
        /// 模型根变换
        /// <br/>由 <see cref="Model3DRenderer.BuildWorldMatrix"/> 作为最内层左乘进 World，
        /// 用于在不烘焙到顶点的前提下统一应用坐标轴翻转 / 全局缩放
        /// <br/>非蒙皮模型默认是 <see cref="Matrix.Identity"/>（与旧行为完全一致）；
        /// 蒙皮模型由导入器写入"AxisFlip * ImportScale"，避免破坏 skinMatrix 计算空间
        /// </summary>
        public Matrix RootTransform { get; internal set; } = Matrix.Identity;
        /// <summary>
        /// 顶点总数
        /// <br/>用于快速查看模型规模，不参与绘制逻辑
        /// </summary>
        public int VertexCount { get; internal set; }
        /// <summary>
        /// 三角形总数
        /// <br/>用于快速查看模型规模，不参与绘制逻辑
        /// </summary>
        public int TriangleCount { get; internal set; }
        /// <summary>
        /// 是否包含可绘制三角形
        /// <br/>提交渲染前建议先检查此值
        /// </summary>
        public bool IsValid => TriangleCount > 0;
        /// <summary>
        /// 是否包含至少一个骨架
        /// <br/>外部判断该模型能否被 <see cref="AnimationPlayer"/> 驱动
        /// </summary>
        public bool IsSkinned => _skeletons.Count > 0;

        private readonly List<Model3DMeshGroup> _groups;
        private readonly Dictionary<string, Model3DMaterial> _materials;
        private readonly List<Model3DSkeleton> _skeletons;
        private readonly List<Model3DAnimationClip> _clips;
        private Dictionary<string, Model3DAnimationClip> _clipsByName;

        internal Vault3DModel(string name, string sourcePath) {
            Name = name ?? string.Empty;
            SourcePath = sourcePath ?? string.Empty;
            _groups = new List<Model3DMeshGroup>();
            _materials = new Dictionary<string, Model3DMaterial>();
            _skeletons = new List<Model3DSkeleton>();
            _clips = new List<Model3DAnimationClip>();
            Diagnostic = new Model3DDiagnostic();
            Bounds = new BoundingBox(Vector3.Zero, Vector3.Zero);
            Pivot = Vector3.Zero;
        }

        internal Vault3DModel(string name, string sourcePath, List<Model3DMeshGroup> groups
            , Dictionary<string, Model3DMaterial> materials, Model3DDiagnostic diagnostic
            , List<Model3DSkeleton> skeletons = null, List<Model3DAnimationClip> clips = null) {
            Name = name ?? string.Empty;
            SourcePath = sourcePath ?? string.Empty;
            _groups = groups ?? new List<Model3DMeshGroup>();
            _materials = materials ?? new Dictionary<string, Model3DMaterial>();
            _skeletons = skeletons ?? new List<Model3DSkeleton>();
            _clips = clips ?? new List<Model3DAnimationClip>();
            Diagnostic = diagnostic ?? new Model3DDiagnostic();
            Pivot = Vector3.Zero;
        }

        /// <summary>
        /// 按名称查找动画 Clip
        /// </summary>
        /// <param name="name">Clip 名（大小写敏感）</param>
        /// <param name="clip">命中时输出</param>
        /// <returns>是否命中</returns>
        public bool TryGetClip(string name, out Model3DAnimationClip clip) {
            if (string.IsNullOrEmpty(name) || _clips.Count == 0) {
                clip = null;
                return false;
            }
            _clipsByName ??= BuildClipLookup(_clips);
            return _clipsByName.TryGetValue(name, out clip);
        }

        private static Dictionary<string, Model3DAnimationClip> BuildClipLookup(List<Model3DAnimationClip> clips) {
            Dictionary<string, Model3DAnimationClip> map = new Dictionary<string, Model3DAnimationClip>(StringComparer.Ordinal);
            for (int i = 0; i < clips.Count; i++) {
                Model3DAnimationClip clip = clips[i];
                if (clip == null) {
                    continue;
                }
                string key = clip.Name;
                if (string.IsNullOrEmpty(key)) {
                    key = $"clip_{i}";
                }
                if (!map.ContainsKey(key)) {
                    map[key] = clip;
                }
            }
            return map;
        }
    }
}
