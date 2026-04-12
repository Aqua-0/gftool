using GFTool.Core.Utils;
using GFTool.Renderer.Core;
using GFTool.Renderer.Core.Graphics;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using Trinity.Core.Flatbuffers.TR.Model;
using Trinity.Core.Utils;
using System.IO;
using System;
using Trinity.Core.Assets;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Trinity.Core.Flatbuffers.Gfx2;


namespace GFTool.Renderer.Scene.GraphicsObjects
{
	    public partial class Model : RefObject
	    {
        private void ParseMeshBuffer(TRVertexDeclaration vertDesc, TRBuffer[] vertexBuffers, TRBuffer indexBuf, TRIndexFormat polyType, long start, long count, TRBoneWeight[]? boneWeights, string meshName)
        {
            if (vertexBuffers == null || vertexBuffers.Length == 0)
            {
                return;
            }

            var posElement = vertDesc.vertexElements.FirstOrDefault(e => e.Usage == TRVertexUsage.POSITION);
            if (posElement == null)
            {
                return;
            }

            // TRMSH vertex elements use:
            // - Slot: which vertex buffer/stride to read from
            // - Layer: semantic "layer" (ex: TEX_COORD0/1, COLOR0/1, BLEND_INDEX0/1, ...)
            //
            // Some external TRMSH/TRMBF tooling treats AttributeLayer as the UV set index.
            // Using Layer as the vertex buffer index causes UV1+ (and other layered attributes) to be read
            // from the wrong buffer (often out-of-range), making them appear missing/zeroed.
            var posBuffer = GetVertexBuffer(vertexBuffers, posElement.Slot);
            if (posBuffer == null)
            {
                return;
            }

            var posStride = GetStride(vertDesc, posElement.Slot);
            if (posStride <= 0)
            {
                return;
            }

            int vertexCount = posBuffer.Bytes.Length / posStride;
            if (vertexCount <= 0)
            {
                return;
            }

            Vector3[] pos = new Vector3[vertexCount];
            Vector3[] norm = new Vector3[vertexCount];
            Vector2[] uv = new Vector2[vertexCount];
            Vector4[] color = new Vector4[vertexCount];
            Vector4[] tangent = new Vector4[vertexCount];
            Vector3[] binormal = new Vector3[vertexCount];
            Vector4[] blendIndices = new Vector4[vertexCount];
            Vector4[] blendWeights = new Vector4[vertexCount];
            bool hasNormals = false;
            bool hasUvs = false;
            bool hasColors = false;
            bool hasTangents = false;
            bool hasBinormals = false;
            bool hasBlendIndices = false;
            bool hasBlendWeights = false;
            TRVertexFormat? blendIndexFormat = null;
            TRVertexFormat? blendWeightFormat = null;
            blendIndexStats = new BlendIndexStats();

            List<uint> indices = new List<uint>();
            long currPos = 0;

            var blendIndexStreams = new List<Vector4[]>();
            var blendWeightStreams = new List<Vector4[]>();

            var uvStreams = new List<Vector2[]>();
            bool colorElementConsumed = false;

            for (int i = 0; i < vertDesc.vertexElements.Length; i++)
            {
                var att = vertDesc.vertexElements[i];
                var buffer = GetVertexBuffer(vertexBuffers, att.Slot);
                if (buffer == null)
                {
                    continue;
                }

                var stride = GetStride(vertDesc, att.Slot);
                if (stride <= 0)
                {
                    continue;
                }

                int? blendIndexStreamIndex = null;
                int? blendWeightStreamIndex = null;
                int? uvStreamIndex = null;
                if (att.Usage == TRVertexUsage.BLEND_INDEX)
                {
                    int layer = Math.Max(att.Layer, 0);
                    EnsureBlendStream(blendIndexStreams, layer, vertexCount);
                    blendIndexStreamIndex = layer;
                }
                else if (att.Usage == TRVertexUsage.BLEND_WEIGHTS)
                {
                    int layer = Math.Max(att.Layer, 0);
                    EnsureBlendStream(blendWeightStreams, layer, vertexCount);
                    blendWeightStreamIndex = layer;
                }
                else if (att.Usage == TRVertexUsage.TEX_COORD)
                {
                    int layer = Math.Max(att.Layer, 0);
                    EnsureUvStream(uvStreams, layer, vertexCount);
                    uvStreamIndex = layer;
                }

                for (int v = 0; v < vertexCount; v++)
                {
                    int offset = (v * stride) + att.Offset;
                    if (!HasBytes(buffer.Bytes, offset, att.Format))
                    {
                        continue;
                    }

                    switch (att.Usage)
                    {
                        case TRVertexUsage.POSITION:
                            pos[v] = ReadVector3(buffer.Bytes, offset, att.Format);
                            break;
                        case TRVertexUsage.NORMAL:
                            norm[v] = ReadNormal(buffer.Bytes, offset, att.Format);
                            hasNormals = true;
                            break;
                        case TRVertexUsage.TEX_COORD:
                            if (uvStreamIndex.HasValue)
                            {
                                uvStreams[uvStreamIndex.Value][v] = ReadVector2(buffer.Bytes, offset, att.Format);
                            }
                            hasUvs = true;
                            break;
                        case TRVertexUsage.COLOR:
                            if (colorElementConsumed)
                            {
                                break;
                            }
                            color[v] = ReadColor(buffer.Bytes, offset, att.Format);
                            hasColors = true;
                            colorElementConsumed = true;
                            break;
                        case TRVertexUsage.TANGENT:
                            tangent[v] = ReadTangent(buffer.Bytes, offset, att.Format);
                            hasTangents = true;
                            break;
                        case TRVertexUsage.BINORMAL:
                            binormal[v] = ReadNormal(buffer.Bytes, offset, att.Format);
                            hasBinormals = true;
                            break;
                        case TRVertexUsage.BLEND_INDEX:
                            if (blendIndexStreamIndex.HasValue)
                            {
                                blendIndexStreams[blendIndexStreamIndex.Value][v] = ReadBlendIndices(buffer.Bytes, offset, att.Format);
                            }
                            hasBlendIndices = true;
                            blendIndexFormat ??= att.Format;
                            break;
                        case TRVertexUsage.BLEND_WEIGHTS:
                            if (blendWeightStreamIndex.HasValue)
                            {
                                blendWeightStreams[blendWeightStreamIndex.Value][v] = ReadBlendWeights(buffer.Bytes, offset, att.Format);
                            }
                            hasBlendWeights = true;
                            blendWeightFormat ??= att.Format;
                            break;
                    }
                }
            }

            if (hasBlendIndices && blendIndexStreams.Count > 0)
            {
                blendIndices = blendIndexStreams[0];
            }

            if (hasBlendWeights && blendWeightStreams.Count > 0)
            {
                blendWeights = blendWeightStreams[0];
            }

            Vector2[] uv0 = uv;
            Vector2[] uv1 = new Vector2[vertexCount];
            if (hasUvs && uvStreams.Count > 0)
            {
                uv0 = uvStreams[0];
                if (uvStreams.Count > 1)
                {
                    uv1 = uvStreams[1];
                }
            }

            bool hasUv1 = false;
            if (hasUvs && uvStreams.Count > 1)
            {
                for (int v = 0; v < uv1.Length; v++)
                {
                    if (uv1[v].LengthSquared > 0.0000001f)
                    {
                        hasUv1 = true;
                        break;
                    }
                }
            }

            // Some meshes carry multiple BLEND INDEX and BLEND WEIGHTS streams (usually 8 influences).
            // Shaders only support 4, so the top 4 weights per vertex are kept.
            if ((blendIndexStreams.Count > 1 || blendWeightStreams.Count > 1) && hasBlendIndices && hasBlendWeights)
            {
                int streamCount = Math.Min(blendIndexStreams.Count, blendWeightStreams.Count);
                if (streamCount > 1)
                {
                    CollapseBlendStreams(blendIndexStreams, blendWeightStreams, streamCount, out blendIndices, out blendWeights);
                }
            }

            if (hasBlendIndices)
            {
                int maxIndex = 0;
                for (int v = 0; v < vertexCount; v++)
                {
                    var idx = blendIndices[v];
                    maxIndex = Math.Max(maxIndex, (int)MathF.Max(MathF.Max(idx.X, idx.Y), MathF.Max(idx.Z, idx.W)));
                }

                blendIndexStats = new BlendIndexStats
                {
                    VertexCount = vertexCount,
                    MaxIndex = maxIndex
                };
            }

            Positions.Add(pos);
            Normals.Add(hasNormals ? norm : new Vector3[vertexCount]);
            UVs.Add(hasUvs ? uv0 : new Vector2[vertexCount]);
            UVs2.Add(hasUvs ? uv1 : new Vector2[vertexCount]);
            HasUv1.Add(hasUv1);
            if (!hasColors)
            {
                for (int v = 0; v < color.Length; v++)
                {
                    color[v] = Vector4.One;
                }
            }
            Colors.Add(color);
            HasVertexColors.Add(hasColors);
            if (!hasTangents)
            {
                for (int v = 0; v < tangent.Length; v++)
                {
                    tangent[v] = new Vector4(1f, 0f, 0f, 1f);
                }
            }
            Tangents.Add(tangent);
            HasTangents.Add(hasTangents);
            if (!hasBinormals)
            {
                for (int v = 0; v < binormal.Length; v++)
                {
                    binormal[v] = Vector3.UnitY;
                }
            }
            Binormals.Add(binormal);
            HasBinormals.Add(hasBinormals);
            BlendIndicies.Add(blendIndices);
            BlendIndiciesOriginal.Add(blendIndices.ToArray());
            BlendWeights.Add(blendWeights);
            BlendBoneWeights.Add(boneWeights);
            BlendMeshNames.Add(meshName);
            HasSkinning.Add(hasBlendIndices && hasBlendWeights);
            if (MessageHandler.Instance.DebugLogsEnabled && (hasBlendIndices || hasBlendWeights))
            {
                MessageHandler.Instance.AddMessage(
                    MessageType.LOG,
                    $"[SkinFmt] mesh={meshName} blendIndexFmt={(blendIndexFormat?.ToString() ?? "<none>")} blendWeightFmt={(blendWeightFormat?.ToString() ?? "<none>")}");
            }

            //Parse index buffer
            using (var indBuf = new BinaryReader(new MemoryStream(indexBuf.Bytes)))
            {
                int indsize = (1 << (int)polyType);
                currPos = start * indsize;
                indBuf.BaseStream.Position = currPos;
                while (currPos < (start + count) * indsize)
                {
                    switch (polyType)
                    {
                        case TRIndexFormat.BYTE: indices.Add(indBuf.ReadByte()); break;
                        case TRIndexFormat.SHORT: indices.Add(indBuf.ReadUInt16()); break;
                        case TRIndexFormat.INT: indices.Add(indBuf.ReadUInt32()); break;
                    }
                    currPos += indsize;
                }
                Indices.Add(indices.ToArray());
            }

        }

        private void ParseMesh(string file)
        {
            var mshBytes = assetProvider.ReadAllBytes(file);
            var msh = FlatBufferConverter.DeserializeFrom<TRMSH>(mshBytes);
            var buffers = LoadFlat<TRMBF>(modelPath.Combine(msh.bufferFilePath)).TRMeshBuffers;

            // Best-effort: parse morph metadata (names + vertex-layer mapping) from the mesh metadata schema.
            // The main renderer path does not depend on this, and it may fail for non-matching variants.
            TryRegisterFullMorphTargets(mshBytes, msh, buffers);

            var shapeCnt = msh.Meshes.Count();
            for (int i = 0; i < shapeCnt; i++)
            {
                var meshShape = msh.Meshes[i];
                var vertBufs = buffers[i].VertexBuffer;
                var indexBuf = buffers[i].IndexBuffer[0]; //LOD0
                var polyType = meshShape.IndexType;
                int boneWeightCount = meshShape.boneWeight?.Length ?? 0;

                if (MessageHandler.Instance.DebugLogsEnabled)
                {
                    MessageHandler.Instance.AddMessage(
                        MessageType.LOG,
                        $"[TRMSH] mesh={meshShape.Name} unk7='{meshShape.MeshUnk7}' meshName='{meshShape.MeshName}' parts={(meshShape.meshParts?.Length ?? 0)}");
                }

                if (meshShape.meshParts == null || meshShape.meshParts.Length == 0)
                {
                    continue;
                }

                foreach (var part in meshShape.meshParts)
                {
                    MaterialNames.Add(part.MaterialName);
                    SubmeshNames.Add($"{meshShape.Name}:{part.MaterialName}");
                    SubmeshParentNodeNames.Add(FirstNonEmpty(meshShape.MeshUnk7, meshShape.MeshName));
                    int declIndex = part.vertexDeclarationIndex;
                    if (declIndex < 0 || declIndex >= meshShape.vertexDeclaration.Length)
                    {
                        declIndex = 0;
                    }
                    ParseMeshBuffer(meshShape.vertexDeclaration[declIndex], vertBufs, indexBuf, meshShape.IndexType, part.indexOffset, part.indexCount, meshShape.boneWeight, meshShape.Name);
                    if (MessageHandler.Instance.DebugLogsEnabled)
                    {
                        int submeshIndex = Positions.Count - 1;
                        bool hasSkin = HasSkinning.Count > submeshIndex && HasSkinning[submeshIndex];
                        string? parentName = SubmeshParentNodeNames.Count > submeshIndex ? SubmeshParentNodeNames[submeshIndex] : null;
                        MessageHandler.Instance.AddMessage(
                            MessageType.LOG,
                            $"[TRMSH] submesh={submeshIndex} name='{SubmeshNames[submeshIndex]}' skinning={hasSkin} parent='{parentName}'");
                    }
                }

                if (blendIndexStats != null)
                {
                    if (MessageHandler.Instance.DebugLogsEnabled)
                    {
                        MessageHandler.Instance.AddMessage(
                            MessageType.LOG,
                            $"[Skin] Mesh={meshShape.Name} verts={blendIndexStats.VertexCount} jointsMax={blendIndexStats.MaxIndex} boneWeights={boneWeightCount} armBones={(armature?.Bones.Count ?? 0)}");
                    }
                }
            }

        }

        private void TryRegisterFullMorphTargets(byte[] trmshBytes, TRMSH trmsh, TRModelBuffer[] buffers)
        {
            if (trmshBytes == null || trmshBytes.Length < 64 || trmsh == null || buffers == null)
            {
                lock (morphGate)
                {
                    cpuFullMorphRegistrationStatus ??= "morph metadata unavailable";
                }
                return;
            }

            void SetStatusIfEmpty(string status)
            {
                lock (morphGate)
                {
                    if (fullMorphTargetsByMeshShapeName.Count == 0 && string.IsNullOrWhiteSpace(cpuFullMorphRegistrationStatus))
                    {
                        cpuFullMorphRegistrationStatus = status;
                    }
                }
            }

            bool TryComputeVertexCount(TRMesh meshShape, TRModelBuffer meshBuffers, out int vertexCount)
            {
                vertexCount = 0;
                if (meshShape?.vertexDeclaration == null || meshShape.vertexDeclaration.Length == 0 || meshBuffers?.VertexBuffer == null)
                {
                    return false;
                }

                // Pick the first declaration that contains POSITION.
                TRVertexDeclaration? decl = null;
                foreach (var d in meshShape.vertexDeclaration)
                {
                    if (d?.vertexElements == null)
                    {
                        continue;
                    }
                    if (d.vertexElements.Any(e => e != null && e.Usage == TRVertexUsage.POSITION))
                    {
                        decl = d;
                        break;
                    }
                }

                if (decl?.vertexElements == null)
                {
                    return false;
                }

                var posElement = decl.vertexElements.FirstOrDefault(e => e != null && e.Usage == TRVertexUsage.POSITION);
                if (posElement == null)
                {
                    return false;
                }

                int slot = posElement.Slot;
                if (slot < 0 || slot >= meshBuffers.VertexBuffer.Length)
                {
                    return false;
                }

                var posBuffer = meshBuffers.VertexBuffer[slot];
                if (posBuffer?.Bytes == null || posBuffer.Bytes.Length == 0)
                {
                    return false;
                }

                int stride = GetStride(decl, slot);
                if (stride <= 0)
                {
                    return false;
                }

                int count = posBuffer.Bytes.Length / stride;
                if (count <= 0)
                {
                    return false;
                }

                vertexCount = count;
                return true;
            }

            void TryRegisterFromTrmbfMorphTargets(TRMesh meshShape, TRModelBuffer meshBuffers)
            {
                if (meshShape == null || meshBuffers == null)
                {
                    return;
                }

                var morphTargets = meshBuffers.MorphTargets;
                if (morphTargets == null || morphTargets.Length == 0)
                {
                    return;
                }

                if (!TryComputeVertexCount(meshShape, meshBuffers, out int vertexCount))
                {
                    SetStatusIfEmpty("morph targets present in TRMBF, but vertex-count could not be determined");
                    return;
                }

                var targets = new List<FullMorphTarget>();
                int multiBufferMorphTargets = 0;
                for (int targetIndex = 0; targetIndex < morphTargets.Length; targetIndex++)
                {
                    var mt = morphTargets[targetIndex];
                    if (mt?.morphBuffers == null || mt.morphBuffers.Length == 0)
                    {
                        continue;
                    }

                    int viableCount = 0;
                    TRBuffer? chosen = null;
                    int chosenStride = 0;

                    for (int bufIndex = 0; bufIndex < mt.morphBuffers.Length; bufIndex++)
                    {
                        var buf = mt.morphBuffers[bufIndex];
                        var bytes = buf?.Bytes;
                        if (bytes == null || bytes.Length == 0)
                        {
                            continue;
                        }

                        // Infer stride from buffer size. If it matches vertexCount and has the 0x1C prefix, we can decode.
                        if (vertexCount <= 0 || (bytes.Length % vertexCount) != 0)
                        {
                            continue;
                        }

                        int stride = bytes.Length / vertexCount;
                        if (stride < 0x1C)
                        {
                            lock (morphGate)
                            {
                                unsupportedFullMorphTargets.Add(new UnsupportedFullMorphTarget(meshShape.Name, $"MorphTarget_{targetIndex:D3}", bufIndex, stride));
                            }
                            continue;
                        }

                        viableCount++;
                        if (chosen == null)
                        {
                            chosen = buf;
                            chosenStride = stride;
                        }
                    }

                    if (viableCount > 1)
                    {
                        multiBufferMorphTargets++;
                    }

                    if (chosen == null)
                    {
                        continue;
                    }

                    targets.Add(new FullMorphTarget
                    {
                        Name = $"MorphTarget_{targetIndex:D3}",
                        VertexLayerId = -1,
                        VertexStride = chosenStride,
                        DeltaBuffer = chosen,
                        AddBlend = true
                    });
                }

                if (targets.Count == 0)
                {
                    return;
                }

                lock (morphGate)
                {
                    fullMorphTargetsByMeshShapeName[meshShape.Name] = targets;
                    cpuFullMorphRegistrationStatus = multiBufferMorphTargets > 0
                        ? $"using TRMBF morph targets (unnamed): {targets.Count} (note: {multiBufferMorphTargets} target(s) have multiple buffers; using first decodable buffer)"
                        : $"using TRMBF morph targets (unnamed): {targets.Count}";
                }
            }

            // Avoid re-registering for the same mesh file if ParseMesh is called multiple times.
            // (This is a per-model cache keyed by mesh shape name.)
            lock (morphGate)
            {
                // If we already have entries, keep them (prototype behavior).
                // Caller can clear by reloading the model.
            }

            Gfx2Mesh? gfxMesh = null;
            try
            {
                gfxMesh = FlatBufferConverter.DeserializeFrom<Gfx2Mesh>(trmshBytes);
            }
            catch (Exception ex)
            {
                gfxMesh = null;
                SetStatusIfEmpty($"gfl.gfx2 parse failed: {ex.GetType().Name}: {ex.Message}");
            }

            if (gfxMesh?.ItemList == null || gfxMesh.ItemList.Length == 0)
            {
                SetStatusIfEmpty("gfl.gfx2 morph metadata not detected in TRMSH");
            }

            // Sanity: the mesh metadata should align with the number of mesh shapes.
            if (gfxMesh?.ItemList != null && trmsh.Meshes == null)
            {
                SetStatusIfEmpty("morph metadata mesh-count mismatch (missing TRMSH meshes)");
            }

            var trmshMeshes = trmsh.Meshes;
            int trmshMeshCount = trmshMeshes?.Length ?? 0;
            int maxMeshIndex = Math.Min(trmshMeshCount, buffers.Length);
            for (int meshIndex = 0; meshIndex < maxMeshIndex; meshIndex++)
            {
                var meshShape = trmshMeshes![meshIndex];
                if (meshShape == null)
                {
                    continue;
                }

                // Fallback path: TRMBF may contain morph targets even when mesh metadata isn't present or parsable.
                var meshBuffers = buffers[meshIndex];
                if (meshBuffers != null && meshBuffers.MorphTargets != null && meshBuffers.MorphTargets.Length > 0)
                {
                    // Prefer TRMBF fallback if metadata parse is missing or mismatched.
                    bool canUseGfx2 = gfxMesh?.ItemList != null && gfxMesh.ItemList.Length == trmshMeshCount;
                    if (!canUseGfx2)
                    {
                        TryRegisterFromTrmbfMorphTargets(meshShape, meshBuffers);
                        continue;
                    }
                }

                var item = (gfxMesh?.ItemList != null && gfxMesh.ItemList.Length == trmshMeshCount) ? gfxMesh.ItemList[meshIndex] : null;
                if (item == null)
                {
                    // If metadata isn't available, but there are no TRMBF morph targets either, there's nothing we can do here.
                    continue;
                }

                // Register partial blend shapes (they use TargetVertexIndex mapping + TRMBF MorphTargets buffers).
                if (item.PartialBlendShapeInfoList != null && item.PartialBlendShapeInfoList.Length > 0)
                {
                    if (meshBuffers?.MorphTargets != null && meshBuffers.MorphTargets.Length > 0)
                    {
                        int groupCount = Math.Min(item.PartialBlendShapeInfoList.Length, meshBuffers.MorphTargets.Length);

                        for (int groupIndex = 0; groupIndex < groupCount; groupIndex++)
                        {
                            var group = item.PartialBlendShapeInfoList[groupIndex];
                            var groupBuffers = meshBuffers.MorphTargets[groupIndex];
                            if (group == null || groupBuffers?.morphBuffers == null || groupBuffers.morphBuffers.Length == 0)
                            {
                                continue;
                            }

                            if (group.VertexLayerList == null || group.VertexLayerList.Length == 0)
                            {
                                continue;
                            }

                            // Find which vertex layer contains the TargetVertexIndex mapping.
                            int mappingLayer = -1;
                            int mappingStride = 0;
                            int mappingOffset = 0;
                            Gfx2VertexAttributeFormat mappingFormat = Gfx2VertexAttributeFormat.Unknown;

                            for (int layerIndex = 0; layerIndex < group.VertexLayerList.Length; layerIndex++)
                            {
                                var vl = group.VertexLayerList[layerIndex];
                                if (vl?.AttributeList == null || vl.AttributeList.Length == 0)
                                {
                                    continue;
                                }

                                var mapAtt = vl.AttributeList.FirstOrDefault(a => a != null && a.Semantic == Gfx2VertexAttributeSemantic.TargetVertexIndex);
                                if (mapAtt == null)
                                {
                                    continue;
                                }

                                mappingLayer = layerIndex;
                                mappingOffset = mapAtt.Offset;
                                mappingFormat = mapAtt.Format;
                                mappingStride = (vl.StreamList != null && vl.StreamList.Length > 0) ? vl.StreamList[0].Stride : 0;
                                break;
                            }

                            if (mappingLayer < 0 || mappingStride <= 0 || mappingLayer >= groupBuffers.morphBuffers.Length)
                            {
                                continue;
                            }

                            var mappingBytes = groupBuffers.morphBuffers[mappingLayer]?.Bytes;
                            if (mappingBytes == null || mappingBytes.Length < mappingStride)
                            {
                                continue;
                            }

                            int mappingCount = mappingBytes.Length / mappingStride;
                            if (mappingCount <= 0)
                            {
                                continue;
                            }

                            int[] targetVertexIndices = new int[mappingCount];
                            for (int i = 0; i < mappingCount; i++)
                            {
                                int off = i * mappingStride + mappingOffset;
                                if (off < 0 || off + 4 > mappingBytes.Length)
                                {
                                    continue;
                                }

                                int idx;
                                if (mappingFormat == Gfx2VertexAttributeFormat.R32_SINT)
                                {
                                    idx = BitConverter.ToInt32(mappingBytes, off);
                                }
                                else if (mappingFormat == Gfx2VertexAttributeFormat.R32_UINT)
                                {
                                    idx = unchecked((int)BitConverter.ToUInt32(mappingBytes, off));
                                }
                                else
                                {
                                    // Unsupported mapping type.
                                    idx = -1;
                                }

                                targetVertexIndices[i] = idx;
                            }

                            if (group.BlendShapeInfoList == null || group.BlendShapeInfoList.Length == 0)
                            {
                                continue;
                            }

                            var partialTargets = new List<PartialMorphTarget>();
                            foreach (var bs in group.BlendShapeInfoList)
                            {
                                if (bs == null || string.IsNullOrWhiteSpace(bs.Name))
                                {
                                    continue;
                                }

                                int layerId = (int)bs.VertexLayerId;
                                if (layerId <= 0 || layerId >= group.VertexLayerList.Length || layerId >= groupBuffers.morphBuffers.Length)
                                {
                                    continue;
                                }

                                int stride = 0;
                                try
                                {
                                    var vl = group.VertexLayerList[layerId];
                                    if (vl?.StreamList != null && vl.StreamList.Length > 0)
                                    {
                                        stride = vl.StreamList[0].Stride;
                                    }
                                }
                                catch
                                {
                                    stride = 0;
                                }

                                if (stride > 0 && stride < 0x1C)
                                {
                                    lock (morphGate)
                                    {
                                        unsupportedFullMorphTargets.Add(new UnsupportedFullMorphTarget(meshShape.Name, bs.Name, layerId, stride));
                                    }
                                    continue;
                                }

                                var buf = groupBuffers.morphBuffers[layerId];
                                if (buf?.Bytes == null || buf.Bytes.Length == 0)
                                {
                                    continue;
                                }

                                partialTargets.Add(new PartialMorphTarget
                                {
                                    Name = bs.Name,
                                    VertexLayerId = layerId,
                                    VertexStride = stride > 0 ? stride : 0x1C,
                                    DeltaBuffer = buf,
                                    TargetVertexIndices = targetVertexIndices,
                                    AddBlend = bs.IsAdditiveBlend
                                });
                            }

                            if (partialTargets.Count > 0)
                            {
                                lock (morphGate)
                                {
                                    if (!partialMorphTargetsByMeshShapeName.TryGetValue(meshShape.Name, out var list))
                                    {
                                        list = new List<PartialMorphTarget>();
                                        partialMorphTargetsByMeshShapeName[meshShape.Name] = list;
                                    }
                                    list.AddRange(partialTargets);
                                    cpuFullMorphRegistrationStatus = null;
                                }
                            }
                        }
                    }
                }

                if (item.BlendShapeInfoList == null || item.BlendShapeInfoList.Length == 0)
                {
                    continue;
                }

                // Ensure we have the vertex buffers for this mesh shape.
                if (meshBuffers?.VertexBuffer == null || meshBuffers.VertexBuffer.Length == 0)
                {
                    continue;
                }

                var targets = new List<FullMorphTarget>();
                foreach (var bs in item.BlendShapeInfoList)
                {
                    if (bs == null || string.IsNullOrWhiteSpace(bs.Name))
                    {
                        continue;
                    }

                    int layerId = (int)bs.VertexLayerId;
                    if (layerId <= 0 || layerId >= meshBuffers.VertexBuffer.Length)
                    {
                        continue;
                    }

                    int stride = 0;
                    try
                    {
                        if (item.VertexLayerList != null && layerId < item.VertexLayerList.Length && item.VertexLayerList[layerId]?.StreamList != null && item.VertexLayerList[layerId].StreamList.Length > 0)
                        {
                            stride = item.VertexLayerList[layerId].StreamList[0].Stride;
                        }
                    }
                    catch
                    {
                        stride = 0;
                    }

                    // Current CPU morph implementation assumes a packed layout compatible with the 0x1C offsets.
                    // Allow larger strides (ex: extra padding/attributes) since many formats keep the same prefix.
                    if (stride > 0 && stride < 0x1C)
                    {
                        lock (morphGate)
                        {
                            unsupportedFullMorphTargets.Add(new UnsupportedFullMorphTarget(meshShape.Name, bs.Name, layerId, stride));
                        }
                        continue;
                    }

                    targets.Add(new FullMorphTarget
                    {
                        Name = bs.Name,
                        VertexLayerId = layerId,
                        VertexStride = stride > 0 ? stride : 0x1C,
                        DeltaBuffer = meshBuffers.VertexBuffer[layerId],
                        AddBlend = bs.IsAdditiveBlend
                    });
                }

                if (targets.Count == 0)
                {
                    continue;
                }

                lock (morphGate)
                {
                    fullMorphTargetsByMeshShapeName[meshShape.Name] = targets;
                    vertexBuffersByMeshShapeName[meshShape.Name] = meshBuffers.VertexBuffer;
                    cpuFullMorphRegistrationStatus = null;
                }
            }
        }

	    }
}
