using GFTool.Renderer.Core;
using OpenTK.Mathematics;
using System.Text.Json;

namespace TrinitySceneView
{
    public partial class SceneViewerForm
    {
        private const string SceneScanCacheExtension = ".scene-scan.json";

        private bool TryLoadCachedSceneModelSpawns(string sceneFile, CancellationToken token, out SceneModelSpawnCollectionResult result)
        {
            result = null!;
            token.ThrowIfCancellationRequested();

            try
            {
                var cacheKey = BuildSceneScanCacheKey(sceneFile);
                if (!SceneDiskCache.TryReadText(config.SceneDiskCacheDirectory, cacheKey, SceneScanCacheExtension, out var json))
                {
                    return false;
                }

                var cache = JsonSerializer.Deserialize<CachedSceneScan>(json);
                if (!IsSceneScanCacheValid(sceneFile, cache))
                {
                    return false;
                }

                var npcDb = new NpcSpawnerDbCache();
                if (cache!.SpawnedSpawnerObjectNames != null)
                {
                    foreach (var name in cache.SpawnedSpawnerObjectNames)
                    {
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            npcDb.SpawnedSpawnerObjectNames.Add(name);
                        }
                    }
                }

                result = new SceneModelSpawnCollectionResult
                {
                    Spawns = cache.Spawns.Select(FromCachedSpawn).ToList(),
                    NpcDb = npcDb
                };

                if (SceneDiagnosticsEnabled)
                {
                    MessageHandler.Instance.AddMessage(
                        MessageType.LOG,
                        $"[SceneCache] scan hit scene={Path.GetFileName(sceneFile)} spawns={result.Spawns.Count}");
                }

                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (SceneDiagnosticsEnabled)
                {
                    MessageHandler.Instance.AddMessage(
                        MessageType.WARNING,
                        $"[SceneCache] scan read failed: {ex.GetType().Name}: {ex.Message}");
                }

                return false;
            }
        }

        private void TryWriteCachedSceneModelSpawns(string sceneFile, SceneModelSpawnCollectionResult result)
        {
            try
            {
                var cache = new CachedSceneScan
                {
                    Version = SceneDiskCache.CacheFormatVersion,
                    SceneFile = NormalizeCachePath(sceneFile),
                    AssetRoot = NormalizeCachePath(assetRoot),
                    LoadNpcSpawnerModels = config.LoadNpcSpawnerModels,
                    LoadStreamingGroupSubScenes = config.LoadStreamingGroupSubScenes,
                    PreferredSceneVariant = preferredSceneVariant,
                    Dependencies = BuildSceneScanDependencies(sceneFile, result.Spawns),
                    Spawns = result.Spawns.Select(ToCachedSpawn).ToList(),
                    SpawnedSpawnerObjectNames = result.NpcDb.SpawnedSpawnerObjectNames.ToList()
                };

                var json = JsonSerializer.Serialize(cache);
                SceneDiskCache.WriteText(config.SceneDiskCacheDirectory, BuildSceneScanCacheKey(sceneFile), SceneScanCacheExtension, json);

                if (SceneDiagnosticsEnabled)
                {
                    MessageHandler.Instance.AddMessage(
                        MessageType.LOG,
                        $"[SceneCache] scan wrote scene={Path.GetFileName(sceneFile)} spawns={cache.Spawns.Count} deps={cache.Dependencies.Count}");
                }
            }
            catch (Exception ex)
            {
                if (SceneDiagnosticsEnabled)
                {
                    MessageHandler.Instance.AddMessage(
                        MessageType.WARNING,
                        $"[SceneCache] scan write failed: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        private string BuildSceneScanCacheKey(string sceneFile)
        {
            return SceneDiskCache.ComputeSha256Hex(
                string.Join(
                    "\n",
                    "scene-scan",
                    SceneDiskCache.CacheFormatVersion.ToString(),
                    NormalizeCachePath(sceneFile),
                    NormalizeCachePath(assetRoot),
                    config.LoadNpcSpawnerModels ? "npc=1" : "npc=0",
                    config.LoadStreamingGroupSubScenes ? "streaming=1" : "streaming=0",
                    preferredSceneVariant?.ToString() ?? "variant=null"));
        }

        private bool IsSceneScanCacheValid(string sceneFile, CachedSceneScan? cache)
        {
            if (cache == null ||
                cache.Version != SceneDiskCache.CacheFormatVersion ||
                !string.Equals(cache.SceneFile, NormalizeCachePath(sceneFile), StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(cache.AssetRoot, NormalizeCachePath(assetRoot), StringComparison.OrdinalIgnoreCase) ||
                cache.LoadNpcSpawnerModels != config.LoadNpcSpawnerModels ||
                cache.LoadStreamingGroupSubScenes != config.LoadStreamingGroupSubScenes ||
                cache.PreferredSceneVariant != preferredSceneVariant ||
                cache.Dependencies == null ||
                cache.Dependencies.Count == 0 ||
                cache.Spawns == null)
            {
                return false;
            }

            foreach (var dependency in cache.Dependencies)
            {
                if (!IsDependencyCurrent(dependency))
                {
                    return false;
                }
            }

            return true;
        }

        private static List<CachedFileFingerprint> BuildSceneScanDependencies(string sceneFile, IReadOnlyList<SceneModelSpawn> spawns)
        {
            return new[] { sceneFile }
                .Concat(spawns.Select(spawn => spawn.SceneFile))
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => NormalizeCachePath(path!)!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(CreateFingerprint)
                .ToList();
        }

        private static CachedFileFingerprint CreateFingerprint(string path)
        {
            if (!File.Exists(path))
            {
                return new CachedFileFingerprint
                {
                    Path = path,
                    Length = -1,
                    LastWriteUtcTicks = 0
                };
            }

            var info = new FileInfo(path);
            return new CachedFileFingerprint
            {
                Path = path,
                Length = info.Length,
                LastWriteUtcTicks = info.LastWriteTimeUtc.Ticks
            };
        }

        private static bool IsDependencyCurrent(CachedFileFingerprint dependency)
        {
            if (string.IsNullOrWhiteSpace(dependency.Path) || !File.Exists(dependency.Path))
            {
                return false;
            }

            var info = new FileInfo(dependency.Path);
            return info.Length == dependency.Length &&
                   info.LastWriteTimeUtc.Ticks == dependency.LastWriteUtcTicks;
        }

        private static CachedSceneModelSpawn ToCachedSpawn(SceneModelSpawn spawn)
        {
            return new CachedSceneModelSpawn
            {
                SceneFile = spawn.SceneFile,
                SceneObjectName = spawn.SceneObjectName,
                ParentSceneObjectName = spawn.ParentSceneObjectName,
                ParentSceneObjectWorldMatrix = spawn.ParentSceneObjectWorldMatrix.HasValue
                    ? ToCachedMatrix(spawn.ParentSceneObjectWorldMatrix.Value)
                    : null,
                ModelPath = spawn.ModelPath,
                ModelMatrix = ToCachedMatrix(spawn.ModelMatrix),
                LocalMatrix = ToCachedMatrix(spawn.LocalMatrix),
                IsTrinsInstance = spawn.IsTrinsInstance,
                KeepWorldSrt = spawn.KeepWorldSrt,
                AttachTransformEnable = spawn.AttachTransformEnable,
                AttachJointName = spawn.AttachJointName
            };
        }

        private static SceneModelSpawn FromCachedSpawn(CachedSceneModelSpawn spawn)
        {
            return new SceneModelSpawn
            {
                SceneFile = spawn.SceneFile,
                SceneObjectName = spawn.SceneObjectName,
                ParentSceneObjectName = spawn.ParentSceneObjectName,
                ParentSceneObjectWorldMatrix = spawn.ParentSceneObjectWorldMatrix != null
                    ? FromCachedMatrix(spawn.ParentSceneObjectWorldMatrix)
                    : null,
                ModelPath = spawn.ModelPath,
                ModelMatrix = FromCachedMatrix(spawn.ModelMatrix),
                LocalMatrix = FromCachedMatrix(spawn.LocalMatrix),
                IsTrinsInstance = spawn.IsTrinsInstance,
                KeepWorldSrt = spawn.KeepWorldSrt,
                AttachTransformEnable = spawn.AttachTransformEnable,
                AttachJointName = spawn.AttachJointName
            };
        }

        private static CachedMatrix ToCachedMatrix(Matrix4 matrix)
        {
            return new CachedMatrix
            {
                Values =
                [
                    matrix.M11, matrix.M12, matrix.M13, matrix.M14,
                    matrix.M21, matrix.M22, matrix.M23, matrix.M24,
                    matrix.M31, matrix.M32, matrix.M33, matrix.M34,
                    matrix.M41, matrix.M42, matrix.M43, matrix.M44
                ]
            };
        }

        private static Matrix4 FromCachedMatrix(CachedMatrix matrix)
        {
            var values = matrix.Values;
            if (values == null || values.Length != 16)
            {
                return Matrix4.Identity;
            }

            return new Matrix4(
                values[0], values[1], values[2], values[3],
                values[4], values[5], values[6], values[7],
                values[8], values[9], values[10], values[11],
                values[12], values[13], values[14], values[15]);
        }

        private static string? NormalizeCachePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            return Path.GetFullPath(path).Replace('\\', '/');
        }

        private sealed class CachedSceneScan
        {
            public int Version { get; set; }
            public string? SceneFile { get; set; }
            public string? AssetRoot { get; set; }
            public bool LoadNpcSpawnerModels { get; set; }
            public bool LoadStreamingGroupSubScenes { get; set; }
            public int? PreferredSceneVariant { get; set; }
            public List<CachedFileFingerprint> Dependencies { get; set; } = new();
            public List<CachedSceneModelSpawn> Spawns { get; set; } = new();
            public List<string> SpawnedSpawnerObjectNames { get; set; } = new();
        }

        private sealed class CachedFileFingerprint
        {
            public string? Path { get; set; }
            public long Length { get; set; }
            public long LastWriteUtcTicks { get; set; }
        }

        private sealed class CachedSceneModelSpawn
        {
            public string SceneFile { get; set; } = string.Empty;
            public string? SceneObjectName { get; set; }
            public string? ParentSceneObjectName { get; set; }
            public CachedMatrix? ParentSceneObjectWorldMatrix { get; set; }
            public string ModelPath { get; set; } = string.Empty;
            public CachedMatrix ModelMatrix { get; set; } = new();
            public CachedMatrix LocalMatrix { get; set; } = new();
            public bool IsTrinsInstance { get; set; }
            public bool KeepWorldSrt { get; set; }
            public bool AttachTransformEnable { get; set; }
            public string? AttachJointName { get; set; }
        }

        private sealed class CachedMatrix
        {
            public float[] Values { get; set; } = Array.Empty<float>();
        }
    }
}
