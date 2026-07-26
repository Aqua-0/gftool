using OpenTK.Mathematics;
using System.Collections.Generic;
using TrinityModelViewer.Export;

namespace TrinitySceneView
{
    public partial class SceneViewerForm
    {
        private sealed class SceneModelSpawnCollectionResult
        {
            public required List<SceneModelSpawn> Spawns { get; init; }
            public required NpcSpawnerDbCache NpcDb { get; init; }
        }

        private sealed class SceneModelSpawn
        {
            public required string SceneFile { get; init; }
            public string? SceneObjectName { get; init; }
            public string? ParentSceneObjectName { get; init; }
            public Matrix4? ParentSceneObjectWorldMatrix { get; init; }
            public required string ModelPath { get; init; }
            public Matrix4 ModelMatrix { get; init; }
            public Matrix4 LocalMatrix { get; init; }
            public bool IsTrinsInstance { get; init; }
            public bool KeepWorldSrt { get; init; }
            public bool AttachTransformEnable { get; init; }
            public string? AttachJointName { get; init; }
        }

        private sealed class LoadedSceneModelInstance : GltfExporter.SceneModelInstance
        {
            public Matrix4 BaseTransform { get; set; }
            public Matrix4 LocalTransform { get; set; }
            public bool IsTrinsInstance { get; set; }
            public bool IsMergedBatch { get; set; }
            public bool FastOverviewBatch { get; set; }
            public int MergedInstanceCount { get; set; }
            public bool KeepWorldSrt { get; set; }
            public bool AttachTransformEnable { get; set; }
            public string? AttachJointName { get; set; }
            public string? ParentSceneObjectName { get; set; }
            public Matrix4? ParentSceneObjectWorldMatrix { get; set; }
        }

        private sealed class FastOverviewLoadPlan
        {
            public required List<FastOverviewBatchGroup> BatchGroups { get; init; }
            public required List<SceneModelSpawn> IndividualSpawns { get; init; }
        }

        private sealed class FastOverviewBatchGroup
        {
            public required string ResolvedPath { get; init; }
            public required List<SceneModelSpawn> Spawns { get; init; }
        }

        private sealed class FastOverviewBatchLoadResult
        {
            public bool Applied { get; init; }
            public bool PersistentCacheHit { get; init; }
            public int CompletedSpawns { get; init; }
            public int MergedInstanceCount { get; init; }
            public double UploadElapsedMs { get; init; }
            public string? SourcePath { get; init; }
        }

        private sealed class TemplateModelSpawn
        {
            public string? SceneObjectName { get; init; }
            public required string ModelPath { get; init; }
            public Matrix4 LocalMatrix { get; init; }
        }

        private sealed record NpcSpawnerCandidate(
            string SourceDb,
            string AssetId,
            string? AppearanceId,
            string? EncountId,
            int? Priority,
            int ActivationConditionsCount,
            string? ActivationConditionsJson,
            string? CreateScenePath
        );

        private sealed class NpcSpawnerDbCache
        {
            public bool Loaded { get; set; }

            public Dictionary<string, List<string>> SpawnerObjectNameToAssetIds { get; } = new(StringComparer.Ordinal);
            public Dictionary<string, List<NpcSpawnerCandidate>> SpawnerObjectNameToCandidates { get; } = new(StringComparer.Ordinal);
            public Dictionary<string, SpawnerTransform> SpawnerTransforms { get; } = new(StringComparer.Ordinal);

            public Dictionary<string, List<string>> AssetIdToObjectTemplateIds { get; } = new(StringComparer.Ordinal);
            public Dictionary<string, string> ObjectTemplateIdToPath { get; } = new(StringComparer.Ordinal);

            public Dictionary<string, List<string>> CreateScenePathToSpawnerObjectNames { get; } = new(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> SpawnedSpawnerObjectNames { get; } = new(StringComparer.Ordinal);
        }

        private readonly record struct SpawnerTransform(
            string Name,
            float X,
            float Y,
            float Z,
            float RotX,
            float RotY,
            float RotZ,
            bool AttachTransformEnable
        );
    }
}
