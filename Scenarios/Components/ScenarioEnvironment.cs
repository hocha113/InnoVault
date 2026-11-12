using Microsoft.Xna.Framework;
using Terraria.ModLoader;

namespace InnoVault.Scenarios
{
    /// <summary>
    /// 环境组件：音乐、背景、水样式、天空、天光与亮度，以及小舞台的涂黑修正与地狱层视觉的取舍<br/>
    /// 音乐、背景、水样式经一个内部的 <see cref="ModSceneEffect"/> 生效，场景效果优先级取设置了这几项的最上层的 <see cref="Priority"/>。
    /// 作用范围随节点树：挂在某片 <see cref="ScenarioArea"/> 下面，或者场景有主区域时，只在本地玩家进入那片区域后生效<br/>
    /// 多个生效中的场景都挂了它时逐属性叠加（<see cref="ScenarioNode.LayerPriority"/>），<see cref="ModifySunLight"/> 从下到上依次调用。
    /// 属性在运行期间修改即时生效
    /// </summary>
    public class ScenarioEnvironment : ScenarioComponent
    {
        /// <summary>音乐槽位，为空表示不改</summary>
        public int? Music { get; set; }

        /// <summary>地表背景，为空表示不改</summary>
        public ModSurfaceBackgroundStyle SurfaceBackground { get; set; }

        /// <summary>地下背景，为空表示不改</summary>
        public ModUndergroundBackgroundStyle UndergroundBackground { get; set; }

        /// <summary>水样式，为空表示不改</summary>
        public ModWaterStyle WaterStyle { get; set; }

        /// <summary>音乐、背景、水样式这一场景效果的优先级</summary>
        public SceneEffectPriority Priority { get; set; } = SceneEffectPriority.BiomeHigh;

        /// <summary><c>SkyManager</c> 的天空键，生效期间激活、失效时停用；为空表示不改</summary>
        public string Sky { get; set; }

        /// <summary>亮度倍数（<c>ModifyLightingBrightness</c>），为空表示不改</summary>
        public float? LightingBrightness { get; set; }

        /// <summary>
        /// 涂黑整个可见范围。原版 <c>Main.DrawBlack</c> 只涂两段行域，屏幕顶部落在舞台下半、地狱层之上时中间一段永远不涂黑，
        /// <c>worldSurface</c> 压得很低的小舞台里无光物块后面会透出天空。各层都没设置时自动：场景舞台的 <c>worldSurface</c> 低于舞台一半高度时开启
        /// </summary>
        public bool? FullRangeBlackout { get; set; }

        /// <summary>
        /// 把地狱层（物块空间最底下 200 行左右）的视觉全部藏起来：背景黑底、地狱背景画、地狱环境光与涂黑阈值。
        /// 它是下面四个属性的总开关：那四个没有单独设置时取它的值；连它也没设置时各自按自动规则。
        /// 它只管画面，可以挂在区域下面只对那一片生效；要整块舞台彻底没有地狱层（含刷怪、液体等全部规则），用 <see cref="ScenarioStage.NoUnderworld"/>
        /// </summary>
        public bool? HideUnderworld { get; set; }

        /// <summary>
        /// 取消地狱行（最底下 200 行）的涂黑亮度阈值抬升。整幅涂黑时这些行的昏暗照明会被切出成片黑块。
        /// 没设置时取 <see cref="HideUnderworld"/>，再没有时与 <see cref="FullRangeBlackout"/> 的结果相同
        /// </summary>
        public bool? NoUnderworldBlackThreshold { get; set; }

        /// <summary>
        /// 取消地狱背景的黑底。原版 <c>Main.DrawBackground</c> 把"高度 − 330 行"（按 <c>worldSurface</c> 对齐）以下当作地狱背景，
        /// 屏幕底边越过那一行时在物块后面铺满一整屏黑底；矮舞台里那一行紧贴地表，站在地面上天空就整片变黑。
        /// 没设置时取 <see cref="HideUnderworld"/>，再没有时自动：场景舞台里那一行离 <c>worldSurface</c> 不足 100 行时取消
        /// </summary>
        public bool? NoUnderworldBackdrop { get; set; }

        /// <summary>
        /// 不画地狱背景画（屏幕底边靠近物块空间底部 220 行时原版画的那几层熔岩远景）。
        /// 没设置时取 <see cref="HideUnderworld"/>，再没有时与 <see cref="NoUnderworldBackdrop"/> 的结果相同
        /// </summary>
        public bool? NoUnderworldBackground { get; set; }

        /// <summary>
        /// 取消地狱行的环境光（原版给最底下 200 行的空格补的那层暗红底光），这些行改由物块、墙与光源自己照亮。
        /// 没设置时取 <see cref="HideUnderworld"/>，再没有时与 <see cref="NoUnderworldBackdrop"/> 的结果相同
        /// </summary>
        public bool? NoUnderworldLight { get; set; }

        //设置了场景效果（音乐、背景、水样式）中的任意一项
        internal bool SetsSceneEffect => Music.HasValue || SurfaceBackground != null || UndergroundBackground != null || WaterStyle != null;

        /// <summary>调整天光颜色（<c>ModifySunLightColor</c>）</summary>
        public virtual void ModifySunLight(ref Color tileColor, ref Color backgroundColor) { }
    }
}
