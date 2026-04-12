using GFTool.Renderer.Scene.GraphicsObjects;
using OpenTK.Mathematics;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Forms;
using JsonEditorEntry = TrinityModelViewer.UI.JsonEditor.JsonEditorService.JsonEditorEntry;

namespace TrinityModelViewer
{
    public partial class ModelViewerForm
    {
        private static readonly JsonSerializerOptions CaseInsensitiveJson = new() { PropertyNameCaseInsensitive = true };

        private static byte[] SerializeFlatbufferFromJson(string kind, string json)
        {
            if (string.Equals(kind, "TRMTR", StringComparison.OrdinalIgnoreCase))
            {
                var trmtr = TrmtrFileJson.Deserialize(json);
                return Trinity.Core.Utils.FlatBufferConverter.SerializeFrom(trmtr);
            }

            if (string.Equals(kind, "TRMMT", StringComparison.OrdinalIgnoreCase))
            {
                // Two flavors; prefer metadata when present.
                Trinity.Core.Flatbuffers.TR.Model.TrmmtMetadataFile? meta = null;
                try { meta = JsonSerializer.Deserialize<Trinity.Core.Flatbuffers.TR.Model.TrmmtMetadataFile>(json, CaseInsensitiveJson); } catch { meta = null; }
                if (meta?.ItemList != null && meta.ItemList.Length > 0)
                {
                    return Trinity.Core.Utils.FlatBufferConverter.SerializeFrom(meta);
                }

                var setMap = JsonSerializer.Deserialize<Trinity.Core.Flatbuffers.TR.Model.TrmmtFile>(json, CaseInsensitiveJson);
                if (setMap == null) throw new InvalidOperationException("JSON did not parse into TRMMT.");
                return Trinity.Core.Utils.FlatBufferConverter.SerializeFrom(setMap);
            }

            if (string.Equals(kind, "TRMBF", StringComparison.OrdinalIgnoreCase))
            {
                var v = JsonSerializer.Deserialize<Trinity.Core.Flatbuffers.TR.Model.TRMBF>(json, CaseInsensitiveJson);
                if (v == null) throw new InvalidOperationException("JSON did not parse into TRMBF.");
                return Trinity.Core.Utils.FlatBufferConverter.SerializeFrom(v);
            }

            if (string.Equals(kind, "TRMSH", StringComparison.OrdinalIgnoreCase))
            {
                var v = JsonSerializer.Deserialize<Trinity.Core.Flatbuffers.TR.Model.TRMSH>(json, CaseInsensitiveJson);
                if (v == null) throw new InvalidOperationException("JSON did not parse into TRMSH.");
                return Trinity.Core.Utils.FlatBufferConverter.SerializeFrom(v);
            }

            if (string.Equals(kind, "TRMDL", StringComparison.OrdinalIgnoreCase))
            {
                var v = JsonSerializer.Deserialize<Trinity.Core.Flatbuffers.TR.Model.TRMDL>(json, CaseInsensitiveJson);
                if (v == null) throw new InvalidOperationException("JSON did not parse into TRMDL.");
                return Trinity.Core.Utils.FlatBufferConverter.SerializeFrom(v);
            }

            if (string.Equals(kind, "TRSKL", StringComparison.OrdinalIgnoreCase))
            {
                var v = JsonSerializer.Deserialize<Trinity.Core.Flatbuffers.TR.Model.TRSKL>(json, CaseInsensitiveJson);
                if (v == null) throw new InvalidOperationException("JSON did not parse into TRSKL.");
                return Trinity.Core.Utils.FlatBufferConverter.SerializeFrom(v);
            }

            throw new NotSupportedException($"Unsupported kind: {kind}");
        }

        private void ApplyFlatbufferJsonToScene(string kind, JsonEditorEntry entry, string json)
        {
            if (entry.Model == null)
            {
                return;
            }

            if (kind == "TRMTR")
            {
                var trmtr = TrmtrFileJson.Deserialize(json);
                entry.Model.ApplyTrmtrFile(entry.Path, trmtr, preserveMaterialUniformOverrides: false);
                PopulateMaterials(entry.Model);
                return;
            }

            if (kind == "TRMMT")
            {
                var bytes = SerializeFlatbufferFromJson(kind, json);
                entry.Model.ApplyTrmmtFile(entry.Path, bytes, resetOverrides: true);
                PopulateMaterials(entry.Model);
                UpdateMaterialVariationsGrid();
                return;
            }

            if (kind == "TRSKL")
            {
                var trskl = JsonSerializer.Deserialize<Trinity.Core.Flatbuffers.TR.Model.TRSKL>(json, CaseInsensitiveJson);
                if (trskl == null)
                {
                    throw new InvalidOperationException("JSON did not parse into TRSKL.");
                }

                entry.Model.ApplyTrsklFile(entry.Path, trskl);
                if (renderCtrl.renderer.HasActiveAnimation())
                {
                    renderCtrl.renderer.SetAnimationTimeSeconds(renderCtrl.renderer.GetAnimationTimeSeconds());
                }
                else
                {
                    entry.Model.ResetPose();
                }
                renderCtrl.Invalidate();
                return;
            }

            if (kind == "TRMBF" || kind == "TRMSH")
            {
                var bytes = SerializeFlatbufferFromJson(kind, json);
                if (!entry.Model.TrySetAssetOverrideBytes(entry.Path, bytes))
                {
                    throw new InvalidOperationException("Model does not support in-memory asset overrides (expected InMemoryOverrideAssetProvider).");
                }

                if (kind == "TRMBF")
                {
                    entry.Model.MarkTrmbfDirty(entry.Path);
                }
                else
                {
                    entry.Model.MarkTrmshDirty(entry.Path);
                }

                entry.Model.ReloadMeshesFromSource();

                var node = modelMap.FirstOrDefault(kv => kv.Value == entry.Model).Key;
                if (node != null)
                {
                    node.Nodes.Clear();
                    PopulateSubmeshes(node, entry.Model);
                }

                _ = renderCtrl.renderer.LoadModelResourcesAsync(entry.Model);
                return;
            }

            if (kind == "TRMDL")
            {
                var bytes = SerializeFlatbufferFromJson(kind, json);
                if (!entry.Model.TrySetAssetOverrideBytes(entry.Path, bytes))
                {
                    throw new InvalidOperationException("Model does not support in-memory asset overrides (expected InMemoryOverrideAssetProvider).");
                }

                entry.Model.ReloadFromTrmdlSource(entry.Path, settings.LoadAllLods);
                entry.Model.MarkTrmdlDirty(entry.Path);

                var node = modelMap.FirstOrDefault(kv => kv.Value == entry.Model).Key;
                if (node != null)
                {
                    node.Nodes.Clear();
                    PopulateSubmeshes(node, entry.Model);
                }

                PopulateMaterials(entry.Model);
                _ = renderCtrl.renderer.LoadModelResourcesAsync(entry.Model);
                return;
            }

            throw new NotSupportedException("Apply is only implemented for TRMTR/TRMMT/TRMBF/TRMSH/TRMDL/TRSKL in Tier 1.");
        }

        private void ApplyTrmmtMetadataJsonToModel(Model model, Trinity.Core.Flatbuffers.TR.Model.TrmmtMetadataFile meta)
        {
            var item = SelectActiveTrmmtMetaItem(model, meta);
            if (item?.ParamList == null || item.ParamList.Length == 0)
            {
                return;
            }

            foreach (var p in item.ParamList)
            {
                if (p == null || !p.UseNoAnime || string.IsNullOrWhiteSpace(p.Name) || p.NoAnimeParam?.MaterialList == null)
                {
                    continue;
                }

                int selected = p.OverrideDefaultValue >= 0 ? p.OverrideDefaultValue : 0;
                model.TrySetMaterialVariantParam(p.Name, selected);

                int variationCount = p.NoAnimeParam.VariationCount;
                int idx = Math.Clamp(selected, 0, Math.Max(0, variationCount - 1));

                foreach (var m in p.NoAnimeParam.MaterialList)
                {
                    if (m == null || string.IsNullOrWhiteSpace(m.MaterialName))
                    {
                        continue;
                    }

                    foreach (var fp in m.FloatParamList ?? Array.Empty<Trinity.Core.Flatbuffers.TR.Model.TrmmtMetaFloatParams>())
                    {
                        if (fp?.Values == null || fp.Values.Length == 0 || string.IsNullOrWhiteSpace(fp.Name))
                        {
                            continue;
                        }
                        float v = fp.Values[Math.Clamp(idx, 0, fp.Values.Length - 1)];
                        model.TrySetMaterialMetadataValueOverride(p.Name, m.MaterialName, fp.Name, v);
                    }

                    foreach (var ip in m.IntParamList ?? Array.Empty<Trinity.Core.Flatbuffers.TR.Model.TrmmtMetaIntParams>())
                    {
                        if (ip?.Values == null || ip.Values.Length == 0 || string.IsNullOrWhiteSpace(ip.Name))
                        {
                            continue;
                        }
                        int v = ip.Values[Math.Clamp(idx, 0, ip.Values.Length - 1)];
                        model.TrySetMaterialMetadataValueOverride(p.Name, m.MaterialName, ip.Name, v);
                    }

                    foreach (var v3p in m.Float3ParamList ?? Array.Empty<Trinity.Core.Flatbuffers.TR.Model.TrmmtMetaFloat3Params>())
                    {
                        if (v3p?.Values == null || v3p.Values.Length == 0 || string.IsNullOrWhiteSpace(v3p.Name))
                        {
                            continue;
                        }
                        var v3 = v3p.Values[Math.Clamp(idx, 0, v3p.Values.Length - 1)];
                        model.TrySetMaterialMetadataValueOverride(p.Name, m.MaterialName, v3p.Name, new Vector3(v3.X, v3.Y, v3.Z));
                    }

                    foreach (var v4p in m.Float4ParamList ?? Array.Empty<Trinity.Core.Flatbuffers.TR.Model.TrmmtMetaFloat4Params>())
                    {
                        if (v4p?.Values == null || v4p.Values.Length == 0 || string.IsNullOrWhiteSpace(v4p.Name))
                        {
                            continue;
                        }
                        var v4 = v4p.Values[Math.Clamp(idx, 0, v4p.Values.Length - 1)];
                        model.TrySetMaterialMetadataValueOverride(p.Name, m.MaterialName, v4p.Name, new Vector4(v4.W, v4.X, v4.Y, v4.Z));
                    }
                }
            }
        }

        private static Trinity.Core.Flatbuffers.TR.Model.TrmmtMetaItem? SelectActiveTrmmtMetaItem(Model model, Trinity.Core.Flatbuffers.TR.Model.TrmmtMetadataFile meta)
        {
            if (meta.ItemList == null || meta.ItemList.Length == 0)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(model.CurrentMaterialSetName))
            {
                var byName = meta.ItemList.FirstOrDefault(i => string.Equals(i?.Name, model.CurrentMaterialSetName, StringComparison.OrdinalIgnoreCase));
                if (byName != null)
                {
                    return byName;
                }
            }

            if (!string.IsNullOrWhiteSpace(model.CurrentMaterialFilePath))
            {
                var fileName = Path.GetFileName(model.CurrentMaterialFilePath);
                foreach (var item in meta.ItemList)
                {
                    if (item?.MaterialPathList == null)
                    {
                        continue;
                    }

                    if (item.MaterialPathList.Any(p => string.Equals(Path.GetFileName(p), fileName, StringComparison.OrdinalIgnoreCase)))
                    {
                        return item;
                    }
                }
            }

            return meta.ItemList.Length == 1 ? meta.ItemList[0] : meta.ItemList[0];
        }

        private void ExportFlatbufferFromJson(string kind, JsonEditorEntry entry, string json)
        {
            if (kind != "TRMTR" && kind != "TRMMT" && kind != "TRMBF" && kind != "TRMSH" && kind != "TRMDL" && kind != "TRSKL")
            {
                MessageBox.Show(this, "Tier 1 export is implemented for TRMTR/TRMMT/TRMBF/TRMSH/TRMDL/TRSKL only.", "Json Editor", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (entry.Model != null)
            {
                ApplyFlatbufferJsonToScene(kind, entry, json);
            }

            using var sfd = new SaveFileDialog();
            sfd.Title = $"Export {kind} from JSON";
            sfd.Filter = kind == "TRMTR"
                ? "TRMTR (*.trmtr)|*.trmtr"
                : kind == "TRMMT"
                    ? "TRMMT (*.trmmt)|*.trmmt"
                    : kind == "TRMBF"
                        ? "TRMBF (*.trmbf)|*.trmbf"
                        : kind == "TRMSH"
                            ? "TRMSH (*.trmsh)|*.trmsh"
                            : kind == "TRMDL"
                                ? "TRMDL (*.trmdl)|*.trmdl"
                                : "TRSKL (*.trskl)|*.trskl";
            sfd.FileName = Path.GetFileName(entry.Path);
            if (sfd.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            var bytes = SerializeFlatbufferFromJson(kind, json);
            Directory.CreateDirectory(Path.GetDirectoryName(sfd.FileName) ?? ".");
            File.WriteAllBytes(sfd.FileName, bytes);
            MessageBox.Show(this, $"Exported:\n{sfd.FileName}", "Json Editor", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ExportFlatbufferFromJsonReserialize(string kind, JsonEditorEntry entry, string json)
        {
            if (kind != "TRMTR" && kind != "TRMMT" && kind != "TRMBF" && kind != "TRMSH" && kind != "TRMDL" && kind != "TRSKL")
            {
                MessageBox.Show(this, "Reserialize export is implemented for TRMTR/TRMMT/TRMBF/TRMSH/TRMDL/TRSKL only.", "Json Editor", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using var sfd = new SaveFileDialog();
            sfd.Title = $"Reserialize {kind} from JSON";
            sfd.Filter = kind == "TRMTR"
                ? "TRMTR (*.trmtr)|*.trmtr"
                : kind == "TRMMT"
                    ? "TRMMT (*.trmmt)|*.trmmt"
                    : kind == "TRMBF"
                        ? "TRMBF (*.trmbf)|*.trmbf"
                        : kind == "TRMSH"
                            ? "TRMSH (*.trmsh)|*.trmsh"
                            : kind == "TRMDL"
                                ? "TRMDL (*.trmdl)|*.trmdl"
                                : "TRSKL (*.trskl)|*.trskl";
            sfd.FileName = Path.GetFileName(entry.Path);
            if (sfd.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            var bytes = SerializeFlatbufferFromJson(kind, json);
            Directory.CreateDirectory(Path.GetDirectoryName(sfd.FileName) ?? ".");
            File.WriteAllBytes(sfd.FileName, bytes);
            MessageBox.Show(this, $"Exported:\n{sfd.FileName}", "Json Editor", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
