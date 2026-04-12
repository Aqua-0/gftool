using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.Linq;
using Trinity.Core.Flatbuffers.Gfx2;
using Trinity.Core.Flatbuffers.TR.Model;

namespace GFTool.Renderer.Scene.GraphicsObjects
{
    public partial class Model
    {
        private sealed record FullMorphTargetKey(string MeshShapeName, string TargetName);

        private sealed class FullMorphTarget
        {
            public string Name { get; init; } = string.Empty;
            public int VertexLayerId { get; init; }
            public int VertexStride { get; init; }
            public TRBuffer? DeltaBuffer { get; init; }
            public bool AddBlend { get; init; } = true;
            public Vector3[]? PositionDelta { get; set; }
            public Vector3[]? NormalDelta { get; set; }
            public Vector4[]? TangentDelta { get; set; }
        }

        private sealed class PartialMorphTarget
        {
            public string Name { get; init; } = string.Empty;
            public int VertexLayerId { get; init; }
            public int VertexStride { get; init; }
            public TRBuffer? DeltaBuffer { get; init; }
            public int[] TargetVertexIndices { get; init; } = Array.Empty<int>();
            public bool AddBlend { get; init; } = true;
            public Vector3[]? PositionDelta { get; set; }
            public Vector3[]? NormalDelta { get; set; }
            public Vector4[]? TangentDelta { get; set; }
        }

        private sealed record UnsupportedFullMorphTarget(string MeshShapeName, string TargetName, int VertexLayerId, int VertexStride);

        private readonly object morphGate = new object();
        private readonly Dictionary<string, List<FullMorphTarget>> fullMorphTargetsByMeshShapeName =
            new Dictionary<string, List<FullMorphTarget>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<PartialMorphTarget>> partialMorphTargetsByMeshShapeName =
            new Dictionary<string, List<PartialMorphTarget>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<FullMorphTargetKey, float> fullMorphWeights =
            new Dictionary<FullMorphTargetKey, float>();
        private readonly Dictionary<string, TRBuffer[]> vertexBuffersByMeshShapeName =
            new Dictionary<string, TRBuffer[]>(StringComparer.OrdinalIgnoreCase);
        private readonly List<UnsupportedFullMorphTarget> unsupportedFullMorphTargets = new List<UnsupportedFullMorphTarget>();
        private string? cpuFullMorphRegistrationStatus;

        private readonly Dictionary<int, Vector3[]> basePositionsBySubmeshIndex = new Dictionary<int, Vector3[]>();
        private readonly Dictionary<int, Vector3[]> baseNormalsBySubmeshIndex = new Dictionary<int, Vector3[]>();
        private readonly Dictionary<int, Vector4[]> baseTangentsBySubmeshIndex = new Dictionary<int, Vector4[]>();
        private readonly Dictionary<int, Vector3[]> baseBinormalsBySubmeshIndex = new Dictionary<int, Vector3[]>();

        private readonly HashSet<int> pendingMorphVboUploads = new HashSet<int>();

        public bool HasCpuFullMorphTargets
        {
            get
            {
                lock (morphGate)
                {
                    return fullMorphTargetsByMeshShapeName.Count > 0 || partialMorphTargetsByMeshShapeName.Count > 0;
                }
            }
        }

        public IReadOnlyList<(string MeshShapeName, string TargetName)> GetCpuFullMorphTargetNames()
        {
            lock (morphGate)
            {
                var results = new List<(string MeshShapeName, string TargetName)>();
                foreach (var kv in fullMorphTargetsByMeshShapeName)
                {
                    string meshName = kv.Key;
                    foreach (var t in kv.Value)
                    {
                        if (!string.IsNullOrWhiteSpace(t.Name))
                        {
                            results.Add((meshName, t.Name));
                        }
                    }
                }

                foreach (var kv in partialMorphTargetsByMeshShapeName)
                {
                    string meshName = kv.Key;
                    foreach (var t in kv.Value)
                    {
                        if (!string.IsNullOrWhiteSpace(t.Name))
                        {
                            results.Add((meshName, t.Name));
                        }
                    }
                }

                return results;
            }
        }

        public string GetCpuFullMorphTargetDebugSummary()
        {
            lock (morphGate)
            {
                if (unsupportedFullMorphTargets.Count == 0 &&
                    string.IsNullOrWhiteSpace(cpuFullMorphRegistrationStatus))
                {
                    return string.Empty;
                }

                var parts = new List<string>();

                if (!string.IsNullOrWhiteSpace(cpuFullMorphRegistrationStatus))
                {
                    parts.Add(cpuFullMorphRegistrationStatus);
                }

                if (unsupportedFullMorphTargets.Count > 0)
                {
                    var strides = unsupportedFullMorphTargets
                        .Select(t => t.VertexStride)
                        .Where(s => s > 0)
                        .Distinct()
                        .OrderBy(s => s)
                        .ToList();

                    string strideList = strides.Count > 0
                        ? string.Join(", ", strides.Select(s => $"0x{s:X}"))
                        : "unknown";

                    parts.Add($"unsupported morph vertex stride(s): {strideList}");
                }

                return string.Join("; ", parts);
            }
        }

        public float GetCpuFullMorphWeight(string meshShapeName, string targetName)
        {
            if (string.IsNullOrWhiteSpace(meshShapeName) || string.IsNullOrWhiteSpace(targetName))
            {
                return 0.0f;
            }

            lock (morphGate)
            {
                return fullMorphWeights.TryGetValue(new FullMorphTargetKey(meshShapeName, targetName), out var w) ? w : 0.0f;
            }
        }

        public void SetCpuFullMorphWeight(string meshShapeName, string targetName, float weight01)
        {
            if (string.IsNullOrWhiteSpace(meshShapeName) || string.IsNullOrWhiteSpace(targetName))
            {
                return;
            }

            float w = Math.Clamp(weight01, 0.0f, 1.0f);
            lock (morphGate)
            {
                var key = new FullMorphTargetKey(meshShapeName, targetName);
                if (w <= 0.000001f)
                {
                    fullMorphWeights.Remove(key);
                }
                else
                {
                    fullMorphWeights[key] = w;
                }
            }
        }

        public void ResetCpuFullMorphWeights()
        {
            lock (morphGate)
            {
                fullMorphWeights.Clear();
            }
        }

        public bool TryApplyCpuFullMorphs(out string? error)
        {
            error = null;

            var submeshIndicesByMeshShape = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            for (int submeshIndex = 0; submeshIndex < BlendMeshNames.Count; submeshIndex++)
            {
                var meshName = BlendMeshNames[submeshIndex];
                if (string.IsNullOrWhiteSpace(meshName))
                {
                    continue;
                }

                if (!submeshIndicesByMeshShape.TryGetValue(meshName, out var list))
                {
                    list = new List<int>();
                    submeshIndicesByMeshShape[meshName] = list;
                }

                list.Add(submeshIndex);
            }

            Dictionary<string, List<FullMorphTarget>> targetsSnapshot;
            Dictionary<string, List<PartialMorphTarget>> partialTargetsSnapshot;
            Dictionary<FullMorphTargetKey, float> weightsSnapshot;
            lock (morphGate)
            {
                targetsSnapshot = fullMorphTargetsByMeshShapeName.ToDictionary(kv => kv.Key, kv => kv.Value.ToList(), StringComparer.OrdinalIgnoreCase);
                partialTargetsSnapshot = partialMorphTargetsByMeshShapeName.ToDictionary(kv => kv.Key, kv => kv.Value.ToList(), StringComparer.OrdinalIgnoreCase);
                weightsSnapshot = new Dictionary<FullMorphTargetKey, float>(fullMorphWeights);
            }

            bool anyChanged = false;

            var meshShapeNames = new HashSet<string>(targetsSnapshot.Keys, StringComparer.OrdinalIgnoreCase);
            foreach (var k in partialTargetsSnapshot.Keys)
            {
                meshShapeNames.Add(k);
            }

            foreach (var meshShapeName in meshShapeNames)
            {
                if (!submeshIndicesByMeshShape.TryGetValue(meshShapeName, out var submeshIndices) || submeshIndices.Count == 0)
                {
                    continue;
                }

                targetsSnapshot.TryGetValue(meshShapeName, out var fullTargets);
                partialTargetsSnapshot.TryGetValue(meshShapeName, out var partialTargets);
                fullTargets ??= new List<FullMorphTarget>();
                partialTargets ??= new List<PartialMorphTarget>();
                if (fullTargets.Count == 0 && partialTargets.Count == 0)
                {
                    continue;
                }

                int representativeSubmesh = submeshIndices[0];
                if (representativeSubmesh < 0 || representativeSubmesh >= Positions.Count)
                {
                    continue;
                }

                int vertexCount = Positions[representativeSubmesh].Length;
                if (vertexCount <= 0)
                {
                    continue;
                }

                if (!basePositionsBySubmeshIndex.ContainsKey(representativeSubmesh))
                {
                    foreach (var sm in submeshIndices)
                    {
                        if (sm < 0 || sm >= Positions.Count)
                        {
                            continue;
                        }

                        basePositionsBySubmeshIndex[sm] = Positions[sm].ToArray();
                        baseNormalsBySubmeshIndex[sm] = Normals[sm].ToArray();
                        baseTangentsBySubmeshIndex[sm] = Tangents[sm].ToArray();
                        baseBinormalsBySubmeshIndex[sm] = Binormals[sm].ToArray();
                    }
                }

                foreach (var t in fullTargets)
                {
                    if (t.PositionDelta != null && t.PositionDelta.Length == vertexCount &&
                        t.NormalDelta != null && t.NormalDelta.Length == vertexCount &&
                        t.TangentDelta != null && t.TangentDelta.Length == vertexCount)
                    {
                        continue;
                    }

                    var bytes = t.DeltaBuffer?.Bytes;
                    if (bytes == null)
                    {
                        if (!vertexBuffersByMeshShapeName.TryGetValue(meshShapeName, out var vertexBuffers) ||
                            vertexBuffers == null ||
                            t.VertexLayerId <= 0 ||
                            t.VertexLayerId >= vertexBuffers.Length)
                        {
                            continue;
                        }

                        bytes = vertexBuffers[t.VertexLayerId].Bytes;
                    }

                    if (!TryDecodeFullMorphDelta(bytes, t.VertexStride, vertexCount, out var pos, out var nrm, out var tan, out var decodeError))
                    {
                        error ??= decodeError ?? $"Failed to decode morph '{t.Name}' for '{meshShapeName}'.";
                        continue;
                    }

                    if (basePositionsBySubmeshIndex.TryGetValue(representativeSubmesh, out var basePos) &&
                        baseNormalsBySubmeshIndex.TryGetValue(representativeSubmesh, out var baseNrm) &&
                        baseTangentsBySubmeshIndex.TryGetValue(representativeSubmesh, out var baseTan) &&
                        LooksLikeAbsoluteMorphPositions(pos, basePos))
                    {
                        int n = Math.Min(pos.Length, basePos.Length);
                        for (int i = 0; i < n; i++)
                        {
                            pos[i] -= basePos[i];
                        }

                        n = Math.Min(nrm.Length, baseNrm.Length);
                        for (int i = 0; i < n; i++)
                        {
                            nrm[i] -= baseNrm[i];
                        }

                        n = Math.Min(tan.Length, baseTan.Length);
                        for (int i = 0; i < n; i++)
                        {
                            tan[i] -= baseTan[i];
                        }
                    }

                    t.PositionDelta = pos;
                    t.NormalDelta = nrm;
                    t.TangentDelta = tan;
                }

                foreach (var t in partialTargets)
                {
                    int partialCount = t.TargetVertexIndices?.Length ?? 0;
                    if (partialCount <= 0)
                    {
                        continue;
                    }

                    if (t.PositionDelta != null && t.PositionDelta.Length == partialCount &&
                        t.NormalDelta != null && t.NormalDelta.Length == partialCount &&
                        t.TangentDelta != null && t.TangentDelta.Length == partialCount)
                    {
                        continue;
                    }

                    var bytes = t.DeltaBuffer?.Bytes;
                    if (bytes == null)
                    {
                        continue;
                    }

                    if (!TryDecodeFullMorphDelta(bytes, t.VertexStride, partialCount, out var pos, out var nrm, out var tan, out var decodeError))
                    {
                        error ??= decodeError ?? $"Failed to decode partial morph '{t.Name}' for '{meshShapeName}'.";
                        continue;
                    }

                    if (basePositionsBySubmeshIndex.TryGetValue(representativeSubmesh, out var basePos) &&
                        baseNormalsBySubmeshIndex.TryGetValue(representativeSubmesh, out var baseNrm) &&
                        baseTangentsBySubmeshIndex.TryGetValue(representativeSubmesh, out var baseTan) &&
                        LooksLikeAbsolutePartialMorphPositions(pos, basePos, t.TargetVertexIndices))
                    {
                        int n = Math.Min(pos.Length, t.TargetVertexIndices.Length);
                        for (int i = 0; i < n; i++)
                        {
                            int j = t.TargetVertexIndices[i];
                            if (j >= 0 && j < basePos.Length)
                            {
                                pos[i] -= basePos[j];
                            }
                        }

                        n = Math.Min(nrm.Length, t.TargetVertexIndices.Length);
                        for (int i = 0; i < n; i++)
                        {
                            int j = t.TargetVertexIndices[i];
                            if (j >= 0 && j < baseNrm.Length)
                            {
                                nrm[i] -= baseNrm[j];
                            }
                        }

                        n = Math.Min(tan.Length, t.TargetVertexIndices.Length);
                        for (int i = 0; i < n; i++)
                        {
                            int j = t.TargetVertexIndices[i];
                            if (j >= 0 && j < baseTan.Length)
                            {
                                tan[i] -= baseTan[j];
                            }
                        }
                    }

                    t.PositionDelta = pos;
                    t.NormalDelta = nrm;
                    t.TangentDelta = tan;
                }

                bool hasAnyWeight = weightsSnapshot.Keys.Any(k => string.Equals(k.MeshShapeName, meshShapeName, StringComparison.OrdinalIgnoreCase));

                var activeFull = new List<(FullMorphTarget Target, float Weight)>();
                foreach (var t in fullTargets)
                {
                    if (string.IsNullOrWhiteSpace(t.Name) || t.PositionDelta == null || t.NormalDelta == null || t.TangentDelta == null)
                    {
                        continue;
                    }

                    if (!weightsSnapshot.TryGetValue(new FullMorphTargetKey(meshShapeName, t.Name), out var w) || w <= 0.000001f)
                    {
                        continue;
                    }

                    activeFull.Add((t, w));
                }

                var activePartial = new List<(PartialMorphTarget Target, float Weight)>();
                foreach (var t in partialTargets)
                {
                    if (string.IsNullOrWhiteSpace(t.Name) || t.PositionDelta == null || t.NormalDelta == null || t.TangentDelta == null)
                    {
                        continue;
                    }

                    if (!weightsSnapshot.TryGetValue(new FullMorphTargetKey(meshShapeName, t.Name), out var w) || w <= 0.000001f)
                    {
                        continue;
                    }

                    activePartial.Add((t, w));
                }

                Vector3[]? partialPosAdd = null;
                Vector3[]? partialNrmAdd = null;
                Vector4[]? partialTanAdd = null;
                if (activePartial.Count > 0)
                {
                    partialPosAdd = new Vector3[vertexCount];
                    partialNrmAdd = new Vector3[vertexCount];
                    partialTanAdd = new Vector4[vertexCount];

                    foreach (var (t, w) in activePartial)
                    {
                        var map = t.TargetVertexIndices;
                        if (map == null || map.Length == 0)
                        {
                            continue;
                        }

                        int n = Math.Min(map.Length, Math.Min(t.PositionDelta!.Length, Math.Min(t.NormalDelta!.Length, t.TangentDelta!.Length)));
                        for (int i = 0; i < n; i++)
                        {
                            int j = map[i];
                            if (j < 0 || j >= vertexCount)
                            {
                                continue;
                            }

                            partialPosAdd[j] += t.PositionDelta![i] * w;
                            partialNrmAdd[j] += t.NormalDelta![i] * w;
                            partialTanAdd[j] += t.TangentDelta![i] * w;
                        }
                    }
                }

                foreach (var submeshIndex in submeshIndices)
                {
                    if (submeshIndex < 0 || submeshIndex >= Positions.Count)
                    {
                        continue;
                    }

                    if (!basePositionsBySubmeshIndex.TryGetValue(submeshIndex, out var basePos) ||
                        !baseNormalsBySubmeshIndex.TryGetValue(submeshIndex, out var baseNrm) ||
                        !baseTangentsBySubmeshIndex.TryGetValue(submeshIndex, out var baseTan) ||
                        !baseBinormalsBySubmeshIndex.TryGetValue(submeshIndex, out var baseBin))
                    {
                        continue;
                    }

                    if (!hasAnyWeight)
                    {
                        Positions[submeshIndex] = basePos.ToArray();
                        Normals[submeshIndex] = baseNrm.ToArray();
                        Tangents[submeshIndex] = baseTan.ToArray();
                        Binormals[submeshIndex] = baseBin.ToArray();

                        lock (morphGate)
                        {
                            pendingMorphVboUploads.Add(submeshIndex);
                        }

                        anyChanged = true;
                        continue;
                    }

                    var outPos = basePos.ToArray();
                    var outNrm = baseNrm.ToArray();
                    var outTan = baseTan.ToArray();
                    var outBin = baseBin.ToArray();

                    for (int v = 0; v < vertexCount; v++)
                    {
                        var p = basePos[v];
                        var n = baseNrm[v];
                        var t = baseTan[v];

                        if (partialPosAdd != null && partialNrmAdd != null && partialTanAdd != null)
                        {
                            p += partialPosAdd[v];
                            n += partialNrmAdd[v];
                            var dt = partialTanAdd[v];
                            t = new Vector4(t.X + dt.X, t.Y + dt.Y, t.Z + dt.Z, t.W + dt.W);
                        }

                        foreach (var (target, w) in activeFull)
                        {
                            p += target.PositionDelta![v] * w;
                            n += target.NormalDelta![v] * w;

                            var dt = target.TangentDelta![v] * w;
                            t = new Vector4(t.X + dt.X, t.Y + dt.Y, t.Z + dt.Z, t.W + dt.W);
                        }

                        if (n.LengthSquared > 0.0000001f)
                        {
                            n = n.Normalized();
                        }

                        var txyz = new Vector3(t.X, t.Y, t.Z);
                        if (txyz.LengthSquared > 0.0000001f)
                        {
                            txyz = txyz.Normalized();
                            t = new Vector4(txyz.X, txyz.Y, txyz.Z, MathF.Abs(t.W) > 0.0000001f ? MathF.Sign(t.W) : 1.0f);
                        }

                        var b = Vector3.Cross(n, new Vector3(t.X, t.Y, t.Z));
                        b *= t.W;
                        if (b.LengthSquared > 0.0000001f)
                        {
                            b = b.Normalized();
                        }

                        outPos[v] = p;
                        outNrm[v] = n;
                        outTan[v] = t;
                        outBin[v] = b;
                    }

                    Positions[submeshIndex] = outPos;
                    Normals[submeshIndex] = outNrm;
                    Tangents[submeshIndex] = outTan;
                    Binormals[submeshIndex] = outBin;

                    lock (morphGate)
                    {
                        pendingMorphVboUploads.Add(submeshIndex);
                    }

                    anyChanged = true;
                }
            }

            return anyChanged;
        }

        private static bool TryDecodeFullMorphDelta(
            byte[] bytes,
            int vertexStride,
            int expectedVertexCount,
            out Vector3[] posDelta,
            out Vector3[] nrmDelta,
            out Vector4[] tanDelta,
            out string? error)
        {
            posDelta = Array.Empty<Vector3>();
            nrmDelta = Array.Empty<Vector3>();
            tanDelta = Array.Empty<Vector4>();
            error = null;

            if (bytes == null || bytes.Length == 0)
            {
                error = "Empty morph buffer.";
                return false;
            }

            int stride = vertexStride > 0 ? vertexStride : 0x1C;
            if (stride < 0x0C)
            {
                error = $"Unsupported morph vertex stride: {stride}.";
                return false;
            }

            int count = bytes.Length / stride;
            if (count != expectedVertexCount)
            {
                error = $"Morph buffer vertex count mismatch: expected {expectedVertexCount}, got {count} (bytes={bytes.Length}).";
                return false;
            }

            posDelta = new Vector3[count];
            nrmDelta = new Vector3[count];
            tanDelta = new Vector4[count];

            float ReadFloat(int o) => BitConverter.ToSingle(bytes, o);
            float ReadHalf(int o) => (float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(bytes, o));

            for (int i = 0; i < count; i++)
            {
                int baseOff = i * stride;
                float vx = ReadFloat(baseOff + 0);
                float vy = ReadFloat(baseOff + 4);
                float vz = ReadFloat(baseOff + 8);

                float nx = 0;
                float ny = 0;
                float nz = 0;
                float tx = 0;
                float ty = 0;
                float tz = 0;
                float tw = 0;
                if (stride >= 0x1C)
                {
                    nx = ReadHalf(baseOff + 12);
                    ny = ReadHalf(baseOff + 14);
                    nz = ReadHalf(baseOff + 16);

                    tx = ReadHalf(baseOff + 20);
                    ty = ReadHalf(baseOff + 22);
                    tz = ReadHalf(baseOff + 24);
                    tw = ReadHalf(baseOff + 26);
                }

                posDelta[i] = new Vector3(vx, vy, vz);
                nrmDelta[i] = new Vector3(nx, ny, nz);
                tanDelta[i] = new Vector4(tx, ty, tz, tw);
            }

            return true;
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

        public readonly record struct ExportMorphTarget(string Name, Vector3[] PositionDeltas, Vector3[] NormalDeltas, Vector4[] TangentDeltas);

        public IReadOnlyList<ExportMorphTarget> BuildCpuMorphTargetsForExport(
            string meshShapeName,
            Vector3[] basePositions,
            Vector3[] baseNormals,
            Vector4[] baseTangents)
        {
            if (string.IsNullOrWhiteSpace(meshShapeName) || basePositions == null)
            {
                return Array.Empty<ExportMorphTarget>();
            }

            int vertexCount = basePositions.Length;
            if (vertexCount <= 0)
            {
                return Array.Empty<ExportMorphTarget>();
            }

            var baseNrm = (baseNormals != null && baseNormals.Length == vertexCount) ? baseNormals : new Vector3[vertexCount];
            var baseTan = (baseTangents != null && baseTangents.Length == vertexCount) ? baseTangents : new Vector4[vertexCount];

            List<FullMorphTarget> fullTargets;
            List<PartialMorphTarget> partialTargets;
            lock (morphGate)
            {
                fullTargets = fullMorphTargetsByMeshShapeName.TryGetValue(meshShapeName, out var f) ? f.ToList() : new List<FullMorphTarget>();
                partialTargets = partialMorphTargetsByMeshShapeName.TryGetValue(meshShapeName, out var p) ? p.ToList() : new List<PartialMorphTarget>();
            }

            if (fullTargets.Count == 0 && partialTargets.Count == 0)
            {
                return Array.Empty<ExportMorphTarget>();
            }

            var results = new List<ExportMorphTarget>(fullTargets.Count + partialTargets.Count);

            foreach (var t in fullTargets)
            {
                if (string.IsNullOrWhiteSpace(t.Name) || t.DeltaBuffer?.Bytes == null)
                {
                    continue;
                }

                if (!TryDecodeFullMorphDelta(t.DeltaBuffer.Bytes, t.VertexStride, vertexCount, out var pos, out var nrm, out var tan, out _))
                {
                    continue;
                }

                if (LooksLikeAbsoluteMorphPositions(pos, basePositions))
                {
                    for (int i = 0; i < vertexCount; i++)
                    {
                        pos[i] -= basePositions[i];
                    }

                    int n = Math.Min(nrm.Length, baseNrm.Length);
                    for (int i = 0; i < n; i++)
                    {
                        nrm[i] -= baseNrm[i];
                    }

                    n = Math.Min(tan.Length, baseTan.Length);
                    for (int i = 0; i < n; i++)
                    {
                        tan[i] -= baseTan[i];
                    }
                }

                results.Add(new ExportMorphTarget(t.Name, pos, nrm, tan));
            }

            foreach (var t in partialTargets)
            {
                if (string.IsNullOrWhiteSpace(t.Name) || t.DeltaBuffer?.Bytes == null || t.TargetVertexIndices == null || t.TargetVertexIndices.Length == 0)
                {
                    continue;
                }

                int partialCount = t.TargetVertexIndices.Length;
                if (!TryDecodeFullMorphDelta(t.DeltaBuffer.Bytes, t.VertexStride, partialCount, out var posPartial, out var nrmPartial, out var tanPartial, out _))
                {
                    continue;
                }

                if (LooksLikeAbsolutePartialMorphPositions(posPartial, basePositions, t.TargetVertexIndices))
                {
                    int n = Math.Min(posPartial.Length, t.TargetVertexIndices.Length);
                    for (int i = 0; i < n; i++)
                    {
                        int j = t.TargetVertexIndices[i];
                        if (j >= 0 && j < basePositions.Length)
                        {
                            posPartial[i] -= basePositions[j];
                        }
                    }

                    n = Math.Min(nrmPartial.Length, t.TargetVertexIndices.Length);
                    for (int i = 0; i < n; i++)
                    {
                        int j = t.TargetVertexIndices[i];
                        if (j >= 0 && j < baseNrm.Length)
                        {
                            nrmPartial[i] -= baseNrm[j];
                        }
                    }

                    n = Math.Min(tanPartial.Length, t.TargetVertexIndices.Length);
                    for (int i = 0; i < n; i++)
                    {
                        int j = t.TargetVertexIndices[i];
                        if (j >= 0 && j < baseTan.Length)
                        {
                            tanPartial[i] -= baseTan[j];
                        }
                    }
                }

                var pos = new Vector3[vertexCount];
                var nrm = new Vector3[vertexCount];
                var tan = new Vector4[vertexCount];

                int count = Math.Min(t.TargetVertexIndices.Length, Math.Min(posPartial.Length, Math.Min(nrmPartial.Length, tanPartial.Length)));
                for (int i = 0; i < count; i++)
                {
                    int j = t.TargetVertexIndices[i];
                    if (j < 0 || j >= vertexCount)
                    {
                        continue;
                    }

                    pos[j] = posPartial[i];
                    nrm[j] = nrmPartial[i];
                    tan[j] = tanPartial[i];
                }

                results.Add(new ExportMorphTarget(t.Name, pos, nrm, tan));
            }

            return results;
        }
    }
}
