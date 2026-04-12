using GFTool.Renderer.Core;
using GFTool.Renderer.Scene.GraphicsObjects;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Trinity.Core.Assets;
using Trinity.Core.Flatbuffers.TR.Model;
using TrinityModelViewer.Export;

namespace TrinityModelViewer
{
    public partial class ModelViewerForm
    {
        private bool shaderWarmupCompleted;
        private Task? flatSharpWarmupTask;

        private void ClearAll()
        {
            sceneModelManager.DisposeAssetProviders();
            ClearTeraPtclEffects();

            renderCtrl.renderer.ClearScene();
            renderCtrl.renderer.StopAnimation();
	            messageListView.Items.Clear();
            materialList.Items.Clear();
            materialList.Columns.Clear();
            modelMap.Clear();
            sceneTree.Nodes.Clear();
            animations.Clear();
            animationsList.Items.Clear();
            loadedAnimationPaths.Clear();
	            currentMaterialsModel = null;
	            currentMaterial = null;
	            ClearMaterialDetails();
	            sceneModelManager.ClearSceneTracking();
	        }

        private void UpdateUvOverrideMenuChecks()
        {
            if (contextMenuNode?.Tag is not NodeTag tag || tag.SubmeshIndices == null || tag.SubmeshIndices.Count == 0)
            {
                return;
            }

            var (layerMaskOverride, aoOverride) = tag.Model.GetUvOverrides(tag.SubmeshIndices[0]);

            if (layerMaskUvMaterialContextMenuItem != null) layerMaskUvMaterialContextMenuItem.Checked = layerMaskOverride == UvSetOverride.Material;
            if (layerMaskUv0ContextMenuItem != null) layerMaskUv0ContextMenuItem.Checked = layerMaskOverride == UvSetOverride.Uv0;
            if (layerMaskUv1ContextMenuItem != null) layerMaskUv1ContextMenuItem.Checked = layerMaskOverride == UvSetOverride.Uv1;

            if (aoUvMaterialContextMenuItem != null) aoUvMaterialContextMenuItem.Checked = aoOverride == UvSetOverride.Material;
            if (aoUv0ContextMenuItem != null) aoUv0ContextMenuItem.Checked = aoOverride == UvSetOverride.Uv0;
            if (aoUv1ContextMenuItem != null) aoUv1ContextMenuItem.Checked = aoOverride == UvSetOverride.Uv1;
        }

        private void layerMaskUvMaterialToolStripMenuItem_Click(object? sender, EventArgs e)
        {
            SetMeshLayerMaskUvOverride(UvSetOverride.Material);
        }

        private void layerMaskUv0ToolStripMenuItem_Click(object? sender, EventArgs e)
        {
            SetMeshLayerMaskUvOverride(UvSetOverride.Uv0);
        }

        private void layerMaskUv1ToolStripMenuItem_Click(object? sender, EventArgs e)
        {
            SetMeshLayerMaskUvOverride(UvSetOverride.Uv1);
        }

        private void aoUvMaterialToolStripMenuItem_Click(object? sender, EventArgs e)
        {
            SetMeshAOUvOverride(UvSetOverride.Material);
        }

        private void aoUv0ToolStripMenuItem_Click(object? sender, EventArgs e)
        {
            SetMeshAOUvOverride(UvSetOverride.Uv0);
        }

        private void aoUv1ToolStripMenuItem_Click(object? sender, EventArgs e)
        {
            SetMeshAOUvOverride(UvSetOverride.Uv1);
        }

        private void SetMeshLayerMaskUvOverride(UvSetOverride value)
        {
            if (contextMenuNode?.Tag is not NodeTag tag || tag.SubmeshIndices == null || tag.SubmeshIndices.Count == 0)
            {
                return;
            }

            foreach (var submeshIndex in tag.SubmeshIndices)
            {
                var uvOverrides = tag.Model.GetUvOverrides(submeshIndex);
                tag.Model.SetUvOverrides(submeshIndex, value, uvOverrides.AO);
            }

            UpdateUvOverrideMenuChecks();
            renderCtrl.Invalidate();
        }

        private void SetMeshAOUvOverride(UvSetOverride value)
        {
            if (contextMenuNode?.Tag is not NodeTag tag || tag.SubmeshIndices == null || tag.SubmeshIndices.Count == 0)
            {
                return;
            }

            foreach (var submeshIndex in tag.SubmeshIndices)
            {
                var uvOverrides = tag.Model.GetUvOverrides(submeshIndex);
                tag.Model.SetUvOverrides(submeshIndex, uvOverrides.LayerMask, value);
            }

            UpdateUvOverrideMenuChecks();
            renderCtrl.Invalidate();
        }

        private void ExportEditedMaterialsForCurrentModel()
        {
            var mdl = currentMaterialsModel;
            if (mdl == null)
            {
                MessageBox.Show(this, "No model selected.", "Export Materials", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (string.IsNullOrWhiteSpace(mdl.CurrentMaterialFilePath) || !File.Exists(mdl.CurrentMaterialFilePath))
            {
                MessageBox.Show(this, "Could not resolve the source .trmtr path for the current material set.", "Export Materials", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

	            if (!mdl.GetMaterials().Any(m => m.HasUniformOverrides) &&
	                !mdl.GetMaterials().Any(m => m.HasSamplerOverrides) &&
	                !mdl.HasMaterialSourceEdits &&
	                !mdl.HasMaterialMetadataSelectionOverrides &&
	                !mdl.HasMaterialMetadataValueOverrides)
	            {
	                var r = MessageBox.Show(this, "No edited material parameters detected (no overrides).\nExport anyway?", "Export Materials",
	                    MessageBoxButtons.YesNo, MessageBoxIcon.Question);
		                if (r != DialogResult.Yes)
		                {
                    return;
                }
            }

            using var sfd = new SaveFileDialog();
            sfd.Title = "Export Edited Materials (.trmtr)";
            sfd.Filter = "TRMTR (*.trmtr)|*.trmtr";
            sfd.FileName = Path.GetFileName(mdl.CurrentMaterialFilePath);
            if (sfd.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

		            try
		            {
		                TrinityModelViewer.Export.EditedMaterialExporter.ExportEditedTrmtr(mdl.CurrentMaterialFilePath, mdl, sfd.FileName);
		                string? trmmtOut = null;
		                var cloneRequests = mdl.GetNewMaterialCloneRequestsSnapshot();
		                bool wantsTrmmt = (mdl.HasMaterialMetadataSelectionOverrides || mdl.HasMaterialMetadataValueOverrides) ||
		                                 cloneRequests.Any(r => r.TrmmtCloneMode != Model.NewMaterialTrmmtCloneMode.None);
		                if (wantsTrmmt)
		                {
		                    string? trmmtSource = mdl.LoadedMaterialMetadataPath ?? mdl.PreferredMaterialMetadataPath;
		                    if (string.IsNullOrWhiteSpace(trmmtSource) && !string.IsNullOrWhiteSpace(mdl.CurrentMaterialFilePath))
		                    {
		                        trmmtSource = Path.ChangeExtension(mdl.CurrentMaterialFilePath, ".trmmt");
		                    }

			                    if (string.IsNullOrWhiteSpace(trmmtSource) && sceneModelManager.TryGetModelSourcePath(mdl, out var trmdlSource) && !string.IsNullOrWhiteSpace(trmdlSource))
			                    {
			                        trmmtSource = Path.ChangeExtension(trmdlSource, ".trmmt");
			                    }

		                    if (!string.IsNullOrWhiteSpace(trmmtSource) && File.Exists(trmmtSource))
		                    {
		                        // Keep the exported metadata filename consistent with the imported source.
		                        var outDir = Path.GetDirectoryName(sfd.FileName) ?? Environment.CurrentDirectory;
		                        trmmtOut = Path.Combine(outDir, Path.GetFileName(trmmtSource));
		                        TrinityModelViewer.Export.EditedMaterialMetadataExporter.ExportEditedTrmmt(trmmtSource, mdl, trmmtOut);
		                    }
		                    else
		                    {
		                        MessageHandler.Instance.AddMessage(MessageType.LOG,
		                            $"[Export] Skipped TRMMT export (source missing). Looked for '{trmmtSource ?? "<null>"}'.");
		                    }
		                }

		                var msg = trmmtOut == null
		                    ? $"Exported:\n{sfd.FileName}"
		                    : $"Exported:\n{sfd.FileName}\n{trmmtOut}";
                MessageBox.Show(this, msg, "Export Materials", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Export failed:\n{ex.Message}", "Export Materials", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async Task<Model?> AddModelToSceneAsync(string filePath, IAssetProvider? assetProvider = null, bool transient = false)
        {
            bool ownsLoad = modelLoadDepth == 0;
            if (ownsLoad)
            {
                BeginModelLoad();
            }

            try
            {
                ReportModelLoadProgress(0);

                var progress = new Progress<float>(p =>
                {
                    int percent = (int)Math.Round(Math.Clamp(p, 0.0f, 1.0f) * 100.0);
                    ReportModelLoadProgress(percent);
                });

                var token = modelLoadCts?.Token ?? CancellationToken.None;
                var mdl = assetProvider == null
                    ? await renderCtrl.renderer.AddSceneModelAsync(filePath, settings.LoadAllLods, token: token, progress: progress)
                    : await renderCtrl.renderer.AddSceneModelAsync(assetProvider, filePath, settings.LoadAllLods, token: token, progress: progress);

                var node = new TreeNode(mdl.Name)
                {
                    Tag = new NodeTag
                    {
                        Type = NodeType.ModelRoot,
                        Model = mdl
                    }
                };
                modelMap.Add(node, mdl);
                sceneTree.Nodes.Add(node);
                PopulateSubmeshes(node, mdl);
                PopulateMaterials(mdl);
                ReportModelLoadProgress(85);

                if (assetProvider == null)
                {
                    TryAutoLoadAnimations(filePath);
                }
                else
                {
                    sceneModelManager.RegisterAssetProvider(assetProvider);
                }
                ReportModelLoadProgress(95);

                // Default to "solo" display for the most recently added model unless multi-model display is enabled.
                ApplyModelVisibility(mdl);
                sceneTree.SelectedNode = node;
                node.EnsureVisible();

                if (!transient)
                {
                    if (assetProvider == null)
                    {
                        settings.LastModelPath = filePath;
                        settings.Save();
                        UpdateLastModelMenu();
                        AddRecentModel(filePath);
                        sceneModelManager.AddLoadedModelPath(filePath);
                        sceneModelManager.SetModelSourcePath(mdl, filePath);
                    }
                    else if (assetProvider is Trinity.Core.Assets.GfpakAssetProvider gfpakProvider)
                    {
                        sceneModelManager.SetModelGfpakSource(mdl, gfpakProvider.ContainerPath, filePath);
                    }
                }

                if (settings.ShowMultipleModels && renderCtrl.renderer.HasActiveAnimation())
                {
                    renderCtrl.renderer.SetAnimationTargets(modelMap.Values);
                }

                ReportModelLoadProgress(100);
                return mdl;
            }
            catch (OperationCanceledException)
            {
                MessageHandler.Instance.AddMessage(MessageType.LOG, "[Load] Model load canceled.");
                return null;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Failed to load model:\n{ex.Message}", "Load Model", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }
            finally
            {
                if (ownsLoad)
                {
                    EndModelLoad();
                }
            }
        }

        private void SelectMaterialByName(string materialName)
        {
            if (string.IsNullOrWhiteSpace(materialName))
            {
                return;
            }

            foreach (ListViewItem item in materialList.Items)
            {
                if (item.Tag is Material mat && string.Equals(mat.Name, materialName, StringComparison.OrdinalIgnoreCase))
                {
                    item.Selected = true;
                    item.Focused = true;
                    item.EnsureVisible();
                    break;
                }
            }
        }
    }
}
