using System;
using System.Reflection;

namespace InnoVault.Vectors.Svg
{
    /// <summary>
    /// <see cref="VectorDocument"/> 的 <see cref="VaultLoadenAttribute"/> 加载器
    /// <br/>用法：<c>[VaultLoaden("Assets/Vectors/Emblem")] static VectorDocument Emblem;</c>，路径可省 <c>.svg</c> 扩展名
    /// <br/>只在客户端加载（纯表现层数据），服务器上成员保持默认值；加载失败得到 <see cref="VectorDocument.Empty"/> 而不是 null
    /// </summary>
    public sealed class VectorDocumentLoadenHandle : VaultLoadenHandle
    {
        /// <inheritdoc/>
        public override Type TargetType => typeof(VectorDocument);
        /// <inheritdoc/>
        public override int Priority => 10;
        /// <inheritdoc/>
        public override bool SupportArrayLoading => true;
        /// <inheritdoc/>
        public override bool LoadOnServer => false;

        /// <inheritdoc/>
        public override bool CanHandle(Type type) => type == typeof(VectorDocument);

        /// <inheritdoc/>
        public override bool CanHandleArrayElement(Type elementType) => SupportArrayLoading && CanHandle(elementType);

        /// <inheritdoc/>
        public override object GetDefaultValue(Type type) => VectorDocument.Empty;

        /// <inheritdoc/>
        public override object HandleLoad(MemberInfo member, VaultLoadenAttribute attribute) {
            if (attribute?.Mod == null || string.IsNullOrEmpty(attribute.Path)) {
                return VectorDocument.Empty;
            }
            return VectorDocument.Load(attribute.Mod, attribute.Path);
        }
    }
}
