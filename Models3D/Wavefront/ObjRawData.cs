using Microsoft.Xna.Framework;
using System.Collections.Generic;

namespace InnoVault.Models3D.Wavefront
{
    /// <summary>
    /// 表示一个 OBJ 面顶点上的索引三元组（位置/UV/法线）
    /// <br/>OBJ 中 -1 表示该分量不存在
    /// </summary>
    public readonly struct ObjFaceVertex
    {
        /// <summary>
        /// 位置索引（0 基），不存在时为 -1
        /// </summary>
        public int Position { get; }
        /// <summary>
        /// 纹理坐标索引（0 基），不存在时为 -1
        /// </summary>
        public int TexCoord { get; }
        /// <summary>
        /// 法线索引（0 基），不存在时为 -1
        /// </summary>
        public int Normal { get; }

        /// <summary>
        /// 构造一个面顶点索引三元组
        /// </summary>
        public ObjFaceVertex(int position, int texCoord, int normal) {
            Position = position;
            TexCoord = texCoord;
            Normal = normal;
        }
    }

    /// <summary>
    /// 表示一个 OBJ 面，包含若干顶点（已被解析器规整为三角形或四边形）
    /// <br/>同一个面内所有顶点引用同一个材质
    /// </summary>
    public sealed class ObjFace
    {
        /// <summary>
        /// 面顶点列表（长度 >= 3）
        /// </summary>
        public ObjFaceVertex[] Vertices { get; }
        /// <summary>
        /// 该面引用的材质名（可能为空字符串）
        /// </summary>
        public string MaterialName { get; }

        /// <summary>
        /// 构造一个 OBJ 面
        /// </summary>
        public ObjFace(ObjFaceVertex[] vertices, string materialName) {
            Vertices = vertices;
            MaterialName = materialName ?? string.Empty;
        }
    }

    /// <summary>
    /// OBJ 解析的纯数据中间表示
    /// <br/>仅记录 OBJ 文本中出现过的内容，不做坐标系转换、不去重，也不构造 GPU 顶点
    /// </summary>
    public sealed class ObjRawData
    {
        /// <summary>
        /// OBJ 中的所有 <c>v</c> 顶点位置（按声明顺序）
        /// </summary>
        public List<Vector3> Positions { get; } = new();
        /// <summary>
        /// OBJ 中的所有 <c>vt</c> 纹理坐标
        /// </summary>
        public List<Vector2> TexCoords { get; } = new();
        /// <summary>
        /// OBJ 中的所有 <c>vn</c> 法线
        /// </summary>
        public List<Vector3> Normals { get; } = new();
        /// <summary>
        /// OBJ 中的所有面
        /// </summary>
        public List<ObjFace> Faces { get; } = new();
        /// <summary>
        /// OBJ 引用过的所有 MTL 文件相对路径（来自 <c>mtllib</c>）
        /// </summary>
        public List<string> MaterialLibraries { get; } = new();
    }
}
