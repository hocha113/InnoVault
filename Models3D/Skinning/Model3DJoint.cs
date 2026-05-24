using Microsoft.Xna.Framework;

namespace InnoVault.Models3D.Skinning
{
    /// <summary>
    /// 骨架中的一根骨骼描述
    /// <br/>仅保存"加载阶段从节点 TRS 拿到的 bind pose 默认值"，运行时不会被修改
    /// <br/>动画采样时若某个通道缺失（例如只动 translation 不动 rotation），就用这里的 bind 值兜底
    /// </summary>
    public struct Model3DJoint
    {
        /// <summary>
        /// 骨骼名称
        /// <br/>来源 glTF 节点 <c>name</c>，主要用于调试
        /// </summary>
        public string Name;
        /// <summary>
        /// bind pose 的本地平移
        /// <br/>来自加载时节点的 translation；缺失时为 <see cref="Vector3.Zero"/>
        /// </summary>
        public Vector3 BindTranslation;
        /// <summary>
        /// bind pose 的本地旋转
        /// <br/>来自加载时节点的 rotation；缺失时为 <see cref="Quaternion.Identity"/>
        /// </summary>
        public Quaternion BindRotation;
        /// <summary>
        /// bind pose 的本地缩放
        /// <br/>来自加载时节点的 scale；缺失时为 <see cref="Vector3.One"/>
        /// </summary>
        public Vector3 BindScale;
    }
}
