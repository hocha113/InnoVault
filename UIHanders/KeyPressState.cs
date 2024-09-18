namespace InnoVault.UIHanders
{
    /// <summary>
    /// 表示按键的状态
    /// </summary>
    public enum KeyPressState
    {
        /// <summary>
        /// 按键未按下或保持不变
        /// </summary>
        None = 0,
        /// <summary>
        /// 按键刚按下
        /// </summary>
        Pressed = 1,
        /// <summary>
        /// 按键刚松开
        /// </summary>
        Released = 2,
        /// <summary>
        /// 持续按下按键
        /// </summary>
        Held = 3
    }
}
