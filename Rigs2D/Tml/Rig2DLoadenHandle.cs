using System;
using System.Reflection;

namespace InnoVault.Rigs2D.Runtime
{
    /// <summary>
    /// <see cref="Vault2DRig"/> 的 <see cref="VaultLoadenAttribute"/> 加载器
    /// <br/>用法：<c>[VaultLoaden("Assets/Rigs/SeaShrimp")] static Vault2DRig Rig;</c>，路径可省 <c>.rig.json</c> 扩展名
    /// <br/>两端加载（<see cref="LoadOnServer"/>）：骨架定义是判定几何（螯尖、足端）的来源，专用服务器也要有；
    /// 服务器上只读 JSON、不取贴图（<see cref="Vault2DRig.Load"/> 内部按 <c>Main.dedServ</c> 跳过贴图解析与热重载登记）
    /// </summary>
    public sealed class Rig2DLoadenHandle : VaultLoadenHandle
    {
        /// <inheritdoc/>
        public override Type TargetType => typeof(Vault2DRig);
        /// <inheritdoc/>
        public override int Priority => 10;
        /// <inheritdoc/>
        public override bool SupportArrayLoading => true;
        /// <inheritdoc/>
        public override bool LoadOnServer => true;

        /// <inheritdoc/>
        public override bool CanHandle(Type type) => type == typeof(Vault2DRig);

        /// <inheritdoc/>
        public override bool CanHandleArrayElement(Type elementType) => SupportArrayLoading && CanHandle(elementType);

        /// <inheritdoc/>
        public override object GetDefaultValue(Type type) => Vault2DRig.Empty;

        /// <inheritdoc/>
        public override object HandleLoad(MemberInfo member, VaultLoadenAttribute attribute) {
            if (attribute?.Mod == null || string.IsNullOrEmpty(attribute.Path)) {
                return Vault2DRig.Empty;
            }
            return Vault2DRig.Load(attribute.Mod, attribute.Path);
        }
    }
}
