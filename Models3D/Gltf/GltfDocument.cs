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
        public int SceneIndex { get; private set; }

        public static GltfDocument Parse(string json, Model3DDiagnostic diagnostic, string source) {
            JObject root = JObject.Parse(json);
            GltfDocument doc = new GltfDocument {
                SceneIndex = ReadInt(root["scene"], 0)
            };

            foreach (JToken token in root["accessors"] as JArray ?? new JArray()) {
                JObject obj = (JObject)token;
                doc.Accessors.Add(new GltfAccessor {
                    BufferView = ReadInt(obj["bufferView"], -1),
                    ByteOffset = ReadInt(obj["byteOffset"], 0),
                    ComponentType = ReadInt(obj["componentType"], 0),
                    Count = ReadInt(obj["count"], 0),
                    Type = ReadString(obj["type"]),
                    Normalized = ReadBool(obj["normalized"], false),
                    Sparse = obj["sparse"] != null,
                });
            }

            foreach (JToken token in root["bufferViews"] as JArray ?? new JArray()) {
                JObject obj = (JObject)token;
                doc.BufferViews.Add(new GltfBufferView {
                    Buffer = ReadInt(obj["buffer"], -1),
                    ByteOffset = ReadInt(obj["byteOffset"], 0),
                    ByteLength = ReadInt(obj["byteLength"], 0),
                    ByteStride = ReadInt(obj["byteStride"], 0),
                });
            }

            foreach (JToken token in root["buffers"] as JArray ?? new JArray()) {
                JObject obj = (JObject)token;
                doc.Buffers.Add(new GltfBuffer {
                    Uri = ReadString(obj["uri"]),
                    ByteLength = ReadInt(obj["byteLength"], 0),
                });
            }

            foreach (JToken token in root["materials"] as JArray ?? new JArray()) {
                JObject obj = (JObject)token;
                JObject pbr = obj["pbrMetallicRoughness"] as JObject;
                doc.Materials.Add(new GltfMaterial {
                    Name = ReadString(obj["name"]),
                    DoubleSided = ReadBool(obj["doubleSided"], false),
                    BaseColorFactor = ReadFloatArray(pbr?["baseColorFactor"], 4),
                    BaseColorTextureIndex = ReadInt(pbr?["baseColorTexture"]?["index"], -1),
                });
            }

            foreach (JToken token in root["meshes"] as JArray ?? new JArray()) {
                JObject obj = (JObject)token;
                GltfMesh mesh = new GltfMesh {
                    Name = ReadString(obj["name"]),
                };
                foreach (JToken primitiveToken in obj["primitives"] as JArray ?? new JArray()) {
                    JObject primitiveObj = (JObject)primitiveToken;
                    GltfPrimitive primitive = new GltfPrimitive {
                        Indices = ReadInt(primitiveObj["indices"], -1),
                        Material = ReadInt(primitiveObj["material"], -1),
                        Mode = ReadInt(primitiveObj["mode"], 4),
                    };
                    JObject attributes = primitiveObj["attributes"] as JObject;
                    if (attributes != null) {
                        foreach (KeyValuePair<string, JToken> kv in attributes) {
                            primitive.Attributes[kv.Key] = ReadInt(kv.Value, -1);
                        }
                    }
                    mesh.Primitives.Add(primitive);
                }
                doc.Meshes.Add(mesh);
            }

            foreach (JToken token in root["nodes"] as JArray ?? new JArray()) {
                JObject obj = (JObject)token;
                GltfNode node = new GltfNode {
                    Name = ReadString(obj["name"]),
                    Mesh = ReadInt(obj["mesh"], -1),
                    Skin = ReadInt(obj["skin"], -1),
                    Matrix = ReadFloatArray(obj["matrix"], 16),
                    Translation = ReadFloatArray(obj["translation"], 3),
                    Rotation = ReadFloatArray(obj["rotation"], 4),
                    Scale = ReadFloatArray(obj["scale"], 3),
                };
                foreach (JToken child in obj["children"] as JArray ?? new JArray()) {
                    node.Children.Add(ReadInt(child, -1));
                }
                doc.Nodes.Add(node);
            }

            foreach (JToken token in root["scenes"] as JArray ?? new JArray()) {
                JObject obj = (JObject)token;
                GltfScene scene = new GltfScene {
                    Name = ReadString(obj["name"]),
                };
                foreach (JToken node in obj["nodes"] as JArray ?? new JArray()) {
                    scene.Nodes.Add(ReadInt(node, -1));
                }
                doc.Scenes.Add(scene);
            }

            foreach (JToken token in root["skins"] as JArray ?? new JArray()) {
                JObject obj = (JObject)token;
                GltfSkin skin = new GltfSkin {
                    Name = ReadString(obj["name"]),
                    InverseBindMatrices = ReadInt(obj["inverseBindMatrices"], -1),
                    Skeleton = ReadInt(obj["skeleton"], -1),
                };
                foreach (JToken joint in obj["joints"] as JArray ?? new JArray()) {
                    skin.Joints.Add(ReadInt(joint, -1));
                }
                doc.Skins.Add(skin);
            }

            foreach (JToken token in root["animations"] as JArray ?? new JArray()) {
                JObject obj = (JObject)token;
                GltfAnimation anim = new GltfAnimation {
                    Name = ReadString(obj["name"]),
                };
                foreach (JToken samplerToken in obj["samplers"] as JArray ?? new JArray()) {
                    JObject samplerObj = (JObject)samplerToken;
                    anim.Samplers.Add(new GltfAnimationSampler {
                        Input = ReadInt(samplerObj["input"], -1),
                        Output = ReadInt(samplerObj["output"], -1),
                        Interpolation = ReadString(samplerObj["interpolation"]),
                    });
                }
                foreach (JToken channelToken in obj["channels"] as JArray ?? new JArray()) {
                    JObject channelObj = (JObject)channelToken;
                    JObject targetObj = channelObj["target"] as JObject;
                    anim.Channels.Add(new GltfAnimationChannel {
                        Sampler = ReadInt(channelObj["sampler"], -1),
                        TargetNode = ReadInt(targetObj?["node"], -1),
                        TargetPath = ReadString(targetObj?["path"]),
                    });
                }
                doc.Animations.Add(anim);
            }

            if (root["extensionsRequired"] is JArray required && required.Count > 0) {
                diagnostic?.Warn(source, 0, $"glTF declares required extensions that are not implemented: {string.Join(", ", required)}");
            }

            return doc;
        }

        private static int ReadInt(JToken token, int fallback) {
            return token == null ? fallback : token.Value<int>();
        }

        private static bool ReadBool(JToken token, bool fallback) {
            return token == null ? fallback : token.Value<bool>();
        }

        private static string ReadString(JToken token) {
            return token == null ? string.Empty : token.Value<string>() ?? string.Empty;
        }

        private static float[] ReadFloatArray(JToken token, int expected) {
            if (token is not JArray array || array.Count != expected) {
                return null;
            }
            float[] result = new float[expected];
            for (int i = 0; i < expected; i++) {
                result[i] = array[i].Value<float>();
            }
            return result;
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
