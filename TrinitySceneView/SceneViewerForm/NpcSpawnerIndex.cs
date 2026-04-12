using GFTool.Core.Flatbuffers.TR.Scene;
using GFTool.Core.Flatbuffers.TR.Scene.Components;
using GFTool.Renderer.Core;
using Trinity.Core.Utils;

namespace TrinitySceneView
{
    public partial class SceneViewerForm
    {
        private SceneNpcSpawnerIndex? npcSpawnerIndex;
        private Dictionary<string, string>? npcModelIndexByDirNameLower;

        private sealed record NpcSpawnerTemplate(string Name, string TemplatePath, string? ModelTrmdlPath);

        private sealed class SceneNpcSpawnerIndex
        {
            public string ScenePath { get; }
            public IReadOnlyList<string> SpawnerScenePaths { get; }
            public IReadOnlyList<NpcSpawnerTemplate> Templates { get; }

            public SceneNpcSpawnerIndex(string scenePath, IReadOnlyList<string> spawnerScenePaths, IReadOnlyList<NpcSpawnerTemplate> templates)
            {
                ScenePath = scenePath;
                SpawnerScenePaths = spawnerScenePaths;
                Templates = templates;
            }
        }

        private void RebuildNpcSpawnerIndex(string scenePath)
        {
            npcSpawnerIndex = null;

            if (!scenePath.EndsWith(".trscn", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                var spawnerScenes = new List<string>();
                var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var queue = new Queue<string>();
                visited.Add(scenePath);
                queue.Enqueue(scenePath);

                var maxScenes = 250;
                while (queue.Count > 0 && visited.Count <= maxScenes)
                {
                    var cur = queue.Dequeue();
                    TRSCN curScene;
                    try { curScene = FlatBufferConverter.DeserializeFrom<TRSCN>(cur); }
                    catch { continue; }

                    var curDir = Path.GetDirectoryName(cur) ?? "";
                    foreach (var sub in EnumerateDirectSubScenes(curScene, curDir))
                    {
                        if (string.IsNullOrWhiteSpace(sub) || !File.Exists(sub))
                        {
                            continue;
                        }

                        if (visited.Add(sub))
                        {
                            queue.Enqueue(sub);
                        }

                        if (sub.IndexOf("placement_npc_spawner", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            spawnerScenes.Add(sub);
                        }
                    }
                }

                if (spawnerScenes.Count == 0)
                {
                    MessageHandler.Instance.AddMessage(MessageType.LOG, "[Scene] NPC spawner index: 0 spawner scenes found.");
                    return;
                }

                spawnerScenes = spawnerScenes.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

                var templates = new List<NpcSpawnerTemplate>();
                foreach (var spawnerScene in spawnerScenes)
                {
                    var sp = FlatBufferConverter.DeserializeFrom<TRSCN>(spawnerScene);
                    WalkChunks(sp.Chunks, chunk =>
                    {
                        if (!string.Equals(chunk.Type, "trinity_ObjectTemplate", StringComparison.Ordinal))
                        {
                            return;
                        }

                        trinity_ObjectTemplate? ot;
                        try { ot = FlatBufferConverter.DeserializeFrom<trinity_ObjectTemplate>(chunk.Data); }
                        catch { return; }

                        if (ot == null || string.IsNullOrWhiteSpace(ot.Name))
                        {
                            return;
                        }

                        var absTemplate = ResolveSubScenePath(Path.GetDirectoryName(spawnerScene) ?? "", ot.FilePath);
                        templates.Add(new NpcSpawnerTemplate(
                            Name: ot.Name,
                            TemplatePath: absTemplate ?? ot.FilePath ?? "",
                            ModelTrmdlPath: null));
                    });
                }

                templates = ResolveSpawnerModels(templates);
                npcSpawnerIndex = new SceneNpcSpawnerIndex(scenePath, spawnerScenes, templates);
                MessageHandler.Instance.AddMessage(
                    MessageType.LOG,
                    $"[Scene] NPC spawner index: {templates.Count} template(s) from {spawnerScenes.Count} spawner scene(s).");
            }
            catch (Exception ex)
            {
                MessageHandler.Instance.AddMessage(MessageType.WARNING, $"[Scene] NPC spawner index failed: {ex.GetType().Name}: {ex.Message}");
                npcSpawnerIndex = null;
            }
        }

        private List<NpcSpawnerTemplate> ResolveSpawnerModels(List<NpcSpawnerTemplate> templates)
        {
            if (templates.Count == 0) return templates;
            if (string.IsNullOrWhiteSpace(assetRoot) || !Directory.Exists(assetRoot)) return templates;

            // Prefer deterministic mapping via the same NPC spawner DBs used at runtime:
            // spawnerObjectName -> assetId -> objectTemplateId -> templatePath -> modelComponent.FilePath -> *.trmdl
            var npcDb = new NpcSpawnerDbCache();
            EnsureNpcSpawnerDbLoaded(npcDb);

            if (npcDb.SpawnerObjectNameToAssetIds.Count > 0 &&
                npcDb.AssetIdToObjectTemplateIds.Count > 0 &&
                npcDb.ObjectTemplateIdToPath.Count > 0)
            {
                var templateCache = new Dictionary<string, List<TemplateModelSpawn>>(StringComparer.OrdinalIgnoreCase);
                var templateInProgress = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                var outList = new List<NpcSpawnerTemplate>(templates.Count);
                foreach (var t in templates)
                {
                    var model = ResolveSpawnerModelViaNpcDb(t.Name, npcDb, templateCache, templateInProgress);
                    outList.Add(t with { ModelTrmdlPath = model });
                }

                var resolved = outList.Count(t => !string.IsNullOrWhiteSpace(t.ModelTrmdlPath));
                if (resolved > 0)
                {
                    MessageHandler.Instance.AddMessage(MessageType.LOG, $"[Scene] NPC spawner models resolved (db): {resolved}/{outList.Count}.");
                    foreach (var t in outList)
                    {
                        if (string.IsNullOrWhiteSpace(t.ModelTrmdlPath)) continue;
                        MessageHandler.Instance.AddMessage(MessageType.LOG, $"[Scene] Spawner '{t.Name}' -> {ToAssetRootRelative(t.ModelTrmdlPath)}");
                    }
                    return outList;
                }
            }

            // Fallback: heuristic keyword match against ik_chara model directories.
            EnsureNpcModelIndex();
            if (npcModelIndexByDirNameLower == null || npcModelIndexByDirNameLower.Count == 0) return templates;

            var fallbackList = new List<NpcSpawnerTemplate>(templates.Count);
            foreach (var t in templates)
            {
                var key = ExtractSpawnerKeyword(t.Name);
                var model = string.IsNullOrWhiteSpace(key) ? null : FindBestModelTrmdlForKeyword(key);
                fallbackList.Add(t with { ModelTrmdlPath = model });
            }

            var fallbackResolved = fallbackList.Count(t => !string.IsNullOrWhiteSpace(t.ModelTrmdlPath));
            if (fallbackResolved > 0)
            {
                MessageHandler.Instance.AddMessage(MessageType.LOG, $"[Scene] NPC spawner models resolved (heuristic): {fallbackResolved}/{fallbackList.Count}.");
                foreach (var t in fallbackList)
                {
                    if (string.IsNullOrWhiteSpace(t.ModelTrmdlPath)) continue;
                    MessageHandler.Instance.AddMessage(MessageType.LOG, $"[Scene] Spawner '{t.Name}' -> {ToAssetRootRelative(t.ModelTrmdlPath)}");
                }
            }

            return fallbackList;
        }

        private string? ResolveSpawnerModelViaNpcDb(
            string spawnerName,
            NpcSpawnerDbCache npcDb,
            Dictionary<string, List<TemplateModelSpawn>> templateCache,
            HashSet<string> templateInProgress)
        {
            if (string.IsNullOrWhiteSpace(spawnerName))
            {
                return null;
            }

            var normalized = NormalizeSpawnerId(spawnerName);
            if (!TryGetByVariants(npcDb.SpawnerObjectNameToAssetIds, spawnerName, normalized, out var assetIds) ||
                assetIds == null ||
                assetIds.Count == 0)
            {
                return null;
            }

            foreach (var assetId in assetIds)
            {
                if (string.IsNullOrWhiteSpace(assetId))
                {
                    continue;
                }

                if (!npcDb.AssetIdToObjectTemplateIds.TryGetValue(assetId, out var objectTemplateIds) || objectTemplateIds.Count == 0)
                {
                    continue;
                }

                foreach (var objectTemplateId in objectTemplateIds)
                {
                    if (!npcDb.ObjectTemplateIdToPath.TryGetValue(objectTemplateId, out var templateRel) || string.IsNullOrWhiteSpace(templateRel))
                    {
                        continue;
                    }

                    var templateAbs = ResolveAssetReferenceWithVariants(templateRel);
                    if (templateAbs == null)
                    {
                        continue;
                    }

                    var templateSpawns = GetTemplateSpawns(templateAbs, templateCache, templateInProgress, System.Threading.CancellationToken.None);
                    if (templateSpawns == null || templateSpawns.Count == 0)
                    {
                        continue;
                    }

                    foreach (var s in templateSpawns)
                    {
                        if (string.IsNullOrWhiteSpace(s.ModelPath))
                        {
                            continue;
                        }

                        var modelAbs = ResolveAssetReferenceWithVariants(s.ModelPath);
                        if (modelAbs == null || !modelAbs.EndsWith(".trmdl", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        return modelAbs;
                    }
                }
            }

            return null;
        }

        private void EnsureNpcModelIndex()
        {
            if (npcModelIndexByDirNameLower != null) return;
            if (string.IsNullOrWhiteSpace(assetRoot) || !Directory.Exists(assetRoot))
            {
                npcModelIndexByDirNameLower = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                return;
            }

            var roots = new[]
            {
                Path.Combine(assetRoot, "ik_chara", "model_uq"),
                Path.Combine(assetRoot, "ik_chara", "model_ob"),
                Path.Combine(assetRoot, "ik_chara", "model_cc_ir"),
                Path.Combine(assetRoot, "ik_chara", "model_cc_vr"),
                Path.Combine(assetRoot, "ik_chara", "model_cc_base"),
            };

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in roots)
            {
                if (!Directory.Exists(r)) continue;
                try
                {
                    foreach (var d in Directory.EnumerateDirectories(r))
                    {
                        var name = Path.GetFileName(d);
                        if (string.IsNullOrWhiteSpace(name)) continue;

                        string? trmdl = null;
                        try { trmdl = Directory.EnumerateFiles(d, "*.trmdl", SearchOption.TopDirectoryOnly).FirstOrDefault(); }
                        catch { }

                        if (string.IsNullOrWhiteSpace(trmdl)) continue;
                        map[name.ToLowerInvariant()] = trmdl;
                    }
                }
                catch { }
            }

            npcModelIndexByDirNameLower = map;
        }

        private string? FindBestModelTrmdlForKeyword(string keyword)
        {
            if (npcModelIndexByDirNameLower == null || npcModelIndexByDirNameLower.Count == 0) return null;
            keyword = keyword.Trim().ToLowerInvariant();
            if (keyword.Length == 0) return null;

            string? best = null;
            var bestScore = int.MinValue;
            foreach (var kv in npcModelIndexByDirNameLower)
            {
                var dir = kv.Key;
                if (dir.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) < 0) continue;

                var score = 0;
                if (dir.EndsWith("_" + keyword, StringComparison.OrdinalIgnoreCase)) score += 40;
                if (dir.Contains("_" + keyword + "_", StringComparison.OrdinalIgnoreCase)) score += 20;
                score -= Math.Abs(dir.Length - keyword.Length);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = kv.Value;
                }
            }

            return best;
        }

        private static string? ExtractSpawnerKeyword(string spawnerName)
        {
            if (string.IsNullOrWhiteSpace(spawnerName)) return null;

            var parts = spawnerName.Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            for (var i = parts.Length - 1; i >= 0; i--)
            {
                var p = parts[i];
                if (IsAllDigits(p)) continue;
                if (IsStopToken(p)) continue;
                return p;
            }
            return null;
        }

        private static bool IsStopToken(string token)
        {
            var t = token.ToLowerInvariant();
            if (t is "npc" or "main" or "sub" or "spawner") return true;
            if (t.StartsWith("t", StringComparison.Ordinal) && t.Length >= 2 && IsAllDigits(t.AsSpan(1))) return true;
            if (t.StartsWith("i", StringComparison.Ordinal) && t.Length >= 2 && IsAllDigits(t.AsSpan(1))) return true;
            return false;
        }

        private static bool IsAllDigits(string s) => IsAllDigits(s.AsSpan());

        private static bool IsAllDigits(ReadOnlySpan<char> s)
        {
            if (s.Length == 0) return false;
            for (var i = 0; i < s.Length; i++)
            {
                if (!char.IsDigit(s[i])) return false;
            }
            return true;
        }

        private string ToAssetRootRelative(string abs)
        {
            if (string.IsNullOrWhiteSpace(assetRoot)) return abs;
            try
            {
                var rel = Path.GetRelativePath(assetRoot, abs);
                return rel.Replace('\\', '/');
            }
            catch
            {
                return abs;
            }
        }

        private static IEnumerable<string> EnumerateDirectSubScenes(TRSCN scene, string sceneDir)
        {
            foreach (var c in scene.Chunks)
            {
                foreach (var p in EnumerateDirectSubScenes(c, sceneDir))
                {
                    yield return p;
                }
            }

            if (scene.SubScenes != null)
            {
                foreach (var rel in scene.SubScenes)
                {
                    var abs = ResolveSubScenePath(sceneDir, rel);
                    if (!string.IsNullOrWhiteSpace(abs))
                    {
                        yield return abs!;
                    }
                }
            }
        }

        private static IEnumerable<string> EnumerateDirectSubScenes(SceneChunk chunk, string sceneDir)
        {
            if (string.Equals(chunk.Type, "SubScene", StringComparison.Ordinal))
            {
                SubScene? sub = null;
                try { sub = FlatBufferConverter.DeserializeFrom<SubScene>(chunk.Data); }
                catch { }

                if (!string.IsNullOrWhiteSpace(sub?.Filepath))
                {
                    var abs = ResolveSubScenePath(sceneDir, sub.Filepath);
                    if (!string.IsNullOrWhiteSpace(abs))
                    {
                        yield return abs!;
                    }
                }
            }

            foreach (var child in chunk.Children)
            {
                foreach (var p in EnumerateDirectSubScenes(child, sceneDir))
                {
                    yield return p;
                }
            }
        }

        private static string? ResolveSubScenePath(string baseDir, string? rel)
        {
            if (string.IsNullOrWhiteSpace(baseDir) || string.IsNullOrWhiteSpace(rel))
            {
                return null;
            }

            try
            {
                var abs = Path.Combine(baseDir, rel);
                return abs.Replace(".trs", "_0.trs", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return null;
            }
        }

        private static void WalkChunks(IList<SceneChunk> chunks, Action<SceneChunk> onChunk)
        {
            foreach (var c in chunks)
            {
                onChunk(c);
                if (c.Children != null && c.Children.Length > 0)
                {
                    WalkChunks(c.Children, onChunk);
                }
            }
        }
    }
}
