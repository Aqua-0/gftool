using GFTool.Core.Flatbuffers.Customization;
using GFTool.Core.Flatbuffers.TR.Scene;
using GFTool.Core.Flatbuffers.TR.Scene.Components;
using GFTool.Renderer.Core;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Trinity.Core.Utils;

namespace TrinitySceneView
{
    public partial class SceneViewerForm
    {
        private void TryCollectCharacterCreationSpawns(
            string sceneFile,
            string? sceneObjectName,
            Matrix4 sceneObjectMatrix,
            SceneChunk ccChunk,
            Dictionary<string, object?> ccdatamCache,
            Dictionary<string, object?> ccdataCache,
            Dictionary<string, List<string>> ccModelsCache,
            List<SceneModelSpawn> spawns,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            trinity_CharacterCreationMasterComponent? cc;
            try
            {
                cc = FlatBufferConverter.DeserializeFrom<trinity_CharacterCreationMasterComponent>(ccChunk.Data);
            }
            catch
            {
                return;
            }

            if (cc?.ccdataMasterList == null || cc.ccdataMasterList.Length == 0)
            {
                return;
            }

            foreach (var entry in cc.ccdataMasterList)
            {
                token.ThrowIfCancellationRequested();

                var modelFiles = ResolveCharacterCreationModels(sceneFile, entry, ccdatamCache, ccdataCache, ccModelsCache, token);
                if (modelFiles.Count == 0)
                {
                    MessageHandler.Instance.AddMessage(
                        MessageType.LOG,
                        $"[Scene] CC resolved 0 models (label={entry.Name ?? "(null)"}, ccdatam={entry.File ?? "(null)"}, SceneObject={sceneObjectName ?? "(null)"})");
                }
                foreach (var modelFile in modelFiles)
                {
                    spawns.Add(new SceneModelSpawn
                    {
                        SceneFile = sceneFile,
                        SceneObjectName = sceneObjectName,
                        ModelPath = modelFile,
                        ModelMatrix = sceneObjectMatrix
                    });
                }
            }
        }

        private void TryCollectTemplateCharacterCreationSpawns(
            string sceneFile,
            Matrix4 sceneObjectMatrix,
            string? sceneObjectName,
            SceneChunk ccChunk,
            List<TemplateModelSpawn> spawns,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            trinity_CharacterCreationMasterComponent? cc;
            try
            {
                cc = FlatBufferConverter.DeserializeFrom<trinity_CharacterCreationMasterComponent>(ccChunk.Data);
            }
            catch
            {
                return;
            }

            if (cc?.ccdataMasterList == null || cc.ccdataMasterList.Length == 0)
            {
                return;
            }

            var ccdatamCache = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            var ccdataCache = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            var ccModelsCache = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in cc.ccdataMasterList)
            {
                token.ThrowIfCancellationRequested();

                var modelFiles = ResolveCharacterCreationModels(sceneFile, entry, ccdatamCache, ccdataCache, ccModelsCache, token);
                foreach (var modelFile in modelFiles)
                {
                    spawns.Add(new TemplateModelSpawn
                    {
                        SceneObjectName = sceneObjectName,
                        ModelPath = modelFile,
                        LocalMatrix = sceneObjectMatrix
                    });
                }
            }
        }

        private IReadOnlyList<string> ResolveCharacterCreationModels(
            string sceneFile,
            CcdataMasterEntry entry,
            Dictionary<string, object?> ccdatamCache,
            Dictionary<string, object?> ccdataCache,
            Dictionary<string, List<string>> ccModelsCache,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            if (entry == null || string.IsNullOrWhiteSpace(entry.File))
            {
                return Array.Empty<string>();
            }

            var ccdatamAbs = ResolveModelPath(entry.File) ?? ResolveSceneReference(sceneFile, entry.File);
            if (ccdatamAbs == null)
            {
                MessageHandler.Instance.AddMessage(
                    MessageType.WARNING,
                    $"[Scene] CC missing ccdatam: {entry.File} (SceneObject={entry.Name ?? "(null)"}, scene={Path.GetFileName(sceneFile)})");
                return Array.Empty<string>();
            }

            string cacheKey = $"{ccdatamAbs}||{entry.Name}";
            if (ccModelsCache.TryGetValue(cacheKey, out var cachedModels))
            {
                return cachedModels;
            }

            var label = entry.Name ?? "";

            string? ccdataRel = null;
            var zaCcdatam = GetOrLoadZaCcdatam(ccdatamAbs, ccdatamCache);
            if (zaCcdatam?.CcdataList != null && zaCcdatam.CcdataList.Length > 0)
            {
                var unit = zaCcdatam.CcdataList.FirstOrDefault(x => string.Equals(x?.Label, label, StringComparison.Ordinal))
                    ?? zaCcdatam.CcdataList.FirstOrDefault();
                ccdataRel = unit?.CcdataPath;
            }
            else
            {
                var svCcdatam = GetOrLoadSvCcdatam(ccdatamAbs, ccdatamCache);
                if (svCcdatam?.Entries != null && svCcdatam.Entries.Length > 0)
                {
                    var unit = svCcdatam.Entries.FirstOrDefault(x => string.Equals(x?.Name, label, StringComparison.Ordinal))
                        ?? svCcdatam.Entries.FirstOrDefault();
                    ccdataRel = unit?.CcdataFile;
                }
            }

            if (string.IsNullOrWhiteSpace(ccdataRel))
            {
                MessageHandler.Instance.AddMessage(
                    MessageType.WARNING,
                    $"[Scene] CC ccdatam parsed but no entry matched (label={label}, file={Path.GetFileName(ccdatamAbs)})");
                ccModelsCache[cacheKey] = new List<string>();
                return ccModelsCache[cacheKey];
            }

            var ccdataAbs =
                ResolveModelPath(ccdataRel) ??
                ResolveSceneReference(sceneFile, ccdataRel) ??
                ResolveModelPath(TryRemapCcdataPath(ccdataRel)) ??
                ResolveSceneReference(sceneFile, TryRemapCcdataPath(ccdataRel));
            if (ccdataAbs == null)
            {
                MessageHandler.Instance.AddMessage(
                    MessageType.WARNING,
                    $"[Scene] CC missing ccdata: {ccdataRel} (ccdatam={Path.GetFileName(ccdatamAbs)})");
                ccModelsCache[cacheKey] = new List<string>();
                return ccModelsCache[cacheKey];
            }

            List<string> models;

            var zaCcdata = GetOrLoadZaCcdata(ccdataAbs, ccdataCache);
            if (zaCcdata?.PartsList != null && zaCcdata.PartsList.Length > 0)
            {
                models = zaCcdata.PartsList
                    .Select(x => x?.TrmdlFilePath)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(TryResolveCcTrmdlPath)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()!;
            }
            else
            {
                var svCcdata = GetOrLoadSvCcdata(ccdataAbs, ccdataCache);
                if (svCcdata?.Entries == null || svCcdata.Entries.Length == 0)
                {
                    MessageHandler.Instance.AddMessage(
                        MessageType.WARNING,
                        $"[Scene] CC failed to parse ccdata: {Path.GetFileName(ccdataAbs)}");
                    ccModelsCache[cacheKey] = new List<string>();
                    return ccModelsCache[cacheKey];
                }

                models = svCcdata.Entries
                    .Select(x => x?.ModelFile)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(TryResolveCcTrmdlPath)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()!;
            }

            ccModelsCache[cacheKey] = models;
            return models;
        }

        private string TryResolveCcTrmdlPath(string? trmdlPath)
        {
            if (string.IsNullOrWhiteSpace(trmdlPath))
            {
                return string.Empty;
            }

            foreach (var candidate in ExpandCcTrmdlPathVariants(trmdlPath))
            {
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    continue;
                }

                if (ResolveModelPath(candidate) != null)
                {
                    return candidate;
                }
            }

            // Fall back to the raw path so the caller can log "missing model file" with the original string.
            return trmdlPath;
        }

        private static IEnumerable<string> ExpandCcTrmdlPathVariants(string trmdlPath)
        {
            yield return trmdlPath;

            var p = trmdlPath.Replace('\\', '/');
            while (p.StartsWith("../", StringComparison.Ordinal))
            {
                p = p.Substring(3);
            }

            yield return p;

            if (p.StartsWith("_model_uq/", StringComparison.OrdinalIgnoreCase) ||
                p.StartsWith("model_uq/", StringComparison.OrdinalIgnoreCase))
            {
                var rest = p.Substring(p.IndexOf('/') + 1);
                yield return "ik_chara/model_uq/" + rest;
            }

            if (p.StartsWith("_model_cc_vr/", StringComparison.OrdinalIgnoreCase) ||
                p.StartsWith("model_cc_vr/", StringComparison.OrdinalIgnoreCase))
            {
                var rest = p.Substring(p.IndexOf('/') + 1);
                yield return "ik_chara/model_cc_vr/" + rest;
            }

            if (p.StartsWith("_model_cc_base/", StringComparison.OrdinalIgnoreCase) ||
                p.StartsWith("model_cc_base/", StringComparison.OrdinalIgnoreCase))
            {
                var rest = p.Substring(p.IndexOf('/') + 1);
                yield return "ik_chara/model_cc_base/" + rest;
            }

            if (p.StartsWith("_model_cc_ir/", StringComparison.OrdinalIgnoreCase) ||
                p.StartsWith("model_cc_ir/", StringComparison.OrdinalIgnoreCase))
            {
                var rest = p.Substring(p.IndexOf('/') + 1);
                yield return "ik_chara/model_cc_ir/" + rest;
            }

            if (p.StartsWith("_model_ob/", StringComparison.OrdinalIgnoreCase) ||
                p.StartsWith("model_ob/", StringComparison.OrdinalIgnoreCase))
            {
                var rest = p.Substring(p.IndexOf('/') + 1);
                var remapped = "ik_chara/model_ob/" + rest;
                yield return remapped;

                // Some character models live under an "_g" folder variant.
                // If the folder is "obXXXX_YY", also try "obXXXX_YY_g".
                var parts = remapped.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3)
                {
                    var folder = parts[2];
                    if (!folder.EndsWith("_g", StringComparison.OrdinalIgnoreCase))
                    {
                        parts[2] = folder + "_g";
                        yield return string.Join("/", parts);
                    }
                }
            }

            if (p.StartsWith("ik_chara/model_ob/", StringComparison.OrdinalIgnoreCase))
            {
                var parts = p.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3)
                {
                    var folder = parts[2];
                    if (!folder.EndsWith("_g", StringComparison.OrdinalIgnoreCase))
                    {
                        parts[2] = folder + "_g";
                        yield return string.Join("/", parts);
                    }
                }
            }
        }

        private static string TryRemapCcdataPath(string ccdataRel)
        {
            if (string.IsNullOrWhiteSpace(ccdataRel))
            {
                return ccdataRel;
            }

            var p = ccdataRel.Replace('\\', '/');
            while (p.StartsWith("../", StringComparison.Ordinal))
            {
                p = p.Substring(3);
            }

            if (p.StartsWith("_sav_uq/", StringComparison.OrdinalIgnoreCase) ||
                p.StartsWith("sav_uq/", StringComparison.OrdinalIgnoreCase))
            {
                var rest = p.Substring(p.IndexOf('/') + 1);
                return "ik_chara/sav_uq/" + rest;
            }

            if (p.StartsWith("_sav_cc_vr/", StringComparison.OrdinalIgnoreCase) ||
                p.StartsWith("sav_cc_vr/", StringComparison.OrdinalIgnoreCase))
            {
                var rest = p.Substring(p.IndexOf('/') + 1);
                return "ik_chara/sav_cc_vr/" + rest;
            }

            if (p.StartsWith("_sav_cc_base/", StringComparison.OrdinalIgnoreCase) ||
                p.StartsWith("sav_cc_base/", StringComparison.OrdinalIgnoreCase))
            {
                var rest = p.Substring(p.IndexOf('/') + 1);
                return "ik_chara/sav_cc_base/" + rest;
            }

            if (p.StartsWith("_sav_cc_ir/", StringComparison.OrdinalIgnoreCase) ||
                p.StartsWith("sav_cc_ir/", StringComparison.OrdinalIgnoreCase))
            {
                var rest = p.Substring(p.IndexOf('/') + 1);
                return "ik_chara/sav_cc_ir/" + rest;
            }

            if (p.StartsWith("_sav_ob/", StringComparison.OrdinalIgnoreCase) ||
                p.StartsWith("sav_ob/", StringComparison.OrdinalIgnoreCase))
            {
                var rest = p.Substring(p.IndexOf('/') + 1);
                return "ik_chara/sav_ob/" + rest;
            }

            if (p.StartsWith("_savm_ob/", StringComparison.OrdinalIgnoreCase) ||
                p.StartsWith("savm_ob/", StringComparison.OrdinalIgnoreCase))
            {
                var rest = p.Substring(p.IndexOf('/') + 1);
                return "ik_chara/savm_ob/" + rest;
            }

            return ccdataRel;
        }

        private static ZaCcDatam? GetOrLoadZaCcdatam(
            string ccdatamAbs,
            Dictionary<string, object?> ccdatamCache)
        {
            if (ccdatamCache.TryGetValue(ccdatamAbs, out var cached))
            {
                return cached as ZaCcDatam;
            }

            try
            {
                cached = FlatBufferConverter.DeserializeFrom<ZaCcDatam>(ccdatamAbs);
            }
            catch
            {
                cached = null;
            }

            ccdatamCache[ccdatamAbs] = cached;
            return cached as ZaCcDatam;
        }

        private static CharacterCreationDataMaster? GetOrLoadSvCcdatam(
            string ccdatamAbs,
            Dictionary<string, object?> ccdatamCache)
        {
            if (ccdatamCache.TryGetValue(ccdatamAbs, out var cached))
            {
                return cached as CharacterCreationDataMaster;
            }

            try
            {
                cached = FlatBufferConverter.DeserializeFrom<CharacterCreationDataMaster>(ccdatamAbs);
            }
            catch
            {
                cached = null;
            }

            ccdatamCache[ccdatamAbs] = cached;
            return cached as CharacterCreationDataMaster;
        }

        private static ZaCcData? GetOrLoadZaCcdata(
            string ccdataAbs,
            Dictionary<string, object?> ccdataCache)
        {
            if (ccdataCache.TryGetValue(ccdataAbs, out var cached))
            {
                return cached as ZaCcData;
            }

            try
            {
                cached = FlatBufferConverter.DeserializeFrom<ZaCcData>(ccdataAbs);
            }
            catch
            {
                cached = null;
            }

            ccdataCache[ccdataAbs] = cached;
            return cached as ZaCcData;
        }

        private static CharacterCreationData? GetOrLoadSvCcdata(
            string ccdataAbs,
            Dictionary<string, object?> ccdataCache)
        {
            if (ccdataCache.TryGetValue(ccdataAbs, out var cached))
            {
                return cached as CharacterCreationData;
            }

            try
            {
                cached = FlatBufferConverter.DeserializeFrom<CharacterCreationData>(ccdataAbs);
            }
            catch
            {
                cached = null;
            }

            ccdataCache[ccdataAbs] = cached;
            return cached as CharacterCreationData;
        }
    }
}
