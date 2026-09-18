using InnoVault.Rigs2D.Animation;
using InnoVault.Rigs2D.Data;
using InnoVault.Rigs2D.Solvers;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using Terraria;

namespace InnoVault.Rigs2D.Runtime
{
    /// <summary>
    /// 一副骨架的运行时实例：每个实体（NPC / 图鉴演员 / 弹幕）各持一份
    /// <br/>每帧流程：<see cref="SetRoot"/>（已同步的根位姿）→ 消费方写求解器目标 → <see cref="Step"/>
    /// （关键帧层 → 静息传播 → 各求解器按序求解并重传播子树）→ 读骨骼位姿做判定 / 交 <see cref="Rig2DRenderer"/> 绘制
    /// <br/>联机契约：实例是纯本地表现量，不发包；只消费已同步输入，同输入各端同输出
    /// </summary>
    public sealed class Rig2DInstance
    {
        /// <summary>
        /// 来源资产（代码直建的实例也会有一个包装资产）
        /// </summary>
        public Vault2DRig Asset { get; }
        /// <summary>
        /// 当前绑定的定义（热重载后会换成新对象）
        /// </summary>
        public Rig2DDefinition Definition { get; private set; }
        /// <summary>
        /// 骨架名
        /// </summary>
        public string Name => Definition?.Name ?? string.Empty;
        /// <summary>
        /// 全部骨骼的世界位姿，下标同 <see cref="Rig2DDefinition.Bones"/>
        /// </summary>
        public Bone2D[] Bones { get; private set; } = [];
        /// <summary>
        /// 全部贴图件的运行时状态，下标同 <see cref="Rig2DDefinition.Pieces"/>
        /// </summary>
        public Piece2DState[] Pieces { get; private set; } = [];
        /// <summary>
        /// 全部带状件的运行时状态，下标同 <see cref="Rig2DDefinition.Ribbons"/>
        /// </summary>
        public Ribbon2DState[] Ribbons { get; private set; } = [];
        /// <summary>
        /// 求解器实例，下标同 <see cref="Rig2DDefinition.Solvers"/>；创建失败的槽位为 <see langword="null"/>
        /// </summary>
        public Rig2DSolver[] Solvers { get; private set; } = [];
        /// <summary>
        /// 关键帧播放头（姿态层，在求解之前写入局部覆盖）
        /// </summary>
        public Rig2DClipPlayer Animation { get; }

        /// <summary>
        /// 整体倍率：骨长、偏移、贴图尺寸同乘（战斗端每帧取宿主 <c>NPC.scale</c>，图鉴端保持 1）
        /// </summary>
        public float Scale { get; set; } = 1f;
        /// <summary>
        /// 确定性种子：装饰性相位偏移的唯一来源，各端一致
        /// </summary>
        public float Seed { get; set; }
        /// <summary>
        /// 累计帧时间（由 <see cref="Step"/> 推进）
        /// </summary>
        public float Time { get; set; }
        /// <summary>
        /// 骨架级镜像（朝向翻转）：沿根朝向轴反射整副骨架——每根继承旋转的骨骼其局部偏移的侧向分量与局部旋转取反，
        /// 全部贴图件沿骨轴镜像，求解器的局部极性参数（肘向 / 卷向 / 弓向）跟着翻；
        /// <c>InheritRotation = false</c> 的世界绝对角骨骼不受影响（重力向的骨骼翻身后仍朝下）
        /// <br/>侧视生物的惯例：<c>SetRoot(pos, dir &gt; 0 ? rot : rot + π)</c> 再 <c>Mirrored = dir &lt; 0</c>。
        /// 值变化时下一次 <see cref="Step"/> 默认整副 <see cref="Snap"/>（见 <see cref="SnapOnMirrorChange"/>）
        /// </summary>
        public bool Mirrored {
            get => mirrored;
            set {
                if (mirrored != value) {
                    mirrored = value;
                    mirrorDirty = true;
                }
            }
        }
        /// <summary>
        /// 镜像符号：<see cref="Mirrored"/> 为 +1 / −1，求解器把局部极性乘上它
        /// </summary>
        public float MirrorSign => mirrored ? -1f : 1f;
        /// <summary>
        /// <see cref="Mirrored"/> 变化时是否在下一次 <see cref="Step"/> 硬重建（默认开；关掉则各求解器从翻转后的静息姿态自行收敛）
        /// </summary>
        public bool SnapOnMirrorChange { get; set; } = true;
        /// <summary>
        /// 根位置（世界）
        /// </summary>
        public Vector2 RootPosition { get; set; }
        /// <summary>
        /// 根朝向（弧度）
        /// </summary>
        public float RootRotation { get; set; }
        /// <summary>
        /// 根位姿单帧位移超过此值（乘 Scale）即整副硬重建；初值取定义
        /// </summary>
        public float SnapDistance { get; set; }
        /// <summary>
        /// 是否已完成首次求解
        /// </summary>
        public bool Built { get; private set; }
        /// <summary>
        /// 是否参与调试叠层（舞台坐标下的图鉴实例应关掉）
        /// </summary>
        public bool DebugVisible { get; set; } = true;
        /// <summary>
        /// 最近一次 <see cref="Step"/> 的游戏帧号
        /// </summary>
        public uint LastStepTick { get; private set; }

        /// <summary>
        /// 声明式句柄是否全部就位：<see cref="Bind"/> 过且定义有效、每个标记成员都命中；未 <see cref="Bind"/> 时为假
        /// </summary>
        public bool Bound { get; private set; }
        /// <summary>
        /// 最近一次绑定的问题清单（成功为空）
        /// </summary>
        public IReadOnlyList<string> BindErrors => bindErrors;

        private int boundVersion;
        private object bindTarget;
        private Action<Rig2DInstance> bindCallback;
        private readonly List<string> bindErrors = [];
        private bool[] externalDriven = [];
        private bool[] solverDriven = [];
        private bool[] solverRan = [];
        private float[] localRotation = [];
        private Vector2[] localOffset = [];
        private bool[] hasLocalOffset = [];
        private float[] localLength = [];
        private int[] pieceOrder = [];
        private int[] ribbonOrder = [];
        private int[] walkStack = [];
        private Vector2 lastRootPos;
        private bool hasLastRoot;
        private bool mirrored;
        private bool mirrorDirty;

        internal Rig2DInstance(Vault2DRig asset, float seed) {
            Asset = asset;
            Seed = seed;
            Animation = new Rig2DClipPlayer(this);
            BindDefinition(asset.Definition, asset.Version);
        }

        //==================== 绑定 ====================

        private void BindDefinition(Rig2DDefinition def, int version) {
            Definition = def;
            boundVersion = version;
            int n = def?.BoneCount ?? 0;
            Bones = new Bone2D[n];
            externalDriven = new bool[n];
            solverDriven = new bool[n];
            localRotation = new float[n];
            localOffset = new Vector2[n];
            hasLocalOffset = new bool[n];
            localLength = new float[n];
            walkStack = new int[Math.Max(n, 1)];
            Array.Fill(localRotation, float.NaN);
            Array.Fill(localLength, float.NaN);

            int pc = def?.Pieces.Count ?? 0;
            Pieces = new Piece2DState[pc];
            pieceOrder = new int[pc];
            for (int i = 0; i < pc; i++) {
                Pieces[i] = Piece2DState.FromDef(def.Pieces[i]);
                pieceOrder[i] = i;
            }

            int rc = def?.Ribbons.Count ?? 0;
            Ribbons = new Ribbon2DState[rc];
            ribbonOrder = new int[rc];
            for (int i = 0; i < rc; i++) {
                Ribbons[i] = Ribbon2DState.FromDef(def.Ribbons[i]);
                ribbonOrder[i] = i;
            }

            int sc = def?.Solvers.Count ?? 0;
            Solvers = new Rig2DSolver[sc];
            solverRan = new bool[sc];
            for (int i = 0; i < sc; i++) {
                Solvers[i] = CreateSolver(def.Solvers[i]);
            }
            for (int i = 0; i < sc; i++) {
                Solvers[i]?.PostBind();
            }
            SnapDistance = def?.SnapDistance ?? 340f;
            Built = false;
            hasLastRoot = false;
        }

        private Rig2DSolver CreateSolver(Solver2DDef sd) {
            Rig2DSolver s = Rig2DSolverRegistry.Create(sd.Type);
            if (s == null) {
                VaultMod.LoggerError($"[Rig2D:{Name}]", $"unknown solver type '{sd.Type}' for '{sd.Name}'");
                return null;
            }
            try {
                s.Bind(this, sd);
            } catch (Exception ex) {
                VaultMod.LoggerError($"[Rig2D:{Name}/{sd.Name}]", $"solver configure failed: {ex.Message}");
                return null;
            }
            return s;
        }

        /// <summary>
        /// 热重载入口：拓扑一致就原地换参（骨骼位姿与求解器状态保留），否则整副重建
        /// </summary>
        internal void Rebind(Rig2DDefinition def, int version) {
            if (def == null) {
                return;
            }
            if (Definition != null && Definition.SameTopology(def)) {
                Definition = def;
                boundVersion = version;
                SnapDistance = def.SnapDistance;
                for (int i = 0; i < Solvers.Length; i++) {
                    if (Solvers[i] == null) {
                        Solvers[i] = CreateSolver(def.Solvers[i]);
                        continue;
                    }
                    try {
                        Solvers[i].Bind(this, def.Solvers[i]);
                    } catch (Exception ex) {
                        VaultMod.LoggerError($"[Rig2D:{Name}/{def.Solvers[i].Name}]", $"solver reconfigure failed: {ex.Message}");
                    }
                }
                for (int i = 0; i < Solvers.Length; i++) {
                    Solvers[i]?.PostBind();
                }
                for (int i = 0; i < Pieces.Length; i++) {
                    //层序键跟着新定义走，其余运行时状态是消费方逐帧写的，保留
                    Pieces[i].SortKey = def.Pieces[i].Layer;
                }
                for (int i = 0; i < Ribbons.Length; i++) {
                    Ribbons[i].SortKey = def.Ribbons[i].Layer;
                }
                ApplyBinding();
                return;
            }
            BindDefinition(def, version);
            ApplyBinding();
        }

        /// <summary>
        /// 声明式句柄绑定：把 <paramref name="target"/> 上标了 <see cref="Rig2DBoneAttribute"/> /
        /// <see cref="Rig2DPieceAttribute"/> / <see cref="Rig2DSolverAttribute"/> 的实例成员按名填好，
        /// 之后每次热重载重绑（<see cref="Step"/> 内发现定义版本变化）自动重填
        /// <br/>全部命中且定义有效时 <see cref="Bound"/> 为真并回调 <paramref name="onBound"/>——一次性配置
        /// （求解器开关、事件回调、贴图覆写）放在回调里，重绑后会再跑一遍
        /// <br/>缺名 / 类型不符不抛：下标写 <c>-1</c>、引用写 <see langword="null"/>，<see cref="Bound"/> 为假，
        /// 问题汇总进 <see cref="BindErrors"/> 并合并记一条日志；定义为空（资产未加载）时静默为假
        /// </summary>
        /// <param name="target">持有标记成员的对象（通常就是消费方自己）</param>
        /// <param name="onBound">每次成功绑定后的回调</param>
        /// <returns>本次是否全部就位</returns>
        public bool Bind(object target, Action<Rig2DInstance> onBound = null) {
            bindTarget = target;
            bindCallback = onBound;
            ApplyBinding();
            return Bound;
        }

        private void ApplyBinding() {
            if (bindTarget == null) {
                return;
            }
            bindErrors.Clear();
            if (Definition == null || Bones.Length == 0) {
                Bound = false;
                bindErrors.Add("definition is empty");
                return;
            }
            Bound = Rig2DBinder.Apply(this, bindTarget, bindErrors);
            if (!Bound) {
                VaultMod.LoggerError($"[Rig2D:{Name}:bind:{bindTarget.GetType().Name}]",
                    $"binding {bindTarget.GetType().Name} to rig '{Name}' has {bindErrors.Count} problem(s): {string.Join("; ", bindErrors)}");
                return;
            }
            try {
                bindCallback?.Invoke(this);
            } catch (Exception ex) {
                VaultMod.LoggerError($"[Rig2D:{Name}:bind:{bindTarget.GetType().Name}]", $"onBound callback threw: {ex}");
            }
        }

        //==================== 查询 ====================

        /// <summary>
        /// 按名查骨骼索引，缺失 <c>-1</c>
        /// </summary>
        public int Bone(string name) => Definition?.BoneIndex(name) ?? -1;

        /// <summary>
        /// 取骨骼引用
        /// </summary>
        public ref Bone2D BoneRef(int index) => ref Bones[index];

        /// <summary>
        /// 按名取骨骼（缺失返回默认值）
        /// </summary>
        public Bone2D GetBone(string name) {
            int i = Bone(name);
            return i >= 0 ? Bones[i] : default;
        }

        /// <summary>
        /// 某骨骼的静息长度（含 Scale 与长度覆写）
        /// </summary>
        public float RestLength(int bone) {
            float len = float.IsNaN(localLength[bone]) ? Definition.Bones[bone].Length : localLength[bone];
            return len * Scale;
        }

        /// <summary>
        /// 按父骨骼当前位姿算出的静息近端位置（不写入）。求解器用它取锚点：
        /// 被求解器接管的骨骼不参与静息传播，其 <c>Pos</c> 字段保存的是上一帧解，锚点必须从父骨骼现算
        /// </summary>
        public Vector2 RestPosition(int bone) {
            Bone2DDef d = Definition.Bones[bone];
            Vector2 anchor;
            float parDir;
            if (d.ParentIndex < 0) {
                anchor = RootPosition;
                parDir = RootRotation;
            }
            else {
                ref Bone2D p = ref Bones[d.ParentIndex];
                anchor = d.AtParentTip ? p.Tip : p.Pos;
                parDir = p.Dir;
            }
            Vector2 off = hasLocalOffset[bone] ? localOffset[bone] : d.Offset;
            //镜像：侧向分量取反（局部系 y 沿父骨骼 Side）
            float offY = off.Y * MirrorSign;
            float cos = (float)Math.Cos(parDir);
            float sin = (float)Math.Sin(parDir);
            return new Vector2(
                anchor.X + (cos * off.X - sin * offY) * Scale,
                anchor.Y + (sin * off.X + cos * offY) * Scale);
        }

        /// <summary>
        /// 按父骨骼当前位姿算出的静息轴向（不写入）
        /// </summary>
        public float RestDirection(int bone) {
            Bone2DDef d = Definition.Bones[bone];
            float parDir = d.ParentIndex < 0 ? RootRotation : Bones[d.ParentIndex].Dir;
            float rot = float.IsNaN(localRotation[bone]) ? d.Rotation : localRotation[bone];
            //镜像只翻继承旋转的骨骼；世界绝对角骨骼保持原角
            return d.InheritRotation ? parDir + rot * MirrorSign : rot;
        }

        /// <summary>
        /// 按名取求解器，缺失 <see langword="null"/>
        /// </summary>
        public Rig2DSolver Solver(string name) {
            int i = Definition?.SolverIndex(name) ?? -1;
            return i >= 0 && i < Solvers.Length ? Solvers[i] : null;
        }

        /// <summary>
        /// 按名取指定类型的求解器；缺失或类型不符返回 <see langword="null"/>
        /// </summary>
        public T Solver<T>(string name) where T : Rig2DSolver => Solver(name) as T;

        /// <summary>
        /// 取第一个指定类型的求解器
        /// </summary>
        public T Solver<T>() where T : Rig2DSolver {
            for (int i = 0; i < Solvers.Length; i++) {
                if (Solvers[i] is T t) {
                    return t;
                }
            }
            return null;
        }

        /// <summary>
        /// 按名查贴图件索引（件名或骨骼名），缺失 <c>-1</c>
        /// </summary>
        public int Piece(string name) => Definition?.PieceIndex(name) ?? -1;

        /// <summary>
        /// 取贴图件状态引用
        /// </summary>
        public ref Piece2DState PieceRef(int index) => ref Pieces[index];

        /// <summary>
        /// 按名取贴图件状态引用；缺失时抛出（件名在开发期就该对上）
        /// </summary>
        public ref Piece2DState PieceRef(string name) {
            int i = Piece(name);
            if (i < 0) {
                throw new ArgumentException($"Rig2D '{Name}' has no piece '{name}'");
            }
            return ref Pieces[i];
        }

        /// <summary>
        /// 把全部贴图件状态重置为设计值
        /// </summary>
        public void ResetPieceStates() {
            for (int i = 0; i < Pieces.Length; i++) {
                Pieces[i] = Piece2DState.FromDef(Definition.Pieces[i]);
            }
        }

        /// <summary>
        /// 按名查带状件索引（件名或其首骨名），缺失 <c>-1</c>
        /// </summary>
        public int Ribbon(string name) => Definition?.RibbonIndex(name) ?? -1;

        /// <summary>
        /// 取带状件状态引用
        /// </summary>
        public ref Ribbon2DState RibbonRef(int index) => ref Ribbons[index];

        /// <summary>
        /// 按名取带状件状态引用；缺失时抛出（件名在开发期就该对上）
        /// </summary>
        public ref Ribbon2DState RibbonRef(string name) {
            int i = Ribbon(name);
            if (i < 0) {
                throw new ArgumentException($"Rig2D '{Name}' has no ribbon '{name}'");
            }
            return ref Ribbons[i];
        }

        /// <summary>
        /// 把全部带状件状态重置为设计值
        /// </summary>
        public void ResetRibbonStates() {
            for (int i = 0; i < Ribbons.Length; i++) {
                Ribbons[i] = Ribbon2DState.FromDef(Definition.Ribbons[i]);
            }
        }

        //==================== 写入 ====================

        /// <summary>
        /// 写入根位姿（每帧在 <see cref="Step"/> 之前）
        /// </summary>
        public void SetRoot(Vector2 position, float rotation) {
            RootPosition = position;
            RootRotation = rotation;
        }

        /// <summary>
        /// 直接给某骨骼写世界位姿（本帧不再被静息传播覆盖），并立即重传播其子树
        /// <br/>用于消费方自己算的世界量（例如平滑后的螯体朝向）；每帧要写就每帧写，标记在 <see cref="Step"/> 末清除
        /// </summary>
        public void SetBoneWorld(int bone, Vector2 pos, float dir) {
            ref Bone2D b = ref Bones[bone];
            b.Pos = pos;
            b.Dir = dir;
            externalDriven[bone] = true;
            PropagateDescendants(bone);
        }

        /// <summary>
        /// 直接给某骨骼写世界位姿与长度
        /// </summary>
        public void SetBoneWorld(int bone, Vector2 pos, float dir, float length) {
            ref Bone2D b = ref Bones[bone];
            b.Pos = pos;
            b.Dir = dir;
            b.Length = length;
            externalDriven[bone] = true;
            PropagateDescendants(bone);
        }

        /// <summary>
        /// 用两点直接给某骨骼写世界位姿：近端钉在 <paramref name="from"/>、尖端指向 <paramref name="to"/>，长度取两点距离
        /// （消费方在 <see cref="Step"/> 之后铺放脚掌 / 爪尖一类末端骨的常用写法）
        /// <br/>两点重合时保持当前轴向，长度写 0；<paramref name="keepRestLength"/> 为真时长度改取静息长（只借方向，不拉伸）
        /// <br/><paramref name="syncLocal"/> 为真时把算出的轴向同步进局部静息旋转覆写（世界绝对角骨骼写世界角，继承旋转的骨骼写相对父骨骼的差角），
        /// 这样下一次硬重建 / 传播时该骨不会退回定义角
        /// </summary>
        public void SetBoneWorld(int bone, Vector2 from, Vector2 to, bool keepRestLength = false, bool syncLocal = true) {
            ref Bone2D b = ref Bones[bone];
            Vector2 d = to - from;
            float len = d.Length();
            float dir = len > 0.0001f ? (float)Math.Atan2(d.Y, d.X) : b.Dir;
            if (syncLocal) {
                Bone2DDef def = Definition.Bones[bone];
                if (def.InheritRotation) {
                    float parDir = def.ParentIndex < 0 ? RootRotation : Bones[def.ParentIndex].Dir;
                    localRotation[bone] = MathHelper.WrapAngle(dir - parDir) * MirrorSign;
                }
                else {
                    localRotation[bone] = dir;
                }
            }
            b.Pos = from;
            b.Dir = dir;
            b.Length = keepRestLength ? RestLength(bone) : len;
            externalDriven[bone] = true;
            PropagateDescendants(bone);
        }

        /// <summary>
        /// 覆写某骨骼的局部静息旋转（持久，直到 <see cref="ClearBoneOverrides"/> / <see cref="ClearBoneLocalRotation"/>）
        /// </summary>
        public void SetBoneLocalRotation(int bone, float rotation) => localRotation[bone] = rotation;

        /// <summary>
        /// 覆写某骨骼的局部偏移（持久）
        /// </summary>
        public void SetBoneLocalOffset(int bone, Vector2 offset) {
            localOffset[bone] = offset;
            hasLocalOffset[bone] = true;
        }

        /// <summary>
        /// 覆写某骨骼的静息长度（持久，Scale 为 1 的量）
        /// </summary>
        public void SetBoneLocalLength(int bone, float length) => localLength[bone] = length;

        /// <summary>
        /// 清除某骨骼的全部局部覆写，回到定义值
        /// </summary>
        public void ClearBoneOverrides(int bone) {
            localRotation[bone] = float.NaN;
            hasLocalOffset[bone] = false;
            localLength[bone] = float.NaN;
        }

        /// <summary>
        /// 只清除某骨骼的局部旋转覆写（偏移 / 长度覆写保留）
        /// </summary>
        public void ClearBoneLocalRotation(int bone) => localRotation[bone] = float.NaN;

        /// <summary>
        /// 只清除某骨骼的局部偏移覆写
        /// </summary>
        public void ClearBoneLocalOffset(int bone) => hasLocalOffset[bone] = false;

        /// <summary>
        /// 只清除某骨骼的静息长度覆写
        /// </summary>
        public void ClearBoneLocalLength(int bone) => localLength[bone] = float.NaN;

        /// <summary>
        /// 当前生效的局部旋转（覆写或定义值）
        /// </summary>
        public float LocalRotation(int bone) => float.IsNaN(localRotation[bone]) ? Definition.Bones[bone].Rotation : localRotation[bone];

        /// <summary>
        /// 当前生效的局部偏移（覆写或定义值，Scale 为 1 的量）
        /// </summary>
        public Vector2 LocalOffset(int bone) => hasLocalOffset[bone] ? localOffset[bone] : Definition.Bones[bone].Offset;

        //==================== 求解 ====================

        /// <summary>
        /// 推进一帧
        /// </summary>
        /// <param name="dt">帧步长（60fps 基准，1 = 一帧）</param>
        public void Step(float dt = 1f) {
            //版本检查必须先于空定义判定：资产首次加载失败（JSON 错 / 源文件后来才落盘）后创建的实例定义为空，
            //热重载修好文件时也要能在这里换上新定义，否则空实例永远不会重绑
            if (Asset != null && Asset.Version != boundVersion) {
                Rebind(Asset.Definition, Asset.Version);
            }
            if (Definition == null || Bones.Length == 0) {
                return;
            }
            Time += dt;
            Animation.Advance(dt);
            Animation.Apply();
            RefreshSolverDriven();

            //镜像切换：先让求解器清掉带极性的迟滞量，再按需整副硬重建
            bool mirrorSnap = false;
            if (mirrorDirty) {
                mirrorDirty = false;
                for (int i = 0; i < Solvers.Length; i++) {
                    Solvers[i]?.OnMirrorChanged();
                }
                mirrorSnap = SnapOnMirrorChange;
            }

            float snapDist = SnapDistance * Math.Max(Scale, 0.01f);
            bool teleport = hasLastRoot && Vector2.DistanceSquared(lastRootPos, RootPosition) > snapDist * snapDist;
            if (!Built || teleport || mirrorSnap) {
                SnapCore();
            }
            else {
                PropagateAll(skipSolverDriven: true);
            }
            RunSolvers(dt);

            Array.Clear(externalDriven);
            lastRootPos = RootPosition;
            hasLastRoot = true;
            LastStepTick = Main.GameUpdateCount;
            Rig2DSystem.NoteStepped(this);
        }

        /// <summary>
        /// 硬重建：静息传播全部骨骼，再让每个求解器从静息姿态直接摆好
        /// </summary>
        public void Snap() {
            if (Asset != null && Asset.Version != boundVersion) {
                Rebind(Asset.Definition, Asset.Version);
            }
            if (Definition == null || Bones.Length == 0) {
                return;
            }
            if (mirrorDirty) {
                mirrorDirty = false;
                for (int i = 0; i < Solvers.Length; i++) {
                    Solvers[i]?.OnMirrorChanged();
                }
            }
            RefreshSolverDriven();
            SnapCore();
            lastRootPos = RootPosition;
            hasLastRoot = true;
        }

        private void SnapCore() {
            PropagateAll(skipSolverDriven: false);
            for (int i = 0; i < Solvers.Length; i++) {
                Rig2DSolver s = Solvers[i];
                if (s == null || !s.Enabled) {
                    MarkSolverRan(i, false);
                    continue;
                }
                s.Snap();
                MarkSolverRan(i, true);
                PropagateAfterSolver(s);
            }
            Built = true;
        }

        private void RunSolvers(float dt) {
            for (int i = 0; i < Solvers.Length; i++) {
                Rig2DSolver s = Solvers[i];
                if (s == null || !s.Enabled) {
                    MarkSolverRan(i, false);
                    continue;
                }
                //停用后重新启用：先让它从骨骼当前位姿重新播种，再续算
                if (i < solverRan.Length && !solverRan[i]) {
                    s.OnEnabled();
                }
                s.Step(dt);
                MarkSolverRan(i, true);
                PropagateAfterSolver(s);
            }
        }

        private void MarkSolverRan(int i, bool ran) {
            if (solverRan.Length != Solvers.Length) {
                solverRan = new bool[Solvers.Length];
            }
            solverRan[i] = ran;
        }

        private void PropagateAfterSolver(Rig2DSolver s) {
            ReadOnlySpan<int> driven = s.DrivenBones;
            for (int k = 0; k < driven.Length; k++) {
                int b = driven[k];
                if (b >= 0 && b < Bones.Length) {
                    PropagateDescendants(b);
                }
            }
        }

        private void RefreshSolverDriven() {
            Array.Clear(solverDriven);
            for (int i = 0; i < Solvers.Length; i++) {
                Rig2DSolver s = Solvers[i];
                if (s == null || !s.Enabled) {
                    continue;
                }
                ReadOnlySpan<int> driven = s.DrivenBones;
                for (int k = 0; k < driven.Length; k++) {
                    int b = driven[k];
                    if (b >= 0 && b < solverDriven.Length) {
                        solverDriven[b] = true;
                    }
                }
            }
        }

        private void PropagateAll(bool skipSolverDriven) {
            int[] order = Definition.EvaluationOrder;
            for (int i = 0; i < order.Length; i++) {
                int b = order[i];
                if (externalDriven[b] || skipSolverDriven && solverDriven[b]) {
                    continue;
                }
                PropagateBone(b);
            }
        }

        /// <summary>
        /// 重传播某骨骼的全部非驱动后代（被求解器或外部接管的子骨骼及其子树跳过）
        /// </summary>
        public void PropagateDescendants(int bone) {
            int[][] children = Definition.Children;
            int top = 0;
            int[] kids = children[bone];
            for (int k = 0; k < kids.Length; k++) {
                walkStack[top++] = kids[k];
            }
            while (top > 0) {
                int b = walkStack[--top];
                if (externalDriven[b] || solverDriven[b]) {
                    continue;
                }
                PropagateBone(b);
                int[] sub = children[b];
                for (int k = 0; k < sub.Length; k++) {
                    walkStack[top++] = sub[k];
                }
            }
        }

        private void PropagateBone(int b) {
            Bone2DDef d = Definition.Bones[b];
            Vector2 anchor;
            float parDir;
            if (d.ParentIndex < 0) {
                anchor = RootPosition;
                parDir = RootRotation;
            }
            else {
                ref Bone2D p = ref Bones[d.ParentIndex];
                anchor = d.AtParentTip ? p.Tip : p.Pos;
                parDir = p.Dir;
            }
            Vector2 off = hasLocalOffset[b] ? localOffset[b] : d.Offset;
            float rot = float.IsNaN(localRotation[b]) ? d.Rotation : localRotation[b];
            float len = float.IsNaN(localLength[b]) ? d.Length : localLength[b];
            //镜像：侧向偏移与继承旋转取反，世界绝对角骨骼不动（与 RestPosition / RestDirection 同一套规则）
            float sign = MirrorSign;
            float offY = off.Y * sign;
            float cos = (float)Math.Cos(parDir);
            float sin = (float)Math.Sin(parDir);
            ref Bone2D me = ref Bones[b];
            me.Pos = new Vector2(
                anchor.X + (cos * off.X - sin * offY) * Scale,
                anchor.Y + (sin * off.X + cos * offY) * Scale);
            me.Dir = d.InheritRotation ? parDir + rot * sign : rot;
            me.Length = len * Scale;
        }

        //==================== 绘制序 ====================

        /// <summary>
        /// 按 <see cref="Piece2DState.SortKey"/> 升序排好的件索引（近乎有序时插入排序为线性）
        /// </summary>
        public ReadOnlySpan<int> SortedPieces() {
            int n = pieceOrder.Length;
            for (int i = 1; i < n; i++) {
                int cur = pieceOrder[i];
                float key = Pieces[cur].SortKey;
                int j = i - 1;
                while (j >= 0 && Pieces[pieceOrder[j]].SortKey > key) {
                    pieceOrder[j + 1] = pieceOrder[j];
                    j--;
                }
                pieceOrder[j + 1] = cur;
            }
            return pieceOrder;
        }

        /// <summary>
        /// 按 <see cref="Ribbon2DState.SortKey"/> 升序排好的带状件索引
        /// </summary>
        public ReadOnlySpan<int> SortedRibbons() {
            int n = ribbonOrder.Length;
            for (int i = 1; i < n; i++) {
                int cur = ribbonOrder[i];
                float key = Ribbons[cur].SortKey;
                int j = i - 1;
                while (j >= 0 && Ribbons[ribbonOrder[j]].SortKey > key) {
                    ribbonOrder[j + 1] = ribbonOrder[j];
                    j--;
                }
                ribbonOrder[j + 1] = cur;
            }
            return ribbonOrder;
        }
    }
}
