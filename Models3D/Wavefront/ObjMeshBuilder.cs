using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;

namespace InnoVault.Models3D.Wavefront
{
    /// <summary>
    /// 把 <see cref="ObjRawData"/> 与材质表组合成 GPU 可用的 <see cref="ObjMeshGroup"/> 列表
    /// <br/>负责坐标系转换、缺失法线生成、UV 翻转、按材质分桶、唯一顶点去重、包围盒统计
    /// </summary>
    public static class ObjMeshBuilder
    {
        /// <summary>
        /// 顶点拆分阈值上限：受 16 位索引限制，每个分组最多 65535 个唯一顶点
        /// </summary>
        public const int MaxVerticesPerGroup = 65535;

        /// <summary>
        /// 构造网格分组
        /// </summary>
        /// <param name="raw">OBJ 原始数据</param>
        /// <param name="materials">材质表（可为空字典）</param>
        /// <param name="options">导入选项</param>
        /// <param name="diagnostic">诊断收集器</param>
        /// <param name="source">用于诊断输出的源标识</param>
        /// <param name="bounds">输出整个模型的包围盒</param>
        /// <returns>按材质分组后的网格列表</returns>
        public static List<ObjMeshGroup> Build(ObjRawData raw, IReadOnlyDictionary<string, ObjMaterial> materials
            , ObjImportOptions options, ObjDiagnostic diagnostic, string source, out BoundingBox bounds) {
            bounds = new BoundingBox(Vector3.Zero, Vector3.Zero);
            if (raw == null || raw.Faces.Count == 0) {
                return new List<ObjMeshGroup>();
            }

            options ??= ObjImportOptions.Default;
            diagnostic ??= new ObjDiagnostic();

            //按材质名分桶面索引
            Dictionary<string, List<ObjFace>> facesByMaterial = new();
            for (int i = 0; i < raw.Faces.Count; i++) {
                ObjFace face = raw.Faces[i];
                if (!facesByMaterial.TryGetValue(face.MaterialName, out List<ObjFace> list)) {
                    list = new List<ObjFace>();
                    facesByMaterial[face.MaterialName] = list;
                }
                list.Add(face);
            }

            List<ObjMeshGroup> groups = new();
            Vector3 min = new Vector3(float.PositiveInfinity);
            Vector3 max = new Vector3(float.NegativeInfinity);
            bool boundsTouched = false;

            foreach (KeyValuePair<string, List<ObjFace>> bucket in facesByMaterial) {
                BuildBucket(bucket.Key, bucket.Value, raw, materials, options, diagnostic, source
                    , groups, ref min, ref max, ref boundsTouched);
            }

            if (boundsTouched) {
                bounds = new BoundingBox(min, max);
            }

            return groups;
        }

        private static void BuildBucket(string materialName, List<ObjFace> faces, ObjRawData raw
            , IReadOnlyDictionary<string, ObjMaterial> materials, ObjImportOptions options
            , ObjDiagnostic diagnostic, string source
            , List<ObjMeshGroup> groups, ref Vector3 min, ref Vector3 max, ref bool boundsTouched) {

            ObjMaterial material = null;
            if (!string.IsNullOrEmpty(materialName) && materials != null) {
                materials.TryGetValue(materialName, out material);
                if (material == null) {
                    diagnostic.Warn(source, 0, $"Material '{materialName}' referenced by face but not declared in any MTL");
                }
            }

            //顶点去重：(positionIndex, texCoordIndex, normalIndex) -> 已分配 zero-based 输出索引
            //由于面数据可能较大，先估算容量
            Dictionary<long, int> dedup = new(faces.Count * 3);
            List<VertexPositionNormalTexture> verts = new(faces.Count * 3);
            List<short> indices = new(faces.Count * 3);

            int positionCount = raw.Positions.Count;
            int texCoordCount = raw.TexCoords.Count;
            int normalCount = raw.Normals.Count;

            int splitGroupIndex = 0;

            for (int f = 0; f < faces.Count; f++) {
                ObjFace face = faces[f];
                if (face.Vertices == null || face.Vertices.Length != 3) {
                    diagnostic.Warn(source, 0, "Non-triangle face encountered after triangulation, skipped");
                    continue;
                }

                short[] triIndices = new short[3];
                Vector3[] triPositions = new Vector3[3];
                bool faceValid = true;

                for (int i = 0; i < 3; i++) {
                    ObjFaceVertex fv = face.Vertices[i];
                    if (fv.Position < 0 || fv.Position >= positionCount) {
                        diagnostic.Warn(source, 0, $"Face references invalid position index {fv.Position}, face skipped");
                        faceValid = false;
                        break;
                    }

                    Vector3 rawPos = raw.Positions[fv.Position];
                    Vector3 pos = options.ApplyAxis(rawPos);
                    triPositions[i] = pos;

                    Vector2 uv = Vector2.Zero;
                    if (fv.TexCoord >= 0 && fv.TexCoord < texCoordCount) {
                        uv = raw.TexCoords[fv.TexCoord];
                        if (options.FlipTextureV) {
                            uv.Y = 1f - uv.Y;
                        }
                    }

                    Vector3 normal = Vector3.Zero;
                    bool hasExplicitNormal = false;
                    if (fv.Normal >= 0 && fv.Normal < normalCount) {
                        normal = options.ApplyAxisNormal(raw.Normals[fv.Normal]);
                        hasExplicitNormal = true;
                    }

                    long key = BuildKey(fv.Position, fv.TexCoord, fv.Normal);
                    if (!dedup.TryGetValue(key, out int newIndex)) {
                        if (verts.Count >= MaxVerticesPerGroup) {
                            diagnostic.Error(source, 0
                                , $"Material '{materialName}' exceeds 65535 unique vertices, mesh splitting; remaining faces dropped");
                            //简化处理：超出 16-bit 索引上限时直接结束当前面循环
                            faceValid = false;
                            break;
                        }
                        verts.Add(new VertexPositionNormalTexture(pos, hasExplicitNormal ? normal : Vector3.Zero, uv));
                        newIndex = verts.Count - 1;
                        dedup[key] = newIndex;
                    }
                    triIndices[i] = (short)newIndex;

                    //累计包围盒
                    if (!boundsTouched) {
                        min = pos;
                        max = pos;
                        boundsTouched = true;
                    }
                    else {
                        min = Vector3.Min(min, pos);
                        max = Vector3.Max(max, pos);
                    }
                }

                if (!faceValid) {
                    if (verts.Count >= MaxVerticesPerGroup) {
                        //立即结束此分组的累积，剩下的面不再处理
                        break;
                    }
                    continue;
                }

                indices.Add(triIndices[0]);
                indices.Add(triIndices[1]);
                indices.Add(triIndices[2]);

                //如果该面没有任何显式法线，根据顶点位置计算面法线，并补到对应顶点
                if (options.GenerateMissingNormals) {
                    bool needGenerate = false;
                    for (int i = 0; i < 3; i++) {
                        if (face.Vertices[i].Normal < 0 || face.Vertices[i].Normal >= normalCount) {
                            needGenerate = true;
                            break;
                        }
                    }
                    if (needGenerate) {
                        Vector3 faceNormal = ComputeFaceNormal(triPositions[0], triPositions[1], triPositions[2]);
                        for (int i = 0; i < 3; i++) {
                            int vIdx = triIndices[i];
                            VertexPositionNormalTexture v = verts[vIdx];
                            //仅在原法线为 0 时填充，避免覆盖共享顶点已经写入的有效法线
                            if (v.Normal.LengthSquared() <= 1e-6f) {
                                v.Normal = faceNormal;
                                verts[vIdx] = v;
                            }
                        }
                    }
                }

                splitGroupIndex++;
            }

            //收尾：把任何仍然为零向量的法线置为 +Z，避免 BasicEffect 启用光照时全黑
            for (int i = 0; i < verts.Count; i++) {
                VertexPositionNormalTexture v = verts[i];
                if (v.Normal.LengthSquared() <= 1e-6f) {
                    v.Normal = Vector3.UnitZ;
                    verts[i] = v;
                }
                else {
                    Vector3 nn = v.Normal;
                    nn.Normalize();
                    v.Normal = nn;
                    verts[i] = v;
                }
            }

            if (verts.Count == 0 || indices.Count == 0) {
                return;
            }

            ObjMeshGroup group = new ObjMeshGroup(materialName, verts.ToArray(), indices.ToArray()) {
                Material = material,
            };
            groups.Add(group);
        }

        private static long BuildKey(int positionIndex, int texCoordIndex, int normalIndex) {
            //三个 21 bit 索引塞进一个 long，足够覆盖 OBJ 常见规模（最大约 200 万）
            unchecked {
                long p = (long)(positionIndex & 0x1FFFFF);
                long t = (long)(texCoordIndex & 0x1FFFFF) << 21;
                long n = (long)(normalIndex & 0x1FFFFF) << 42;
                return p | t | n;
            }
        }

        private static Vector3 ComputeFaceNormal(Vector3 a, Vector3 b, Vector3 c) {
            Vector3 ab = b - a;
            Vector3 ac = c - a;
            Vector3 cross = Vector3.Cross(ab, ac);
            float len = cross.Length();
            if (len <= 1e-6f) {
                return Vector3.UnitZ;
            }
            return cross / len;
        }
    }
}
