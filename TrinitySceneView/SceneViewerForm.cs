using GFTool.Core.Flatbuffers.TR.Scene.Components;
using GFTool.Core.Flatbuffers.TR.Scene;
using GFTool.Renderer;
using GFTool.Renderer.Core;
using GFTool.Renderer.Core.Graphics;
using GFTool.Renderer.Scene.GraphicsObjects;
using OpenTK.Mathematics;
using System.Drawing;
using System.Text;
using Trinity.Core.Utils;
using TrinityModelViewer.Export;
using Point = System.Drawing.Point;
using GFTool.Renderer.Core;
using System.Linq;


namespace TrinitySceneView
{
    public partial class SceneViewerForm : Form
    {

        Point prevMousePos;

        private TRSceneTree sceneTree;
        private readonly SceneViewerConfig config;
        private string? assetRoot;
        private string? lastOpenedScenePath;
        private int? preferredSceneVariant;
        private bool suppressModelListEvents;
        private readonly List<LoadedSceneModelInstance> loadedSceneModelInstances = new();

        public SceneViewerForm()
        {
            InitializeComponent();

            config = SceneViewerConfig.Load();
            assetRoot = string.IsNullOrWhiteSpace(config.AssetRoot) ? null : config.AssetRoot;
            MessageHandler.Instance.DebugLogsEnabled = config.DebugLogs;
            ApplyTheme();
            RebuildSceneAssetProvider();
            InitializeEventViewerTab();
            InitializeNpcActorsTab();
            FormClosed += (_, _) =>
            {
                try
                {
                    cameraStatusTimer?.Stop();
                    cameraStatusTimer?.Dispose();
                }
                catch { }

                DisposeExtraAssetProviders();
            };

            var setRoot = new ToolStripMenuItem("Set Asset Root...");
            setRoot.Click += setAssetRoot_Click;
            assetsToolStripMenuItem.DropDownItems.Add(setRoot);

            var addGfpaks = new ToolStripMenuItem("Add GFPAK(s)...");
            addGfpaks.Click += addGfpaks_Click;
            assetsToolStripMenuItem.DropDownItems.Add(addGfpaks);

            var clearGfpaks = new ToolStripMenuItem("Clear GFPAK(s)");
            clearGfpaks.Click += clearGfpaks_Click;
            assetsToolStripMenuItem.DropDownItems.Add(clearGfpaks);

            assetsToolStripMenuItem.DropDownItems.Add(new ToolStripSeparator());

            var importHashes = new ToolStripMenuItem("Import GFPAK hash list...");
            importHashes.Click += importGfpakHashList_Click;
            assetsToolStripMenuItem.DropDownItems.Add(importHashes);

            var exportSceneGltf = new ToolStripMenuItem("Export Loaded Scene as glTF...");
            exportSceneGltf.Click += exportLoadedSceneAsGltf_Click;
            fileToolStripMenuItem.DropDownItems.Add(new ToolStripSeparator());
            fileToolStripMenuItem.DropDownItems.Add(exportSceneGltf);

            var openTrtrr = new ToolStripMenuItem("Open Terrain (.trtrr)...");
            openTrtrr.Click += openTRTRR_Click;
            sceneToolStripMenuItem.DropDownItems.Add(openTrtrr);

            var openTrtrrGfpak = new ToolStripMenuItem("Open Terrain (GFPAK)...");
            openTrtrrGfpak.Click += openTRTRR_FromGfpak_Click;
            sceneToolStripMenuItem.DropDownItems.Add(openTrtrrGfpak);

            sceneToolStripMenuItem.DropDownItems.Add(new ToolStripSeparator());

            var openTrhf = new ToolStripMenuItem("Open HeightField (.trhf)...");
            openTrhf.Click += openTRHF_Click;
            sceneToolStripMenuItem.DropDownItems.Add(openTrhf);

            var openTrhfGfpak = new ToolStripMenuItem("Open HeightField (GFPAK)...");
            openTrhfGfpak.Click += openTRHF_FromGfpak_Click;
            sceneToolStripMenuItem.DropDownItems.Add(openTrhfGfpak);

            sceneToolStripMenuItem.DropDownItems.Add(new ToolStripSeparator());

            var openBgParts = new ToolStripMenuItem("Open BG Parts Placement (.bin)...");
            openBgParts.Click += openBgPartsPlacement_Click;
            sceneToolStripMenuItem.DropDownItems.Add(openBgParts);

            var openBgPartsGfpak = new ToolStripMenuItem("Open BG Parts Placement (GFPAK)...");
            openBgPartsGfpak.Click += openBgPartsPlacementFromGfpak_Click;
            sceneToolStripMenuItem.DropDownItems.Add(openBgPartsGfpak);

            var openDoorData = new ToolStripMenuItem("Open Door Data (.bin)...");
            openDoorData.Click += openDoorData_Click;
            sceneToolStripMenuItem.DropDownItems.Add(openDoorData);

            var openDoorDataGfpak = new ToolStripMenuItem("Open Door Data (GFPAK)...");
            openDoorDataGfpak.Click += openDoorDataFromGfpak_Click;
            sceneToolStripMenuItem.DropDownItems.Add(openDoorDataGfpak);

            sceneToolStripMenuItem.DropDownItems.Add(new ToolStripSeparator());

            var openBgPartsModelSet = new ToolStripMenuItem("Open BG Parts Model Set (.bin)...");
            openBgPartsModelSet.Click += openBgPartsModelSet_Click;
            sceneToolStripMenuItem.DropDownItems.Add(openBgPartsModelSet);

            var openBgPartsModelSetGfpak = new ToolStripMenuItem("Open BG Parts Model Set (GFPAK)...");
            openBgPartsModelSetGfpak.Click += openBgPartsModelSetFromGfpak_Click;
            sceneToolStripMenuItem.DropDownItems.Add(openBgPartsModelSetGfpak);

            var darkModeItem = new ToolStripMenuItem("Dark Mode")
            {
                CheckOnClick = true,
                Checked = config.DarkMode
            };
            darkModeItem.CheckedChanged += (_, _) =>
            {
                config.DarkMode = darkModeItem.Checked;
                config.Save();
                ApplyTheme();
            };
            viewToolStripMenuItem.DropDownItems.Add(new ToolStripSeparator());
            viewToolStripMenuItem.DropDownItems.Add(darkModeItem);

            var debugLogsItem = new ToolStripMenuItem("Enable Debug Logs")
            {
                CheckOnClick = true,
                Checked = config.DebugLogs
            };
            debugLogsItem.CheckedChanged += (_, _) =>
            {
                config.DebugLogs = debugLogsItem.Checked;
                config.Save();
                MessageHandler.Instance.DebugLogsEnabled = config.DebugLogs;
                MessageHandler.Instance.AddMessage(MessageType.LOG, $"[Logs] Debug logs {(config.DebugLogs ? "enabled" : "disabled")}.");
            };
            viewToolStripMenuItem.DropDownItems.Add(debugLogsItem);

            var originItem = new ToolStripMenuItem("Spawn Models At Origin")
            {
                CheckOnClick = true,
                Checked = config.SpawnModelsAtOrigin
            };
            originItem.CheckedChanged += (_, _) =>
            {
                config.SpawnModelsAtOrigin = originItem.Checked;
                config.Save();
                MessageHandler.Instance.AddMessage(
                    MessageType.LOG,
                    $"[Scene] Spawn-at-origin {(config.SpawnModelsAtOrigin ? "enabled" : "disabled")}.");
            };
            sceneToolStripMenuItem.DropDownItems.Add(new ToolStripSeparator());
            sceneToolStripMenuItem.DropDownItems.Add(originItem);

            var clipItem = new ToolStripMenuItem("Large Clip Planes")
            {
                CheckOnClick = true,
                Checked = config.LargeClipPlanes
            };
            clipItem.CheckedChanged += (_, _) =>
            {
                config.LargeClipPlanes = clipItem.Checked;
                config.Save();
                ApplySceneClipPlanes(Vector3.Zero, 0f);
                MessageHandler.Instance.AddMessage(
                    MessageType.LOG,
                    $"[Scene] Large clip planes {(config.LargeClipPlanes ? "enabled" : "disabled")}.");
            };
            previewToolStripMenuItem.DropDownItems.Add(clipItem);

            bool updatingRotationUi = false;

            var rotXItem = new ToolStripMenuItem("Rotate Scene 180° (X)")
            {
                CheckOnClick = true,
                Checked = config.RotateModels180X
            };
            var rotYItem = new ToolStripMenuItem("Rotate Scene 180° (Y)")
            {
                CheckOnClick = true,
                Checked = config.RotateModels180Y
            };
            var rotActorXItem = new ToolStripMenuItem("Rotate Actors 180° (X)")
            {
                CheckOnClick = true,
                Checked = config.RotateActors180X
            };
            var rotActorYItem = new ToolStripMenuItem("Rotate Actors 180° (Y)")
            {
                CheckOnClick = true,
                Checked = config.RotateActors180Y
            };

            rotXItem.CheckedChanged += (_, _) =>
            {
                if (updatingRotationUi)
                {
                    return;
                }

                updatingRotationUi = true;
                try
                {
                    config.RotateModels180X = rotXItem.Checked;
                    if (rotXItem.Checked)
                    {
                        rotYItem.Checked = false;
                        config.RotateModels180Y = false;
                    }
                }
                finally
                {
                    updatingRotationUi = false;
                }

                config.Save();
                MessageHandler.Instance.AddMessage(
                    MessageType.LOG,
                    $"[Scene] 180° rotation X {(config.RotateModels180X ? "enabled" : "disabled")} (reload scene to apply).");
            };

            rotYItem.CheckedChanged += (_, _) =>
            {
                if (updatingRotationUi)
                {
                    return;
                }

                updatingRotationUi = true;
                try
                {
                    config.RotateModels180Y = rotYItem.Checked;
                    if (rotYItem.Checked)
                    {
                        rotXItem.Checked = false;
                        config.RotateModels180X = false;
                    }
                }
                finally
                {
                    updatingRotationUi = false;
                }

                config.Save();
                MessageHandler.Instance.AddMessage(
                    MessageType.LOG,
                    $"[Scene] 180° rotation Y {(config.RotateModels180Y ? "enabled" : "disabled")} (reload scene to apply).");
            };

            rotActorXItem.CheckedChanged += (_, _) =>
            {
                if (updatingRotationUi)
                {
                    return;
                }

                updatingRotationUi = true;
                try
                {
                    config.RotateActors180X = rotActorXItem.Checked;
                    if (rotActorXItem.Checked)
                    {
                        rotActorYItem.Checked = false;
                        config.RotateActors180Y = false;
                    }
                }
                finally
                {
                    updatingRotationUi = false;
                }

                config.Save();
                MessageHandler.Instance.AddMessage(
                    MessageType.LOG,
                    $"[Actors] 180° rotation X {(config.RotateActors180X ? "enabled" : "disabled")}.");
            };

            rotActorYItem.CheckedChanged += (_, _) =>
            {
                if (updatingRotationUi)
                {
                    return;
                }

                updatingRotationUi = true;
                try
                {
                    config.RotateActors180Y = rotActorYItem.Checked;
                    if (rotActorYItem.Checked)
                    {
                        rotActorXItem.Checked = false;
                        config.RotateActors180X = false;
                    }
                }
                finally
                {
                    updatingRotationUi = false;
                }

                config.Save();
                MessageHandler.Instance.AddMessage(
                    MessageType.LOG,
                    $"[Actors] 180° rotation Y {(config.RotateActors180Y ? "enabled" : "disabled")}.");
            };

            sceneToolStripMenuItem.DropDownItems.Add(new ToolStripSeparator());
            sceneToolStripMenuItem.DropDownItems.Add(rotXItem);
            sceneToolStripMenuItem.DropDownItems.Add(rotYItem);
            sceneToolStripMenuItem.DropDownItems.Add(new ToolStripSeparator());
            sceneToolStripMenuItem.DropDownItems.Add(rotActorXItem);
            sceneToolStripMenuItem.DropDownItems.Add(rotActorYItem);
            sceneToolStripMenuItem.DropDownItems.Add(new ToolStripSeparator());

            var mapActorsItem = new ToolStripMenuItem("Map Scene Rotation To Actors")
            {
                CheckOnClick = true,
                Checked = config.ApplySceneRotationToActors
            };
            mapActorsItem.CheckedChanged += (_, _) =>
            {
                config.ApplySceneRotationToActors = mapActorsItem.Checked;
                config.Save();
                MessageHandler.Instance.AddMessage(
                    MessageType.LOG,
                    $"[Actors] Scene rotation mapping {(config.ApplySceneRotationToActors ? "enabled" : "disabled")}.");
            };
            sceneToolStripMenuItem.DropDownItems.Add(mapActorsItem);

            var mapEventCamItem = new ToolStripMenuItem("Map Scene Rotation To Event Camera")
            {
                CheckOnClick = true,
                Checked = config.ApplySceneRotationToEventCamera
            };
            mapEventCamItem.CheckedChanged += (_, _) =>
            {
                config.ApplySceneRotationToEventCamera = mapEventCamItem.Checked;
                config.Save();
                MessageHandler.Instance.AddMessage(
                    MessageType.LOG,
                    $"[EventCam] Scene rotation mapping {(config.ApplySceneRotationToEventCamera ? "enabled" : "disabled")}.");
            };
            sceneToolStripMenuItem.DropDownItems.Add(mapEventCamItem);

            var lodItem = new ToolStripMenuItem("Load All LODs")
            {
                CheckOnClick = true,
                Checked = config.LoadAllLods
            };
            lodItem.CheckedChanged += (_, _) =>
            {
                config.LoadAllLods = lodItem.Checked;
                config.Save();
                MessageHandler.Instance.AddMessage(
                    MessageType.LOG,
                    $"[Scene] Load-all-LODs {(config.LoadAllLods ? "enabled" : "disabled")} (reload to apply).");
            };
            sceneToolStripMenuItem.DropDownItems.Add(lodItem);

            var additiveItem = new ToolStripMenuItem("Additive Loads")
            {
                CheckOnClick = true,
                Checked = config.AdditiveLoads
            };
            additiveItem.CheckedChanged += (_, _) =>
            {
                config.AdditiveLoads = additiveItem.Checked;
                config.Save();
                MessageHandler.Instance.AddMessage(
                    MessageType.LOG,
                    $"[Scene] Additive loads {(config.AdditiveLoads ? "enabled" : "disabled")}.");
            };
            sceneToolStripMenuItem.DropDownItems.Add(additiveItem);

            var hfDetail = new ToolStripMenuItem("HeightField Detail");
            AddHeightFieldDetailItem(hfDetail, "Low (fast)", 100_000);
            AddHeightFieldDetailItem(hfDetail, "Medium", 400_000);
            AddHeightFieldDetailItem(hfDetail, "High (slow)", 1_200_000);
            previewToolStripMenuItem.DropDownItems.Add(new ToolStripSeparator());
            previewToolStripMenuItem.DropDownItems.Add(hfDetail);

            var clearHf = new ToolStripMenuItem("Clear HeightField Preview");
            clearHf.Click += async (_, _) =>
            {
                if (renderCtrl?.renderer == null)
                {
                    return;
                }

                try
                {
                    await renderCtrl.renderer.ReplaceHeightFieldOverlayAsync(null);
                    renderCtrl.Invalidate();
                }
                catch
                {
                    // ignore
                }
            };
            previewToolStripMenuItem.DropDownItems.Add(clearHf);
        }

        private void AddHeightFieldDetailItem(ToolStripMenuItem parent, string label, int maxSegments)
        {
            var item = new ToolStripMenuItem(label)
            {
                CheckOnClick = true,
                Checked = config.HeightFieldMaxSegments == maxSegments
            };
            item.Click += (_, _) =>
            {
                config.HeightFieldMaxSegments = maxSegments;
                config.Save();

                foreach (ToolStripMenuItem? sibling in parent.DropDownItems.OfType<ToolStripMenuItem>())
                {
                    if (!ReferenceEquals(sibling, item))
                    {
                        sibling.Checked = false;
                    }
                }

                item.Checked = true;
                MessageHandler.Instance.AddMessage(MessageType.LOG, $"[Hayabusa] HeightField detail set: {label} ({maxSegments:N0} segments cap)");
            };
            parent.DropDownItems.Add(item);
        }

        private void ApplyTheme()
        {
            ApplyTheme(this);
        }

        private void ApplyTheme(Control root)
        {
            var isDark = config?.DarkMode == true;
            var back = isDark ? Color.FromArgb(30, 30, 30) : SystemColors.Control;
            var fore = isDark ? Color.Gainsboro : SystemColors.ControlText;
            var panelBack = isDark ? Color.FromArgb(40, 40, 40) : SystemColors.Control;
            var listBack = isDark ? Color.FromArgb(24, 24, 24) : SystemColors.Window;

            ApplyThemeRecursive(root, back, panelBack, listBack, fore, isDark);
        }

        private void ApplyThemeRecursive(Control control, Color back, Color panelBack, Color listBack, Color fore, bool isDark)
        {
            if (control is Form || control is Panel || control is SplitContainer || control is GroupBox)
            {
                control.BackColor = panelBack;
                control.ForeColor = fore;
            }
            else if (control is ListView || control is TreeView || control is TextBox)
            {
                control.BackColor = listBack;
                control.ForeColor = fore;
            }
            else if (control is MenuStrip || control is ToolStrip)
            {
                control.BackColor = back;
                control.ForeColor = fore;
            }
            else if (control is Button || control is CheckBox)
            {
                control.BackColor = back;
                control.ForeColor = fore;
            }
            else
            {
                control.BackColor = back;
                control.ForeColor = fore;
            }

            foreach (Control child in control.Controls)
            {
                ApplyThemeRecursive(child, back, panelBack, listBack, fore, isDark);
            }
        }

        private async void openTRSOT_Click(object sender, EventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                RestoreDirectory = true
            };
            ofd.Filter = "Trinity Scene (*.trscn;*.trsog;*.trsot)|*.trscn;*.trsog;*.trsot|All files (*.*)|*.*";
            if (!string.IsNullOrWhiteSpace(config.LastSceneDirectory))
            {
                try
                {
                    if (Directory.Exists(config.LastSceneDirectory))
                    {
                        ofd.InitialDirectory = config.LastSceneDirectory;
                    }
                }
                catch { }
            }
            else if (!string.IsNullOrWhiteSpace(lastOpenedScenePath))
            {
                try
                {
                    var dir = Path.GetDirectoryName(lastOpenedScenePath);
                    if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                    {
                        ofd.InitialDirectory = dir;
                    }
                }
                catch { }
            }
            else if (!string.IsNullOrWhiteSpace(assetRoot) && Directory.Exists(assetRoot))
            {
                ofd.InitialDirectory = assetRoot;
            }
            if (ofd.ShowDialog() != DialogResult.OK) return;

            lastOpenedScenePath = ofd.FileName;
            try
            {
                var dir = Path.GetDirectoryName(lastOpenedScenePath);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    config.LastSceneDirectory = dir;
                    config.Save();
                }
            }
            catch { }
            preferredSceneVariant = TryDetectVariantFromPath(lastOpenedScenePath);
            sceneView.Nodes.Clear();
            sceneTree = new TRSceneTree();
            sceneTree.DeserializeScene(ofd.FileName);
            sceneView.Nodes.Add(sceneTree.TreeNode);
            RebuildNpcSpawnerIndex(ofd.FileName);

            await TryLoadSceneModelsAsync(ofd.FileName);
        }

        private async void setAssetRoot_Click(object? sender, EventArgs e)
        {
            using var fbd = new FolderBrowserDialog();
            fbd.Description = "Select the extracted game content root (must contain folders like 'field_graphic', etc.)";
            fbd.UseDescriptionForTitle = true;
            if (!string.IsNullOrWhiteSpace(assetRoot) && Directory.Exists(assetRoot))
            {
                fbd.SelectedPath = assetRoot;
            }

            if (fbd.ShowDialog() != DialogResult.OK)
            {
                return;
            }

            assetRoot = fbd.SelectedPath;
            config.AssetRoot = assetRoot;
            config.Save();
            RebuildSceneAssetProvider();
            AutoMountGfpaksFromAssetRoot();
            EventViewerOnAssetRootChanged();

            MessageHandler.Instance.AddMessage(MessageType.LOG, $"[Scene] Asset root set to: {assetRoot}");
            if (!string.IsNullOrWhiteSpace(lastOpenedScenePath) && File.Exists(lastOpenedScenePath))
            {
                await TryLoadSceneModelsAsync(lastOpenedScenePath);
            }
        }

        private void expandToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TreeNode node = sceneView.SelectedNode;
            var pair = sceneTree.FindFirst(node);
            var meta = pair.Value;
            //Only expand nodes with external files
            if (meta.IsExternal)
                sceneTree.DeserializeScene(meta, pair.Key);
        }

        private void resetCameraToOriginToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ResetCameraToOrigin();
        }

        private void exportLoadedSceneAsGltf_Click(object? sender, EventArgs e)
        {
            if (loadedSceneModelInstances.Count == 0)
            {
                MessageBox.Show(this,
                    "There are no loaded static scene models to export.",
                    "Export Scene glTF",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            using var sfd = new SaveFileDialog();
            sfd.Filter = "glTF 2.0 (*.gltf)|*.gltf";
            sfd.FileName = !string.IsNullOrWhiteSpace(lastOpenedScenePath)
                ? $"{Path.GetFileNameWithoutExtension(lastOpenedScenePath)}.gltf"
                : "scene.gltf";

            if (sfd.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            try
            {
                UseWaitCursor = true;
                GltfExporter.ExportScene(loadedSceneModelInstances, sfd.FileName, Path.GetFileNameWithoutExtension(sfd.FileName));
                MessageBox.Show(this,
                    $"Exported:\n{sfd.FileName}",
                    "Export Scene glTF",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    $"Export failed:\n{ex.Message}",
                    "Export Scene glTF",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                UseWaitCursor = false;
            }
        }

        private void ClearProperties()
        {
            InfoBox.Text = string.Empty;
            propertyGrid.SelectedObject = null;
        }

        //Treeview context
        private void sceneView_MouseUp(object sender, MouseEventArgs e)
        {
            Point ClickPoint = new Point(e.X, e.Y);
            TreeNode ClickNode = sceneView.GetNodeAt(ClickPoint);
            sceneView.SelectedNode = ClickNode;
            if (ClickNode == null) return;

            if (e.Button == MouseButtons.Right)
            {
                Point ScreenPoint = sceneView.PointToScreen(ClickPoint);
                Point FormPoint = this.PointToClient(ScreenPoint);
                sceneContext.Show(this, FormPoint);
            }

            //Check for data to display
            var meta = sceneTree.GetNodeMeta(sceneView.SelectedNode);
            if (meta == null || meta?.Data == null)
            {
                // Allow lazy decode for certain chunk types that vary by version (e.g. PropertySheet).
                if (meta != null && meta.Value.Type == nameof(trinity_PropertySheet) && meta.Value.RawData != null)
                {
                    var decoded = TryDecodePropertySheet(meta.Value.RawData);
                    if (decoded != null)
                    {
                        InfoBox.Text = TRSceneProperties.GetProperties(nameof(trinity_PropertySheet), decoded);
                        propertyGrid.SelectedObject = new ScenePropertyGridProxy(meta.Value);
                        return;
                    }
                }

                ClearProperties();
                return;
            }

            InfoBox.Text = TRSceneProperties.GetProperties(meta?.Type, meta?.Data);
            propertyGrid.SelectedObject = new ScenePropertyGridProxy(meta.Value);

            // Best-effort: if the selected node represents an NPC spawner, show candidate models.
            TryUpdateSpawnerFromSelection(meta);
        }

        private static trinity_PropertySheet? TryDecodePropertySheet(byte[] data)
        {
            try
            {
                return FlatBufferConverter.DeserializeFrom<trinity_PropertySheet>(data);
            }
            catch (InvalidDataException)
            {
                // Fallback for chunks stored without a root uoffset: wrap with a 4-byte offset.
                if (data.Length < 8)
                {
                    return null;
                }

                try
                {
                    var wrapped = new byte[data.Length + 4];
                    BitConverter.GetBytes(4).CopyTo(wrapped, 0);
                    Buffer.BlockCopy(data, 0, wrapped, 4, data.Length);
                    return FlatBufferConverter.DeserializeFrom<trinity_PropertySheet>(wrapped);
                }
                catch
                {
                    return null;
                }
            }
            catch
            {
                return null;
            }
        }

        private sealed class ScenePropertyGridProxy
        {
            private readonly SceneMetaData meta;

            public ScenePropertyGridProxy(SceneMetaData meta)
            {
                this.meta = meta;
            }

            public bool IsExternal => meta.IsExternal;
            public string Type => meta.Type ?? string.Empty;
            public string FilePath => meta.FilePath ?? string.Empty;

            public string Summary
            {
                get
                {
                    try
                    {
                        if (meta.Data is trinity_SceneObject so)
                        {
                            return $"SceneObject '{so.Name}' tags={so.TagList?.Length ?? 0}";
                        }

                        if (meta.Data is trinity_ModelComponent mc)
                        {
                            return $"ModelComponent '{mc.FilePath}'";
                        }

                        if (meta.Data is trinity_PropertySheet ps)
                        {
                            // Avoid enumerating FlatSharp vectors here; some schema mismatches throw lazily.
                            return $"PropertySheet '{ps.name}' template='{ps.template}'";
                        }
                    }
                    catch
                    {
                        // Some FlatSharp vectors can throw when schema doesn't match; keep property grid stable.
                    }

                    return meta.Data?.GetType().Name ?? "(null)";
                }
            }
        }

    }
}
