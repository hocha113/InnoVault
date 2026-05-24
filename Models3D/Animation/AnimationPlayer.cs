using InnoVault.Models3D.Runtime;
using InnoVault.Models3D.Skinning;
using Microsoft.Xna.Framework;
using System;

namespace InnoVault.Models3D.Animation
{
    /// <summary>
    /// 实例级的动画播放头
    /// <br/>负责推进时间、采样 <see cref="Model3DAnimationClip"/> 到所属 <see cref="Vault3DModel"/> 的每个骨架，
    /// 并把结果写入 <see cref="SkinningPalette"/>，供渲染器读取做 CPU 蒙皮
    /// <br/>支持两段 Clip 之间的线性 cross-fade：调用 <see cref="Play(string, float, bool)"/> 传入
    /// <c>fadeIn &gt; 0</c> 即可在 <c>fadeIn</c> 秒内从旧姿态过渡到新姿态
    /// <br/>调用方可选择让渲染器自动驱动时间（默认）或自己调 <see cref="Tick(float)"/>
    /// </summary>
    public sealed class AnimationPlayer
    {
        /// <summary>
        /// 关联的模型
        /// <br/>必须含 <see cref="Vault3DModel.Skeletons"/>，否则播放无效但不抛异常
        /// </summary>
        public Vault3DModel Model { get; }

        /// <summary>
        /// 当前正在播放的 Clip；未调用 <see cref="Play(string, float, bool)"/> 时为 <see langword="null"/>
        /// </summary>
        public Model3DAnimationClip Current { get; private set; }
        /// <summary>
        /// 当前 Clip 已播放的时间，单位秒
        /// <br/>Loop 时会按 <see cref="Model3DAnimationClip.Duration"/> 取模
        /// </summary>
        public float Time;
        /// <summary>
        /// 播放速率
        /// <br/>负值用于反向播放，例如卷轴回卷
        /// </summary>
        public float Speed = 1f;
        /// <summary>
        /// 是否循环
        /// <br/>false 时播放到末尾会停在末帧
        /// </summary>
        public bool Loop = true;
        /// <summary>
        /// 是否暂停
        /// <br/>true 时 <see cref="Update(float)"/> 不推进时间，但 <see cref="Tick(float)"/> 仍然有效
        /// </summary>
        public bool Paused;

        /// <summary>
        /// 上一段 Clip，仅在 cross-fade 期间非空
        /// </summary>
        public Model3DAnimationClip Previous { get; private set; }
        /// <summary>
        /// 上一段 Clip 的播放时间（cross-fade 期间继续推进，营造"上一段未停"的效果）
        /// </summary>
        public float PreviousTime;
        /// <summary>
        /// 上一段 Clip 的速率
        /// </summary>
        public float PreviousSpeed = 1f;
        /// <summary>
        /// 上一段 Clip 是否循环
        /// </summary>
        public bool PreviousLoop = true;
        /// <summary>
        /// Cross-fade 总时长，单位秒
        /// </summary>
        public float FadeDuration;
        /// <summary>
        /// Cross-fade 已经走过的时间
        /// </summary>
        public float FadeElapsed;

        /// <summary>
        /// 是否处于 cross-fade 阶段
        /// </summary>
        public bool IsFading => Previous != null && FadeDuration > 0f && FadeElapsed < FadeDuration;

        //每个骨架一个调色板，索引与 Vault3DModel.Skeletons 对齐
        private readonly SkinningPalette[] _palettes;
        //每个骨架一份 scratch：当前帧采样得到的 TRS（按 joint 顺序）
        private readonly Vector3[][] _curTranslations;
        private readonly Quaternion[][] _curRotations;
        private readonly Vector3[][] _curScales;
        private readonly Vector3[][] _prevTranslations;
        private readonly Quaternion[][] _prevRotations;
        private readonly Vector3[][] _prevScales;
        //本地矩阵与世界矩阵 scratch
        private readonly Matrix[][] _localMatrices;
        private readonly Matrix[][] _globalMatrices;

        /// <summary>
        /// 构造一个绑定到给定模型的播放头
        /// <br/>会按模型骨架数量预分配 scratch；不立即开始播放
        /// </summary>
        /// <param name="model">关联模型；不能为 <see langword="null"/></param>
        public AnimationPlayer(Vault3DModel model) {
            Model = model ?? throw new ArgumentNullException(nameof(model));
            int skeletonCount = Model.Skeletons.Count;
            _palettes = new SkinningPalette[skeletonCount];
            _curTranslations = new Vector3[skeletonCount][];
            _curRotations = new Quaternion[skeletonCount][];
            _curScales = new Vector3[skeletonCount][];
            _prevTranslations = new Vector3[skeletonCount][];
            _prevRotations = new Quaternion[skeletonCount][];
            _prevScales = new Vector3[skeletonCount][];
            _localMatrices = new Matrix[skeletonCount][];
            _globalMatrices = new Matrix[skeletonCount][];
            for (int s = 0; s < skeletonCount; s++) {
                Model3DSkeleton skel = Model.Skeletons[s];
                int jointCount = skel.JointCount;
                _palettes[s] = new SkinningPalette(skel);
                _curTranslations[s] = new Vector3[jointCount];
                _curRotations[s] = new Quaternion[jointCount];
                _curScales[s] = new Vector3[jointCount];
                _prevTranslations[s] = new Vector3[jointCount];
                _prevRotations[s] = new Quaternion[jointCount];
                _prevScales[s] = new Vector3[jointCount];
                _localMatrices[s] = new Matrix[jointCount];
                _globalMatrices[s] = new Matrix[jointCount];
            }
        }

        /// <summary>
        /// 拿到指定骨架的调色板
        /// <br/>渲染器按 <see cref="Model3DMeshGroup.SkinIndex"/> 读取
        /// </summary>
        /// <param name="skeletonIndex">骨架索引</param>
        /// <returns>调色板；索引越界时返回 <see langword="null"/></returns>
        public SkinningPalette GetPalette(int skeletonIndex) {
            if (skeletonIndex < 0 || skeletonIndex >= _palettes.Length) {
                return null;
            }
            return _palettes[skeletonIndex];
        }

        /// <summary>
        /// 按名称播放 Clip
        /// </summary>
        /// <param name="clipName">Clip 名（区分大小写）</param>
        /// <param name="fadeIn">从当前姿态线性 cross-fade 到新 Clip 的秒数；0 表示立即切换</param>
        /// <param name="restart">即使是同一 Clip 也强制从头播</param>
        /// <returns>是否成功切换到给定 Clip</returns>
        public bool Play(string clipName, float fadeIn = 0f, bool restart = false) {
            if (string.IsNullOrEmpty(clipName) || Model == null) {
                return false;
            }
            if (!Model.TryGetClip(clipName, out Model3DAnimationClip clip)) {
                return false;
            }
            Play(clip, fadeIn, restart);
            return true;
        }

        /// <summary>
        /// 直接播放给定 Clip
        /// <br/>当 <paramref name="fadeIn"/> &gt; 0 时，先把当前姿态（Clip + Time + Speed）拍快照到 Previous，
        /// 再切换 Current。哪怕新旧 Clip 是同一段，也会触发 cross-fade，
        /// 用来实现"调头继续播"等场景（先以旧 Speed 继续衰减，新 Speed 同步淡入）
        /// </summary>
        /// <param name="clip">目标 Clip</param>
        /// <param name="fadeIn">cross-fade 时长</param>
        /// <param name="restart">是否强制重置 Time（包括同 Clip 切换）</param>
        public void Play(Model3DAnimationClip clip, float fadeIn = 0f, bool restart = false) {
            if (clip == null) {
                return;
            }

            //无 fade 时切到同一 Clip 且不要求 restart：保持当前状态不动
            if (fadeIn <= 0f && Current == clip && !restart) {
                return;
            }

            if (fadeIn > 0f && Current != null) {
                Previous = Current;
                PreviousTime = Time;
                PreviousSpeed = Speed;
                PreviousLoop = Loop;
                FadeDuration = fadeIn;
                FadeElapsed = 0f;
            }
            else {
                Previous = null;
                FadeDuration = 0f;
                FadeElapsed = 0f;
            }
            //同 Clip 时若未要求 restart 则保留 Time，体感上是"在原位置继续推进"
            if (Current != clip || restart) {
                Time = 0f;
            }
            Current = clip;
        }

        /// <summary>
        /// 停止播放并清理所有状态
        /// <br/>下次绘制将退回 bind pose
        /// </summary>
        public void Stop() {
            Current = null;
            Previous = null;
            Time = 0f;
            PreviousTime = 0f;
            FadeDuration = 0f;
            FadeElapsed = 0f;
        }

        /// <summary>
        /// 按 <see cref="Speed"/> 推进时间；<see cref="Paused"/> 为 true 时不推进
        /// <br/>由 <see cref="Runtime.Model3DRenderer.DrawInstance"/> 在每帧自动调用一次
        /// </summary>
        /// <param name="deltaSeconds">距离上一帧的秒数</param>
        public void Update(float deltaSeconds) {
            if (Paused) {
                return;
            }
            Tick(deltaSeconds);
        }

        /// <summary>
        /// 不论暂停状态，按 <see cref="Speed"/> 推进时间
        /// <br/>用于外部逻辑驱动；调用方需保证不会与 <see cref="Update(float)"/> 同帧叠加导致双倍速
        /// </summary>
        /// <param name="deltaSeconds">推进的秒数</param>
        public void Tick(float deltaSeconds) {
            if (Current == null) {
                return;
            }
            Time = AdvanceTime(Time, deltaSeconds * Speed, Current.Duration, Loop);
            if (Previous != null) {
                PreviousTime = AdvanceTime(PreviousTime, deltaSeconds * PreviousSpeed, Previous.Duration, PreviousLoop);
                if (FadeDuration > 0f) {
                    FadeElapsed += MathF.Abs(deltaSeconds);
                    if (FadeElapsed >= FadeDuration) {
                        Previous = null;
                        FadeDuration = 0f;
                        FadeElapsed = 0f;
                    }
                }
                else {
                    Previous = null;
                }
            }
        }

        /// <summary>
        /// 把当前姿态采样写入每个骨架的 <see cref="SkinningPalette"/>
        /// <br/>必须在 <see cref="Update(float)"/> / <see cref="Tick(float)"/> 之后调用，
        /// 渲染器会在每帧绘制前自动完成；外部一般无需手动调
        /// </summary>
        public void SamplePose() {
            int skeletonCount = _palettes.Length;
            float fadeWeight = IsFading ? MathHelper.Clamp(FadeElapsed / FadeDuration, 0f, 1f) : 1f;

            for (int s = 0; s < skeletonCount; s++) {
                Model3DSkeleton skel = Model.Skeletons[s];
                FillBindPose(skel, _curTranslations[s], _curRotations[s], _curScales[s]);
                ApplyClipToScratch(s, Current, Time, _curTranslations[s], _curRotations[s], _curScales[s]);

                if (Previous != null && fadeWeight < 1f) {
                    FillBindPose(skel, _prevTranslations[s], _prevRotations[s], _prevScales[s]);
                    ApplyClipToScratch(s, Previous, PreviousTime, _prevTranslations[s], _prevRotations[s], _prevScales[s]);
                    BlendScratch(skel.JointCount, _prevTranslations[s], _prevRotations[s], _prevScales[s]
                        , _curTranslations[s], _curRotations[s], _curScales[s], fadeWeight);
                }

                BuildLocalAndGlobal(skel, _curTranslations[s], _curRotations[s], _curScales[s]
                    , _localMatrices[s], _globalMatrices[s]);
                BuildPalette(skel, _globalMatrices[s], _palettes[s]);
            }
        }

        private static void FillBindPose(Model3DSkeleton skel, Vector3[] t, Quaternion[] r, Vector3[] sc) {
            for (int j = 0; j < skel.JointCount; j++) {
                Model3DJoint joint = skel.Joints[j];
                t[j] = joint.BindTranslation;
                r[j] = joint.BindRotation;
                sc[j] = joint.BindScale;
            }
        }

        private static void ApplyClipToScratch(int skeletonIndex, Model3DAnimationClip clip, float time
            , Vector3[] t, Quaternion[] r, Vector3[] sc) {
            if (clip == null) {
                return;
            }
            Model3DAnimationChannel[] channels = clip.Channels;
            for (int c = 0; c < channels.Length; c++) {
                Model3DAnimationChannel ch = channels[c];
                if (ch == null || ch.SkeletonIndex != skeletonIndex) {
                    continue;
                }
                int j = ch.JointIndex;
                if ((uint)j >= (uint)t.Length) {
                    continue;
                }
                switch (ch.Path) {
                    case Model3DAnimationPath.Translation:
                        t[j] = ch.Sampler.SampleVec3(time);
                        break;
                    case Model3DAnimationPath.Rotation:
                        Quaternion q = ch.Sampler.SampleQuat(time);
                        if (q.LengthSquared() > 1e-8f) {
                            q.Normalize();
                        }
                        else {
                            q = Quaternion.Identity;
                        }
                        r[j] = q;
                        break;
                    case Model3DAnimationPath.Scale:
                        sc[j] = ch.Sampler.SampleVec3(time);
                        break;
                }
            }
        }

        //prev * (1 - w) + cur * w，注意旋转用 Slerp
        private static void BlendScratch(int count
            , Vector3[] prevT, Quaternion[] prevR, Vector3[] prevS
            , Vector3[] curT, Quaternion[] curR, Vector3[] curS, float w) {
            for (int j = 0; j < count; j++) {
                curT[j] = Vector3.Lerp(prevT[j], curT[j], w);
                curS[j] = Vector3.Lerp(prevS[j], curS[j], w);
                curR[j] = Quaternion.Slerp(prevR[j], curR[j], w);
            }
        }

        private static void BuildLocalAndGlobal(Model3DSkeleton skel
            , Vector3[] t, Quaternion[] r, Vector3[] sc
            , Matrix[] local, Matrix[] global) {
            int count = skel.JointCount;
            for (int j = 0; j < count; j++) {
                local[j] = Matrix.CreateScale(sc[j]) * Matrix.CreateFromQuaternion(r[j]) * Matrix.CreateTranslation(t[j]);
            }
            //按 EvaluationOrder 遍历可以保证父矩阵先被计算
            int[] order = skel.EvaluationOrder;
            Matrix[] prefixes = skel.RootAncestorMatrices;
            for (int k = 0; k < order.Length; k++) {
                int j = order[k];
                int parent = skel.ParentIndices[j];
                if (parent < 0 || parent >= count) {
                    //root joint：起点是场景图中"非 joint 祖先链"的世界矩阵
                    //——XNA row-vector 约定下，global = local * prefix，保证蒙皮结果与 IBM 同处 scene-root 空间
                    Matrix prefix = prefixes != null && (uint)j < (uint)prefixes.Length ? prefixes[j] : Matrix.Identity;
                    global[j] = local[j] * prefix;
                }
                else {
                    global[j] = local[j] * global[parent];
                }
            }
        }

        private static void BuildPalette(Model3DSkeleton skel, Matrix[] global, SkinningPalette palette) {
            Matrix[] ibm = skel.InverseBindMatrices;
            Matrix[] dst = palette.Matrices;
            int count = skel.JointCount;
            for (int j = 0; j < count; j++) {
                dst[j] = ibm[j] * global[j];
            }
        }

        private static float AdvanceTime(float current, float delta, float duration, bool loop) {
            if (duration <= 0f) {
                return 0f;
            }
            float next = current + delta;
            if (loop) {
                next %= duration;
                if (next < 0f) {
                    next += duration;
                }
                return next;
            }
            if (next < 0f) {
                return 0f;
            }
            if (next > duration) {
                return duration;
            }
            return next;
        }
    }
}
