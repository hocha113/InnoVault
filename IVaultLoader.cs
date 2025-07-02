namespace InnoVault
{
    /// <summary>
    /// 提供一个通用的资源加载、卸载途径
    /// </summary>
    public interface IVaultLoader
    {
        /// <summary>
        /// 该方法在Load中的最后调用，并且不会在服务器上调用，一般用于加载Asset客户端资源
        /// </summary>
        public void LoadAsset() { }
        /// <summary>
        /// 该方法在Load中的最后调用
        /// </summary>
        public void SetupData() { }
        /// <summary>
        /// 添加配方
        /// </summary>
        public void AddRecipesData() { }
        /// <summary>
        /// 该方法在Load前行调用
        /// </summary>
        public void LoadData() { }
        /// <summary>
        /// 该方法在UnLoad最后调用
        /// </summary>
        public void UnLoadData() { }
    }
}
