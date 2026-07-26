using OpenTK.Mathematics;

namespace TrinitySceneView
{
    public static partial class SceneFastOverviewBatching
    {
        public sealed class TestSpawn
        {
            public required string SceneFile { get; init; }
            public string? SceneObjectName { get; init; }
            public required string ModelPath { get; init; }
            public required string ResolvedModelPath { get; init; }
            public Matrix4 ModelMatrix { get; init; }
            public bool IsTrinsInstance { get; init; }
            public bool AttachTransformEnable { get; init; }
            public string? AttachJointName { get; init; }
            public string? ParentSceneObjectName { get; init; }
        }

        public sealed class BatchGroup<TSpawn>
        {
            public required string ResolvedModelPath { get; init; }
            public required IReadOnlyList<TSpawn> Spawns { get; init; }
        }

        public sealed class Plan<TSpawn>
        {
            public required IReadOnlyList<BatchGroup<TSpawn>> BatchGroups { get; init; }
            public required IReadOnlyList<TSpawn> IndividualSpawns { get; init; }
        }

        public static TestSpawn CreateTestSpawn(
            string sceneFile,
            string? sceneObjectName,
            string resolvedModelPath,
            Matrix4 modelMatrix,
            bool isTrins,
            bool attach = false,
            string? attachJointName = null,
            string? parent = null)
        {
            return new TestSpawn
            {
                SceneFile = sceneFile,
                SceneObjectName = sceneObjectName,
                ModelPath = resolvedModelPath,
                ResolvedModelPath = resolvedModelPath,
                ModelMatrix = modelMatrix,
                IsTrinsInstance = isTrins,
                AttachTransformEnable = attach,
                AttachJointName = attachJointName,
                ParentSceneObjectName = parent
            };
        }

        public static Plan<TestSpawn> BuildPlanForTests(IReadOnlyList<TestSpawn> spawns, int minBatchCount)
        {
            var attachParentNames = GetAttachParentNames(
                spawns,
                spawn => spawn.AttachTransformEnable,
                spawn => spawn.ParentSceneObjectName);

            return BuildPlan(
                spawns,
                minBatchCount,
                spawn => spawn.ResolvedModelPath,
                spawn =>
                {
                    if (!string.IsNullOrWhiteSpace(spawn.SceneObjectName) &&
                        attachParentNames.Contains(spawn.SceneObjectName))
                    {
                        return "attach-parent-source";
                    }

                    return GetEarlyBatchRejectReason(
                        spawn.ResolvedModelPath,
                        spawn.AttachTransformEnable,
                        spawn.AttachJointName,
                        spawn.ParentSceneObjectName);
                });
        }

        public static bool IsEarlyBatchCandidate(
            string? resolvedModelPath,
            bool attachTransformEnable,
            string? attachJointName,
            string? parentSceneObjectName)
        {
            return GetEarlyBatchRejectReason(resolvedModelPath, attachTransformEnable, attachJointName, parentSceneObjectName) == null;
        }

        public static string? GetEarlyBatchRejectReason(
            string? resolvedModelPath,
            bool attachTransformEnable,
            string? attachJointName,
            string? parentSceneObjectName)
        {
            if (string.IsNullOrWhiteSpace(resolvedModelPath))
            {
                return "missing-path";
            }

            if (attachTransformEnable && !string.IsNullOrWhiteSpace(parentSceneObjectName))
            {
                return "attach-parent";
            }

            if (!string.IsNullOrWhiteSpace(attachJointName))
            {
                return "attach-joint";
            }

            return null;
        }

        public static string GetBatchKey(string resolvedModelPath)
        {
            return resolvedModelPath.Trim().Replace('\\', '/');
        }

        public static HashSet<string> GetAttachParentNames<TSpawn>(
            IEnumerable<TSpawn> spawns,
            Func<TSpawn, bool> attachTransformEnabledSelector,
            Func<TSpawn, string?> parentNameSelector)
        {
            var parentNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var spawn in spawns)
            {
                if (!attachTransformEnabledSelector(spawn))
                {
                    continue;
                }

                var parentName = parentNameSelector(spawn);
                if (!string.IsNullOrWhiteSpace(parentName))
                {
                    parentNames.Add(parentName);
                }
            }

            return parentNames;
        }

        public static Plan<TSpawn> BuildPlan<TSpawn>(
            IReadOnlyList<TSpawn> spawns,
            int minBatchCount,
            Func<TSpawn, string?> resolvedModelPathSelector,
            Func<TSpawn, string?> rejectReasonSelector)
        {
            var batchGroups = new List<BatchGroup<TSpawn>>();
            var individualSpawns = new List<TSpawn>();
            var groups = new Dictionary<string, List<TSpawn>>(StringComparer.OrdinalIgnoreCase);

            foreach (var spawn in spawns)
            {
                var resolvedModelPath = resolvedModelPathSelector(spawn);
                var rejectReason = rejectReasonSelector(spawn);
                if (rejectReason != null || string.IsNullOrWhiteSpace(resolvedModelPath))
                {
                    individualSpawns.Add(spawn);
                    continue;
                }

                var key = GetBatchKey(resolvedModelPath);
                if (!groups.TryGetValue(key, out var group))
                {
                    group = new List<TSpawn>();
                    groups.Add(key, group);
                }

                group.Add(spawn);
            }

            var threshold = Math.Max(1, minBatchCount);
            foreach (var group in groups.OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (group.Value.Count >= threshold)
                {
                    batchGroups.Add(new BatchGroup<TSpawn>
                    {
                        ResolvedModelPath = group.Key,
                        Spawns = group.Value
                    });
                }
                else
                {
                    individualSpawns.AddRange(group.Value);
                }
            }

            return new Plan<TSpawn>
            {
                BatchGroups = batchGroups,
                IndividualSpawns = individualSpawns
            };
        }
    }

    public partial class SceneViewerForm
    {
        private FastOverviewLoadPlan BuildFastOverviewLoadPlan(
            IReadOnlyList<SceneModelSpawn> spawns,
            IReadOnlyList<string?> resolvedPaths,
            int minBatchCount)
        {
            var batchGroups = new List<FastOverviewBatchGroup>();
            var individualSpawns = new List<SceneModelSpawn>();
            var grouped = new Dictionary<string, FastOverviewBatchGroup>(StringComparer.OrdinalIgnoreCase);
            var rejectCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var attachParentNames = SceneFastOverviewBatching.GetAttachParentNames(
                spawns,
                spawn => spawn.AttachTransformEnable && !spawn.KeepWorldSrt,
                spawn => spawn.ParentSceneObjectWorldMatrix.HasValue ? null : spawn.ParentSceneObjectName);

            for (int i = 0; i < spawns.Count; i++)
            {
                var spawn = spawns[i];
                var resolvedPath = i < resolvedPaths.Count ? resolvedPaths[i] : null;
                var rejectReason = GetEarlyBatchRejectReason(spawn, resolvedPath, attachParentNames);
                if (rejectReason != null || string.IsNullOrWhiteSpace(resolvedPath))
                {
                    individualSpawns.Add(spawn);
                    var reason = rejectReason ?? "missing-path";
                    rejectCounts[reason] = rejectCounts.TryGetValue(reason, out var count) ? count + 1 : 1;
                    continue;
                }

                var key = SceneFastOverviewBatching.GetBatchKey(resolvedPath);
                if (!grouped.TryGetValue(key, out var group))
                {
                    group = new FastOverviewBatchGroup
                    {
                        ResolvedPath = resolvedPath,
                        Spawns = new List<SceneModelSpawn>()
                    };
                    grouped.Add(key, group);
                }

                group.Spawns.Add(spawn);
            }

            var threshold = Math.Max(1, minBatchCount);
            foreach (var group in grouped.Values.OrderByDescending(g => g.Spawns.Count))
            {
                if (group.Spawns.Count >= threshold)
                {
                    batchGroups.Add(group);
                }
                else
                {
                    individualSpawns.AddRange(group.Spawns);
                    rejectCounts["below-threshold"] = rejectCounts.TryGetValue("below-threshold", out var count)
                        ? count + group.Spawns.Count
                        : group.Spawns.Count;
                }
            }

            if (SceneDiagnosticsEnabled)
            {
                foreach (var reject in rejectCounts.OrderByDescending(r => r.Value).ThenBy(r => r.Key).Take(8))
                {
                    GFTool.Renderer.Core.MessageHandler.Instance.AddMessage(
                        GFTool.Renderer.Core.MessageType.LOG,
                        $"[Scene][FastOverviewReject] reason={reject.Key} count={reject.Value}");
                }
            }

            return new FastOverviewLoadPlan
            {
                BatchGroups = batchGroups,
                IndividualSpawns = individualSpawns
            };
        }

        private static string? GetEarlyBatchRejectReason(SceneModelSpawn spawn, string? resolvedPath)
        {
            return GetEarlyBatchRejectReason(spawn, resolvedPath, requiredParentNames: null);
        }

        private static string? GetEarlyBatchRejectReason(SceneModelSpawn spawn, string? resolvedPath, HashSet<string>? requiredParentNames)
        {
            if (requiredParentNames != null &&
                !string.IsNullOrWhiteSpace(spawn.SceneObjectName) &&
                requiredParentNames.Contains(spawn.SceneObjectName))
            {
                return "attach-parent-source";
            }

            var attachTransformEnable = spawn.AttachTransformEnable && !spawn.KeepWorldSrt;
            var attachJointName = attachTransformEnable ? spawn.AttachJointName : null;
            var parentSceneObjectName = attachTransformEnable ? spawn.ParentSceneObjectName : null;

            return SceneFastOverviewBatching.GetEarlyBatchRejectReason(
                resolvedPath,
                attachTransformEnable,
                attachJointName,
                parentSceneObjectName);
        }
    }
}
