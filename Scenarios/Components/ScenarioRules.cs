using System.Collections.Generic;
using Terraria;
using Terraria.ModLoader;

namespace InnoVault.Scenarios
{
    /// <summary>
    /// 回城物品与死亡复活后，玩家落到哪里
    /// </summary>
    public enum ScenarioSpawnBehavior
    {
        /// <summary>原版行为（床位或世界出生点）</summary>
        Vanilla,
        /// <summary>落到锚点（<see cref="ScenarioRules.RecallAnchor"/> / <see cref="ScenarioRules.RespawnAnchor"/>，缺省 <see cref="ScenarioAnchors.Entry"/>）；找不到时按原版处理</summary>
        Anchor,
        /// <summary>离开场景舞台，返回主世界</summary>
        ReturnHome,
    }

    /// <summary>
    /// 运行规则组件：时间、物块更新、天气与事件、刷怪、城镇 NPC、重力、区域判定、回城与复活落点<br/>
    /// 可以挂在拥有舞台的场景上，也可以挂在区域场景或不占空间的场景上（例如一场遭遇战期间关闭自然刷怪）。
    /// 场景挂了 <see cref="ScenarioArea"/> 时，只在本地玩家进入区域后生效<br/>
    /// 多个生效中的场景都挂了它时逐属性叠加（<see cref="ScenarioComponent.LayerPriority"/>）：每个属性取最上层已设置（不为空）的值，
    /// 虚方法从下到上依次调用。属性在运行期间修改即时生效；主世界里没有任何场景挂它时，所有行为与原版一致
    /// </summary>
    public class ScenarioRules : ScenarioComponent
    {
        /// <summary>时间流速倍数，乘在原版当前流速上；0 表示冻结时间，为空表示不改</summary>
        public double? TimeRate { get; set; }

        /// <summary>每帧强制写入 <c>Main.time</c>，为空表示不强制</summary>
        public double? FixedTime { get; set; }

        /// <summary>每帧强制写入 <c>Main.dayTime</c>，为空表示不强制</summary>
        public bool? FixedDayTime { get; set; }

        /// <summary>关闭随机物块更新（草与腐化蔓延、植物生长等）；电路与物块实体照常运行。与旅途模式"冻结时间"是同一机制</summary>
        public bool? FreezeTiles { get; set; }

        /// <summary>关闭天气与事件的计时（下雨开始与结束、坠星、云层与风的推进）</summary>
        public bool? FreezeEventTimers { get; set; }

        /// <summary>每帧停止下雨与沙尘暴</summary>
        public bool? NoWeather { get; set; }

        /// <summary>
        /// 不发生事件：昼夜交替时不开始血月、日食、入侵等，每帧结束已在进行的血月、日食、南瓜月、霜月、入侵、史莱姆雨、派对、灯笼夜，并取消待落的陨石
        /// </summary>
        public bool? NoEvents { get; set; }

        /// <summary>城镇 NPC 不会入住</summary>
        public bool? NoTownNPCs { get; set; }

        /// <summary>关闭自然刷怪（最大刷怪数为 0）</summary>
        public bool? NoNaturalSpawns { get; set; }

        /// <summary>重力倍数，相对 <c>Player.defaultGravity</c>，覆盖原版高空低重力；为空表示不改</summary>
        public float? Gravity { get; set; }

        /// <summary>
        /// 取消海洋判定：海滩区域、海洋背景、海洋音乐、海洋刷怪、火把运气里的海洋计数。各层都没设置时自动：场景舞台宽度不足 760 格
        /// （原版海滩宽度 380 格是只读常量，这样窄的舞台地表全判海洋）时取消；为 <see langword="false"/> 时保持原版
        /// </summary>
        public bool? NoOceanZone { get; set; }

        /// <summary>取消地狱高度区域判定（最底下 200 行），该处改判洞穴层。只影响区域标志，地狱背景与光照仍按原版</summary>
        public bool? NoUnderworldZone { get; set; }

        /// <summary>取消太空高度区域判定，该处改判地表</summary>
        public bool? NoSpaceZone { get; set; }

        /// <summary>使用回城物品（魔镜、回忆药水等）后的落点，为空表示不改</summary>
        public ScenarioSpawnBehavior? RecallBehavior { get; set; }

        /// <summary>死亡复活后的落点，为空表示不改</summary>
        public ScenarioSpawnBehavior? RespawnBehavior { get; set; }

        /// <summary><see cref="RecallBehavior"/> 为 <see cref="ScenarioSpawnBehavior.Anchor"/> 时用的锚点名，为空时取 <see cref="ScenarioAnchors.Entry"/></summary>
        public string RecallAnchor { get; set; }

        /// <summary><see cref="RespawnBehavior"/> 为 <see cref="ScenarioSpawnBehavior.Anchor"/> 时用的锚点名，为空时取 <see cref="ScenarioAnchors.Entry"/></summary>
        public string RespawnAnchor { get; set; }

        /// <summary>原版算完高度区域与海滩之后、生物群落与场景效果之前调用，可以在这里改写玩家的区域标志</summary>
        /// <param name="player">正在更新区域的玩家</param>
        public virtual void ModifyZones(Player player) { }

        /// <summary>调整刷怪频率与上限，在 <see cref="NoNaturalSpawns"/> 之后调用</summary>
        public virtual void EditSpawnRate(Player player, ref int spawnRate, ref int maxSpawns) { }

        /// <summary>调整刷怪池</summary>
        public virtual void EditSpawnPool(IDictionary<int, float> pool, NPCSpawnInfo spawnInfo) { }
    }
}
