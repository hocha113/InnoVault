namespace InnoVault.Vectors
{
    /// <summary>
    /// 矢量绘图模块的生命周期：卸载时释放 <see cref="VectorRenderer"/> 自建的 GPU 资源并清空 SVG 解析缓存
    /// </summary>
    internal sealed class VectorLoader : IVaultLoader
    {
        void IVaultLoader.UnLoadData() {
            VectorRenderer.Unload();
            VectorPath.ClearSvgCache();
        }
    }
}
