using Microsoft.Xna.Framework;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace InnoVault.Rigs2D.Data
{
    /// <summary>
    /// 代码优先的骨架定义构建器
    /// <br/>链式声明骨骼、贴图件与求解器，<see cref="Build"/> 产出已 <see cref="Rig2DDefinition.Resolve"/> 的定义；
    /// 需要落盘时用 <see cref="Rig2DJson.ToJsonText"/> 导出，之后就能改 JSON 热重载调参
    /// </summary>
    /// <example>
    /// <code>
    /// Rig2DDefinition def = new Rig2DBuilder("Crab")
    ///     .Bone("body")
    ///     .Bone("shoulderR", "body", offset: new Vector2(30f, 12f))
    ///     .Bone("upperR", "shoulderR", length: 60f)
    ///     .Bone("foreR", "upperR", length: 48f, atTip: true)
    ///     .Piece("upperR", "Assets/Crab/Upper", proximal: new Vector2(8f, 6f), axis: 0f, layer: 10)
    ///     .Solver("TwoBoneIK", "armR", ["upperR", "foreR"]).Param("spring", 0.2f).Param("damping", 0.7f)
    ///     .Build();
    /// </code>
    /// </example>
    public sealed class Rig2DBuilder
    {
        private readonly Rig2DDefinition def = new();
        private Solver2DDef currentSolver;
        private Piece2DDef currentPiece;
        private Ribbon2DDef currentRibbon;
        private Channel2DDef currentChannel;
        private Pose2DDef currentPose;

        /// <summary>
        /// 新建构建器
        /// </summary>
        /// <param name="name">骨架名</param>
        public Rig2DBuilder(string name) {
            def.Name = name ?? string.Empty;
        }

        /// <summary>
        /// 设置瞬移硬重建阈值
        /// </summary>
        public Rig2DBuilder SnapDistance(float distance) {
            def.SnapDistance = distance;
            return this;
        }

        /// <summary>
        /// 声明一根骨骼
        /// </summary>
        /// <param name="name">骨骼名</param>
        /// <param name="parent">父骨骼名，空为根</param>
        /// <param name="length">静息骨长</param>
        /// <param name="offset">相对父锚点的局部偏移</param>
        /// <param name="rotation">静息旋转（弧度）</param>
        /// <param name="atTip">偏移从父尖端起量</param>
        /// <param name="inheritRotation">是否继承父旋转</param>
        public Rig2DBuilder Bone(string name, string parent = null, float length = 0f, Vector2 offset = default
            , float rotation = 0f, bool atTip = false, bool inheritRotation = true) {
            def.Bones.Add(new Bone2DDef {
                Name = name,
                Parent = parent,
                Length = length,
                Offset = offset,
                Rotation = rotation,
                AtParentTip = atTip,
                InheritRotation = inheritRotation,
            });
            return this;
        }

        /// <summary>
        /// 连续声明一条链：每节都以前一节尖端为锚（<c>atTip</c>），名字为 <paramref name="prefix"/> + 序号（从 1 起）
        /// </summary>
        /// <param name="prefix">名字前缀</param>
        /// <param name="parent">第一节的父骨骼（空则第一节为根）</param>
        /// <param name="firstAtTip">第一节是否也挂在父骨骼尖端（否则挂在父近端）</param>
        /// <param name="lengths">各节长度</param>
        public Rig2DBuilder Chain(string prefix, string parent, bool firstAtTip, params float[] lengths) {
            string prev = parent;
            for (int i = 0; i < lengths.Length; i++) {
                string name = prefix + (i + 1);
                bool atTip = !string.IsNullOrEmpty(prev) && (i > 0 || firstAtTip);
                Bone(name, prev, lengths[i], atTip: atTip);
                prev = name;
            }
            return this;
        }

        /// <summary>
        /// 声明一件贴图（挂在骨骼近端）
        /// </summary>
        /// <param name="bone">所挂骨骼</param>
        /// <param name="texture">贴图路径（模组相对，无扩展名）</param>
        /// <param name="proximal">贴图内钉在骨骼近端的像素</param>
        /// <param name="axis">贴图内在骨轴角（弧度）</param>
        /// <param name="layer">层序</param>
        /// <param name="name">件名（缺省用骨骼名）</param>
        public Rig2DBuilder Piece(string bone, string texture, Vector2 proximal, float axis, int layer = 0, string name = null) {
            currentPiece = new Piece2DDef {
                Name = name,
                Bone = bone,
                Texture = texture,
                Proximal = proximal,
                Axis = axis,
                Layer = layer,
            };
            def.Pieces.Add(currentPiece);
            return this;
        }

        /// <summary>
        /// 用"远端像素"声明贴图件，轴角与轴长自动反推
        /// </summary>
        public Rig2DBuilder PieceByDistal(string bone, string texture, Vector2 proximal, Vector2 distal, int layer = 0, string name = null) {
            Piece(bone, texture, proximal, 0f, layer, name);
            currentPiece.SetDistal(distal);
            return this;
        }

        /// <summary>
        /// 设置最近一件贴图的拉伸方式
        /// </summary>
        public Rig2DBuilder Stretch(Piece2DStretch mode, float axisLength = 0f, float min = 0f, float max = 0f) {
            RequirePiece().Stretch = mode;
            if (axisLength > 0f) {
                currentPiece.AxisLength = axisLength;
            }
            currentPiece.StretchMin = min;
            currentPiece.StretchMax = max;
            return this;
        }

        /// <summary>
        /// 设置最近一件贴图的镜像 / 压暗 / 缩放 / 着色
        /// </summary>
        public Rig2DBuilder Look(bool mirror = false, float dark = 1f, Vector2? scale = null, Color? tint = null, float alpha = 1f) {
            Piece2DDef p = RequirePiece();
            p.Mirror = mirror;
            p.Dark = dark;
            p.Scale = scale ?? Vector2.One;
            p.Tint = tint ?? Color.White;
            p.Alpha = alpha;
            return this;
        }

        /// <summary>
        /// 把最近一件贴图设为不受光照（发光贴图）
        /// </summary>
        public Rig2DBuilder Unlit(bool unlit = true) {
            RequirePiece().Unlit = unlit;
            return this;
        }

        /// <summary>
        /// 设置最近一件贴图为多帧竖排图集
        /// </summary>
        public Rig2DBuilder Frames(int frames, int framePad = 0) {
            Piece2DDef p = RequirePiece();
            p.Frames = Math.Max(1, frames);
            p.FramePad = framePad;
            return this;
        }

        /// <summary>
        /// 把最近一件贴图设为关节盖件：钉在 <paramref name="bone2"/> 的近端，轴向取所挂骨与它的角平分线，按弯角放大
        /// </summary>
        public Rig2DBuilder JointCap(string bone2, float weight = 0.5f, float bendScale = 0f) {
            Piece2DDef p = RequirePiece();
            p.Bone2 = bone2;
            p.JointWeight = weight;
            p.BendScale = bendScale;
            return this;
        }

        /// <summary>
        /// 给最近一件贴图挂按角换帧（阈值升序、弧度；桶号即帧号，<paramref name="map"/> 可改映射）
        /// </summary>
        public Rig2DBuilder FrameBy(Piece2DFrameSource source, string boneOrChannel, float hysteresis, float[] thresholds, int[] map = null, string refBone = null) {
            Piece2DDef p = RequirePiece();
            float[] th = (float[])thresholds?.Clone() ?? [];
            Array.Sort(th);
            p.FrameBy = new Piece2DFrameBy {
                Source = source,
                Bone = source == Piece2DFrameSource.Channel ? null : boneOrChannel,
                Channel = source == Piece2DFrameSource.Channel ? boneOrChannel : null,
                Ref = refBone,
                Thresholds = th,
                Map = map,
                Hysteresis = hysteresis,
            };
            return this;
        }

        /// <summary>
        /// 把最近一件贴图的近端改成按帧尺寸比例给（贴图尺寸定义期未知时用：<c>(0.5, 0.5)</c> 帧中心、<c>(0.5, 1)</c> 底边中点）
        /// </summary>
        public Rig2DBuilder ProximalNormalized(Vector2 uv) {
            Piece2DDef p = RequirePiece();
            p.Proximal = uv;
            p.ProximalNormalized = true;
            return this;
        }

        /// <summary>
        /// 声明一条带状件（沿骨链铺的纹理条带），之后的 <see cref="RibbonStyle"/> / <see cref="RibbonLook"/> / <see cref="RibbonTaper"/> / <see cref="RibbonProfile"/> 都改它
        /// </summary>
        /// <param name="name">件名（缺省用首骨名）</param>
        /// <param name="texture">贴图路径（模组相对，无扩展名；u 沿链、v 横跨）</param>
        /// <param name="width">根端整宽（像素）</param>
        /// <param name="bones">骨链，根 → 尖</param>
        public Rig2DBuilder Ribbon(string name, string texture, float width, params string[] bones) {
            currentRibbon = new Ribbon2DDef {
                Name = name,
                Texture = texture,
                Width = width,
            };
            currentRibbon.Bones.AddRange(bones);
            def.Ribbons.Add(currentRibbon);
            return this;
        }

        /// <summary>
        /// 设置最近一条带状件的尖端宽度（线性收窄）
        /// </summary>
        public Rig2DBuilder RibbonTaper(float widthEnd) {
            RequireRibbon().WidthEnd = widthEnd;
            return this;
        }

        /// <summary>
        /// 设置最近一条带状件的宽度剖面（按沿链进度 0..1 线性采样）
        /// </summary>
        public Rig2DBuilder RibbonProfile(params float[] widths) {
            RequireRibbon().WidthProfile = widths ?? [];
            return this;
        }

        /// <summary>
        /// 设置最近一条带状件的纹理映射、细分与尖端延伸
        /// </summary>
        public Rig2DBuilder RibbonStyle(Ribbon2DUv uv, float tileLength = 64f, int smooth = 0, bool includeTip = true) {
            Ribbon2DDef r = RequireRibbon();
            r.Uv = uv;
            r.TileLength = tileLength;
            r.Smooth = Math.Max(0, smooth);
            r.IncludeTip = includeTip;
            return this;
        }

        /// <summary>
        /// 设置最近一条带状件的层序 / 着色 / 压暗 / 不透明度 / 加色
        /// </summary>
        public Rig2DBuilder RibbonLook(int layer = 0, Color? tint = null, float dark = 1f, float alpha = 1f, bool additive = false) {
            Ribbon2DDef r = RequireRibbon();
            r.Layer = layer;
            r.Tint = tint ?? Color.White;
            r.Dark = dark;
            r.Alpha = alpha;
            r.Additive = additive;
            return this;
        }

        /// <summary>
        /// 把最近一条带状件设为不受光照（能量带）
        /// </summary>
        public Rig2DBuilder RibbonUnlit(bool unlit = true) {
            RequireRibbon().Unlit = unlit;
            return this;
        }

        /// <summary>
        /// 肢体条带：逐骨锚定 u（<paramref name="uvStops"/> 为每个关节点的 u，空 = 均分），关节斜接保体积与按折角加宽
        /// </summary>
        public Rig2DBuilder RibbonLimb(float[] uvStops = null, bool miter = true, float jointBulge = 0f, float miterLimit = 2.5f) {
            Ribbon2DDef r = RequireRibbon();
            r.Uv = Ribbon2DUv.Bone;
            r.UvStops = uvStops ?? [];
            r.Miter = miter;
            r.JointBulge = jointBulge;
            r.MiterLimit = miterLimit;
            return this;
        }

        /// <summary>
        /// 最近一条带状件的左右偏置剖面（−1..1，正 = 法线正向一侧更厚；镜像时自动取反）
        /// </summary>
        public Rig2DBuilder RibbonBias(params float[] bias) {
            RequireRibbon().BiasProfile = bias ?? [];
            return this;
        }

        /// <summary>
        /// 最近一条带状件的首尾端帽（长度为 Scale 1 的像素，u 为占纹理的比例；帽段不随条带拉伸）
        /// </summary>
        public Rig2DBuilder RibbonCaps(float startLength, float startU, float endLength, float endU) {
            Ribbon2DDef r = RequireRibbon();
            r.CapStart = startLength;
            r.CapStartU = startU;
            r.CapEnd = endLength;
            r.CapEndU = endU;
            return this;
        }

        /// <summary>
        /// 声明一个求解器，之后的 <see cref="Param(string, float)"/> 系列都写进它
        /// </summary>
        /// <param name="type">类型名（注册表键）</param>
        /// <param name="name">实例名</param>
        /// <param name="bones">参与的骨骼名</param>
        public Rig2DBuilder Solver(string type, string name, params string[] bones) {
            currentSolver = new Solver2DDef {
                Type = type,
                Name = name,
                Params = new JObject(),
            };
            currentSolver.Bones.AddRange(bones);
            def.Solvers.Add(currentSolver);
            return this;
        }

        /// <summary>
        /// 写入最近一个求解器的浮点参数
        /// </summary>
        public Rig2DBuilder Param(string key, float value) {
            RequireSolver().Set(key, value);
            return this;
        }

        /// <summary>
        /// 写入最近一个求解器的整数参数
        /// </summary>
        public Rig2DBuilder Param(string key, int value) {
            RequireSolver().Set(key, value);
            return this;
        }

        /// <summary>
        /// 写入最近一个求解器的布尔参数
        /// </summary>
        public Rig2DBuilder Param(string key, bool value) {
            RequireSolver().Set(key, value);
            return this;
        }

        /// <summary>
        /// 写入最近一个求解器的字符串参数
        /// </summary>
        public Rig2DBuilder Param(string key, string value) {
            RequireSolver().Set(key, value);
            return this;
        }

        /// <summary>
        /// 写入最近一个求解器的向量参数
        /// </summary>
        public Rig2DBuilder Param(string key, Vector2 value) {
            RequireSolver().Set(key, new JArray(value.X, value.Y));
            return this;
        }

        /// <summary>
        /// 写入最近一个求解器的数组参数
        /// </summary>
        public Rig2DBuilder Param(string key, params float[] values) {
            RequireSolver().Set(key, new JArray(values));
            return this;
        }

        /// <summary>
        /// 写入最近一个求解器的字符串数组参数
        /// </summary>
        public Rig2DBuilder Param(string key, params string[] values) {
            RequireSolver().Set(key, new JArray(values));
            return this;
        }

        /// <summary>
        /// 加入一段关键帧片段
        /// </summary>
        public Rig2DBuilder Clip(Animation.Rig2DClip clip) {
            if (clip != null) {
                def.Clips.Add(clip);
            }
            return this;
        }

        /// <summary>
        /// 声明一个通道，之后的 <see cref="BindBone"/> / <see cref="BindSolver"/> / <see cref="BindPiece"/> / <see cref="BindRibbon"/> / <see cref="BindRoot"/> 都绑它
        /// </summary>
        /// <param name="name">通道名</param>
        /// <param name="type">值形状</param>
        /// <param name="blend">插值方式</param>
        /// <param name="defaultValue">缺省值（标量取 X）</param>
        public Rig2DBuilder Channel(string name, Channel2DType type = Channel2DType.Scalar, Channel2DBlend blend = Channel2DBlend.Linear,
            Vector2 defaultValue = default) {
            currentChannel = new Channel2DDef {
                Name = name,
                Type = type,
                Blend = blend,
                Default = defaultValue,
            };
            def.Channels.Add(currentChannel);
            return this;
        }

        /// <summary>
        /// 把最近一个通道绑到骨骼局部量（<c>rotation</c> / <c>offset</c> / <c>length</c>）
        /// </summary>
        public Rig2DBuilder BindBone(string bone, string prop = "rotation", Channel2DMode mode = Channel2DMode.Add, float scale = 1f) {
            RequireChannel().Bind = new Channel2DBind {
                Target = Channel2DTarget.Bone,
                Name = bone,
                Prop = prop,
                Mode = mode,
                Scale = scale,
            };
            return this;
        }

        /// <summary>
        /// 把最近一个通道绑到求解器属性；给了 <paramref name="space"/> 或属性本身是空间量（<c>target</c> 一类）时按空间换算成世界点
        /// </summary>
        public Rig2DBuilder BindSolver(string solver, string prop = "target", Channel2DSpace? space = null, string spaceBone = null,
            Vector2 offset = default, bool rotate = false, float scale = 1f) {
            RequireChannel().Bind = new Channel2DBind {
                Target = Channel2DTarget.Solver,
                Name = solver,
                Prop = prop,
                Space = space ?? Channel2DSpace.Anchor,
                HasSpace = space.HasValue,
                SpaceBone = spaceBone,
                Offset = offset,
                Rotate = rotate,
                Scale = scale,
            };
            return this;
        }

        /// <summary>
        /// 把最近一个通道绑到贴图件状态（<c>frame</c> / <c>visible</c> / <c>layer</c> / <c>rotation</c> / <c>alpha</c> / <c>scale</c>）
        /// </summary>
        public Rig2DBuilder BindPiece(string piece, string prop, float scale = 1f) {
            RequireChannel().Bind = new Channel2DBind {
                Target = Channel2DTarget.Piece,
                Name = piece,
                Prop = prop,
                Scale = scale,
            };
            return this;
        }

        /// <summary>
        /// 把最近一个通道绑到带状件状态（<c>width</c> / <c>alpha</c> / <c>visible</c> / <c>uvOffset</c> / <c>layer</c>）
        /// </summary>
        public Rig2DBuilder BindRibbon(string ribbon, string prop, float scale = 1f) {
            RequireChannel().Bind = new Channel2DBind {
                Target = Channel2DTarget.Ribbon,
                Name = ribbon,
                Prop = prop,
                Scale = scale,
            };
            return this;
        }

        /// <summary>
        /// 把最近一个通道绑到实例根（<c>position</c> 空间量 / <c>rotation</c>）
        /// </summary>
        public Rig2DBuilder BindRoot(string prop = "position", Channel2DSpace? space = null, Vector2 offset = default, float scale = 1f) {
            RequireChannel().Bind = new Channel2DBind {
                Target = Channel2DTarget.Root,
                Prop = prop,
                Space = space ?? Channel2DSpace.Anchor,
                HasSpace = space.HasValue,
                Offset = offset,
                Scale = scale,
            };
            return this;
        }

        /// <summary>
        /// 声明一个通道组
        /// </summary>
        public Rig2DBuilder ChannelGroup(string name, params string[] channels) {
            def.ChannelGroups[name] = [.. channels];
            return this;
        }

        /// <summary>
        /// 声明一张姿态，之后的 <see cref="Set(string, float)"/> / <see cref="Set(string, Vector2)"/> 都写进它
        /// </summary>
        public Rig2DBuilder Pose(string name, string inherit = null) {
            currentPose = new Pose2DDef {
                Name = name,
                Inherit = inherit,
            };
            def.Poses.Add(currentPose);
            return this;
        }

        /// <summary>
        /// 加入一个招式（段式定义，字段语义同 JSON 的 <c>moves</c>）
        /// </summary>
        public Rig2DBuilder Move(Move2DDef move) {
            if (move != null) {
                def.Moves.Add(move);
            }
            return this;
        }

        /// <summary>
        /// 往受击胶囊组 <paramref name="group"/> 里加一个胶囊（沿骨 [from, to] 段，半径为 Scale 1 的像素）
        /// </summary>
        public Rig2DBuilder Hitbox(string group, string bone, float radius, float from = 0f, float to = 1f) {
            if (!def.Hitboxes.TryGetValue(group, out List<Hitbox2DDef> list)) {
                list = [];
                def.Hitboxes[group] = list;
            }
            list.Add(new Hitbox2DDef { Bone = bone, Radius = radius, From = from, To = to });
            return this;
        }

        /// <summary>
        /// 加入一套运动层定义（字段语义同 JSON 的 <c>gaits</c>）
        /// </summary>
        public Rig2DBuilder Gait(Gait2DDef gait) {
            if (gait != null) {
                def.Gaits.Add(gait);
            }
            return this;
        }

        /// <summary>
        /// 给最近一张姿态写标量通道值
        /// </summary>
        public Rig2DBuilder Set(string channel, float value) {
            RequirePose().Set(channel, value);
            return this;
        }

        /// <summary>
        /// 给最近一张姿态写向量通道值
        /// </summary>
        public Rig2DBuilder Set(string channel, Vector2 value) {
            RequirePose().Set(channel, value);
            return this;
        }

        /// <summary>
        /// 解析并返回定义；解析失败时抛出 <see cref="InvalidOperationException"/>（代码构建的错误应当在开发期暴露）
        /// </summary>
        public Rig2DDefinition Build() {
            if (!def.Resolve(out string error)) {
                throw new InvalidOperationException($"Rig2D '{def.Name}' definition invalid: {error}");
            }
            return def;
        }

        /// <summary>
        /// 尝试解析，失败时记日志并返回 <see langword="null"/>
        /// </summary>
        public Rig2DDefinition TryBuild() {
            if (def.Resolve(out string error)) {
                return def;
            }
            Rig2DPlatform.LogError($"[Rig2DBuilder:{def.Name}]", $"definition invalid: {error}");
            return null;
        }

        private Solver2DDef RequireSolver()
            => currentSolver ?? throw new InvalidOperationException("Rig2DBuilder: call Solver(...) before Param(...)");

        private Piece2DDef RequirePiece()
            => currentPiece ?? throw new InvalidOperationException("Rig2DBuilder: call Piece(...) before piece modifiers");

        private Ribbon2DDef RequireRibbon()
            => currentRibbon ?? throw new InvalidOperationException("Rig2DBuilder: call Ribbon(...) before ribbon modifiers");

        private Channel2DDef RequireChannel()
            => currentChannel ?? throw new InvalidOperationException("Rig2DBuilder: call Channel(...) before Bind*(...)");

        private Pose2DDef RequirePose()
            => currentPose ?? throw new InvalidOperationException("Rig2DBuilder: call Pose(...) before Set(...)");
    }
}
