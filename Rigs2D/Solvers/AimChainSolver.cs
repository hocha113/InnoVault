using InnoVault.Rigs2D.Data;
using InnoVault.Rigs2D.Runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json.Linq;
using System;

namespace InnoVault.Rigs2D.Solvers
{
    /// <summary>
    /// 瞄准链：把一路或几路瞄准量（弧度，朝向系：面向右时正 = 顺时针 = 低头 / 前俯，镜像自动翻）按逐骨权重分到一串骨上，
    /// 等价于在这些骨的局部旋转上加 Σ 输入 × 权重；输入为负时可以换一套权重（例如只有仰头时胸腰才跟着后倒）。
    /// 每骨的附加角求和后按限位钳制。本轮已被前面的求解器解过的骨从那份解续写
    /// <br/>骨骼：<c>[链首, …, 链尾]</c>，父在前（中间可以隔着不参与的骨）
    /// <br/>参数：<c>inputs</c> [{ <c>name</c>, <c>weights</c> [逐骨], <c>weightsUp</c> [逐骨，输入为负时用，缺省同 weights] }]
    /// （缺省为一路 <c>aim</c>，权重取顶层 <c>weights</c> / <c>weightsUp</c>，都缺时整份给链尾）；<c>gain</c> 1；
    /// <c>limitMin</c> / <c>limitMax</c>（逐骨附加角下 / 上限，弧度；<c>limitMinDeg</c> / <c>limitMaxDeg</c> 用度；同为 0 = 不限）
    /// <br/>点模式 <c>mode</c> "point"：<c>target</c> 世界点，以 <c>pointBone</c>（链内序号或骨名，缺省链尾）加瞄准前的轴向
    /// 偏 <c>pointOffset</c>（弧度，朝向系；<c>pointOffsetDeg</c> 用度）为零，算出朝向系夹角 × <c>pointWeight</c> 1 并入 <c>pointInput</c>（缺省第一路）；
    /// <c>maxAim</c> 0 = 不钳
    /// <br/>通道属性：各路输入名（标量）、<c>gain</c>、<c>pointWeight</c>、<c>target</c>（空间量）
    /// </summary>
    public sealed class AimChainSolver : Rig2DSolver
    {
        private sealed class AimInput
        {
            public string Name;
            public float[] Down;
            public float[] Up;
            public float Value;
        }

        private AimInput[] inputs = [];
        private float[] limitMin = [];
        private float[] limitMax = [];
        private float[] applied = [];
        private float[] extraWorld = [];
        private bool pointMode;
        private int pointBone;
        private int pointInput;
        private float pointOffset;
        private float maxAim;
        private readonly Rig2DChainLayout chain = new();

        /// <summary>
        /// 整体增益（逐帧可改；Configure 时重置为参数值）
        /// </summary>
        public float Gain { get; set; } = 1f;
        /// <summary>
        /// 点模式夹角并入输入时的权重
        /// </summary>
        public float PointWeight { get; set; } = 1f;
        /// <summary>
        /// 点模式的世界目标点
        /// </summary>
        public Vector2 Target { get; set; }
        /// <summary>
        /// 点模式本帧算出的朝向系夹角（加权前）
        /// </summary>
        public float PointAim { get; private set; }
        /// <summary>
        /// 本帧逐骨实际附加的角（朝向系，钳制后）
        /// </summary>
        public ReadOnlySpan<float> Applied => applied;
        /// <summary>
        /// 输入路数
        /// </summary>
        public int InputCount => inputs.Length;

        /// <summary>
        /// 按名取输入序号，缺失 <c>-1</c>
        /// </summary>
        public int InputIndex(string name) {
            for (int i = 0; i < inputs.Length; i++) {
                if (inputs[i].Name == name) {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// 写一路输入（弧度，朝向系）
        /// </summary>
        public void SetInput(int index, float value) {
            if ((uint)index < (uint)inputs.Length) {
                inputs[index].Value = value;
            }
        }

        /// <summary>
        /// 按名写一路输入
        /// </summary>
        public void SetInput(string name, float value) => SetInput(InputIndex(name), value);

        /// <summary>
        /// 读一路输入
        /// </summary>
        public float GetInput(int index) => (uint)index < (uint)inputs.Length ? inputs[index].Value : 0f;

        /// <inheritdoc/>
        protected override void Configure(Solver2DDef def) {
            int n = bones.Length;
            float[] fallback = new float[n];
            if (n > 0) {
                fallback[n - 1] = 1f;
            }
            if (def.Params?["inputs"] is JArray arr && arr.Count > 0) {
                AimInput[] list = new AimInput[arr.Count];
                for (int i = 0; i < arr.Count; i++) {
                    JObject o = arr[i] as JObject;
                    float[] down = Weights(o?["weights"] as JArray, n, fallback);
                    list[i] = new AimInput {
                        Name = (string)o?["name"] ?? (i == 0 ? "aim" : "aim" + i),
                        Down = down,
                        Up = Weights(o?["weightsUp"] as JArray, n, down),
                        Value = FindOld((string)o?["name"]),
                    };
                }
                inputs = list;
            }
            else {
                float[] down = def.Has("weights") ? def.GetFloatArray("weights", n, 0f) : fallback;
                float[] up = def.Has("weightsUp") ? def.GetFloatArray("weightsUp", n, 0f) : down;
                inputs = [new AimInput { Name = "aim", Down = down, Up = up, Value = inputs.Length > 0 ? inputs[0].Value : 0f }];
            }
            limitMin = Limits(def, "limitMin", n);
            limitMax = Limits(def, "limitMax", n);
            applied = new float[n];
            extraWorld = new float[n];
            Gain = def.GetFloat("gain", 1f);
            PointWeight = def.GetFloat("pointWeight", 1f);
            pointMode = def.GetString("mode", "angle").ToLowerInvariant() == "point";
            string pb = def.GetString("pointBone", null);
            pointBone = n - 1;
            if (!string.IsNullOrEmpty(pb)) {
                int bi = Rig?.Definition?.BoneIndex(pb) ?? -1;
                pointBone = Array.IndexOf(bones, bi);
            }
            else if (def.Has("pointBone")) {
                pointBone = Math.Clamp(def.GetInt("pointBone", n - 1), 0, Math.Max(n - 1, 0));
            }
            string pi = def.GetString("pointInput", null);
            pointInput = string.IsNullOrEmpty(pi) ? 0 : Math.Max(InputIndex(pi), 0);
            pointOffset = def.GetAngle("pointOffset", 0f);
            maxAim = Math.Max(def.GetAngle("maxAim", 0f), 0f);
            chain.Bind(Rig, bones);
        }

        //热重载时按名保留旧输入值
        private float FindOld(string name) {
            if (name == null) {
                return 0f;
            }
            foreach (AimInput old in inputs) {
                if (old.Name == name) {
                    return old.Value;
                }
            }
            return 0f;
        }

        private static float[] Weights(JArray arr, int n, float[] fallback) {
            if (arr == null) {
                return (float[])fallback.Clone();
            }
            float[] w = new float[n];
            for (int i = 0; i < n && i < arr.Count; i++) {
                JToken t = arr[i];
                w[i] = t.Type is JTokenType.Float or JTokenType.Integer ? (float)t : 0f;
            }
            return w;
        }

        private static float[] Limits(Solver2DDef def, string key, int n) {
            if (def.Has(key + "Deg")) {
                float[] deg = def.GetFloatArray(key + "Deg", n, 0f);
                for (int i = 0; i < deg.Length; i++) {
                    deg[i] = MathHelper.ToRadians(deg[i]);
                }
                return deg;
            }
            return def.GetFloatArray(key, n, 0f);
        }

        /// <inheritdoc/>
        protected internal override int ChannelProperty(string prop, out bool spatial) {
            spatial = prop == "target";
            switch (prop) {
                case "target":
                    return 0;
                case "gain":
                    return 1;
                case "pointWeight":
                    return 2;
            }
            int i = InputIndex(prop);
            return i >= 0 ? 3 + i : -1;
        }

        /// <inheritdoc/>
        protected internal override void SetChannel(int property, Vector2 value) {
            switch (property) {
                case 0:
                    Target = value;
                    return;
                case 1:
                    Gain = value.X;
                    return;
                case 2:
                    PointWeight = value.X;
                    return;
            }
            SetInput(property - 3, value.X);
        }

        /// <inheritdoc/>
        public override void Snap() => Solve();

        /// <inheritdoc/>
        public override void Step(float dt) => Solve();

        private void Solve() {
            int n = bones.Length;
            if (n == 0) {
                return;
            }
            chain.Capture(Rig);
            float sign = MirrorSign;
            float point = 0f;
            PointAim = 0f;
            if (pointMode && (uint)pointBone < (uint)n && bones[pointBone] >= 0) {
                ref Bone2D pb = ref B(pointBone);
                Vector2 d = Target - pb.Pos;
                if (d.LengthSquared() > 0.25f) {
                    float axis = pb.Dir + pointOffset * sign;
                    float a = MathHelper.WrapAngle(MathF.Atan2(d.Y, d.X) - axis) * sign;
                    if (maxAim > 0f) {
                        a = MathHelper.Clamp(a, -maxAim, maxAim);
                    }
                    PointAim = a;
                    point = a * PointWeight;
                }
            }
            for (int k = 0; k < n; k++) {
                float add = 0f;
                for (int i = 0; i < inputs.Length; i++) {
                    float v = inputs[i].Value;
                    if (pointMode && i == pointInput) {
                        v += point;
                    }
                    if (v != 0f) {
                        add += v * (v < 0f ? inputs[i].Up[k] : inputs[i].Down[k]);
                    }
                }
                add *= Gain;
                if (limitMin[k] != 0f || limitMax[k] != 0f) {
                    add = MathHelper.Clamp(add, Math.Min(limitMin[k], limitMax[k]), Math.Max(limitMin[k], limitMax[k]));
                }
                applied[k] = add;
                extraWorld[k] = add * sign;
            }
            chain.Lay(Rig, extraWorld, Vector2.Zero);
        }

        /// <inheritdoc/>
        public override void DebugDraw(SpriteBatch sb, Func<Vector2, Vector2> toScreen) {
            if (!pointMode) {
                return;
            }
            Texture2D px = Rig2DDebugDraw.Pixel;
            if (px == null) {
                return;
            }
            sb.Draw(px, toScreen(Target), new Rectangle(0, 0, 1, 1), Color.LimeGreen, 0f, new Vector2(0.5f), 5f, SpriteEffects.None, 0f);
        }
    }
}
