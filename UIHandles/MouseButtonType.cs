namespace InnoVault.UIHandles
{
    /// <summary>
    /// 鼠标按键类型，用于<see cref="UIHandle"/>中区分不同的鼠标输入<br/>
    /// 配合<see cref="KeyPressState"/>可以实现统一的"哪个键、何种状态"判断
    /// </summary>
    public enum MouseButtonType
    {
        /// <summary>
        /// 鼠标左键
        /// </summary>
        Left,
        /// <summary>
        /// 鼠标右键
        /// </summary>
        Right,
        /// <summary>
        /// 鼠标中键（滚轮按下）
        /// </summary>
        Middle,
    }
}
