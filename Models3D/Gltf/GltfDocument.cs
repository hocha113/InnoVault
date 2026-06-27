using InnoVault.JsonDatas;
using InnoVault.Models3D.Runtime;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace InnoVault.Models3D.Gltf
{
    internal sealed class GltfDocument
    {
        public List<GltfAccessor> Accessors { get; } = new();
        public List<GltfBufferView> BufferViews { get; } = new();
        public List<GltfBuffer> Buffers { get; } = new();
        public List<GltfMaterial> Materials { get; } = new();
        public List<GltfMesh> Meshes { get; } = new();
        public List<GltfNode> Nodes { get; } = new();
        public List<GltfScene> Scenes { get; } = new();
        public List<GltfSkin> Skins { get; } = new();
        public List<GltfAnimation> Animations { get; } = new();
        public List<GltfImage> Images { get; } = new();
        public List<GltfTexture> Textures { get; } = new();
        public int SceneIndex { get; private set; }

        public static GltfDocument Parse(string json, Model3DDiagnostic diagnostic, string source) {
            JObject root = JObject.Parse(json);
            GltfDocument doc = new GltfDocument {
                SceneIndex = root.GetInt("scene", 0)
            };

            foreach (JToken token in root["accessors"] as JArray ?? new JArray()) {
                JObject obj = (JObject)token;
                doc.Accessors.Add(new GltfAccessor {
                    BufferView = obj.GetInt("bufferView", -1),
                    ByteOffset = obj.GetInt("byteOffset", 0),
                    ComponentType = obj.GetInt("componentType", 0),
                    Count = obj.GetInt("count", 0),
                    Type = obj.GetString("type"),
                    Normalized = obj.GetBool("normalized", false),
                    Sparse = obj["sparse"] != null,
                });
            }

            foreach (JToken token in root["bufferViews"] as JArray ?? new JArray()) {
                JObject obj = (JObject)token;
                doc.BufferViews.Add(new GltfBufferView {
                    Buffer = obj.GetInt("buffer", -1),
                    ByteOffset = obj.GetInt("byteOffset", 0),
                    ByteLength = obj.GetInt("byteLength", 0),
                    ByteStride = obj.GetInt("byteStride", 0),
                });
            }

            foreach (JToken token in root["buffers"] as JArray ?? new JArray()) {
                JObject obj = (JObject)token;
                doc.Buffers.Add(new GltfBuffer {
                    Uri = obj.GetString("uri"),
                    ByteLength = obj.GetInt("byteLength", 0),
                });
            }

            foreach (JToken token in root["materials"] as JArray ?? new JArray()) {
                JObject obj = (JObject)token;
                JObject pbr = obj["pbrMetallicRoughness"] as JObject;
                JObject extensions = obj["extensions"] as JObject;
                JObject specGloss = extensions?["KHR_materials_pbrSpecularGlossiness"] as JObject;
                doc.Materials.Add(new GltfMaterial {
                    Name = obj.GetString("name"),
                    DoubleSided = obj.GetBool("doubleSided", false),
                    BaseColorFactor = pbr.GetFloatArray("baseColorFactor", 4),
                    BaseColorTextureIndex = pbr.GetObject("baseColorTexture").GetInt("index", -1),
                    SpecGlossDiffuseFactor = specGloss.GetFloatArray("diffuseFactor", 4),
                    SpecGlossDiffuseTextureIndex = specGloss.GetObject("diffuseTexture").GetInt("index", -1),
                });
            }

            foreach (JToken token in root["meshes"] as JArray ?? new JArray()) {
                JObject obj = (JObject)token;
                GltfMesh mesh = new GltfMesh {
                    Name = obj.GetString("name"),
                };
                foreach (JToken primitiveToken in obj["primitives"] as JArray ?? new JArray()) {
                    JObject primitiveObj = (JObject)primitiveToken;
                    GltfPrimitive primitive = new GltfPrimitive {
                        Indices = primitiveObj.GetInt("indices", -1),
                        Material = primitiveObj.GetInt("material", -1),
                        Mode = primitiveObj.GetInt("mode", 4),
                    };
                    JObject attributes = primitiveObj["attributes"] as JObject;
                    if (attributes != null) {
                        foreach (KeyValuePair<string, JToken> kv in attributes) {
                            primitive.Attributes[kv.Key] = kv.Value.AsInt(-1);
                        }
                    }
                    mesh.Primitives.Add(primitive);
                }
                doc.Meshes.Add(mesh);
            }

            foreach (JToken token in root["nodes"] as JArray ?? new JArray()) {
                JObject obj = (JObject)token;
                GltfNode node = new GltfNode {
                    Name = obj.GetString("name"),
                    Mesh = obj.GetInt("mesh", -1),
                    Skin = obj.GetInt("skin", -1),
                    Matrix = obj.GetFloatArray("matrix", 16),
                    Translation = obj.GetFloatArray("translation", 3),
                    Rotation = obj.GetFloatArray("rotation", 4),
                    Scale = obj.GetFloatArray("scale", 3),
                };
                foreach (JToken child in obj["children"] as JArray ?? new JArray()) {
                    node.Children.Add(child.AsInt(-1));
                }
                doc.Nodes.Add(node);
            }

            foreach (JToken token in root["scenes"] as JArray ?? new JArray()) {
                JObject obj = (JObject)token;
                GltfScene scene = new GltfScene {
                    Name = obj.GetString("name"),
                };
                foreach (JToken node in obj["nodes"] as JArray ?? new JArray()) {
                    scene.Nodes.Add(node.AsInt(-1));
                }
                doc.Scenes.Add(scene);
            }

            foreach (JToken token in root["skins"] as JArray ?? new JArray()) {
                JObject obj = (JObject)token;
                GltfSkin skin = new GltfSkin {
                    Name = obj.GetString("name"),
                    InverseBindMatrices = obj.GetInt("inverseBindMatrices", -1),
                    Skeleton = obj.GetInt("skeleton", -1),
                };
                foreach (JToken joint in obj["joints"] as JArray ?? new JArray()) {
                    skin.Joints.Add(joint.AsInt(-1));
                }
                doc.Skins.Add(skin);
            }

            foreach (JToken token in root["images"] as JArray ?? new JArray()) {
                JObject obj = (JObject)token;
                doc.Images.Add(new GltfImage {
                    Uri = obj.GetString("uri"),
                    MimeType = obj.GetString("mimeType"),
                    BufferView = obj.GetInt("bufferView", -1),
                });
            }

            foreach (JToken token in root["textures"] as JArray ?? new JArray()) {
                JObject obj = (JObject)token;
                doc.Textures.Add(new GltfTexture {
                    Source = obj.GetInt("source", -1),
                    Sampler = obj.GetInt("sampler", -1),
                });
            }

            foreach (JToken token in root["animations"] as JArray ?? new JArray()) {
                JObject obj = (JObject)token;
                GltfAnimation anim = new GltfAnimation {
                    Name = obj.GetString("name"),
                };
                foreach (JToken samplerToken in obj["samplers"] as JArray ?? new JArray()) {
                    JObject samplerObj = (JObject)samplerToken;
                    anim.Samplers.Add(new GltfAnimationSampler {
                        Input = samplerObj.GetInt("input", -1),
                        Output = samplerObj.GetInt("output", -1),
                        Interpolation = samplerObj.GetString("interpolation"),
                    });
                }
                foreach (JToken channelToken in obj["channels"] as JArray ?? new JArray()) {
                    JObject channelObj = (JObject)channelToken;
                    JObject targetObj = channelObj["target"] as JObject;
                    anim.Channels.Add(new GltfAnimationChannel {
                        Sampler = channelObj.GetInt("sampler", -1),
                        TargetNode = targetObj.GetInt("node", -1),
                        TargetPath = targetObj.GetString("path"),
                    });
                }
                doc.Animations.Add(anim);
            }

            if (root["extensionsRequired"] is JArray required && required.Count > 0) {
                //本导入器已支持的扩展白名单：解析逻辑分散在材质/贴图等子段，不再为这些名字报警
                List<string> unsupported = new List<string>();
                for (int i = 0; i < required.Count; i++) {
                    string name = required[i]?.Value<string>() ?? string.Empty;
                    if (IsSupportedExtension(name)) {
                        continue;
                    }
                    unsupported.Add(name);
                }
                if (unsupported.Count > 0) {
                    diagnostic?.Warn(source, 0
                        , $"glTF declares required extensions that are not implemented: {string.Join(", ", unsupported)}");
                }
            }

            return doc;
        }

        //当前导入器已经能消费（或确认无副作用）的 glTF 扩展白名单
        private static bool IsSupportedExtension(string name) {
            switch (name) {
                case "KHR_materials_pbrSpecularGlossiness":   //在 GltfMaterial 中读 diffuseTexture/diffuseFactor
                case "KHR_materials_emissive_strength":       //emissive 暂不消费但不影响其他通路
                    return true;
                default:
                    return false;
            }
        }
    }

    internal sealed class GltfAccessor
    {
        public int BufferView;
        public int ByteOffset;
        public int ComponentType;
        public int Count;
        public string Type;
        public bool Normalized;
        public bool Sparse;
    }

    internal sealed class GltfBufferView
    {
        public int Buffer;
        public int ByteOffset;
        public int ByteLength;
        public int ByteStride;
    }

    internal sealed class GltfBuffer
    {
        public string Uri;
        public int ByteLength;
    }

    internal sealed class GltfMaterial
    {
        public string Name;
        public bool DoubleSided;
        public float[] BaseColorFactor;
        public int BaseColorTextureIndex;
        //KHR_materials_pbrSpecularGlossiness 扩展中 diffuse 的 4 维 RGBA 因子，主要用作贴图缺失时的兜底颜色
        public float[] SpecGlossDiffuseFactor;
        //KHR_materials_pbrSpecularGlossiness.diffuseTexture.index，未设置为 -1
        public int SpecGlossDiffuseTextureIndex;
    }

    internal sealed class GltfImage
    {
        public string Uri;
        public string MimeType;
        public int BufferView;
    }

    internal sealed class GltfTexture
    {
        public int Source;
        public int Sampler;
    }

    internal sealed class GltfMesh
    {
        public string Name;
        public List<GltfPrimitive> Primitives { get; } = new();
    }

    internal sealed class GltfPrimitive
    {
        public Dictionary<string, int> Attributes { get; } = new();
        public int Indices;
        public int Material;
        public int Mode;
    }

    internal sealed class GltfNode
    {
        public string Name;
        public int Mesh;
        public int Skin;
        public float[] Matrix;
        public float[] Translation;
        public float[] Rotation;
        public float[] Scale;
        public List<int> Children { get; } = new();
    }

    internal sealed class GltfScene
    {
        public string Name;
        public List<int> Nodes { get; } = new();
    }

    internal sealed class GltfSkin
    {
        public string Name;
        public int InverseBindMatrices;
        public int Skeleton;
        public List<int> Joints { get; } = new();
    }

    internal sealed class GltfAnimation
    {
        public string Name;
        public List<GltfAnimationSampler> Samplers { get; } = new();
        public List<GltfAnimationChannel> Channels { get; } = new();
    }

    internal sealed class GltfAnimationSampler
    {
        public int Input;
        public int Output;
        public string Interpolation;
    }

    internal sealed class GltfAnimationChannel
    {
        public int Sampler;
        public int TargetNode;
        public string TargetPath;
    }
}
