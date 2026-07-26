using GFTool.Core.Utils;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Trinity.Core.Flatbuffers.TR.Model;

namespace GFTool.Renderer.Scene.GraphicsObjects
{
    public partial class Model
    {
        internal const int PersistentMeshCacheVersion = 3;

        public static Model CreateWithPersistentMeshCache(Trinity.Core.Assets.IAssetProvider assetProvider, string model, bool loadAllLods, out bool cacheHit)
        {
            return CreateWithPersistentMeshCache(
                assetProvider,
                model,
                loadAllLods,
                enableCpuMorphRegistration: true,
                cachePath: null,
                out cacheHit);
        }

        public static Model CreateWithPersistentMeshCache(
            Trinity.Core.Assets.IAssetProvider assetProvider,
            string model,
            bool loadAllLods,
            bool enableCpuMorphRegistration,
            string? cachePath,
            out bool cacheHit)
        {
            // The mesh snapshot intentionally excludes CPU morph metadata. Never satisfy a
            // morph-enabled request from this cache or the Model Viewer would silently lose
            // editable morph targets.
            if (enableCpuMorphRegistration)
            {
                cacheHit = false;
                return new Model(assetProvider, model, loadAllLods, enableCpuMorphRegistration: true);
            }

            bool useDefaultCachePath = string.IsNullOrWhiteSpace(cachePath);
            string resolvedCachePath = useDefaultCachePath
                ? PersistentMeshCache.GetDefaultCachePath(model, loadAllLods, enableCpuMorphRegistration)
                : Path.GetFullPath(cachePath!);
            if (useDefaultCachePath)
            {
                PersistentMeshCache.EnsureCacheDirectory(resolvedCachePath);
            }

            if (PersistentMeshCache.TryLoadFile(resolvedCachePath, out var snapshot))
            {
                try
                {
                    cacheHit = true;
                    return new Model(assetProvider, model, loadAllLods, snapshot);
                }
                catch
                {
                    // A structurally valid snapshot can still be incompatible with newly
                    // decoded model metadata. Rebuild normally and replace it asynchronously.
                }
            }

            cacheHit = false;
            var built = new Model(assetProvider, model, loadAllLods, enableCpuMorphRegistration: false);
            PersistentMeshCache.QueueStoreAtPath(built, resolvedCachePath);
            return built;
        }

        private Model(Trinity.Core.Assets.IAssetProvider assetProvider, string model, bool loadAllLods, PersistentMeshCacheSnapshot meshCache)
        {
            this.assetProvider = assetProvider ?? throw new ArgumentNullException(nameof(assetProvider));
            enableCpuMorphRegistration = false;
            var prepareStopwatch = System.Diagnostics.Stopwatch.StartNew();
            ResetPreparePerfStats();
            ClearDirtyFlags();
            Name = Path.GetFileNameWithoutExtension(model);
            modelMat = Matrix4.Identity;
            modelPath = new PathString(model);
            trmdlSourcePath = model;
            loadedAllLods = loadAllLods;
            preferredMaterialMetadataPath = Path.ChangeExtension(model, ".trmmt");

            var mdl = Trinity.Core.Utils.FlatBufferConverter.DeserializeFrom<TRMDL>(this.assetProvider.ReadAllBytes(model));

            ApplyPersistentMeshCache(meshCache);

            baseSkeletonCategoryHint = GuessBaseSkeletonCategory(
                model,
                mdl.Meshes != null && mdl.Meshes.Length > 0 ? mdl.Meshes[0].PathName : null,
                mdl.Skeleton?.PathName);

            if (mdl.Materials != null && mdl.Materials.Length > 0)
            {
                var resolvedMaterials = new List<string>(mdl.Materials.Length);
                foreach (var mat in mdl.Materials)
                {
                    if (string.IsNullOrWhiteSpace(mat))
                    {
                        continue;
                    }

                    resolvedMaterials.Add(ResolveTrmtrPath(modelPath.Combine(mat), this.assetProvider));
                }

                if (resolvedMaterials.Count > 0)
                {
                    ParseMaterial(resolvedMaterials[resolvedMaterials.Count - 1]);
                }
            }

            defaultMaterialFilePath = currentMaterialFilePath;

            if (mdl.Skeleton != null)
            {
                if (!string.IsNullOrWhiteSpace(mdl.Skeleton.PathName))
                {
                    ParseArmature(modelPath.Combine(mdl.Skeleton.PathName));
                }
                else
                {
                    TryParseBaseArmature(model, baseSkeletonCategoryHint);
                }
            }
            else
            {
                TryParseBaseArmature(model, baseSkeletonCategoryHint);
            }

            ResolveRigidParentAttachments();
            prepareStopwatch.Stop();
            FinalizePreparePerfStats(prepareStopwatch.Elapsed.TotalMilliseconds);
        }

        private void ApplyPersistentMeshCache(PersistentMeshCacheSnapshot snapshot)
        {
            loadedMeshFiles.Clear();
            loadedMeshFiles.AddRange(snapshot.LoadedMeshFiles);

            Positions = snapshot.Positions.ToList();
            Normals = snapshot.Normals.ToList();
            UVs = snapshot.UVs.ToList();
            UVs2 = snapshot.UVs2.ToList();
            HasUv1 = new List<bool>(snapshot.HasUv1);
            Colors = snapshot.Colors.ToList();
            Tangents = snapshot.Tangents.ToList();
            Binormals = snapshot.Binormals.ToList();
            BlendIndicies = snapshot.BlendIndices.ToList();
            BlendWeights = snapshot.BlendWeights.ToList();
            BlendBoneWeights = snapshot.BlendBoneWeights.ToList();
            BlendIndiciesOriginal = snapshot.BlendIndicesOriginal.ToList();
            BlendMeshNames = new List<string>(snapshot.BlendMeshNames);

            Indices = snapshot.Indices.ToList();
            HasVertexColors = new List<bool>(snapshot.HasVertexColors);
            HasTangents = new List<bool>(snapshot.HasTangents);
            HasBinormals = new List<bool>(snapshot.HasBinormals);
            HasSkinning = new List<bool>(snapshot.HasSkinning);

            MaterialNames = new List<string>(snapshot.MaterialNames);
            SubmeshNames = new List<string>(snapshot.SubmeshNames);
            SubmeshParentNodeNames.Clear();
            SubmeshParentNodeNames.AddRange(snapshot.SubmeshParentNodeNames);

            blendIndexStats = new BlendIndexStats
            {
                VertexCount = snapshot.BlendIndexVertexCount,
                MaxIndex = snapshot.BlendIndexMaxIndex
            };

            rigidParentBoneIndexBySubmesh = null;
            EnsureSubmeshVisibilitySize(Positions.Count);

            gpuSetupComplete = false;
            gpuSetupIndex = -1;
            VAOs = Array.Empty<int>();
            VBOs = Array.Empty<int>();
            EBOs = Array.Empty<int>();
        }

        internal bool CanExportPersistentMeshCache()
        {
            lock (morphGate)
            {
                return fullMorphTargetsByMeshShapeName.Count == 0 &&
                       partialMorphTargetsByMeshShapeName.Count == 0 &&
                       vertexBuffersByMeshShapeName.Count == 0 &&
                       unsupportedFullMorphTargets.Count == 0 &&
                       string.IsNullOrWhiteSpace(cpuFullMorphRegistrationStatus);
            }
        }

        internal PersistentMeshCacheSnapshot BuildPersistentMeshCacheSnapshot()
        {
            return new PersistentMeshCacheSnapshot
            {
                LoadedMeshFiles = loadedMeshFiles.ToArray(),
                Positions = Positions.Select(CloneArray).ToArray(),
                Normals = Normals.Select(CloneArray).ToArray(),
                UVs = UVs.Select(CloneArray).ToArray(),
                UVs2 = UVs2.Select(CloneArray).ToArray(),
                HasUv1 = HasUv1.ToArray(),
                Colors = Colors.Select(CloneArray).ToArray(),
                Tangents = Tangents.Select(CloneArray).ToArray(),
                Binormals = Binormals.Select(CloneArray).ToArray(),
                BlendIndices = BlendIndicies.Select(CloneArray).ToArray(),
                BlendWeights = BlendWeights.Select(CloneArray).ToArray(),
                BlendBoneWeights = BlendBoneWeights.Select(CloneBoneWeights).ToArray(),
                BlendIndicesOriginal = BlendIndiciesOriginal.Select(CloneArray).ToArray(),
                BlendMeshNames = BlendMeshNames.ToArray(),
                Indices = Indices.Select(CloneArray).ToArray(),
                HasVertexColors = HasVertexColors.ToArray(),
                HasTangents = HasTangents.ToArray(),
                HasBinormals = HasBinormals.ToArray(),
                HasSkinning = HasSkinning.ToArray(),
                MaterialNames = MaterialNames.ToArray(),
                SubmeshNames = SubmeshNames.ToArray(),
                SubmeshParentNodeNames = SubmeshParentNodeNames.ToArray(),
                BlendIndexVertexCount = blendIndexStats?.VertexCount ?? 0,
                BlendIndexMaxIndex = blendIndexStats?.MaxIndex ?? 0
            };
        }

        private static Vector2[] CloneArray(Vector2[] source) => source.ToArray();
        private static Vector3[] CloneArray(Vector3[] source) => source.ToArray();
        private static Vector4[] CloneArray(Vector4[] source) => source.ToArray();
        private static uint[] CloneArray(uint[] source) => source.ToArray();

        private static TRBoneWeight[]? CloneBoneWeights(TRBoneWeight[]? source)
        {
            if (source == null)
            {
                return null;
            }

            var result = new TRBoneWeight[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                var item = source[i];
                result[i] = new TRBoneWeight
                {
                    RigIndex = item?.RigIndex ?? 0,
                    RigWeight = item?.RigWeight ?? 0f
                };
            }

            return result;
        }
    }

    internal sealed class PersistentMeshCacheSnapshot
    {
        public required string[] LoadedMeshFiles { get; init; }
        public required Vector3[][] Positions { get; init; }
        public required Vector3[][] Normals { get; init; }
        public required Vector2[][] UVs { get; init; }
        public required Vector2[][] UVs2 { get; init; }
        public required bool[] HasUv1 { get; init; }
        public required Vector4[][] Colors { get; init; }
        public required Vector4[][] Tangents { get; init; }
        public required Vector3[][] Binormals { get; init; }
        public required Vector4[][] BlendIndices { get; init; }
        public required Vector4[][] BlendWeights { get; init; }
        public required TRBoneWeight[]?[] BlendBoneWeights { get; init; }
        public required Vector4[][] BlendIndicesOriginal { get; init; }
        public required string[] BlendMeshNames { get; init; }
        public required uint[][] Indices { get; init; }
        public required bool[] HasVertexColors { get; init; }
        public required bool[] HasTangents { get; init; }
        public required bool[] HasBinormals { get; init; }
        public required bool[] HasSkinning { get; init; }
        public required string[] MaterialNames { get; init; }
        public required string[] SubmeshNames { get; init; }
        public required string?[] SubmeshParentNodeNames { get; init; }
        public required int BlendIndexVertexCount { get; init; }
        public required int BlendIndexMaxIndex { get; init; }
    }

    internal static class PersistentMeshCache
    {
        private const string Magic = "GFTMESH1";
        private static readonly object StoreGate = new object();
        private static readonly HashSet<string> PendingStores = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly System.Threading.SemaphoreSlim StoreSemaphore = new System.Threading.SemaphoreSlim(1, 1);

        private sealed class SourceStamp
        {
            public required string Path { get; init; }
            public required long Length { get; init; }
            public required long LastWriteUtcTicks { get; init; }
        }

        public static bool TryLoad(string trmdlPath, bool loadAllLods, out PersistentMeshCacheSnapshot snapshot)
        {
            return TryLoadFile(
                GetDefaultCachePath(trmdlPath, loadAllLods, enableCpuMorphRegistration: false),
                out snapshot);
        }

        internal static bool TryLoadFile(string cachePath, out PersistentMeshCacheSnapshot snapshot)
        {
            snapshot = null!;
            try
            {
                if (string.IsNullOrWhiteSpace(cachePath) || !File.Exists(cachePath))
                {
                    return false;
                }

                using var file = File.OpenRead(cachePath);
                using var reader = new BinaryReader(file, Encoding.UTF8, leaveOpen: false);

                if (reader.ReadString() != Magic)
                {
                    return false;
                }

                if (reader.ReadInt32() != Model.PersistentMeshCacheVersion)
                {
                    return false;
                }

                int sourceCount = reader.ReadInt32();
                var stamps = new List<SourceStamp>(sourceCount);
                for (int i = 0; i < sourceCount; i++)
                {
                    stamps.Add(new SourceStamp
                    {
                        Path = reader.ReadString(),
                        Length = reader.ReadInt64(),
                        LastWriteUtcTicks = reader.ReadInt64()
                    });
                }

                if (!AreSourceStampsCurrent(stamps))
                {
                    return false;
                }

                snapshot = ReadSnapshot(reader);
                return true;
            }
            catch
            {
                snapshot = null!;
                return false;
            }
        }

        public static void QueueStore(Model model, bool loadAllLods)
        {
            string cachePath = GetDefaultCachePath(
                model.TrmdlSourcePath,
                loadAllLods,
                enableCpuMorphRegistration: false);
            EnsureCacheDirectory(cachePath);
            QueueStoreAtPath(model, cachePath);
        }

        internal static void QueueStoreAtPath(Model model, string cachePath)
        {
            try
            {
                if (model == null ||
                    string.IsNullOrWhiteSpace(cachePath) ||
                    !model.CanExportPersistentMeshCache())
                {
                    return;
                }

                cachePath = Path.GetFullPath(cachePath);
                string? cacheDirectory = Path.GetDirectoryName(cachePath);
                if (string.IsNullOrWhiteSpace(cacheDirectory))
                {
                    return;
                }

                if (!Directory.Exists(cacheDirectory))
                {
                    return;
                }
                lock (StoreGate)
                {
                    if (!PendingStores.Add(cachePath))
                    {
                        return;
                    }
                }

                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    await StoreSemaphore.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        if (!model.CanExportPersistentMeshCache())
                        {
                            return;
                        }

                        var sourceStamps = BuildSourceStamps(model);
                        var snapshot = model.BuildPersistentMeshCacheSnapshot();
                        TryStore(cachePath, sourceStamps, snapshot);
                    }
                    catch
                    {
                        // Cache population is best-effort and must never fault a scene load.
                    }
                    finally
                    {
                        StoreSemaphore.Release();
                        lock (StoreGate)
                        {
                            PendingStores.Remove(cachePath);
                        }
                    }
                });
            }
            catch
            {
            }
        }

        private static void TryStore(string cachePath, List<SourceStamp> sourceStamps, PersistentMeshCacheSnapshot snapshot)
        {
            string? tempPath = null;
            try
            {
                string? cacheDirectory = Path.GetDirectoryName(cachePath);
                if (string.IsNullOrWhiteSpace(cacheDirectory) ||
                    !Directory.Exists(cacheDirectory))
                {
                    // The owner may have cleared the cache while this queued write was
                    // waiting. Do not recreate an unmarked cache directory behind it.
                    return;
                }

                tempPath = cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                using (var file = File.Create(tempPath))
                using (var writer = new BinaryWriter(file, Encoding.UTF8, leaveOpen: false))
                {
                    writer.Write(Magic);
                    writer.Write(Model.PersistentMeshCacheVersion);
                    writer.Write(sourceStamps.Count);
                    foreach (var stamp in sourceStamps)
                    {
                        writer.Write(stamp.Path);
                        writer.Write(stamp.Length);
                        writer.Write(stamp.LastWriteUtcTicks);
                    }

                    WriteSnapshot(writer, snapshot);
                }

                File.Move(tempPath, cachePath, overwrite: true);
                tempPath = null;
            }
            catch
            {
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(tempPath))
                {
                    try
                    {
                        File.Delete(tempPath);
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static List<SourceStamp> BuildSourceStamps(Model model)
        {
            var result = new List<SourceStamp>();

            void AddIfFile(string path)
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    return;
                }

                var info = new FileInfo(path);
                result.Add(new SourceStamp
                {
                    Path = path,
                    Length = info.Length,
                    LastWriteUtcTicks = info.LastWriteTimeUtc.Ticks
                });
            }

            AddIfFile(model.TrmdlSourcePath);
            foreach (var meshPath in model.LoadedMeshFiles)
            {
                AddIfFile(meshPath);
                try
                {
                    var trmsh = Trinity.Core.Utils.FlatBufferConverter.DeserializeFrom<TRMSH>(File.ReadAllBytes(meshPath));
                    if (!string.IsNullOrWhiteSpace(trmsh?.bufferFilePath))
                    {
                        AddIfFile(model.ResolveTrmbfPath(trmsh.bufferFilePath));
                    }
                }
                catch
                {
                }
            }

            return result;
        }

        private static bool AreSourceStampsCurrent(List<SourceStamp> stamps)
        {
            for (int i = 0; i < stamps.Count; i++)
            {
                var stamp = stamps[i];
                if (string.IsNullOrWhiteSpace(stamp.Path) || !File.Exists(stamp.Path))
                {
                    return false;
                }

                var info = new FileInfo(stamp.Path);
                if (info.Length != stamp.Length || info.LastWriteTimeUtc.Ticks != stamp.LastWriteUtcTicks)
                {
                    return false;
                }
            }

            return true;
        }

        internal static string GetDefaultCachePath(string trmdlPath, bool loadAllLods, bool enableCpuMorphRegistration)
        {
            string baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GFTool",
                "RendererCache",
                "ModelMesh");
            string key = $"{trmdlPath}|lods={(loadAllLods ? 1 : 0)}|morph={(enableCpuMorphRegistration ? 1 : 0)}|v={Model.PersistentMeshCacheVersion.ToString(CultureInfo.InvariantCulture)}";
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
            return Path.Combine(baseDir, hash + ".bin");
        }

        internal static void EnsureCacheDirectory(string cachePath)
        {
            string? cacheDirectory = Path.GetDirectoryName(cachePath);
            if (!string.IsNullOrWhiteSpace(cacheDirectory))
            {
                Directory.CreateDirectory(cacheDirectory);
            }
        }

        private static PersistentMeshCacheSnapshot ReadSnapshot(BinaryReader reader)
        {
            return new PersistentMeshCacheSnapshot
            {
                LoadedMeshFiles = ReadStringArray(reader),
                Positions = ReadVector3ArrayArray(reader),
                Normals = ReadVector3ArrayArray(reader),
                UVs = ReadVector2ArrayArray(reader),
                UVs2 = ReadVector2ArrayArray(reader),
                HasUv1 = ReadBoolArray(reader),
                Colors = ReadVector4ArrayArray(reader),
                Tangents = ReadVector4ArrayArray(reader),
                Binormals = ReadVector3ArrayArray(reader),
                BlendIndices = ReadVector4ArrayArray(reader),
                BlendWeights = ReadVector4ArrayArray(reader),
                BlendBoneWeights = ReadBoneWeightArrayArray(reader),
                BlendIndicesOriginal = ReadVector4ArrayArray(reader),
                BlendMeshNames = ReadStringArray(reader),
                Indices = ReadUIntArrayArray(reader),
                HasVertexColors = ReadBoolArray(reader),
                HasTangents = ReadBoolArray(reader),
                HasBinormals = ReadBoolArray(reader),
                HasSkinning = ReadBoolArray(reader),
                MaterialNames = ReadStringArray(reader),
                SubmeshNames = ReadStringArray(reader),
                SubmeshParentNodeNames = ReadNullableStringArray(reader),
                BlendIndexVertexCount = reader.ReadInt32(),
                BlendIndexMaxIndex = reader.ReadInt32()
            };
        }

        private static void WriteSnapshot(BinaryWriter writer, PersistentMeshCacheSnapshot snapshot)
        {
            WriteStringArray(writer, snapshot.LoadedMeshFiles);
            WriteVector3ArrayArray(writer, snapshot.Positions);
            WriteVector3ArrayArray(writer, snapshot.Normals);
            WriteVector2ArrayArray(writer, snapshot.UVs);
            WriteVector2ArrayArray(writer, snapshot.UVs2);
            WriteBoolArray(writer, snapshot.HasUv1);
            WriteVector4ArrayArray(writer, snapshot.Colors);
            WriteVector4ArrayArray(writer, snapshot.Tangents);
            WriteVector3ArrayArray(writer, snapshot.Binormals);
            WriteVector4ArrayArray(writer, snapshot.BlendIndices);
            WriteVector4ArrayArray(writer, snapshot.BlendWeights);
            WriteBoneWeightArrayArray(writer, snapshot.BlendBoneWeights);
            WriteVector4ArrayArray(writer, snapshot.BlendIndicesOriginal);
            WriteStringArray(writer, snapshot.BlendMeshNames);
            WriteUIntArrayArray(writer, snapshot.Indices);
            WriteBoolArray(writer, snapshot.HasVertexColors);
            WriteBoolArray(writer, snapshot.HasTangents);
            WriteBoolArray(writer, snapshot.HasBinormals);
            WriteBoolArray(writer, snapshot.HasSkinning);
            WriteStringArray(writer, snapshot.MaterialNames);
            WriteStringArray(writer, snapshot.SubmeshNames);
            WriteNullableStringArray(writer, snapshot.SubmeshParentNodeNames);
            writer.Write(snapshot.BlendIndexVertexCount);
            writer.Write(snapshot.BlendIndexMaxIndex);
        }

        private static string[] ReadStringArray(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            var result = new string[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = reader.ReadString();
            }

            return result;
        }

        private static string?[] ReadNullableStringArray(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            var result = new string?[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = reader.ReadBoolean() ? reader.ReadString() : null;
            }

            return result;
        }

        private static bool[] ReadBoolArray(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            var result = new bool[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = reader.ReadBoolean();
            }

            return result;
        }

        private static Vector2[][] ReadVector2ArrayArray(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            var result = new Vector2[count][];
            for (int i = 0; i < count; i++)
            {
                int length = reader.ReadInt32();
                var values = new Vector2[length];
                for (int j = 0; j < length; j++)
                {
                    values[j] = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                }
                result[i] = values;
            }

            return result;
        }

        private static Vector3[][] ReadVector3ArrayArray(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            var result = new Vector3[count][];
            for (int i = 0; i < count; i++)
            {
                int length = reader.ReadInt32();
                var values = new Vector3[length];
                for (int j = 0; j < length; j++)
                {
                    values[j] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                }
                result[i] = values;
            }

            return result;
        }

        private static Vector4[][] ReadVector4ArrayArray(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            var result = new Vector4[count][];
            for (int i = 0; i < count; i++)
            {
                int length = reader.ReadInt32();
                var values = new Vector4[length];
                for (int j = 0; j < length; j++)
                {
                    values[j] = new Vector4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                }
                result[i] = values;
            }

            return result;
        }

        private static uint[][] ReadUIntArrayArray(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            var result = new uint[count][];
            for (int i = 0; i < count; i++)
            {
                int length = reader.ReadInt32();
                var values = new uint[length];
                for (int j = 0; j < length; j++)
                {
                    values[j] = reader.ReadUInt32();
                }
                result[i] = values;
            }

            return result;
        }

        private static TRBoneWeight[]?[] ReadBoneWeightArrayArray(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            var result = new TRBoneWeight[]?[count];
            for (int i = 0; i < count; i++)
            {
                if (!reader.ReadBoolean())
                {
                    result[i] = null;
                    continue;
                }

                int length = reader.ReadInt32();
                var values = new TRBoneWeight[length];
                for (int j = 0; j < length; j++)
                {
                    values[j] = new TRBoneWeight
                    {
                        RigIndex = reader.ReadInt32(),
                        RigWeight = reader.ReadSingle()
                    };
                }
                result[i] = values;
            }

            return result;
        }

        private static void WriteStringArray(BinaryWriter writer, string[] values)
        {
            writer.Write(values.Length);
            for (int i = 0; i < values.Length; i++)
            {
                writer.Write(values[i] ?? string.Empty);
            }
        }

        private static void WriteNullableStringArray(BinaryWriter writer, string?[] values)
        {
            writer.Write(values.Length);
            for (int i = 0; i < values.Length; i++)
            {
                bool hasValue = values[i] != null;
                writer.Write(hasValue);
                if (hasValue)
                {
                    writer.Write(values[i]!);
                }
            }
        }

        private static void WriteBoolArray(BinaryWriter writer, bool[] values)
        {
            writer.Write(values.Length);
            for (int i = 0; i < values.Length; i++)
            {
                writer.Write(values[i]);
            }
        }

        private static void WriteVector2ArrayArray(BinaryWriter writer, Vector2[][] values)
        {
            writer.Write(values.Length);
            for (int i = 0; i < values.Length; i++)
            {
                writer.Write(values[i].Length);
                for (int j = 0; j < values[i].Length; j++)
                {
                    writer.Write(values[i][j].X);
                    writer.Write(values[i][j].Y);
                }
            }
        }

        private static void WriteVector3ArrayArray(BinaryWriter writer, Vector3[][] values)
        {
            writer.Write(values.Length);
            for (int i = 0; i < values.Length; i++)
            {
                writer.Write(values[i].Length);
                for (int j = 0; j < values[i].Length; j++)
                {
                    writer.Write(values[i][j].X);
                    writer.Write(values[i][j].Y);
                    writer.Write(values[i][j].Z);
                }
            }
        }

        private static void WriteVector4ArrayArray(BinaryWriter writer, Vector4[][] values)
        {
            writer.Write(values.Length);
            for (int i = 0; i < values.Length; i++)
            {
                writer.Write(values[i].Length);
                for (int j = 0; j < values[i].Length; j++)
                {
                    writer.Write(values[i][j].X);
                    writer.Write(values[i][j].Y);
                    writer.Write(values[i][j].Z);
                    writer.Write(values[i][j].W);
                }
            }
        }

        private static void WriteUIntArrayArray(BinaryWriter writer, uint[][] values)
        {
            writer.Write(values.Length);
            for (int i = 0; i < values.Length; i++)
            {
                writer.Write(values[i].Length);
                for (int j = 0; j < values[i].Length; j++)
                {
                    writer.Write(values[i][j]);
                }
            }
        }

        private static void WriteBoneWeightArrayArray(BinaryWriter writer, TRBoneWeight[]?[] values)
        {
            writer.Write(values.Length);
            for (int i = 0; i < values.Length; i++)
            {
                bool hasValue = values[i] != null;
                writer.Write(hasValue);
                if (!hasValue)
                {
                    continue;
                }

                writer.Write(values[i]!.Length);
                for (int j = 0; j < values[i]!.Length; j++)
                {
                    writer.Write(values[i]![j].RigIndex);
                    writer.Write(values[i]![j].RigWeight);
                }
            }
        }
    }
}
