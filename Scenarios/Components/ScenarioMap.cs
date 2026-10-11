namespace InnoVault.Scenarios
{
    /// <summary>
    /// 地图策略组件<br/>
    /// <see cref="NoMap"/> 跟随玩家：场景挂了 <see cref="ScenarioArea"/> 时只在本地玩家进入区域后生效，多层叠加时取最上层已设置的值。
    /// 关闭地图期间不改玩家设置文件，失效后恢复玩家原来的地图开关<br/>
    /// <see cref="RevealMap"/> 在场景开始时（<c>OnStart</c>）揭开一次，范围是场景区域的外接框，没有区域时是整块舞台；
    /// 代价与面积成正比，只建议小舞台或小区域用
    /// </summary>
    public class ScenarioMap : ScenarioComponent
    {
        /// <summary>关闭大地图、小地图与地图记录，为空表示不改</summary>
        public bool? NoMap { get; set; }

        /// <summary>场景开始时揭开范围内的地图</summary>
        public bool RevealMap { get; init; }

        /// <inheritdoc/>
        protected override void OnStart() {
            if (RevealMap) {
                ScenarioPlatform.RevealMap?.Invoke(this);
            }
        }
    }
}
