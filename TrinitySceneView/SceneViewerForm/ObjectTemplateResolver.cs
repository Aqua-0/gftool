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
        private void TryCollectObjectTemplateSpawns(
            string sceneFile,
            string? parentSceneObjectName,
            Matrix4 instanceMatrix,
            SceneChunk templateChunk,
            Dictionary<string, List<TemplateModelSpawn>> templateCache,
            HashSet<string> templateInProgress,
            List<SceneModelSpawn> spawns,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            trinity_ObjectTemplate? ot;
            try
            {
                ot = FlatBufferConverter.DeserializeFrom<trinity_ObjectTemplate>(templateChunk.Data);
            }
            catch
            {
                return;
            }

            if (ot == null || !SceneTransformMath.ShouldLoadObjectTemplateFile(ot))
            {
                return;
            }

            var resolved = ResolveSceneReference(sceneFile, ot.FilePath);
            if (resolved == null)
            {
                MessageHandler.Instance.AddMessage(
                    MessageType.WARNING,
                    $"[Scene] Missing ObjectTemplate file: {ot.FilePath} (SceneObject={parentSceneObjectName ?? "(null)"}, scene={Path.GetFileName(sceneFile)})");
                return;
            }

            var templateSpawns = GetTemplateSpawns(resolved, templateCache, templateInProgress, token);
            if (templateSpawns.Count == 0)
            {
                return;
            }

            SceneTransformMath.TryBuildObjectTemplateInstanceMatrix(
                ot,
                instanceMatrix,
                out var objectTemplateInstanceMatrix,
                out var objectTemplateSceneObjectName);

            if (SceneDiagnosticsMatchesTarget(parentSceneObjectName, ot.FilePath))
            {
                MessageHandler.Instance.AddMessage(
                    MessageType.LOG,
                    $"[Scene][TargetTpl] parent='{parentSceneObjectName}' template='{resolved}' entity='{objectTemplateSceneObjectName ?? ""}' count={templateSpawns.Count}");

                foreach (var t in templateSpawns)
                {
                    var pos = new Vector3(t.LocalMatrix.M41, t.LocalMatrix.M42, t.LocalMatrix.M43);
                    var scale = t.LocalMatrix.ExtractScale();
                    var rot = ExtractNormalizedRotation(t.LocalMatrix);
                    MessageHandler.Instance.AddMessage(
                        MessageType.LOG,
                        $"[Scene][TargetTpl] parent='{parentSceneObjectName}' tplObj='{t.SceneObjectName}' model='{t.ModelPath}' localPos=({pos.X}, {pos.Y}, {pos.Z}) localRot=({rot.W}, {rot.X}, {rot.Y}, {rot.Z}) localScale=({scale.X}, {scale.Y}, {scale.Z})");
                }
            }

            foreach (var t in templateSpawns)
            {
                token.ThrowIfCancellationRequested();
                spawns.Add(new SceneModelSpawn
                {
                    SceneFile = sceneFile,
                    SceneObjectName = string.IsNullOrWhiteSpace(t.SceneObjectName)
                        ? objectTemplateSceneObjectName ?? parentSceneObjectName
                        : t.SceneObjectName,
                    ModelPath = t.ModelPath,
                    ModelMatrix = t.LocalMatrix * objectTemplateInstanceMatrix
                });
            }
        }

        private List<TemplateModelSpawn> GetTemplateSpawns(
            string templateFile,
            Dictionary<string, List<TemplateModelSpawn>> templateCache,
            HashSet<string> templateInProgress,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            if (templateCache.TryGetValue(templateFile, out var cached))
            {
                return cached;
            }

            if (!templateInProgress.Add(templateFile))
            {
                MessageHandler.Instance.AddMessage(MessageType.WARNING, $"[Scene] ObjectTemplate recursion detected: {Path.GetFileName(templateFile)}");
                templateCache[templateFile] = new List<TemplateModelSpawn>();
                return templateCache[templateFile];
            }

            try
            {
                if (!File.Exists(templateFile))
                {
                    templateCache[templateFile] = new List<TemplateModelSpawn>();
                    return templateCache[templateFile];
                }

                try
                {
                    var sceneCache = new Dictionary<string, TRSCN>(StringComparer.OrdinalIgnoreCase);
                    if (!TryGetCachedScene(templateFile, sceneCache, out var t))
                    {
                        templateCache[templateFile] = new List<TemplateModelSpawn>();
                        return templateCache[templateFile];
                    }

                    var activeScenes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var spawns = new List<TemplateModelSpawn>();
                    CollectTemplateSpawnsRecursive(templateFile, t, Matrix4.Identity, sceneCache, activeScenes, templateCache, templateInProgress, spawns, token);
                    templateCache[templateFile] = spawns;
                    return spawns;
                }
                catch
                {
                    templateCache[templateFile] = new List<TemplateModelSpawn>();
                    return templateCache[templateFile];
                }
            }
            finally
            {
                templateInProgress.Remove(templateFile);
            }
        }

        private void CollectTemplateSpawnsRecursive(
            string sceneFile,
            TRSCN trscn,
            Matrix4 parentMatrix,
            Dictionary<string, TRSCN> sceneCache,
            HashSet<string> activeScenes,
            Dictionary<string, List<TemplateModelSpawn>> templateCache,
            HashSet<string> templateInProgress,
            List<TemplateModelSpawn> spawns,
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
                var handledSubScenes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var sceneRef in SceneReferencePlanner.GetDirectSceneReferences(
                             sceneFile,
                             trscn,
                             preferredSceneVariant,
                             includeZaBattleRuntimeInjections: false))
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

                    var subSceneRootMatrix = SceneTransformMath.GetSubSceneRootMatrix(parentMatrix);
                    CollectTemplateSpawnsRecursive(sceneRef.ResolvedPath, subScn, subSceneRootMatrix, sceneCache, activeScenes, templateCache, templateInProgress, spawns, token);
                }

                if (trscn.Chunks == null)
                {
                    return;
                }

                foreach (var chunk in trscn.Chunks)
                {
                    CollectTemplateSpawnsFromChunk(sceneFile, chunk, parentMatrix, sceneCache, activeScenes, handledSubScenes, templateCache, templateInProgress, spawns, token);
                }
            }
            finally
            {
                activeScenes.Remove(sceneKey);
            }
        }

        private void CollectTemplateSpawnsFromChunk(
            string sceneFile,
            SceneChunk chunk,
            Matrix4 parentMatrix,
            Dictionary<string, TRSCN> sceneCache,
            HashSet<string> activeScenes,
            HashSet<string> handledSubScenes,
            Dictionary<string, List<TemplateModelSpawn>> templateCache,
            HashSet<string> templateInProgress,
            List<TemplateModelSpawn> spawns,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            if (chunk == null || string.IsNullOrWhiteSpace(chunk.Type))
            {
                return;
            }

            if (chunk.Type == nameof(SubScene))
            {
                try
                {
                    if (SceneReferencePlanner.TryParseSubScene(chunk, out var sub) && !string.IsNullOrWhiteSpace(sub.Filepath))
                    {
                        if (ShouldSkipGroupedSubScene(sub))
                        {
                            return;
                        }

                        var resolved = ResolveSceneReference(sceneFile, sub.Filepath);
                        if (resolved != null &&
                            !handledSubScenes.Contains(GetSceneCacheKey(resolved)) &&
                            TryGetCachedScene(resolved, sceneCache, out var subScn))
                        {
                            var subSceneRootMatrix = SceneTransformMath.GetSubSceneRootMatrix(parentMatrix);
                            CollectTemplateSpawnsRecursive(resolved, subScn, subSceneRootMatrix, sceneCache, activeScenes, templateCache, templateInProgress, spawns, token);
                        }
                    }
                }
                catch
                {
                    // ignore
                }
            }
            else if (chunk.Type == nameof(trinity_SceneObject))
            {
                TryCollectTemplateSceneObjectSpawns(
                    sceneFile,
                    chunk,
                    parentMatrix,
                    sceneCache,
                    activeScenes,
                    handledSubScenes,
                    templateCache,
                    templateInProgress,
                    spawns,
                    token);

                // Template scene-object traversal also handles its own children with the composed matrix.
                // Re-entering the generic recursion below would re-walk nested scene objects with the
                // wrong parent matrix and duplicate/misplace template-derived spawns.
                return;
            }
            else if (chunk.Type == nameof(trinity_ObjectTemplate))
            {
                TryCollectTemplateObjectTemplateSpawns(
                    sceneFile,
                    chunk,
                    parentMatrix,
                    sceneCache,
                    activeScenes,
                    handledSubScenes,
                    templateCache,
                    templateInProgress,
                    spawns,
                    token);

                // ObjectTemplate traversal handles its children with the embedded entity matrix.
                return;
            }
            else if (chunk.Type == nameof(trinity_CharacterCreationMasterComponent))
            {
                // Some templates store CC components at the root (not under a SceneObject).
                TryCollectTemplateCharacterCreationSpawns(sceneFile, parentMatrix, sceneObjectName: null, ccChunk: chunk, spawns: spawns, token: token);
            }

            if (chunk.Children != null)
            {
                foreach (var child in chunk.Children)
                {
                    CollectTemplateSpawnsFromChunk(sceneFile, child, parentMatrix, sceneCache, activeScenes, handledSubScenes, templateCache, templateInProgress, spawns, token);
                }
            }
        }

        private void TryCollectTemplateSceneObjectSpawns(
            string sceneFile,
            SceneChunk chunk,
            Matrix4 parentMatrix,
            Dictionary<string, TRSCN> sceneCache,
            HashSet<string> activeScenes,
            HashSet<string> handledSubScenes,
            Dictionary<string, List<TemplateModelSpawn>> templateCache,
            HashSet<string> templateInProgress,
            List<TemplateModelSpawn> spawns,
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

            if (SceneDiagnosticsMatchesTarget(sceneObject.Name, sceneFile))
            {
                var pos = new Vector3(sceneObjectMatrix.M41, sceneObjectMatrix.M42, sceneObjectMatrix.M43);
                var scale = sceneObjectMatrix.ExtractScale();
                var rot = ExtractNormalizedRotation(sceneObjectMatrix);
                MessageHandler.Instance.AddMessage(
                    MessageType.LOG,
                    $"[Scene][TargetTplObj] scene='{Path.GetFileName(sceneFile)}' obj='{sceneObject.Name}' pos=({pos.X}, {pos.Y}, {pos.Z}) rot=({rot.W}, {rot.X}, {rot.Y}, {rot.Z}) scale=({scale.X}, {scale.Y}, {scale.Z})");
            }

            foreach (var child in chunk.Children)
            {
                if (child?.Type != nameof(trinity_ModelComponent))
                {
                    if (child?.Type == nameof(trinity_SceneObject))
                    {
                        TryCollectTemplateSceneObjectSpawns(
                            sceneFile,
                            child,
                            sceneObjectMatrix,
                            sceneCache,
                            activeScenes,
                            handledSubScenes,
                            templateCache,
                            templateInProgress,
                            spawns,
                            token);
                    }
                    else if (child?.Type == nameof(trinity_ObjectTemplate))
                    {
                        TryCollectTemplateObjectTemplateSpawns(
                            sceneFile,
                            child,
                            sceneObjectMatrix,
                            sceneCache,
                            activeScenes,
                            handledSubScenes,
                            templateCache,
                            templateInProgress,
                            spawns,
                            token);
                    }
                    else if (child?.Type == nameof(SubScene))
                    {
                        try
                        {
                            if (SceneReferencePlanner.TryParseSubScene(child, out var sub) && !string.IsNullOrWhiteSpace(sub.Filepath))
                            {
                                if (ShouldSkipGroupedSubScene(sub))
                                {
                                    continue;
                                }

                                var resolved = ResolveSceneReference(sceneFile, sub.Filepath);
                                if (resolved != null &&
                                    !handledSubScenes.Contains(GetSceneCacheKey(resolved)) &&
                                    TryGetCachedScene(resolved, sceneCache, out var subScn))
                                {
                                    var subSceneRootMatrix = SceneTransformMath.GetSubSceneRootMatrix(sceneObjectMatrix);
                                    CollectTemplateSpawnsRecursive(resolved, subScn, subSceneRootMatrix, sceneCache, activeScenes, templateCache, templateInProgress, spawns, token);
                                }
                            }
                        }
                        catch
                        {
                            // ignore
                        }
                    }
                    else if (child?.Type == nameof(trinity_CharacterCreationMasterComponent))
                    {
                        TryCollectTemplateCharacterCreationSpawns(sceneFile, sceneObjectMatrix, sceneObject.Name, child, spawns, token);
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

                spawns.Add(new TemplateModelSpawn
                {
                    SceneObjectName = sceneObject.Name,
                    ModelPath = modelComponent.FilePath,
                    LocalMatrix = sceneObjectMatrix
                });
            }
        }

        private void TryCollectTemplateObjectTemplateSpawns(
            string sceneFile,
            SceneChunk templateChunk,
            Matrix4 parentMatrix,
            Dictionary<string, TRSCN> sceneCache,
            HashSet<string> activeScenes,
            HashSet<string> handledSubScenes,
            Dictionary<string, List<TemplateModelSpawn>> templateCache,
            HashSet<string> templateInProgress,
            List<TemplateModelSpawn> spawns,
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

            SceneTransformMath.TryBuildObjectTemplateInstanceMatrix(
                objectTemplate,
                parentMatrix,
                out var objectTemplateMatrix,
                out var objectTemplateSceneObjectName);

            var objectTemplateName = !string.IsNullOrWhiteSpace(objectTemplateSceneObjectName)
                ? objectTemplateSceneObjectName
                : !string.IsNullOrWhiteSpace(objectTemplate.Name)
                    ? objectTemplate.Name
                    : null;

            TryCollectNestedTemplateSpawns(
                sceneFile,
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

                    spawns.Add(new TemplateModelSpawn
                    {
                        SceneObjectName = objectTemplateName,
                        ModelPath = modelComponent.FilePath,
                        LocalMatrix = objectTemplateMatrix
                    });
                }
                else if (child.Type == nameof(trinity_SceneObject))
                {
                    TryCollectTemplateSceneObjectSpawns(
                        sceneFile,
                        child,
                        objectTemplateMatrix,
                        sceneCache,
                        activeScenes,
                        handledSubScenes,
                        templateCache,
                        templateInProgress,
                        spawns,
                        token);
                }
                else if (child.Type == nameof(trinity_ObjectTemplate))
                {
                    TryCollectTemplateObjectTemplateSpawns(
                        sceneFile,
                        child,
                        objectTemplateMatrix,
                        sceneCache,
                        activeScenes,
                        handledSubScenes,
                        templateCache,
                        templateInProgress,
                        spawns,
                        token);
                }
                else if (child.Type == nameof(SubScene))
                {
                    try
                    {
                        if (SceneReferencePlanner.TryParseSubScene(child, out var sub) && !string.IsNullOrWhiteSpace(sub.Filepath))
                        {
                            if (ShouldSkipGroupedSubScene(sub))
                            {
                                continue;
                            }

                            var resolved = ResolveSceneReference(sceneFile, sub.Filepath);
                            if (resolved != null &&
                                !handledSubScenes.Contains(GetSceneCacheKey(resolved)) &&
                                TryGetCachedScene(resolved, sceneCache, out var subScn))
                            {
                                var subSceneRootMatrix = SceneTransformMath.GetSubSceneRootMatrix(objectTemplateMatrix);
                                CollectTemplateSpawnsRecursive(resolved, subScn, subSceneRootMatrix, sceneCache, activeScenes, templateCache, templateInProgress, spawns, token);
                            }
                        }
                    }
                    catch
                    {
                        // ignore
                    }
                }
                else if (child.Type == nameof(trinity_CharacterCreationMasterComponent))
                {
                    TryCollectTemplateCharacterCreationSpawns(sceneFile, objectTemplateMatrix, objectTemplateName, child, spawns, token);
                }
                else if (child.Children != null && child.Children.Length > 0)
                {
                    CollectTemplateSpawnsFromChunk(
                        sceneFile,
                        child,
                        objectTemplateMatrix,
                        sceneCache,
                        activeScenes,
                        handledSubScenes,
                        templateCache,
                        templateInProgress,
                        spawns,
                        token);
                }
            }
        }

        private void TryCollectNestedTemplateSpawns(
            string sceneFile,
            Matrix4 instanceMatrix,
            SceneChunk templateChunk,
            Dictionary<string, List<TemplateModelSpawn>> templateCache,
            HashSet<string> templateInProgress,
            List<TemplateModelSpawn> spawns,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            trinity_ObjectTemplate? ot;
            try
            {
                ot = FlatBufferConverter.DeserializeFrom<trinity_ObjectTemplate>(templateChunk.Data);
            }
            catch
            {
                return;
            }

            if (ot == null || !SceneTransformMath.ShouldLoadObjectTemplateFile(ot))
            {
                return;
            }

            var resolved = ResolveSceneReference(sceneFile, ot.FilePath);
            if (resolved == null)
            {
                return;
            }

            var templateSpawns = GetTemplateSpawns(resolved, templateCache, templateInProgress, token);
            if (templateSpawns.Count == 0)
            {
                return;
            }

            SceneTransformMath.TryBuildObjectTemplateInstanceMatrix(
                ot,
                instanceMatrix,
                out var objectTemplateInstanceMatrix,
                out _);

            if (SceneDiagnosticsMatchesTarget(null, resolved))
            {
                MessageHandler.Instance.AddMessage(
                    MessageType.LOG,
                    $"[Scene][TargetTpl] nestedTemplate='{resolved}' count={templateSpawns.Count}");
            }

            foreach (var t in templateSpawns)
            {
                token.ThrowIfCancellationRequested();
                spawns.Add(new TemplateModelSpawn
                {
                    SceneObjectName = t.SceneObjectName,
                    ModelPath = t.ModelPath,
                    LocalMatrix = t.LocalMatrix * objectTemplateInstanceMatrix
                });
            }
        }

        private void TryCollectRootTemplateSpawns(
            string sceneFile,
            Matrix4 instanceMatrix,
            SceneChunk templateChunk,
            Dictionary<string, List<TemplateModelSpawn>> templateCache,
            HashSet<string> templateInProgress,
            List<TemplateModelSpawn> spawns,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            trinity_ObjectTemplate? ot;
            try
            {
                ot = FlatBufferConverter.DeserializeFrom<trinity_ObjectTemplate>(templateChunk.Data);
            }
            catch
            {
                return;
            }

            if (ot == null || !SceneTransformMath.ShouldLoadObjectTemplateFile(ot))
            {
                return;
            }

            var resolved = ResolveSceneReference(sceneFile, ot.FilePath);
            if (resolved == null)
            {
                return;
            }

            var templateSpawns = GetTemplateSpawns(resolved, templateCache, templateInProgress, token);
            if (templateSpawns.Count == 0)
            {
                return;
            }

            SceneTransformMath.TryBuildObjectTemplateInstanceMatrix(
                ot,
                instanceMatrix,
                out var objectTemplateInstanceMatrix,
                out _);

            foreach (var t in templateSpawns)
            {
                token.ThrowIfCancellationRequested();
                spawns.Add(new TemplateModelSpawn
                {
                    SceneObjectName = t.SceneObjectName,
                    ModelPath = t.ModelPath,
                    LocalMatrix = t.LocalMatrix * objectTemplateInstanceMatrix
                });
            }
        }
    }
}
