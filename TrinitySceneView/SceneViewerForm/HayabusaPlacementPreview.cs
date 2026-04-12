using GFTool.Core.Flatbuffers.Hayabusa.Placement.BgParts;
using GFTool.Core.Flatbuffers.Hayabusa.Placement.Common;
using GFTool.Core.Flatbuffers.Hayabusa.Placement.Door;
using GFTool.Renderer.Core;
using GFTool.Renderer.Scene.GraphicsObjects;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Trinity.Core.Utils;
using TrinitySceneView.UI;

namespace TrinitySceneView
{
    public partial class SceneViewerForm
    {
        private const string DefaultBgPartsModelSetPath = "bin/field/param/placement/common/bg_parts_model_set.bin";
        private const string DefaultDoorDataPath = "bin/field/param/placement/door/door_data.bin";

        private Dictionary<ulong, bg_parts_model_set>? bgPartsModelSetByHash;
        private door_data[]? doorDataEntries;

        private void openBgPartsModelSet_Click(object? sender, EventArgs e)
        {
            using var ofd = new OpenFileDialog
            {
                Filter = "Hayabusa BG Parts Model Set (*.bin)|*.bin|All files (*.*)|*.*"
            };
            if (ofd.ShowDialog(this) != DialogResult.OK) return;

            try
            {
                var modelSet = FlatBufferConverter.DeserializeFrom<bg_parts_model_setParameter>(ofd.FileName);
                LoadBgPartsModelSet(modelSet, Path.GetFileName(ofd.FileName));
            }
            catch (Exception ex)
            {
                MessageHandler.Instance.AddMessage(MessageType.ERROR, $"[Hayabusa] Failed to load bg_parts_model_set: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private void openBgPartsModelSetFromGfpak_Click(object? sender, EventArgs e)
        {
            var provider = PickGfpakProvider();
            if (provider == null) return;

            using var browser = new GfpakEntryBrowserForm(
                provider,
                title: "Open BG Parts Model Set (.bin)",
                hint: "Select `bg_parts_model_set.bin` (usually under `bin/field/param/placement/common/`).\nIf names are missing, import a hash list or paste the path inside the pack.",
                filter: e => e.Path != null && e.Path.EndsWith(".bin", StringComparison.OrdinalIgnoreCase));

            if (browser.ShowDialog(this) != DialogResult.OK || browser.SelectedEntry == null)
            {
                return;
            }

            try
            {
                if (!TryReadAllBytes(provider, browser.SelectedEntry.Value, out var bytes, out var displayName))
                {
                    MessageHandler.Instance.AddMessage(MessageType.ERROR, "[GFPAK] Failed to read selected entry.");
                    return;
                }

                var modelSet = FlatBufferConverter.DeserializeFrom<bg_parts_model_setParameter>(bytes);
                LoadBgPartsModelSet(modelSet, displayName);
            }
            catch (DllNotFoundException ex)
            {
                MessageHandler.Instance.AddMessage(MessageType.ERROR, $"[GFPAK] This entry requires Oodle decompression.\nPlace `oo2core_8_win64.dll` next to the executable and try again.\n{ex.Message}");
            }
            catch (Exception ex)
            {
                MessageHandler.Instance.AddMessage(MessageType.ERROR, $"[GFPAK] Failed to load bg_parts_model_set: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private void openDoorData_Click(object? sender, EventArgs e)
        {
            using var ofd = new OpenFileDialog
            {
                Filter = "Hayabusa Door Data (*.bin)|*.bin|All files (*.*)|*.*"
            };
            if (ofd.ShowDialog(this) != DialogResult.OK) return;

            try
            {
                var doorData = FlatBufferConverter.DeserializeFrom<door_dataParameter>(ofd.FileName);
                LoadDoorData(doorData, Path.GetFileName(ofd.FileName));
            }
            catch (Exception ex)
            {
                MessageHandler.Instance.AddMessage(MessageType.ERROR, $"[Hayabusa] Failed to load door_data: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private void openDoorDataFromGfpak_Click(object? sender, EventArgs e)
        {
            var provider = PickGfpakProvider();
            if (provider == null) return;

            using var browser = new GfpakEntryBrowserForm(
                provider,
                title: "Open Door Data (.bin)",
                hint: "Select `door_data.bin` (usually under `bin/field/param/placement/door/`).\nIf names are missing, import a hash list or paste the path inside the pack.",
                filter: e => e.Path != null && e.Path.EndsWith(".bin", StringComparison.OrdinalIgnoreCase));

            if (browser.ShowDialog(this) != DialogResult.OK || browser.SelectedEntry == null)
            {
                return;
            }

            try
            {
                if (!TryReadAllBytes(provider, browser.SelectedEntry.Value, out var bytes, out var displayName))
                {
                    MessageHandler.Instance.AddMessage(MessageType.ERROR, "[GFPAK] Failed to read selected entry.");
                    return;
                }

                var doorData = FlatBufferConverter.DeserializeFrom<door_dataParameter>(bytes);
                LoadDoorData(doorData, displayName);
            }
            catch (DllNotFoundException ex)
            {
                MessageHandler.Instance.AddMessage(MessageType.ERROR, $"[GFPAK] This entry requires Oodle decompression.\nPlace `oo2core_8_win64.dll` next to the executable and try again.\n{ex.Message}");
            }
            catch (Exception ex)
            {
                MessageHandler.Instance.AddMessage(MessageType.ERROR, $"[GFPAK] Failed to load door_data: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private async void openBgPartsPlacement_Click(object? sender, EventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Filter = "Hayabusa BG Parts Placement (*.bin)|*.bin|All files (*.*)|*.*"
            };
            if (ofd.ShowDialog(this) != DialogResult.OK) return;

            await TryLoadBgPartsPlacementAsync(ofd.FileName);
        }

        private async void openBgPartsPlacementFromGfpak_Click(object? sender, EventArgs e)
        {
            var provider = PickGfpakProvider();
            if (provider == null) return;

            using var browser = new GfpakEntryBrowserForm(
                provider,
                title: "Open BG Parts Placement (.bin)",
                hint: "Select a per-area BG parts placement bin (usually `bin/field/param/placement/<area>/bg_parts/bg_parts.bin`).\nIf names are missing, import a hash list or paste the path inside the pack.",
                filter: e => e.Path != null &&
                             e.Path.Contains("bin/field/param/placement/", StringComparison.OrdinalIgnoreCase) &&
                             e.Path.EndsWith("bg_parts/bg_parts.bin", StringComparison.OrdinalIgnoreCase));

            if (browser.ShowDialog(this) != DialogResult.OK || browser.SelectedEntry == null)
            {
                return;
            }

            try
            {
                if (!TryReadAllBytes(provider, browser.SelectedEntry.Value, out var bytes, out var displayName))
                {
                    MessageHandler.Instance.AddMessage(MessageType.ERROR, "[GFPAK] Failed to read selected entry.");
                    return;
                }

                await TryLoadBgPartsPlacementAsync(bytes, displayName);
            }
            catch (DllNotFoundException ex)
            {
                MessageHandler.Instance.AddMessage(MessageType.ERROR, $"[GFPAK] This entry requires Oodle decompression.\nPlace `oo2core_8_win64.dll` next to the executable and try again.\n{ex.Message}");
            }
            catch (Exception ex)
            {
                MessageHandler.Instance.AddMessage(MessageType.ERROR, $"[GFPAK] Failed to load placement: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private bool TryEnsureBgPartsModelSetLoaded()
        {
            if (bgPartsModelSetByHash != null && bgPartsModelSetByHash.Count > 0)
            {
                return true;
            }

            try
            {
                if (sceneAssetProvider != null && sceneAssetProvider.Exists(DefaultBgPartsModelSetPath))
                {
                    var bytes = sceneAssetProvider.ReadAllBytes(DefaultBgPartsModelSetPath);
                    var modelSet = FlatBufferConverter.DeserializeFrom<bg_parts_model_setParameter>(bytes);
                    LoadBgPartsModelSet(modelSet, DefaultBgPartsModelSetPath);
                    return true;
                }
            }
            catch (Exception ex)
            {
                MessageHandler.Instance.AddMessage(MessageType.WARNING, $"[Hayabusa] Failed to auto-load bg_parts_model_set: {ex.GetType().Name}: {ex.Message}");
            }

            return false;
        }

        private void TryAutoLoadDoorDataIfMissing()
        {
            if (doorDataEntries != null && doorDataEntries.Length > 0)
            {
                return;
            }

            try
            {
                if (sceneAssetProvider != null && sceneAssetProvider.Exists(DefaultDoorDataPath))
                {
                    var bytes = sceneAssetProvider.ReadAllBytes(DefaultDoorDataPath);
                    var doorData = FlatBufferConverter.DeserializeFrom<door_dataParameter>(bytes);
                    LoadDoorData(doorData, DefaultDoorDataPath);
                }
            }
            catch (Exception ex)
            {
                MessageHandler.Instance.AddMessage(MessageType.WARNING, $"[Hayabusa] Failed to auto-load door_data: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private async Task TryLoadBgPartsPlacementAsync(string placementBinPath)
        {
            if (renderCtrl?.renderer == null)
            {
                return;
            }

            if (sceneAssetProvider == null)
            {
                RebuildSceneAssetProvider();
            }

            if (!TryEnsureBgPartsModelSetLoaded())
            {
                MessageHandler.Instance.AddMessage(MessageType.WARNING, $"[Hayabusa] Missing bg_parts_model_set. Set Asset Root and/or add GFPAKs, or open it manually: {DefaultBgPartsModelSetPath}");
                return;
            }

            TryAutoLoadDoorDataIfMissing();

            bg_partsParameter placement;
            try
            {
                placement = FlatBufferConverter.DeserializeFrom<bg_partsParameter>(placementBinPath);
            }
            catch (Exception ex)
            {
                MessageHandler.Instance.AddMessage(MessageType.ERROR, $"[Hayabusa] Failed to parse placement bin: {ex.GetType().Name}: {ex.Message}");
                return;
            }

            await TryLoadBgPartsPlacementAsync(placement, Path.GetFileName(placementBinPath));
        }

        private async Task TryLoadBgPartsPlacementAsync(byte[] placementBytes, string displayName)
        {
            bg_partsParameter placement;
            try
            {
                placement = FlatBufferConverter.DeserializeFrom<bg_partsParameter>(placementBytes);
            }
            catch (Exception ex)
            {
                MessageHandler.Instance.AddMessage(MessageType.ERROR, $"[Hayabusa] Failed to parse placement bin: {ex.GetType().Name}: {ex.Message}");
                return;
            }

            await TryLoadBgPartsPlacementAsync(placement, displayName);
        }

        private async Task TryLoadBgPartsPlacementAsync(bg_partsParameter placement, string placementNameForUi)
        {
            if (renderCtrl?.renderer == null)
            {
                return;
            }

            if (sceneAssetProvider == null)
            {
                RebuildSceneAssetProvider();
            }

            if (!TryEnsureBgPartsModelSetLoaded())
            {
                MessageHandler.Instance.AddMessage(MessageType.WARNING, $"[Hayabusa] Missing bg_parts_model_set. Add GFPAKs and/or open it manually: {DefaultBgPartsModelSetPath}");
                return;
            }

            TryAutoLoadDoorDataIfMissing();

            var bgParts = placement.BgPartsArray ?? Array.Empty<bg_parts>();
            int totalInstances = bgParts.Sum(p => p?.BaseParameterArray?.Length ?? 0);
            if (totalInstances > 800)
            {
                var result = MessageBox.Show(
                    this,
                    $"This placement file contains {totalInstances} instances.\n\nLoading all models can be slow and memory-heavy.\n\nContinue?",
                    "Hayabusa Placement",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                if (result != DialogResult.Yes)
                {
                    return;
                }
            }

            CancelSceneLoad();
            int loadVersion = Interlocked.Increment(ref sceneLoadVersion);
            sceneLoadCts = new CancellationTokenSource();
            var token = sceneLoadCts.Token;

            isSceneLoading = true;
            if (!config.AdditiveLoads)
            {
                renderCtrl.renderer.ClearScene();
                ClearModelsList();
            }
            renderCtrl.Invalidate();

            BeginSceneLoadUi(loadVersion, $"Spawning {totalInstances} model(s)...");

            var missingModelIds = new HashSet<ulong>();
            var spawnedPositions = new List<Vector3>(Math.Min(totalInstances, 1024));

            try
            {
                string areaGuessForAutoMount = GuessAreaName(placementNameForUi);
                if (!string.IsNullOrWhiteSpace(areaGuessForAutoMount))
                {
                    TryAutoMountAreaModelPack(areaGuessForAutoMount);
                }

                int completed = 0;
                int total = Math.Max(1, totalInstances);
                foreach (var part in bgParts)
                {
                    token.ThrowIfCancellationRequested();
                    if (part == null)
                    {
                        continue;
                    }

                    if (bgPartsModelSetByHash == null || !bgPartsModelSetByHash.TryGetValue(part.ModelIDHash, out var modelDef) || modelDef == null)
                    {
                        missingModelIds.Add(part.ModelIDHash);
                        continue;
                    }

                    string? modelPath = !string.IsNullOrWhiteSpace(modelDef.PackTrmdlPath) ? modelDef.PackTrmdlPath : modelDef.TrmdlPath;
                    if (string.IsNullOrWhiteSpace(modelPath))
                    {
                        missingModelIds.Add(part.ModelIDHash);
                        continue;
                    }

                    var instances = part.BaseParameterArray ?? Array.Empty<PlacementBaseParameter>();
                    for (int i = 0; i < instances.Length; i++)
                    {
                        token.ThrowIfCancellationRequested();

                        var inst = instances[i];
                        completed++;

                        var progress = new Progress<float>(p =>
                        {
                            int percent = ComputeOverallPercent(total, completed - 1, p);
                            ReportSceneLoadUi(loadVersion, percent, $"Loading {completed}/{total}: {Path.GetFileNameWithoutExtension(modelPath)}");
                        });

                        Model model;
                        try
                        {
                            model = await renderCtrl.renderer.AddSceneModelAsync(sceneAssetProvider!, modelPath, loadAllLods: config.LoadAllLods, token: token, progress: progress);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (FileNotFoundException ex)
                        {
                            string hint = string.Empty;
                            if (!string.IsNullOrWhiteSpace(ex.FileName) &&
                                ex.FileName.Replace('\\', '/').StartsWith("bin/field/model/pack/", StringComparison.OrdinalIgnoreCase) &&
                                !string.IsNullOrWhiteSpace(areaGuessForAutoMount))
                            {
                                string baseArea = areaGuessForAutoMount;
                                int subIdx = baseArea.IndexOf("_s", StringComparison.OrdinalIgnoreCase);
                                if (subIdx > 0) baseArea = baseArea.Substring(0, subIdx);
                                hint = $" (open `.../archive/field/model/pack/{baseArea}.gfpak`)";
                            }

                            MessageHandler.Instance.AddMessage(MessageType.WARNING, $"[Hayabusa] Missing referenced file: {ex.FileName ?? modelPath}{hint}");
                            continue;
                        }
                        catch (Exception ex)
                        {
                            MessageHandler.Instance.AddMessage(MessageType.WARNING, $"[Hayabusa] Failed to load '{modelPath}': {ex.GetType().Name}: {ex.Message}");
                            continue;
                        }

                        var baseMat = BuildPlacementMatrix(inst);
                        var mat = ApplyViewerMatrixOptions(baseMat, config.SpawnModelsAtOrigin, config.RotateModels180X, config.RotateModels180Y, out var position, out _);
                        model.SetModelMatrix(mat);
                        spawnedPositions.Add(position);

                        string label = string.IsNullOrWhiteSpace(part.UniqueID)
                            ? $"[BgParts] 0x{part.ModelIDHash:X16}"
                            : $"[BgParts] {part.UniqueID}";
                        AddModelToList(label, modelPath, model);
                    }
                }

                var info = new StringBuilder();
                string areaGuess = GuessAreaName(placementNameForUi);
                info.AppendLine($"BG Parts placement: {placementNameForUi}");
                info.AppendLine($"Entries: {bgParts.Length}  Instances: {totalInstances}");
                info.AppendLine($"Area guess: {areaGuess}");

                if (missingModelIds.Count > 0)
                {
                    info.AppendLine();
                    info.AppendLine($"Missing modelIDHash entries: {missingModelIds.Count}");
                    foreach (var h in missingModelIds.Take(20))
                    {
                        info.AppendLine($"- 0x{h:X16}");
                    }
                    if (missingModelIds.Count > 20)
                    {
                        info.AppendLine($"(and {missingModelIds.Count - 20} more)");
                    }
                }

                var doorsForArea = Array.Empty<door_data>();
                if (doorDataEntries != null && doorDataEntries.Length > 0 && !string.IsNullOrWhiteSpace(areaGuess))
                {
                    doorsForArea = doorDataEntries
                        .Where(d => d != null && string.Equals(d.AreaName, areaGuess, StringComparison.OrdinalIgnoreCase))
                        .ToArray();
                }

                if (doorsForArea.Length > 0)
                {
                    info.AppendLine();
                    info.AppendLine($"Doors (matched areaName='{areaGuess}'): {doorsForArea.Length}");
                    foreach (var d in doorsForArea.Take(20))
                    {
                        string doorId = d.DoorID ?? "(null)";
                        string exitId = d.ExitDoorID ?? "(null)";
                        var pos = d.ColPos ?? d.ActionButtonPos ?? d.ExitPos;
                        if (pos != null)
                        {
                            info.AppendLine($"- {doorId} -> {exitId} @ ({pos.X:0.###},{pos.Y:0.###},{pos.Z:0.###})");
                        }
                        else
                        {
                            info.AppendLine($"- {doorId} -> {exitId}");
                        }
                    }
                    if (doorsForArea.Length > 20)
                    {
                        info.AppendLine($"(and {doorsForArea.Length - 20} more)");
                    }
                }

                InfoBox.Text = info.ToString();

                if (spawnedPositions.Count > 0)
                {
                    var (center, radius) = ComputeBounds(spawnedPositions);
                    renderCtrl.renderer.FocusCamera(center, MathF.Max(10f, radius * 1.2f));
                    ApplySceneClipPlanes(center, radius);
                }

                EndSceneLoadUi(loadVersion, $"Loaded {spawnedPositions.Count}/{totalInstances} model(s).");
            }
            catch (OperationCanceledException)
            {
                EndSceneLoadUi(loadVersion, "Load canceled.");
            }
            catch (Exception ex)
            {
                EndSceneLoadUi(loadVersion, "Load failed.");
                MessageHandler.Instance.AddMessage(MessageType.ERROR, $"[Hayabusa] Load failed: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                if (sceneLoadVersion == loadVersion)
                {
                    isSceneLoading = false;
                    renderCtrl?.Invalidate();
                }
            }
        }

        private void LoadBgPartsModelSet(bg_parts_model_setParameter modelSet, string sourceLabel)
        {
            bgPartsModelSetByHash = (modelSet.BgPartsModelSetArray ?? Array.Empty<bg_parts_model_set>())
                .Where(e => e != null && e.ModelIDHash != 0)
                .GroupBy(e => e.ModelIDHash)
                .ToDictionary(g => g.Key, g => g.First());

            MessageHandler.Instance.AddMessage(MessageType.LOG, $"[Hayabusa] Loaded bg_parts_model_set: {bgPartsModelSetByHash.Count} entries. ({sourceLabel})");
        }

        private void LoadDoorData(door_dataParameter doorData, string sourceLabel)
        {
            doorDataEntries = doorData.DoorDataArray ?? Array.Empty<door_data>();
            MessageHandler.Instance.AddMessage(MessageType.LOG, $"[Hayabusa] Loaded door_data: {doorDataEntries.Length} entries. ({sourceLabel})");
        }

        private static string GuessAreaName(string placementNameForUi)
        {
            if (string.IsNullOrWhiteSpace(placementNameForUi))
            {
                return string.Empty;
            }

            // Prefer any embedded path (from GFPAK) and strip extension.
            string name = placementNameForUi.Replace('\\', '/');
            int lastSlash = name.LastIndexOf('/');
            if (lastSlash >= 0 && lastSlash + 1 < name.Length)
            {
                name = name.Substring(lastSlash + 1);
            }
            return Path.GetFileNameWithoutExtension(name);
        }

        private static Matrix4 BuildPlacementMatrix(PlacementBaseParameter bp)
        {
            if (bp == null)
            {
                return Matrix4.Identity;
            }

            var pos = bp.Pos != null ? new Vector3(bp.Pos.X, bp.Pos.Y, bp.Pos.Z) : Vector3.Zero;
            var scale = bp.Scale != null ? new Vector3(bp.Scale.X, bp.Scale.Y, bp.Scale.Z) : Vector3.One;

            Quaternion q = Quaternion.Identity;
            if (bp.Rot != null)
            {
                float rx = MathHelper.DegreesToRadians(bp.Rot.X);
                float ry = MathHelper.DegreesToRadians(bp.Rot.Y);
                float rz = MathHelper.DegreesToRadians(bp.Rot.Z);
                q = Quaternion.FromEulerAngles(rx, ry, rz);
                q.Normalize();
            }

            return Matrix4.CreateTranslation(pos) * Matrix4.CreateFromQuaternion(q) * Matrix4.CreateScale(scale);
        }

        private static (Vector3 Center, float Radius) ComputeBounds(List<Vector3> positions)
        {
            if (positions == null || positions.Count == 0)
            {
                return (Vector3.Zero, 0f);
            }

            var min = positions[0];
            var max = positions[0];
            for (int i = 1; i < positions.Count; i++)
            {
                min = Vector3.ComponentMin(min, positions[i]);
                max = Vector3.ComponentMax(max, positions[i]);
            }
            var center = (min + max) * 0.5f;
            float radius = 0f;
            for (int i = 0; i < positions.Count; i++)
            {
                radius = MathF.Max(radius, (positions[i] - center).Length);
            }
            return (center, radius);
        }
    }
}
