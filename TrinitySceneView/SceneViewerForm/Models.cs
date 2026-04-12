using GFTool.Core.Flatbuffers.TR.Scene.Components;
using GFTool.Core.Flatbuffers.TR.Scene;
using GFTool.Renderer;
using GFTool.Renderer.Core;
using GFTool.Renderer.Core.Graphics;
using GFTool.Renderer.Scene.GraphicsObjects;
using OpenTK.Mathematics;
using System.Drawing;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Trinity.Core.Utils;
using Point = System.Drawing.Point;
using GFTool.Renderer.Core;


namespace TrinitySceneView
{
    public partial class SceneViewerForm : Form
    {
        private NpcSpawnerDbCache? npcSpawnerDb;
        private string? selectedSpawnerId;

        private CancellationTokenSource? sceneLoadCts;
        private bool isSceneLoading;
        private int sceneLoadVersion;

        private Task TryLoadSceneModelsAsync(string sceneFile)
        {
            return TryLoadSceneModelsAsync(sceneFile, CancellationToken.None);
        }

        private async Task TryLoadSceneModelsAsync(string sceneFile, CancellationToken externalToken)
        {
            if (renderCtrl?.renderer == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(assetRoot) || !Directory.Exists(assetRoot))
            {
                MessageHandler.Instance.AddMessage(MessageType.WARNING, "[Scene] Asset root not set; use File -> Set Asset Root... to enable model loading.");
                return;
            }

            CancelSceneLoad();
            int loadVersion = Interlocked.Increment(ref sceneLoadVersion);
            var myCts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
            sceneLoadCts = myCts;
            var token = myCts.Token;

            isSceneLoading = true;
            if (!config.AdditiveLoads)
            {
                renderCtrl.renderer.ClearScene();
                ClearModelsList();
                loadedSceneModelInstances.Clear();
            }
            renderCtrl.Invalidate();

            BeginSceneLoadUi(loadVersion, "Scanning scene...");

            try
            {
                var result = await Task.Run(() => CollectSceneModelSpawns(sceneFile, token), token);
                npcSpawnerDb = result.NpcDb;
                var spawns = result.Spawns;

                if (spawns.Count == 0)
                {
                    EndSceneLoadUi(loadVersion, "No models found.");
                    return;
                }

                var spawnedPositions = new List<Vector3>(spawns.Count);
                int completed = 0;
                int total = spawns.Count;

                for (int i = 0; i < spawns.Count; i++)
                {
                    token.ThrowIfCancellationRequested();

                    var spawn = spawns[i];
                    var resolved = ResolveModelPath(spawn.ModelPath);
                    if (resolved == null)
                    {
                        completed++;
                        int percentMissing = ComputeOverallPercent(total, completed, 1.0f);
                        ReportSceneLoadUi(loadVersion, percentMissing, $"Missing model ({completed}/{total})");
                        MessageHandler.Instance.AddMessage(
                            MessageType.WARNING,
                            $"[Scene] Missing model file: {spawn.ModelPath} (SceneObject={spawn.SceneObjectName ?? "(null)"}, scene={Path.GetFileName(spawn.SceneFile)})");
                        continue;
                    }

                    var progress = new Progress<float>(p =>
                    {
                        int percent = ComputeOverallPercent(total, completed, p);
                        ReportSceneLoadUi(loadVersion, percent, $"Loading {completed + 1}/{total}: {Path.GetFileNameWithoutExtension(resolved)}");
                    });

                    Model model;
                    try
                    {
                        model = await renderCtrl.renderer.AddSceneModelAsync(resolved, token: token, progress: progress);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        completed++;
                        int percentFailed = ComputeOverallPercent(total, completed, 1.0f);
                        ReportSceneLoadUi(loadVersion, percentFailed, $"Failed ({completed}/{total})");
                        MessageHandler.Instance.AddMessage(
                            MessageType.WARNING,
                            $"[Scene] Failed to load model '{resolved}' (SceneObject={spawn.SceneObjectName ?? "(null)"}): {ex.GetType().Name}: {ex.Message}");
                        continue;
                    }

                    var mat = ApplyViewerMatrixOptions(spawn.ModelMatrix, config.SpawnModelsAtOrigin, config.RotateModels180X, config.RotateModels180Y, out var position, out var scale);
                    model.SetModelMatrix(mat);
                    spawnedPositions.Add(position);
                    AddModelToList(spawn.SceneObjectName, spawn.ModelPath, model);
                    loadedSceneModelInstances.Add(new LoadedSceneModelInstance
                    {
                        Name = string.IsNullOrWhiteSpace(spawn.SceneObjectName)
                            ? Path.GetFileNameWithoutExtension(spawn.ModelPath)
                            : spawn.SceneObjectName,
                        SourcePath = resolved,
                        Transform = mat,
                        Model = model
                    });

                    completed++;
                    int percentDone = ComputeOverallPercent(total, completed, 1.0f);
                    ReportSceneLoadUi(loadVersion, percentDone, $"Loaded {completed}/{total}");

                    if (MessageHandler.Instance.DebugLogsEnabled)
                    {
                        MessageHandler.Instance.AddMessage(
                            MessageType.LOG,
                            $"[Scene] Model '{spawn.SceneObjectName}' -> '{spawn.ModelPath}' pos={position} scale={scale}{(config.SpawnModelsAtOrigin ? " (origin override)" : "")}");
                    }
                }

                if (spawnedPositions.Count > 0)
                {
                    var min = new Vector3(float.PositiveInfinity);
                    var max = new Vector3(float.NegativeInfinity);
                    foreach (var p in spawnedPositions)
                    {
                        min = Vector3.ComponentMin(min, p);
                        max = Vector3.ComponentMax(max, p);
                    }

                    var center = (min + max) * 0.5f;
                    var radius = (max - min).Length * 0.5f;
                    // Start close for small props, but auto-dolly out for large scenes.
                    var distance = MathF.Max(2.5f, radius * 2.5f);
                    renderCtrl.renderer.FocusCamera(center, distance);
                    ApplySceneClipPlanes(center, radius);

                    if (MessageHandler.Instance.DebugLogsEnabled)
                    {
                        MessageHandler.Instance.AddMessage(
                        MessageType.LOG,
                        $"[Scene] Focus camera at {center} (models={spawnedPositions.Count}, radius≈{radius:0.###}, dist≈{distance:0.###}).");
	                    }
	                }

	                EndSceneLoadUi(loadVersion, $"Loaded {completed}/{total} model(s).");
	            }
	            catch (OperationCanceledException)
	            {
	                EndSceneLoadUi(loadVersion, "Load canceled.");
	                MessageHandler.Instance.AddMessage(MessageType.LOG, "[Scene] Load canceled.");
            }
            catch (Exception ex)
            {
                EndSceneLoadUi(loadVersion, "Load failed.");
                MessageHandler.Instance.AddMessage(MessageType.ERROR, $"[Scene] Load failed: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                if (sceneLoadVersion == loadVersion)
                {
                    isSceneLoading = false;
                    renderCtrl.Invalidate();
                }

                try { myCts.Dispose(); } catch { }
                if (ReferenceEquals(sceneLoadCts, myCts))
                {
                    sceneLoadCts = null;
                }
            }
        }

        private void CancelSceneLoad()
        {
            try
            {
                sceneLoadCts?.Cancel();
            }
            catch
            {
                // ignore
            }
        }

        private void BeginSceneLoadUi(int loadVersion, string message)
        {
            if (InvokeRequired)
            {
                BeginInvoke((Action)(() => BeginSceneLoadUi(loadVersion, message)));
                return;
            }

            if (sceneLoadVersion != loadVersion)
            {
                return;
            }

            loadingProgressBar.Value = 0;
            loadingProgressBar.Visible = true;
            statusLbl.Text = message;
        }

        private void ReportSceneLoadUi(int loadVersion, int percent, string message)
        {
            if (InvokeRequired)
            {
                BeginInvoke((Action)(() => ReportSceneLoadUi(loadVersion, percent, message)));
                return;
            }

            if (sceneLoadVersion != loadVersion)
            {
                return;
            }

            loadingProgressBar.Value = Math.Clamp(percent, 0, 100);
            statusLbl.Text = message;
        }

        private void EndSceneLoadUi(int loadVersion, string message)
        {
            if (InvokeRequired)
            {
                BeginInvoke((Action)(() => EndSceneLoadUi(loadVersion, message)));
                return;
            }

            if (sceneLoadVersion != loadVersion)
            {
                return;
            }

            loadingProgressBar.Value = 100;
            statusLbl.Text = message;
            loadingProgressBar.Visible = false;
        }

        private static int ComputeOverallPercent(int total, int completed, float currentModelProgress)
        {
            if (total <= 0)
            {
                return 0;
            }

            float scanPortion = 0.05f;
            float loadPortion = 1.0f - scanPortion;

            float doneModels = Math.Clamp(completed, 0, total);
            float modelP = Math.Clamp(currentModelProgress, 0f, 1f);
            float overall = scanPortion + loadPortion * ((doneModels + modelP) / total);
            return (int)Math.Round(overall * 100.0f);
        }

        private void ClearModelsList()
        {
            suppressModelListEvents = true;
            try
            {
                modelsListView.Items.Clear();
            }
            finally
            {
                suppressModelListEvents = false;
            }
        }

        private void ApplySceneClipPlanes(Vector3 center, float radius)
        {
            if (renderCtrl?.renderer == null)
            {
                return;
            }

            if (!config.LargeClipPlanes)
            {
                renderCtrl.renderer.SetCameraClipPlanes(0.1f, 100.0f);
                return;
            }

            // Keep far clip generous for large-world Trinity scenes.
            var far = MathF.Max(10_000.0f, radius * 200.0f);
            renderCtrl.renderer.SetCameraClipPlanes(0.1f, far);
        }

        private void AddModelToList(string? sceneObjectName, string modelPath, Model model)
        {
            string name = string.IsNullOrWhiteSpace(sceneObjectName)
                ? Path.GetFileNameWithoutExtension(modelPath)
                : sceneObjectName;

            var item = new ListViewItem(name)
            {
                Checked = model.IsVisible,
                Tag = model
            };
            item.SubItems.Add(modelPath);

            suppressModelListEvents = true;
            try
            {
                modelsListView.Items.Add(item);
            }
            finally
            {
                suppressModelListEvents = false;
            }
        }

        private void modelsListView_ItemChecked(object sender, ItemCheckedEventArgs e)
        {
            if (suppressModelListEvents)
            {
                return;
            }

            if (e.Item?.Tag is Model model)
            {
                model.SetVisible(e.Item.Checked);
                renderCtrl.Invalidate();
            }
        }

        private static Matrix4 ApplyViewerMatrixOptions(Matrix4 baseMat, bool forceOrigin, bool rotate180x, bool rotate180y, out Vector3 position, out Vector3 scaleOut)
        {
            var mat = baseMat;

            if (forceOrigin)
            {
                mat.M41 = 0f;
                mat.M42 = 0f;
                mat.M43 = 0f;
            }
            position = new Vector3(mat.M41, mat.M42, mat.M43);

            scaleOut = new Vector3(
                new Vector3(mat.M11, mat.M12, mat.M13).Length,
                new Vector3(mat.M21, mat.M22, mat.M23).Length,
                new Vector3(mat.M31, mat.M32, mat.M33).Length);

            if (!rotate180x)
            {
                if (!rotate180y)
                {
                    return mat;
                }
            }

            if (rotate180y)
            {
                mat = Matrix4.CreateRotationY(MathHelper.Pi) * mat;
            }

            if (rotate180x)
            {
                mat = Matrix4.CreateRotationX(MathHelper.Pi) * mat;
            }

            return mat;
        }

        private string? ResolveModelPath(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(assetRoot))
            {
                return null;
            }

            // Allow absolute paths.
            if (Path.IsPathRooted(filePath))
            {
                if (File.Exists(filePath))
                {
                    return filePath;
                }

                // Some scene files embed absolute authoring paths. If the file isn't present, try to
                // re-root under the configured asset root by stripping to a known content folder.
                var rerooted = TryRerootUnderAssetRoot(filePath, assetRoot);
                if (rerooted != null)
                {
                    return rerooted;
                }
            }

            var normalized = filePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            var combined = Path.GetFullPath(Path.Combine(assetRoot, normalized));
            if (File.Exists(combined))
            {
                return combined;
            }

            // Be forgiving if the configured asset root is a subfolder of the real dump root.
            // Try a few parents so references like "ik_chara/..." can still resolve.
            try
            {
                var cur = assetRoot;
                for (int i = 0; i < 3; i++)
                {
                    var parent = Directory.GetParent(cur);
                    if (parent == null)
                    {
                        break;
                    }

                    cur = parent.FullName;
                    var parentCombined = Path.GetFullPath(Path.Combine(cur, normalized));
                    if (File.Exists(parentCombined))
                    {
                        return parentCombined;
                    }
                }
            }
            catch
            {
                // ignore
            }

            return null;
        }

        private static string? TryRerootUnderAssetRoot(string rootedPath, string assetRoot)
        {
            if (string.IsNullOrWhiteSpace(rootedPath) || string.IsNullOrWhiteSpace(assetRoot))
            {
                return null;
            }

            string normalized = rootedPath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            // Prefer earlier matches (closest to drive root).
            string[] knownRoots =
            {
                "ai_influence",
                "avalon",
                "field_graphic",
                "field",
                "ik_ai_behavior",
                "ik_chara",
                "ik_demo",
                "ik_effect",
                "ik_event",
                "ik_message",
                "ik_pokemon",
                "light",
                "param_ai",
                "param_chr",
                "script",
                "system",
                "system_resource",
                "world",
                "common",
                "legend",
                "model",
                "effect",
                "ui"
            };

            foreach (var root in knownRoots)
            {
                string needle = Path.DirectorySeparatorChar + root + Path.DirectorySeparatorChar;
                int idx = normalized.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
                if (idx < 0)
                {
                    continue;
                }

                // Keep the root folder name itself (e.g. "field\...").
                string relative = normalized.Substring(idx + 1);
                string candidate = Path.GetFullPath(Path.Combine(assetRoot, relative));
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        private string? ResolveSceneReference(string sceneFile, string referencedPath)
        {
            if (string.IsNullOrWhiteSpace(sceneFile) || string.IsNullOrWhiteSpace(referencedPath))
            {
                return null;
            }

            string baseDir = Path.GetDirectoryName(sceneFile) ?? string.Empty;
            string normalized = referencedPath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);

            // Try direct path relative to the scene file.
            string candidate = Path.GetFullPath(Path.Combine(baseDir, normalized));
            if (File.Exists(candidate))
            {
                return candidate;
            }

            // Try common versioned suffixes: *_0.ext and *_1.ext.
            string ext = Path.GetExtension(candidate);
            if (!string.IsNullOrWhiteSpace(ext))
            {
                // If the reference already includes an explicit variant suffix, don't try to append another.
                string fileNameNoExt = Path.GetFileNameWithoutExtension(candidate);
                if (fileNameNoExt.EndsWith("_0", StringComparison.OrdinalIgnoreCase) ||
                    fileNameNoExt.EndsWith("_1", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                string noExt = candidate.Substring(0, candidate.Length - ext.Length);
                var variants = preferredSceneVariant.HasValue
                    ? new[] { preferredSceneVariant.Value, preferredSceneVariant.Value == 0 ? 1 : 0 }
                    : new[] { 0, 1 };

                foreach (int variant in variants)
                {
                    string withVariant = $"{noExt}_{variant}{ext}";
                    if (File.Exists(withVariant))
                    {
                        return withVariant;
                    }
                }
            }

            return null;
        }

        private static int? TryDetectVariantFromPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            string name = Path.GetFileNameWithoutExtension(path);
            if (name.EndsWith("_0", StringComparison.OrdinalIgnoreCase)) return 0;
            if (name.EndsWith("_1", StringComparison.OrdinalIgnoreCase)) return 1;
            return null;
        }
    }
}
