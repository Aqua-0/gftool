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
            public required string ModelPath { get; init; }
            public Matrix4 ModelMatrix { get; init; }
        }

        private sealed class LoadedSceneModelInstance : GltfExporter.SceneModelInstance
        {
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
