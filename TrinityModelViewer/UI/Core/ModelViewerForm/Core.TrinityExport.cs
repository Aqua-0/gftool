using GFTool.Renderer.Scene.GraphicsObjects;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using BnTxx;
using Trinity.Core.Assets;
using Trinity.Core.Flatbuffers.TR.Model;
using TrinityModelViewer.Export;

namespace TrinityModelViewer
{
    public partial class ModelViewerForm
    {
        private void ExportTrinityFromSelection()
        {
            var selected = sceneTree.SelectedNode;
            if (selected?.Tag is not NodeTag tag || tag.Type != NodeType.ModelRoot)
            {
	                MessageBox.Show(this, "Select a model root node first (the top-level model entry in the scene tree).", "Export Trinity",
	                    MessageBoxButtons.OK, MessageBoxIcon.Information);
	                return;
	            }

		            bool hasGltfPreview = gltfImportContextByModel.TryGetValue(tag.Model, out var ctx);
		            string? referenceTrmdlPath = null;
		            bool referenceIsGfpak = false;
		            string? gfpakPath = null;
		            string? gfpakEntry = null;

		            if (hasGltfPreview)
		            {
		                referenceTrmdlPath = ctx.ReferenceTrmdlPath;
		            }
		            else if (sceneModelManager.TryGetModelSourcePath(tag.Model, out var src) && !string.IsNullOrWhiteSpace(src) && File.Exists(src))
		            {
		                referenceTrmdlPath = src;
		            }
		            else if (sceneModelManager.TryGetModelGfpakSource(tag.Model, out var container, out var entry) &&
		                     !string.IsNullOrWhiteSpace(container) && !string.IsNullOrWhiteSpace(entry))
		            {
		                referenceIsGfpak = true;
		                gfpakPath = container;
		                gfpakEntry = entry;
		            }
		            else
		            {
		                MessageBox.Show(this, "Could not resolve the source .trmdl path for this model.", "Export Trinity",
		                    MessageBoxButtons.OK, MessageBoxIcon.Error);
		                return;
		            }

	            using var sfd = new SaveFileDialog();
	            sfd.Title = "Export Trinity Model Set (.trmdl)";
	            sfd.Filter = "TRMDL (*.trmdl)|*.trmdl";
	            string initialDir = Environment.CurrentDirectory;
	            if (!string.IsNullOrWhiteSpace(settings.LastExportTrinityDirectory) && Directory.Exists(settings.LastExportTrinityDirectory))
	            {
	                initialDir = settings.LastExportTrinityDirectory;
	            }
		            else if (!referenceIsGfpak && !string.IsNullOrWhiteSpace(referenceTrmdlPath))
		            {
		                var srcDir = Path.GetDirectoryName(referenceTrmdlPath);
		                if (!string.IsNullOrWhiteSpace(srcDir))
		                {
		                    initialDir = Path.Combine(srcDir, "trinity_export");
		                }
		            }
		            else if (referenceIsGfpak && !string.IsNullOrWhiteSpace(gfpakPath))
		            {
		                var packDir = Path.GetDirectoryName(gfpakPath);
		                if (!string.IsNullOrWhiteSpace(packDir))
		                {
		                    initialDir = Path.Combine(packDir, "trinity_export");
		                }
		            }

		            if (!Directory.Exists(initialDir))
		            {
		                Directory.CreateDirectory(initialDir);
		            }

	            sfd.InitialDirectory = initialDir;
	            sfd.FileName = !string.IsNullOrWhiteSpace(referenceTrmdlPath)
	                ? Path.GetFileName(referenceTrmdlPath)
	                : (!string.IsNullOrWhiteSpace(gfpakEntry) ? Path.GetFileName(gfpakEntry) : "model.trmdl");
	            if (sfd.ShowDialog(this) != DialogResult.OK)
	            {
	                return;
	            }

		            try
		            {
		                var outFull = Path.GetFullPath(sfd.FileName);
		                if (!referenceIsGfpak && !string.IsNullOrWhiteSpace(referenceTrmdlPath))
		                {
		                    var refFull = Path.GetFullPath(referenceTrmdlPath);
		                    if (string.Equals(outFull, refFull, StringComparison.OrdinalIgnoreCase))
		                    {
		                        MessageBox.Show(this, "Refusing to export over the original imported .trmdl. Pick a different output path.", "Export Trinity",
		                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
		                        return;
		                    }
		                }

		                if (hasGltfPreview)
		                {
		                    TrinityModelViewer.Export.GltfTrinityPipeline.Export(
		                        referenceTrmdlPath,
		                        ctx.GltfPath,
		                        sfd.FileName,
		                        patchBaseColorTextures: false,
		                        exportModelPcBaseOnExport: settings.ExportModelPcBaseOnExport);
		                }
		                else if (!referenceIsGfpak)
		                {
		                    TrinityModelViewer.Export.TrinityModelSetExporter.ExportCopy(referenceTrmdlPath, sfd.FileName);
		                }
		                else
		                {
		                    TrinityModelViewer.Export.GfpakModelSetExtractor.ExportCopy(gfpakPath!, gfpakEntry!, sfd.FileName, includeProtagBaseSkeleton: true);
		                }

		                var patchNotes = new List<string>();
		                if (settings.AutoGenerateLodsOnExport)
		                {
		                    if (TrinityModelViewer.Export.TrmdlLodPatcher.ForceAllLodsToUseMesh0(sfd.FileName, out var lodError))
		                    {
		                        patchNotes.Add("Auto-generate LODs: forced all LODs to use LOD0 mesh (placeholder).");
		                    }
		                    else if (!string.IsNullOrWhiteSpace(lodError))
		                    {
		                        patchNotes.Add($"Auto-generate LODs failed: {lodError}");
		                    }
		                }
		                TryPatchExportedTrinityMaterials(sfd.FileName, tag.Model, patchNotes);

		                settings.LastExportTrinityDirectory = Path.GetDirectoryName(sfd.FileName) ?? settings.LastExportTrinityDirectory;
		                settings.Save();
		                var msg = $"Exported:\n{sfd.FileName}";
		                if (patchNotes.Count > 0)
		                {
		                    msg += "\n\nNotes:\n- " + string.Join("\n- ", patchNotes.Distinct());
		                }
		                MessageBox.Show(this, msg, "Export Trinity", MessageBoxButtons.OK, MessageBoxIcon.Information);
		            }
		            catch (DllNotFoundException ex)
		            {
		                MessageBox.Show(this,
		                    $"This GFPAK appears to require Oodle decompression.\n\nPlace `oo2core_8_win64.dll` next to the executable, then try again.\n\n{ex.Message}",
		                    "Missing Oodle", MessageBoxButtons.OK, MessageBoxIcon.Error);
		            }
			            catch (Exception ex)
			            {
			                MessageBox.Show(this, $"Export failed:\n{ex.Message}", "Export Trinity", MessageBoxButtons.OK, MessageBoxIcon.Error);
			            }
			        }

        private void ExportTrinityReserializeFromSelection()
        {
            var selected = sceneTree.SelectedNode;
            if (selected?.Tag is not NodeTag tag || tag.Type != NodeType.ModelRoot)
            {
                MessageBox.Show(this, "Select a model root node first (the top-level model entry in the scene tree).", "Export Trinity (Reserialize)",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            bool hasGltfPreview = gltfImportContextByModel.TryGetValue(tag.Model, out var ctx);
            string? referenceTrmdlPath = null;
            bool referenceIsGfpak = false;
            string? gfpakPath = null;
            string? gfpakEntry = null;

            if (hasGltfPreview)
            {
                referenceTrmdlPath = ctx.ReferenceTrmdlPath;
            }
            else if (sceneModelManager.TryGetModelSourcePath(tag.Model, out var src) && !string.IsNullOrWhiteSpace(src) && File.Exists(src))
            {
                referenceTrmdlPath = src;
            }
            else if (sceneModelManager.TryGetModelGfpakSource(tag.Model, out var container, out var entry) &&
                     !string.IsNullOrWhiteSpace(container) && !string.IsNullOrWhiteSpace(entry))
            {
                referenceIsGfpak = true;
                gfpakPath = container;
                gfpakEntry = entry;
            }
            else
            {
                MessageBox.Show(this, "Could not resolve the source .trmdl path for this model.", "Export Trinity (Reserialize)",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            using var sfd = new SaveFileDialog();
            sfd.Title = "Export Trinity Model Set (.trmdl)";
            sfd.Filter = "TRMDL (*.trmdl)|*.trmdl";
            string initialDir = Environment.CurrentDirectory;
            if (!string.IsNullOrWhiteSpace(settings.LastExportTrinityDirectory) && Directory.Exists(settings.LastExportTrinityDirectory))
            {
                initialDir = settings.LastExportTrinityDirectory;
            }
            else if (!referenceIsGfpak && !string.IsNullOrWhiteSpace(referenceTrmdlPath))
            {
                var srcDir = Path.GetDirectoryName(referenceTrmdlPath);
                if (!string.IsNullOrWhiteSpace(srcDir))
                {
                    initialDir = Path.Combine(srcDir, "trinity_export");
                }
            }
            else if (referenceIsGfpak && !string.IsNullOrWhiteSpace(gfpakPath))
            {
                var packDir = Path.GetDirectoryName(gfpakPath);
                if (!string.IsNullOrWhiteSpace(packDir))
                {
                    initialDir = Path.Combine(packDir, "trinity_export");
                }
            }

            if (!Directory.Exists(initialDir))
            {
                Directory.CreateDirectory(initialDir);
            }

            sfd.InitialDirectory = initialDir;
            sfd.FileName = !string.IsNullOrWhiteSpace(referenceTrmdlPath)
                ? Path.GetFileName(referenceTrmdlPath)
                : (!string.IsNullOrWhiteSpace(gfpakEntry) ? Path.GetFileName(gfpakEntry) : "model.trmdl");
            if (sfd.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            try
            {
                var outFull = Path.GetFullPath(sfd.FileName);
                if (!referenceIsGfpak && !string.IsNullOrWhiteSpace(referenceTrmdlPath))
                {
                    var refFull = Path.GetFullPath(referenceTrmdlPath);
                    if (string.Equals(outFull, refFull, StringComparison.OrdinalIgnoreCase))
                    {
                        MessageBox.Show(this, "Refusing to export over the original imported .trmdl. Pick a different output path.", "Export Trinity (Reserialize)",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                }

                if (hasGltfPreview)
                {
                    TrinityModelViewer.Export.GltfTrinityPipeline.Export(
                        referenceTrmdlPath,
                        ctx.GltfPath,
                        sfd.FileName,
                        patchBaseColorTextures: false,
                        exportModelPcBaseOnExport: settings.ExportModelPcBaseOnExport);
                }
                else if (!referenceIsGfpak)
                {
                    TrinityModelViewer.Export.TrinityModelSetExporter.ExportReserializeCopy(referenceTrmdlPath, sfd.FileName, tag.Model);
                }
                else
                {
                    TrinityModelViewer.Export.GfpakModelSetExtractor.ExportReserializeCopy(gfpakPath!, gfpakEntry!, sfd.FileName, tag.Model, includeProtagBaseSkeleton: true);
                }

                var patchNotes = new List<string>();
                if (settings.AutoGenerateLodsOnExport)
                {
                    if (TrinityModelViewer.Export.TrmdlLodPatcher.ForceAllLodsToUseMesh0(sfd.FileName, out var lodError))
                    {
                        patchNotes.Add("Auto-generate LODs: forced all LODs to use LOD0 mesh (placeholder).");
                    }
                    else if (!string.IsNullOrWhiteSpace(lodError))
                    {
                        patchNotes.Add($"Auto-generate LODs failed: {lodError}");
                    }
                }

                TryPatchExportedTrinityMaterials(sfd.FileName, tag.Model, patchNotes);

                settings.LastExportTrinityDirectory = Path.GetDirectoryName(sfd.FileName) ?? settings.LastExportTrinityDirectory;
                settings.Save();
                var msg = $"Exported:\n{sfd.FileName}";
                if (patchNotes.Count > 0)
                {
                    msg += "\n\nNotes:\n- " + string.Join("\n- ", patchNotes.Distinct());
                }
                MessageBox.Show(this, msg, "Export Trinity (Reserialize)", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (DllNotFoundException ex)
            {
                MessageBox.Show(this,
                    $"This GFPAK appears to require Oodle decompression.\n\nPlace `oo2core_8_win64.dll` next to the executable, then try again.\n\n{ex.Message}",
                    "Missing Oodle", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Export failed:\n{ex.Message}", "Export Trinity (Reserialize)", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void TryPatchExportedTrinityMaterials(string exportedTrmdlPath, Model model, List<string> notes)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (notes == null) throw new ArgumentNullException(nameof(notes));

            bool hasUniformEdits = model.GetMaterials().Any(m => m.HasUniformOverrides);
            bool hasSamplerEdits = model.GetMaterials().Any(m => m.HasSamplerOverrides);
            bool hasMaterialSourceEdits = model.HasMaterialSourceEdits;
            bool hasMetadataEdits = model.HasMaterialMetadataSelectionOverrides || model.HasMaterialMetadataValueOverrides;
            var cloneRequests = model.GetNewMaterialCloneRequestsSnapshot();
            bool wantsTrmmt = hasMetadataEdits || cloneRequests.Any(r => r.TrmmtCloneMode != Model.NewMaterialTrmmtCloneMode.None);
            if (!hasUniformEdits && !hasSamplerEdits && !hasMaterialSourceEdits && !hasMetadataEdits && !wantsTrmmt)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(exportedTrmdlPath) || !File.Exists(exportedTrmdlPath))
            {
                notes.Add("Skipped applying runtime material edits (exported TRMDL not found).");
                return;
            }

            Trinity.Core.Flatbuffers.TR.Model.TRMDL? trmdl = null;
            try
            {
                trmdl = Trinity.Core.Utils.FlatBufferConverter.DeserializeFrom<Trinity.Core.Flatbuffers.TR.Model.TRMDL>(exportedTrmdlPath);
            }
            catch (Exception ex)
            {
                notes.Add($"Failed to read exported TRMDL for material patching: {ex.Message}");
                return;
            }

            if (trmdl == null)
            {
                notes.Add("Failed to read exported TRMDL for material patching.");
                return;
            }

            var outputDir = Path.GetDirectoryName(Path.GetFullPath(exportedTrmdlPath)) ?? Environment.CurrentDirectory;
            var outputDirFull = Path.GetFullPath(outputDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

            var trmtrPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var materialRels = trmdl.Materials ?? Array.Empty<string>();
            foreach (var relRaw in materialRels)
            {
                if (string.IsNullOrWhiteSpace(relRaw))
                {
                    continue;
                }

                var rel = relRaw.Replace('\\', '/');
                var abs = Path.GetFullPath(Path.Combine(outputDir, rel));
                if (!abs.StartsWith(outputDirFull, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                trmtrPaths.Add(abs);

                // Patch sibling material-set variants too (`<stem>_00.trmtr`, etc), if present.
                try
                {
                    var relDir = Path.GetDirectoryName(rel)?.Replace('\\', '/') ?? string.Empty;
                    var dirAbs = string.IsNullOrWhiteSpace(relDir) ? outputDir : Path.Combine(outputDir, relDir);
                    if (!Directory.Exists(dirAbs))
                    {
                        continue;
                    }

                    var stem = Path.GetFileNameWithoutExtension(rel);
                    if (string.IsNullOrWhiteSpace(stem))
                    {
                        continue;
                    }

                    foreach (var variant in Directory.EnumerateFiles(dirAbs, stem + "_*.trmtr"))
                    {
                        trmtrPaths.Add(Path.GetFullPath(variant));
                    }
                }
                catch
                {
                    // Ignore.
                }
            }

            int trmtrProcessed = 0;
            bool shouldWriteTrmtr = hasUniformEdits || hasSamplerEdits || hasMaterialSourceEdits;

            if (shouldWriteTrmtr && trmtrPaths.Count > 0)
            {
                foreach (var path in trmtrPaths)
                {
                    if (!File.Exists(path))
                    {
                        continue;
                    }
                    try
                    {
                        // Reserialize so sampler wrap / JSON-applied material changes can be captured.
                        TrmtrReserializePatcher.PatchTrmtrInPlaceByReserialize(path, model);
                        trmtrProcessed++;
                    }
                    catch (Exception ex)
                    {
                        notes.Add($"TRMTR patch failed for '{Path.GetFileName(path)}': {ex.Message}");
                    }
                }
            }

            if (trmtrProcessed > 0)
            {
                notes.Add($"Wrote {trmtrProcessed} TRMTR file(s) from runtime edits.");
            }
            if (shouldWriteTrmtr && trmtrProcessed == 0)
            {
                notes.Add("No TRMTR files were available to write with runtime edits.");
            }

            if (wantsTrmmt)
            {
                var trmmtPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                try
                {
                    var trmmtNextToTrmdl = Path.ChangeExtension(exportedTrmdlPath, ".trmmt");
                    if (!string.IsNullOrWhiteSpace(trmmtNextToTrmdl) && File.Exists(trmmtNextToTrmdl))
                    {
                        trmmtPaths.Add(Path.GetFullPath(trmmtNextToTrmdl));
                    }
                }
                catch
                {
                }

                foreach (var trmtrPath in trmtrPaths)
                {
                    try
                    {
                        var trmmt = Path.ChangeExtension(trmtrPath, ".trmmt");
                        if (!string.IsNullOrWhiteSpace(trmmt) && File.Exists(trmmt))
                        {
                            trmmtPaths.Add(Path.GetFullPath(trmmt));
                        }
                    }
                    catch
                    {
                    }
                }

                int trmmtProcessed = 0;
                foreach (var path in trmmtPaths)
                {
                    try
                    {
                        var tmp = path + ".tmp";
                        TrinityModelViewer.Export.EditedMaterialMetadataExporter.ExportEditedTrmmt(path, model, tmp);
                        File.Copy(tmp, path, overwrite: true);
                        File.Delete(tmp);
                        trmmtProcessed++;
                    }
                    catch (Exception ex)
                    {
                        notes.Add($"TRMMT patch failed for '{Path.GetFileName(path)}': {ex.Message}");
                    }
                }

                if (trmmtProcessed > 0)
                {
                    notes.Add($"Wrote {trmmtProcessed} TRMMT file(s) from runtime edits.");
                }
                if (trmmtProcessed == 0)
                {
                    notes.Add("No TRMMT files were available to write with runtime edits.");
                }
            }
        }

        private void ExportTrinityPatchFromSelection()
        {
            var selected = sceneTree.SelectedNode;
            if (selected?.Tag is not NodeTag tag || tag.Type != NodeType.ModelRoot)
            {
                MessageBox.Show(this, "Select a model root node first (the top-level model entry in the scene tree).", "Export Trinity (Edited Only)",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!sceneModelManager.TryGetModelSourcePath(tag.Model, out var referenceTrmdlPath) ||
                string.IsNullOrWhiteSpace(referenceTrmdlPath) ||
                !File.Exists(referenceTrmdlPath))
            {
                MessageBox.Show(this, "Could not resolve the source .trmdl path for this model.", "Export Trinity (Edited Only)",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var mdl = tag.Model;
            bool hasTrinityEdits = mdl.IsTrmdlDirty || mdl.HasDirtyMeshes || mdl.HasDirtyBuffers || mdl.IsTrsklDirty;
            if (!mdl.GetMaterials().Any(m => m.HasUniformOverrides) &&
                !tag.Model.GetMaterials().Any(m => m.HasSamplerOverrides) &&
                !tag.Model.HasMaterialSourceEdits &&
                !tag.Model.GetMaterials().SelectMany(m => m.Textures).Any(t => t.IsEdited) &&
                !tag.Model.HasMaterialMetadataSelectionOverrides &&
                !tag.Model.HasMaterialMetadataValueOverrides &&
                !hasTrinityEdits)
            {
                MessageBox.Show(this, "No edited assets detected for this model.", "Export Trinity (Edited Only)",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using var fbd = new FolderBrowserDialog();
            fbd.Description = "Select output folder for edited assets (writes only modified files).";
            var refDir = Path.GetDirectoryName(referenceTrmdlPath) ?? Environment.CurrentDirectory;
            if (!string.IsNullOrWhiteSpace(settings.LastExportTrinityDirectory) && Directory.Exists(settings.LastExportTrinityDirectory))
            {
                fbd.SelectedPath = settings.LastExportTrinityDirectory;
            }
            else
            {
                fbd.SelectedPath = Path.Combine(refDir, "trinity_export_patch");
            }

            if (fbd.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(fbd.SelectedPath))
            {
                return;
            }

            var outputRoot = fbd.SelectedPath;
            Directory.CreateDirectory(outputRoot);

            int exportedCount = 0;
            var warnings = new List<string>();
            var exportedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var modelDir = Path.GetDirectoryName(mdl.TrmdlSourcePath) ?? refDir;
                bool TryWrite(string srcPath, byte[] bytes)
                {
                    if (string.IsNullOrWhiteSpace(srcPath) || bytes == null || bytes.Length == 0)
                    {
                        return false;
                    }
                    string rel = Path.GetRelativePath(modelDir, srcPath);
                    if (rel.StartsWith(".."))
                    {
                        rel = Path.GetFileName(srcPath);
                    }
                    string dst = Path.Combine(outputRoot, rel);
                    if (!exportedPaths.Add(Path.GetFullPath(dst)))
                    {
                        return false;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(dst) ?? outputRoot);
                    File.WriteAllBytes(dst, bytes);
                    exportedCount++;
                    return true;
                }

                if (mdl.IsTrmdlDirty)
                {
                    var src = mdl.DirtyTrmdlPath ?? mdl.TrmdlSourcePath;
                    TryWrite(src, mdl.ReadAssetBytes(src));
                }

                if (mdl.IsTrsklDirty && !string.IsNullOrWhiteSpace(mdl.CurrentSkeletonPath))
                {
                    var src = mdl.DirtyTrsklPath ?? mdl.CurrentSkeletonPath;
                    TryWrite(src, mdl.ReadAssetBytes(src));
                }

                var trmbfFromMeshes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var trmshPath in mdl.DirtyTrmshPaths)
                {
                    var bytes = mdl.ReadAssetBytes(trmshPath);
                    if (!TryWrite(trmshPath, bytes))
                    {
                        continue;
                    }

                    try
                    {
                        var trmsh = Trinity.Core.Utils.FlatBufferConverter.DeserializeFrom<TRMSH>(bytes);
                        var bufferRel = trmsh?.bufferFilePath;
                        if (!string.IsNullOrWhiteSpace(bufferRel))
                        {
                            var bufferPath = mdl.ResolveTrmbfPath(bufferRel);
                            if (!string.IsNullOrWhiteSpace(bufferPath))
                            {
                                trmbfFromMeshes.Add(bufferPath);
                            }
                        }
                    }
                    catch
                    {
                        // ignore
                    }
                }

                var trmbfToWrite = new HashSet<string>(mdl.DirtyTrmbfPaths, StringComparer.OrdinalIgnoreCase);
                foreach (var p in trmbfFromMeshes)
                {
                    trmbfToWrite.Add(p);
                }
                foreach (var trmbfPath in trmbfToWrite)
                {
                    TryWrite(trmbfPath, mdl.ReadAssetBytes(trmbfPath));
                }

                if (!string.IsNullOrWhiteSpace(mdl.CurrentMaterialFilePath) &&
                    File.Exists(mdl.CurrentMaterialFilePath) &&
                    (mdl.GetMaterials().Any(m => m.HasUniformOverrides) || mdl.GetMaterials().Any(m => m.HasSamplerOverrides) || mdl.HasMaterialSourceEdits))
                {
                    string rel = Path.GetRelativePath(refDir, mdl.CurrentMaterialFilePath);
                    if (rel.StartsWith(".."))
                    {
                        rel = Path.GetFileName(mdl.CurrentMaterialFilePath);
                    }
                    string dst = Path.Combine(outputRoot, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(dst) ?? outputRoot);
                    TrinityModelViewer.Export.EditedMaterialExporter.ExportEditedTrmtr(mdl.CurrentMaterialFilePath, mdl, dst);
                    exportedCount++;
                }

                if (mdl.HasMaterialMetadataSelectionOverrides || mdl.HasMaterialMetadataValueOverrides)
                {
                    var trmmtSource = Path.ChangeExtension(referenceTrmdlPath, ".trmmt");
                    if (!string.IsNullOrWhiteSpace(trmmtSource) && File.Exists(trmmtSource))
                    {
                        string rel = Path.GetRelativePath(refDir, trmmtSource);
                        if (rel.StartsWith(".."))
                        {
                            rel = Path.GetFileName(trmmtSource);
                        }
                        string dst = Path.Combine(outputRoot, rel);
                        Directory.CreateDirectory(Path.GetDirectoryName(dst) ?? outputRoot);
                        TrinityModelViewer.Export.EditedMaterialMetadataExporter.ExportEditedTrmmt(trmmtSource, mdl, dst);
                        exportedCount++;
                    }
                }

	                bool canUseUltimateTex = settings.UseUltimateTexForBntxExport &&
	                                         !string.IsNullOrWhiteSpace(settings.UltimateTexCliPath) &&
	                                         File.Exists(settings.UltimateTexCliPath);

	                string trmtrOutputDir = outputRoot;
	                if (!string.IsNullOrWhiteSpace(mdl.CurrentMaterialFilePath) && File.Exists(mdl.CurrentMaterialFilePath))
	                {
	                    string relMat = Path.GetRelativePath(refDir, mdl.CurrentMaterialFilePath);
	                    if (relMat.StartsWith(".."))
	                    {
	                        relMat = Path.GetFileName(mdl.CurrentMaterialFilePath);
	                    }
	                    string dstMat = Path.Combine(outputRoot, relMat);
	                    trmtrOutputDir = Path.GetDirectoryName(dstMat) ?? outputRoot;
	                }

	                var outputRootFull = Path.GetFullPath(outputRoot);

	                foreach (var tex in mdl.GetMaterials().SelectMany(m => m.Textures).DistinctBy(t => t.CacheKey))
	                {
	                    if (!tex.IsEdited)
	                    {
	                        continue;
	                    }

                    if (!tex.TryGetEditedBitmap(out var bmp))
                    {
                        continue;
                    }

	                    using (bmp)
	                    {
	                        string logical = tex.SourceFile ?? string.Empty;
	                        if (string.IsNullOrWhiteSpace(logical))
	                        {
	                            warnings.Add($"Edited texture '{tex.Name}' has no logical source path; exporting PNG next to the TRMTR.");
	                            logical = $"{tex.Name}.png";
	                        }

	                        logical = logical.Replace('\\', '/').TrimStart('/', '\\');
	                        string dst = Path.GetFullPath(Path.Combine(trmtrOutputDir, logical));
	                        if (!dst.StartsWith(outputRootFull, StringComparison.OrdinalIgnoreCase))
	                        {
	                            warnings.Add($"Edited texture '{tex.Name}' resolves outside export root via '{tex.SourceFile}'. Writing it next to the TRMTR instead.");
	                            dst = Path.Combine(trmtrOutputDir, Path.GetFileName(logical));
	                        }

	                        Directory.CreateDirectory(Path.GetDirectoryName(dst) ?? outputRoot);

	                        if (Path.GetExtension(dst).Equals(".bntx", StringComparison.OrdinalIgnoreCase) && canUseUltimateTex)
	                        {
	                            string srcPath = string.Empty;
	                            if (!tex.TryGetResolvedSourcePath(out srcPath) || string.IsNullOrWhiteSpace(srcPath) || !File.Exists(srcPath))
	                            {
	                                warnings.Add($"Edited texture '{tex.Name}' could not resolve its source BNTX; exporting a PNG sidecar instead.");
	                                var pngSidecar = Path.ChangeExtension(dst, ".png");
	                                bmp.Save(pngSidecar, System.Drawing.Imaging.ImageFormat.Png);
	                                exportedCount++;
	                                continue;
	                            }

	                            var preferredName = Path.GetFileNameWithoutExtension(tex.SourceFile);
	                            if (!BNTX.TryGetUltimateTexFormatFromFile(srcPath, preferredName, out var format, out var noMipmaps, out var fmtError))
	                            {
	                                warnings.Add($"Edited texture '{tex.Name}' could not determine its BNTX format ({fmtError}). Exporting a PNG sidecar instead.");
	                                var pngSidecar = Path.ChangeExtension(dst, ".png");
	                                bmp.Save(pngSidecar, System.Drawing.Imaging.ImageFormat.Png);
	                                exportedCount++;
	                                continue;
	                            }

	                            if (!UltimateTexCli.TryEncodeBntxFromBitmap(
	                                    bmp,
	                                    settings.UltimateTexCliPath,
	                                    dst,
	                                    format,
	                                    noMipmaps,
	                                    srcPath,
	                                    out var encodeError))
	                            {
	                                warnings.Add($"Edited texture '{tex.Name}' failed to encode via ultimate_tex_cli ({encodeError}). Exporting a PNG sidecar instead.");
	                                var pngSidecar = Path.ChangeExtension(dst, ".png");
	                                bmp.Save(pngSidecar, System.Drawing.Imaging.ImageFormat.Png);
	                                exportedCount++;
	                                continue;
	                            }

	                            exportedCount++;
	                        }
	                        else
	                        {
	                            string ext = Path.GetExtension(dst);
	                            if (!ext.Equals(".png", StringComparison.OrdinalIgnoreCase) &&
	                                !ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) &&
	                                !ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) &&
	                                !ext.Equals(".bmp", StringComparison.OrdinalIgnoreCase))
	                            {
	                                warnings.Add($"Edited texture '{tex.Name}' exports to '{ext}'. Writing PNG.");
	                                dst = Path.ChangeExtension(dst, ".png");
	                                Directory.CreateDirectory(Path.GetDirectoryName(dst) ?? outputRoot);
	                            }

	                            bmp.Save(dst, System.Drawing.Imaging.ImageFormat.Png);
	                            exportedCount++;
	                        }
	                    }
	                }

                settings.LastExportTrinityDirectory = outputRoot;
                settings.Save();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Export failed:\n{ex.Message}", "Export Trinity (Edited Only)", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var msg = $"Exported {exportedCount} edited file(s) to:\n{outputRoot}";
            if (warnings.Count > 0)
            {
                msg += "\n\nNotes:\n- " + string.Join("\n- ", warnings.Distinct());
            }
            MessageBox.Show(this, msg, "Export Trinity (Edited Only)", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
