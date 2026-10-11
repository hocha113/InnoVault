using Microsoft.Xna.Framework;
using Terraria.ModLoader;

namespace InnoVault.Scenarios
{
    /// <summary>
    /// 环境组件：音乐、背景、水样式、天空、天光与亮度，以及小舞台的涂黑修正<br/>
    /// 音乐、背景、水样式经一个内部的 <see cref="ModSceneEffect"/> 生效，场景效果优先级取设置了这几项的最上层的 <see cref="Priority"/>。
    /// 场景挂了 <see cref="ScenarioArea"/> 时，只在本地玩家进入区域后生效<br/>
    /// 多个生效中的场景都挂了它时逐属性叠加（<see cref="ScenarioComponent.LayerPriority"/>），<see cref="ModifySunLight"/> 从下到上依次调用。
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
        /// 取消地狱行（最底下 200 行）的涂黑亮度阈值抬升。整幅涂黑时这些行的昏暗照明会被切出成片黑块。各层都没设置时与 <see cref="FullRangeBlackout"/> 的结果相同
        /// </summary>
        public bool? NoUnderworldBlackThreshold { get; set; }

        /// <summary>
        /// 取消地狱背景的黑底。原版 <c>Main.DrawBackground</c> 把"高度 − 330 行"（按 <c>worldSurface</c> 对齐）以下当作地狱背景，
        /// 屏幕底边越过那一行时在物块后面铺满一整屏黑底；矮舞台里那一行紧贴地表，站在地面上天空就整片变黑。
        /// 各层都没设置时自动：场景舞台里那一行离 <c>worldSurface</c> 不足 100 行时取消
        /// </summary>
        public bool? NoUnderworldBackdrop { get; set; }

        //设置了场景效果（音乐、背景、水样式）中的任意一项
        internal bool SetsSceneEffect => Music.HasValue || SurfaceBackground != null || UndergroundBackground != null || WaterStyle != null;

        /// <summary>调整天光颜色（<c>ModifySunLightColor</c>）</summary>
        public virtual void ModifySunLight(ref Color tileColor, ref Color backgroundColor) { }
    }
}
