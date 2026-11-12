using System;
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
        /// <summary>
        /// 离开场景舞台，返回主世界，落在 <see cref="ScenarioRules.RecallAnchor"/> / <see cref="ScenarioRules.RespawnAnchor"/> 指定的主世界锚点上
        /// （为空时按这次换舞台的缺省落点）。用在复活上时，单人模式下玩家不会先在原地站起来：复活倒计时走完就直接换舞台，到了那边再复活<br/>
        /// 多人模式下这是那个玩家发给服务器的一次换舞台请求，放行后所有玩家一起回主世界；
        /// 一个人倒下要不要带走所有人，在被离开的那块舞台的 <see cref="VaultScenario.AuthorizeRequest"/> 里决定（请求的 <see cref="ScenarioRequest.From"/> 就是它）
        /// </summary>
        ReturnHome,
        /// <summary>
        /// 换到另一块舞台：目标是 <see cref="ScenarioRules.RecallDestination"/> / <see cref="ScenarioRules.RespawnDestination"/>，
        /// 落点是 <see cref="ScenarioRules.RecallAnchor"/> / <see cref="ScenarioRules.RespawnAnchor"/>（为空时按这次换舞台的缺省落点）。
        /// 没有指定目标时按原版处理。复活时的表现与多人模式下的请求方式同 <see cref="ReturnHome"/>
        /// </summary>
        Travel,
    }

    /// <summary>
    /// 运行规则组件：时间、世界模拟（物块更新、液体、电路、物块实体）、天气与事件、刷怪、城镇 NPC、重力、区域判定、回城与复活落点<br/>
    /// 可以挂在拥有舞台的场景上，也可以挂在区域场景或不占空间的场景上（例如一场遭遇战期间关闭自然刷怪）。
    /// 作用范围随节点树：挂在某片 <see cref="ScenarioArea"/> 下面，或者场景有主区域时，只对身在那片区域里的玩家生效（<see cref="ScenarioNode.ScopeArea"/>）<br/>
    /// 多个生效中的场景都挂了它时逐属性叠加（<see cref="ScenarioNode.LayerPriority"/>）：每个属性取最上层已设置（不为空）的值，
    /// 虚方法从下到上依次调用。属性在运行期间修改即时生效；主世界里没有任何场景挂它时，所有行为与原版一致<br/>
    /// 多人模式下分两类：刷怪、重力、区域判定、回城与复活落点按玩家分别取值，区域里的只作用于身在区域里的玩家；
    /// 时间、世界模拟、天气、事件、城镇 NPC 是整块舞台共享的状态，由服务器决定，而服务器只听作用于整块舞台的规则节点。
    /// 运行期间改属性不会自动同步：在每一端用同样的条件改，或者把几套规则各写成一个节点、用 <see cref="ScenarioNode.Enabled"/> 切换（开关会同步）
    /// </summary>
    public class ScenarioRules : ScenarioComponent
    {
        /// <summary>时间流速倍数，乘在原版当前流速上；0 表示冻结时间，为空表示不改</summary>
        public double? TimeRate { get; set; }

        /// <summary>每帧强制写入 <c>Main.time</c>，为空表示不强制</summary>
        public double? FixedTime { get; set; }

        /// <summary>每帧强制写入 <c>Main.dayTime</c>，为空表示不强制</summary>
        public bool? FixedDayTime { get; set; }

        /// <summary>关闭随机物块更新（草与腐化蔓延、植物生长等），与旅途模式"冻结时间"是同一机制。液体、电路与物块实体各有自己的开关</summary>
        public bool? FreezeTiles { get; set; }

        /// <summary>
        /// 液体停摆：水、岩浆、蜂蜜、微光不再流动、沉降与蒸发，按生成或摆放时的样子留在原处；停摆期间新倒进来的液体也不会流开。
        /// 拥有舞台的场景设了它时，进入这块舞台也不做液体沉降（<see cref="ScenarioStage.SettleLiquids"/>）
        /// </summary>
        public bool? FreezeLiquids { get; set; }

        /// <summary>电路计时停摆：计时器不再走，机关的冷却不再推进；开关、压力板这类主动触发的电路照常传导</summary>
        public bool? FreezeWiring { get; set; }

        /// <summary>物块实体停摆：原版与模组的物块实体（<c>TileEntity</c>）不再逐帧更新，它们的摆放、移除与交互不受影响</summary>
        public bool? FreezeTileEntities { get; set; }

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

        /// <summary>
        /// 重力倍数，为空表示不改。作用于玩家（相对 <c>Player.defaultGravity</c>）、NPC 与掉落物，并取消原版随高度衰减的高空低重力；
        /// 弹幕的重力由各自的 AI 决定，不在其内。玩家按自己所在的层取值，NPC 与掉落物按各自所在的位置取值
        /// </summary>
        public float? Gravity { get; set; }

        /// <summary>
        /// 取消海洋判定：海滩区域、海洋背景、海洋音乐、海洋刷怪、火把运气里的海洋计数。各层都没设置时自动：场景舞台宽度不足 760 格
        /// （原版海滩宽度 380 格是只读常量，这样窄的舞台地表全判海洋）时取消；为 <see langword="false"/> 时保持原版
        /// </summary>
        public bool? NoOceanZone { get; set; }

        /// <summary>
        /// 取消地狱高度区域判定（最底下 200 行），该处改判洞穴层；设在作用于整块舞台的规则上时，水在这些行里也不再蒸发。
        /// 地狱的背景与光照归 <see cref="ScenarioEnvironment.HideUnderworld"/> 管；要整块舞台彻底没有地狱层，用 <see cref="ScenarioStage.NoUnderworld"/>
        /// </summary>
        public bool? NoUnderworldZone { get; set; }

        /// <summary>取消太空高度区域判定，该处改判地表</summary>
        public bool? NoSpaceZone { get; set; }

        /// <summary>使用回城物品（魔镜、回忆药水等）后的落点，为空表示不改</summary>
        public ScenarioSpawnBehavior? RecallBehavior { get; set; }

        /// <summary>死亡复活后的落点，为空表示不改</summary>
        public ScenarioSpawnBehavior? RespawnBehavior { get; set; }

        /// <summary>
        /// <see cref="RecallBehavior"/> 用的锚点名。<see cref="ScenarioSpawnBehavior.Anchor"/> 时是当前舞台里的锚点，为空取 <see cref="ScenarioAnchors.Entry"/>；
        /// <see cref="ScenarioSpawnBehavior.ReturnHome"/> / <see cref="ScenarioSpawnBehavior.Travel"/> 时是目的地舞台里的落点锚点，为空按那次换舞台的缺省落点
        /// （回到出生点用 <see cref="ScenarioAnchors.Spawn"/>）
        /// </summary>
        public string RecallAnchor { get; set; }

        /// <summary><see cref="RespawnBehavior"/> 用的锚点名，含义同 <see cref="RecallAnchor"/></summary>
        public string RespawnAnchor { get; set; }

        /// <summary><see cref="RecallBehavior"/> 为 <see cref="ScenarioSpawnBehavior.Travel"/> 时要换去的舞台场景类型（<see cref="VaultScenario"/> 的子类）</summary>
        public Type RecallDestination { get; set; }

        /// <summary><see cref="RespawnBehavior"/> 为 <see cref="ScenarioSpawnBehavior.Travel"/> 时要换去的舞台场景类型（<see cref="VaultScenario"/> 的子类）</summary>
        public Type RespawnDestination { get; set; }

        /// <summary>
        /// 一套“世界停摆”的规则：随机物块更新、液体、电路计时、物块实体与天气事件计时全部停下，适合布景式的舞台。
        /// 时间、刷怪等其余属性在返回的对象上接着设置
        /// </summary>
        public static ScenarioRules Still() => new() {
            FreezeTiles = true,
            FreezeLiquids = true,
            FreezeWiring = true,
            FreezeTileEntities = true,
            FreezeEventTimers = true,
        };

        /// <summary>原版算完高度区域与海滩之后、生物群落与场景效果之前调用，可以在这里改写玩家的区域标志</summary>
        /// <param name="player">正在更新区域的玩家</param>
        public virtual void ModifyZones(Player player) { }

        /// <summary>调整刷怪频率与上限，在 <see cref="NoNaturalSpawns"/> 之后调用</summary>
        public virtual void EditSpawnRate(Player player, ref int spawnRate, ref int maxSpawns) { }

        /// <summary>调整刷怪池</summary>
        public virtual void EditSpawnPool(IDictionary<int, float> pool, NPCSpawnInfo spawnInfo) { }

        /// <inheritdoc/>
        protected override string Validate() {
            foreach (Type destination in new[] { RecallDestination, RespawnDestination }) {
                if (destination != null && !typeof(VaultScenario).IsAssignableFrom(destination)) {
                    return $"{destination.FullName} is not a VaultScenario";
                }
            }
            return null;
        }
    }
}
