namespace InnoVault.Cinematics
{
    /// <summary>
    /// 演出系统的加载器，屏幕位置接入由 <see cref="CutscenePlayer"/> 执行，这里负责生命周期清理
    /// </summary>
    public sealed class CutsceneLoader : IVaultLoader
    {
        void IVaultLoader.LoadData() {
            if (!VaultUtils.isServer) {
                CutsceneDirector.Reset();
            }
        }

        void IVaultLoader.UnLoadData() {
            CutsceneDirector.Reset();
        }
    }
}
