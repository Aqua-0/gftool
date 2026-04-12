using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenTK.Mathematics;
using Trinity.Core.Flatbuffers.TR.Model;
using Trinity.Core.Flatbuffers.Utils;
using Trinity.Core.Utils;


namespace TrinityModelViewer.Export
{
    internal static partial class GltfTrinityPipeline
    {
        private static partial class GltfReader
        {
            internal sealed class GltfDocument
            {
                public string Directory = string.Empty;
                public JsonDocument Json = null!;
                public byte[][] Buffers = Array.Empty<byte[]>();
            }

            public static GltfDocument Load(string path)
            {
                var dir = Path.GetDirectoryName(path) ?? Environment.CurrentDirectory;
                var json = JsonDocument.Parse(File.ReadAllText(path));
                var buffers = LoadBuffers(json.RootElement, dir);
                return new GltfDocument { Directory = dir, Json = json, Buffers = buffers };
            }

            public static List<TrinityPrimitive> ExtractMeshPrimitives(
                GltfDocument doc,
                Dictionary<string, int> boneNameToSkinJointIndex,
                Dictionary<string, int>? boneNameToPaletteIndex)
            {
                var root = doc.Json.RootElement;
                var nodes = GetArray(root, "nodes");
                var meshes = GetArray(root, "meshes");
                var materials = GetArray(root, "materials");
                var skins = GetArray(root, "skins");

                List<string>? TryGetTargetNames(JsonElement mesh)
                {
                    if (mesh.TryGetProperty("extras", out var extras) &&
                        extras.ValueKind == JsonValueKind.Object &&
                        extras.TryGetProperty("targetNames", out var tn) &&
                        tn.ValueKind == JsonValueKind.Array)
                    {
                        var list = new List<string>();
                        foreach (var el in tn.EnumerateArray())
                        {
                            if (el.ValueKind != JsonValueKind.String)
                            {
                                continue;
                            }

                            var s = el.GetString();
                            if (!string.IsNullOrWhiteSpace(s))
                            {
                                list.Add(s);
                            }
                        }

                        return list.Count > 0 ? list : null;
                    }

                    return null;
                }

                var result = new List<TrinityPrimitive>();
                for (int nodeIndex = 0; nodeIndex < nodes.Count; nodeIndex++)
                {
                    var node = nodes[nodeIndex];
                    if (!TryGetInt(node, "mesh", out int meshIndex))
                    {
                        continue;
                    }

                    int? skinIndex = TryGetInt(node, "skin", out int s) ? s : null;
                    string nodeName = TryGetString(node, "name") ?? $"node_{nodeIndex}";

                    if (meshIndex < 0 || meshIndex >= meshes.Count)
                    {
                        continue;
                    }

                    var mesh = meshes[meshIndex];
                    var targetNames = TryGetTargetNames(mesh);
                    var primitives = GetArray(mesh, "primitives");
                    for (int primIndex = 0; primIndex < primitives.Count; primIndex++)
                    {
                        var prim = primitives[primIndex];
                        var tri = ReadPrimitive(doc, prim, nodeName, primIndex, materials, skins, skinIndex, boneNameToSkinJointIndex, boneNameToPaletteIndex, targetNames);
                        if (tri != null)
                        {
                            result.Add(tri);
                        }
                    }
                }

                return result;
            }

            public static Dictionary<string, string?> ExtractBaseColorTextures(GltfDocument doc)
            {
                var root = doc.Json.RootElement;
                var materials = GetArray(root, "materials");
                var textures = GetArray(root, "textures");
                var images = GetArray(root, "images");

                var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < materials.Count; i++)
                {
                    var mat = materials[i];
                    string name = TryGetString(mat, "name") ?? $"material_{i}";

                    if (!mat.TryGetProperty("pbrMetallicRoughness", out var pbr))
                    {
                        map[name] = null;
                        continue;
                    }

                    if (!pbr.TryGetProperty("baseColorTexture", out var bct) || !TryGetInt(bct, "index", out int texIndex))
                    {
                        map[name] = null;
                        continue;
                    }

                    if (texIndex < 0 || texIndex >= textures.Count)
                    {
                        map[name] = null;
                        continue;
                    }

                    var tex = textures[texIndex];
                    if (!TryGetInt(tex, "source", out int imgIndex) || imgIndex < 0 || imgIndex >= images.Count)
                    {
                        map[name] = null;
                        continue;
                    }

                    var img = images[imgIndex];
                    map[name] = TryGetString(img, "uri");
                }

                return map;
            }

            private static TrinityPrimitive? ReadPrimitive(
                GltfDocument doc,
                JsonElement prim,
                string nodeName,
                int primIndex,
                IReadOnlyList<JsonElement> materials,
                IReadOnlyList<JsonElement> skins,
                int? nodeSkinIndex,
                Dictionary<string, int> boneNameToSkinJointIndex,
                Dictionary<string, int>? boneNameToPaletteIndex,
                IReadOnlyList<string>? morphTargetNames)
            {
                var root = doc.Json.RootElement;
                bool allowRawJointsFromTexcoords = true;
                if (root.TryGetProperty("asset", out var asset) && asset.ValueKind == JsonValueKind.Object)
                {
                    var generator = TryGetString(asset, "generator") ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(generator) && generator.Contains("Blender", StringComparison.OrdinalIgnoreCase))
                    {
                        allowRawJointsFromTexcoords = false;
                    }
                }

                int? templateShapeIndex = null;
                int? templatePartIndex = null;
                if (prim.TryGetProperty("extras", out var extras) &&
                    extras.ValueKind == JsonValueKind.Object &&
                    extras.TryGetProperty("trinity", out var trinity) &&
                    trinity.ValueKind == JsonValueKind.Object)
                {
                    if (TryGetInt(trinity, "shapeIndex", out int shapeIndex) && shapeIndex >= 0)
                    {
                        templateShapeIndex = shapeIndex;
                    }
                    if (TryGetInt(trinity, "partIndex", out int partIndex) && partIndex >= 0)
                    {
                        templatePartIndex = partIndex;
                    }
                }

                if (!prim.TryGetProperty("attributes", out var attrs))
                {
                    return null;
                }

                if (!TryGetInt(attrs, "POSITION", out int posAccessor))
                {
                    return null;
                }

                var positions = ReadVec3(doc, posAccessor);
                if (positions.Length == 0)
                {
                    return null;
                }

                Vector3[] normals = Array.Empty<Vector3>();
                if (TryGetInt(attrs, "NORMAL", out int normAccessor))
                {
                    normals = ReadVec3(doc, normAccessor);
                }

                Vector4[] tangents = Array.Empty<Vector4>();
                if (TryGetInt(attrs, "TANGENT", out int tanAccessor))
                {
                    tangents = ReadVec4(doc, tanAccessor);
                }

                var morphTargets = new List<TrinityPrimitive.MorphTarget>();
                if (prim.TryGetProperty("targets", out var targets) && targets.ValueKind == JsonValueKind.Array)
                {
                    int targetIndex = 0;
                    foreach (var t in targets.EnumerateArray())
                    {
                        if (t.ValueKind != JsonValueKind.Object)
                        {
                            targetIndex++;
                            continue;
                        }

                        if (!TryGetInt(t, "POSITION", out int mtPosAcc))
                        {
                            targetIndex++;
                            continue;
                        }

                        var mtPos = ReadVec3(doc, mtPosAcc);
                        if (mtPos.Length != positions.Length)
                        {
                            targetIndex++;
                            continue;
                        }

                        Vector3[] mtNrm = Array.Empty<Vector3>();
                        if (TryGetInt(t, "NORMAL", out int mtNrmAcc))
                        {
                            mtNrm = ReadVec3(doc, mtNrmAcc);
                        }
                        if (mtNrm.Length != positions.Length)
                        {
                            mtNrm = new Vector3[positions.Length];
                        }

                        Vector4[] mtTan = Array.Empty<Vector4>();
                        if (TryGetInt(t, "TANGENT", out int mtTanAcc))
                        {
                            mtTan = ReadVec4(doc, mtTanAcc);
                        }
                        if (mtTan.Length != positions.Length)
                        {
                            mtTan = new Vector4[positions.Length];
                        }

                        string name = (morphTargetNames != null && targetIndex >= 0 && targetIndex < morphTargetNames.Count)
                            ? morphTargetNames[targetIndex]
                            : $"MorphTarget_{targetIndex:D3}";

                        morphTargets.Add(new TrinityPrimitive.MorphTarget
                        {
                            Name = name,
                            PositionDeltas = mtPos,
                            NormalDeltas = mtNrm,
                            TangentDeltas = mtTan
                        });

                        targetIndex++;
                    }
                }

                Vector2[] uv0 = Array.Empty<Vector2>();
                if (TryGetInt(attrs, "TEXCOORD_0", out int uvAccessor))
                {
                    uv0 = ReadVec2(doc, uvAccessor);
                    // `GltfExporter` flips V on export (uv.y = 1 - y). Undo it on import so round-trips
                    if (uv0.Length > 0)
                    {
                        for (int i = 0; i < uv0.Length; i++)
                        {
                            uv0[i] = new Vector2(uv0[i].X, 1f - uv0[i].Y);
                        }
                    }
                }

                uint[] indices;
                if (TryGetInt(prim, "indices", out int indexAccessor))
                {
                    indices = ReadIndices(doc, indexAccessor);
                }
                else
                {
                    // No index buffer: emit a trivial sequence.
                    indices = Enumerable.Range(0, positions.Length).Select(i => (uint)i).ToArray();
                }

                string materialName = "Material";
                if (TryGetInt(prim, "material", out int matIndex) && matIndex >= 0 && matIndex < materials.Count)
                {
                    materialName = TryGetString(materials[matIndex], "name") ?? $"material_{matIndex}";
                }

                Vector4[] colors = Array.Empty<Vector4>();
                if (TryGetInt(attrs, "COLOR_0", out int colorAccessor))
                {
                    colors = ReadColorVec4(doc, colorAccessor);
                }

	                int jointsAccessor = -1;
	                int jointsRawAccessor = -1;
	                int weightsAccessor = -1;
	                bool hasSkinning = nodeSkinIndex.HasValue &&
	                                   TryGetInt(attrs, "JOINTS_0", out jointsAccessor) &&
	                                   TryGetInt(attrs, "WEIGHTS_0", out weightsAccessor);

	                Vector4i[] joints = Array.Empty<Vector4i>();
	                Vector4[] weights = Array.Empty<Vector4>();
	                if (hasSkinning)
	                {
	                    // Prefer lossless Trinity round-trip joints if present.
	                    // These are the raw on-disk indices and don't need name-based remapping.
                    bool rawFromAttribute = false;
                    bool rawFromTexcoords = false;
                    bool hasRaw = TryGetInt(attrs, "_TRINITY_JOINTS_RAW", out jointsRawAccessor);
                    if (hasRaw)
                    {
                        rawFromAttribute = true;
                        joints = ReadVec4UShort(doc, jointsRawAccessor);
                    }
                    else if (allowRawJointsFromTexcoords &&
                             TryGetInt(attrs, "TEXCOORD_1", out int uv01Accessor) &&
                             TryGetInt(attrs, "TEXCOORD_2", out int uv23Accessor) &&
                             TryReadRawJointsFromTexcoords(doc, uv01Accessor, uv23Accessor, positions.Length, out var uvJoints))
                    {
                        // Blender drops unknown attributes like `_TRINITY_JOINTS_RAW`, but will preserve UV sets.
                        joints = uvJoints;
                        rawFromTexcoords = true;
                        hasRaw = true;
                    }
	                    else
	                    {
	                        joints = ReadVec4UShort(doc, jointsAccessor);
	                    }
	                    weights = ReadWeights(doc, weightsAccessor);

	                    if (joints.Length != positions.Length || weights.Length != positions.Length)
	                    {
	                        // Mismatched streams: drop skinning.
	                        hasSkinning = false;
	                        joints = Array.Empty<Vector4i>();
	                        weights = Array.Empty<Vector4>();
	                    }
	                    else
	                    {
	                        if (hasRaw)
	                        {
		                            // Raw joints can be in connected skinning-palette space (protag clothing).
		                            // Do not compare them against name-mapped joint info indices, or we'll "fix" them by
		                            // overwriting palette indices (breaks in-game exports).
		                            if (boneNameToPaletteIndex != null && boneNameToPaletteIndex.Count > 0)
		                            {
		                                ReconcileRawJointsWithSkinWeightsViaPalette(
		                                    doc,
		                                    skins,
		                                    nodeSkinIndex!.Value,
		                                    boneNameToPaletteIndex,
		                                    jointsAccessor,
		                                    joints,
		                                    weights);
		                            }
		                            else if (rawFromTexcoords && !rawFromAttribute)
		                            {
		                                ReconcileRawJointsWithSkinWeights(
		                                    doc,
		                                    skins,
		                                    nodeSkinIndex!.Value,
		                                    boneNameToSkinJointIndex,
		                                    jointsAccessor,
		                                    joints,
		                                    weights);
		                            }
		                        }
		                        else
		                        {
		                            if (boneNameToPaletteIndex != null && boneNameToPaletteIndex.Count > 0)
		                            {
		                                MapJointsInPlaceViaPalette(doc, skins, nodeSkinIndex!.Value, boneNameToPaletteIndex, joints, weights);
		                            }
		                            else
		                            {
		                                MapJointsInPlace(doc, skins, nodeSkinIndex!.Value, boneNameToSkinJointIndex, joints, weights);
		                            }
		                        }
		                    }
		                }

                string primName;
                if (!string.IsNullOrWhiteSpace(materialName) && !string.Equals(materialName, "Material", StringComparison.OrdinalIgnoreCase))
                {
                    primName = $"{nodeName}:{materialName}";
                }
                else
                {
                    primName = $"{nodeName}:{primIndex}";
                }

                return new TrinityPrimitive
                {
                    Name = primName,
                    MaterialName = materialName,
                    TemplateShapeIndex = templateShapeIndex,
                    TemplatePartIndex = templatePartIndex,
                    SourceNodeName = nodeName,
                    SourcePrimitiveIndex = primIndex,
                    Positions = positions,
                    Normals = normals,
                    Tangents = tangents,
                    Colors = colors,
                    Uv0 = uv0,
                    HasSkinning = hasSkinning,
                    JointIndices = joints,
                    JointWeights = weights,
                    Indices = indices,
                    MorphTargets = morphTargets
                };
            }

	            private static void MapJointsInPlace(
	                GltfDocument doc,
	                IReadOnlyList<JsonElement> skins,
	                int skinIndex,
	                Dictionary<string, int> boneNameToSkinJointIndex,
	                Vector4i[] joints,
	                Vector4[] weights)
	            {
	                if (skinIndex < 0 || skinIndex >= skins.Count)
	                {
	                    return;
	                }

	                var skin = skins[skinIndex];
	                var jointNodes = GetIntArray(skin, "joints");
	                var nodes = GetArray(doc.Json.RootElement, "nodes");

	                int ResolveMappedJoint(int jointSlot)
	                {
	                    if (jointSlot < 0 || jointSlot >= jointNodes.Count)
	                    {
	                        return -1;
	                    }

	                    int nodeIndex = jointNodes[jointSlot];
	                    if (nodeIndex < 0 || nodeIndex >= nodes.Count)
	                    {
	                        return -1;
	                    }

	                    var node = nodes[nodeIndex];
	                    var name = TryGetString(node, "name");
	                    if (name == null)
	                    {
	                        return -1;
	                    }

	                    return boneNameToSkinJointIndex.TryGetValue(name, out var ji) ? ji : -1;
	                }

	                for (int i = 0; i < joints.Length; i++)
	                {
	                    var j = joints[i];
	                    var w = weights[i];

	                    int j0 = ResolveMappedJoint(j.X);
	                    int j1 = ResolveMappedJoint(j.Y);
	                    int j2 = ResolveMappedJoint(j.Z);
	                    int j3 = ResolveMappedJoint(j.W);

	                    float w0 = w.X;
	                    float w1 = w.Y;
	                    float w2 = w.Z;
	                    float w3 = w.W;

	                    if (j0 < 0) { j0 = 0; w0 = 0; }
	                    if (j1 < 0) { j1 = 0; w1 = 0; }
	                    if (j2 < 0) { j2 = 0; w2 = 0; }
	                    if (j3 < 0) { j3 = 0; w3 = 0; }

	                    float sum = w0 + w1 + w2 + w3;
	                    if (sum <= 0.000001f)
	                    {
	                        j0 = 0;
	                        w0 = 1;
	                        w1 = w2 = w3 = 0;
	                        sum = 1;
	                    }

	                    float inv = 1.0f / sum;
	                    weights[i] = new Vector4(w0 * inv, w1 * inv, w2 * inv, w3 * inv);
	                    joints[i] = new Vector4i(j0, j1, j2, j3);
	                }
	            }

	            private static void MapJointsInPlaceViaPalette(
	                GltfDocument doc,
	                IReadOnlyList<JsonElement> skins,
	                int skinIndex,
	                Dictionary<string, int> boneNameToPaletteIndex,
	                Vector4i[] joints,
	                Vector4[] weights)
	            {
	                if (skinIndex < 0 || skinIndex >= skins.Count)
	                {
	                    return;
	                }

	                var skin = skins[skinIndex];
	                var jointNodes = GetIntArray(skin, "joints");
	                var nodes = GetArray(doc.Json.RootElement, "nodes");

	                int ResolvePaletteJoint(int jointSlot)
	                {
	                    if (jointSlot < 0 || jointSlot >= jointNodes.Count)
	                    {
	                        return -1;
	                    }

	                    int nodeIndex = jointNodes[jointSlot];
	                    if (nodeIndex < 0 || nodeIndex >= nodes.Count)
	                    {
	                        return -1;
	                    }

	                    var node = nodes[nodeIndex];
	                    var name = TryGetString(node, "name");
	                    if (name == null)
	                    {
	                        return -1;
	                    }

	                    return boneNameToPaletteIndex.TryGetValue(name, out var pi) ? pi : -1;
	                }

	                for (int i = 0; i < joints.Length; i++)
	                {
	                    var j = joints[i];
	                    var w = weights[i];

	                    int j0 = ResolvePaletteJoint(j.X);
	                    int j1 = ResolvePaletteJoint(j.Y);
	                    int j2 = ResolvePaletteJoint(j.Z);
	                    int j3 = ResolvePaletteJoint(j.W);

	                    float w0 = w.X;
	                    float w1 = w.Y;
	                    float w2 = w.Z;
	                    float w3 = w.W;

	                    if (j0 < 0) { j0 = 0; w0 = 0; }
	                    if (j1 < 0) { j1 = 0; w1 = 0; }
	                    if (j2 < 0) { j2 = 0; w2 = 0; }
	                    if (j3 < 0) { j3 = 0; w3 = 0; }

	                    float sum = w0 + w1 + w2 + w3;
	                    if (sum <= 0.000001f)
	                    {
	                        j0 = 0;
	                        w0 = 1;
	                        w1 = w2 = w3 = 0;
	                        sum = 1;
	                    }

	                    float inv = 1.0f / sum;
	                    weights[i] = new Vector4(w0 * inv, w1 * inv, w2 * inv, w3 * inv);
	                    joints[i] = new Vector4i(j0, j1, j2, j3);
	                }
	            }

	            private static bool TryReadRawJointsFromTexcoords(GltfDocument doc, int uv01Accessor, int uv23Accessor, int expectedCount, out Vector4i[] joints)
	            {
	                joints = Array.Empty<Vector4i>();

	                var uv01 = ReadVec2(doc, uv01Accessor);
	                var uv23 = ReadVec2(doc, uv23Accessor);
	                if (uv01.Length != expectedCount || uv23.Length != expectedCount)
	                {
	                    return false;
	                }

	                float max = 0;
	                float maxFrac = 0;
	                var outJoints = new Vector4i[expectedCount];
	                for (int i = 0; i < expectedCount; i++)
	                {
	                    float j0f = uv01[i].X;
	                    float j1f = uv01[i].Y;
	                    float j2f = uv23[i].X;
	                    float j3f = uv23[i].Y;

	                    float r0 = MathF.Round(j0f);
	                    float r1 = MathF.Round(j1f);
	                    float r2 = MathF.Round(j2f);
	                    float r3 = MathF.Round(j3f);

	                    maxFrac = Math.Max(maxFrac, Math.Abs(j0f - r0));
	                    maxFrac = Math.Max(maxFrac, Math.Abs(j1f - r1));
	                    maxFrac = Math.Max(maxFrac, Math.Abs(j2f - r2));
	                    maxFrac = Math.Max(maxFrac, Math.Abs(j3f - r3));

	                    int j0 = (int)r0;
	                    int j1 = (int)r1;
	                    int j2 = (int)r2;
	                    int j3 = (int)r3;
	                    outJoints[i] = new Vector4i(j0, j1, j2, j3);
	                    max = Math.Max(max, Math.Max(Math.Max(j0, j1), Math.Max(j2, j3)));
	                }

	                // Heuristics:
	                // - Raw joints should be basically integers.
	                // - And they should not look like regular UVs (0..1 range).
	                if (maxFrac > 0.001f)
	                {
	                    return false;
	                }
	                if (max <= 1.5f)
	                {
	                    return false;
	                }
	                if (max > 4096f)
	                {
	                    return false;
	                }

	                joints = outJoints;
	                return true;
	            }

	            private static void ReconcileRawJointsWithSkinWeights(
	                GltfDocument doc,
	                IReadOnlyList<JsonElement> skins,
	                int skinIndex,
	                Dictionary<string, int> boneNameToSkinJointIndex,
	                int jointsAccessor,
	                Vector4i[] jointsRaw,
	                Vector4[] weights)
	            {
                if (!HasCompleteSkinMapping(doc, skins, skinIndex, boneNameToSkinJointIndex))
                {
                    return;
                }

                // Map JOINTS_0 into the same index space as jointsRaw so we can pair weights to the correct bones.
                var jointsFromSkin = ReadVec4UShort(doc, jointsAccessor);
                if (jointsFromSkin.Length != jointsRaw.Length)
                {
                    return;
	                }

	                var weightsFromSkin = (Vector4[])weights.Clone();
	                MapJointsInPlace(doc, skins, skinIndex, boneNameToSkinJointIndex, jointsFromSkin, weightsFromSkin);

	                for (int i = 0; i < jointsRaw.Length; i++)
	                {
	                    var raw = jointsRaw[i];
	                    var mapped = jointsFromSkin[i];
	                    var mappedW = weightsFromSkin[i];

	                    if (TryPermuteWeightsToMatchRawJoints(raw, mapped, mappedW, out var permuted))
	                    {
	                        // Raw joints look valid; just shuffle weights to match their order.
	                        weights[i] = permuted;
	                    }
	                    else
	                    {
	                        // Raw joints don't match what's actually in JOINTS_0 (e.g. user edited weights/bone groups).
	                        // Respect the glTF skinning edit and drop the stale raw joints.
	                        jointsRaw[i] = mapped;
	                        weights[i] = mappedW;
                    }
                }
	            }

	            private static void ReconcileRawJointsWithSkinWeightsViaPalette(
	                GltfDocument doc,
	                IReadOnlyList<JsonElement> skins,
	                int skinIndex,
	                Dictionary<string, int> boneNameToPaletteIndex,
	                int jointsAccessor,
	                Vector4i[] jointsRaw,
	                Vector4[] weights)
	            {
	                if (!HasCompleteSkinMapping(doc, skins, skinIndex, boneNameToPaletteIndex))
	                {
	                    return;
	                }

	                var jointsFromSkin = ReadVec4UShort(doc, jointsAccessor);
	                if (jointsFromSkin.Length != jointsRaw.Length)
	                {
	                    return;
	                }

	                var mappedToPalette = MapSkinSlotsToPaletteIndices(doc, skins, skinIndex, boneNameToPaletteIndex, jointsFromSkin);
	                if (mappedToPalette == null || mappedToPalette.Length != jointsRaw.Length)
	                {
	                    return;
	                }

	                var weightsFromSkin = (Vector4[])weights.Clone();

	                for (int i = 0; i < jointsRaw.Length; i++)
	                {
	                    var raw = jointsRaw[i];
	                    var mapped = mappedToPalette[i];
	                    var mappedW = weightsFromSkin[i];

	                    if (TryPermuteWeightsToMatchRawJoints(raw, mapped, mappedW, out var permuted))
	                    {
	                        weights[i] = permuted;
	                    }
	                    else
	                    {
	                        jointsRaw[i] = mapped;
	                        weights[i] = mappedW;
	                    }
	                }
	            }

	            private static Vector4i[]? MapSkinSlotsToPaletteIndices(
	                GltfDocument doc,
	                IReadOnlyList<JsonElement> skins,
	                int skinIndex,
	                Dictionary<string, int> boneNameToPaletteIndex,
	                Vector4i[] jointsFromSkin)
	            {
	                if (skinIndex < 0 || skinIndex >= skins.Count)
	                {
	                    return null;
	                }

	                var skin = skins[skinIndex];
	                var jointNodes = GetIntArray(skin, "joints");
	                var nodes = GetArray(doc.Json.RootElement, "nodes");

	                int ResolvePalette(int jointSlot)
	                {
	                    if (jointSlot < 0 || jointSlot >= jointNodes.Count)
	                    {
	                        return -1;
	                    }

	                    int nodeIndex = jointNodes[jointSlot];
	                    if (nodeIndex < 0 || nodeIndex >= nodes.Count)
	                    {
	                        return -1;
	                    }

	                    var node = nodes[nodeIndex];
	                    var name = TryGetString(node, "name");
	                    if (string.IsNullOrWhiteSpace(name))
	                    {
	                        return -1;
	                    }

	                    return boneNameToPaletteIndex.TryGetValue(name, out var pi) ? pi : -1;
	                }

	                var mapped = new Vector4i[jointsFromSkin.Length];
	                for (int i = 0; i < jointsFromSkin.Length; i++)
	                {
	                    var j = jointsFromSkin[i];
	                    mapped[i] = new Vector4i(
	                        ResolvePalette(j.X),
	                        ResolvePalette(j.Y),
	                        ResolvePalette(j.Z),
	                        ResolvePalette(j.W));
	                }

	                return mapped;
	            }

	            private static bool HasCompleteSkinMapping(
	                GltfDocument doc,
	                IReadOnlyList<JsonElement> skins,
	                int skinIndex,
	                Dictionary<string, int> boneNameToSkinJointIndex)
	            {
                if (boneNameToSkinJointIndex.Count == 0)
                {
                    return false;
                }

                if (skinIndex < 0 || skinIndex >= skins.Count)
                {
                    return false;
                }

                var skin = skins[skinIndex];
                var jointNodes = GetIntArray(skin, "joints");
                if (jointNodes.Count == 0)
                {
                    return false;
                }

                var nodes = GetArray(doc.Json.RootElement, "nodes");
                foreach (var jointNode in jointNodes)
                {
                    if (jointNode < 0 || jointNode >= nodes.Count)
                    {
                        return false;
                    }

                    var node = nodes[jointNode];
                    var name = TryGetString(node, "name");
                    if (string.IsNullOrWhiteSpace(name) || !boneNameToSkinJointIndex.ContainsKey(name))
                    {
                        return false;
                    }
                }

                return true;
            }

            private static bool TryPermuteWeightsToMatchRawJoints(Vector4i raw, Vector4i mapped, Vector4 mappedWeights, out Vector4 permuted)
            {
                permuted = default;

	                int[] rawJ = { raw.X, raw.Y, raw.Z, raw.W };
	                int[] mappedJ = { mapped.X, mapped.Y, mapped.Z, mapped.W };
	                float[] mappedW = { mappedWeights.X, mappedWeights.Y, mappedWeights.Z, mappedWeights.W };

	                // Quick multiset check (order doesn't matter, but counts do).
	                int[] a = (int[])rawJ.Clone();
	                int[] b = (int[])mappedJ.Clone();
	                Array.Sort(a);
	                Array.Sort(b);
	                for (int i = 0; i < 4; i++)
	                {
	                    if (a[i] != b[i])
	                    {
	                        return false;
	                    }
	                }

	                bool[] used = new bool[4];
	                float[] outW = new float[4];

	                for (int outIndex = 0; outIndex < 4; outIndex++)
	                {
	                    int targetJoint = rawJ[outIndex];
	                    int found = -1;
	                    for (int k = 0; k < 4; k++)
	                    {
	                        if (used[k])
	                        {
	                            continue;
	                        }
	                        if (mappedJ[k] == targetJoint)
	                        {
	                            found = k;
	                            break;
	                        }
	                    }

	                    if (found >= 0)
	                    {
	                        used[found] = true;
	                        outW[outIndex] = mappedW[found];
	                    }
	                    else
	                    {
	                        // Shouldn't happen if multisets are equal, but keep it safe.
	                        outW[outIndex] = 0;
	                    }
	                }

	                permuted = new Vector4(outW[0], outW[1], outW[2], outW[3]);
	                return true;
	            }
	        }
	    }

}
