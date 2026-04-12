using GFTool.Core.Flatbuffers.TR.Scene;
using GFTool.Core.Flatbuffers.TR.Scene.Components;
using GFTool.Renderer.Core;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Trinity.Core.Utils;

namespace TrinitySceneView
{
    public partial class SceneViewerForm
    {
        private SceneModelSpawnCollectionResult CollectSceneModelSpawns(string sceneFile, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            TRSCN trscn = FlatBufferConverter.DeserializeFrom<TRSCN>(sceneFile);
            var loadedScenes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var templateCache = new Dictionary<string, List<TemplateModelSpawn>>(StringComparer.OrdinalIgnoreCase);
            var templateInProgress = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var ccdatamCache = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            var ccdataCache = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            var ccModelsCache = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            var npcDb = new NpcSpawnerDbCache();
            var placementRegistryLogged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var spawns = new List<SceneModelSpawn>();
            CollectSceneModelSpawnsRecursive(
                sceneFile,
                trscn,
                Matrix4.Identity,
                loadedScenes,
                templateCache,
                templateInProgress,
                ccdatamCache,
                ccdataCache,
                ccModelsCache,
                npcDb,
                placementRegistryLogged,
                spawns,
                token);
            return new SceneModelSpawnCollectionResult
            {
                Spawns = spawns,
                NpcDb = npcDb
            };
        }

        private void CollectSceneModelSpawnsRecursive(
            string sceneFile,
            TRSCN trscn,
            Matrix4 parentMatrix,
            HashSet<string> loadedScenes,
            Dictionary<string, List<TemplateModelSpawn>> templateCache,
            HashSet<string> templateInProgress,
            Dictionary<string, object?> ccdatamCache,
            Dictionary<string, object?> ccdataCache,
            Dictionary<string, List<string>> ccModelsCache,
            NpcSpawnerDbCache npcDb,
            HashSet<string> placementRegistryLogged,
            List<SceneModelSpawn> spawns,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            if (!loadedScenes.Add(sceneFile))
            {
                return;
            }

            // Some scenes don't contain explicit spawner objects; spawn those via createScenePath linkage.
            TryCollectNpcSpawnersForSceneFile(sceneFile, parentMatrix, npcDb, templateCache, templateInProgress, spawns, token);

            // Some scenes contain spawners directly in the trscn (PlacementRegistry -> PlacementSpawnerArray).
            TryCollectPlacementSpawnerSpawnsForSceneFile(sceneFile, trscn, parentMatrix, npcDb, templateCache, templateInProgress, spawns, token);

            if (trscn.Chunks == null)
            {
                return;
            }

            foreach (var chunk in trscn.Chunks)
            {
                CollectSceneModelSpawnsFromChunk(
                    sceneFile,
                    chunk,
                    parentMatrix,
                    loadedScenes,
                    templateCache,
                    templateInProgress,
                    ccdatamCache,
                    ccdataCache,
                    ccModelsCache,
                    npcDb,
                    placementRegistryLogged,
                    spawns,
                    token);
            }
        }

        private void CollectSceneModelSpawnsFromChunk(
            string sceneFile,
            SceneChunk chunk,
            Matrix4 parentMatrix,
            HashSet<string> loadedScenes,
            Dictionary<string, List<TemplateModelSpawn>> templateCache,
            HashSet<string> templateInProgress,
            Dictionary<string, object?> ccdatamCache,
            Dictionary<string, object?> ccdataCache,
            Dictionary<string, List<string>> ccModelsCache,
            NpcSpawnerDbCache npcDb,
            HashSet<string> placementRegistryLogged,
            List<SceneModelSpawn> spawns,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            if (chunk == null || string.IsNullOrWhiteSpace(chunk.Type))
            {
                return;
            }

            if (chunk.Type == nameof(trinity_PlacementRegistry) && chunk.Data != null)
            {
                try
                {
                    var pr = FlatBufferConverter.DeserializeFrom<trinity_PlacementRegistry>(chunk.Data);
                    var key = $"{sceneFile}:{pr.Entry.Discriminator}";
                    if (placementRegistryLogged.Add(key))
                    {
                        pr.Entry.Switch(
                            defaultCase: () => MessageHandler.Instance.AddMessage(MessageType.LOG, $"[Scene] PlacementRegistry: (unknown/empty) scene={Path.GetFileName(sceneFile)}"),
                            case1: (PlacementObjectArray ol) => MessageHandler.Instance.AddMessage(MessageType.LOG, $"[Scene] PlacementRegistry: objects={ol.Table?.Count ?? 0} scene={Path.GetFileName(sceneFile)}"),
                            case2: (PlacementObjectTemplateArray tl) =>
                            {
                                MessageHandler.Instance.AddMessage(MessageType.LOG, $"[Scene] PlacementRegistry: templates={tl.Table?.Count ?? 0} scene={Path.GetFileName(sceneFile)}");

                                if (tl.Table == null || tl.Table.Count == 0)
                                {
                                    return;
                                }

                                int shown = 0;
                                foreach (var t in tl.Table)
                                {
                                    if (t == null) continue;
                                    if (++shown > 20) break;

                                    var resolved = TryResolvePlacementTemplatePath(sceneFile, t.Path);
                                    if (resolved == null)
                                    {
                                        MessageHandler.Instance.AddMessage(MessageType.LOG, $"[Scene] Placement template: {t.Name} -> {t.Path} (missing)");
                                        continue;
                                    }

                                    List<TemplateModelSpawn> templateSpawns;
                                    try
                                    {
                                        templateSpawns = GetTemplateSpawns(
                                            resolved,
                                            templateCache,
                                            templateInProgress,
                                            token);
                                    }
                                    catch
                                    {
                                        templateSpawns = new List<TemplateModelSpawn>();
                                    }

                                    MessageHandler.Instance.AddMessage(
                                        MessageType.LOG,
                                        $"[Scene] Placement template: {t.Name} -> {t.Path} ({Path.GetFileName(resolved)}) models={templateSpawns.Count}");
                                }
                            },
                            case3: (PlacementPositionArray pl) => MessageHandler.Instance.AddMessage(MessageType.LOG, $"[Scene] PlacementRegistry: positions={pl.Table?.Count ?? 0} scene={Path.GetFileName(sceneFile)}"),
                            case4: (PlacementSpawnerArray sl) => MessageHandler.Instance.AddMessage(MessageType.LOG, $"[Scene] PlacementRegistry: spawners={sl.Table?.Count ?? 0} scene={Path.GetFileName(sceneFile)}"));
                    }
                }
                catch
                {
                    // ignore
                }
            }

            if (chunk.Type == nameof(SubScene))
            {
                TryLoadSubScene(
                    sceneFile,
                    chunk,
                    parentMatrix,
                    loadedScenes,
                    templateCache,
                    templateInProgress,
                    ccdatamCache,
                    ccdataCache,
                    ccModelsCache,
                    npcDb,
                    placementRegistryLogged,
                    spawns,
                    token);
            }
            else if (chunk.Type == nameof(trinity_ObjectTemplate))
            {
                // Some spawner scenes store ObjectTemplates at the root (not under a SceneObject).
                // Handle those deterministically so NPC spawners can resolve/spawn.
                TryCollectNpcSpawnerFromObjectTemplate(
                    sceneFile,
                    parentMatrix,
                    parentMatrix * Matrix4.Identity,
                    chunk,
                    npcDb,
                    templateCache,
                    templateInProgress,
                    spawns,
                    token);
            }
            else if (chunk.Type == nameof(trinity_SceneObject))
            {
                TryCollectSceneObjectSpawns(
                    sceneFile,
                    chunk,
                    parentMatrix,
                    templateCache,
                    templateInProgress,
                    ccdatamCache,
                    ccdataCache,
                    ccModelsCache,
                    npcDb,
                    placementRegistryLogged,
                    spawns,
                    token);
            }

            if (chunk.Children != null)
            {
                foreach (var child in chunk.Children)
                {
                    CollectSceneModelSpawnsFromChunk(
                        sceneFile,
                        child,
                        parentMatrix,
                        loadedScenes,
                        templateCache,
                        templateInProgress,
                        ccdatamCache,
                        ccdataCache,
                        ccModelsCache,
                        npcDb,
                        placementRegistryLogged,
                        spawns,
                        token);
                }
            }
        }

        private void TryLoadSubScene(
            string sceneFile,
            SceneChunk chunk,
            Matrix4 parentMatrix,
            HashSet<string> loadedScenes,
            Dictionary<string, List<TemplateModelSpawn>> templateCache,
            HashSet<string> templateInProgress,
            Dictionary<string, object?> ccdatamCache,
            Dictionary<string, object?> ccdataCache,
            Dictionary<string, List<string>> ccModelsCache,
            NpcSpawnerDbCache npcDb,
            HashSet<string> placementRegistryLogged,
            List<SceneModelSpawn> spawns,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            try
            {
                var sub = FlatBufferConverter.DeserializeFrom<SubScene>(chunk.Data);
                if (string.IsNullOrWhiteSpace(sub.Filepath))
                {
                    return;
                }

                var resolved = ResolveSceneReference(sceneFile, sub.Filepath);
                if (resolved == null)
                {
                    return;
                }

                var subScn = FlatBufferConverter.DeserializeFrom<TRSCN>(resolved);

                // Evidence-first: when investigating missing NPC spawns, log what the spawner subscene actually contains.
                if (resolved.Replace('\\', '/').Contains("placement_npc_spawner", StringComparison.OrdinalIgnoreCase))
                {
                    var key = $"npcSpawnerSceneSummary:{resolved}";
                    if (placementRegistryLogged.Add(key))
                    {
                        TryLogNpcSpawnerSceneSummary(resolved, subScn);
                    }
                }

                CollectSceneModelSpawnsRecursive(
                    resolved,
                    subScn,
                    parentMatrix,
                    loadedScenes,
                    templateCache,
                    templateInProgress,
                    ccdatamCache,
                    ccdataCache,
                    ccModelsCache,
                    npcDb,
                    placementRegistryLogged,
                    spawns,
                    token);
            }
            catch
            {
                // ignore
            }
        }

        private void TryCollectSceneObjectSpawns(
            string sceneFile,
            SceneChunk chunk,
            Matrix4 parentMatrix,
            Dictionary<string, List<TemplateModelSpawn>> templateCache,
            HashSet<string> templateInProgress,
            Dictionary<string, object?> ccdatamCache,
            Dictionary<string, object?> ccdataCache,
            Dictionary<string, List<string>> ccModelsCache,
            NpcSpawnerDbCache npcDb,
            HashSet<string> placementRegistryLogged,
            List<SceneModelSpawn> spawns,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            trinity_SceneObject? sceneObject;
            try
            {
                sceneObject = FlatBufferConverter.DeserializeFrom<trinity_SceneObject>(chunk.Data);
            }
            catch
            {
                return;
            }

            if (sceneObject == null || chunk.Children == null || chunk.Children.Length == 0)
            {
                return;
            }

            var sceneObjectMatrix = parentMatrix * BuildSrtMatrix(sceneObject.Srt);

            TryCollectNpcSpawnerSpawns(
                sceneFile,
                parentMatrix,
                sceneObject.Name,
                sceneObjectMatrix,
                npcDb,
                templateCache,
                templateInProgress,
                spawns,
                token);

            foreach (var child in chunk.Children)
            {
                if (child?.Type != nameof(trinity_ModelComponent))
                {
                    if (child?.Type == nameof(trinity_ObjectTemplate))
                    {
                        TryCollectNpcSpawnerFromObjectTemplate(
                            sceneFile,
                            parentMatrix,
                            sceneObjectMatrix,
                            child,
                            npcDb,
                            templateCache,
                            templateInProgress,
                            spawns,
                            token);

                        TryCollectObjectTemplateSpawns(sceneFile, sceneObject.Name, sceneObjectMatrix, child, templateCache, templateInProgress, spawns, token);
                    }
                    else if (child?.Type == nameof(trinity_CharacterCreationMasterComponent))
                    {
                        TryCollectCharacterCreationSpawns(
                            sceneFile,
                            sceneObject.Name,
                            sceneObjectMatrix,
                            child,
                            ccdatamCache,
                            ccdataCache,
                            ccModelsCache,
                            spawns,
                            token);
                    }
                    continue;
                }

                trinity_ModelComponent? modelComponent;
                try
                {
                    modelComponent = FlatBufferConverter.DeserializeFrom<trinity_ModelComponent>(child.Data);
                }
                catch
                {
                    continue;
                }

                if (modelComponent == null || string.IsNullOrWhiteSpace(modelComponent.FilePath))
                {
                    continue;
                }

                spawns.Add(new SceneModelSpawn
                {
                    SceneFile = sceneFile,
                    SceneObjectName = sceneObject.Name,
                    ModelPath = modelComponent.FilePath,
                    ModelMatrix = sceneObjectMatrix
                });
            }
        }

        private void TryCollectNpcSpawnerFromObjectTemplate(
            string sceneFile,
            Matrix4 parentMatrix,
            Matrix4 sceneObjectMatrix,
            SceneChunk templateChunk,
            NpcSpawnerDbCache npcDb,
            Dictionary<string, List<TemplateModelSpawn>> templateCache,
            HashSet<string> templateInProgress,
            List<SceneModelSpawn> spawns,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            if (templateChunk?.Data == null || npcDb == null)
            {
                return;
            }

            trinity_ObjectTemplate? ot;
            try
            {
                ot = FlatBufferConverter.DeserializeFrom<trinity_ObjectTemplate>(templateChunk.Data);
            }
            catch
            {
                return;
            }

            if (ot == null)
            {
                return;
            }

            // In some scenes, the spawner id shows up in `Scope` instead of `Name`.
            // Try both (deterministically), and de-dup via SpawnedSpawnerObjectNames.
            var candidates = new[] { ot.Name, ot.Scope };
            if (!string.IsNullOrWhiteSpace(ot.FilePath) &&
                ot.FilePath.Replace('\\', '/').Contains("/npc_spawner/", StringComparison.OrdinalIgnoreCase))
            {
                MessageHandler.Instance.AddMessage(
                    MessageType.LOG,
                    $"[Scene] Spawner OT: name={ot.Name ?? "(null)"} scope={ot.Scope ?? "(null)"} file={ot.FilePath}");
            }
            foreach (var c in candidates)
            {
                if (string.IsNullOrWhiteSpace(c))
                {
                    continue;
                }

                var normalized = NormalizeSpawnerId(c);
                if (!npcDb.SpawnedSpawnerObjectNames.Add(normalized))
                {
                    continue;
                }

                TryCollectNpcSpawnerSpawns(
                    sceneFile,
                    parentMatrix,
                    normalized,
                    sceneObjectMatrix,
                    npcDb,
                    templateCache,
                    templateInProgress,
                    spawns,
                    token);
            }
        }

        private void TryCollectPlacementSpawnerSpawnsForSceneFile(
            string sceneFile,
            TRSCN trscn,
            Matrix4 parentMatrix,
            NpcSpawnerDbCache npcDb,
            Dictionary<string, List<TemplateModelSpawn>> templateCache,
            HashSet<string> templateInProgress,
            List<SceneModelSpawn> spawns,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            if (trscn?.Chunks == null || trscn.Chunks.Length == 0)
            {
                return;
            }

            var positionsByName = new Dictionary<string, PlacementPosition>(StringComparer.Ordinal);
            var templatesByName = new Dictionary<string, string>(StringComparer.Ordinal);
            var spawnerList = new List<PlacementSpawner>();
            CollectPlacementRegistryData(trscn.Chunks, positionsByName, templatesByName, spawnerList);

            if (spawnerList.Count == 0)
            {
                return;
            }

            int resolvedScenes = 0;
            int spawnedModels = 0;
            foreach (var s in spawnerList)
            {
                token.ThrowIfCancellationRequested();

                if (s == null || string.IsNullOrWhiteSpace(s.Name) || string.IsNullOrWhiteSpace(s.Scene))
                {
                    continue;
                }

                var normalized = NormalizeSpawnerId(s.Name);
                if (!npcDb.SpawnedSpawnerObjectNames.Add(normalized))
                {
                    continue;
                }

                // `PlacementSpawner.Scene` can be either a path or a template name (resolved via PlacementObjectTemplateArray).
                var spawnerScene = TryResolvePlacementTemplatePath(sceneFile, s.Scene);
                if (spawnerScene == null && templatesByName.TryGetValue(s.Scene, out var templatePath) && !string.IsNullOrWhiteSpace(templatePath))
                {
                    spawnerScene = TryResolvePlacementTemplatePath(sceneFile, templatePath);
                }
                if (spawnerScene == null)
                {
                    if (MessageHandler.Instance.DebugLogsEnabled)
                    {
                        MessageHandler.Instance.AddMessage(
                            MessageType.LOG,
                            $"[Scene] Placement spawner unresolved: name={s.Name} sceneRef={s.Scene} scene={Path.GetFileName(sceneFile)}");
                    }
                    continue;
                }

                resolvedScenes++;

                var baseMatrix = parentMatrix;
                if (positionsByName.TryGetValue(s.Name, out var p) || positionsByName.TryGetValue(normalized, out p))
                {
                    baseMatrix = parentMatrix * BuildPlacementPositionMatrix(p);
                }

                List<TemplateModelSpawn> templateSpawns;
                try
                {
                    templateSpawns = GetTemplateSpawns(spawnerScene, templateCache, templateInProgress, token);
                }
                catch
                {
                    templateSpawns = new List<TemplateModelSpawn>();
                }

                foreach (var m in templateSpawns)
                {
                    token.ThrowIfCancellationRequested();
                    spawns.Add(new SceneModelSpawn
                    {
                        SceneFile = sceneFile,
                        SceneObjectName = s.Name,
                        ModelPath = m.ModelPath,
                        ModelMatrix = baseMatrix * m.LocalMatrix
                    });
                    spawnedModels++;
                }
            }

            if (resolvedScenes > 0)
            {
                MessageHandler.Instance.AddMessage(
                    MessageType.LOG,
                    $"[Scene] Placement spawners: {spawnerList.Count} spawner(s), resolvedScenes={resolvedScenes}, spawnedModels={spawnedModels} scene={Path.GetFileName(sceneFile)}");
            }
        }

        private static void CollectPlacementRegistryData(
            SceneChunk[] chunks,
            Dictionary<string, PlacementPosition> positionsByName,
            Dictionary<string, string> templatesByName,
            List<PlacementSpawner> spawners)
        {
            foreach (var chunk in chunks)
            {
                if (chunk == null)
                {
                    continue;
                }

                if (chunk.Type == nameof(trinity_PlacementRegistry) && chunk.Data != null)
                {
                    try
                    {
                        var pr = FlatBufferConverter.DeserializeFrom<trinity_PlacementRegistry>(chunk.Data);
                        pr.Entry.Switch(
                            defaultCase: () => { },
                            case1: (PlacementObjectArray _) => { },
                            case2: (PlacementObjectTemplateArray tl) =>
                            {
                                if (tl?.Table == null) return;
                                foreach (var t in tl.Table)
                                {
                                    if (t == null || string.IsNullOrWhiteSpace(t.Name) || string.IsNullOrWhiteSpace(t.Path)) continue;
                                    if (!templatesByName.ContainsKey(t.Name))
                                    {
                                        templatesByName[t.Name] = t.Path;
                                    }
                                }
                            },
                            case3: (PlacementPositionArray pl) =>
                            {
                                if (pl?.Table == null) return;
                                foreach (var p in pl.Table)
                                {
                                    if (p == null || string.IsNullOrWhiteSpace(p.Name)) continue;
                                    if (!positionsByName.ContainsKey(p.Name))
                                    {
                                        positionsByName[p.Name] = p;
                                    }
                                }
                            },
                            case4: (PlacementSpawnerArray sl) =>
                            {
                                if (sl?.Table == null) return;
                                foreach (var s in sl.Table)
                                {
                                    if (s == null) continue;
                                    spawners.Add(s);
                                }
                            });
                    }
                    catch
                    {
                        // ignore
                    }
                }

                if (chunk.Children != null && chunk.Children.Length > 0)
                {
                    CollectPlacementRegistryData(chunk.Children, positionsByName, templatesByName, spawners);
                }
            }
        }

        private static Matrix4 BuildPlacementPositionMatrix(PlacementPosition p)
        {
            var pos = p.Position != null
                ? new Vector3(p.Position.X, p.Position.Y, p.Position.Z)
                : Vector3.Zero;

            var rot = p.Rotation != null
                ? new Vector3(p.Rotation.X, p.Rotation.Y, p.Rotation.Z)
                : Vector3.Zero;

            float rx = MathHelper.DegreesToRadians(rot.X);
            float ry = MathHelper.DegreesToRadians(rot.Y);
            float rz = MathHelper.DegreesToRadians(rot.Z);

            var q = Quaternion.FromEulerAngles(rx, ry, rz);
            q.Normalize();

            return Matrix4.CreateTranslation(pos) * Matrix4.CreateFromQuaternion(q);
        }

        private string? TryResolvePlacementTemplatePath(string sceneFile, string? templatePath)
        {
            if (string.IsNullOrWhiteSpace(templatePath))
            {
                return null;
            }

            // Some placements use relative paths (relative to the scene file), others use asset-root relative paths.
            var direct = ResolveModelPath(templatePath);
            if (direct != null)
            {
                return direct;
            }

            try
            {
                var sceneDir = Path.GetDirectoryName(sceneFile);
                if (!string.IsNullOrWhiteSpace(sceneDir))
                {
                    var combined = Path.GetFullPath(Path.Combine(sceneDir, templatePath));
                    if (File.Exists(combined))
                    {
                        return combined;
                    }

                    // Some assets use the usual _0 / _1 variants.
                    var ext = Path.GetExtension(combined);
                    if (!string.IsNullOrWhiteSpace(ext))
                    {
                        var noExt = combined.Substring(0, combined.Length - ext.Length);
                        foreach (int variant in new[] { 0, 1 })
                        {
                            var withVariant = $"{noExt}_{variant}{ext}";
                            if (File.Exists(withVariant))
                            {
                                return withVariant;
                            }
                        }
                    }
                }
            }
            catch
            {
                // ignore
            }

            return null;
        }
    }
}
