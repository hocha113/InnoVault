using InnoVault.Models3D.Runtime;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;

namespace InnoVault.Models3D.Skinning
{
    /// <summary>
    /// 单个 <see cref="Model3DInstance"/> 持有的蒙皮 scratch 缓冲
    /// <br/>按 mesh group 缓存一份"蒙皮后顶点数组"，让同模型的多个实例可以拥有独立姿态
    /// <br/>仅供框架内部使用；外部代码无需关心
    /// </summary>
    internal sealed class InstanceSkinScratch
    {
        private readonly Dictionary<Model3DMeshGroup, VertexPositionNormalTexture[]> _groupBuffers = new();

        /// <summary>
        /// 取或创建给定 mesh group 的蒙皮目标缓冲
        /// <br/>返回的数组长度与 <see cref="Model3DMeshGroup.BindVertices"/> 一致
        /// </summary>
        /// <param name="group">蒙皮目标 group</param>
        /// <returns>可写顶点缓冲；若 group 缺少 bind 数据返回 <see langword="null"/></returns>
        public VertexPositionNormalTexture[] GetOrCreateGroupBuffer(Model3DMeshGroup group) {
            if (group == null) {
                return null;
            }
            VertexPositionNormalTexture[] bind = group.BindVertices;
            if (bind == null || bind.Length == 0) {
                return null;
            }
            if (_groupBuffers.TryGetValue(group, out VertexPositionNormalTexture[] cached)
                && cached != null && cached.Length == bind.Length) {
                return cached;
            }
            VertexPositionNormalTexture[] fresh = new VertexPositionNormalTexture[bind.Length];
            _groupBuffers[group] = fresh;
            return fresh;
        }
    }
}
