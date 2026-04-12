using GFTool.Core.Flatbuffers.TR.Scene;
using GFTool.Core.Flatbuffers.TR.Scene.Components;
using GFTool.Renderer.Core;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TrinitySceneView
{
    public partial class SceneViewerForm
    {
        private sealed record SceneNpcListItem(string SpawnerId, string? AssetId)
        {
            public override string ToString()
            {
                return string.IsNullOrWhiteSpace(AssetId) ? SpawnerId : $"{SpawnerId} -> {AssetId}";
            }
        }

        private sealed record SpawnerCandidateRow(
            string SpawnerId,
            NpcSpawnerCandidate Candidate,
            string ObjectTemplateId,
            string? TemplateRel,
            string? TemplateAbs
        );

        private void TryUpdateSpawnerFromSelection(SceneMetaData? meta)
        {
            if (meta == null)
            {
                return;
            }

            string? name = null;
            try
            {
                if (meta.Value.Data is trinity_SceneObject so)
                {
                    name = so.Name;
                }
                else if (meta.Value.Data is trinity_ObjectTemplate ot)
                {
                    name = string.IsNullOrWhiteSpace(ot.Name) ? ot.Scope : ot.Name;
                }
            }
            catch
            {
                // ignore
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            name = NormalizeSpawnerId(name);
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            selectedSpawnerId = name;
            spawnerLookupTextBox.Text = name;
            PopulateSpawnerCandidates(name);
        }

        private void btnLookupSpawner_Click(object sender, EventArgs e)
        {
            if (npcTabActorsMode)
            {
                _ = SpawnAllEventActorsAsync();
                return;
            }

            var spawnerId = spawnerLookupTextBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(spawnerId))
            {
                MessageHandler.Instance.AddMessage(MessageType.WARNING, "[Scene] Enter a spawner id (or a substring) to list candidates.");
                return;
            }

            selectedSpawnerId = NormalizeSpawnerId(spawnerId);
            spawnerLookupTextBox.Text = selectedSpawnerId;
            PopulateSpawnerCandidates(selectedSpawnerId);
        }

        private void btnListSceneSpawners_Click(object sender, EventArgs e)
        {
            if (npcTabActorsMode)
            {
                RefreshNpcActorsFromEventTimeline();
                return;
            }

            PopulateSceneNpcList(selectFirst: true, showWarnings: true);
        }

        private void sceneSpawnerComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            string? spawnerId = sceneSpawnerComboBox.SelectedItem switch
            {
                SceneNpcListItem item => item.SpawnerId,
                string s => s,
                _ => null
            };

            if (string.IsNullOrWhiteSpace(spawnerId))
            {
                return;
            }

            selectedSpawnerId = NormalizeSpawnerId(spawnerId);
            spawnerLookupTextBox.Text = selectedSpawnerId;
            PopulateSpawnerCandidates(selectedSpawnerId);
        }

        private void PopulateSceneNpcList(bool selectFirst, bool showWarnings)
        {
            if (npcSpawnerDb == null)
            {
                if (showWarnings)
                {
                    MessageHandler.Instance.AddMessage(MessageType.WARNING, "[Scene] Load a scene first.");
                }
                return;
            }

            if (string.IsNullOrWhiteSpace(lastOpenedScenePath))
            {
                if (showWarnings)
                {
                    MessageHandler.Instance.AddMessage(MessageType.WARNING, "[Scene] No scene path available.");
                }
                return;
            }

            EnsureNpcSpawnerDbLoaded(npcSpawnerDb);

            var rel = TryMakeAssetRelativePath(lastOpenedScenePath);
            if (rel == null)
            {
                if (showWarnings)
                {
                    MessageHandler.Instance.AddMessage(MessageType.WARNING, "[Scene] Could not compute scene relative path (check asset root).");
                }
                return;
            }

            List<string>? spawners = null;
            foreach (var key in GetScenePathVariants(rel))
            {
                if (npcSpawnerDb.CreateScenePathToSpawnerObjectNames.TryGetValue(key, out var bucket) && bucket.Count > 0)
                {
                    spawners = bucket;
                    break;
                }
            }

            // Fallback: include any spawner ids seen during scene scan (ObjectTemplate names, etc).
            var fromCollection = npcSpawnerDb.SpawnedSpawnerObjectNames.ToList();
            if (spawners == null || spawners.Count == 0)
            {
                spawners = fromCollection;
            }
            else
            {
                spawners = spawners.Concat(fromCollection).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }

            spawners.Sort(StringComparer.OrdinalIgnoreCase);

            sceneSpawnerComboBox.BeginUpdate();
            try
            {
                var keep = selectedSpawnerId;
                sceneSpawnerComboBox.Items.Clear();

                foreach (var spawnerId in spawners.Take(500))
                {
                    string? assetId = null;
                    if (npcSpawnerDb.SpawnerObjectNameToCandidates.TryGetValue(spawnerId, out var candidates) && candidates.Count > 0)
                    {
                        var distinct = candidates.Select(c => c.AssetId).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.Ordinal).Take(3).ToList();
                        assetId = distinct.Count == 1 ? distinct[0] : null;
                    }

                    sceneSpawnerComboBox.Items.Add(new SceneNpcListItem(spawnerId, assetId));
                }

                if (!string.IsNullOrWhiteSpace(keep))
                {
                    for (int i = 0; i < sceneSpawnerComboBox.Items.Count; i++)
                    {
                        if (sceneSpawnerComboBox.Items[i] is SceneNpcListItem item &&
                            string.Equals(item.SpawnerId, keep, StringComparison.OrdinalIgnoreCase))
                        {
                            sceneSpawnerComboBox.SelectedIndex = i;
                            return;
                        }
                    }
                }
            }
            finally
            {
                sceneSpawnerComboBox.EndUpdate();
            }

            if (selectFirst && sceneSpawnerComboBox.Items.Count > 0)
            {
                sceneSpawnerComboBox.SelectedIndex = 0;
            }
            else if (sceneSpawnerComboBox.Items.Count == 0 && showWarnings)
            {
                MessageHandler.Instance.AddMessage(MessageType.LOG, "[Scene] No NPC spawner ids were found for this scene.");
            }
        }

        private void PopulateSpawnerCandidates(string spawnerId)
        {
            spawnerCandidatesListView.BeginUpdate();
            try
            {
                spawnerCandidatesListView.Items.Clear();
                spawnerCandidateDetailsTextBox.Text = string.Empty;
            }
            finally
            {
                spawnerCandidatesListView.EndUpdate();
            }

            if (npcSpawnerDb == null || string.IsNullOrWhiteSpace(spawnerId))
            {
                return;
            }

            EnsureNpcSpawnerDbLoaded(npcSpawnerDb);
            if (!TryGetByVariants(npcSpawnerDb.SpawnerObjectNameToCandidates, spawnerId, NormalizeSpawnerId(spawnerId), out var candidates) ||
                candidates == null ||
                candidates.Count == 0)
            {
                // Fuzzy fallback: treat input as substring and show the first few matching spawners.
                var needle = spawnerId.Trim();
                var matches = npcSpawnerDb.SpawnerObjectNameToCandidates
                    .Where(kvp => kvp.Key.Contains(needle, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                    .Take(200)
                    .ToList();

                if (matches.Count == 0)
                {
                    MessageHandler.Instance.AddMessage(MessageType.LOG, $"[Scene] No NPC spawner candidates for: {spawnerId}");
                    return;
                }

                MessageHandler.Instance.AddMessage(
                    MessageType.LOG,
                    $"[Scene] No exact spawner match; showing {matches.Count} spawners containing '{needle}'.");

                spawnerCandidatesListView.BeginUpdate();
                try
                {
                    foreach (var m in matches)
                    {
                        foreach (var c in m.Value)
                        {
                            if (!npcSpawnerDb.AssetIdToObjectTemplateIds.TryGetValue(c.AssetId, out var objectTemplateIds) ||
                                objectTemplateIds.Count == 0)
                            {
                                AddCandidateRow(m.Key, c, objectTemplateId: "(missing)", templateRel: null, templateAbs: null);
                                continue;
                            }

                            foreach (var objectTemplateId in objectTemplateIds)
                            {
                                npcSpawnerDb.ObjectTemplateIdToPath.TryGetValue(objectTemplateId, out var templateRel);
                                var templateAbs = templateRel == null ? null : ResolveAssetReferenceWithVariants(templateRel);
                                AddCandidateRow(m.Key, c, objectTemplateId, templateRel, templateAbs);
                            }
                        }
                    }
                }
                finally
                {
                    spawnerCandidatesListView.EndUpdate();
                }
                return;
            }

            var sorted = candidates
                .OrderByDescending(c => c.Priority ?? int.MinValue)
                .ThenBy(c => c.AssetId, StringComparer.Ordinal)
                .ToList();

            spawnerCandidatesListView.BeginUpdate();
            try
            {
                foreach (var c in sorted)
                {
                    if (!npcSpawnerDb.AssetIdToObjectTemplateIds.TryGetValue(c.AssetId, out var objectTemplateIds) ||
                        objectTemplateIds.Count == 0)
                    {
                        AddCandidateRow(spawnerId, c, objectTemplateId: "(missing)", templateRel: null, templateAbs: null);
                        continue;
                    }

                    foreach (var objectTemplateId in objectTemplateIds)
                    {
                        npcSpawnerDb.ObjectTemplateIdToPath.TryGetValue(objectTemplateId, out var templateRel);
                        var templateAbs = templateRel == null ? null : ResolveAssetReferenceWithVariants(templateRel);
                        AddCandidateRow(spawnerId, c, objectTemplateId, templateRel, templateAbs);
                    }
                }
            }
            finally
            {
                spawnerCandidatesListView.EndUpdate();
            }

            static string Short(string? s, int max)
            {
                if (string.IsNullOrWhiteSpace(s))
                {
                    return "";
                }
                return s.Length <= max ? s : s.Substring(0, max - 1) + "…";
            }

            void AddCandidateRow(string spawner, NpcSpawnerCandidate candidate, string objectTemplateId, string? templateRel, string? templateAbs)
            {
                var item = new ListViewItem(candidate.AssetId);
                item.SubItems.Add(candidate.AppearanceId ?? "");
                item.SubItems.Add(objectTemplateId);
                item.SubItems.Add(Short(templateRel, 64));
                item.SubItems.Add(candidate.Priority?.ToString() ?? "");
                item.SubItems.Add(candidate.ActivationConditionsCount.ToString());
                item.SubItems.Add(candidate.SourceDb);
                item.Tag = new SpawnerCandidateRow(spawner, candidate, objectTemplateId, templateRel, templateAbs);
                spawnerCandidatesListView.Items.Add(item);
            }
        }

        private void spawnerCandidatesListView_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (npcTabActorsMode)
            {
                if (spawnerCandidatesListView.SelectedItems.Count != 1)
                {
                    spawnerCandidateDetailsTextBox.Text = string.Empty;
                    return;
                }

                var actorId = spawnerCandidatesListView.SelectedItems[0].Tag as string;
                if (string.IsNullOrWhiteSpace(actorId))
                {
                    spawnerCandidateDetailsTextBox.Text = string.Empty;
                    return;
                }

                eventActorStates.TryGetValue(actorId, out var state);
                var spawned = eventActorModels.ContainsKey(actorId);
                spawnerCandidateDetailsTextBox.Text =
                    $"actor={actorId}\r\n" +
                    $"kind={ClassifyEventActorKind(actorId)} spawned={(spawned ? "true" : "false")}\r\n" +
                    $"visible={(state?.Visible == false ? "false" : "true")}\r\n" +
                    $"pos={(state == null ? "(unset)" : state.Position.ToString())}\r\n" +
                    $"rotDeg={(state == null ? "(unset)" : state.RotationDegrees.ToString())}";
                return;
            }

            if (spawnerCandidatesListView.SelectedItems.Count != 1)
            {
                spawnerCandidateDetailsTextBox.Text = string.Empty;
                return;
            }

            var row = spawnerCandidatesListView.SelectedItems[0].Tag as SpawnerCandidateRow;
            if (row == null)
            {
                spawnerCandidateDetailsTextBox.Text = string.Empty;
                return;
            }

            var c = row.Candidate;
            spawnerCandidateDetailsTextBox.Text =
                $"spawner={row.SpawnerId}\r\n" +
                $"assetId={c.AssetId}\r\n" +
                $"appearanceId={c.AppearanceId ?? "(null)"} encountId={c.EncountId ?? "(null)"} prio={c.Priority?.ToString() ?? "(null)"}\r\n" +
                $"objectTemplateId={row.ObjectTemplateId}\r\n" +
                $"template={row.TemplateRel ?? "(missing)"}\r\n" +
                $"createScenePath={c.CreateScenePath ?? "(null)"}\r\n" +
                $"activationConditionList(count={c.ActivationConditionsCount}):\r\n{c.ActivationConditionsJson ?? ""}";
        }

        private async void btnSpawnCandidate_Click(object sender, EventArgs e)
        {
            if (npcTabActorsMode)
            {
                await SpawnSelectedEventActorsAsync();
                return;
            }

            if (isSceneLoading)
            {
                MessageHandler.Instance.AddMessage(MessageType.WARNING, "[Scene] Wait for the current load to finish before spawning.");
                return;
            }

            if (renderCtrl?.renderer == null)
            {
                return;
            }

            if (spawnerCandidatesListView.SelectedItems.Count != 1)
            {
                return;
            }

            var row = spawnerCandidatesListView.SelectedItems[0].Tag as SpawnerCandidateRow;
            if (row == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(row.TemplateAbs) || !System.IO.File.Exists(row.TemplateAbs))
            {
                MessageHandler.Instance.AddMessage(MessageType.WARNING, "[Scene] Selected candidate has no resolvable template file.");
                return;
            }

            await SpawnNpcCandidateAsync(row);
        }

        private async Task SpawnNpcCandidateAsync(SpawnerCandidateRow row)
        {
            if (npcSpawnerDb == null)
            {
                return;
            }

            EnsureNpcSpawnerDbLoaded(npcSpawnerDb);

            Matrix4 baseMatrix = Matrix4.Identity;
            if (TryGetByVariants(npcSpawnerDb.SpawnerTransforms, row.SpawnerId, NormalizeSpawnerId(row.SpawnerId), out var t))
            {
                baseMatrix = BuildSpawnerTransformMatrix(t);
            }

            var templateCache = new Dictionary<string, List<TemplateModelSpawn>>(StringComparer.OrdinalIgnoreCase);
            var templateInProgress = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var templateSpawns = GetTemplateSpawns(row.TemplateAbs!, templateCache, templateInProgress, CancellationToken.None);
            if (templateSpawns.Count == 0)
            {
                MessageHandler.Instance.AddMessage(MessageType.WARNING, "[Scene] Selected template produced no model spawns.");
                return;
            }

            int total = templateSpawns.Count;
            int completed = 0;
            int myLoadVersion = Interlocked.Increment(ref sceneLoadVersion);
            BeginSceneLoadUi(myLoadVersion, $"Spawning {total} model(s)...");

            try
            {
                foreach (var spawn in templateSpawns)
                {
                    var resolved = ResolveModelPath(spawn.ModelPath);
                    completed++;

                    if (resolved == null)
                    {
                        ReportSceneLoadUi(myLoadVersion, (int)(completed * 100.0 / Math.Max(1, total)), $"Missing ({completed}/{total})");
                        MessageHandler.Instance.AddMessage(MessageType.WARNING, $"[Scene] Missing model file: {spawn.ModelPath}");
                        continue;
                    }

                    var progress = new Progress<float>(p =>
                    {
                        int percent = (int)(((completed - 1) + p) * 100.0 / Math.Max(1, total));
                        ReportSceneLoadUi(myLoadVersion, percent, $"Spawning {completed}/{total}: {System.IO.Path.GetFileNameWithoutExtension(resolved)}");
                    });

                    var model = await renderCtrl!.renderer.AddSceneModelAsync(resolved, token: CancellationToken.None, progress: progress);
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
                    AddModelToList(row.SpawnerId, spawn.ModelPath, model);
                }

                EndSceneLoadUi(myLoadVersion, $"Spawned {total} model(s).");
            }
            catch (Exception ex)
            {
                EndSceneLoadUi(myLoadVersion, "Spawn failed.");
                MessageHandler.Instance.AddMessage(MessageType.ERROR, $"[Scene] Spawn failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
