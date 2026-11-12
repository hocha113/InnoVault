using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using Terraria.Localization;
using Terraria.WorldBuilding;

namespace InnoVault.Scenarios
{
    /// <summary>
    /// 舞台的保存方式
    /// </summary>
    public enum StagePersistence
    {
        /// <summary>按根世界长期保存，再次进入时读回上次离开时的状态；<see cref="ScenarioStage.Version"/> 变化后重新生成</summary>
        Persistent,
        /// <summary>每次进入都重新生成，只活在内存里：生成完直接进入，离开时不存盘（途中自动存档写下的文件在离开时删除）</summary>
        Transient,
    }

    /// <summary>
    /// 舞台组件：挂上它的场景拥有一整块独立的物块空间（舞台），只能经 <see cref="ScenarioDirector.Travel(VaultScenario, string, Action{VaultScenario})"/> 进入。
    /// 这里只做声明，生成与切换由平台后端执行<br/>
    /// 一个场景最多挂一个舞台组件，而且必须直接挂在场景上。入口位置由 <see cref="ScenarioAnchors"/> 的 <see cref="ScenarioAnchors.Entry"/> 决定，
    /// 没有时落在舞台出生点（生成步骤写的 <c>Main.spawnTileX/Y</c>）<br/>
    /// 生成出来的舞台直接从内存进入：生成步骤摆好的物块、液体与写进各处的状态原样保留，不经过存盘再读盘（<see cref="LoadAfterGeneration"/>）
    /// </summary>
    public class ScenarioStage : ScenarioComponent
    {
        /// <summary>原版启动时固定分配的物块空间能装下的宽度（物块）。不超过它与 <see cref="NativeHeight"/> 的舞台不需要重塑物块空间</summary>
        public const int NativeWidth = 8400;
        /// <summary>原版启动时固定分配的物块空间能装下的高度（物块）</summary>
        public const int NativeHeight = 2400;
        /// <summary>舞台宽度上限（物块）</summary>
        public const int MaxWidth = 16000;
        /// <summary>舞台高度上限（物块），含 <see cref="NoUnderworld"/> 垫在下方的行</summary>
        public const int MaxHeight = 16000;
        /// <summary>
        /// 舞台总格数上限（宽乘高），等于原版大世界的格数：超出原版尺寸的舞台只改变宽高比，不比原版多占内存
        /// </summary>
        public const int MaxArea = NativeWidth * NativeHeight;
        /// <summary>
        /// <see cref="NoUnderworld"/> 的舞台在声明的高度下方垫出的行数。原版所有按"距物块空间底部多少行"写死的地狱规则（最深的一处是 360 行）都落在这段里
        /// </summary>
        public const int UnderlayRows = 400;

        /// <summary>
        /// 声明一块指定尺寸的舞台。宽或高超过 <see cref="NativeWidth"/> × <see cref="NativeHeight"/> 时，进入这块舞台的每一端都会按它的尺寸重新分配物块空间，
        /// 离开后换回原版的分配；重新分配会造成一次明显的卡顿，由加载画面盖住
        /// </summary>
        /// <param name="width">宽度（物块），不超过 <see cref="MaxWidth"/></param>
        /// <param name="height">高度（物块），不超过 <see cref="MaxHeight"/>；宽乘高不超过 <see cref="MaxArea"/></param>
        public ScenarioStage(int width, int height) {
            Width = width;
            Height = height;
        }

        /// <summary>宽度（物块）</summary>
        public int Width { get; }

        /// <summary>声明的高度（物块），即舞台内容占用的行数。物块空间实际分配的行数见 <see cref="PhysicalHeight"/></summary>
        public int Height { get; }

        /// <summary>
        /// 这块舞台实际占用的行数，也就是身在其中时的 <c>Main.maxTilesY</c>：平时等于 <see cref="Height"/>，
        /// <see cref="NoUnderworld"/> 时多出 <see cref="UnderlayRows"/> 行
        /// </summary>
        public int PhysicalHeight => Height + (NoUnderworld ? UnderlayRows : 0);

        /// <summary>舞台内容的范围（物块坐标）：左上角在原点，宽高是声明的尺寸，不含垫在下方的行</summary>
        public Rectangle Bounds => new(0, 0, Width, Height);

        /// <summary>这块舞台是否超出了原版固定分配的物块空间（需要重塑物块空间才能装下）</summary>
        public bool ExceedsNativeSpace => Width > NativeWidth || PhysicalHeight > NativeHeight;

        /// <summary>保存方式，默认长期保存</summary>
        public StagePersistence Persistence { get; init; } = StagePersistence.Persistent;

        /// <summary>生成版本号。长期保存的舞台在版本号与上次生成时不同时会重新生成，内容更新后递增它</summary>
        public int Version { get; init; }

        /// <summary>
        /// 这块舞台的显示名，生成时写进世界名（大地图标题等显示世界名的地方看到的就是它）；为空时用场景的类型名。
        /// 长期保存的舞台沿用生成那一次的语言
        /// </summary>
        public LocalizedText DisplayName { get; init; }

        /// <summary>
        /// 生成前写入 <c>Main.worldSurface</c> 的行号，决定地表与地下背景、天光、天空区域、雨区与低重力范围；
        /// 为空时取高度的 30%。生成步骤里可以再改
        /// </summary>
        public int? SurfaceLevel { get; init; }

        /// <summary>生成前写入 <c>Main.rockLayer</c> 的行号；为空时取高度的 45%（且不高于地表层）。生成步骤里可以再改</summary>
        public int? RockLevel { get; init; }

        /// <summary>
        /// 这块舞台没有地狱层。原版把物块空间最底下的两三百行写死成地狱（区域、刷怪、背景、环境光、水的蒸发、地图配色等一百多处），
        /// 矮舞台会整张图落在里面。设为真时框架在声明的高度下方多垫 <see cref="UnderlayRows"/> 行空物块，并把世界底边留在声明的高度上：
        /// 那些写死的规则全部落在垫出来的行里，玩家、镜头与实体都到不了；声明的范围里只有地表、地下与洞穴层。
        /// 紧贴声明高度的下面铺着一行石块封底，舞台底部敞着的液体不会漏进垫出来的行<br/>
        /// 生成步骤运行时 <c>Main.maxTilesY</c> 仍是声明的高度，写法不变；进入舞台之后 <c>Main.maxTilesY</c> 是 <see cref="PhysicalHeight"/>，
        /// 运行期代码要用舞台范围时读 <see cref="Bounds"/> 或 <c>Main.bottomWorld</c>，不要用 <c>Main.maxTilesY</c> 推算底边
        /// </summary>
        public bool NoUnderworld { get; init; }

        /// <summary>生成种子；为空时由根世界身份与场景 Key 确定性派生，同一个根世界里每次生成结果相同</summary>
        public int? Seed { get; init; }

        /// <summary>
        /// 这块舞台实际用的种子，各端一致：裁决端在生成步骤执行之前（或读入已有舞台时）定下，客户端随场景的网络状态收到（激活之前）。
        /// 生成步骤与各端的表现按它取随机，不要读 <c>Main.ActiveWorldFileData</c>（客户端上是空壳）
        /// </summary>
        public int ResolvedSeed { get; internal set; }

        /// <summary>生成步骤。不重写 <see cref="BuildPasses"/> 时由它提供；两者都没有时生成一块空舞台</summary>
        public Action<List<GenPass>> Passes { get; init; }

        /// <summary>
        /// 生成后是否先存盘再从文件读回（原版新建世界的流程）。缺省为假：生成完直接从内存进入，物块与液体原样保留，世界只在生成前清空一次，
        /// 生成步骤写进各模组静态状态的内容不会被第二次清空冲掉；各模组的世界数据照常存取一遍（SaveWorldData、OnWorldLoad、LoadWorldData，在内存里完成）。
        /// 一次性舞台生成时因此不写任何文件，长期保存的舞台生成后照常存一次盘、只是不再读回。
        /// 注意一种写法：生成步骤往某个系统的列表里登记内容，该系统在 LoadWorldData 里用追加的方式读回、又只在 ClearWorld 里清空，那么这些内容会出现两遍；
        /// 读回时改成赋值，或者在 OnWorldLoad 里清空。
        /// 设为真换回存盘再读盘：多一次清空世界，物块经过原版读档时的整理（旧版物块的转换、临时物块的清除），生成期间创建的非常驻 NPC 不会留下
        /// </summary>
        public bool LoadAfterGeneration { get; init; }

        /// <summary>
        /// 进入这块舞台时是否做一次全图的液体沉降（原版读档末尾的那一步，悬空的液体会落下来并摊平）。为空时自动：
        /// 场景自己作用于整块舞台的规则设了 <see cref="ScenarioRules.FreezeLiquids"/> 时不沉降，否则沉降。
        /// 不沉降时液体按生成步骤摆放的（或存档里的）样子留在原处
        /// </summary>
        public bool? SettleLiquids { get; init; }

        /// <summary>
        /// 玩家倒下期间被带到这块舞台时，是否在抵达的那一刻直接复活在落点上，缺省为真。为假时复活倒计时接着走，走完后按这块舞台的复活规则复活
        /// </summary>
        public bool ReviveOnArrival { get; init; } = true;

        /// <summary>
        /// 从这块舞台回主世界、又没有指定落点时用的主世界锚点名；为空时回到离开主世界之前的位置。
        /// 要落在出生点（床位或世界出生点）填 <see cref="ScenarioAnchors.Spawn"/>
        /// </summary>
        public string ExitAnchor { get; init; }

        internal bool BindsRootWorld { get; private init; }

        /// <summary>收集生成步骤，默认调用 <see cref="Passes"/>。只在需要生成时由平台后端调用</summary>
        /// <param name="passes">按执行顺序填入的生成步骤</param>
        protected virtual void BuildPasses(List<GenPass> passes) => Passes?.Invoke(passes);

        internal void CollectPasses(List<GenPass> passes) => BuildPasses(passes);

        //种子只有裁决端算得出来（客户端没有世界文件），随实例状态发给客户端
        /// <inheritdoc/>
        protected override void NetSend(BinaryWriter writer) => writer.Write(ResolvedSeed);

        /// <inheritdoc/>
        protected override void NetReceive(BinaryReader reader) => ResolvedSeed = reader.ReadInt32();

        //根场景绑定菜单选中的世界文件，尺寸由文件决定
        internal static ScenarioStage ForRoot() => new(0, 0) { BindsRootWorld = true };

        internal string ValidateStage() {
            if (BindsRootWorld) {
                return null;
            }
            int physical = PhysicalHeight;
            if (Width <= 0 || Height <= 0 || Width > MaxWidth || physical > MaxHeight) {
                return NoUnderworld
                    ? $"stage size {Width}x{Height} (+{UnderlayRows} underlay rows) is outside 1..{MaxWidth} x 1..{MaxHeight}"
                    : $"stage size {Width}x{Height} is outside 1..{MaxWidth} x 1..{MaxHeight}";
            }
            if ((long)Width * physical > MaxArea) {
                return $"stage size {Width}x{physical} has more than {MaxArea} tiles";
            }
            if (ExceedsNativeSpace && !ScenarioPlatform.CanResizeTileSpace) {
                return $"stage size {Width}x{physical} needs a resized tile space (more than {NativeWidth}x{NativeHeight}), which is unavailable on this endpoint";
            }
            if (SurfaceLevel is int surface && (surface <= 0 || surface >= Height)) {
                return $"SurfaceLevel {surface} is outside 1..{Height - 1}";
            }
            if (RockLevel is int rock && (rock <= 0 || rock >= Height)) {
                return $"RockLevel {rock} is outside 1..{Height - 1}";
            }
            if (Scenario?.GetComponent<ScenarioAnchors>() is ScenarioAnchors anchors) {
                foreach (var pair in anchors.Points) {
                    if (pair.Value.X < 0 || pair.Value.Y < 0 || pair.Value.X >= Width || pair.Value.Y >= Height) {
                        return $"anchor '{pair.Key}' {pair.Value} is outside the stage";
                    }
                }
            }
            return null;
        }

        internal int ResolveSurfaceLevel() => SurfaceLevel ?? (int)(Height * 0.3);

        internal int ResolveRockLevel() => Math.Max(RockLevel ?? (int)(Height * 0.45), ResolveSurfaceLevel());

        //进入时要不要做液体沉降：显式声明为准；否则看场景自己作用于整块舞台的规则节点里最后一个表态的 FreezeLiquids
        internal bool ResolveSettleLiquids() {
            if (SettleLiquids is bool declared) {
                return declared;
            }
            bool frozen = false;
            if (Scenario != null) {
                foreach (ScenarioNode node in Scenario.AllNodes) {
                    if (node is ScenarioRules { FreezeLiquids: bool value } rules && rules.ScopeArea == null && rules.EnabledInTree) {
                        frozen = value;
                    }
                }
            }
            return !frozen;
        }
    }
}
