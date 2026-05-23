using Terraria;

namespace InnoVault.Models3D.Runtime
{
    /// <summary>
    /// Models3D 子系统的生命周期挂载点
    /// <br/>主要负责在模组卸载时释放渲染器持有的 GPU 资源
    /// </summary>
    internal sealed class Model3DSystem : IVaultLoader
    {
        void IVaultLoader.UnLoadData() {
            //先在卸载线程清空静态订阅，避免下次加载时仍持有外部 mod 的委托引用
            Model3DRenderer.ClearAllSubscriptions();

            //客户端持有的 BasicEffect / RT 由渲染器自身管理；服务器无 GPU 资源
            if (Main.dedServ) {
                return;
            }

            Main.QueueMainThreadAction(() => {
                Model3DRenderer.ClearPersistent();
                Model3DRenderer.Instance?.DisposeEffect();
                Model3DRenderer.Instance?.DisposeRenderTarget();
            });
        }
    }
}
