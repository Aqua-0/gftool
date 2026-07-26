using GFTool.Core.Flatbuffers.TR.Scene.Components;
using Trinity.Core.Utils;

namespace GFTool.Core.Flatbuffers.TR.Scene
{
    public enum SceneReferenceKind
    {
        TopLevelSubScene,
        ChunkSubSceneFallback,
        ZaBattleRuntimeInjection
    }

    public sealed record SceneReference(
        string RequestedPath,
        string? ResolvedPath,
        SceneReferenceKind Kind,
        string? Group = null,
        bool IsStreamingGroup = false,
        bool IsClodStreamingFallback = false);

    public static class SceneReferencePlanner
    {
        private const string ZaBattleMarker = "_za_battle_";

        public static IReadOnlyList<SceneReference> GetDirectSceneReferences(
            string sceneFile,
            TRSCN trscn,
            int? preferredVariant = null,
            bool includeZaBattleRuntimeInjections = false)
        {
            var refs = new List<SceneReference>();
            if (string.IsNullOrWhiteSpace(sceneFile) || trscn == null)
            {
                return refs;
            }

            var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var chunkSubScenes = CollectChunkSubSceneMetadata(sceneFile, trscn.Chunks, preferredVariant);

            if (trscn.SubScenes != null)
            {
                foreach (var path in trscn.SubScenes)
                {
                    chunkSubScenes.TryGetValue(MakeReferenceKey(sceneFile, path, preferredVariant), out var metadata);
                    AddReference(
                        refs,
                        emitted,
                        sceneFile,
                        path,
                        SceneReferenceKind.TopLevelSubScene,
                        preferredVariant,
                        metadata?.Group,
                        metadata?.IsStreamingGroup ?? false);
                }
            }

            if (trscn.Chunks != null)
            {
                foreach (var chunk in trscn.Chunks)
                {
                    if (!string.Equals(chunk.Type, nameof(SubScene), StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (!TryParseSubScene(chunk, out var sub) || string.IsNullOrWhiteSpace(sub.Filepath))
                    {
                        continue;
                    }

                    chunkSubScenes.TryGetValue(MakeReferenceKey(sceneFile, sub.Filepath, preferredVariant), out var metadata);
                    AddReference(
                        refs,
                        emitted,
                        sceneFile,
                        sub.Filepath,
                        SceneReferenceKind.ChunkSubSceneFallback,
                        preferredVariant,
                        string.IsNullOrWhiteSpace(sub.Group) ? metadata?.Group : sub.Group,
                        metadata?.IsStreamingGroup ?? false);
                }
            }

            if (includeZaBattleRuntimeInjections)
            {
                foreach (var path in GetZaBattleRuntimeInjectionRequests(sceneFile, trscn))
                {
                    AddReference(refs, emitted, sceneFile, path, SceneReferenceKind.ZaBattleRuntimeInjection, preferredVariant, group: null, isStreamingGroup: false);
                }
            }

            return refs;
        }

        public static bool TryReadScene(string sceneFile, out TRSCN trscn)
        {
            trscn = null!;
            if (string.IsNullOrWhiteSpace(sceneFile))
            {
                return false;
            }

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(sceneFile);
            }
            catch
            {
                return false;
            }

            return TryParseFlatBuffer(bytes, out trscn) ||
                   (bytes.Length > 4 && TryParseFlatBuffer(bytes.AsSpan(4).ToArray(), out trscn));
        }

        public static bool TryParseSubScene(SceneChunk chunk, out SubScene subScene)
        {
            subScene = null!;
            if (chunk?.Data == null)
            {
                return false;
            }

            return TryParseFlatBuffer(chunk.Data, out subScene) ||
                   (chunk.Data.Length > 4 && TryParseFlatBuffer(chunk.Data.AsSpan(4).ToArray(), out subScene));
        }

        public static string? ResolveSceneReference(string sceneFile, string referencedPath, int? preferredVariant = null)
        {
            if (string.IsNullOrWhiteSpace(sceneFile) || string.IsNullOrWhiteSpace(referencedPath))
            {
                return null;
            }

            string baseDir = Path.GetDirectoryName(sceneFile) ?? string.Empty;
            string normalized = referencedPath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            string candidate;

            try
            {
                candidate = Path.IsPathRooted(normalized)
                    ? Path.GetFullPath(normalized)
                    : Path.GetFullPath(Path.Combine(baseDir, normalized));
            }
            catch
            {
                return null;
            }

            foreach (var baseCandidate in GetExtensionCandidates(candidate))
            {
                if (File.Exists(baseCandidate))
                {
                    return baseCandidate;
                }

                string ext = Path.GetExtension(baseCandidate);
                if (string.IsNullOrWhiteSpace(ext))
                {
                    continue;
                }

                string fileNameNoExt = Path.GetFileNameWithoutExtension(baseCandidate);
                if (fileNameNoExt.EndsWith("_0", StringComparison.OrdinalIgnoreCase) ||
                    fileNameNoExt.EndsWith("_1", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string noExt = baseCandidate.Substring(0, baseCandidate.Length - ext.Length);
                var variants = preferredVariant.HasValue
                    ? new[] { preferredVariant.Value, preferredVariant.Value == 0 ? 1 : 0 }
                    : new[] { 0, 1 };

                foreach (int variant in variants)
                {
                    string withVariant = $"{noExt}_{variant}{ext}";
                    if (File.Exists(withVariant))
                    {
                        return withVariant;
                    }
                }
            }

            return null;
        }

        private static void AddReference(
            List<SceneReference> refs,
            HashSet<string> emitted,
            string sceneFile,
            string? requestedPath,
            SceneReferenceKind kind,
            int? preferredVariant,
            string? group,
            bool isStreamingGroup)
        {
            if (string.IsNullOrWhiteSpace(requestedPath))
            {
                return;
            }

            var resolved = ResolveSceneReference(sceneFile, requestedPath, preferredVariant);
            var key = resolved ?? MakeUnresolvedReferenceKey(sceneFile, requestedPath);
            if (!emitted.Add(key))
            {
                return;
            }

            refs.Add(new SceneReference(
                requestedPath,
                resolved,
                kind,
                group,
                isStreamingGroup,
                IsClodStreamingFallbackReference(requestedPath, resolved)));
        }

        private static bool IsClodStreamingFallbackReference(string requestedPath, string? resolvedPath)
        {
            return IsClodStreamingFallbackPath(requestedPath) ||
                   IsClodStreamingFallbackPath(resolvedPath);
        }

        private static bool IsClodStreamingFallbackPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            var normalized = path.Replace('\\', '/');
            var name = Path.GetFileNameWithoutExtension(normalized);
            return name.Contains("_clod_streaming_", StringComparison.OrdinalIgnoreCase);
        }

        private sealed record ChunkSubSceneMetadata(string? Group, bool IsStreamingGroup);

        private static Dictionary<string, ChunkSubSceneMetadata> CollectChunkSubSceneMetadata(
            string sceneFile,
            SceneChunk[]? chunks,
            int? preferredVariant)
        {
            var result = new Dictionary<string, ChunkSubSceneMetadata>(StringComparer.OrdinalIgnoreCase);
            if (chunks == null || chunks.Length == 0)
            {
                return result;
            }

            CollectChunkSubSceneMetadata(sceneFile, chunks, preferredVariant, isInsideStreamingGroup: false, result);
            return result;
        }

        private static void CollectChunkSubSceneMetadata(
            string sceneFile,
            IEnumerable<SceneChunk> chunks,
            int? preferredVariant,
            bool isInsideStreamingGroup,
            Dictionary<string, ChunkSubSceneMetadata> result)
        {
            foreach (var chunk in chunks)
            {
                if (chunk == null)
                {
                    continue;
                }

                bool childIsInsideStreamingGroup = isInsideStreamingGroup || IsStreamingGroupChunk(chunk.Type);
                if (string.Equals(chunk.Type, nameof(SubScene), StringComparison.Ordinal) &&
                    TryParseSubScene(chunk, out var sub) &&
                    !string.IsNullOrWhiteSpace(sub.Filepath))
                {
                    var key = MakeReferenceKey(sceneFile, sub.Filepath, preferredVariant);
                    var next = new ChunkSubSceneMetadata(
                        string.IsNullOrWhiteSpace(sub.Group) ? null : sub.Group,
                        childIsInsideStreamingGroup);
                    if (result.TryGetValue(key, out var existing))
                    {
                        next = new ChunkSubSceneMetadata(
                            existing.Group ?? next.Group,
                            existing.IsStreamingGroup || next.IsStreamingGroup);
                    }

                    result[key] = next;
                }

                if (chunk.Children != null && chunk.Children.Length > 0)
                {
                    CollectChunkSubSceneMetadata(sceneFile, chunk.Children, preferredVariant, childIsInsideStreamingGroup, result);
                }
            }
        }

        private static bool IsStreamingGroupChunk(string? chunkType)
        {
            return !string.IsNullOrWhiteSpace(chunkType) &&
                   chunkType.EndsWith("StreamingGroup", StringComparison.Ordinal);
        }

        private static string MakeReferenceKey(string sceneFile, string? requestedPath, int? preferredVariant)
        {
            if (string.IsNullOrWhiteSpace(requestedPath))
            {
                return string.Empty;
            }

            return ResolveSceneReference(sceneFile, requestedPath, preferredVariant) ??
                   MakeUnresolvedReferenceKey(sceneFile, requestedPath);
        }

        private static IEnumerable<string> GetZaBattleRuntimeInjectionRequests(string sceneFile, TRSCN trscn)
        {
            var sceneName = GetSceneName(sceneFile, trscn);
            var markerIndex = sceneName.IndexOf(ZaBattleMarker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex <= 0)
            {
                yield break;
            }

            var areaName = sceneName.Substring(0, markerIndex);
            var variationId = sceneName.Substring(markerIndex + ZaBattleMarker.Length);
            if (string.IsNullOrWhiteSpace(areaName) || string.IsNullOrWhiteSpace(variationId))
            {
                yield break;
            }

            string sceneDir = Path.GetDirectoryName(sceneFile) ?? string.Empty;
            string dirName = Path.GetFileName(sceneDir);
            string prefix = string.Equals(dirName, sceneName + "_", StringComparison.OrdinalIgnoreCase)
                ? $"../{sceneName}/sub_scene/"
                : "sub_scene/";

            var suffixes = new[] { "placement_spawner", "royale_spawner", "medal_spawner" };
            foreach (var suffix in suffixes)
            {
                var injectedSceneName = $"{areaName}_{variationId}_{suffix}";
                yield return $"{prefix}{injectedSceneName}_/{injectedSceneName}.trscn";
            }
        }

        private static string GetSceneName(string sceneFile, TRSCN trscn)
        {
            if (!string.IsNullOrWhiteSpace(trscn.Name))
            {
                return trscn.Name;
            }

            var name = Path.GetFileNameWithoutExtension(sceneFile) ?? string.Empty;
            if (name.EndsWith("_0", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith("_1", StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring(0, name.Length - 2);
            }

            return name;
        }

        private static IEnumerable<string> GetExtensionCandidates(string candidate)
        {
            yield return candidate;

            if (candidate.EndsWith(".trs", StringComparison.OrdinalIgnoreCase))
            {
                yield return candidate.Substring(0, candidate.Length - ".trs".Length) + ".trscn";
            }
        }

        private static bool TryParseFlatBuffer<T>(byte[] bytes, out T obj) where T : class
        {
            obj = null!;
            try
            {
                obj = FlatBufferConverter.DeserializeFrom<T>(bytes);
                return obj != null;
            }
            catch
            {
                return false;
            }
        }

        private static string MakeUnresolvedReferenceKey(string sceneFile, string requestedPath)
        {
            try
            {
                string baseDir = Path.GetDirectoryName(sceneFile) ?? string.Empty;
                string normalized = requestedPath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
                string combined = Path.IsPathRooted(normalized)
                    ? normalized
                    : Path.Combine(baseDir, normalized);
                return Path.GetFullPath(combined);
            }
            catch
            {
                return requestedPath.Replace('\\', '/');
            }
        }
    }
}
