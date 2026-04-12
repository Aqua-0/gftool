using GFTool.Renderer.Scene.GraphicsObjects;
using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Trinity.Core.Flatbuffers.TR.Model;
using Trinity.Core.Utils;

namespace TrinityModelViewer
{
    public partial class ModelViewerForm
    {
        private void ShowNewMaterialDialogForCurrentModel()
        {
            var mdl = currentMaterialsModel;
            if (mdl == null)
            {
                MessageBox.Show(this, "No model selected.", "New Material", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (string.IsNullOrWhiteSpace(mdl.CurrentMaterialFilePath) || !File.Exists(mdl.CurrentMaterialFilePath))
            {
                MessageBox.Show(this, "Could not resolve the current .trmtr path for this model.", "New Material", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            bool supportsSvZaTrmtr = false;
            try
            {
                var bytes = File.ReadAllBytes(mdl.CurrentMaterialFilePath);
                var parsed = FlatBufferConverter.DeserializeFrom<TrmtrFile>(bytes);
                supportsSvZaTrmtr = parsed?.Materials != null && parsed.Materials.Length > 0;
            }
            catch
            {
                supportsSvZaTrmtr = false;
            }

            if (!supportsSvZaTrmtr)
            {
                MessageBox.Show(this, "New material is currently supported for SV/ZA TRMTR only.", "New Material", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var materials = mdl.GetMaterials().Where(m => m != null && !string.IsNullOrWhiteSpace(m.Name)).ToList();
            if (materials.Count == 0)
            {
                MessageBox.Show(this, "This model has no materials to clone.", "New Material", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using var dialog = new Form
            {
                Text = "New Material (Clone)",
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MinimizeBox = false,
                MaximizeBox = false,
                ShowInTaskbar = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(12)
            };

            var templateLabel = new Label { AutoSize = true, Text = "Clone from:" };
            var templateCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 280
            };
            foreach (var mat in materials)
            {
                templateCombo.Items.Add(mat.Name);
            }
            templateCombo.SelectedIndex = 0;

            var nameLabel = new Label { AutoSize = true, Text = "New material name:" };
            var nameBox = new TextBox { Width = 280, Text = materials[0].Name + "_copy" };
            templateCombo.SelectedIndexChanged += (_, __) =>
            {
                if (templateCombo.SelectedItem is not string selected || string.IsNullOrWhiteSpace(selected))
                {
                    return;
                }

                nameBox.Text = selected + "_copy";
            };

            var trmmtLabel = new Label { AutoSize = true, Text = "TRMMT variation clone:" };
            var trmmtMode = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 280
            };
            trmmtMode.Items.Add("None");
            trmmtMode.Items.Add("Safe (only if material already exists in TRMMT)");
            trmmtMode.Items.Add("Unsafe (reserialize TRMMT and append)");
            trmmtMode.SelectedIndex = 0;

            var ok = new Button { Text = "Create", DialogResult = DialogResult.OK, Size = new Size(100, 28) };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Size = new Size(100, 28) };
            dialog.AcceptButton = ok;
            dialog.CancelButton = cancel;

            var buttons = new FlowLayoutPanel
            {
                AutoSize = true,
                WrapContents = false,
                FlowDirection = FlowDirection.RightToLeft,
                Margin = new Padding(0, 10, 0, 0),
                Dock = DockStyle.Fill
            };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);

            var layout = new TableLayoutPanel
            {
                AutoSize = true,
                ColumnCount = 2,
                RowCount = 4,
                Dock = DockStyle.Fill
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            layout.Controls.Add(templateLabel, 0, 0);
            layout.Controls.Add(templateCombo, 1, 0);
            layout.Controls.Add(nameLabel, 0, 1);
            layout.Controls.Add(nameBox, 1, 1);
            layout.Controls.Add(trmmtLabel, 0, 2);
            layout.Controls.Add(trmmtMode, 1, 2);
            layout.Controls.Add(buttons, 0, 3);
            layout.SetColumnSpan(buttons, 2);

            dialog.Controls.Add(layout);
            ApplyTheme(dialog);

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            var templateName = templateCombo.SelectedItem as string;
            var newName = nameBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(templateName) || string.IsNullOrWhiteSpace(newName))
            {
                MessageBox.Show(this, "Template and name are required.", "New Material", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var mode = trmmtMode.SelectedIndex switch
            {
                1 => Model.NewMaterialTrmmtCloneMode.Safe,
                2 => Model.NewMaterialTrmmtCloneMode.Unsafe,
                _ => Model.NewMaterialTrmmtCloneMode.None
            };

            if (!mdl.TryAddClonedMaterial(templateName, newName, mode, out var createdName, out var error))
            {
                MessageBox.Show(this, error ?? "Failed to create material.", "New Material", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            PopulateMaterials(mdl);
            foreach (ListViewItem item in materialList.Items)
            {
                if (string.Equals(item.Text, createdName, StringComparison.OrdinalIgnoreCase))
                {
                    item.Selected = true;
                    item.Focused = true;
                    item.EnsureVisible();
                    break;
                }
            }

            renderCtrl.Invalidate();
        }
    }
}
