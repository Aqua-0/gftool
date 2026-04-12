using GFTool.Renderer.Core;
using GFTool.Core.Flatbuffers.TR.Scene;
using GFTool.Core.Flatbuffers.TR.Scene.Components;
using GFTool.Renderer.Scene.GraphicsObjects;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Titan.Resource;
using Trinity.Core.Utils;

namespace TrinitySceneView
{
    public partial class SceneViewerForm
    {
        private readonly object pokemonCatalogGate = new();
        private string? cachedPokemonCatalogAbs;
        private CatalogEntry[]? cachedPokemonCatalogTable;

        private async Task SpawnSelectedEventActorsAsync()
        {
            if (!npcTabActorsMode)
            {
                return;
            }

            var ids = spawnerCandidatesListView.SelectedItems
                .Cast<ListViewItem>()
                .Select(i => i.Tag as string)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s!)
                .ToList();

            if (ids.Count == 0)
            {
                return;
            }

            foreach (var id in ids)
            {
                await SpawnEventActorAsync(id);
            }

            RefreshNpcActorsFromEventTimeline();
            ApplyEventSimulationToRenderer();
        }

        private async Task SpawnAllEventActorsAsync()
        {
            if (!npcTabActorsMode)
            {
                return;
            }

            var ids = spawnerCandidatesListView.Items
                .Cast<ListViewItem>()
                .Select(i => i.Tag as string)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s!)
                .Where(s => ClassifyEventActorKind(s) == "NPC")
                .ToList();

            foreach (var id in ids)
            {
                await SpawnEventActorAsync(id);
            }

            RefreshNpcActorsFromEventTimeline();
            ApplyEventSimulationToRenderer();
        }

        private async Task SpawnEventActorAsync(string actorId)
        {
            if (isSceneLoading)
            {
                MessageHandler.Instance.AddMessage(MessageType.WARNING, "[Event] Wait for the current load to finish before spawning.");
                return;
            }

            if (renderCtrl?.renderer == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(assetRoot) || !Directory.Exists(assetRoot))
            {
                MessageHandler.Instance.AddMessage(MessageType.WARNING, "[Event] Asset root not set; use File -> Set Asset Root... first.");
                return;
            }

            if (string.Equals(actorId, "Player", StringComparison.Ordinal))
            {
                MessageHandler.Instance.AddMessage(MessageType.LOG, "[Event] 'Player' spawning not implemented.");
                return;
            }

            if (eventActorModels.ContainsKey(actorId))
            {
                return;
            }

            if (TryGetEventActorPokemonSpawnInfo(actorId, out var pokemonSpecies, out var pokemonForm, out var pokemonGender, out var pokePos, out var pokeRotY))
            {
                await SpawnEventPokemonAsync(actorId, pokemonSpecies, pokemonForm, pokemonGender, pokePos, pokeRotY);
                return;
            }

            if (npcSpawnerDb == null)
            {
                MessageHandler.Instance.AddMessage(MessageType.WARNING, "[Event] Load a scene first (NPC DB not available).");
                return;
            }

            EnsureNpcSpawnerDbLoaded(npcSpawnerDb);

            if (!TryResolveEventActorToNpcAssetId(actorId, out var npcAssetId))
            {
                if (TryGetEventActorPokemonNo(actorId, out var pokemonNoFallback))
                {
                    MessageHandler.Instance.AddMessage(MessageType.WARNING, $"[Event] Actor '{actorId}' is a Pokemon (Create_NpcPokemon no={pokemonNoFallback}), but no spawn info was found yet.");
                }
                else
                {
                    MessageHandler.Instance.AddMessage(MessageType.WARNING, $"[Event] Actor '{actorId}' has no NPC asset mapping yet (need Create_Npc_Dynamic or npc_* id).");
                }
                return;
            }

            if (!TryResolveNpcTemplateForAssetId(npcAssetId, out var templateAbs))
            {
                MessageHandler.Instance.AddMessage(MessageType.WARNING, $"[Event] No NPC template for actor '{actorId}' (npcAssetId='{npcAssetId}').");
                return;
            }

            Matrix4 baseMatrix;
            if (eventActorStates.TryGetValue(actorId, out var state))
            {
                baseMatrix = BuildTransformMatrix(state.Position, state.RotationDegrees);
            }
            else
            {
                baseMatrix = TryGetBaseMatrixForNpcAssetId(npcAssetId);
            }

            var templateCache = new Dictionary<string, List<TemplateModelSpawn>>(StringComparer.OrdinalIgnoreCase);
            var templateInProgress = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<TemplateModelSpawn> templateSpawns;
            try
            {
                templateSpawns = GetTemplateSpawns(templateAbs, templateCache, templateInProgress, CancellationToken.None);
            }
            catch
            {
                templateSpawns = new List<TemplateModelSpawn>();
            }

            if (templateSpawns.Count == 0)
            {
                MessageHandler.Instance.AddMessage(MessageType.WARNING, $"[Event] Actor '{actorId}' template produced no model spawns. npcAssetId='{npcAssetId}' template='{Path.GetFileName(templateAbs)}'");
                TryLogTemplateSpawnTrace(actorId, npcAssetId, templateAbs);
                return;
            }

            eventActorSourceModelRels[actorId] = templateSpawns
                .Select(s => s.ModelPath ?? string.Empty)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();
            if (!eventActorMotionDirRels.ContainsKey(actorId))
            {
                foreach (var rel in eventActorSourceModelRels[actorId])
                {
                    if (TryInferMotionDirRelFromModelRel(rel, out var motionRel))
                    {
                        eventActorMotionDirRels[actorId] = motionRel;
                        break;
                    }
                }
            }

            int total = templateSpawns.Count;
            int completed = 0;
            int myLoadVersion = Interlocked.Increment(ref sceneLoadVersion);
            BeginSceneLoadUi(myLoadVersion, $"Spawning '{actorId}' ({total} model(s))...");

            var spawned = new List<EventActorModel>(total);
            try
            {
                foreach (var spawn in templateSpawns)
                {
                    completed++;
                    var resolved = ResolveModelPath(spawn.ModelPath);
                    if (resolved == null)
                    {
                        ReportSceneLoadUi(myLoadVersion, (int)(completed * 100.0 / Math.Max(1, total)), $"Missing ({completed}/{total})");
                        MessageHandler.Instance.AddMessage(MessageType.WARNING, $"[Event] Missing model file: {spawn.ModelPath}");
                        continue;
                    }

                    var progress = new Progress<float>(p =>
                    {
                        int percent = (int)(((completed - 1) + p) * 100.0 / Math.Max(1, total));
                        ReportSceneLoadUi(myLoadVersion, percent, $"Spawning {completed}/{total}: {Path.GetFileNameWithoutExtension(resolved)}");
                    });

                    Model model;
                    try
                    {
                        model = await renderCtrl.renderer.AddSceneModelAsync(resolved, token: CancellationToken.None, progress: progress);
                    }
                    catch (Exception ex)
                    {
                        MessageHandler.Instance.AddMessage(
                            MessageType.WARNING,
                            $"[Event] Failed to load model '{resolved}' (from '{spawn.ModelPath}'): {ex.GetType().Name}: {ex.Message}");
                        continue;
                    }

                    var mat = ApplyViewerMatrixOptions(
                        baseMatrix * spawn.LocalMatrix,
                        config.SpawnModelsAtOrigin,
                        config.ApplySceneRotationToActors && config.RotateModels180X,
                        config.ApplySceneRotationToActors && config.RotateModels180Y,
                        out _,
                        out _);
                    if (config.RotateActors180X)
                    {
                        mat = mat * Matrix4.CreateRotationX(MathHelper.Pi);
                    }
                    if (config.RotateActors180Y)
                    {
                        mat = mat * Matrix4.CreateRotationY(MathHelper.Pi);
                    }
                    model.SetModelMatrix(mat);
                    AddModelToList(actorId, spawn.ModelPath, model);
                    spawned.Add(new EventActorModel(model, spawn.LocalMatrix));
                }

                eventActorModels[actorId] = spawned;

                if (!eventActorStates.TryGetValue(actorId, out var s))
                {
                    s = new EventActorState();
                    eventActorStates[actorId] = s;
                }
                if (s.Position == Vector3.Zero)
                {
                    s.Position = new Vector3(baseMatrix.M41, baseMatrix.M42, baseMatrix.M43);
                }

                EndSceneLoadUi(myLoadVersion, $"Spawned '{actorId}' ({spawned.Count}/{total}).");
            }
            catch (Exception ex)
            {
                EndSceneLoadUi(myLoadVersion, "Spawn failed.");
                MessageHandler.Instance.AddMessage(MessageType.ERROR, $"[Event] Spawn failed: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                renderCtrl.Invalidate();
            }
        }

        private bool TryResolveNpcTemplateForAssetId(string assetId, out string templateAbs)
        {
            templateAbs = string.Empty;
            if (npcSpawnerDb == null)
            {
                return false;
            }

            if (!npcSpawnerDb.AssetIdToObjectTemplateIds.TryGetValue(assetId, out var objectTemplateIds) || objectTemplateIds.Count == 0)
            {
                return false;
            }

            foreach (var objectTemplateId in objectTemplateIds)
            {
                if (!npcSpawnerDb.ObjectTemplateIdToPath.TryGetValue(objectTemplateId, out var templateRel) || string.IsNullOrWhiteSpace(templateRel))
                {
                    continue;
                }

                var abs = ResolveAssetReferenceWithVariants(templateRel);
                if (abs == null)
                {
                    continue;
                }

                templateAbs = abs;
                return true;
            }

            return false;
        }

        private Matrix4 TryGetBaseMatrixForNpcAssetId(string assetId)
        {
            if (npcSpawnerDb == null)
            {
                return Matrix4.Identity;
            }

            // Prefer a spawner in the currently loaded scene that maps deterministically to this assetId.
            var sceneSpawners = GetNpcSpawnerIdsForCurrentScene();
            if (sceneSpawners.Count == 0)
            {
                return Matrix4.Identity;
            }

            var matches = new List<string>();
            foreach (var spawnerId in sceneSpawners)
            {
                if (!npcSpawnerDb.SpawnerObjectNameToCandidates.TryGetValue(spawnerId, out var candidates) || candidates.Count == 0)
                {
                    continue;
                }

                if (candidates.Any(c => string.Equals(c.AssetId, assetId, StringComparison.Ordinal)))
                {
                    matches.Add(spawnerId);
                }
            }

            if (matches.Count == 0)
            {
                return Matrix4.Identity;
            }

            matches.Sort(StringComparer.OrdinalIgnoreCase);
            var chosen = NormalizeSpawnerId(matches[0]);

            if (TryGetByVariants(npcSpawnerDb.SpawnerTransforms, chosen, NormalizeSpawnerId(chosen), out var t))
            {
                return BuildSpawnerTransformMatrix(t);
            }

            return Matrix4.Identity;
        }

        private async Task SpawnEventPokemonAsync(string actorId, int pokemonSpecies, int pokemonForm, int pokemonGender, Vector3 pos, float rotY)
        {
            if (renderCtrl?.renderer == null)
            {
                return;
            }

            // Prefer the catalog mapping (species -> model path). This is required when internal species ids
            // don't match the pmNNNN folder number.
            string rel;
            string? catalogReason = null;
            if (!TryResolvePokemonTrmdlRelFromCatalog(pokemonSpecies, pokemonForm, pokemonGender, out rel, out catalogReason))
            {
                // Fallback: best-effort inference for common cases where speciesNo == pm folder number.
                var pm = $"pm{pokemonSpecies:0000}";
                var baseName = $"{pm}_00_00";
                rel = $"ik_pokemon/data/{pm}/{baseName}/{baseName}.trmdl";
            }

            var abs = ResolveModelPath(rel);
            if (abs == null)
            {
                if (!string.IsNullOrWhiteSpace(catalogReason))
                {
                    MessageHandler.Instance.AddMessage(MessageType.LOG, $"[Event] Pokemon catalog: {catalogReason}");
                }
                MessageHandler.Instance.AddMessage(
                    MessageType.WARNING,
                    $"[Event] Pokemon actor '{actorId}' needs '{rel}', but it wasn't found under the current asset root.");
                return;
            }

            var myLoadVersion = Interlocked.Increment(ref sceneLoadVersion);
            BeginSceneLoadUi(myLoadVersion, $"Spawning '{actorId}' (species={pokemonSpecies})...");

            try
            {
                var model = await renderCtrl.renderer.AddSceneModelAsync(abs, token: CancellationToken.None);
                var baseMat = BuildTransformMatrix(pos, new Vector3(0, rotY, 0));
                var mat = ApplyViewerMatrixOptions(
                    baseMat,
                    config.SpawnModelsAtOrigin,
                    config.ApplySceneRotationToActors && config.RotateModels180X,
                    config.ApplySceneRotationToActors && config.RotateModels180Y,
                    out _,
                    out _);
                if (config.RotateActors180X)
                {
                    mat = mat * Matrix4.CreateRotationX(MathHelper.Pi);
                }
                if (config.RotateActors180Y)
                {
                    mat = mat * Matrix4.CreateRotationY(MathHelper.Pi);
                }
                model.SetModelMatrix(mat);
                AddModelToList(actorId, rel, model);
                eventActorModels[actorId] = new List<EventActorModel> { new EventActorModel(model, Matrix4.Identity) };
                EndSceneLoadUi(myLoadVersion, $"Spawned '{actorId}' (species={pokemonSpecies}).");
            }
            catch (Exception ex)
            {
                EndSceneLoadUi(myLoadVersion, "Spawn failed.");
                MessageHandler.Instance.AddMessage(MessageType.WARNING, $"[Event] Pokemon spawn failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private bool TryResolvePokemonTrmdlRelFromCatalog(int pokemonSpecies, int pokemonForm, int pokemonGender, out string relTrmdl, out string? reason)
        {
            relTrmdl = string.Empty;
            reason = null;

            var table = GetPokemonCatalogTable(out var catalogAbs);
            if (table == null || table.Length == 0)
            {
                reason = $"Catalog missing/unreadable: '{catalogAbs ?? "ik_pokemon/catalog/catalog/poke_resource_table.trpmcatalog"}'";
                return false;
            }

            var candidates = table
                .Where(e => e?.SpeciesInfo != null && e.SpeciesInfo.Species == pokemonSpecies)
                .ToList();
            if (candidates.Count == 0)
            {
                reason = $"No catalog entry for species={pokemonSpecies}.";
                return false;
            }

            CatalogEntry? chosen =
                candidates.FirstOrDefault(e => e.SpeciesInfo!.Form == pokemonForm && e.SpeciesInfo!.Gender == (byte)pokemonGender) ??
                candidates.FirstOrDefault(e => e.SpeciesInfo!.Form == pokemonForm) ??
                candidates[0];

            var modelPath = (chosen.ModelPath ?? string.Empty).Replace('\\', '/').TrimStart('/');
            if (string.IsNullOrWhiteSpace(modelPath))
            {
                reason = $"Catalog entry has empty model_path for species={pokemonSpecies} (form={pokemonForm} gender={pokemonGender}).";
                return false;
            }

            // Most entries are like: "pm0721/pm0721_00_00/pm0721_00_00.trmdl".
            if (!modelPath.EndsWith(".trmdl", StringComparison.OrdinalIgnoreCase))
            {
                reason = $"Catalog model_path isn't a .trmdl: '{modelPath}'.";
                return false;
            }

            relTrmdl = modelPath.StartsWith("ik_pokemon/data/", StringComparison.OrdinalIgnoreCase)
                ? modelPath
                : $"ik_pokemon/data/{modelPath}";

            var si = chosen.SpeciesInfo!;
            reason = $"Resolved species={pokemonSpecies} (form={pokemonForm} gender={pokemonGender}) -> (form={si.Form} gender={si.Gender}) '{relTrmdl}'.";
            return true;
        }

        private CatalogEntry[]? GetPokemonCatalogTable(out string? catalogAbs)
        {
            const string catalogRel = "ik_pokemon/catalog/catalog/poke_resource_table.trpmcatalog";
            catalogAbs = ResolveModelPath(catalogRel);
            if (catalogAbs == null)
            {
                return null;
            }

            lock (pokemonCatalogGate)
            {
                if (cachedPokemonCatalogTable != null &&
                    !string.IsNullOrWhiteSpace(cachedPokemonCatalogAbs) &&
                    string.Equals(cachedPokemonCatalogAbs, catalogAbs, StringComparison.OrdinalIgnoreCase))
                {
                    return cachedPokemonCatalogTable;
                }

                try
                {
                    var doc = FlatBufferConverter.DeserializeFrom<Catalog>(catalogAbs);
                    cachedPokemonCatalogAbs = catalogAbs;
                    cachedPokemonCatalogTable = doc.Table ?? Array.Empty<CatalogEntry>();
                    return cachedPokemonCatalogTable;
                }
                catch (Exception ex)
                {
                    cachedPokemonCatalogAbs = catalogAbs;
                    cachedPokemonCatalogTable = null;
                    MessageHandler.Instance.AddMessage(MessageType.WARNING, $"[Event] Failed to parse pokemon catalog '{catalogAbs}': {ex.GetType().Name}: {ex.Message}");
                    return null;
                }
            }
        }

        private void TryLogTemplateSpawnTrace(string actorId, string npcAssetId, string templateAbs)
        {
            if (string.IsNullOrWhiteSpace(templateAbs))
            {
                return;
            }

            MessageHandler.Instance.AddMessage(
                MessageType.LOG,
                $"[Event] Spawn trace: actor='{actorId}' npcAssetId='{npcAssetId}' templateAbs='{templateAbs}' assetRoot='{assetRoot ?? "(null)"}'");

            TRSCN? trscn = null;
            try
            {
                trscn = FlatBufferConverter.DeserializeFrom<TRSCN>(templateAbs);
            }
            catch (Exception ex)
            {
                MessageHandler.Instance.AddMessage(MessageType.WARNING, $"[Event] Spawn trace: template parse failed: {ex.GetType().Name}: {ex.Message}");
                return;
            }

            int ccChunks = 0;
            int modelComponents = 0;
            int objectTemplates = 0;
            Walk(trscn?.Chunks);

            MessageHandler.Instance.AddMessage(
                MessageType.LOG,
                $"[Event] Spawn trace: template chunks: cc={ccChunks} modelComponents={modelComponents} objectTemplates={objectTemplates}");

            if (ccChunks > 0 && trscn != null)
            {
                TryLogCcResolution(templateAbs, trscn);
            }

            void Walk(SceneChunk[]? chunks)
            {
                if (chunks == null) return;
                foreach (var c in chunks)
                {
                    if (c == null) continue;
                    if (c.Type == nameof(trinity_CharacterCreationMasterComponent)) ccChunks++;
                    if (c.Type == nameof(trinity_ModelComponent)) modelComponents++;
                    if (c.Type == nameof(trinity_ObjectTemplate)) objectTemplates++;
                    if (c.Children != null && c.Children.Length > 0) Walk(c.Children);
                }
            }
        }

        private void TryLogCcResolution(string sceneFile, TRSCN trscn)
        {
            var ccChunks = new List<SceneChunk>();
            CollectCcChunks(trscn.Chunks, ccChunks);
            if (ccChunks.Count == 0)
            {
                return;
            }

            var ccdatamCache = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            var ccdataCache = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            var ccModelsCache = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            int shown = 0;
            foreach (var chunk in ccChunks)
            {
                if (chunk?.Data == null) continue;
                trinity_CharacterCreationMasterComponent? cc;
                try
                {
                    cc = FlatBufferConverter.DeserializeFrom<trinity_CharacterCreationMasterComponent>(chunk.Data);
                }
                catch
                {
                    continue;
                }

                if (cc?.ccdataMasterList == null) continue;
                foreach (var entry in cc.ccdataMasterList)
                {
                    if (entry == null) continue;
                    if (++shown > 3) return;

                    var models = ResolveCharacterCreationModels(sceneFile, entry, ccdatamCache, ccdataCache, ccModelsCache, CancellationToken.None);
                    var first = models.Count > 0 ? models[0] : "(none)";
                    MessageHandler.Instance.AddMessage(
                        MessageType.LOG,
                        $"[Event] Spawn trace: CC '{entry.Name ?? "(null)"}' ccdatam='{entry.File ?? "(null)"}' => models={models.Count} first='{first}'");
                }
            }
        }

        private static void CollectCcChunks(SceneChunk[]? chunks, List<SceneChunk> outList)
        {
            if (chunks == null) return;
            foreach (var c in chunks)
            {
                if (c == null) continue;
                if (c.Type == nameof(trinity_CharacterCreationMasterComponent))
                {
                    outList.Add(c);
                }
                if (c.Children != null && c.Children.Length > 0)
                {
                    CollectCcChunks(c.Children, outList);
                }
            }
        }

        private HashSet<string> GetNpcSpawnerIdsForCurrentScene()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (npcSpawnerDb == null || string.IsNullOrWhiteSpace(lastOpenedScenePath))
            {
                return set;
            }

            var rel = TryMakeAssetRelativePath(lastOpenedScenePath);
            if (rel == null)
            {
                return set;
            }

            foreach (var key in GetScenePathVariants(rel))
            {
                if (npcSpawnerDb.CreateScenePathToSpawnerObjectNames.TryGetValue(key, out var bucket) && bucket.Count > 0)
                {
                    foreach (var s in bucket)
                    {
                        if (!string.IsNullOrWhiteSpace(s)) set.Add(NormalizeSpawnerId(s));
                    }
                    break;
                }
            }

            foreach (var s in npcSpawnerDb.SpawnedSpawnerObjectNames)
            {
                if (!string.IsNullOrWhiteSpace(s)) set.Add(NormalizeSpawnerId(s));
            }

            return set;
        }
    }
}
