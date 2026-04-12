using GFTool.Renderer.Core;
using GFTool.Renderer.Scene.GraphicsObjects;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TrinityModelViewer
{
    public partial class ModelViewerForm
    {
        private void PopulateMaterials(Model mdl)
        {
            currentMaterialsModel = mdl;
            EnsureMaterialListHeaderButtons();
            materialList.BeginUpdate();
            materialList.Items.Clear();
            materialList.Columns.Clear();
            materialList.View = View.Details;
            materialList.Columns.Add("Material", 160);

            foreach (var mat in mdl.GetMaterials())
            {
                var item = new ListViewItem(mat.Name);
                item.Tag = mat;
                materialList.Items.Add(item);
            }

            materialList.AutoResizeColumn(0, ColumnHeaderAutoResizeStyle.HeaderSize);
            materialList.EndUpdate();

            if (materialList.Items.Count > 0)
            {
                materialList.Items[0].Selected = true;
            }

            UpdateMaterialSetUi();
        }

        private void EnsureMaterialListHeaderButtons()
        {
            if (exportEditedMaterialsButton != null || newMaterialButton != null)
            {
                return;
            }

            var headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 30
            };

            exportEditedMaterialsButton = new Button
            {
                Text = "Export edited materials...",
                AutoSize = true
            };
            exportEditedMaterialsButton.Click += (s, e) => ExportEditedMaterialsForCurrentModel();

            newMaterialButton = new Button
            {
                Text = "New material...",
                AutoSize = true
            };
            newMaterialButton.Click += (s, e) => ShowNewMaterialDialogForCurrentModel();

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = false,
                Padding = new Padding(0),
                Margin = new Padding(0)
            };
            buttons.Controls.Add(exportEditedMaterialsButton);
            buttons.Controls.Add(newMaterialButton);
            headerPanel.Controls.Add(buttons);
            materialSplitContainer.Panel1.Controls.Add(headerPanel);
            headerPanel.BringToFront();
            ApplyTheme(headerPanel);
        }

        private void SetupMaterialGrids()
        {
            materialTexturesGrid.Columns.Clear();
            materialTexturesGrid.Columns.Add("TextureName", "Name");
            materialTexturesGrid.Columns.Add("TextureFile", "File");
            materialTexturesGrid.Columns.Add("TextureSlot", "Slot");
            materialTexturesGrid.Columns.Add("TextureSampler", "Sampler");
            materialTexturesGrid.ReadOnly = false;
            materialTexturesGrid.EditMode = DataGridViewEditMode.EditOnEnter;
            materialTexturesGrid.AllowUserToAddRows = false;
            materialTexturesGrid.AllowUserToDeleteRows = false;
            materialTexturesGrid.MultiSelect = false;
            materialTexturesGrid.Columns["TextureName"].ReadOnly = true;
            materialTexturesGrid.Columns["TextureSlot"].ReadOnly = true;
            materialTexturesGrid.Columns["TextureSampler"].ReadOnly = true;
            materialTexturesGrid.Columns["TextureFile"].ReadOnly = false;
            materialTexturesGrid.CurrentCellDirtyStateChanged -= materialTexturesGrid_CurrentCellDirtyStateChanged;
            materialTexturesGrid.CurrentCellDirtyStateChanged += materialTexturesGrid_CurrentCellDirtyStateChanged;
            materialTexturesGrid.CellValueChanged -= materialTexturesGrid_CellValueChanged;
            materialTexturesGrid.CellValueChanged += materialTexturesGrid_CellValueChanged;
            EnsureTextureGridContextMenu();

            materialParamsGrid.Columns.Clear();
            materialParamsGrid.Columns.Add("ParamName", "Name");
            materialParamsGrid.Columns.Add("ParamType", "Type");
            materialParamsGrid.Columns.Add("ParamValue", "Value");
            materialParamsGrid.ReadOnly = false;
            materialParamsGrid.EditMode = DataGridViewEditMode.EditOnEnter;
            materialParamsGrid.Columns["ParamName"].ReadOnly = true;
            materialParamsGrid.Columns["ParamType"].ReadOnly = true;
            materialParamsGrid.Columns["ParamValue"].ReadOnly = false;
            materialParamsGrid.AllowUserToAddRows = false;
            materialParamsGrid.AllowUserToDeleteRows = false;
            materialParamsGrid.MultiSelect = false;
            materialParamsGrid.CurrentCellDirtyStateChanged -= materialParamsGrid_CurrentCellDirtyStateChanged;
            materialParamsGrid.CurrentCellDirtyStateChanged += materialParamsGrid_CurrentCellDirtyStateChanged;
            materialParamsGrid.CellPainting -= materialParamsGrid_CellPainting;
            materialParamsGrid.CellPainting += materialParamsGrid_CellPainting;
            materialParamsGrid.CellMouseClick -= materialParamsGrid_CellMouseClick;
            materialParamsGrid.CellMouseClick += materialParamsGrid_CellMouseClick;
            materialParamsGrid.CellBeginEdit -= materialParamsGrid_CellBeginEdit;
            materialParamsGrid.CellBeginEdit += materialParamsGrid_CellBeginEdit;
            materialParamsGrid.CellValueChanged -= materialParamsGrid_CellValueChanged;
            materialParamsGrid.CellValueChanged += materialParamsGrid_CellValueChanged;
            materialParamsGrid.CellClick -= materialParamsGrid_CellClick;
            materialParamsGrid.CellClick += materialParamsGrid_CellClick;
            EnsureMaterialSetUi();

            materialUvGrid.Columns.Clear();
            materialUvGrid.Columns.Add("UvName", "Name");
            materialUvGrid.Columns.Add("UvValue", "Value");

            materialSamplersGrid.Columns.Clear();
            materialSamplersGrid.Columns.Add("SamplerIndex", "Index");
            materialSamplersGrid.Columns.Add("SamplerRepeatU", "Repeat U");
            materialSamplersGrid.Columns.Add("SamplerRepeatV", "Repeat V");
            materialSamplersGrid.Columns.Add("SamplerRepeatW", "Repeat W");
            materialSamplersGrid.Columns.Add("SamplerBorderColor", "Border Color");
            materialSamplersGrid.Columns.Add("SamplerState0", "State0");
            materialSamplersGrid.Columns.Add("SamplerState1", "State1");
            materialSamplersGrid.Columns.Add("SamplerState2", "State2");
            materialSamplersGrid.Columns.Add("SamplerState3", "State3");
            materialSamplersGrid.Columns.Add("SamplerState4", "State4");
            materialSamplersGrid.Columns.Add("SamplerState5", "State5");
            materialSamplersGrid.Columns.Add("SamplerState6", "State6");
            materialSamplersGrid.Columns.Add("SamplerState7", "State7");
            materialSamplersGrid.Columns.Add("SamplerState8", "State8");
        }

        private void materialTexturesGrid_CurrentCellDirtyStateChanged(object? sender, EventArgs e)
        {
            if (materialTexturesGrid.IsCurrentCellDirty)
            {
                materialTexturesGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        }

        private void materialTexturesGrid_CellValueChanged(object? sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
            {
                return;
            }

            var column = materialTexturesGrid.Columns[e.ColumnIndex];
            if (column == null || !string.Equals(column.Name, "TextureFile", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (currentMaterialsModel == null || currentMaterial == null)
            {
                return;
            }

            var row = materialTexturesGrid.Rows[e.RowIndex];
            if (row?.Tag is not Texture tex)
            {
                return;
            }

            string desired = row.Cells[e.ColumnIndex].Value?.ToString() ?? string.Empty;
            string previous = tex.SourceFile ?? string.Empty;

            if (string.Equals(desired.Trim(), previous, StringComparison.OrdinalIgnoreCase))
            {
                row.Cells[e.ColumnIndex].Value = previous;
                return;
            }

            if (!currentMaterialsModel.TrySetMaterialTextureSourceFile(currentMaterial.Name, tex.Name, desired, out var error))
            {
                row.Cells[e.ColumnIndex].Value = previous;
                if (!string.IsNullOrWhiteSpace(error))
                {
                    MessageBox.Show(this, error, "Update Texture File", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                return;
            }

            var updated = currentMaterial.Textures.FirstOrDefault(t => string.Equals(t.Name, tex.Name, StringComparison.OrdinalIgnoreCase) && t.Slot == tex.Slot)
                          ?? currentMaterial.Textures.FirstOrDefault(t => string.Equals(t.Name, tex.Name, StringComparison.OrdinalIgnoreCase))
                          ?? tex;

            row.Tag = updated;
            row.Cells[e.ColumnIndex].Value = updated.SourceFile;
        }

        private void materialList_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (materialList.SelectedItems.Count == 0)
            {
                currentMaterial = null;
                ClearMaterialDetails();
                return;
            }

            if (materialList.SelectedItems[0].Tag is not Material mat)
            {
                currentMaterial = null;
                ClearMaterialDetails();
                return;
            }

            currentMaterial = mat;
            PopulateMaterialDetails(mat);
        }

        private void ClearMaterialDetails()
        {
            UpdateMaterialSetUi();
            UpdateMaterialVariantUi();
            materialVariationsGrid?.Rows.Clear();
            materialTexturesGrid.Rows.Clear();
            materialParamsGrid.Rows.Clear();
            materialUvGrid.Rows.Clear();
            materialSamplersGrid.Rows.Clear();
            SetTexturePreview(null, ownsImage: true);
            SetUvPreview(null, ownsImage: true);
        }

        private void PopulateMaterialDetails(Material mat)
        {
            ClearMaterialDetails();

            isUpdatingMaterialGrids = true;
            try
            {
                materialParamsGrid.Rows.Add("Shader", "Name", mat.ShaderName);
                materialParamsGrid.Rows[0].Cells["ParamValue"].ReadOnly = true;

                foreach (var param in mat.ShaderParameters)
                {
                    string value = param.Value;
                    if (mat.TryGetUniformOverride(param.Name, out var overrideValue))
                    {
                        value = FormatOverrideValue("Option", overrideValue);
                    }
                    int rowIndex = materialParamsGrid.Rows.Add(param.Name, "Option", value);
                    ConfigureParamValueCell(rowIndex, param.Name, "Option", value);
                }

                foreach (var param in mat.FloatParameters)
                {
                    string value = param.Value.ToString("0.####", CultureInfo.InvariantCulture);
                    if (mat.TryGetUniformOverride(param.Name, out var overrideValue))
                    {
                        value = FormatOverrideValue("Float", overrideValue);
                    }
                    materialParamsGrid.Rows.Add(param.Name, "Float", value);
                }

                foreach (var param in mat.Vec2Parameters)
                {
                    string value = $"{param.Value.X.ToString("0.####", CultureInfo.InvariantCulture)}, {param.Value.Y.ToString("0.####", CultureInfo.InvariantCulture)}";
                    if (mat.TryGetUniformOverride(param.Name, out var overrideValue))
                    {
                        value = FormatOverrideValue("Vec2", overrideValue);
                    }
                    materialParamsGrid.Rows.Add(param.Name, "Vec2", value);
                }

                foreach (var param in mat.Vec3Parameters)
                {
                    string value =
                        $"{param.Value.X.ToString("0.####", CultureInfo.InvariantCulture)}, {param.Value.Y.ToString("0.####", CultureInfo.InvariantCulture)}, {param.Value.Z.ToString("0.####", CultureInfo.InvariantCulture)}";
                    if (mat.TryGetUniformOverride(param.Name, out var overrideValue))
                    {
                        value = FormatOverrideValue("Vec3", overrideValue);
                    }
                    materialParamsGrid.Rows.Add(param.Name, "Vec3", value);
                }

                foreach (var param in mat.Vec4Parameters)
                {
                    string value =
                        $"{param.Value.W.ToString("0.####", CultureInfo.InvariantCulture)}, {param.Value.X.ToString("0.####", CultureInfo.InvariantCulture)}, {param.Value.Y.ToString("0.####", CultureInfo.InvariantCulture)}, {param.Value.Z.ToString("0.####", CultureInfo.InvariantCulture)}";
                    if (mat.TryGetUniformOverride(param.Name, out var overrideValue))
                    {
                        value = FormatOverrideValue("Vec4", overrideValue);
                    }
                    materialParamsGrid.Rows.Add(param.Name, "Vec4", value);
                }

                bool HasParam(string name)
                {
                    return mat.ShaderParameters.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) ||
                           mat.FloatParameters.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) ||
                           mat.Vec2Parameters.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) ||
                           mat.Vec3Parameters.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) ||
                           mat.Vec4Parameters.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
                }

                void AddVec4IfMissing(string name)
                {
                    if (HasParam(name))
                    {
                        return;
                    }

                    string value = "0, 0, 0, 0";
                    if (mat.TryGetUniformOverride(name, out var overrideValue))
                    {
                        value = FormatOverrideValue("Vec4", overrideValue);
                    }
                    materialParamsGrid.Rows.Add(name, "Vec4", value);
                }

                if (string.Equals(mat.ShaderName, "Standard", StringComparison.OrdinalIgnoreCase))
                {
                    AddVec4IfMissing("ScrollUVSpeed");
                }

                for (int i = 0; i < mat.Textures.Count; i++)
                {
                    var tex = mat.Textures[i];
                    string samplerIndex = i < mat.Samplers.Count ? i.ToString() : "-";
                    var rowIndex = materialTexturesGrid.Rows.Add(tex.Name, tex.SourceFile, tex.Slot.ToString(), samplerIndex);
                    materialTexturesGrid.Rows[rowIndex].Tag = tex;
                }

                foreach (var param in mat.ShaderParameters)
                {
                    if (IsUvParamName(param.Name))
                    {
                        string value = param.Value;
                        if (mat.TryGetUniformOverride(param.Name, out var overrideValue))
                        {
                            value = FormatOverrideValue("Option", overrideValue);
                        }
                        materialUvGrid.Rows.Add(param.Name, value);
                    }
                }

                foreach (var param in mat.Vec2Parameters)
                {
                    if (IsUvParamName(param.Name))
                    {
                        string value = $"{param.Value.X.ToString("0.####", CultureInfo.InvariantCulture)}, {param.Value.Y.ToString("0.####", CultureInfo.InvariantCulture)}";
                        if (mat.TryGetUniformOverride(param.Name, out var overrideValue))
                        {
                            value = FormatOverrideValue("Vec2", overrideValue);
                        }
                        materialUvGrid.Rows.Add(param.Name, value);
                    }
                }

                foreach (var param in mat.Vec3Parameters)
                {
                    if (IsUvParamName(param.Name))
                    {
                        string value =
                            $"{param.Value.X.ToString("0.####", CultureInfo.InvariantCulture)}, {param.Value.Y.ToString("0.####", CultureInfo.InvariantCulture)}, {param.Value.Z.ToString("0.####", CultureInfo.InvariantCulture)}";
                        if (mat.TryGetUniformOverride(param.Name, out var overrideValue))
                        {
                            value = FormatOverrideValue("Vec3", overrideValue);
                        }
                        materialUvGrid.Rows.Add(param.Name, value);
                    }
                }

                foreach (var param in mat.Vec4Parameters)
                {
                    if (IsUvParamName(param.Name))
                    {
                        string value =
                            $"{param.Value.W.ToString("0.####", CultureInfo.InvariantCulture)}, {param.Value.X.ToString("0.####", CultureInfo.InvariantCulture)}, {param.Value.Y.ToString("0.####", CultureInfo.InvariantCulture)}, {param.Value.Z.ToString("0.####", CultureInfo.InvariantCulture)}";
                        if (mat.TryGetUniformOverride(param.Name, out var overrideValue))
                        {
                            value = FormatOverrideValue("Vec4", overrideValue);
                        }
                        materialUvGrid.Rows.Add(param.Name, value);
                    }
                }

                materialSamplersGrid.Rows.Clear();
                for (int i = 0; i < mat.Samplers.Count; i++)
                {
                    var sampler = mat.Samplers[i];
                    string borderText = $"0x{sampler.BorderColor:X8}";
                    materialSamplersGrid.Rows.Add(
                        i.ToString(),
                        sampler.RepeatU.ToString(),
                        sampler.RepeatV.ToString(),
                        sampler.RepeatW.ToString(),
                        borderText,
                        $"0x{sampler.State0:X8}",
                        $"0x{sampler.State1:X8}",
                        $"0x{sampler.State2:X8}",
                        $"0x{sampler.State3:X8}",
                        $"0x{sampler.State4:X8}",
                        $"0x{sampler.State5:X8}",
                        $"0x{sampler.State6:X8}",
                        $"0x{sampler.State7:X8}",
                        $"0x{sampler.State8:X8}"
                    );
                }

                if (materialTexturesGrid.Rows.Count > 0)
                {
                    materialTexturesGrid.ClearSelection();
                    materialTexturesGrid.Rows[0].Selected = true;
                }

                RequestMaterialPreviewUpdate();
            }
            finally
            {
                isUpdatingMaterialGrids = false;
            }

            UpdateMaterialVariationsGrid();
        }

    }
}
