using GFTool.Core.Flatbuffers.TR.Scene;
using GFTool.Core.Flatbuffers.TR.Scene.Components;
using GFTool.Core.Flatbuffers.Hayabusa.Gfx2;
using GFTool.Core.Flatbuffers.TR.Gfx2;
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

            if (!SceneReferencePlanner.TryReadScene(sceneFile, out var trscn))
            {
                trscn = FlatBufferConverter.DeserializeFrom<TRSCN>(sceneFile);
            }

            var sceneCache = new Dictionary<string, TRSCN>(StringComparer.OrdinalIgnoreCase)
            {
                [GetSceneCacheKey(sceneFile)] = trscn
            };
            var activeScenes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
                sceneCache,
                activeScenes,
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
            Dictionary<string, TRSCN> sceneCache,
            HashSet<string> activeScenes,
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

            var sceneKey = GetSceneCacheKey(sceneFile);
            if (!activeScenes.Add(sceneKey))
            {
                return;
            }

            try
            {
                if (config.LoadNpcSpawnerModels)
                {
                    // Some scenes don't contain explicit spawner objects; spawn those via createScenePath linkage.
                    TryCollectNpcSpawnersForSceneFile(sceneFile, parentMatrix, npcDb, templateCache, templateInProgress, spawns, token);

                    // Some scenes contain spawners directly in the trscn (PlacementRegistry -> PlacementSpawnerArray).
                    TryCollectPlacementSpawnerSpawnsForSceneFile(sceneFile, trscn, parentMatrix, npcDb, templateCache, templateInProgress, spawns, token);
                }

                var handledSubScenes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var sceneRef in SceneReferencePlanner.GetDirectSceneReferences(
                             sceneFile,
                             trscn,
                             preferredSceneVariant,
                             includeZaBattleRuntimeInjections: true))
                {
                    token.ThrowIfCancellationRequested();
                    if (sceneRef.ResolvedPath == null)
                    {
                        continue;
                    }

                    handledSubScenes.Add(GetSceneCacheKey(sceneRef.ResolvedPath));
                    if (ShouldSkipSceneReference(sceneRef))
                    {
                        continue;
                    }

                    if (!TryGetCachedScene(sceneRef.ResolvedPath, sceneCache, out var subScn))
                    {
                        continue;
                    }

                    if (sceneRef.ResolvedPath.Replace('\\', '/').Contains("placement_npc_spawner", StringComparison.OrdinalIgnoreCase))
                    {
                        var key = $"npcSpawnerSceneSummary:{sceneRef.ResolvedPath}";
                        if (placementRegistryLogged.Add(key))
                        {
                            TryLogNpcSpawnerSceneSummary(sceneRef.ResolvedPath, subScn);
                        }
                    }

                    var subSceneRootMatrix = SceneTransformMath.GetSubSceneRootMatrix(parentMatrix);
                    CollectSceneModelSpawnsRecursive(
                        sceneRef.ResolvedPath,
                        subScn,
                        subSceneRootMatrix,
                        sceneCache,
                        activeScenes,
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
                        sceneCache,
                        activeScenes,
                        handledSubScenes,
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
            finally
            {
                activeScenes.Remove(sceneKey);
            }
        }

        private void CollectSceneModelSpawnsFromChunk(
            string sceneFile,
            SceneChunk chunk,
            Matrix4 parentMatrix,
            Dictionary<string, TRSCN> sceneCache,
            HashSet<string> activeScenes,
            HashSet<string> handledSubScenes,
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
                    sceneCache,
                    activeScenes,
                    handledSubScenes,
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
                TryCollectObjectTemplateChunkSpawns(
                    sceneFile,
                    chunk,
                    parentMatrix,
                    null,
                    sceneCache,
                    activeScenes,
                    handledSubScenes,
                    templateCache,
                    templateInProgress,
                    ccdatamCache,
                    ccdataCache,
                    ccModelsCache,
                    npcDb,
                    placementRegistryLogged,
                    spawns,
                    token);

                // ObjectTemplate traversal handles its own children with the embedded entity matrix.
                return;
            }
            else if (chunk.Type == nameof(trinity_CombineLODEntity))
            {
                TryCollectCombineLodSpawns(sceneFile, parentMatrix, chunk, spawns, token);
            }
            else if (chunk.Type == nameof(trinity_SceneObject))
            {
                TryCollectSceneObjectSpawns(
                    sceneFile,
                    chunk,
                    parentMatrix,
                    null,
                    sceneCache,
                    activeScenes,
                    handledSubScenes,
                    templateCache,
                    templateInProgress,
                    ccdatamCache,
                    ccdataCache,
                    ccModelsCache,
                    npcDb,
                    placementRegistryLogged,
                    spawns,
                    token);

                // Scene-object traversal handles its own children with the correct composed matrix.
                // Falling through to the generic child recursion below re-walks nested scene objects
                // using the old parent matrix, which produces duplicates and misplaced/origin spawns.
                return;
            }

            if (chunk.Children != null)
            {
                foreach (var child in chunk.Children)
                {
                    CollectSceneModelSpawnsFromChunk(
                        sceneFile,
                        child,
                        parentMatrix,
                        sceneCache,
                        activeScenes,
                        handledSubScenes,
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

        private void TryCollectCombineLodSpawns(
            string sceneFile,
            Matrix4 parentMatrix,
            SceneChunk chunk,
            List<SceneModelSpawn> spawns,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            if (chunk?.Data == null)
            {
                return;
            }

            trinity_CombineLODEntity? combineLod;
            try
            {
                combineLod = FlatBufferConverter.DeserializeFrom<trinity_CombineLODEntity>(chunk.Data);
            }
            catch
            {
                return;
            }

            if (combineLod?.Configures == null || combineLod.Configures.Length == 0)
            {
                return;
            }

            if (combineLod.SyncConditionalStreaming && config.LoadStreamingGroupSubScenes)
            {
                return;
            }

            var combineLodMatrix = SceneTransformMath.BuildCombineLodEntityMatrix(combineLod) * parentMatrix;
            var sceneObjectName = string.IsNullOrWhiteSpace(combineLod.ResourceName)
                ? Path.GetFileNameWithoutExtension(sceneFile)
                : combineLod.ResourceName;

            foreach (var configure in combineLod.Configures)
            {
                token.ThrowIfCancellationRequested();
                if (configure == null || string.IsNullOrWhiteSpace(configure.ModelFilePath))
                {
                    continue;
                }

                spawns.Add(new SceneModelSpawn
                {
                    SceneFile = sceneFile,
                    SceneObjectName = sceneObjectName,
                    ModelPath = configure.ModelFilePath,
                    ModelMatrix = combineLodMatrix
                });
            }
        }

        private void TryLoadSubScene(
            string sceneFile,
            SceneChunk chunk,
            Matrix4 parentMatrix,
            Dictionary<string, TRSCN> sceneCache,
            HashSet<string> activeScenes,
            HashSet<string> handledSubScenes,
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
                if (!SceneReferencePlanner.TryParseSubScene(chunk, out var sub) || string.IsNullOrWhiteSpace(sub.Filepath))
                {
                    return;
                }

                if (ShouldSkipGroupedSubScene(sub))
                {
                    return;
                }

                var resolved = ResolveSceneReference(sceneFile, sub.Filepath);
                if (resolved == null)
                {
                    return;
                }

                if (handledSubScenes.Contains(GetSceneCacheKey(resolved)))
                {
                    return;
                }

                if (!TryGetCachedScene(resolved, sceneCache, out var subScn))
                {
                    return;
                }

                // Evidence-first: when investigating missing NPC spawns, log what the spawner subscene actually contains.
                if (resolved.Replace('\\', '/').Contains("placement_npc_spawner", StringComparison.OrdinalIgnoreCase))
                {
                    var key = $"npcSpawnerSceneSummary:{resolved}";
                    if (placementRegistryLogged.Add(key))
                    {
                        TryLogNpcSpawnerSceneSummary(resolved, subScn);
                    }
                }

                var subSceneRootMatrix = SceneTransformMath.GetSubSceneRootMatrix(parentMatrix);
                CollectSceneModelSpawnsRecursive(
                    resolved,
                    subScn,
                    subSceneRootMatrix,
                    sceneCache,
                    activeScenes,
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

        private bool ShouldSkipSceneReference(SceneReference sceneRef)
        {
            if (sceneRef.IsStreamingGroup && !config.LoadStreamingGroupSubScenes)
            {
                return true;
            }

            return config.LoadStreamingGroupSubScenes && sceneRef.IsClodStreamingFallback;
        }

        private bool ShouldSkipGroupedSubScene(SubScene subScene)
        {
            return !config.LoadStreamingGroupSubScenes && !string.IsNullOrWhiteSpace(subScene.Group);
        }

        private static bool TryGetCachedScene(string sceneFile, Dictionary<string, TRSCN> sceneCache, out TRSCN trscn)
        {
            var key = GetSceneCacheKey(sceneFile);
            if (sceneCache.TryGetValue(key, out var cached) && cached != null)
            {
                trscn = cached;
                return true;
            }

            if (!SceneReferencePlanner.TryReadScene(sceneFile, out trscn))
            {
                trscn = null!;
                return false;
            }

            sceneCache[key] = trscn;
            return true;
        }

        private static string GetSceneCacheKey(string sceneFile)
        {
            try
            {
                return Path.GetFullPath(sceneFile);
            }
            catch
            {
                return sceneFile;
            }
        }

        private void TryCollectSceneObjectSpawns(
            string sceneFile,
            SceneChunk chunk,
            Matrix4 parentMatrix,
            string? parentSceneObjectName,
            Dictionary<string, TRSCN> sceneCache,
            HashSet<string> activeScenes,
            HashSet<string> handledSubScenes,
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

            var sceneObjectLocalMatrix = BuildSrtMatrix(sceneObject.Srt);
            var sceneObjectMatrix =
                sceneObject.AttachTransform && sceneObject.KeepWorldSrt
                    ? sceneObjectLocalMatrix
                    : sceneObjectLocalMatrix * parentMatrix;
            string? explicitInstancerPath = TryGetModelInstancerPath(sceneFile, chunk.Children);

            bool targetMatch = SceneDiagnosticsMatchesTarget(sceneObject.Name, explicitInstancerPath);
            if (!targetMatch)
            {
                foreach (var child in chunk.Children)
                {
                    if (child?.Type != nameof(trinity_ModelComponent))
                    {
                        continue;
                    }

                    try
                    {
                        var targetModelComponent = FlatBufferConverter.DeserializeFrom<trinity_ModelComponent>(child.Data);
                        if (targetModelComponent != null && SceneDiagnosticsMatchesTarget(sceneObject.Name, targetModelComponent.FilePath))
                        {
                            targetMatch = true;
                            break;
                        }
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }

            if (targetMatch)
            {
                var scenePos = new Vector3(sceneObjectMatrix.M41, sceneObjectMatrix.M42, sceneObjectMatrix.M43);
                var parentPos = new Vector3(parentMatrix.M41, parentMatrix.M42, parentMatrix.M43);
                var sceneScale = sceneObjectMatrix.ExtractScale();
                var sceneRot = ExtractNormalizedRotation(sceneObjectMatrix);
                var rawPos = sceneObject.Srt?.Translate != null
                    ? new Vector3(sceneObject.Srt.Translate.X, sceneObject.Srt.Translate.Y, sceneObject.Srt.Translate.Z)
                    : Vector3.Zero;
                var rawScale = sceneObject.Srt?.Scale != null
                    ? new Vector3(sceneObject.Srt.Scale.X, sceneObject.Srt.Scale.Y, sceneObject.Srt.Scale.Z)
                    : Vector3.One;
                var rawRot = sceneObject.Srt?.Rotate != null
                    ? new Vector3(sceneObject.Srt.Rotate.X, sceneObject.Srt.Rotate.Y, sceneObject.Srt.Rotate.Z)
                    : Vector3.Zero;

                MessageHandler.Instance.AddMessage(
                    MessageType.LOG,
                    $"[Scene][TargetObj] scene='{Path.GetFileName(sceneFile)}' obj='{sceneObject.Name}' parent='{parentSceneObjectName ?? ""}' parentPos=({parentPos.X}, {parentPos.Y}, {parentPos.Z}) rawPos=({rawPos.X}, {rawPos.Y}, {rawPos.Z}) rawRotEuler=({rawRot.X}, {rawRot.Y}, {rawRot.Z}) rawScale=({rawScale.X}, {rawScale.Y}, {rawScale.Z}) pos=({scenePos.X}, {scenePos.Y}, {scenePos.Z}) rot=({sceneRot.W}, {sceneRot.X}, {sceneRot.Y}, {sceneRot.Z}) scale=({sceneScale.X}, {sceneScale.Y}, {sceneScale.Z}) keepWorld={sceneObject.KeepWorldSrt} attach={sceneObject.AttachTransform} attachJoint='{sceneObject.AttachJointName ?? ""}' instancer='{explicitInstancerPath ?? ""}'");
            }

            if (config.LoadNpcSpawnerModels)
            {
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
            }

            foreach (var child in chunk.Children)
            {
                if (child?.Type != nameof(trinity_ModelComponent))
                {
                    if (child?.Type == nameof(trinity_SceneObject))
                    {
                        TryCollectSceneObjectSpawns(
                            sceneFile,
                            child,
                            sceneObjectMatrix,
                            sceneObject.Name,
                            sceneCache,
                            activeScenes,
                            handledSubScenes,
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
                    else if (child?.Type == nameof(trinity_ObjectTemplate))
                    {
                        TryCollectObjectTemplateChunkSpawns(
                            sceneFile,
                            child,
                            sceneObjectMatrix,
                            sceneObject.Name,
                            sceneCache,
                            activeScenes,
                            handledSubScenes,
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
                    else if (child?.Type == nameof(SubScene))
                    {
                        TryLoadSubScene(
                            sceneFile,
                            child,
                            sceneObjectMatrix,
                            sceneCache,
                            activeScenes,
                            handledSubScenes,
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
                    else if (IsBuildingComponentType(child?.Type))
                    {
                        TryCollectBuildingComponentSpawns(
                            sceneFile,
                            child!,
                            sceneObject.Name,
                            parentSceneObjectName,
                            parentMatrix,
                            sceneObjectMatrix,
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

                if (TryCollectTrinsInstanceRootSpawns(sceneFile, sceneObject.Name, parentSceneObjectName, modelComponent.FilePath, parentMatrix, sceneObjectMatrix, explicitInstancerPath, spawns))
                {
                    continue;
                }

                spawns.Add(new SceneModelSpawn
                {
                    SceneFile = sceneFile,
                    SceneObjectName = sceneObject.Name,
                    ParentSceneObjectName = parentSceneObjectName,
                    ParentSceneObjectWorldMatrix = parentMatrix,
                    ModelPath = modelComponent.FilePath,
                    ModelMatrix = sceneObjectMatrix,
                    LocalMatrix = sceneObjectLocalMatrix,
                    KeepWorldSrt = sceneObject.KeepWorldSrt,
                    AttachTransformEnable = sceneObject.AttachTransform,
                    AttachJointName = sceneObject.AttachJointName
                });
            }
        }

        private void TryCollectBuildingComponentSpawns(
            string sceneFile,
            SceneChunk chunk,
            string? sceneObjectName,
            string? parentSceneObjectName,
            Matrix4 parentMatrix,
            Matrix4 ownerWorldMatrix,
            List<SceneModelSpawn> spawns,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            if (chunk?.Data == null)
            {
                return;
            }

            trinity_BuildingComponent? buildingComponent;
            try
            {
                buildingComponent = FlatBufferConverter.DeserializeFrom<trinity_BuildingComponent>(chunk.Data);
            }
            catch
            {
                return;
            }

            if (buildingComponent == null || string.IsNullOrWhiteSpace(buildingComponent.FilePath))
            {
                return;
            }

            var buildingFile = ResolveBuildingFilePath(sceneFile, buildingComponent.FilePath);
            if (buildingFile == null)
            {
                if (SceneDiagnosticsMatchesTarget(sceneObjectName, buildingComponent.FilePath))
                {
                    MessageHandler.Instance.AddMessage(
                        MessageType.WARNING,
                        $"[Scene][Building] Missing building file '{buildingComponent.FilePath}' for '{sceneObjectName ?? ""}' in {Path.GetFileName(sceneFile)}");
                }
                return;
            }

            TrBuilding? building;
            try
            {
                building = FlatBufferConverter.DeserializeFrom<TrBuilding>(buildingFile);
            }
            catch (Exception ex)
            {
                MessageHandler.Instance.AddMessage(
                    MessageType.WARNING,
                    $"[Scene] Failed to read building file '{buildingComponent.FilePath}': {ex.GetType().Name}: {ex.Message}");
                return;
            }

            if (building?.Chips == null || building.Chips.Length == 0)
            {
                return;
            }

            var emitted = 0;
            foreach (var chip in building.Chips)
            {
                token.ThrowIfCancellationRequested();
                if (chip == null || string.IsNullOrWhiteSpace(chip.ModelName) || chip.Srt == null)
                {
                    continue;
                }

                var chipModelPath = BuildBuildingChipModelPath(buildingComponent.FilePath, chip.ModelName);
                if (chipModelPath == null)
                {
                    continue;
                }

                var chipLocalMatrix = SceneTransformMath.BuildSrtMatrixRadians(
                    ToVector3OrDefault(chip.Srt.Scale, Vector3.One),
                    ToVector3OrDefault(chip.Srt.Rotate, Vector3.Zero),
                    ToVector3OrDefault(chip.Srt.Translate, Vector3.Zero));
                var chipWorldMatrix = chipLocalMatrix * ownerWorldMatrix;

                spawns.Add(new SceneModelSpawn
                {
                    SceneFile = sceneFile,
                    SceneObjectName = sceneObjectName,
                    ParentSceneObjectName = parentSceneObjectName,
                    ParentSceneObjectWorldMatrix = parentMatrix,
                    ModelPath = chipModelPath,
                    ModelMatrix = chipWorldMatrix,
                    LocalMatrix = chipLocalMatrix
                });
                emitted++;
            }

            if (SceneDiagnosticsMatchesTarget(sceneObjectName, buildingComponent.FilePath))
            {
                MessageHandler.Instance.AddMessage(
                    MessageType.LOG,
                    $"[Scene][Building] obj='{sceneObjectName ?? ""}' file='{buildingComponent.FilePath}' chips={building.Chips.Length} emitted={emitted} worldPos=({ownerWorldMatrix.M41}, {ownerWorldMatrix.M42}, {ownerWorldMatrix.M43})");
            }
        }

        private static bool IsBuildingComponentType(string? type)
        {
            return string.Equals(type, nameof(trinity_BuildingComponent), StringComparison.Ordinal) ||
                   string.Equals(type, "BuildingComponent", StringComparison.Ordinal) ||
                   string.Equals(type, "gfl.scene.fb.BuildingComponent", StringComparison.Ordinal);
        }

        private string? ResolveBuildingFilePath(string sceneFile, string filePath)
        {
            var resolved = ResolveModelPath(filePath);
            if (resolved != null)
            {
                return resolved;
            }

            if (Path.IsPathRooted(filePath) && File.Exists(filePath))
            {
                return filePath;
            }

            var sceneDir = Path.GetDirectoryName(sceneFile);
            if (!string.IsNullOrWhiteSpace(sceneDir))
            {
                var sceneRelative = Path.GetFullPath(Path.Combine(sceneDir, filePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar)));
                if (File.Exists(sceneRelative))
                {
                    return sceneRelative;
                }
            }

            return null;
        }

        private static string? BuildBuildingChipModelPath(string buildingComponentPath, string modelName)
        {
            var firstDelIndex = modelName.IndexOf('_');
            var lastDelIndex = modelName.LastIndexOf('_');
            if (firstDelIndex <= 0 || lastDelIndex <= 0)
            {
                return null;
            }

            var x2 = modelName[..firstDelIndex];
            var x1 = modelName[..lastDelIndex];
            var normalizedBuildingPath = buildingComponentPath.Replace('\\', '/');
            var parts = normalizedBuildingPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (!string.Equals(parts[i], x2, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var folder = parts[i + 1];
                if (!folder.StartsWith(x2, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var root = string.Join('/', parts.Take(i));
                var suffix = folder[x2.Length..];
                return string.IsNullOrEmpty(root)
                    ? $"{x2}/{x2}{suffix}/{x1}/{modelName}.trmdl"
                    : $"{root}/{x2}/{x2}{suffix}/{x1}/{modelName}.trmdl";
            }

            return $"field/model/{x2}/{x2}_b/{x1}/{modelName}.trmdl";
        }

        private static Vector3 ToVector3OrDefault(Trinity.Core.Flatbuffers.Utils.Vector3f? value, Vector3 fallback)
        {
            return value == null
                ? fallback
                : new Vector3(value.X, value.Y, value.Z);
        }

        private void TryCollectObjectTemplateChunkSpawns(
            string sceneFile,
            SceneChunk templateChunk,
            Matrix4 parentMatrix,
            string? parentSceneObjectName,
            Dictionary<string, TRSCN> sceneCache,
            HashSet<string> activeScenes,
            HashSet<string> handledSubScenes,
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

            trinity_ObjectTemplate? objectTemplate;
            try
            {
                objectTemplate = FlatBufferConverter.DeserializeFrom<trinity_ObjectTemplate>(templateChunk.Data);
            }
            catch
            {
                return;
            }

            if (objectTemplate == null)
            {
                return;
            }

            SceneTransformMath.TryGetObjectTemplateSceneObject(objectTemplate, out var templateSceneObject);
            SceneTransformMath.TryBuildObjectTemplateInstanceMatrix(
                objectTemplate,
                parentMatrix,
                out var objectTemplateMatrix,
                out var objectTemplateSceneObjectName);

            var objectTemplateName = !string.IsNullOrWhiteSpace(objectTemplateSceneObjectName)
                ? objectTemplateSceneObjectName
                : !string.IsNullOrWhiteSpace(objectTemplate.Name)
                    ? objectTemplate.Name
                    : parentSceneObjectName;
            var objectTemplateLocalMatrix = templateSceneObject != null
                ? BuildSrtMatrix(templateSceneObject.Srt)
                : Matrix4.Identity;

            if (config.LoadNpcSpawnerModels)
            {
                TryCollectNpcSpawnerFromObjectTemplate(
                    sceneFile,
                    parentMatrix,
                    objectTemplateMatrix,
                    templateChunk,
                    npcDb,
                    templateCache,
                    templateInProgress,
                    spawns,
                    token);
            }

            TryCollectObjectTemplateSpawns(
                sceneFile,
                parentSceneObjectName,
                parentMatrix,
                templateChunk,
                templateCache,
                templateInProgress,
                spawns,
                token);

            if (templateChunk.Children == null || templateChunk.Children.Length == 0)
            {
                return;
            }

            string? explicitInstancerPath = TryGetModelInstancerPath(sceneFile, templateChunk.Children);

            foreach (var child in templateChunk.Children)
            {
                token.ThrowIfCancellationRequested();

                if (child == null || string.IsNullOrWhiteSpace(child.Type))
                {
                    continue;
                }

                if (child.Type == nameof(trinity_ModelComponent))
                {
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

                    if (TryCollectTrinsInstanceRootSpawns(
                            sceneFile,
                            objectTemplateName,
                            parentSceneObjectName,
                            modelComponent.FilePath,
                            parentMatrix,
                            objectTemplateMatrix,
                            explicitInstancerPath,
                            spawns))
                    {
                        continue;
                    }

                    spawns.Add(new SceneModelSpawn
                    {
                        SceneFile = sceneFile,
                        SceneObjectName = objectTemplateName,
                        ParentSceneObjectName = parentSceneObjectName,
                        ParentSceneObjectWorldMatrix = parentMatrix,
                        ModelPath = modelComponent.FilePath,
                        ModelMatrix = objectTemplateMatrix,
                        LocalMatrix = objectTemplateLocalMatrix,
                        KeepWorldSrt = templateSceneObject?.KeepWorldSrt ?? false,
                        AttachTransformEnable = templateSceneObject?.AttachTransform ?? false,
                        AttachJointName = templateSceneObject?.AttachJointName
                    });
                }
                else if (child.Type == nameof(trinity_SceneObject))
                {
                    TryCollectSceneObjectSpawns(
                        sceneFile,
                        child,
                        objectTemplateMatrix,
                        objectTemplateName,
                        sceneCache,
                        activeScenes,
                        handledSubScenes,
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
                else if (child.Type == nameof(trinity_ObjectTemplate))
                {
                    TryCollectObjectTemplateChunkSpawns(
                        sceneFile,
                        child,
                        objectTemplateMatrix,
                        objectTemplateName,
                        sceneCache,
                        activeScenes,
                        handledSubScenes,
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
                else if (child.Type == nameof(SubScene))
                {
                    TryLoadSubScene(
                        sceneFile,
                        child,
                        objectTemplateMatrix,
                        sceneCache,
                        activeScenes,
                        handledSubScenes,
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
                else if (child.Type == nameof(trinity_CharacterCreationMasterComponent))
                {
                    TryCollectCharacterCreationSpawns(
                        sceneFile,
                        objectTemplateName,
                        objectTemplateMatrix,
                        child,
                        ccdatamCache,
                        ccdataCache,
                        ccModelsCache,
                        spawns,
                        token);
                }
                else if (IsBuildingComponentType(child.Type))
                {
                    TryCollectBuildingComponentSpawns(
                        sceneFile,
                        child,
                        objectTemplateName,
                        parentSceneObjectName,
                        parentMatrix,
                        objectTemplateMatrix,
                        spawns,
                        token);
                }
                else if (child.Type == nameof(trinity_CombineLODEntity))
                {
                    TryCollectCombineLodSpawns(sceneFile, objectTemplateMatrix, child, spawns, token);
                }
                else if (child.Children != null && child.Children.Length > 0)
                {
                    CollectSceneModelSpawnsFromChunk(
                        sceneFile,
                        child,
                        objectTemplateMatrix,
                        sceneCache,
                        activeScenes,
                        handledSubScenes,
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

        private bool TryCollectTrinsInstanceRootSpawns(
            string sceneFile,
            string? sceneObjectName,
            string? parentSceneObjectName,
            string modelPath,
            Matrix4 parentMatrix,
            Matrix4 sceneObjectMatrix,
            string? explicitTrinsPath,
            List<SceneModelSpawn> spawns)
        {
            if (string.IsNullOrWhiteSpace(sceneFile) || string.IsNullOrWhiteSpace(sceneObjectName))
            {
                return false;
            }

            string? trinsPath = explicitTrinsPath;
            if (string.IsNullOrWhiteSpace(trinsPath))
            {
                string sceneDir = Path.GetDirectoryName(sceneFile) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(sceneDir))
                {
                    return false;
                }

                string siblingTrinsPath = Path.Combine(sceneDir, sceneObjectName + ".trins");
                if (!File.Exists(siblingTrinsPath))
                {
                    return false;
                }

                trinsPath = siblingTrinsPath;
            }

            List<Matrix4> instanceMatrices;
            try
            {
                instanceMatrices = LoadTrinsInstanceMatrices(trinsPath);
            }
            catch (Exception ex)
            {
                MessageHandler.Instance.AddMessage(
                    MessageType.WARNING,
                    $"[Scene] Failed to read instances for '{sceneObjectName}': {ex.GetType().Name}: {ex.Message}");
                return false;
            }

            if (instanceMatrices.Count == 0)
            {
                return false;
            }

            if (SceneDiagnosticsMatchesTarget(sceneObjectName, modelPath) || SceneDiagnosticsMatchesTarget(sceneObjectName, trinsPath))
            {
                var rootPos = new Vector3(sceneObjectMatrix.M41, sceneObjectMatrix.M42, sceneObjectMatrix.M43);
                MessageHandler.Instance.AddMessage(
                    MessageType.LOG,
                    $"[Scene][TargetInst] root='{sceneObjectName}' trins='{trinsPath}' scene='{Path.GetFileName(sceneFile)}' parentPos=({parentMatrix.M41}, {parentMatrix.M42}, {parentMatrix.M43}) rootPos=({rootPos.X}, {rootPos.Y}, {rootPos.Z}) count={instanceMatrices.Count}");

                for (int i = 0; i < instanceMatrices.Count; i++)
                {
                    var instanceMatrix = instanceMatrices[i];
                    var instancePos = new Vector3(instanceMatrix.M41, instanceMatrix.M42, instanceMatrix.M43);
                    var instanceScale = instanceMatrix.ExtractScale();
                    MessageHandler.Instance.AddMessage(
                        MessageType.LOG,
                        $"[Scene][TargetInst] root='{sceneObjectName}' idx={i} pos=({instancePos.X}, {instancePos.Y}, {instancePos.Z}) scale=({instanceScale.X}, {instanceScale.Y}, {instanceScale.Z})");
                }
            }

            foreach (var instanceMatrix in instanceMatrices)
            {
                spawns.Add(new SceneModelSpawn
                {
                    SceneFile = sceneFile,
                    SceneObjectName = sceneObjectName,
                    ParentSceneObjectName = parentSceneObjectName,
                    ParentSceneObjectWorldMatrix = parentMatrix,
                    ModelPath = modelPath,
                    ModelMatrix = instanceMatrix,
                    LocalMatrix = instanceMatrix,
                    IsTrinsInstance = true
                });
            }

            if (SceneDiagnosticsEnabled)
            {
                MessageHandler.Instance.AddMessage(
                    MessageType.LOG,
                    $"[Scene] Expanded instance root '{sceneObjectName}' -> {instanceMatrices.Count} spawn(s) from {Path.GetFileName(trinsPath)}");
            }

            return true;
        }

        private string? TryGetModelInstancerPath(string sceneFile, SceneChunk[] children)
        {
            foreach (var child in children)
            {
                if (child?.Type != nameof(trinity_ModelInstancerComponent))
                {
                    continue;
                }

                try
                {
                    var instancer = FlatBufferConverter.DeserializeFrom<trinity_ModelInstancerComponent>(child.Data);
                    if (instancer == null || string.IsNullOrWhiteSpace(instancer.FilePath))
                    {
                        continue;
                    }

                    return ResolveModelPath(instancer.FilePath) ?? ResolveSceneReference(sceneFile, instancer.FilePath);
                }
                catch
                {
                    return null;
                }
            }

            return null;
        }

        private static List<Matrix4> LoadTrinsInstanceMatrices(string trinsPath)
        {
            var result = new List<Matrix4>();
            var buf = FlatBufferConverter.DeserializeFrom<InstanceBuffer>(trinsPath);
            if (buf == null)
            {
                return result;
            }

            if (buf.Buffer?.Data != null && buf.Buffer.Data.Length >= 64)
            {
                int count = buf.ParticleCount > 0
                    ? Math.Min(buf.ParticleCount, buf.Buffer.Data.Length / 64)
                    : buf.Buffer.Data.Length / 64;

                for (int i = 0; i < count; i++)
                {
                    int offset = i * 64;
                    result.Add(ReadTrinsMatrix(buf.Buffer.Data, offset));
                }

                return result;
            }

            if (buf.ParticleList != null && buf.ParticleList.Length > 0)
            {
                foreach (var particle in buf.ParticleList)
                {
                    if (particle?.Srt == null)
                    {
                        continue;
                    }

                    var srt = particle.Srt;
                    var scale = new Vector3(srt.Scale.X, srt.Scale.Y, srt.Scale.Z);
                    var rotDeg = new Vector3(srt.Rotate.X, srt.Rotate.Y, srt.Rotate.Z);
                    var trans = new Vector3(srt.Translate.X, srt.Translate.Y, srt.Translate.Z);
                    result.Add(SceneTransformMath.BuildSrtMatrix(scale, rotDeg, trans));
                }

                return result;
            }

            return result;
        }

        private static Matrix4 ReadTrinsMatrix(byte[] data, int offset)
        {
            float f0 = BitConverter.ToSingle(data, offset + 0);
            float f1 = BitConverter.ToSingle(data, offset + 4);
            float f2 = BitConverter.ToSingle(data, offset + 8);
            float f3 = BitConverter.ToSingle(data, offset + 12);
            float f4 = BitConverter.ToSingle(data, offset + 16);
            float f5 = BitConverter.ToSingle(data, offset + 20);
            float f6 = BitConverter.ToSingle(data, offset + 24);
            float f7 = BitConverter.ToSingle(data, offset + 28);
            float f8 = BitConverter.ToSingle(data, offset + 32);
            float f9 = BitConverter.ToSingle(data, offset + 36);
            float f10 = BitConverter.ToSingle(data, offset + 40);
            float f11 = BitConverter.ToSingle(data, offset + 44);
            float f12 = BitConverter.ToSingle(data, offset + 48);
            float f13 = BitConverter.ToSingle(data, offset + 52);
            float f14 = BitConverter.ToSingle(data, offset + 56);
            float f15 = BitConverter.ToSingle(data, offset + 60);

            return new Matrix4(
                f0, f1, f2, f3,
                f4, f5, f6, f7,
                f8, f9, f10, f11,
                f12, f13, f14, f15);
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
                    baseMatrix = BuildPlacementPositionMatrix(p) * parentMatrix;
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
                        ModelMatrix = m.LocalMatrix * baseMatrix,
                        LocalMatrix = m.LocalMatrix
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

            return SceneTransformMath.BuildSrtMatrix(Vector3.One, rot, pos);
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
