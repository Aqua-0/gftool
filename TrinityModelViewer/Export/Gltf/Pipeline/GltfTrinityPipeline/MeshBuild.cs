using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenTK.Mathematics;
using Trinity.Core.Flatbuffers.Gfx2;
using Trinity.Core.Flatbuffers.TR.Model;
using Trinity.Core.Flatbuffers.Utils;
using Trinity.Core.Utils;


namespace TrinityModelViewer.Export
{
	    internal static partial class GltfTrinityPipeline
	    {
	        private sealed class TrinityPrimitive
	        {
                internal sealed class MorphTarget
                {
                    public string Name = string.Empty;
                    public Vector3[] PositionDeltas = Array.Empty<Vector3>();
                    public Vector3[] NormalDeltas = Array.Empty<Vector3>();
                    public Vector4[] TangentDeltas = Array.Empty<Vector4>();
                }

	            public string Name = "Mesh";
	            public string MaterialName = "Material";
	            public int? TemplateShapeIndex;
	            public int? TemplatePartIndex;
	            public string? SourceNodeName;
	            public int SourcePrimitiveIndex;
	            public Vector3[] Positions = Array.Empty<Vector3>();
	            public Vector3[] Normals = Array.Empty<Vector3>();
	            public Vector4[] Tangents = Array.Empty<Vector4>();
	            public Vector4[] Colors = Array.Empty<Vector4>();
	            public Vector2[] Uv0 = Array.Empty<Vector2>();
	            public Vector4i[] JointIndices = Array.Empty<Vector4i>();
	            public Vector4 Weights = Vector4.UnitX;
	            public Vector4[] JointWeights = Array.Empty<Vector4>();
	            public uint[] Indices = Array.Empty<uint>();
	            public bool HasSkinning;
                public List<MorphTarget> MorphTargets = new List<MorphTarget>();
	        }

        private readonly struct Bounds3
        {
            public Bounds3(Vector3 min, Vector3 max)
            {
                Min = min;
                Max = max;
            }

            public Vector3 Min { get; }
            public Vector3 Max { get; }
        }

	        private static (TRMSH Trmsh, TRMBF Trmbf, TRBoundingBox Bounds) BuildTrinityMeshFilesFromTemplate(
	            IReadOnlyList<TrinityPrimitive> prims,
                byte[] templateTrmshBytes,
	            TRMSH templateTrmsh,
	            TRMBF templateTrmbf,
	            string outTrmbfPath,
	            HashSet<string> materialNames)
	        {
            if (templateTrmsh.Meshes == null || templateTrmsh.Meshes.Length == 0)
            {
                throw new InvalidOperationException("Template TRMSH has no meshes.");
            }
            if (templateTrmbf.TRMeshBuffers == null || templateTrmbf.TRMeshBuffers.Length == 0)
            {
                throw new InvalidOperationException("Template TRMBF has no mesh buffers.");
            }
            if (templateTrmsh.Meshes.Length != templateTrmbf.TRMeshBuffers.Length)
            {
                throw new InvalidOperationException($"Template TRMSH/TRMBF mismatch: meshes={templateTrmsh.Meshes.Length} buffers={templateTrmbf.TRMeshBuffers.Length}");
            }

            var primBySubmesh = new Dictionary<string, TrinityPrimitive>(StringComparer.OrdinalIgnoreCase);
            var primByTemplateIndex = new Dictionary<(int ShapeIndex, int PartIndex), TrinityPrimitive>();
            foreach (var prim in prims)
            {
                if (prim.TemplateShapeIndex.HasValue && prim.TemplatePartIndex.HasValue)
                {
                    primByTemplateIndex[(prim.TemplateShapeIndex.Value, prim.TemplatePartIndex.Value)] = prim;
                }

                if (!string.IsNullOrWhiteSpace(prim.SourceNodeName))
                {
                    primBySubmesh[$"{prim.SourceNodeName}:{prim.SourcePrimitiveIndex}"] = prim;
                }

                var key = TryGetImportedNodeName(prim.Name) ?? prim.Name;
                if (!primBySubmesh.ContainsKey(key))
                {
                    primBySubmesh[key] = prim;
                }
            }

                Gfx2Mesh? gfx2 = null;
                try
                {
                    if (templateTrmshBytes != null && templateTrmshBytes.Length > 0)
                    {
                        gfx2 = FlatBufferConverter.DeserializeFrom<Gfx2Mesh>(templateTrmshBytes);
                    }
                }
                catch
                {
                    gfx2 = null;
                }

	            var outMeshes = new TRMesh[templateTrmsh.Meshes.Length];
	            var outBuffers = new TRModelBuffer[templateTrmbf.TRMeshBuffers.Length];

            var globalMin = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            var globalMax = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

	            for (int meshShapeIndex = 0; meshShapeIndex < templateTrmsh.Meshes.Length; meshShapeIndex++)
	            {
	                var meshShape = templateTrmsh.Meshes[meshShapeIndex];
	                var meshBuffers = templateTrmbf.TRMeshBuffers[meshShapeIndex];
                if (meshShape == null || meshBuffers == null)
                {
                    throw new InvalidOperationException($"Template has null mesh or buffer at index {meshShapeIndex}.");
                }

	                var templateParts = meshShape.meshParts ?? Array.Empty<TRMeshPart>();
	                if (templateParts.Length == 0)
	                {
	                    outMeshes[meshShapeIndex] = meshShape;
	                    outBuffers[meshShapeIndex] = meshBuffers;
	                    continue;
	                }

	                // Each part is exported as a glTF primitive, with Trinity's mesh shape as the node name.
	                // `GltfReader` names primitives as "{MeshName}:{MaterialName}" so we can map them back deterministically.
	                var primsForShape = new List<(TRMeshPart Part, TrinityPrimitive Prim)>();
	                var usedPrims = new HashSet<TrinityPrimitive>();
	                for (int partIndex = 0; partIndex < templateParts.Length; partIndex++)
	                {
	                    var part = templateParts[partIndex];
	                    var submeshKey = $"{meshShape.Name}:{part.MaterialName}";
	                    if (!primByTemplateIndex.TryGetValue((meshShapeIndex, partIndex), out var prim) &&
	                        !primBySubmesh.TryGetValue(submeshKey, out prim) &&
	                        !primBySubmesh.TryGetValue($"{meshShape.Name}:{partIndex}", out prim))
	                    {
	                        throw new InvalidOperationException($"glTF is missing mesh part '{submeshKey}' required by the template TRMSH.");
	                    }

	                    primsForShape.Add((part, prim));
	                    usedPrims.Add(prim);
	                    if (!string.IsNullOrWhiteSpace(prim.MaterialName))
	                    {
	                        materialNames.Add(prim.MaterialName);
	                    }
	                }

	                // Allow additional glTF primitives to become new TRMeshParts when the template didn't have them.
	                // This is needed for edits like "split triangles to use an existing different material".
	                //
	                // We only do this when the primitive belongs to this mesh shape (node name matches).
	                // New parts reuse the first template part's unknown fields / vertexDeclarationIndex.
	                var extraPrims = prims
	                    .Where(p =>
	                    {
	                        var node = p.SourceNodeName ?? string.Empty;
	                        node = TryGetImportedNodeName(node) ?? node;
	                        return string.Equals(node, meshShape.Name, StringComparison.OrdinalIgnoreCase);
	                    })
	                    .Where(p => !usedPrims.Contains(p))
	                    .Where(p => p.Indices.Length > 0 && p.Positions.Length > 0)
	                    .OrderBy(p => p.SourcePrimitiveIndex)
	                    .ToList();

	                if (extraPrims.Count > 0)
	                {
	                    var defaults = templateParts[0];
	                    foreach (var prim in extraPrims)
	                    {
	                        if (string.IsNullOrWhiteSpace(prim.MaterialName))
	                        {
	                            throw new InvalidOperationException(
	                                $"glTF mesh '{meshShape.Name}' has an extra primitive with no material name; cannot map it to a TRMeshPart.");
	                        }

	                        primsForShape.Add((new TRMeshPart
	                        {
	                            unk3 = defaults.unk3,
	                            MaterialName = prim.MaterialName,
	                            vertexDeclarationIndex = defaults.vertexDeclarationIndex
	                        }, prim));
	                        usedPrims.Add(prim);
	                        materialNames.Add(prim.MaterialName);
	                    }
	                }

                // Determine the template vertex count from its base vertex layer (layer 0) stride/length.
                var templateVertexBuffers = meshBuffers.VertexBuffer ?? Array.Empty<TRBuffer>();
                if (templateVertexBuffers.Length == 0)
                {
                    throw new InvalidOperationException($"Template mesh '{meshShape.Name}' has no vertex buffers.");
                }

                var decls = meshShape.vertexDeclaration ?? Array.Empty<TRVertexDeclaration>();
                if (decls.Length == 0)
                {
                    throw new InvalidOperationException($"Template mesh '{meshShape.Name}' has no vertexDeclaration.");
                }

                int vertexLayerCount = templateVertexBuffers.Length;
                if (decls.Length < vertexLayerCount)
                {
                    throw new InvalidOperationException(
                        $"Template mesh '{meshShape.Name}' has {vertexLayerCount} vertex buffers but only {decls.Length} vertex layers in TRMSH.");
                }

                int baseStride = GetStride(decls[0], 0);
                if (baseStride <= 0)
                {
                    throw new InvalidOperationException($"Template mesh '{meshShape.Name}' has invalid stride for base vertex layer 0.");
                }

                int templateVertexCount = templateVertexBuffers[0]?.Bytes?.Length > 0 ? templateVertexBuffers[0].Bytes.Length / baseStride : 0;
                if (templateVertexCount <= 0)
                {
                    throw new InvalidOperationException($"Template mesh '{meshShape.Name}' has an empty vertex buffer for vertex layer 0.");
                }

                for (int layer = 1; layer < vertexLayerCount; layer++)
                {
                    var buf = templateVertexBuffers[layer];
                    if (buf?.Bytes == null)
                    {
                        continue;
                    }

                    int stride = GetStride(decls[layer], 0);
                    if (stride <= 0 && buf.Bytes.Length > 0 && (buf.Bytes.Length % templateVertexCount) == 0)
                    {
                        stride = buf.Bytes.Length / templateVertexCount;
                    }
                    if (stride <= 0)
                    {
                        continue;
                    }

                    int count = buf.Bytes.Length / stride;
                    if (count != templateVertexCount)
                    {
                        throw new InvalidOperationException(
                            $"Template mesh '{meshShape.Name}' has inconsistent vertex counts across vertex layers (layer0={templateVertexCount}, layer{layer}={count}).");
                    }
                }

                static bool PositionsExactlyEqual(Vector3[] a, Vector3[] b)
                {
                    if (ReferenceEquals(a, b))
                    {
                        return true;
                    }
                    if (a.Length != b.Length)
                    {
                        return false;
                    }
                    for (int i = 0; i < a.Length; i++)
                    {
                        if (a[i] != b[i])
                        {
                            return false;
                        }
                    }
                    return true;
                }

                // used a shared vertex pool. To keep the pipeline deterministic, merge such primitives into a single
                // vertex pool by concatenating their vertices and remapping indices.
                //
                // We detect this by checking whether the vertex streams are truly shared across primitives; if not,
                // concatenation is used to preserve the (primitive-local) vertex order and indices.
                bool needsConcatenation = false;
                var firstPrim = primsForShape[0].Prim;
                for (int i = 1; i < primsForShape.Count; i++)
                {
                    var prim = primsForShape[i].Prim;
                    if (prim.Positions.Length != firstPrim.Positions.Length || !PositionsExactlyEqual(prim.Positions, firstPrim.Positions))
                    {
                        needsConcatenation = true;
                        break;
                    }
                }
                var partInfos = new List<(TRMeshPart Part, TrinityPrimitive Prim, int VertexBase, int LocalVertexCount)>(primsForShape.Count);
                TrinityPrimitive canonical;
                int vertexCount;
                if (!needsConcatenation)
                {
                    canonical = primsForShape[0].Prim;
                    vertexCount = canonical.Positions.Length;
                    for (int i = 0; i < primsForShape.Count; i++)
                    {
                        partInfos.Add((primsForShape[i].Part, primsForShape[i].Prim, 0, vertexCount));
                    }

                    // glTF stores morph targets per-primitive. If the mesh shape uses a shared vertex pool across
                    // multiple primitives, only one primitive may actually carry the targets depending on exporter.
                    // Prefer any available target set so morph-enabled templates don't false-fail on import.
                    if (canonical.MorphTargets == null || canonical.MorphTargets.Count == 0)
                    {
                        for (int i = 0; i < primsForShape.Count; i++)
                        {
                            var mt = primsForShape[i].Prim.MorphTargets;
                            if (mt != null && mt.Count > 0)
                            {
                                canonical.MorphTargets = mt;
                                break;
                            }
                        }
                    }
                }
                else
                {
                    int total = 0;
                    for (int i = 0; i < primsForShape.Count; i++)
                    {
                        int count = primsForShape[i].Prim.Positions.Length;
                        if (count <= 0)
                        {
                            throw new InvalidOperationException($"glTF mesh '{meshShape.Name}' has an empty primitive '{primsForShape[i].Prim.Name}'.");
                        }
                        total += count;
                    }

                    vertexCount = total;

                    bool needsNormals = DeclHas(decls, TRVertexUsage.NORMAL, 0);
                    bool needsUv0 = DeclHas(decls, TRVertexUsage.TEX_COORD, 0);
                    bool needsSkin = DeclHas(decls, TRVertexUsage.BLEND_INDEX, 0) || DeclHas(decls, TRVertexUsage.BLEND_WEIGHTS, 0);

                    var positions = new Vector3[vertexCount];
                    var normals = needsNormals ? new Vector3[vertexCount] : Array.Empty<Vector3>();
                    var tangents = new Vector4[vertexCount];
                    var colors = new Vector4[vertexCount];
                    var uv0 = needsUv0 ? new Vector2[vertexCount] : Array.Empty<Vector2>();
                    var joints = needsSkin ? new Vector4i[vertexCount] : Array.Empty<Vector4i>();
                    var weights = needsSkin ? new Vector4[vertexCount] : Array.Empty<Vector4>();

                    // Defaults for missing streams.
                    if (needsNormals)
                    {
                        for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.UnitZ;
                    }
                    for (int i = 0; i < tangents.Length; i++) tangents[i] = new Vector4(1f, 0f, 0f, 1f);
                    for (int i = 0; i < colors.Length; i++) colors[i] = Vector4.One;
                    if (needsUv0)
                    {
                        for (int i = 0; i < uv0.Length; i++) uv0[i] = Vector2.Zero;
                    }
                    if (needsSkin)
                    {
                        for (int i = 0; i < weights.Length; i++) weights[i] = new Vector4(1f, 0f, 0f, 0f);
                    }

                    int cursor = 0;
                    bool anySkinning = false;
                    for (int i = 0; i < primsForShape.Count; i++)
                    {
                        var prim = primsForShape[i].Prim;
                        int localCount = prim.Positions.Length;
                        partInfos.Add((primsForShape[i].Part, prim, cursor, localCount));

                        Array.Copy(prim.Positions, 0, positions, cursor, localCount);

                        if (needsNormals && prim.Normals != null && prim.Normals.Length == localCount)
                        {
                            Array.Copy(prim.Normals, 0, normals, cursor, localCount);
                        }
                        if (prim.Tangents != null && prim.Tangents.Length == localCount)
                        {
                            Array.Copy(prim.Tangents, 0, tangents, cursor, localCount);
                        }
                        if (prim.Colors != null && prim.Colors.Length == localCount)
                        {
                            Array.Copy(prim.Colors, 0, colors, cursor, localCount);
                        }
                        if (needsUv0 && prim.Uv0 != null && prim.Uv0.Length == localCount)
                        {
                            Array.Copy(prim.Uv0, 0, uv0, cursor, localCount);
                        }
                        if (needsSkin && prim.HasSkinning && prim.JointIndices != null && prim.JointWeights != null &&
                            prim.JointIndices.Length == localCount && prim.JointWeights.Length == localCount)
                        {
                            Array.Copy(prim.JointIndices, 0, joints, cursor, localCount);
                            Array.Copy(prim.JointWeights, 0, weights, cursor, localCount);
                            anySkinning = true;
                        }

                        cursor += localCount;
                    }

                    canonical = new TrinityPrimitive
                    {
                        Name = meshShape.Name,
                        MaterialName = string.Empty,
                        Positions = positions,
                        Normals = normals,
                        Tangents = tangents,
                        Colors = colors,
                        Uv0 = uv0,
                        HasSkinning = needsSkin && anySkinning,
                        JointIndices = joints,
                        JointWeights = weights,
                        Indices = Array.Empty<uint>(),
                        MorphTargets = MergeMorphTargetsForConcatenatedMesh(partInfos, vertexCount)
                    };
                }

                if (vertexCount <= 0)
                {
                    throw new InvalidOperationException($"glTF mesh '{meshShape.Name}' has no vertices.");
                }

	                bool vertexCountChanged = vertexCount != templateVertexCount;
	                var outBoneWeights = BuildBoneWeights(meshShape, canonical, vertexCount, vertexCountChanged);

                bool templateHasAnyMorph =
                    (meshBuffers.MorphTargets != null && meshBuffers.MorphTargets.Length > 0) ||
                    (meshBuffers.VertexBuffer != null && meshBuffers.VertexBuffer.Length > 1);

                if (gfx2?.ItemList != null && meshShapeIndex >= 0 && meshShapeIndex < gfx2.ItemList.Length)
                {
                    var item = gfx2.ItemList[meshShapeIndex];
                    if ((item.BlendShapeInfoList != null && item.BlendShapeInfoList.Length > 0) ||
                        (item.PartialBlendShapeInfoList != null && item.PartialBlendShapeInfoList.Length > 0))
                    {
                        templateHasAnyMorph = true;
                    }
                }

                // Safety: if the mesh contains morph targets, changing vertex count would invalidate morph buffers.
                if (vertexCountChanged && templateHasAnyMorph && (canonical.MorphTargets == null || canonical.MorphTargets.Count == 0))
                {
                    throw new InvalidOperationException(
                        $"Mesh '{meshShape.Name}' contains morph targets; vertex-count changes are not supported unless the imported glTF also includes morph targets (shape keys).");
                }

                // Safety: if the mesh has multiple LOD index buffers in this TRMBF entry, changing vertex count would invalidate the other index buffers.
                var templateIndexBuffers = meshBuffers.IndexBuffer ?? Array.Empty<TRBuffer>();
                if (vertexCountChanged && templateIndexBuffers.Length > 1)
                {
                    throw new InvalidOperationException(
                        $"Mesh '{meshShape.Name}' contains multiple index buffers (LOD variants). Vertex-count changes are not supported for this mesh yet.");
                }

                static bool DeclHas(TRVertexDeclaration[] declList, TRVertexUsage usage, int layer)
                {
                    foreach (var d in declList)
                    {
                        if (d?.vertexElements == null)
                        {
                            continue;
                        }

                        foreach (var e in d.vertexElements)
                        {
                            if (e == null)
                            {
                                continue;
                            }

                            if (e.Usage == usage && (int)e.Layer == layer)
                            {
                                return true;
                            }
                        }
                    }

                    return false;
                }

	                if (vertexCountChanged)
	                {
	                    // If the template declares a stream, require the glTF to provide it when adding/removing vertices.
	                    // Otherwise, new vertices would be written with undefined/default values.
	                    if (DeclHas(decls, TRVertexUsage.NORMAL, 0) && (canonical.Normals == null || canonical.Normals.Length != vertexCount))
	                    {
	                        throw new InvalidOperationException($"Mesh '{meshShape.Name}' requires normals; the imported glTF is missing NORMAL.");
	                    }
	                    if (DeclHas(decls, TRVertexUsage.TEX_COORD, 0) && (canonical.Uv0 == null || canonical.Uv0.Length != vertexCount))
	                    {
	                        throw new InvalidOperationException($"Mesh '{meshShape.Name}' requires UV0; the imported glTF is missing TEXCOORD_0.");
	                    }
	                    bool requiresJointsForResize = DeclHas(decls, TRVertexUsage.BLEND_INDEX, 0);
	                    bool requiresWeights = DeclHas(decls, TRVertexUsage.BLEND_WEIGHTS, 0);
	                    if ((requiresJointsForResize || requiresWeights) && !canonical.HasSkinning)
	                    {
	                        throw new InvalidOperationException(
	                            $"Mesh '{meshShape.Name}' is skinned in the template, but the imported glTF has no skinning (JOINTS_0/WEIGHTS_0). " +
	                            "Make sure the mesh remains skinned (armature/skin modifier) and you export with skinning enabled.");
	                    }
	                }

                // The TRMBF VertexBuffer[] is keyed by *vertex layer id* (base layer 0, morph layers 1+),
                // and each layer contains one stream (stream_id == 0) in our observed assets.
                if (decls.Length < vertexLayerCount)
                {
                    throw new InvalidOperationException(
                        $"Template mesh '{meshShape.Name}' has {vertexLayerCount} vertex buffers but only {decls.Length} vertex layers in TRMSH.");
                }

                int[] layerStrides = new int[vertexLayerCount];
                for (int layer = 0; layer < vertexLayerCount; layer++)
                {
                    int stride = GetStride(decls[layer], 0);
                    if (stride <= 0)
                    {
                        // Fallback: infer stride from template buffer length and template vertex count.
                        // (Some TRMSH variants omit stream_list/stride in morph layers.)
                        var src = templateVertexBuffers[layer]?.Bytes;
                        if (src != null && src.Length > 0 && templateVertexCount > 0 && (src.Length % templateVertexCount) == 0)
                        {
                            stride = src.Length / templateVertexCount;
                        }
                    }
                    if (stride <= 0)
                    {
                        throw new InvalidOperationException($"Template mesh '{meshShape.Name}' has invalid stride for vertex layer {layer}.");
                    }
                    layerStrides[layer] = stride;
                }

                var outVertexBuffers = new TRBuffer[vertexLayerCount];
                for (int layer = 0; layer < vertexLayerCount; layer++)
                {
                    int newLen = vertexCount * layerStrides[layer];
                    var bytes = new byte[newLen];

                    bool preserveTemplateBytes = !needsConcatenation && !vertexCountChanged;
                    if (preserveTemplateBytes)
                    {
                        // Preserve existing per-vertex data for attributes we don't currently re-export (UV1+, binormals, etc.)
                        // only when the vertex pool is unchanged (same count, same layout).
                        var src = templateVertexBuffers[layer]?.Bytes;
                        if (src != null && src.Length > 0)
                        {
                            Buffer.BlockCopy(src, 0, bytes, 0, Math.Min(src.Length, bytes.Length));
                        }
                    }

                    outVertexBuffers[layer] = new TRBuffer { Bytes = bytes };
                }

                // Populate declared attributes for the base vertex layer (0). Keep morph layers intact unless glTF provides targets.
                if (decls[0]?.vertexElements != null && decls[0].vertexElements.Length > 0)
                {
                    foreach (var el in decls[0].vertexElements)
                    {
                        if (el == null)
                        {
                            continue;
                        }
                        // Only stream 0 is supported for writing; preserve other streams if present.
                        if (el.Slot != 0)
                        {
                            continue;
                        }

                        WriteVertexElement(
                            outVertexBuffers[0].Bytes,
                            layerStrides[0],
                            el,
                            canonical,
                            vertexCount);
                    }
                }

                // Clone morph target buffers from the template so we can selectively overwrite delta payloads.
                var outMorphTargets = CloneMorphTargets(meshBuffers.MorphTargets ?? Array.Empty<TRMorphTarget>());

                // If the imported glTF contains morph targets, write them back using mesh metadata (name -> buffer mapping).
                if (canonical.MorphTargets != null && canonical.MorphTargets.Count > 0)
                {
                    if (gfx2?.ItemList != null && meshShapeIndex >= 0 && meshShapeIndex < gfx2.ItemList.Length)
                    {
                        ApplyImportedMorphTargets(
                            canonical,
                            vertexCount,
                            meshShape.Name,
                            templateVertexBuffers,
                            templateVertexCount,
                            decls,
                            layerStrides,
                            outVertexBuffers,
                            outMorphTargets,
                            gfx2.ItemList[meshShapeIndex]);
                    }
                    else if (templateHasAnyMorph)
                    {
                        throw new InvalidOperationException(
                            $"Imported glTF contains morph targets for mesh '{meshShape.Name}', but morph metadata could not be read from the template TRMSH. " +
                            "Make sure you import using an original TRMSH/TRMBF pair with intact gfx2 blendshape metadata.");
                    }
                }

                // Build a single LOD0 index buffer by concatenating parts in template order and updating offsets.
                var indexType = meshShape.IndexType;
                int indexSize = indexType switch
                {
                    TRIndexFormat.BYTE => 1,
                    TRIndexFormat.SHORT => 2,
                    TRIndexFormat.INT => 4,
                    _ => 4
                };
                if (vertexCountChanged && indexType == TRIndexFormat.BYTE && vertexCount > byte.MaxValue)
                {
                    throw new InvalidOperationException(
                        $"Mesh '{meshShape.Name}' uses BYTE indices but the edited mesh has {vertexCount} vertices. Reduce vertex count below 255, or keep topology unchanged.");
                }
                if (vertexCountChanged && indexType == TRIndexFormat.SHORT && vertexCount > ushort.MaxValue)
                {
                    throw new InvalidOperationException(
                        $"Mesh '{meshShape.Name}' uses SHORT indices but the edited mesh has {vertexCount} vertices. Reduce vertex count below 65535, or keep topology unchanged.");
                }

	                var outParts = new TRMeshPart[primsForShape.Count];
	                int runningIndexOffset = 0;
	                var indexBytes = new List<byte>(capacity: primsForShape.Sum(p => p.Prim.Indices.Length) * indexSize);
	                for (int p = 0; p < primsForShape.Count; p++)
	                {
	                    var (part, prim, vertexBase, localVertexCount) = needsConcatenation ? partInfos[p] : (primsForShape[p].Part, primsForShape[p].Prim, 0, vertexCount);
	                    if (prim.Indices.Length == 0)
	                    {
	                        throw new InvalidOperationException($"glTF mesh part '{meshShape.Name}:{part.MaterialName}' has no indices.");
	                    }

                    foreach (var idx in prim.Indices)
                    {
                        if (idx >= (uint)localVertexCount)
                        {
                            throw new InvalidOperationException(
                                $"glTF mesh part '{meshShape.Name}:{part.MaterialName}' references out-of-range vertex index {idx} (vertexCount={localVertexCount}).");
                        }

                        uint adjusted = (uint)vertexBase + idx;
                        if (adjusted >= (uint)vertexCount)
                        {
                            throw new InvalidOperationException(
                                $"glTF mesh part '{meshShape.Name}:{part.MaterialName}' produced an invalid remapped index {adjusted} (vertexCount={vertexCount}).");
                        }

                        switch (indexType)
                        {
                            case TRIndexFormat.BYTE:
                                if (adjusted > byte.MaxValue)
                                {
                                    throw new InvalidOperationException($"Index {adjusted} does not fit in BYTE indices for mesh '{meshShape.Name}'.");
                                }
                                indexBytes.Add((byte)adjusted);
                                break;
                            case TRIndexFormat.SHORT:
                                if (adjusted > ushort.MaxValue)
                                {
                                    throw new InvalidOperationException($"Index {adjusted} does not fit in SHORT indices for mesh '{meshShape.Name}'.");
                                }
                                indexBytes.Add((byte)(adjusted & 0xFF));
                                indexBytes.Add((byte)((adjusted >> 8) & 0xFF));
                                break;
                            case TRIndexFormat.INT:
                            default:
                                indexBytes.Add((byte)(adjusted & 0xFF));
                                indexBytes.Add((byte)((adjusted >> 8) & 0xFF));
                                indexBytes.Add((byte)((adjusted >> 16) & 0xFF));
                                indexBytes.Add((byte)((adjusted >> 24) & 0xFF));
                                break;
                        }
                    }

	                    outParts[p] = new TRMeshPart
	                    {
	                        indexCount = prim.Indices.Length,
	                        indexOffset = runningIndexOffset,
	                        unk3 = part.unk3,
	                        MaterialName = prim.MaterialName,
	                        vertexDeclarationIndex = part.vertexDeclarationIndex
	                    };

                    runningIndexOffset += prim.Indices.Length;
                }

                var bounds = ComputeBounds(canonical.Positions);
                globalMin = Vector3.ComponentMin(globalMin, bounds.Min);
                globalMax = Vector3.ComponentMax(globalMax, bounds.Max);

                outMeshes[meshShapeIndex] = new TRMesh
                {
                    Name = meshShape.Name,
                    boundingBox = new TRBoundingBox
                    {
                        MinBound = new Vector3f { X = bounds.Min.X, Y = bounds.Min.Y, Z = bounds.Min.Z },
                        MaxBound = new Vector3f { X = bounds.Max.X, Y = bounds.Max.Y, Z = bounds.Max.Z }
                    },
                    IndexType = meshShape.IndexType,
                    vertexDeclaration = meshShape.vertexDeclaration,
                    meshParts = outParts,
                    res0 = meshShape.res0,
                    res1 = meshShape.res1,
                    res2 = meshShape.res2,
                    res3 = meshShape.res3,
                    clipSphere = new Sphere
                    {
                        X = (bounds.Min.X + bounds.Max.X) * 0.5f,
                        Y = (bounds.Min.Y + bounds.Max.Y) * 0.5f,
                        Z = (bounds.Min.Z + bounds.Max.Z) * 0.5f,
                        Radius = (bounds.Max - bounds.Min).Length * 0.5f
                    },
                    boneWeight = outBoneWeights,
                    MeshUnk7 = meshShape.MeshUnk7,
                    MeshName = meshShape.MeshName
                };

                var outIndexBuffers = templateIndexBuffers.Length > 0
                    ? templateIndexBuffers.Select((buf, i) => i == 0 ? new TRBuffer { Bytes = indexBytes.ToArray() } : buf).ToArray()
                    : new[] { new TRBuffer { Bytes = indexBytes.ToArray() } };

                outBuffers[meshShapeIndex] = new TRModelBuffer
                {
                    IndexBuffer = outIndexBuffers,
                    VertexBuffer = outVertexBuffers,
                    MorphTargets = outMorphTargets
                };
            }

            var resultBounds = new TRBoundingBox
            {
                MinBound = new Vector3f { X = globalMin.X, Y = globalMin.Y, Z = globalMin.Z },
                MaxBound = new Vector3f { X = globalMax.X, Y = globalMax.Y, Z = globalMax.Z }
            };

            var outTrmsh = new TRMSH
            {
                Version = templateTrmsh.Version,
                Meshes = outMeshes,
                bufferFilePath = outTrmbfPath.Replace('\\', '/')
            };

            var outTrmbf = new TRMBF
            {
                Field_00 = templateTrmbf.Field_00,
                TRMeshBuffers = outBuffers
            };

            return (outTrmsh, outTrmbf, resultBounds);
        }

        private static List<TrinityPrimitive.MorphTarget> MergeMorphTargetsForConcatenatedMesh(
            IReadOnlyList<(TRMeshPart Part, TrinityPrimitive Prim, int VertexBase, int LocalVertexCount)> partInfos,
            int totalVertexCount)
        {
            if (partInfos == null || partInfos.Count == 0 || totalVertexCount <= 0)
            {
                return new List<TrinityPrimitive.MorphTarget>();
            }

            var nameOrder = new List<string>();
            var nameSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (_, prim, _, _) in partInfos)
            {
                if (prim.MorphTargets == null || prim.MorphTargets.Count == 0)
                {
                    continue;
                }

                foreach (var mt in prim.MorphTargets)
                {
                    if (mt == null || string.IsNullOrWhiteSpace(mt.Name))
                    {
                        continue;
                    }
                    if (nameSet.Add(mt.Name))
                    {
                        nameOrder.Add(mt.Name);
                    }
                }
            }

            if (nameOrder.Count == 0)
            {
                return new List<TrinityPrimitive.MorphTarget>();
            }

            var merged = new List<TrinityPrimitive.MorphTarget>(nameOrder.Count);
            var byName = new Dictionary<string, TrinityPrimitive.MorphTarget>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in nameOrder)
            {
                var target = new TrinityPrimitive.MorphTarget
                {
                    Name = name,
                    PositionDeltas = new Vector3[totalVertexCount],
                    NormalDeltas = new Vector3[totalVertexCount],
                    TangentDeltas = new Vector4[totalVertexCount]
                };
                merged.Add(target);
                byName[name] = target;
            }

            foreach (var (_, prim, vertexBase, localCount) in partInfos)
            {
                if (prim.MorphTargets == null || prim.MorphTargets.Count == 0)
                {
                    continue;
                }

                foreach (var mt in prim.MorphTargets)
                {
                    if (mt == null || string.IsNullOrWhiteSpace(mt.Name))
                    {
                        continue;
                    }
                    if (!byName.TryGetValue(mt.Name, out var dst))
                    {
                        continue;
                    }

                    if (mt.PositionDeltas != null && mt.PositionDeltas.Length == localCount)
                    {
                        Array.Copy(mt.PositionDeltas, 0, dst.PositionDeltas, vertexBase, localCount);
                    }
                    if (mt.NormalDeltas != null && mt.NormalDeltas.Length == localCount)
                    {
                        Array.Copy(mt.NormalDeltas, 0, dst.NormalDeltas, vertexBase, localCount);
                    }
                    if (mt.TangentDeltas != null && mt.TangentDeltas.Length == localCount)
                    {
                        Array.Copy(mt.TangentDeltas, 0, dst.TangentDeltas, vertexBase, localCount);
                    }
                }
            }

            return merged;
        }

        private static TRMorphTarget[] CloneMorphTargets(TRMorphTarget[] src)
        {
            if (src == null || src.Length == 0)
            {
                return Array.Empty<TRMorphTarget>();
            }

            var dst = new TRMorphTarget[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                var mt = src[i];
                var bufs = mt?.morphBuffers;
                if (bufs == null || bufs.Length == 0)
                {
                    dst[i] = new TRMorphTarget { morphBuffers = Array.Empty<TRBuffer>() };
                    continue;
                }

                var outBufs = new TRBuffer[bufs.Length];
                for (int b = 0; b < bufs.Length; b++)
                {
                    var bytes = bufs[b]?.Bytes;
                    outBufs[b] = new TRBuffer { Bytes = bytes != null ? (byte[])bytes.Clone() : Array.Empty<byte>() };
                }
                dst[i] = new TRMorphTarget { morphBuffers = outBufs };
            }

            return dst;
        }

        private static void ApplyImportedMorphTargets(
            TrinityPrimitive canonical,
            int vertexCount,
            string meshShapeName,
            TRBuffer[] templateVertexBuffers,
            int templateVertexCount,
            TRVertexDeclaration[] vertexLayers,
            int[] layerStrides,
            TRBuffer[] outVertexBuffers,
            TRMorphTarget[] outMorphTargets,
            Gfx2MeshItem gfx2Item)
        {
            if (canonical.MorphTargets == null || canonical.MorphTargets.Count == 0)
            {
                return;
            }

            var basePositions = canonical.Positions ?? Array.Empty<Vector3>();
            var baseNormals = (canonical.Normals != null && canonical.Normals.Length == vertexCount) ? canonical.Normals : null;
            var baseTangents = (canonical.Tangents != null && canonical.Tangents.Length == vertexCount) ? canonical.Tangents : null;

            // Decode template base positions for "absolute vs delta" heuristics.
            Vector3[] templateBasePositions = Array.Empty<Vector3>();
            try
            {
                if (templateVertexBuffers.Length > 0 && templateVertexBuffers[0]?.Bytes != null &&
                    vertexLayers.Length > 0 && vertexLayers[0]?.vertexElements != null)
                {
                    var posEl = FindVertexElement(vertexLayers[0], TRVertexUsage.POSITION, 0);
                    if (posEl != null && posEl.Slot == 0)
                    {
                        templateBasePositions = DecodePositions(templateVertexBuffers[0].Bytes, layerStrides[0], posEl, templateVertexCount);
                    }
                }
            }
            catch
            {
                templateBasePositions = Array.Empty<Vector3>();
            }

            var fullByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (gfx2Item.BlendShapeInfoList != null)
            {
                foreach (var bs in gfx2Item.BlendShapeInfoList)
                {
                    if (bs == null || string.IsNullOrWhiteSpace(bs.Name))
                    {
                        continue;
                    }
                    fullByName[bs.Name] = (int)bs.VertexLayerId;
                }
            }

            var partialByName = new Dictionary<string, (int GroupIndex, int LayerId)>(StringComparer.OrdinalIgnoreCase);
            if (gfx2Item.PartialBlendShapeInfoList != null)
            {
                for (int groupIndex = 0; groupIndex < gfx2Item.PartialBlendShapeInfoList.Length; groupIndex++)
                {
                    var group = gfx2Item.PartialBlendShapeInfoList[groupIndex];
                    if (group?.BlendShapeInfoList == null || group.BlendShapeInfoList.Length == 0)
                    {
                        continue;
                    }
                    foreach (var bs in group.BlendShapeInfoList)
                    {
                        if (bs == null || string.IsNullOrWhiteSpace(bs.Name))
                        {
                            continue;
                        }
                        partialByName[bs.Name] = (groupIndex, (int)bs.VertexLayerId);
                    }
                }
            }

            var partialTargetsByGroup = new Dictionary<int, List<(TrinityPrimitive.MorphTarget Target, int LayerId)>>();

            foreach (var mt in canonical.MorphTargets)
            {
                if (mt == null || string.IsNullOrWhiteSpace(mt.Name))
                {
                    continue;
                }

                if (fullByName.TryGetValue(mt.Name, out int fullLayerId))
                {
                    TryApplyFullMorphTarget(mt, basePositions, baseNormals, baseTangents, templateVertexBuffers, templateBasePositions, vertexLayers, layerStrides, outVertexBuffers, meshShapeName, fullLayerId);
                    continue;
                }

                if (partialByName.TryGetValue(mt.Name, out var partial))
                {
                    if (!partialTargetsByGroup.TryGetValue(partial.GroupIndex, out var list))
                    {
                        list = new List<(TrinityPrimitive.MorphTarget Target, int LayerId)>();
                        partialTargetsByGroup[partial.GroupIndex] = list;
                    }
                    list.Add((mt, partial.LayerId));
                }
            }

            if (partialTargetsByGroup.Count == 0)
            {
                return;
            }

            foreach (var kvp in partialTargetsByGroup)
            {
                ApplyPartialMorphTargetsForGroup(
                    kvp.Key,
                    kvp.Value,
                    vertexCount,
                    templateVertexCount,
                    basePositions,
                    baseNormals,
                    baseTangents,
                    templateBasePositions,
                    outMorphTargets,
                    gfx2Item);
            }
        }

        private static void ApplyPartialMorphTargetsForGroup(
            int groupIndex,
            List<(TrinityPrimitive.MorphTarget Target, int LayerId)> targets,
            int vertexCount,
            int templateVertexCount,
            Vector3[] basePositions,
            Vector3[]? baseNormals,
            Vector4[]? baseTangents,
            Vector3[] templateBasePositions,
            TRMorphTarget[] outMorphTargets,
            Gfx2MeshItem gfx2Item)
        {
            if (targets == null || targets.Count == 0)
            {
                return;
            }

            if (groupIndex < 0 || groupIndex >= outMorphTargets.Length)
            {
                return;
            }

            var group = (gfx2Item.PartialBlendShapeInfoList != null && groupIndex < gfx2Item.PartialBlendShapeInfoList.Length)
                ? gfx2Item.PartialBlendShapeInfoList[groupIndex]
                : null;
            if (group?.VertexLayerList == null || group.VertexLayerList.Length == 0)
            {
                return;
            }

            var groupBuffers = outMorphTargets[groupIndex]?.morphBuffers;
            if (groupBuffers == null || groupBuffers.Length == 0)
            {
                return;
            }

            // Find which vertex layer contains the TargetVertexIndex mapping.
            int mappingLayer = -1;
            int mappingStride = 0;
            int mappingOffset = 0;
            Gfx2VertexAttributeFormat mappingFormat = Gfx2VertexAttributeFormat.Unknown;
            for (int layer = 0; layer < group.VertexLayerList.Length; layer++)
            {
                var vl = group.VertexLayerList[layer];
                if (vl?.AttributeList == null || vl.AttributeList.Length == 0)
                {
                    continue;
                }
                var map = vl.AttributeList.FirstOrDefault(a => a != null && a.Semantic == Gfx2VertexAttributeSemantic.TargetVertexIndex);
                if (map == null)
                {
                    continue;
                }

                mappingLayer = layer;
                mappingOffset = map.Offset;
                mappingFormat = map.Format;
                mappingStride = (vl.StreamList != null && vl.StreamList.Length > 0) ? vl.StreamList[0].Stride : 0;
                break;
            }
            if (mappingLayer < 0 || mappingStride <= 0 || mappingLayer >= groupBuffers.Length)
            {
                return;
            }

            // Determine the mapping list to use:
            // - If vertex order/count matches the template, preserve the template mapping (stable, deterministic).
            // - If vertex count changed, rebuild mapping indices from the imported deltas (union of affected vertices).
            int[] mappingIndices;
            byte[]? templateMappingBytes = groupBuffers[mappingLayer]?.Bytes;
            if (vertexCount == templateVertexCount && templateMappingBytes != null && templateMappingBytes.Length >= mappingStride)
            {
                mappingIndices = DecodeTargetVertexIndices(templateMappingBytes, mappingStride, mappingOffset, mappingFormat);
            }
            else
            {
                const float eps = 1e-5f;
                float epsSq = eps * eps;
                var affected = new bool[vertexCount];

                foreach (var (t, _) in targets)
                {
                    if (t.PositionDeltas == null || t.PositionDeltas.Length != vertexCount)
                    {
                        continue;
                    }

                    for (int v = 0; v < vertexCount; v++)
                    {
                        if (affected[v])
                        {
                            continue;
                        }

                        if (t.PositionDeltas[v].LengthSquared > epsSq)
                        {
                            affected[v] = true;
                        }
                    }
                }

                var list = new List<int>();
                for (int v = 0; v < vertexCount; v++)
                {
                    if (affected[v])
                    {
                        list.Add(v);
                    }
                }
                mappingIndices = list.ToArray();

                var newMappingBytes = new byte[mappingIndices.Length * mappingStride];
                for (int i = 0; i < mappingIndices.Length; i++)
                {
                    int off = (i * mappingStride) + mappingOffset;
                    if (off < 0 || off + 4 > newMappingBytes.Length)
                    {
                        continue;
                    }

                    int idx = mappingIndices[i];
                    if (mappingFormat == Gfx2VertexAttributeFormat.R32_SINT)
                    {
                        BinaryPrimitives.WriteInt32LittleEndian(newMappingBytes.AsSpan(off, 4), idx);
                    }
                    else if (mappingFormat == Gfx2VertexAttributeFormat.R32_UINT)
                    {
                        BinaryPrimitives.WriteUInt32LittleEndian(newMappingBytes.AsSpan(off, 4), unchecked((uint)idx));
                    }
                }
                groupBuffers[mappingLayer].Bytes = newMappingBytes;
            }

            if (mappingIndices.Length == 0)
            {
                // No affected vertices; keep buffers valid but empty.
                foreach (var (_, layerId) in targets)
                {
                    if (layerId >= 0 && layerId < groupBuffers.Length && groupBuffers[layerId] != null)
                    {
                        groupBuffers[layerId].Bytes = Array.Empty<byte>();
                    }
                }
                return;
            }

            // Determine whether the template stores absolute positions in these partial buffers.
            // Compute using the template's own mapping (if available), so we don't depend on the edited vertex order.
            var templateIsAbsoluteByLayer = new Dictionary<int, bool>();
            int[] templateMappingIndices = Array.Empty<int>();
            if (templateMappingBytes != null && templateMappingBytes.Length >= mappingStride)
            {
                templateMappingIndices = DecodeTargetVertexIndices(templateMappingBytes, mappingStride, mappingOffset, mappingFormat);
            }

            foreach (var (_, layerId) in targets)
            {
                if (templateIsAbsoluteByLayer.ContainsKey(layerId))
                {
                    continue;
                }

                bool isAbs = false;
                try
                {
                    if (templateBasePositions.Length > 0 &&
                        templateMappingIndices.Length > 0 &&
                        layerId > 0 &&
                        layerId < group.VertexLayerList.Length &&
                        layerId < groupBuffers.Length)
                    {
                        var targetLayer = group.VertexLayerList[layerId];
                        if (targetLayer?.StreamList != null && targetLayer.StreamList.Length > 0 &&
                            targetLayer?.AttributeList != null && targetLayer.AttributeList.Length > 0)
                        {
                            int stride = targetLayer.StreamList[0].Stride;
                            var bytes = groupBuffers[layerId]?.Bytes;
                            var posAtt = targetLayer.AttributeList.FirstOrDefault(a => a != null && a.Semantic == Gfx2VertexAttributeSemantic.Position);
                            if (stride > 0 && bytes != null && bytes.Length >= templateMappingIndices.Length * stride && posAtt != null && posAtt.StreamId == 0)
                            {
                                var posEl = new TRVertexElement { Slot = posAtt.StreamId, Usage = TRVertexUsage.POSITION, Layer = 0, Offset = posAtt.Offset, Format = (TRVertexFormat)(int)posAtt.Format };
                                var decoded = DecodePositions(bytes, stride, posEl, templateMappingIndices.Length);
                                isAbs = LooksLikeAbsolutePartialMorphPositions(decoded, templateBasePositions, templateMappingIndices);
                            }
                        }
                    }
                }
                catch
                {
                    isAbs = false;
                }

                templateIsAbsoluteByLayer[layerId] = isAbs;
            }

            // Write each imported target into its corresponding partial buffer layer, using the mapping list.
            foreach (var (mt, layerId) in targets)
            {
                if (layerId <= 0 || layerId >= group.VertexLayerList.Length || layerId >= groupBuffers.Length)
                {
                    continue;
                }

                if (mt.PositionDeltas == null || mt.PositionDeltas.Length != vertexCount)
                {
                    continue;
                }

                var targetLayer = group.VertexLayerList[layerId];
                if (targetLayer?.AttributeList == null || targetLayer.StreamList == null || targetLayer.StreamList.Length == 0)
                {
                    continue;
                }

                int stride = targetLayer.StreamList[0].Stride;
                if (stride <= 0)
                {
                    continue;
                }

                var posAtt = targetLayer.AttributeList.FirstOrDefault(a => a != null && a.Semantic == Gfx2VertexAttributeSemantic.Position);
                if (posAtt == null || posAtt.StreamId != 0)
                {
                    continue;
                }
                var nrmAtt = targetLayer.AttributeList.FirstOrDefault(a => a != null && a.Semantic == Gfx2VertexAttributeSemantic.Normal);
                var tanAtt = targetLayer.AttributeList.FirstOrDefault(a => a != null && a.Semantic == Gfx2VertexAttributeSemantic.Tangent);

                var dst = new byte[mappingIndices.Length * stride];
                groupBuffers[layerId].Bytes = dst;

                bool templateIsAbsolute = templateIsAbsoluteByLayer.TryGetValue(layerId, out var abs) && abs;

                for (int i = 0; i < mappingIndices.Length; i++)
                {
                    int j = mappingIndices[i];
                    if (j < 0 || j >= basePositions.Length)
                    {
                        continue;
                    }

                    int off = i * stride + posAtt.Offset;
                    var value = templateIsAbsolute ? (basePositions[j] + mt.PositionDeltas[j]) : mt.PositionDeltas[j];
                    WriteVector3(dst, off, (TRVertexFormat)(int)posAtt.Format, value);
                }

                if (nrmAtt != null && nrmAtt.StreamId == 0 && mt.NormalDeltas != null && mt.NormalDeltas.Length == vertexCount)
                {
                    for (int i = 0; i < mappingIndices.Length; i++)
                    {
                        int j = mappingIndices[i];
                        if (j < 0 || j >= basePositions.Length)
                        {
                            continue;
                        }
                        int off = i * stride + nrmAtt.Offset;
                        var baseNrm = baseNormals != null ? baseNormals[j] : Vector3.UnitZ;
                        var value = templateIsAbsolute ? (baseNrm + mt.NormalDeltas[j]) : mt.NormalDeltas[j];
                        WriteNormal(dst, off, (TRVertexFormat)(int)nrmAtt.Format, value);
                    }
                }

                if (tanAtt != null && tanAtt.StreamId == 0 && mt.TangentDeltas != null && mt.TangentDeltas.Length == vertexCount)
                {
                    for (int i = 0; i < mappingIndices.Length; i++)
                    {
                        int j = mappingIndices[i];
                        if (j < 0 || j >= basePositions.Length)
                        {
                            continue;
                        }
                        int off = i * stride + tanAtt.Offset;
                        var baseTan = baseTangents != null ? baseTangents[j] : new Vector4(1f, 0f, 0f, 1f);
                        var value = templateIsAbsolute ? (baseTan + mt.TangentDeltas[j]) : mt.TangentDeltas[j];
                        WriteTangent(dst, off, (TRVertexFormat)(int)tanAtt.Format, value);
                    }
                }
            }
        }

        private static void TryApplyFullMorphTarget(
            TrinityPrimitive.MorphTarget mt,
            Vector3[] basePositions,
            Vector3[]? baseNormals,
            Vector4[]? baseTangents,
            TRBuffer[] templateVertexBuffers,
            Vector3[] templateBasePositions,
            TRVertexDeclaration[] vertexLayers,
            int[] layerStrides,
            TRBuffer[] outVertexBuffers,
            string meshShapeName,
            int layerId)
        {
            if (layerId <= 0 || layerId >= outVertexBuffers.Length || layerId >= vertexLayers.Length)
            {
                return;
            }

            if (mt.PositionDeltas == null || mt.PositionDeltas.Length != basePositions.Length)
            {
                return;
            }

            var decl = vertexLayers[layerId];
            var posEl = FindVertexElement(decl, TRVertexUsage.POSITION, 0);
            if (posEl == null || posEl.Slot != 0)
            {
                return;
            }

            var nrmEl = FindVertexElement(decl, TRVertexUsage.NORMAL, 0);
            var tanEl = FindVertexElement(decl, TRVertexUsage.TANGENT, 0);

            int stride = layerStrides[layerId];
            var dst = outVertexBuffers[layerId].Bytes;
            if (dst == null || dst.Length < (basePositions.Length * stride))
            {
                return;
            }

            bool templateIsAbsolute = false;
            try
            {
                if (templateBasePositions.Length > 0 &&
                    layerId < templateVertexBuffers.Length &&
                    templateVertexBuffers[layerId]?.Bytes != null)
                {
                    var decoded = DecodePositions(templateVertexBuffers[layerId].Bytes, stride, posEl, templateBasePositions.Length);
                    templateIsAbsolute = LooksLikeAbsoluteMorphPositions(decoded, templateBasePositions);
                }
            }
            catch
            {
                templateIsAbsolute = false;
            }

            for (int v = 0; v < basePositions.Length; v++)
            {
                int offset = (v * stride) + posEl.Offset;
                var value = templateIsAbsolute ? (basePositions[v] + mt.PositionDeltas[v]) : mt.PositionDeltas[v];
                WriteVector3(dst, offset, posEl.Format, value);
            }

            if (nrmEl != null && nrmEl.Slot == 0 && mt.NormalDeltas != null && mt.NormalDeltas.Length == basePositions.Length)
            {
                for (int v = 0; v < basePositions.Length; v++)
                {
                    int offset = (v * stride) + nrmEl.Offset;
                    var baseNrm = baseNormals != null ? baseNormals[v] : Vector3.UnitZ;
                    var value = templateIsAbsolute ? (baseNrm + mt.NormalDeltas[v]) : mt.NormalDeltas[v];
                    WriteNormal(dst, offset, nrmEl.Format, value);
                }
            }

            if (tanEl != null && tanEl.Slot == 0 && mt.TangentDeltas != null && mt.TangentDeltas.Length == basePositions.Length)
            {
                for (int v = 0; v < basePositions.Length; v++)
                {
                    int offset = (v * stride) + tanEl.Offset;
                    var baseTan = baseTangents != null ? baseTangents[v] : new Vector4(1f, 0f, 0f, 1f);
                    var value = templateIsAbsolute ? (baseTan + mt.TangentDeltas[v]) : mt.TangentDeltas[v];
                    WriteTangent(dst, offset, tanEl.Format, value);
                }
            }
        }

        private static void TryApplyPartialMorphTarget(
            TrinityPrimitive.MorphTarget mt,
            Vector3[] basePositions,
            Vector3[]? baseNormals,
            Vector4[]? baseTangents,
            Vector3[] templateBasePositions,
            TRMorphTarget[] outMorphTargets,
            Gfx2MeshItem gfx2Item,
            string meshShapeName,
            int groupIndex,
            int layerId)
        {
            // Deprecated: partial morph writes are handled by ApplyPartialMorphTargetsForGroup so we can rebuild mapping
            // when vertex counts change. Kept for now to avoid breaking older call sites.
            if (groupIndex < 0 || groupIndex >= outMorphTargets.Length)
            {
                return;
            }

            if (mt.PositionDeltas == null || mt.PositionDeltas.Length != basePositions.Length)
            {
                return;
            }

            var group = (gfx2Item.PartialBlendShapeInfoList != null && groupIndex < gfx2Item.PartialBlendShapeInfoList.Length)
                ? gfx2Item.PartialBlendShapeInfoList[groupIndex]
                : null;
            if (group?.VertexLayerList == null || group.VertexLayerList.Length == 0)
            {
                return;
            }

            // Find which vertex layer contains the TargetVertexIndex mapping.
            int mappingLayer = -1;
            int mappingStride = 0;
            int mappingOffset = 0;
            Gfx2VertexAttributeFormat mappingFormat = Gfx2VertexAttributeFormat.Unknown;
            for (int layer = 0; layer < group.VertexLayerList.Length; layer++)
            {
                var vl = group.VertexLayerList[layer];
                if (vl?.AttributeList == null || vl.AttributeList.Length == 0)
                {
                    continue;
                }
                var map = vl.AttributeList.FirstOrDefault(a => a != null && a.Semantic == Gfx2VertexAttributeSemantic.TargetVertexIndex);
                if (map == null)
                {
                    continue;
                }

                mappingLayer = layer;
                mappingOffset = map.Offset;
                mappingFormat = map.Format;
                mappingStride = (vl.StreamList != null && vl.StreamList.Length > 0) ? vl.StreamList[0].Stride : 0;
                break;
            }
            if (mappingLayer < 0 || mappingStride <= 0)
            {
                return;
            }

            var groupBuffers = outMorphTargets[groupIndex]?.morphBuffers;
            if (groupBuffers == null || mappingLayer >= groupBuffers.Length || layerId < 0 || layerId >= groupBuffers.Length)
            {
                return;
            }

            var mappingBytes = groupBuffers[mappingLayer]?.Bytes;
            if (mappingBytes == null || mappingBytes.Length < mappingStride)
            {
                return;
            }

            int partialCount = mappingBytes.Length / mappingStride;
            if (partialCount <= 0)
            {
                return;
            }

            int[] targetVertexIndices = DecodeTargetVertexIndices(mappingBytes, mappingStride, mappingOffset, mappingFormat);
            if (targetVertexIndices.Length != partialCount)
            {
                return;
            }

            if (layerId <= 0 || layerId >= group.VertexLayerList.Length)
            {
                return;
            }

            var targetLayer = group.VertexLayerList[layerId];
            if (targetLayer?.AttributeList == null || targetLayer.StreamList == null || targetLayer.StreamList.Length == 0)
            {
                return;
            }

            int stride = targetLayer.StreamList[0].Stride;
            if (stride <= 0)
            {
                return;
            }

            var dst = groupBuffers[layerId]?.Bytes;
            if (dst == null || dst.Length < (partialCount * stride))
            {
                return;
            }

            var posAtt = targetLayer.AttributeList.FirstOrDefault(a => a != null && a.Semantic == Gfx2VertexAttributeSemantic.Position);
            if (posAtt == null || posAtt.StreamId != 0)
            {
                return;
            }
            var nrmAtt = targetLayer.AttributeList.FirstOrDefault(a => a != null && a.Semantic == Gfx2VertexAttributeSemantic.Normal);
            var tanAtt = targetLayer.AttributeList.FirstOrDefault(a => a != null && a.Semantic == Gfx2VertexAttributeSemantic.Tangent);

            bool templateIsAbsolute = false;
            try
            {
                if (templateBasePositions.Length > 0 && dst.Length >= partialCount * stride)
                {
                    var posEl = new TRVertexElement { Slot = posAtt.StreamId, Usage = TRVertexUsage.POSITION, Layer = 0, Offset = posAtt.Offset, Format = (TRVertexFormat)(int)posAtt.Format };
                    var decoded = DecodePositions(dst, stride, posEl, partialCount);
                    templateIsAbsolute = LooksLikeAbsolutePartialMorphPositions(decoded, templateBasePositions, targetVertexIndices);
                }
            }
            catch
            {
                templateIsAbsolute = false;
            }

            for (int i = 0; i < partialCount; i++)
            {
                int j = targetVertexIndices[i];
                if (j < 0 || j >= basePositions.Length)
                {
                    continue;
                }

                int off = i * stride + posAtt.Offset;
                var value = templateIsAbsolute ? (basePositions[j] + mt.PositionDeltas[j]) : mt.PositionDeltas[j];
                WriteVector3(dst, off, (TRVertexFormat)(int)posAtt.Format, value);
            }

            if (nrmAtt != null && nrmAtt.StreamId == 0 && mt.NormalDeltas != null && mt.NormalDeltas.Length == basePositions.Length)
            {
                for (int i = 0; i < partialCount; i++)
                {
                    int j = targetVertexIndices[i];
                    if (j < 0 || j >= basePositions.Length)
                    {
                        continue;
                    }
                    int off = i * stride + nrmAtt.Offset;
                    var baseNrm = baseNormals != null ? baseNormals[j] : Vector3.UnitZ;
                    var value = templateIsAbsolute ? (baseNrm + mt.NormalDeltas[j]) : mt.NormalDeltas[j];
                    WriteNormal(dst, off, (TRVertexFormat)(int)nrmAtt.Format, value);
                }
            }

            if (tanAtt != null && tanAtt.StreamId == 0 && mt.TangentDeltas != null && mt.TangentDeltas.Length == basePositions.Length)
            {
                for (int i = 0; i < partialCount; i++)
                {
                    int j = targetVertexIndices[i];
                    if (j < 0 || j >= basePositions.Length)
                    {
                        continue;
                    }
                    int off = i * stride + tanAtt.Offset;
                    var baseTan = baseTangents != null ? baseTangents[j] : new Vector4(1f, 0f, 0f, 1f);
                    var value = templateIsAbsolute ? (baseTan + mt.TangentDeltas[j]) : mt.TangentDeltas[j];
                    WriteTangent(dst, off, (TRVertexFormat)(int)tanAtt.Format, value);
                }
            }
        }

        private static TRVertexElement? FindVertexElement(TRVertexDeclaration decl, TRVertexUsage usage, int layer)
        {
            if (decl?.vertexElements == null)
            {
                return null;
            }

            foreach (var el in decl.vertexElements)
            {
                if (el == null)
                {
                    continue;
                }
                if (el.Usage == usage && el.Layer == layer)
                {
                    return el;
                }
            }

            return null;
        }

        private static Vector3[] DecodePositions(byte[] bytes, int stride, TRVertexElement posEl, int vertexCount)
        {
            if (bytes == null || bytes.Length == 0 || stride <= 0 || vertexCount <= 0)
            {
                return Array.Empty<Vector3>();
            }

            var result = new Vector3[vertexCount];
            for (int v = 0; v < vertexCount; v++)
            {
                int off = v * stride + posEl.Offset;
                result[v] = ReadVector3(bytes, off, posEl.Format);
            }
            return result;
        }

        private static Vector3 ReadVector3(byte[] src, int offset, TRVertexFormat format)
        {
            switch (format)
            {
                case TRVertexFormat.X32_Y32_Z32_FLOAT:
                    return new Vector3(
                        BinaryPrimitives.ReadSingleLittleEndian(src.AsSpan(offset + 0, 4)),
                        BinaryPrimitives.ReadSingleLittleEndian(src.AsSpan(offset + 4, 4)),
                        BinaryPrimitives.ReadSingleLittleEndian(src.AsSpan(offset + 8, 4)));
                case TRVertexFormat.W16_X16_Y16_Z16_FLOAT:
                {
                    float x = (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(src.AsSpan(offset + 0, 2)));
                    float y = (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(src.AsSpan(offset + 2, 2)));
                    float z = (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(src.AsSpan(offset + 4, 2)));
                    return new Vector3(x, y, z);
                }
                default:
                    // Unknown/unsupported encoding for heuristics. Return 0 to avoid misclassification.
                    return Vector3.Zero;
            }
        }

        private static int[] DecodeTargetVertexIndices(byte[] bytes, int stride, int offset, Gfx2VertexAttributeFormat format)
        {
            if (bytes == null || bytes.Length == 0 || stride <= 0)
            {
                return Array.Empty<int>();
            }

            int count = bytes.Length / stride;
            var indices = new int[count];
            for (int i = 0; i < count; i++)
            {
                int off = i * stride + offset;
                if (off < 0 || off + 4 > bytes.Length)
                {
                    indices[i] = -1;
                    continue;
                }

                indices[i] = format switch
                {
                    Gfx2VertexAttributeFormat.R32_SINT => BitConverter.ToInt32(bytes, off),
                    Gfx2VertexAttributeFormat.R32_UINT => unchecked((int)BitConverter.ToUInt32(bytes, off)),
                    _ => -1
                };
            }
            return indices;
        }

        private static bool LooksLikeAbsoluteMorphPositions(Vector3[] decodedPositions, Vector3[] basePositions)
        {
            if (decodedPositions == null || basePositions == null)
            {
                return false;
            }

            int count = Math.Min(decodedPositions.Length, basePositions.Length);
            if (count <= 0)
            {
                return false;
            }

            int sample = Math.Min(64, count);
            float maxBase = 0.0f;
            float maxDecoded = 0.0f;
            float maxDelta = 0.0f;

            for (int i = 0; i < sample; i++)
            {
                var b = basePositions[i];
                var d = decodedPositions[i];
                var delta = d - b;

                maxBase = MathF.Max(maxBase, b.Length);
                maxDecoded = MathF.Max(maxDecoded, d.Length);
                maxDelta = MathF.Max(maxDelta, delta.Length);
            }

            if (maxBase <= 0.000001f || maxDecoded <= 0.000001f)
            {
                return false;
            }

            bool similarScale = maxDecoded >= maxBase * 0.5f && maxDecoded <= maxBase * 1.5f;
            bool deltaMuchSmaller = maxDelta <= maxBase * 0.25f;
            return similarScale && deltaMuchSmaller;
        }

        private static bool LooksLikeAbsolutePartialMorphPositions(Vector3[] decodedPositions, Vector3[] basePositions, int[] targetVertexIndices)
        {
            if (decodedPositions == null || basePositions == null || targetVertexIndices == null)
            {
                return false;
            }

            int count = Math.Min(decodedPositions.Length, targetVertexIndices.Length);
            if (count <= 0)
            {
                return false;
            }

            int sample = Math.Min(64, count);
            float maxBase = 0.0f;
            float maxDecoded = 0.0f;
            float maxDelta = 0.0f;

            int taken = 0;
            for (int i = 0; i < count && taken < sample; i++)
            {
                int j = targetVertexIndices[i];
                if (j < 0 || j >= basePositions.Length)
                {
                    continue;
                }

                var b = basePositions[j];
                var d = decodedPositions[i];
                var delta = d - b;

                maxBase = MathF.Max(maxBase, b.Length);
                maxDecoded = MathF.Max(maxDecoded, d.Length);
                maxDelta = MathF.Max(maxDelta, delta.Length);
                taken++;
            }

            if (taken == 0 || maxBase <= 0.000001f || maxDecoded <= 0.000001f)
            {
                return false;
            }

            bool similarScale = maxDecoded >= maxBase * 0.5f && maxDecoded <= maxBase * 1.5f;
            bool deltaMuchSmaller = maxDelta <= maxBase * 0.25f;
            return similarScale && deltaMuchSmaller;
        }

	        private static TRBoneWeight[]? BuildBoneWeights(TRMesh meshShape, TrinityPrimitive canonical, int vertexCount, bool vertexCountChanged)
	        {
	            // Many Trinity meshes (notably protag clothing with connected skinning palettes) rely on the template's
	            // `boneWeight` table semantics for skinning. Rebuilding this table from glTF joints can silently change
	            // what BLEND_INDEX is supposed to mean (palette vs local table vs rig index), causing bad deformation.
	            // Preserve the template's table unless there wasn't one.
	            if (meshShape.boneWeight != null && meshShape.boneWeight.Length > 0)
	            {
	                return meshShape.boneWeight;
	            }

            // If there is no skinning on the imported primitive, keep whatever the template had.
            if (!canonical.HasSkinning || canonical.JointIndices == null || canonical.JointWeights == null)
            {
                return meshShape.boneWeight;
            }

            // If the template had no bone weights and the vertex count didn't change, leave it alone to avoid surprises.
            if ((meshShape.boneWeight == null || meshShape.boneWeight.Length == 0) && !vertexCountChanged)
            {
                return meshShape.boneWeight;
            }

            if (canonical.JointIndices.Length != vertexCount || canonical.JointWeights.Length != vertexCount)
            {
                return meshShape.boneWeight;
            }

            var totals = new Dictionary<int, float>();
            for (int i = 0; i < vertexCount; i++)
            {
                var j = canonical.JointIndices[i];
                var w = canonical.JointWeights[i];

                void Add(int joint, float weight)
                {
                    if (weight <= 0.0000001f)
                    {
                        return;
                    }
                    if (joint < 0)
                    {
                        return;
                    }
                    totals[joint] = totals.TryGetValue(joint, out var existing) ? existing + weight : weight;
                }

                Add(j.X, w.X);
                Add(j.Y, w.Y);
                Add(j.Z, w.Z);
                Add(j.W, w.W);
            }

            if (totals.Count == 0)
            {
                return meshShape.boneWeight;
            }

            return totals
                .OrderBy(kvp => kvp.Key)
                .Select(kvp => new TRBoneWeight { RigIndex = kvp.Key, RigWeight = kvp.Value })
                .ToArray();
        }

        private static string? TryGetImportedNodeName(string? importedName)
        {
            if (string.IsNullOrWhiteSpace(importedName))
            {
                return null;
            }

            int last = importedName.LastIndexOf('_');
            if (last <= 0 || last >= importedName.Length - 1)
            {
                return importedName;
            }

            var suffix = importedName.Substring(last + 1);
            if (!suffix.All(char.IsDigit))
            {
                return importedName;
            }

            return importedName.Substring(0, last);
        }

        private static Bounds3 ComputeBounds(Vector3[] positions)
        {
            var min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
            for (int i = 0; i < positions.Length; i++)
            {
                min = Vector3.ComponentMin(min, positions[i]);
                max = Vector3.ComponentMax(max, positions[i]);
            }
            return new Bounds3(min, max);
        }
    }
}
