using System;
using System.Reflection;

namespace InnoVault.Videos
{
    /// <summary>
    /// <see cref="VaultVideo"/> 的 <see cref="VaultLoadenAttribute"/> 加载器
    /// <br/>用法：<c>[VaultLoaden("Assets/Videos/Intro")] static VaultVideo Intro;</c>，路径可省 <c>.ogv</c> 扩展名
    /// <br/>只在客户端加载（纯表现层数据），服务器上成员保持 <see cref="VaultVideo.Empty"/>；加载失败得到 <see cref="VaultVideo.IsEmpty"/> 为真的实例而不是 null
    /// </summary>
    public sealed class VaultVideoLoadenHandle : VaultLoadenHandle
    {
        /// <inheritdoc/>
        public override Type TargetType => typeof(VaultVideo);
        /// <inheritdoc/>
        public override int Priority => 10;
        /// <inheritdoc/>
        public override bool SupportArrayLoading => true;
        /// <inheritdoc/>
        public override bool LoadOnServer => false;

        /// <inheritdoc/>
        public override bool CanHandle(Type type) => type == typeof(VaultVideo);

        /// <inheritdoc/>
        public override bool CanHandleArrayElement(Type elementType) => SupportArrayLoading && CanHandle(elementType);

        /// <inheritdoc/>
        public override object GetDefaultValue(Type type) => VaultVideo.Empty;

        /// <inheritdoc/>
        public override object HandleLoad(MemberInfo member, VaultLoadenAttribute attribute) {
            if (attribute?.Mod == null || string.IsNullOrEmpty(attribute.Path)) {
                return VaultVideo.Empty;
            }
            return VaultVideo.Load(attribute.Mod, attribute.Path);
        }
    }
}
