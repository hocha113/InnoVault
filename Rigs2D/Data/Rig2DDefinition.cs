using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Text;

namespace InnoVault.Rigs2D.Data
{
    /// <summary>
    /// 一副 2D 骨架的完整静态定义：骨骼树、贴图件、求解器
    /// <br/>定义本身与任何贴图或游戏实体无关，可以来自 JSON（<see cref="Rig2DJson"/>）也可以来自代码（<see cref="Rig2DBuilder"/>）；
    /// <see cref="Resolve"/> 之后名字全部换成索引并算出父先子后的求值顺序，运行时不再做字符串查找
    /// </summary>
    public sealed class Rig2DDefinition
    {
        /// <summary>
        /// 骨架名（日志与调试显示）
        /// </summary>
        public string Name { get; set; } = string.Empty;
        /// <summary>
        /// 根位姿单帧位移超过此距离（乘以实例 Scale）视为瞬移，整副骨架硬重建防抽搐
        /// </summary>
        public float SnapDistance { get; set; } = 340f;
        /// <summary>
        /// 骨骼定义表；<see cref="Resolve"/> 后各项 <see cref="Bone2DDef.Index"/> 与此表下标一致
        /// </summary>
        public List<Bone2DDef> Bones { get; } = [];
        /// <summary>
        /// 贴图件定义表
        /// </summary>
        public List<Piece2DDef> Pieces { get; } = [];
        /// <summary>
        /// 求解器定义表；运行时按此顺序逐个 <c>Step</c>，被依赖的求解器必须排在前面
        /// </summary>
        public List<Solver2DDef> Solvers { get; } = [];
        /// <summary>
        /// 带状件定义表（可选）：沿骨链铺的纹理条带，与整图件共用层序键
        /// </summary>
        public List<Ribbon2DDef> Ribbons { get; } = [];
        /// <summary>
        /// 关键帧片段表（可选），运行时由 <c>Rig2DClipPlayer</c> 播放
        /// </summary>
        public List<Animation.Rig2DClip> Clips { get; } = [];
        /// <summary>
        /// 通道表（可选）：动画层与消费方写通道，绑定把值落到骨骼 / 求解器 / 件 / 带 / 根
        /// </summary>
        public List<Channel2DDef> Channels { get; } = [];
        /// <summary>
        /// 通道组（名 → 通道名列表）：遮罩混合、招式分节错时、动画层遮罩按组取通道
        /// </summary>
        public Dictionary<string, List<string>> ChannelGroups { get; } = new(StringComparer.Ordinal);
        /// <summary>
        /// 姿态库（可选）
        /// </summary>
        public List<Pose2DDef> Poses { get; } = [];
        /// <summary>
        /// 招式表（可选）：段式写法的动作，解析后由 <see cref="MoveValue(string)"/> 取 <see cref="Animation.Rig2DMove"/> 求值
        /// </summary>
        public List<Move2DDef> Moves { get; } = [];
        /// <summary>
        /// 运动层定义（可选）：由 <see cref="Animation.Rig2DGait"/> 按名取用
        /// </summary>
        public List<Gait2DDef> Gaits { get; } = [];
        /// <summary>
        /// 受击 / 碰撞胶囊组（可选）：组名 → 胶囊表
        /// </summary>
        public Dictionary<string, List<Hitbox2DDef>> Hitboxes { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// 按组名取胶囊表；缺失返回 <see langword="null"/>
        /// </summary>
        public List<Hitbox2DDef> HitboxGroup(string name)
            => !string.IsNullOrEmpty(name) && Hitboxes.TryGetValue(name, out List<Hitbox2DDef> g) ? g : null;

        /// <summary>
        /// 父先子后的骨骼求值顺序，由 <see cref="Resolve"/> 生成
        /// </summary>
        public int[] EvaluationOrder { get; private set; } = [];
        /// <summary>
        /// 每根骨骼的直接子骨骼索引，由 <see cref="Resolve"/> 生成
        /// </summary>
        public int[][] Children { get; private set; } = [];
        /// <summary>
        /// 是否已成功解析
        /// </summary>
        public bool Resolved { get; private set; }

        private readonly Dictionary<string, int> boneLookup = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> pieceLookup = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> solverLookup = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> ribbonLookup = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> channelLookup = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> poseLookup = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int[]> groupLookup = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> moveLookup = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> gaitLookup = new(StringComparer.Ordinal);
        private Animation.Rig2DPose[] poseValues = [];
        private Animation.Rig2DMove[] moveValues = [];

        /// <summary>
        /// 按名取运动层定义；缺失返回 <see langword="null"/>
        /// </summary>
        public Gait2DDef GaitValue(string name) {
            if (string.IsNullOrEmpty(name)) {
                return null;
            }
            return gaitLookup.TryGetValue(name, out int i) ? Gaits[i] : null;
        }

        /// <summary>
        /// 按名查招式索引，缺失返回 <c>-1</c>
        /// </summary>
        public int MoveIndex(string name) {
            if (string.IsNullOrEmpty(name)) {
                return -1;
            }
            return moveLookup.TryGetValue(name, out int i) ? i : -1;
        }

        /// <summary>
        /// 解析后的招式（全体实例共享）；越界返回 <see langword="null"/>
        /// </summary>
        public Animation.Rig2DMove MoveValue(int index) => (uint)index < (uint)moveValues.Length ? moveValues[index] : null;

        /// <summary>
        /// 按名取解析后的招式；缺失返回 <see langword="null"/>
        /// </summary>
        public Animation.Rig2DMove MoveValue(string name) => MoveValue(MoveIndex(name));

        /// <summary>
        /// 按名查通道索引，缺失返回 <c>-1</c>
        /// </summary>
        public int ChannelIndex(string name) {
            if (string.IsNullOrEmpty(name)) {
                return -1;
            }
            return channelLookup.TryGetValue(name, out int i) ? i : -1;
        }

        /// <summary>
        /// 按名查姿态索引，缺失返回 <c>-1</c>
        /// </summary>
        public int PoseIndex(string name) {
            if (string.IsNullOrEmpty(name)) {
                return -1;
            }
            return poseLookup.TryGetValue(name, out int i) ? i : -1;
        }

        /// <summary>
        /// 通道组的通道索引；缺失返回空数组
        /// </summary>
        public int[] ChannelGroup(string name) {
            if (string.IsNullOrEmpty(name)) {
                return [];
            }
            return groupLookup.TryGetValue(name, out int[] g) ? g : [];
        }

        /// <summary>
        /// 解析后的姿态值（全体实例共享，只读使用；要改先 <see cref="Animation.Rig2DPose.CopyFrom"/> 到自己的缓冲）。越界返回 <see langword="null"/>
        /// </summary>
        public Animation.Rig2DPose PoseValue(int index) => (uint)index < (uint)poseValues.Length ? poseValues[index] : null;

        /// <summary>
        /// 按名查骨骼索引，缺失返回 <c>-1</c>
        /// </summary>
        public int BoneIndex(string name) {
            if (string.IsNullOrEmpty(name)) {
                return -1;
            }
            return boneLookup.TryGetValue(name, out int i) ? i : -1;
        }

        /// <summary>
        /// 按名查贴图件索引（件名或骨骼名），缺失返回 <c>-1</c>
        /// </summary>
        public int PieceIndex(string name) {
            if (string.IsNullOrEmpty(name)) {
                return -1;
            }
            return pieceLookup.TryGetValue(name, out int i) ? i : -1;
        }

        /// <summary>
        /// 按名查求解器索引，缺失返回 <c>-1</c>
        /// </summary>
        public int SolverIndex(string name) {
            if (string.IsNullOrEmpty(name)) {
                return -1;
            }
            return solverLookup.TryGetValue(name, out int i) ? i : -1;
        }

        /// <summary>
        /// 按名查带状件索引（件名或其首骨名），缺失返回 <c>-1</c>
        /// </summary>
        public int RibbonIndex(string name) {
            if (string.IsNullOrEmpty(name)) {
                return -1;
            }
            return ribbonLookup.TryGetValue(name, out int i) ? i : -1;
        }

        /// <summary>
        /// 骨骼数量
        /// </summary>
        public int BoneCount => Bones.Count;

        /// <summary>
        /// 解析全部名字引用并生成求值顺序
        /// <br/>失败时返回 <see langword="false"/> 并给出可读的错误汇总；成功后 <see cref="Resolved"/> 为真
        /// <br/>允许多个根骨骼；父引用成环或指向自身视为错误
        /// </summary>
        public bool Resolve(out string error) {
            Resolved = false;
            StringBuilder sb = null;
            void Fail(string msg) {
                sb ??= new StringBuilder();
                sb.Append(msg).Append("; ");
            }

            boneLookup.Clear();
            pieceLookup.Clear();
            solverLookup.Clear();
            ribbonLookup.Clear();
            channelLookup.Clear();
            poseLookup.Clear();
            groupLookup.Clear();
            moveLookup.Clear();
            gaitLookup.Clear();
            poseValues = [];
            moveValues = [];

            if (Bones.Count == 0) {
                Fail("no bones");
            }

            for (int i = 0; i < Bones.Count; i++) {
                Bone2DDef b = Bones[i];
                b.Index = i;
                if (string.IsNullOrEmpty(b.Name)) {
                    Fail($"bone #{i} has no name");
                    continue;
                }
                if (!boneLookup.TryAdd(b.Name, i)) {
                    Fail($"duplicate bone name '{b.Name}'");
                }
            }

            int[] parents = new int[Bones.Count];
            for (int i = 0; i < Bones.Count; i++) {
                Bone2DDef b = Bones[i];
                if (b.IsRoot) {
                    parents[i] = -1;
                }
                else {
                    int p = BoneIndex(b.Parent);
                    if (p < 0) {
                        Fail($"bone '{b.Name}' parent '{b.Parent}' not found");
                    }
                    else if (p == i) {
                        Fail($"bone '{b.Name}' parents itself");
                        p = -1;
                    }
                    parents[i] = p;
                }
                b.ParentIndex = parents[i];
            }

            //环检测：沿父链走不超过 N 步就该到根
            for (int i = 0; i < Bones.Count; i++) {
                int cur = i;
                int steps = 0;
                while (cur >= 0 && steps <= Bones.Count) {
                    cur = parents[cur];
                    steps++;
                }
                if (cur >= 0) {
                    Fail($"bone '{Bones[i].Name}' is in a parent cycle");
                }
            }

            EvaluationOrder = BuildEvaluationOrder(parents);
            Children = BuildChildren(parents);

            for (int i = 0; i < Pieces.Count; i++) {
                Piece2DDef p = Pieces[i];
                p.Index = i;
                p.BoneIndex = BoneIndex(p.Bone);
                if (p.BoneIndex < 0) {
                    Fail($"piece '{p.DisplayName}' bone '{p.Bone}' not found");
                }
                p.Bone2Index = string.IsNullOrEmpty(p.Bone2) ? -1 : BoneIndex(p.Bone2);
                if (!string.IsNullOrEmpty(p.Bone2) && p.Bone2Index < 0) {
                    Fail($"piece '{p.DisplayName}' bone2 '{p.Bone2}' not found");
                }
                if (p.FrameBy is Piece2DFrameBy fb) {
                    fb.BoneIndex = string.IsNullOrEmpty(fb.Bone) ? p.BoneIndex : BoneIndex(fb.Bone);
                    if (fb.BoneIndex < 0 && fb.Source != Piece2DFrameSource.Channel) {
                        Fail($"piece '{p.DisplayName}' frameBy bone '{fb.Bone}' not found");
                    }
                    fb.RefIndex = !string.IsNullOrEmpty(fb.Ref) ? BoneIndex(fb.Ref)
                        : fb.BoneIndex >= 0 ? Bones[fb.BoneIndex].ParentIndex : -1;
                    if (!string.IsNullOrEmpty(fb.Ref) && fb.RefIndex < 0) {
                        Fail($"piece '{p.DisplayName}' frameBy ref '{fb.Ref}' not found");
                    }
                }
                if (p.Frames < 1) {
                    p.Frames = 1;
                }
                string key = p.DisplayName;
                if (!string.IsNullOrEmpty(key)) {
                    pieceLookup.TryAdd(key, i);
                }
            }

            for (int i = 0; i < Solvers.Count; i++) {
                Solver2DDef s = Solvers[i];
                if (string.IsNullOrEmpty(s.Name)) {
                    s.Name = $"{s.Type}_{i}";
                }
                if (!solverLookup.TryAdd(s.Name, i)) {
                    Fail($"duplicate solver name '{s.Name}'");
                }
                int[] idx = new int[s.Bones.Count];
                for (int k = 0; k < idx.Length; k++) {
                    idx[k] = BoneIndex(s.Bones[k]);
                    if (idx[k] < 0) {
                        Fail($"solver '{s.Name}' bone '{s.Bones[k]}' not found");
                    }
                }
                s.BoneIndices = idx;
            }

            for (int i = 0; i < Ribbons.Count; i++) {
                Ribbon2DDef r = Ribbons[i];
                r.Index = i;
                if (r.Bones.Count == 0) {
                    Fail($"ribbon '{r.DisplayName}' has no bones");
                }
                int[] idx = new int[r.Bones.Count];
                for (int k = 0; k < idx.Length; k++) {
                    idx[k] = BoneIndex(r.Bones[k]);
                    if (idx[k] < 0) {
                        Fail($"ribbon '{r.DisplayName}' bone '{r.Bones[k]}' not found");
                    }
                }
                r.BoneIndices = idx;
                string key = r.DisplayName;
                if (!string.IsNullOrEmpty(key)) {
                    ribbonLookup.TryAdd(key, i);
                }
            }

            ResolveChannels(Fail);
            foreach (Piece2DDef p in Pieces) {
                if (p.FrameBy is { Source: Piece2DFrameSource.Channel } fb) {
                    fb.ChannelIndex = ChannelIndex(fb.Channel);
                    if (fb.ChannelIndex < 0) {
                        Fail($"piece '{p.DisplayName}' frameBy channel '{fb.Channel}' not found");
                    }
                }
            }
            ResolveGroups(Fail);
            ResolvePoses(Fail);
            ResolveMoves(Fail);
            ResolveGaits(Fail);
            foreach (KeyValuePair<string, List<Hitbox2DDef>> kv in Hitboxes) {
                foreach (Hitbox2DDef h in kv.Value) {
                    h.BoneIndex = BoneIndex(h.Bone);
                    if (h.BoneIndex < 0) {
                        Fail($"hitbox group '{kv.Key}': bone '{h.Bone}' not found");
                    }
                }
            }

            for (int i = 0; i < Clips.Count; i++) {
                Clips[i]?.Resolve(this);
            }

            error = sb?.ToString();
            Resolved = sb == null;
            return Resolved;
        }

        //属性码 = 下标（运行时按码分派，见 Rig2DInstance.ApplyChannelBindings）
        internal static readonly string[] BoneProps = ["rotation", "offset", "length"];
        internal static readonly string[] PieceProps = ["frame", "visible", "layer", "rotation", "alpha", "scale"];
        internal static readonly string[] RibbonProps = ["width", "alpha", "visible", "uvOffset", "layer"];
        internal static readonly string[] RootProps = ["position", "rotation"];

        //通道：名字查表 + 绑定对象名转索引 + 属性名校验（求解器属性由求解器自己认，实例绑定时校验）
        private void ResolveChannels(Action<string> fail) {
            for (int i = 0; i < Channels.Count; i++) {
                Channel2DDef c = Channels[i];
                c.Index = i;
                if (string.IsNullOrEmpty(c.Name)) {
                    fail($"channel #{i} has no name");
                    continue;
                }
                if (!channelLookup.TryAdd(c.Name, i)) {
                    fail($"duplicate channel name '{c.Name}'");
                }
                Channel2DBind b = c.Bind;
                if (b == null || b.Target == Channel2DTarget.None) {
                    continue;
                }
                b.TargetIndex = -1;
                b.SpaceBoneIndex = -1;
                b.PropCode = -1;
                switch (b.Target) {
                    case Channel2DTarget.Bone:
                        b.TargetIndex = BoneIndex(b.Name);
                        if (b.TargetIndex < 0) {
                            fail($"channel '{c.Name}' bone '{b.Name}' not found");
                        }
                        b.PropCode = Array.IndexOf(BoneProps, b.Prop);
                        if (b.PropCode < 0) {
                            fail($"channel '{c.Name}' bone prop '{b.Prop}' unknown (rotation / offset / length)");
                        }
                        break;
                    case Channel2DTarget.Solver:
                        b.TargetIndex = SolverIndex(b.Name);
                        if (b.TargetIndex < 0) {
                            fail($"channel '{c.Name}' solver '{b.Name}' not found");
                        }
                        if (string.IsNullOrEmpty(b.Prop)) {
                            fail($"channel '{c.Name}' solver binding has no prop");
                        }
                        break;
                    case Channel2DTarget.Piece:
                        b.TargetIndex = PieceIndex(b.Name);
                        if (b.TargetIndex < 0) {
                            fail($"channel '{c.Name}' piece '{b.Name}' not found");
                        }
                        b.PropCode = Array.IndexOf(PieceProps, b.Prop);
                        if (b.PropCode < 0) {
                            fail($"channel '{c.Name}' piece prop '{b.Prop}' unknown (frame / visible / layer / rotation / alpha / scale)");
                        }
                        break;
                    case Channel2DTarget.Ribbon:
                        b.TargetIndex = RibbonIndex(b.Name);
                        if (b.TargetIndex < 0) {
                            fail($"channel '{c.Name}' ribbon '{b.Name}' not found");
                        }
                        b.PropCode = Array.IndexOf(RibbonProps, b.Prop);
                        if (b.PropCode < 0) {
                            fail($"channel '{c.Name}' ribbon prop '{b.Prop}' unknown (width / alpha / visible / uvOffset / layer)");
                        }
                        break;
                    case Channel2DTarget.Root:
                        b.PropCode = Array.IndexOf(RootProps, b.Prop);
                        if (b.PropCode < 0) {
                            fail($"channel '{c.Name}' root prop '{b.Prop}' unknown (position / rotation)");
                        }
                        break;
                }
                if (b.Space == Channel2DSpace.Bone) {
                    b.SpaceBoneIndex = BoneIndex(b.SpaceBone);
                    if (b.SpaceBoneIndex < 0) {
                        fail($"channel '{c.Name}' space bone '{b.SpaceBone}' not found");
                    }
                }
            }
        }

        private void ResolveGroups(Action<string> fail) {
            foreach (KeyValuePair<string, List<string>> kv in ChannelGroups) {
                List<int> idx = new(kv.Value.Count);
                foreach (string name in kv.Value) {
                    int i = ChannelIndex(name);
                    if (i < 0) {
                        fail($"channel group '{kv.Key}' channel '{name}' not found");
                        continue;
                    }
                    idx.Add(i);
                }
                groupLookup[kv.Key] = idx.ToArray();
            }
        }

        //姿态：继承链按需递归展开（带环检测），值表 = 通道缺省 ← 继承 ← 本表
        private void ResolvePoses(Action<string> fail) {
            int n = Channels.Count;
            for (int i = 0; i < Poses.Count; i++) {
                Pose2DDef p = Poses[i];
                p.Index = i;
                p.Resolved = [];
                p.Specified = [];
                if (string.IsNullOrEmpty(p.Name)) {
                    fail($"pose #{i} has no name");
                    continue;
                }
                if (!poseLookup.TryAdd(p.Name, i)) {
                    fail($"duplicate pose name '{p.Name}'");
                }
            }
            int[] state = new int[Poses.Count];
            for (int i = 0; i < Poses.Count; i++) {
                ResolvePose(i, n, state, fail);
            }
            poseValues = new Animation.Rig2DPose[Poses.Count];
            for (int i = 0; i < Poses.Count; i++) {
                Animation.Rig2DPose value = new(this);
                Vector2[] resolved = Poses[i].Resolved;
                if (resolved.Length == n) {
                    Array.Copy(resolved, value.Values, n);
                }
                poseValues[i] = value;
            }
        }

        //state：0 未访问 / 1 展开中 / 2 已完成
        private void ResolvePose(int i, int n, int[] state, Action<string> fail) {
            if (state[i] == 2) {
                return;
            }
            Pose2DDef p = Poses[i];
            if (state[i] == 1) {
                fail($"pose '{p.Name}' inherits itself through a cycle");
                p.Resolved = Defaults(n);
                p.Specified = new bool[n];
                state[i] = 2;
                return;
            }
            state[i] = 1;
            Vector2[] values;
            bool[] specified;
            if (!string.IsNullOrEmpty(p.Inherit)) {
                int parent = PoseIndex(p.Inherit);
                if (parent < 0) {
                    fail($"pose '{p.Name}' inherits unknown pose '{p.Inherit}'");
                    values = Defaults(n);
                    specified = new bool[n];
                }
                else {
                    ResolvePose(parent, n, state, fail);
                    values = (Vector2[])Poses[parent].Resolved.Clone();
                    specified = (bool[])Poses[parent].Specified.Clone();
                    if (values.Length != n) {
                        values = Defaults(n);
                        specified = new bool[n];
                    }
                }
            }
            else {
                values = Defaults(n);
                specified = new bool[n];
            }
            foreach (KeyValuePair<string, Vector2> kv in p.Values) {
                int c = ChannelIndex(kv.Key);
                if (c < 0) {
                    fail($"pose '{p.Name}' channel '{kv.Key}' not found");
                    continue;
                }
                values[c] = kv.Value;
                specified[c] = true;
            }
            p.Resolved = values;
            p.Specified = specified;
            state[i] = 2;
        }

        private void ResolveMoves(Action<string> fail) {
            moveValues = new Animation.Rig2DMove[Moves.Count];
            for (int i = 0; i < Moves.Count; i++) {
                Move2DDef m = Moves[i];
                if (string.IsNullOrEmpty(m.Name)) {
                    fail($"move #{i} has no name");
                    continue;
                }
                if (!moveLookup.TryAdd(m.Name, i)) {
                    fail($"duplicate move name '{m.Name}'");
                }
                moveValues[i] = Animation.Rig2DMove.Build(this, m, fail);
            }
        }

        private void ResolveGaits(Action<string> fail) {
            for (int i = 0; i < Gaits.Count; i++) {
                Gait2DDef g = Gaits[i];
                if (string.IsNullOrEmpty(g.Name)) {
                    fail($"gait #{i} has no name");
                    continue;
                }
                if (!gaitLookup.TryAdd(g.Name, i)) {
                    fail($"duplicate gait name '{g.Name}'");
                }
                foreach (Gait2DLeg leg in g.Legs) {
                    leg.ChannelIndex = ChannelIndex(leg.Channel);
                    if (leg.ChannelIndex < 0) {
                        fail($"gait '{g.Name}': leg channel '{leg.Channel}' not found");
                    }
                    else if (Channels[leg.ChannelIndex].IsScalar) {
                        fail($"gait '{g.Name}': leg channel '{leg.Channel}' must be a vector channel");
                        leg.ChannelIndex = -1;
                    }
                    leg.AngleIndex = string.IsNullOrEmpty(leg.Angle) ? -1 : ChannelIndex(leg.Angle);
                    if (!string.IsNullOrEmpty(leg.Angle) && leg.AngleIndex < 0) {
                        fail($"gait '{g.Name}': angle channel '{leg.Angle}' not found");
                    }
                    leg.SwingIndex = ScalarChannel(g.Name, "swing", leg.Swing, fail);
                    leg.AutoIndex = ScalarChannel(g.Name, "auto", leg.Auto, fail);
                }
                foreach (Gait2DMode mode in g.Modes) {
                    foreach (Gait2DWave wave in mode.Waves) {
                        wave.ChannelIndex = ChannelIndex(wave.Channel);
                        if (wave.ChannelIndex < 0) {
                            fail($"gait '{g.Name}' mode '{mode.Name}': wave channel '{wave.Channel}' not found");
                        }
                    }
                }
                g.HipIndex = string.IsNullOrEmpty(g.Hip) ? -1 : ChannelIndex(g.Hip);
                if (!string.IsNullOrEmpty(g.Hip) && g.HipIndex < 0) {
                    fail($"gait '{g.Name}': hip channel '{g.Hip}' not found");
                }
                g.TiltIndex = string.IsNullOrEmpty(g.Tilt) ? -1 : ChannelIndex(g.Tilt);
                if (!string.IsNullOrEmpty(g.Tilt) && g.TiltIndex < 0) {
                    fail($"gait '{g.Name}': tilt channel '{g.Tilt}' not found");
                }
                if (g.Modes.Count == 0) {
                    fail($"gait '{g.Name}' has no modes");
                }
            }
        }

        /// <summary>运动层腿上的可选标量通道：空名返回 −1；找不到或是矢量通道都记错并返回 −1</summary>
        private int ScalarChannel(string gait, string what, string name, Action<string> fail) {
            if (string.IsNullOrEmpty(name)) {
                return -1;
            }
            int i = ChannelIndex(name);
            if (i < 0) {
                fail($"gait '{gait}': {what} channel '{name}' not found");
                return -1;
            }
            if (!Channels[i].IsScalar) {
                fail($"gait '{gait}': {what} channel '{name}' must be a scalar channel");
                return -1;
            }
            return i;
        }

        private Vector2[] Defaults(int n) {
            Vector2[] v = new Vector2[n];
            for (int i = 0; i < n; i++) {
                v[i] = Channels[i].Default;
            }
            return v;
        }

        //深度优先：所有根依序入栈，弹出即输出，保证父总在子之前；孤立/异常节点按自然序补齐
        private static int[] BuildEvaluationOrder(int[] parents) {
            int n = parents.Length;
            int[] order = new int[n];
            if (n == 0) {
                return order;
            }
            List<int>[] children = new List<int>[n];
            for (int i = 0; i < n; i++) {
                int p = parents[i];
                if (p < 0 || p >= n) {
                    continue;
                }
                children[p] ??= [];
                children[p].Add(i);
            }
            int cursor = 0;
            Stack<int> stack = new(n);
            for (int i = n - 1; i >= 0; i--) {
                if (parents[i] < 0 || parents[i] >= n) {
                    stack.Push(i);
                }
            }
            bool[] visited = new bool[n];
            while (stack.Count > 0) {
                int node = stack.Pop();
                if (visited[node]) {
                    continue;
                }
                visited[node] = true;
                order[cursor++] = node;
                List<int> kids = children[node];
                if (kids == null) {
                    continue;
                }
                for (int k = kids.Count - 1; k >= 0; k--) {
                    stack.Push(kids[k]);
                }
            }
            for (int i = 0; i < n && cursor < n; i++) {
                if (!visited[i]) {
                    order[cursor++] = i;
                }
            }
            return order;
        }

        private static int[][] BuildChildren(int[] parents) {
            int n = parents.Length;
            List<int>[] lists = new List<int>[n];
            for (int i = 0; i < n; i++) {
                int p = parents[i];
                if (p < 0 || p >= n) {
                    continue;
                }
                lists[p] ??= [];
                lists[p].Add(i);
            }
            int[][] result = new int[n][];
            for (int i = 0; i < n; i++) {
                result[i] = lists[i] != null ? lists[i].ToArray() : [];
            }
            return result;
        }

        /// <summary>
        /// 深拷贝整份定义（未解析状态，需要调用方再 <see cref="Resolve"/>）
        /// </summary>
        public Rig2DDefinition Clone() {
            Rig2DDefinition c = new() {
                Name = Name,
                SnapDistance = SnapDistance,
            };
            foreach (Bone2DDef b in Bones) {
                c.Bones.Add(b.Clone());
            }
            foreach (Piece2DDef p in Pieces) {
                c.Pieces.Add(p.Clone());
            }
            foreach (Solver2DDef s in Solvers) {
                c.Solvers.Add(s.Clone());
            }
            foreach (Ribbon2DDef r in Ribbons) {
                c.Ribbons.Add(r.Clone());
            }
            foreach (Animation.Rig2DClip clip in Clips) {
                if (clip != null) {
                    c.Clips.Add(clip.Clone());
                }
            }
            foreach (Channel2DDef ch in Channels) {
                c.Channels.Add(ch.Clone());
            }
            foreach (KeyValuePair<string, List<string>> kv in ChannelGroups) {
                c.ChannelGroups[kv.Key] = [.. kv.Value];
            }
            foreach (Pose2DDef p in Poses) {
                c.Poses.Add(p.Clone());
            }
            foreach (Move2DDef m in Moves) {
                c.Moves.Add(m.Clone());
            }
            foreach (Gait2DDef g in Gaits) {
                c.Gaits.Add(g.Clone());
            }
            foreach (KeyValuePair<string, List<Hitbox2DDef>> kv in Hitboxes) {
                List<Hitbox2DDef> list = [];
                foreach (Hitbox2DDef h in kv.Value) {
                    list.Add(h.Clone());
                }
                c.Hitboxes[kv.Key] = list;
            }
            return c;
        }

        /// <summary>
        /// 两份定义的骨骼拓扑是否一致（数量、名字、父级都相同）；热重载据此决定能否原地换参
        /// </summary>
        public bool SameTopology(Rig2DDefinition other) {
            if (other == null || other.Bones.Count != Bones.Count) {
                return false;
            }
            for (int i = 0; i < Bones.Count; i++) {
                Bone2DDef a = Bones[i];
                Bone2DDef b = other.Bones[i];
                if (!string.Equals(a.Name, b.Name, StringComparison.Ordinal)
                    || !string.Equals(a.Parent ?? string.Empty, b.Parent ?? string.Empty, StringComparison.Ordinal)) {
                    return false;
                }
            }
            if (other.Pieces.Count != Pieces.Count || other.Solvers.Count != Solvers.Count || other.Ribbons.Count != Ribbons.Count) {
                return false;
            }
            for (int i = 0; i < Solvers.Count; i++) {
                if (!string.Equals(Solvers[i].Name, other.Solvers[i].Name, StringComparison.Ordinal)
                    || !string.Equals(Solvers[i].Type, other.Solvers[i].Type, StringComparison.Ordinal)) {
                    return false;
                }
            }
            for (int i = 0; i < Ribbons.Count; i++) {
                if (!string.Equals(Ribbons[i].DisplayName, other.Ribbons[i].DisplayName, StringComparison.Ordinal)) {
                    return false;
                }
            }
            return true;
        }
    }
}
