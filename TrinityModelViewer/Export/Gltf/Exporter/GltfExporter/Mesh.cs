using GFTool.Renderer.Core;
using GFTool.Renderer.Scene.GraphicsObjects;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TrinityModelViewer.Export
{
    internal static partial class GltfExporter
    {
        private static string GetShapeName(string submeshName)
        {
            if (string.IsNullOrWhiteSpace(submeshName))
            {
                return "Mesh";
            }

            int colon = submeshName.IndexOf(':');
            if (colon > 0)
            {
                return submeshName.Substring(0, colon);
            }

            return submeshName;
        }

	        private static int AddMeshShape(
	            GltfRoot gltf,
	            BinaryBufferBuilder buffer,
                Model model,
	            Model.ExportSubmesh canonical,
	            IReadOnlyList<Model.ExportSubmesh> parts,
	            int shapeIndex,
	            Dictionary<string, int> gltfMaterialIndex,
            Dictionary<string, Material> materialByName,
            Dictionary<string, int> textureCache,
            string texDir)
        {
            int vertexCount = canonical.Positions.Length;

            var positions = canonical.Positions;
            var normals = canonical.Normals.Length == vertexCount
                ? canonical.Normals
                : Enumerable.Repeat(Vector3.UnitY, vertexCount).ToArray();
            var uvs = canonical.UVs.Length == vertexCount
                ? canonical.UVs.Select(uv => new Vector2(uv.X, 1f - uv.Y)).ToArray()
                : Enumerable.Repeat(Vector2.Zero, vertexCount).ToArray();

            bool hasTangents = parts.Any(p => p.HasTangents) && canonical.Tangents.Length == vertexCount;
            var tangents = hasTangents ? canonical.Tangents : Array.Empty<Vector4>();

	            bool hasSkinning = parts.Any(p => p.HasSkinning);
	            var joints = canonical.BlendIndices.Length == vertexCount
	                ? canonical.BlendIndices
	                : Enumerable.Repeat(Vector4.Zero, vertexCount).ToArray();
	            var jointsRaw = canonical.BlendIndicesRaw.Length == vertexCount
	                ? canonical.BlendIndicesRaw
	                : Array.Empty<Vector4>();
	            var weights = canonical.BlendWeights.Length == vertexCount
	                ? canonical.BlendWeights
	                : Enumerable.Repeat(new Vector4(1, 0, 0, 0), vertexCount).ToArray();

            int posAcc = AddAccessorVec3(gltf, buffer, positions, target: 34962, includeMinMax: true);
            int nrmAcc = AddAccessorVec3(gltf, buffer, normals, target: 34962);
            int uvAcc = AddAccessorVec2(gltf, buffer, uvs, target: 34962);

            int? tanAcc = null;
            if (hasTangents)
            {
                tanAcc = AddAccessorVec4(gltf, buffer, tangents, target: 34962);
            }

	            int? jointAcc = null;
	            int? jointRawAcc = null;
	            int? jointRawUv01Acc = null;
	            int? jointRawUv23Acc = null;
	            int? weightAcc = null;
	            if (hasSkinning)
	            {
	                jointAcc = AddAccessorUShort4(gltf, buffer, joints, target: 34962);
	                if (jointsRaw.Length == vertexCount)
	                {
	                    // Keep the original on-disk indices for lossless Trinity round-trips.
	                    // The renderer may remap indices for preview, but the game expects the raw index space.
	                    jointRawAcc = AddAccessorUShort4(gltf, buffer, jointsRaw, target: 34962);
	                    // Blender drops unknown attributes like `_TRINITY_JOINTS_RAW`, so also stash raw joints in
	                    // standard UV channels that Blender will preserve (as float2 + float2).
	                    var uv01 = new Vector2[vertexCount];
	                    var uv23 = new Vector2[vertexCount];
	                    for (int i = 0; i < vertexCount; i++)
	                    {
	                        var v = jointsRaw[i];
	                        uv01[i] = new Vector2(v.X, v.Y);
	                        uv23[i] = new Vector2(v.Z, v.W);
	                    }
	                    jointRawUv01Acc = AddAccessorVec2(gltf, buffer, uv01, target: 34962);
	                    jointRawUv23Acc = AddAccessorVec2(gltf, buffer, uv23, target: 34962);
	                }
	                weightAcc = AddAccessorVec4(gltf, buffer, weights, target: 34962);
	            }

	            var gltfMesh = new GltfMesh
	            {
	                Name = GetShapeName(canonical.Name),
	                Primitives = new List<GltfPrimitive>()
	            };

                // Morph targets (shape keys): emit per-primitive targets and mesh default weights.
                List<Dictionary<string, int>>? gltfTargets = null;
                var shapeName = GetShapeName(canonical.Name);
                var morphTargets = model.BuildCpuMorphTargetsForExport(shapeName, positions, normals, tangents);
                if (morphTargets.Count > 0)
                {
                    gltfMesh.Weights = Enumerable.Repeat(0.0f, morphTargets.Count).ToArray();
                    gltfMesh.Extras = new Dictionary<string, object>
                    {
                        ["targetNames"] = morphTargets.Select(t => t.Name).ToArray()
                    };

                    gltfTargets = new List<Dictionary<string, int>>(morphTargets.Count);
                    for (int i = 0; i < morphTargets.Count; i++)
                    {
                        var mt = morphTargets[i];
                        var d = new Dictionary<string, int>
                        {
                            ["POSITION"] = AddAccessorVec3(gltf, buffer, mt.PositionDeltas, target: 34962),
                            ["NORMAL"] = AddAccessorVec3(gltf, buffer, mt.NormalDeltas, target: 34962)
                        };
                        if (hasTangents && mt.TangentDeltas.Length == vertexCount)
                        {
                            d["TANGENT"] = AddAccessorVec4(gltf, buffer, mt.TangentDeltas, target: 34962);
                        }
                        gltfTargets.Add(d);
                    }
                }

	            for (int partIndex = 0; partIndex < parts.Count; partIndex++)
	            {
	                var part = parts[partIndex];
                    var (forceRepeatU, forceRepeatV) = GetRepeatOverrideFromUvs(uvs, part.Indices);
	                int materialIndex = GetOrCreateMaterial(gltf, gltfMaterialIndex, materialByName, textureCache, part.MaterialName, texDir, forceRepeatU, forceRepeatV);
	                int idxAcc = AddAccessorIndices(gltf, buffer, part.Indices);

	                var prim = new GltfPrimitive
	                {
	                    Attributes = new Dictionary<string, int>
	                    {
	                        ["POSITION"] = posAcc,
	                        ["NORMAL"] = nrmAcc,
	                        ["TEXCOORD_0"] = uvAcc
	                    },
	                    Indices = idxAcc,
	                    Material = materialIndex,
                        Targets = gltfTargets,
	                    Extras = new Dictionary<string, object>
	                    {
	                        ["trinity"] = new Dictionary<string, object>
	                        {
	                            ["shapeIndex"] = shapeIndex,
                            ["partIndex"] = partIndex,
                            ["templateSubmeshName"] = part.Name,
                            ["templateMaterialName"] = part.MaterialName
                        }
                    }
                };

                if (tanAcc.HasValue)
                {
                    prim.Attributes["TANGENT"] = tanAcc.Value;
                }

	                if (jointAcc.HasValue && weightAcc.HasValue)
	                {
	                    prim.Attributes["JOINTS_0"] = jointAcc.Value;
	                    prim.Attributes["WEIGHTS_0"] = weightAcc.Value;
	                    if (jointRawAcc.HasValue)
	                    {
	                        // Custom attribute (glTF allows app-specific attrs starting with underscore).
	                        prim.Attributes["_TRINITY_JOINTS_RAW"] = jointRawAcc.Value;
	                    }
	                    if (jointRawUv01Acc.HasValue && jointRawUv23Acc.HasValue)
	                    {
	                        prim.Attributes["TEXCOORD_1"] = jointRawUv01Acc.Value;
	                        prim.Attributes["TEXCOORD_2"] = jointRawUv23Acc.Value;
	                    }
	                }

                gltfMesh.Primitives!.Add(prim);
            }

            int meshIndex = gltf.Meshes.Count;
            gltf.Meshes.Add(gltfMesh);
            return meshIndex;
        }

        private static int AddMesh(GltfRoot gltf, BinaryBufferBuilder buffer, Model.ExportSubmesh sub, int materialIndex)
        {
            int vertexCount = sub.Positions.Length;

            var positions = sub.Positions;
            var normals = sub.Normals.Length == vertexCount ? sub.Normals : Enumerable.Repeat(Vector3.UnitY, vertexCount).ToArray();
            var uvs = sub.UVs.Length == vertexCount
                ? sub.UVs.Select(uv => new Vector2(uv.X, 1f - uv.Y)).ToArray()
                : Enumerable.Repeat(Vector2.Zero, vertexCount).ToArray();
	            var tangents = sub.Tangents.Length == vertexCount ? sub.Tangents : Array.Empty<Vector4>();
	            var joints = sub.BlendIndices.Length == vertexCount ? sub.BlendIndices : Enumerable.Repeat(Vector4.Zero, vertexCount).ToArray();
	            var jointsRaw = sub.BlendIndicesRaw.Length == vertexCount ? sub.BlendIndicesRaw : Array.Empty<Vector4>();
	            var weights = sub.BlendWeights.Length == vertexCount ? sub.BlendWeights : Enumerable.Repeat(new Vector4(1, 0, 0, 0), vertexCount).ToArray();

            int posAcc = AddAccessorVec3(gltf, buffer, positions, target: 34962, includeMinMax: true);
            int nrmAcc = AddAccessorVec3(gltf, buffer, normals, target: 34962);
            int uvAcc = AddAccessorVec2(gltf, buffer, uvs, target: 34962);

            int? tanAcc = null;
            if (sub.HasTangents && tangents.Length == vertexCount)
            {
                tanAcc = AddAccessorVec4(gltf, buffer, tangents, target: 34962);
            }

	            int? jointAcc = null;
	            int? jointRawAcc = null;
	            int? jointRawUv01Acc = null;
	            int? jointRawUv23Acc = null;
	            int? weightAcc = null;
	            if (sub.HasSkinning)
	            {
	                jointAcc = AddAccessorUShort4(gltf, buffer, joints, target: 34962);
	                if (jointsRaw.Length == vertexCount)
	                {
	                    jointRawAcc = AddAccessorUShort4(gltf, buffer, jointsRaw, target: 34962);
	                    var uv01 = new Vector2[vertexCount];
	                    var uv23 = new Vector2[vertexCount];
	                    for (int i = 0; i < vertexCount; i++)
	                    {
	                        var v = jointsRaw[i];
	                        uv01[i] = new Vector2(v.X, v.Y);
	                        uv23[i] = new Vector2(v.Z, v.W);
	                    }
	                    jointRawUv01Acc = AddAccessorVec2(gltf, buffer, uv01, target: 34962);
	                    jointRawUv23Acc = AddAccessorVec2(gltf, buffer, uv23, target: 34962);
	                }
	                weightAcc = AddAccessorVec4(gltf, buffer, weights, target: 34962);
	            }

            int idxAcc = AddAccessorIndices(gltf, buffer, sub.Indices);

            var prim = new GltfPrimitive
            {
                Attributes = new Dictionary<string, int>
                {
                    ["POSITION"] = posAcc,
                    ["NORMAL"] = nrmAcc,
                    ["TEXCOORD_0"] = uvAcc
                },
                Indices = idxAcc,
                Material = materialIndex
            };

            if (tanAcc.HasValue)
            {
                prim.Attributes["TANGENT"] = tanAcc.Value;
            }

	            if (jointAcc.HasValue && weightAcc.HasValue)
	            {
	                prim.Attributes["JOINTS_0"] = jointAcc.Value;
	                prim.Attributes["WEIGHTS_0"] = weightAcc.Value;
	                if (jointRawAcc.HasValue)
	                {
	                    prim.Attributes["_TRINITY_JOINTS_RAW"] = jointRawAcc.Value;
	                }
	                if (jointRawUv01Acc.HasValue && jointRawUv23Acc.HasValue)
	                {
	                    prim.Attributes["TEXCOORD_1"] = jointRawUv01Acc.Value;
	                    prim.Attributes["TEXCOORD_2"] = jointRawUv23Acc.Value;
	                }
	            }

            var mesh = new GltfMesh
            {
                Name = sub.Name,
                Primitives = new List<GltfPrimitive> { prim }
            };
            int meshIndex = gltf.Meshes.Count;
            gltf.Meshes.Add(mesh);
            return meshIndex;
        }

        private static (bool ForceRepeatU, bool ForceRepeatV) GetRepeatOverrideFromUvs(Vector2[] uvs, uint[] indices)
        {
            if (uvs == null || uvs.Length == 0)
            {
                return (false, false);
            }

            const float epsilon = 0.001f;
            float minU = float.PositiveInfinity;
            float maxU = float.NegativeInfinity;
            float minV = float.PositiveInfinity;
            float maxV = float.NegativeInfinity;

            bool sampledAny = false;
            if (indices != null && indices.Length > 0)
            {
                for (int i = 0; i < indices.Length; i++)
                {
                    uint index = indices[i];
                    if (index >= uvs.Length)
                    {
                        continue;
                    }

                    var uv = uvs[index];
                    minU = Math.Min(minU, uv.X);
                    maxU = Math.Max(maxU, uv.X);
                    minV = Math.Min(minV, uv.Y);
                    maxV = Math.Max(maxV, uv.Y);
                    sampledAny = true;
                }
            }

            if (!sampledAny)
            {
                for (int i = 0; i < uvs.Length; i++)
                {
                    var uv = uvs[i];
                    minU = Math.Min(minU, uv.X);
                    maxU = Math.Max(maxU, uv.X);
                    minV = Math.Min(minV, uv.Y);
                    maxV = Math.Max(maxV, uv.Y);
                }
            }

            bool forceRepeatU = minU < -epsilon || maxU > 1.0f + epsilon;
            bool forceRepeatV = minV < -epsilon || maxV > 1.0f + epsilon;
            return (forceRepeatU, forceRepeatV);
        }
    }
}
