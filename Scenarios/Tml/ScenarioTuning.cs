namespace InnoVault.Scenarios
{
    //换舞台后端的开发者开关，缺省值就是正式行为。/vaultdebug scenario option 可以在运行时切换，实机排查时用来对照两条路径；
    //只影响切换之后发起的换舞台，模组重新加载后回到缺省值
    internal static class ScenarioTuning
    {
        //生成出来的舞台直接从内存进入；关掉后一律先存盘再从文件读回
        internal static bool DirectEntry = true;

        //单人换舞台沿用内存里的角色对象；关掉后每次换舞台都从存档重新读取角色
        internal static bool KeepPlayer = true;

        internal static void Reset() {
            DirectEntry = true;
            KeepPlayer = true;
        }
    }
}
