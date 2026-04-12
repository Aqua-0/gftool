using GFTool.Core.Flatbuffers.Hayabusa.Gfx2;
using GFTool.Core.Flatbuffers.Hayabusa.Physics;
using GFTool.Core.Flatbuffers.Hayabusa.Terrain;
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
        private async void openTRTRR_Click(object? sender, EventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Filter = "Hayabusa Terrain (*.trtrr)|*.trtrr|All files (*.*)|*.*"
            };
            if (ofd.ShowDialog() != DialogResult.OK) return;

            if (string.IsNullOrWhiteSpace(assetRoot) || !Directory.Exists(assetRoot))
            {
                MessageHandler.Instance.AddMessage(MessageType.WARNING, "[Hayabusa] Asset root not set; use File -> Set Asset Root... to enable model loading.");
                return;
            }

            await TryLoadHayabusaTerrainPreviewAsync(ofd.FileName);
        }

        private void openTRTRR_FromGfpak_Click(object? sender, EventArgs e)
        {
            var provider = PickGfpakProvider();
            if (provider == null)
            {
                return;
            }

            using var browser = new GfpakEntryBrowserForm(
                provider,
                title: "Open Terrain (.trtrr)",
                hint: "Select a `.trtrr` entry.\nIf names are missing, import a hash list or paste the path inside the pack.",
                filter: entry => entry.Path != null && entry.Path.EndsWith(".trtrr", StringComparison.OrdinalIgnoreCase));

            if (browser.ShowDialog(this) != DialogResult.OK || browser.SelectedEntry == null)
            {
                return;
            }

            var entryPath = browser.SelectedEntry.Value.Path;
            if (string.IsNullOrWhiteSpace(entryPath))
            {
                MessageHandler.Instance.AddMessage(MessageType.WARNING, "[GFPAK] Selected TRTRR has no known path; cannot resolve relative references.");
                return;
            }

            try
            {
                var bytes = provider.ReadAllBytes(entryPath);
                _ = TryLoadHayabusaTerrainPreviewFromPackAsync(bytes, entryPath);
            }
            catch (DllNotFoundException ex)
            {
                MessageHandler.Instance.AddMessage(MessageType.ERROR, $"[GFPAK] This entry requires Oodle decompression.\nPlace `oo2core_8_win64.dll` next to the executable and try again.\n{ex.Message}");
            }
            catch (Exception ex)
            {
                MessageHandler.Instance.AddMessage(MessageType.ERROR, $"[GFPAK] Failed to open TRTRR: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private void openTRHF_Click(object? sender, EventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Filter = "Hayabusa HeightField (*.trhf)|*.trhf|All files (*.*)|*.*"
            };
            if (ofd.ShowDialog() != DialogResult.OK) return;

            TryShowTrhfInfo(ofd.FileName);
            _ = TryLoadHeightFieldMeshPreviewAsync(ofd.FileName);
        }

        private void openTRHF_FromGfpak_Click(object? sender, EventArgs e)
        {
            var provider = PickGfpakProvider();
            if (provider == null)
            {
                return;
            }

            using var browser = new GfpakEntryBrowserForm(
                provider,
                title: "Open HeightField (.trhf)",
                hint: "Select a `.trhf` entry.\nIf names are missing, import a hash list or paste the path inside the pack.",
                filter: entry => entry.Path != null && entry.Path.EndsWith(".trhf", StringComparison.OrdinalIgnoreCase));

            if (browser.ShowDialog(this) != DialogResult.OK || browser.SelectedEntry == null)
            {
                return;
            }

            var entryPath = browser.SelectedEntry.Value.Path;
            if (string.IsNullOrWhiteSpace(entryPath))
            {
                MessageHandler.Instance.AddMessage(MessageType.WARNING, "[GFPAK] Selected TRHF has no known path.");
                return;
            }

            try
            {
                var bytes = provider.ReadAllBytes(entryPath);
                TryShowTrhfInfo(bytes, entryPath);
                _ = TryLoadHeightFieldMeshPreviewAsync(bytes, entryPath);
            }
            catch (DllNotFoundException ex)
            {
                MessageHandler.Instance.AddMessage(MessageType.ERROR, $"[GFPAK] This entry requires Oodle decompression.\nPlace `oo2core_8_win64.dll` next to the executable and try again.\n{ex.Message}");
            }
            catch (Exception ex)
            {
                MessageHandler.Instance.AddMessage(MessageType.ERROR, $"[GFPAK] Failed to open TRHF: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private async Task TryLoadHayabusaTerrainPreviewAsync(string trtrrPath)
        {
            if (renderCtrl?.renderer == null)
            {
                return;
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

            BeginSceneLoadUi(loadVersion, "Scanning terrain...");

            try
            {
                token.ThrowIfCancellationRequested();
                var terrain = FlatBufferConverter.DeserializeFrom<Terrain>(trtrrPath);

                var sb = new StringBuilder();
                sb.AppendLine($"TRTRR: {Path.GetFileName(trtrrPath)}");
                if (terrain.BoundingBox != null)
                {
                    sb.AppendLine($"AABB min=({terrain.BoundingBox.Min.X:0.###},{terrain.BoundingBox.Min.Y:0.###},{terrain.BoundingBox.Min.Z:0.###}) max=({terrain.BoundingBox.Max.X:0.###},{terrain.BoundingBox.Max.Y:0.###},{terrain.BoundingBox.Max.Z:0.###})");
                }

                var trees = terrain.Trees ?? Array.Empty<Tree>();
                sb.AppendLine($"Trees entries: {trees.Length}");

                // For now, we only load each unique tree model once (preview) to avoid blowing up on thousands of instances.
                var uniqueModels = new Dictionary<string, (int InstanceFiles, int Instances)>(StringComparer.OrdinalIgnoreCase);
                foreach (var t in trees)
                {
                    var modelRel = t.ModelFilePath ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(modelRel))
                    {
                        continue;
                    }

                    int totalInstances = 0;
                    int instanceFiles = 0;
                    var inst = t.InstanceFilePathList ?? Array.Empty<string>();
                    foreach (var instRel in inst)
                    {
                        if (string.IsNullOrWhiteSpace(instRel)) continue;
                        instanceFiles++;
                        var instFull = ResolveRelativeToTrtrr(trtrrPath, instRel);
                        totalInstances += TryGetTrinsInstanceCount(instFull);
                    }

                    if (uniqueModels.TryGetValue(modelRel, out var cur))
                    {
                        uniqueModels[modelRel] = (cur.InstanceFiles + instanceFiles, cur.Instances + totalInstances);
                    }
                    else
                    {
                        uniqueModels[modelRel] = (instanceFiles, totalInstances);
                    }
                }

                foreach (var kvp in uniqueModels.OrderByDescending(k => k.Value.Instances).ThenBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                {
                    sb.AppendLine($"- {kvp.Key} (trins={kvp.Value.InstanceFiles}, instances≈{kvp.Value.Instances})");
                }

                InfoBox.Text = sb.ToString();

                int completed = 0;
                int total = uniqueModels.Count;
                var spawnedPositions = new List<Vector3>(capacity: Math.Min(total, 64));

                foreach (var kvp in uniqueModels)
                {
                    token.ThrowIfCancellationRequested();
                    completed++;

                    int percent = (int)(completed * 100f / Math.Max(1, total));
                    ReportSceneLoadUi(loadVersion, percent, $"Loading {completed}/{total}: {Path.GetFileNameWithoutExtension(kvp.Key)}");

                    var resolvedModel = ResolveHayabusaTrtrrRelativePath(trtrrPath, kvp.Key);
                    if (resolvedModel == null)
                    {
                        MessageHandler.Instance.AddMessage(MessageType.WARNING, $"[Hayabusa] Missing tree model: {kvp.Key}");
                        continue;
                    }

                    Model model;
                    try
                    {
                        model = await renderCtrl.renderer.AddSceneModelAsync(resolvedModel, token: token);
                    }
                    catch (Exception ex)
                    {
                        MessageHandler.Instance.AddMessage(MessageType.WARNING, $"[Hayabusa] Failed to load model '{resolvedModel}': {ex.GetType().Name}: {ex.Message}");
                        continue;
                    }

                    // Preview models at origin; actual per-instance placement requires instanced rendering support.
                    var mat = ApplyViewerMatrixOptions(Matrix4.Identity, config.SpawnModelsAtOrigin, config.RotateModels180X, config.RotateModels180Y, out var position, out var scale);
                    model.SetModelMatrix(mat);
                    spawnedPositions.Add(position);
                    AddModelToList($"[TreePreview] {Path.GetFileNameWithoutExtension(kvp.Key)} (instances≈{kvp.Value.Instances})", kvp.Key, model);
                }

                if (spawnedPositions.Count > 0)
                {
                    renderCtrl.renderer.FocusCamera(Vector3.Zero, 20f);
                    ApplySceneClipPlanes(Vector3.Zero, 50f);
                }

                EndSceneLoadUi(loadVersion, $"Loaded {completed}/{total} preview model(s).");
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

        private async Task TryLoadHayabusaTerrainPreviewFromPackAsync(byte[] trtrrBytes, string trtrrPathInPack)
        {
            if (renderCtrl?.renderer == null)
            {
                return;
            }

            if (sceneAssetProvider == null)
            {
                RebuildSceneAssetProvider();
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

            BeginSceneLoadUi(loadVersion, "Scanning terrain...");

            try
            {
                token.ThrowIfCancellationRequested();
                var terrain = FlatBufferConverter.DeserializeFrom<Terrain>(trtrrBytes);
                await TryLoadHayabusaTerrainPreviewCoreAsync(loadVersion, token, terrain, trtrrPathInPack);
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

        private static string ResolveRelativeToTrtrr(string trtrrPath, string relative)
        {
            var dir = Path.GetDirectoryName(trtrrPath) ?? string.Empty;
            return Path.GetFullPath(Path.Combine(dir, relative));
        }

        private string? ResolveHayabusaTrtrrRelativePath(string trtrrPath, string relativeModelPath)
        {
            // Terrain files use CreateFromRelativePath-style semantics: relative to the .trtrr file folder.
            // In extracted packs this often resolves directly under the chosen assetRoot.
            var full = ResolveRelativeToTrtrr(trtrrPath, relativeModelPath);
            if (File.Exists(full)) return full;

            // Fallback: sometimes model paths are already rooted at "bin/..."; allow resolving under assetRoot.
            var trimmed = relativeModelPath.TrimStart('\\', '/');
            var rootCandidate = Path.Combine(assetRoot!, trimmed);
            if (File.Exists(rootCandidate)) return rootCandidate;

            return null;
        }

        private static int TryGetTrinsInstanceCount(string trinsPath)
        {
            try
            {
                if (!File.Exists(trinsPath))
                {
                    return 0;
                }

                var buf = FlatBufferConverter.DeserializeFrom<InstanceBuffer>(trinsPath);
                if (buf == null) return 0;

                if (buf.ParticleCount > 0) return buf.ParticleCount;
                if (buf.ParticleList != null) return buf.ParticleList.Length;

                if (buf.Buffer?.Data != null && buf.Buffer.Data.Length > 0)
                {
                    // Heuristic: SRT packed as 9 floats (scale/rot/translate) == 36 bytes per instance.
                    var bytes = buf.Buffer.Data.Length;
                    if (bytes % 36 == 0)
                    {
                        return bytes / 36;
                    }
                }

                return 0;
            }
            catch
            {
                return 0;
            }
        }

        private int TryGetTrinsInstanceCountFromProvider(string pathInProvider)
        {
            try
            {
                if (sceneAssetProvider == null || !sceneAssetProvider.Exists(pathInProvider))
                {
                    return 0;
                }

                var bytes = sceneAssetProvider.ReadAllBytes(pathInProvider);
                var buf = FlatBufferConverter.DeserializeFrom<InstanceBuffer>(bytes);
                if (buf == null) return 0;

                if (buf.ParticleCount > 0) return buf.ParticleCount;
                if (buf.ParticleList != null) return buf.ParticleList.Length;

                if (buf.Buffer?.Data != null && buf.Buffer.Data.Length > 0)
                {
                    var b = buf.Buffer.Data.Length;
                    if (b % 36 == 0) return b / 36;
                }

                return 0;
            }
            catch
            {
                return 0;
            }
        }

        private static void AppendVec(StringBuilder sb, string name, HeightFieldVector? v)
        {
            if (v == null)
            {
                sb.AppendLine($"{name}: (null)");
                return;
            }
            sb.AppendLine($"{name}: ({v.X:0.###}, {v.Y:0.###}, {v.Z:0.###})");
        }

        private void TryShowTrhfInfo(string trhfPath)
        {
            try
            {
                var hf = FlatBufferConverter.DeserializeFrom<TrhfHeightField>(trhfPath);
                var sb = new StringBuilder();
                sb.AppendLine($"TRHF: {Path.GetFileName(trhfPath)}");
                sb.AppendLine($"Row={hf.Row} Column={hf.Column}");
                sb.AppendLine($"LayerWidth={hf.LayerWidth} LayerHeight={hf.LayerHeight}");
                AppendVec(sb, "Position", hf.Position);
                AppendVec(sb, "Scale", hf.Scale);
                AppendVec(sb, "Size", hf.Size);

                var layers = hf.Layers ?? Array.Empty<HeightFieldLayer>();
                sb.AppendLine($"Layers={layers.Length}");
                foreach (var l in layers)
                {
                    if (l == null) continue;
                    sb.AppendLine($"- {l.Name} pixels={(l.Pixels?.Length ?? 0)}");
                }
                sb.AppendLine($"Mask pixels={(hf.Mask?.Pixels?.Length ?? 0)}");
                sb.AppendLine($"Heights={(hf.Heights?.Length ?? 0)}");

                InfoBox.Text = sb.ToString();
                MessageHandler.Instance.AddMessage(MessageType.LOG, $"[Hayabusa] Loaded TRHF info: {Path.GetFileName(trhfPath)}");
            }
            catch (Exception ex)
            {
                MessageHandler.Instance.AddMessage(MessageType.ERROR, $"[Hayabusa] Failed to parse TRHF '{trhfPath}': {ex.GetType().Name}: {ex.Message}");
            }
        }

        private void TryShowTrhfInfo(byte[] trhfBytes, string nameForUi)
        {
            try
            {
                var hf = FlatBufferConverter.DeserializeFrom<TrhfHeightField>(trhfBytes);
                var sb = new StringBuilder();
                sb.AppendLine($"TRHF: {nameForUi}");
                sb.AppendLine($"Row={hf.Row} Column={hf.Column}");
                sb.AppendLine($"LayerWidth={hf.LayerWidth} LayerHeight={hf.LayerHeight}");
                AppendVec(sb, "Position", hf.Position);
                AppendVec(sb, "Scale", hf.Scale);
                AppendVec(sb, "Size", hf.Size);

                var layers = hf.Layers ?? Array.Empty<HeightFieldLayer>();
                sb.AppendLine($"Layers={layers.Length}");
                foreach (var l in layers)
                {
                    if (l == null) continue;
                    sb.AppendLine($"- {l.Name} pixels={(l.Pixels?.Length ?? 0)}");
                }
                sb.AppendLine($"Mask pixels={(hf.Mask?.Pixels?.Length ?? 0)}");
                sb.AppendLine($"Heights={(hf.Heights?.Length ?? 0)}");

                InfoBox.Text = sb.ToString();
                MessageHandler.Instance.AddMessage(MessageType.LOG, $"[Hayabusa] Loaded TRHF info: {nameForUi}");
            }
            catch (Exception ex)
            {
                MessageHandler.Instance.AddMessage(MessageType.ERROR, $"[Hayabusa] Failed to parse TRHF '{nameForUi}': {ex.GetType().Name}: {ex.Message}");
            }
        }

        private async Task TryLoadHeightFieldMeshPreviewAsync(string trhfPath)
        {
            if (renderCtrl?.renderer == null)
            {
                return;
            }

            TrhfHeightField hf;
            try
            {
                hf = FlatBufferConverter.DeserializeFrom<TrhfHeightField>(trhfPath);
            }
            catch (Exception ex)
            {
                MessageHandler.Instance.AddMessage(MessageType.ERROR, $"[Hayabusa] Failed to parse TRHF '{trhfPath}': {ex.GetType().Name}: {ex.Message}");
                return;
            }

            var verts = BuildHeightFieldWireVertices(hf, maxSegments: Math.Max(10_000, config.HeightFieldMaxSegments));
            if (verts.Length == 0)
            {
                MessageHandler.Instance.AddMessage(MessageType.WARNING, "[Hayabusa] TRHF has no heights to preview.");
                return;
            }

            try
            {
                await renderCtrl.renderer.ReplaceHeightFieldOverlayAsync(new HeightFieldMesh(verts));
                renderCtrl.Invalidate();
                MessageHandler.Instance.AddMessage(MessageType.LOG, $"[Hayabusa] HeightField preview: {verts.Length / 2} segments.");
            }
            catch (Exception ex)
            {
                MessageHandler.Instance.AddMessage(MessageType.ERROR, $"[Hayabusa] TRHF mesh preview failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private async Task TryLoadHeightFieldMeshPreviewAsync(byte[] trhfBytes, string nameForUi)
        {
            if (renderCtrl?.renderer == null)
            {
                return;
            }

            TrhfHeightField hf;
            try
            {
                hf = FlatBufferConverter.DeserializeFrom<TrhfHeightField>(trhfBytes);
            }
            catch (Exception ex)
            {
                MessageHandler.Instance.AddMessage(MessageType.ERROR, $"[Hayabusa] Failed to parse TRHF '{nameForUi}': {ex.GetType().Name}: {ex.Message}");
                return;
            }

            var verts = BuildHeightFieldWireVertices(hf, maxSegments: Math.Max(10_000, config.HeightFieldMaxSegments));
            if (verts.Length == 0)
            {
                MessageHandler.Instance.AddMessage(MessageType.WARNING, "[Hayabusa] TRHF has no heights to preview.");
                return;
            }

            try
            {
                await renderCtrl.renderer.ReplaceHeightFieldOverlayAsync(new HeightFieldMesh(verts));
                renderCtrl.Invalidate();
                MessageHandler.Instance.AddMessage(MessageType.LOG, $"[Hayabusa] HeightField preview: {verts.Length / 2} segments. ({nameForUi})");
            }
            catch (Exception ex)
            {
                MessageHandler.Instance.AddMessage(MessageType.ERROR, $"[Hayabusa] TRHF mesh preview failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static string? ResolveHayabusaPackRelative(string basePathInPack, string referenced)
        {
            if (string.IsNullOrWhiteSpace(referenced))
            {
                return null;
            }

            var refNorm = referenced.Trim().Replace('\\', '/').TrimStart('/');
            if (refNorm.StartsWith("bin/", StringComparison.OrdinalIgnoreCase))
            {
                return refNorm;
            }

            var baseNorm = (basePathInPack ?? string.Empty).Replace('\\', '/');
            int lastSlash = baseNorm.LastIndexOf('/');
            string baseDir = lastSlash >= 0 ? baseNorm.Substring(0, lastSlash + 1) : string.Empty;
            return (baseDir + refNorm).Replace('\\', '/');
        }

        private async Task TryLoadHayabusaTerrainPreviewCoreAsync(int loadVersion, CancellationToken token, Terrain terrain, string trtrrNameOrPathInPack)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"TRTRR: {Path.GetFileName(trtrrNameOrPathInPack)}");
            if (terrain.BoundingBox != null)
            {
                sb.AppendLine($"AABB min=({terrain.BoundingBox.Min.X:0.###},{terrain.BoundingBox.Min.Y:0.###},{terrain.BoundingBox.Min.Z:0.###}) max=({terrain.BoundingBox.Max.X:0.###},{terrain.BoundingBox.Max.Y:0.###},{terrain.BoundingBox.Max.Z:0.###})");
            }

            var trees = terrain.Trees ?? Array.Empty<Tree>();
            sb.AppendLine($"Trees entries: {trees.Length}");

            var uniqueModels = new Dictionary<string, (int InstanceFiles, int Instances)>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in trees)
            {
                var modelRel = t.ModelFilePath ?? string.Empty;
                if (string.IsNullOrWhiteSpace(modelRel))
                {
                    continue;
                }

                int totalInstances = 0;
                int instanceFiles = 0;
                var inst = t.InstanceFilePathList ?? Array.Empty<string>();
                foreach (var instRel in inst)
                {
                    if (string.IsNullOrWhiteSpace(instRel)) continue;
                    instanceFiles++;

                    int instCount = 0;
                    if (File.Exists(trtrrNameOrPathInPack))
                    {
                        var instFull = ResolveRelativeToTrtrr(trtrrNameOrPathInPack, instRel);
                        instCount = TryGetTrinsInstanceCount(instFull);
                    }
                    else
                    {
                        var instPath = ResolveHayabusaPackRelative(trtrrNameOrPathInPack, instRel);
                        if (instPath != null)
                        {
                            instCount = TryGetTrinsInstanceCountFromProvider(instPath);
                        }
                    }

                    totalInstances += instCount;
                }

                if (uniqueModels.TryGetValue(modelRel, out var cur))
                {
                    uniqueModels[modelRel] = (cur.InstanceFiles + instanceFiles, cur.Instances + totalInstances);
                }
                else
                {
                    uniqueModels[modelRel] = (instanceFiles, totalInstances);
                }
            }

            foreach (var kvp in uniqueModels.OrderByDescending(k => k.Value.Instances).ThenBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine($"- {kvp.Key} (trins={kvp.Value.InstanceFiles}, instances≈{kvp.Value.Instances})");
            }

            InfoBox.Text = sb.ToString();

            int completed = 0;
            int total = uniqueModels.Count;
            var spawnedPositions = new List<Vector3>(capacity: Math.Min(total, 64));

            foreach (var kvp in uniqueModels)
            {
                token.ThrowIfCancellationRequested();
                completed++;

                int percent = (int)(completed * 100f / Math.Max(1, total));
                ReportSceneLoadUi(loadVersion, percent, $"Loading {completed}/{total}: {Path.GetFileNameWithoutExtension(kvp.Key)}");

                string? modelPath;
                if (File.Exists(trtrrNameOrPathInPack))
                {
                    modelPath = ResolveHayabusaTrtrrRelativePath(trtrrNameOrPathInPack, kvp.Key);
                }
                else
                {
                    modelPath = ResolveHayabusaPackRelative(trtrrNameOrPathInPack, kvp.Key);
                }

                if (string.IsNullOrWhiteSpace(modelPath))
                {
                    MessageHandler.Instance.AddMessage(MessageType.WARNING, $"[Hayabusa] Missing tree model: {kvp.Key}");
                    continue;
                }

                Model model;
                try
                {
                    if (sceneAssetProvider != null)
                    {
                        model = await renderCtrl!.renderer.AddSceneModelAsync(sceneAssetProvider, modelPath, loadAllLods: config.LoadAllLods, token: token);
                    }
                    else
                    {
                        model = await renderCtrl!.renderer.AddSceneModelAsync(modelPath, token: token);
                    }
                }
                catch (Exception ex)
                {
                    MessageHandler.Instance.AddMessage(MessageType.WARNING, $"[Hayabusa] Failed to load model '{modelPath}': {ex.GetType().Name}: {ex.Message}");
                    continue;
                }

                var mat = ApplyViewerMatrixOptions(Matrix4.Identity, config.SpawnModelsAtOrigin, config.RotateModels180X, config.RotateModels180Y, out var position, out _);
                model.SetModelMatrix(mat);
                spawnedPositions.Add(position);
                AddModelToList($"[TreePreview] {Path.GetFileNameWithoutExtension(kvp.Key)} (instances≈{kvp.Value.Instances})", kvp.Key, model);
            }

            if (spawnedPositions.Count > 0)
            {
                renderCtrl!.renderer.FocusCamera(Vector3.Zero, 20f);
                ApplySceneClipPlanes(Vector3.Zero, 50f);
            }

            EndSceneLoadUi(loadVersion, $"Loaded {completed}/{total} preview model(s).");
        }

        private static Vector3[] BuildHeightFieldWireVertices(TrhfHeightField hf, int maxSegments)
        {
            if (hf == null || hf.Row <= 1 || hf.Column <= 1)
            {
                return Array.Empty<Vector3>();
            }

            var heights = hf.Heights ?? Array.Empty<float>();
            if (heights.Length == 0)
            {
                return Array.Empty<Vector3>();
            }

            int rows = hf.Row;
            int cols = hf.Column;
            int expected = checked(rows * cols);
            int usable = Math.Min(expected, heights.Length);
            if (usable < cols + 1)
            {
                return Array.Empty<Vector3>();
            }

            var pos0 = hf.Position != null ? new Vector3(hf.Position.X, hf.Position.Y, hf.Position.Z) : Vector3.Zero;
            var scale = hf.Scale != null ? new Vector3(hf.Scale.X, hf.Scale.Y, hf.Scale.Z) : new Vector3(1f, 1f, 1f);
            if (scale.X == 0f) scale.X = 1f;
            if (scale.Y == 0f) scale.Y = 1f;
            if (scale.Z == 0f) scale.Z = 1f;

            long rawSegments = (long)(rows - 1) * (cols - 1) * 2;
            int stride = 1;
            if (rawSegments > maxSegments && maxSegments > 0)
            {
                double factor = Math.Sqrt(rawSegments / (double)maxSegments);
                stride = Math.Max(1, (int)Math.Ceiling(factor));
            }

            int rSteps = (rows - 1 + stride - 1) / stride;
            int cSteps = (cols - 1 + stride - 1) / stride;
            long approxSegments = (long)rSteps * cSteps * 2;
            int capSegments = maxSegments > 0 ? maxSegments : int.MaxValue / 2;
            if (approxSegments > capSegments)
            {
                double factor = Math.Sqrt(approxSegments / (double)capSegments);
                stride = Math.Max(1, (int)Math.Ceiling(stride * factor));
            }

            static int Index(int r, int c, int cols) => r * cols + c;

            Vector3 Sample(int r, int c)
            {
                int idx = Index(r, c, cols);
                if (idx < 0 || idx >= usable)
                {
                    return pos0;
                }

                float h = heights[idx] * scale.Y;
                float x = pos0.X + c * scale.X;
                float z = pos0.Z + r * scale.Z;
                float y = pos0.Y + h;
                return new Vector3(x, y, z);
            }

            var segments = new List<Vector3>(capacity: (int)Math.Min((long)capSegments * 2, 2_000_000));

            for (int r = 0; r < rows; r += stride)
            {
                int rNext = Math.Min(r + stride, rows - 1);
                for (int c = 0; c < cols; c += stride)
                {
                    int cNext = Math.Min(c + stride, cols - 1);

                    if (cNext != c)
                    {
                        segments.Add(Sample(r, c));
                        segments.Add(Sample(r, cNext));
                    }

                    if (rNext != r)
                    {
                        segments.Add(Sample(r, c));
                        segments.Add(Sample(rNext, c));
                    }

                    if (segments.Count / 2 >= capSegments)
                    {
                        return segments.ToArray();
                    }
                }
            }

            return segments.ToArray();
        }
    }
}
