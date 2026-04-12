using GFTool.Renderer.Core;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Trinity.Core.Flatbuffers.Reflections;
using Trinity.Core.Utils;

namespace TrinitySceneView
{
    public partial class SceneViewerForm
    {
        private void TryCollectNpcSpawnersForSceneFile(
            string sceneFile,
            Matrix4 parentMatrix,
            NpcSpawnerDbCache npcDb,
            Dictionary<string, List<TemplateModelSpawn>> templateCache,
            HashSet<string> templateInProgress,
            List<SceneModelSpawn> spawns,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            if (npcDb == null || string.IsNullOrWhiteSpace(sceneFile))
            {
                return;
            }

            EnsureNpcSpawnerDbLoaded(npcDb);
            if (npcDb.CreateScenePathToSpawnerObjectNames.Count == 0 || npcDb.SpawnerObjectNameToAssetIds.Count == 0)
            {
                return;
            }

            var rel = TryMakeAssetRelativePath(sceneFile);
            if (rel == null)
            {
                return;
            }

            var keyVariants = GetScenePathVariants(rel);
            foreach (var key in keyVariants)
            {
                token.ThrowIfCancellationRequested();

                if (!npcDb.CreateScenePathToSpawnerObjectNames.TryGetValue(key, out var spawnerNames) || spawnerNames.Count == 0)
                {
                    continue;
                }

                MessageHandler.Instance.AddMessage(
                    MessageType.LOG,
                    $"[Scene] createScenePath match: {key} -> {spawnerNames.Count} spawners");

                foreach (var spawnerName in spawnerNames)
                {
                    token.ThrowIfCancellationRequested();

                    var normalized = NormalizeSpawnerId(spawnerName);
                    if (!npcDb.SpawnedSpawnerObjectNames.Add(normalized))
                    {
                        continue;
                    }

                    TryCollectNpcSpawnerSpawns(
                        sceneFile,
                        parentMatrix,
                        normalized,
                        parentMatrix * Matrix4.Identity,
                        npcDb,
                        templateCache,
                        templateInProgress,
                        spawns,
                        token);
                }

                // Only use the first matching createScenePath bucket.
                return;
            }
        }

        private void TryCollectNpcSpawnerSpawns(
            string sceneFile,
            Matrix4 parentMatrix,
            string? sceneObjectName,
            Matrix4 sceneObjectMatrix,
            NpcSpawnerDbCache npcDb,
            Dictionary<string, List<TemplateModelSpawn>> templateCache,
            HashSet<string> templateInProgress,
            List<SceneModelSpawn> spawns,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            if (npcDb == null || string.IsNullOrWhiteSpace(sceneObjectName))
            {
                return;
            }

            EnsureNpcSpawnerDbLoaded(npcDb);

            if (npcDb.SpawnerObjectNameToAssetIds.Count == 0 ||
                npcDb.AssetIdToObjectTemplateIds.Count == 0 ||
                npcDb.ObjectTemplateIdToPath.Count == 0)
            {
                return;
            }

            var normalizedName = NormalizeSpawnerId(sceneObjectName);
            if (!TryGetByVariants(npcDb.SpawnerObjectNameToAssetIds, sceneObjectName, normalizedName, out var assetIds) ||
                assetIds == null ||
                assetIds.Count == 0)
            {
                return;
            }

            string? templateAbs = null;
            string? chosenAssetId = null;
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

                    var abs = ResolveAssetReferenceWithVariants(templateRel);
                    if (abs == null)
                    {
                        continue;
                    }

                    templateAbs = abs;
                    chosenAssetId = assetId;
                    break;
                }

                if (templateAbs != null)
                {
                    break;
                }
            }

            if (templateAbs == null)
            {
                return;
            }

            if (assetIds.Count > 1)
            {
                MessageHandler.Instance.AddMessage(
                    MessageType.LOG,
                    $"[Scene] NPC spawner assetId choices: {sceneObjectName} -> {string.Join(" | ", assetIds)}");
            }

            MessageHandler.Instance.AddMessage(
                MessageType.LOG,
                $"[Scene] NPC spawner resolved: {sceneObjectName} -> assetId={chosenAssetId} (choices={assetIds.Count})");

            return;
        }

        private static bool TryGetByVariants<T>(Dictionary<string, T> dict, string key, string normalized, [MaybeNullWhen(false)] out T value)
        {
            if (dict.TryGetValue(key, out value))
            {
                return true;
            }

            if (!string.Equals(key, normalized, StringComparison.Ordinal) && dict.TryGetValue(normalized, out value))
            {
                return true;
            }

            var withId = "id_" + key;
            if (dict.TryGetValue(withId, out value))
            {
                return true;
            }

            var withIdNorm = "id_" + normalized;
            if (dict.TryGetValue(withIdNorm, out value))
            {
                return true;
            }

            value = default!;
            return false;
        }

        private string? ResolveAssetReferenceWithVariants(string referencedPath)
        {
            if (string.IsNullOrWhiteSpace(referencedPath))
            {
                return null;
            }

            var direct = ResolveModelPath(referencedPath);
            if (direct != null)
            {
                return direct;
            }

            string normalized = referencedPath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            string candidate = Path.GetFullPath(Path.Combine(assetRoot ?? "", normalized));
            string ext = Path.GetExtension(candidate);
            if (string.IsNullOrWhiteSpace(ext))
            {
                return null;
            }

            string fileNameNoExt = Path.GetFileNameWithoutExtension(candidate);
            if (fileNameNoExt.EndsWith("_0", StringComparison.OrdinalIgnoreCase) ||
                fileNameNoExt.EndsWith("_1", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            string noExt = candidate.Substring(0, candidate.Length - ext.Length);
            foreach (int variant in new[] { 0, 1 })
            {
                string withVariant = $"{noExt}_{variant}{ext}";
                if (File.Exists(withVariant))
                {
                    return withVariant;
                }
            }

            return null;
        }

        private void EnsureNpcSpawnerDbLoaded(NpcSpawnerDbCache cache)
        {
            if (cache.Loaded)
            {
                return;
            }

            cache.Loaded = true;

            try
            {
                LoadNpcSpawnerObjectNameMap(cache);
                LoadSpawnerTransforms(cache);
                LoadNpcAssetData(cache);
                LoadNpcObjectTemplateData(cache);

                MessageHandler.Instance.AddMessage(
                    MessageType.LOG,
                    $"[Scene] NPC DB loaded: spawners={cache.SpawnerObjectNameToAssetIds.Count} transforms={cache.SpawnerTransforms.Count} assets={cache.AssetIdToObjectTemplateIds.Count} templates={cache.ObjectTemplateIdToPath.Count}");
            }
            catch (Exception ex)
            {
                MessageHandler.Instance.AddMessage(MessageType.WARNING, $"[Scene] Failed loading NPC spawner DBs: {ex.Message}");
            }
        }

        private void LoadNpcSpawnerObjectNameMap(NpcSpawnerDbCache cache)
        {
            LoadNpcSpawnerObjectNameMapInto(
                cache,
                bfbsRel: "world/ik_data/field/npc_spawner/placement_npc_spawner_data/placement_npc_spawner_data/placement_npc_spawner_data_array.bfbs",
                binRel: "world/ik_data/field/npc_spawner/placement_npc_spawner_data/placement_npc_spawner_data/placement_npc_spawner_data_array.bin");

            LoadNpcSpawnerObjectNameMapInto(
                cache,
                bfbsRel: "world/ik_data/field/npc_spawner/traffic_npc_spawner_data/traffic_npc_spawner_data/traffic_npc_spawner_data_array.bfbs",
                binRel: "world/ik_data/field/npc_spawner/traffic_npc_spawner_data/traffic_npc_spawner_data/traffic_npc_spawner_data_array.bin");
        }

        private void LoadNpcSpawnerObjectNameMapInto(NpcSpawnerDbCache cache, string bfbsRel, string binRel)
        {
            var bfbsAbs = ResolveModelPath(bfbsRel);
            var binAbs = ResolveModelPath(binRel);
            if (bfbsAbs == null || binAbs == null)
            {
                return;
            }

            var sourceDb = bfbsRel.Replace('\\', '/').Contains("/traffic_", StringComparison.OrdinalIgnoreCase)
                ? "traffic"
                : "placement";

            using var doc = LoadReflectionDoc(bfbsAbs, binAbs);
            if (!doc.RootElement.TryGetProperty("values", out var values) || values.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var group in values.EnumerateArray())
            {
                if (!group.TryGetProperty("root", out var root) || root.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var el in root.EnumerateArray())
                {
                    var assetId = el.TryGetProperty("assetId", out var a) ? ReadStringOrNumber(a) : "";
                    if (assetId.Length == 0)
                    {
                        continue;
                    }

                    if (!el.TryGetProperty("appearingConditionList", out var acl) || acl.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    foreach (var cond in acl.EnumerateArray())
                    {
                        var appearanceId =
                            cond.TryGetProperty("id", out var idEl) ? ReadStringOrNumber(idEl) :
                            cond.TryGetProperty("npcId", out var npcIdEl) ? ReadStringOrNumber(npcIdEl) :
                            null;

                        var encountId =
                            cond.TryGetProperty("encountId", out var encEl) ? ReadStringOrNumber(encEl) :
                            cond.TryGetProperty("encountDataId", out var enc2El) ? ReadStringOrNumber(enc2El) :
                            null;

                        int activationCount = 0;
                        string? activationJson = null;
                        if (cond.TryGetProperty("activationConditionList", out var act))
                        {
                            if (act.ValueKind == JsonValueKind.Array)
                            {
                                activationCount = act.GetArrayLength();
                            }

                            // Keep as raw JSON for now; evaluating conditions requires game state.
                            activationJson = act.ValueKind == JsonValueKind.Undefined ? null : act.GetRawText();
                        }

                        if (!cond.TryGetProperty("spawnerObjectInfoList", out var so) || so.ValueKind != JsonValueKind.Array)
                        {
                            continue;
                        }

                        foreach (var oi in so.EnumerateArray())
                        {
                            var objectName = oi.TryGetProperty("objectName", out var on) ? ReadStringOrNumber(on) : "";
                            if (objectName.Length == 0)
                            {
                                continue;
                            }

                            var priority =
                                oi.TryGetProperty("priority", out var priEl) ? ReadIntOrNull(priEl) :
                                oi.TryGetProperty("Priority", out var pri2El) ? ReadIntOrNull(pri2El) :
                                null;

                            objectName = NormalizeSpawnerId(objectName);
                            if (!cache.SpawnerObjectNameToAssetIds.TryGetValue(objectName, out var assetList))
                            {
                                assetList = new List<string>();
                                cache.SpawnerObjectNameToAssetIds[objectName] = assetList;
                            }

                            if (!assetList.Contains(assetId, StringComparer.Ordinal))
                            {
                                assetList.Add(assetId);
                            }

                            var createScenePath = oi.TryGetProperty("createScenePath", out var csp) ? ReadStringOrNumber(csp) : "";
                            if (!string.IsNullOrWhiteSpace(createScenePath))
                            {
                                createScenePath = createScenePath.Replace('\\', '/');
                                if (!cache.CreateScenePathToSpawnerObjectNames.TryGetValue(createScenePath, out var spawnerList))
                                {
                                    spawnerList = new List<string>();
                                    cache.CreateScenePathToSpawnerObjectNames[createScenePath] = spawnerList;
                                }
                                if (!spawnerList.Contains(objectName, StringComparer.Ordinal))
                                {
                                    spawnerList.Add(objectName);
                                }
                            }

                            if (!cache.SpawnerObjectNameToCandidates.TryGetValue(objectName, out var candidates))
                            {
                                candidates = new List<NpcSpawnerCandidate>();
                                cache.SpawnerObjectNameToCandidates[objectName] = candidates;
                            }

                            candidates.Add(new NpcSpawnerCandidate(
                                SourceDb: sourceDb,
                                AssetId: assetId,
                                AppearanceId: string.IsNullOrWhiteSpace(appearanceId) ? null : appearanceId,
                                EncountId: string.IsNullOrWhiteSpace(encountId) ? null : encountId,
                                Priority: priority,
                                ActivationConditionsCount: activationCount,
                                ActivationConditionsJson: activationJson,
                                CreateScenePath: string.IsNullOrWhiteSpace(createScenePath) ? null : createScenePath
                            ));
                        }
                    }
                }
            }
        }

        private void LoadSpawnerTransforms(NpcSpawnerDbCache cache)
        {
            LoadSpawnerTransformsInto(
                cache,
                bfbsRel: "world/ik_data/field/spawner_transform_data/placement_npc_spawner_transform/placement_npc_spawner_transform/placement_npc_spawner_transform_array.bfbs",
                binRel: "world/ik_data/field/spawner_transform_data/placement_npc_spawner_transform/placement_npc_spawner_transform/placement_npc_spawner_transform_array.bin");

            LoadSpawnerTransformsInto(
                cache,
                bfbsRel: "world/ik_data/field/spawner_transform_data/traffic_npc_spawner_transform/traffic_npc_spawner_transform/traffic_npc_spawner_transform_array.bfbs",
                binRel: "world/ik_data/field/spawner_transform_data/traffic_npc_spawner_transform/traffic_npc_spawner_transform/traffic_npc_spawner_transform_array.bin");
        }

        private void LoadSpawnerTransformsInto(NpcSpawnerDbCache cache, string bfbsRel, string binRel)
        {
            var bfbsAbs = ResolveModelPath(bfbsRel);
            var binAbs = ResolveModelPath(binRel);
            if (bfbsAbs == null || binAbs == null)
            {
                return;
            }

            using var doc = LoadReflectionDoc(bfbsAbs, binAbs);
            if (!doc.RootElement.TryGetProperty("values", out var values) || values.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var group in values.EnumerateArray())
            {
                if (!group.TryGetProperty("root", out var root) || root.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var el in root.EnumerateArray())
                {
                    var name = el.TryGetProperty("Name", out var nameEl) ? (nameEl.GetString() ?? "") : "";
                    if (name.Length == 0)
                    {
                        continue;
                    }

                    name = NormalizeSpawnerId(name);

                    static float ReadVec3(JsonElement t, string key)
                        => t.TryGetProperty(key, out var v) ? (float)v.GetDouble() : 0.0f;

                    var pos = el.TryGetProperty("Position", out var p) ? p : default;
                    var rot = el.TryGetProperty("Rotation", out var r) ? r : default;

                    var x = pos.ValueKind == JsonValueKind.Object ? ReadVec3(pos, "X") : 0.0f;
                    var y = pos.ValueKind == JsonValueKind.Object ? ReadVec3(pos, "Y") : 0.0f;
                    var z = pos.ValueKind == JsonValueKind.Object ? ReadVec3(pos, "Z") : 0.0f;

                    var rx = rot.ValueKind == JsonValueKind.Object ? ReadVec3(rot, "X") : 0.0f;
                    var ry = rot.ValueKind == JsonValueKind.Object ? ReadVec3(rot, "Y") : 0.0f;
                    var rz = rot.ValueKind == JsonValueKind.Object ? ReadVec3(rot, "Z") : 0.0f;

                    var attach = el.TryGetProperty("AttachTransformEnable", out var a) && a.ValueKind == JsonValueKind.True;

                    cache.SpawnerTransforms[name] = new SpawnerTransform(name, x, y, z, rx, ry, rz, attach);
                }
            }
        }

        private void LoadNpcAssetData(NpcSpawnerDbCache cache)
        {
            var bfbsAbs = ResolveModelPath("world/ik_data/field/npc/npc_asset_data/npc_asset_data/npc_asset_data.bfbs");
            var binAbs = ResolveModelPath("world/ik_data/field/npc/npc_asset_data/npc_asset_data/npc_asset_data.bin");
            if (bfbsAbs == null || binAbs == null)
            {
                return;
            }

            using var doc = LoadReflectionDoc(bfbsAbs, binAbs);

            JsonElement root;
            if (doc.RootElement.TryGetProperty("root", out var rootEl))
            {
                root = rootEl;
            }
            else
            {
                root = doc.RootElement;
            }

            if (root.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var row in root.EnumerateArray())
            {
                var assetId = row.TryGetProperty("assetId", out var a) ? ReadStringOrNumber(a) : "";
                if (assetId.Length == 0)
                {
                    continue;
                }

                if (!row.TryGetProperty("npcInfoList", out var infos) || infos.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                var ids = new List<string>();
                foreach (var info in infos.EnumerateArray())
                {
                    var id = info.TryGetProperty("objectTemplateId", out var ot) ? ReadStringOrNumber(ot) : "";
                    if (id.Length > 0)
                    {
                        ids.Add(id);
                    }
                }

                ids = ids.Distinct(StringComparer.Ordinal).ToList();
                if (ids.Count == 0)
                {
                    continue;
                }

                cache.AssetIdToObjectTemplateIds[assetId] = ids;
            }
        }

        private void LoadNpcObjectTemplateData(NpcSpawnerDbCache cache)
        {
            var bfbsAbs = ResolveModelPath("world/ik_data/field/npc/npc_object_template_data/npc_object_template_data/npc_object_template_data.bfbs");
            var binAbs = ResolveModelPath("world/ik_data/field/npc/npc_object_template_data/npc_object_template_data/npc_object_template_data.bin");
            if (bfbsAbs == null || binAbs == null)
            {
                return;
            }

            using var doc = LoadReflectionDoc(bfbsAbs, binAbs);
            if (!doc.RootElement.TryGetProperty("root", out var root) || root.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var row in root.EnumerateArray())
            {
                var id = row.TryGetProperty("id", out var idEl) ? ReadStringOrNumber(idEl) : "";
                var path = row.TryGetProperty("path", out var p) ? ReadStringOrNumber(p) : "";
                if (id.Length == 0 || path.Length == 0)
                {
                    continue;
                }

                if (!cache.ObjectTemplateIdToPath.ContainsKey(id))
                {
                    cache.ObjectTemplateIdToPath[id] = path;
                }
            }
        }

        private static JsonDocument LoadReflectionDoc(string bfbsAbs, string binAbs)
        {
            byte[] bfbsBytes = File.ReadAllBytes(bfbsAbs);
            byte[] binBytes = File.ReadAllBytes(binAbs);
            var reflectionSchema = FlatBufferConverter.DeserializeFrom<ReflectionSchema>(bfbsBytes);
            var ctx = ReflectionSchemaContext.Create(reflectionSchema);
            var json = FlatbufferReflectionJsonDumper.Dump(binBytes, ctx);
            return JsonDocument.Parse(json);
        }

        private static string ReadStringOrNumber(JsonElement el)
        {
            switch (el.ValueKind)
            {
                case JsonValueKind.String:
                    return el.GetString() ?? "";
                case JsonValueKind.Number:
                    // Use integer form when possible (ids are typically uint-ish).
                    if (el.TryGetInt64(out long i64))
                    {
                        return i64.ToString();
                    }
                    if (el.TryGetUInt64(out ulong u64))
                    {
                        return u64.ToString();
                    }
                    return el.GetDouble().ToString(System.Globalization.CultureInfo.InvariantCulture);
                default:
                    return "";
            }
        }

        private static int? ReadIntOrNull(JsonElement el)
        {
            switch (el.ValueKind)
            {
                case JsonValueKind.Number:
                    if (el.TryGetInt32(out int i32))
                    {
                        return i32;
                    }
                    if (el.TryGetInt64(out long i64))
                    {
                        if (i64 > int.MaxValue) return int.MaxValue;
                        if (i64 < int.MinValue) return int.MinValue;
                        return (int)i64;
                    }
                    if (el.TryGetDouble(out double d))
                    {
                        return (int)d;
                    }
                    return null;
                case JsonValueKind.String:
                    {
                        var s = el.GetString();
                        if (int.TryParse(s, out int parsed))
                        {
                            return parsed;
                        }
                        return null;
                    }
                default:
                    return null;
            }
        }

        private string? TryMakeAssetRelativePath(string absolutePath)
        {
            if (string.IsNullOrWhiteSpace(assetRoot) || string.IsNullOrWhiteSpace(absolutePath))
            {
                return null;
            }

            try
            {
                var fullRoot = Path.GetFullPath(assetRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var fullPath = Path.GetFullPath(absolutePath);
                if (fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
                {
                    var rel = fullPath.Substring(fullRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    return rel.Replace('\\', '/');
                }
            }
            catch
            {
                // ignore
            }

            return null;
        }

        private static IEnumerable<string> GetScenePathVariants(string relPath)
        {
            relPath = relPath.Replace('\\', '/');
            yield return relPath;

            var file = Path.GetFileName(relPath);
            var dir = Path.GetDirectoryName(relPath)?.Replace('\\', '/') ?? "";

            var noExt = Path.GetFileNameWithoutExtension(file);
            var ext = Path.GetExtension(file);
            var trimmedNoExt = noExt;
            if (noExt.EndsWith("_0", StringComparison.OrdinalIgnoreCase) || noExt.EndsWith("_1", StringComparison.OrdinalIgnoreCase))
            {
                trimmedNoExt = noExt.Substring(0, noExt.Length - 2);
                yield return string.IsNullOrEmpty(dir) ? (trimmedNoExt + ext) : (dir + "/" + trimmedNoExt + ext);
            }

            // Some DBs use createScenePath as a scene key (no directory, no extension), e.g. "t4_sp04".
            // Provide a few deterministic variants derived from the file path.
            if (!string.IsNullOrWhiteSpace(trimmedNoExt))
            {
                yield return trimmedNoExt;
                yield return trimmedNoExt.TrimEnd('_');
            }

            if (!string.IsNullOrWhiteSpace(dir))
            {
                var dirName = Path.GetFileName(dir.TrimEnd('/'));
                if (!string.IsNullOrWhiteSpace(dirName))
                {
                    yield return dirName;
                    yield return dirName.TrimEnd('_');
                }
            }
        }
    }
}
