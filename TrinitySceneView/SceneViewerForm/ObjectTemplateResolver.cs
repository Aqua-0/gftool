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

            if (ot == null || string.IsNullOrWhiteSpace(ot.FilePath))
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

            foreach (var t in templateSpawns)
            {
                token.ThrowIfCancellationRequested();
                spawns.Add(new SceneModelSpawn
                {
                    SceneFile = sceneFile,
                    SceneObjectName = string.IsNullOrWhiteSpace(t.SceneObjectName) ? parentSceneObjectName : t.SceneObjectName,
                    ModelPath = t.ModelPath,
                    ModelMatrix = instanceMatrix * t.LocalMatrix
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
                    TRSCN t = FlatBufferConverter.DeserializeFrom<TRSCN>(templateFile);
                    var loadedScenes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var spawns = new List<TemplateModelSpawn>();
                    CollectTemplateSpawnsRecursive(templateFile, t, Matrix4.Identity, loadedScenes, templateCache, templateInProgress, spawns, token);
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
            HashSet<string> loadedScenes,
            Dictionary<string, List<TemplateModelSpawn>> templateCache,
            HashSet<string> templateInProgress,
            List<TemplateModelSpawn> spawns,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            if (!loadedScenes.Add(sceneFile))
            {
                return;
            }

            if (trscn.Chunks == null)
            {
                return;
            }

            foreach (var chunk in trscn.Chunks)
            {
                CollectTemplateSpawnsFromChunk(sceneFile, chunk, parentMatrix, loadedScenes, templateCache, templateInProgress, spawns, token);
            }
        }

        private void CollectTemplateSpawnsFromChunk(
            string sceneFile,
            SceneChunk chunk,
            Matrix4 parentMatrix,
            HashSet<string> loadedScenes,
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
                    var sub = FlatBufferConverter.DeserializeFrom<SubScene>(chunk.Data);
                    if (!string.IsNullOrWhiteSpace(sub.Filepath))
                    {
                        var resolved = ResolveSceneReference(sceneFile, sub.Filepath);
                        if (resolved != null)
                        {
                            var subScn = FlatBufferConverter.DeserializeFrom<TRSCN>(resolved);
                            CollectTemplateSpawnsRecursive(resolved, subScn, parentMatrix, loadedScenes, templateCache, templateInProgress, spawns, token);
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
                TryCollectTemplateSceneObjectSpawns(sceneFile, chunk, parentMatrix, templateCache, templateInProgress, spawns, token);
            }
            else if (chunk.Type == nameof(trinity_ObjectTemplate))
            {
                // Some templates (including NPC object templates) store ObjectTemplates at the root.
                // Follow them so we can deterministically reach the underlying model/CC data.
                TryCollectRootTemplateSpawns(sceneFile, parentMatrix, chunk, templateCache, templateInProgress, spawns, token);
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
                    CollectTemplateSpawnsFromChunk(sceneFile, child, parentMatrix, loadedScenes, templateCache, templateInProgress, spawns, token);
                }
            }
        }

        private void TryCollectTemplateSceneObjectSpawns(
            string sceneFile,
            SceneChunk chunk,
            Matrix4 parentMatrix,
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

            var sceneObjectMatrix = parentMatrix * BuildSrtMatrix(sceneObject.Srt);

            foreach (var child in chunk.Children)
            {
                if (child?.Type != nameof(trinity_ModelComponent))
                {
                    if (child?.Type == nameof(trinity_ObjectTemplate))
                    {
                        TryCollectNestedTemplateSpawns(sceneFile, sceneObjectMatrix, child, templateCache, templateInProgress, spawns, token);
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

            if (ot == null || string.IsNullOrWhiteSpace(ot.FilePath))
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

            foreach (var t in templateSpawns)
            {
                token.ThrowIfCancellationRequested();
                spawns.Add(new TemplateModelSpawn
                {
                    SceneObjectName = t.SceneObjectName,
                    ModelPath = t.ModelPath,
                    LocalMatrix = instanceMatrix * t.LocalMatrix
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

            if (ot == null || string.IsNullOrWhiteSpace(ot.FilePath))
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

            foreach (var t in templateSpawns)
            {
                token.ThrowIfCancellationRequested();
                spawns.Add(new TemplateModelSpawn
                {
                    SceneObjectName = t.SceneObjectName,
                    ModelPath = t.ModelPath,
                    LocalMatrix = instanceMatrix * t.LocalMatrix
                });
            }
        }
    }
}
