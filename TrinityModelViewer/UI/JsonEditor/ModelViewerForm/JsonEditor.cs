using GFTool.Renderer.Core;
using GFTool.Renderer.Scene.GraphicsObjects;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using TrinityModelViewer.UI.JsonEditor;
using JsonEditorEntry = TrinityModelViewer.UI.JsonEditor.JsonEditorService.JsonEditorEntry;

namespace TrinityModelViewer
{
    public partial class ModelViewerForm
    {
        private readonly JsonEditorService jsonEditorService = new JsonEditorService();

        private void SetupJsonEditorTab()
        {
            if (jsonEditorTabPage != null)
            {
                return;
            }

            jsonEditorTabPage = new TabPage { Text = "Json Editor" };

            var root = new Panel { Dock = DockStyle.Fill };

            var header = new Panel { Dock = DockStyle.Bottom, Height = 28 };
            var addJsonFileButton = new Button
            {
                Text = "Add file...",
                Dock = DockStyle.Right,
                Width = 90
            };
            addJsonFileButton.Click += (s, e) => AddJsonEditorFile();
            refreshJsonFilesButton = new Button
            {
                Text = "Refresh",
                Dock = DockStyle.Right,
                Width = 90
            };
            refreshJsonFilesButton.Click += (s, e) => RefreshJsonEditorFileList();
            header.Controls.Add(refreshJsonFilesButton);
            header.Controls.Add(addJsonFileButton);

            jsonFilesGrid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                ColumnHeadersVisible = true,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single,
                MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoGenerateColumns = false,
                ClipboardCopyMode = DataGridViewClipboardCopyMode.Disable
            };

            jsonFilesGrid.EnableHeadersVisualStyles = false;

            jsonFilesGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Type",
                HeaderText = "Type",
                Width = 70,
                ReadOnly = true
            });
            jsonFilesGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Model",
                HeaderText = "File",
                Width = 160,
                ReadOnly = true
            });
            jsonFilesGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Path",
                HeaderText = "Path",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                ReadOnly = true
            });

            jsonFilesGrid.ColumnHeadersHeight = 24;

            jsonFilesGrid.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex >= 0)
                {
                    OpenSelectedJsonEntry();
                }
            };
            jsonFilesGrid.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    OpenSelectedJsonEntry();
                    e.Handled = true;
                }
            };

            var contextMenu = new ContextMenuStrip();
            var editMenuItem = new ToolStripMenuItem("Edit...");
            editMenuItem.Click += (s, e) => OpenSelectedJsonEntry();
            contextMenu.Items.Add(editMenuItem);
            contextMenu.Items.Add(new ToolStripSeparator());

            var copyPathMenuItem = new ToolStripMenuItem("Copy path");
            copyPathMenuItem.Click += (s, e) =>
            {
                var entry = GetSelectedJsonEditorEntry();
                if (!string.IsNullOrWhiteSpace(entry.Path))
                {
                    Clipboard.SetText(entry.Path);
                }
            };
            contextMenu.Items.Add(copyPathMenuItem);

            jsonFilesGrid.ContextMenuStrip = contextMenu;
            jsonFilesGrid.CellMouseDown += (s, e) =>
            {
                if (e.Button != MouseButtons.Right || e.RowIndex < 0)
                {
                    return;
                }

                jsonFilesGrid.ClearSelection();
                jsonFilesGrid.Rows[e.RowIndex].Selected = true;
                jsonFilesGrid.CurrentCell = jsonFilesGrid.Rows[e.RowIndex].Cells[Math.Max(0, e.ColumnIndex)];
            };

            root.Controls.Add(header);
            root.Controls.Add(jsonFilesGrid);

            jsonEditorTabPage.Controls.Add(root);
            leftTabs.TabPages.Add(jsonEditorTabPage);

            leftTabs.SelectedIndexChanged += (s, e) =>
            {
                if (leftTabs.SelectedTab == jsonEditorTabPage)
                {
                    RefreshJsonEditorFileList();
                }
            };

            ApplyTheme(jsonEditorTabPage);
        }

        private void RefreshJsonEditorFileList()
        {
            if (jsonFilesGrid == null)
            {
                return;
            }

            var entries = jsonEditorService.EnumerateFlatbufferEntriesInScene(
                    sceneModelManager.ModelSourcePaths,
                    message => MessageHandler.Instance.AddMessage(MessageType.LOG, message))
                .OrderBy(e => e.Type, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => Path.GetFileNameWithoutExtension(e.Path), StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.Path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            jsonFilesGrid.SuspendLayout();
            try
            {
                jsonFilesGrid.Rows.Clear();
                foreach (var e in entries)
                {
                    int rowIndex = jsonFilesGrid.Rows.Add(e.Type, Path.GetFileNameWithoutExtension(e.Path), e.Path);
                    jsonFilesGrid.Rows[rowIndex].Tag = e;
                }
            }
            finally
            {
                jsonFilesGrid.ResumeLayout();
            }
        }

        private void AddJsonEditorFile()
        {
            using var ofd = new OpenFileDialog();
            ofd.Title = "Add FlatBuffer file to Json Editor";
            ofd.Filter =
                "Trinity FlatBuffers|*.trmdl;*.trmsh;*.trmbf;*.trskl;*.trmtr;*.trmmt|All files|*.*";
            ofd.Multiselect = true;

            if (ofd.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            EnsureJsonEditorTabSelected();

            if (jsonFilesGrid == null)
            {
                return;
            }

            var existingPaths = new List<string>();
            foreach (DataGridViewRow row in jsonFilesGrid.Rows)
            {
                if (row.Tag is JsonEditorEntry entry && !string.IsNullOrWhiteSpace(entry.Path))
                {
                    existingPaths.Add(entry.Path);
                }
            }

            var defaultModel = currentMaterialsModel ?? sceneModelManager.ModelSourcePaths.Keys.FirstOrDefault();
            foreach (var entry in jsonEditorService.CreateManualEntries(ofd.FileNames ?? Array.Empty<string>(), existingPaths, defaultModel))
            {
                int rowIndex = jsonFilesGrid.Rows.Add(entry.Type, Path.GetFileNameWithoutExtension(entry.Path), entry.Path);
                jsonFilesGrid.Rows[rowIndex].Tag = entry;
            }
        }

        private void EnsureJsonEditorTabSelected()
        {
            SetupJsonEditorTab();
            if (jsonEditorTabPage != null)
            {
                leftTabs.SelectedTab = jsonEditorTabPage;
            }
        }

        private void OpenSelectedJsonEntry()
        {
            var entry = GetSelectedJsonEditorEntry();
            if (string.IsNullOrWhiteSpace(entry.Type))
            {
                return;
            }

            OpenFlatbufferJsonEditor(entry);
        }

        private JsonEditorEntry GetSelectedJsonEditorEntry()
        {
            if (jsonFilesGrid == null)
            {
                return default;
            }

            DataGridViewRow? row = null;
            if (jsonFilesGrid.SelectedRows.Count > 0)
            {
                row = jsonFilesGrid.SelectedRows[0];
            }
            else if (jsonFilesGrid.CurrentRow != null)
            {
                row = jsonFilesGrid.CurrentRow;
            }

            return row?.Tag is JsonEditorEntry entry ? entry : default;
        }

	        private void OpenFlatbufferJsonEditor(JsonEditorEntry entry)
	        {
            if (!File.Exists(entry.Path))
            {
                MessageBox.Show(this, $"File not found:\n{entry.Path}", "Json Editor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(entry.Path);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Read failed:\n{ex.Message}", "Json Editor", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string json;
            string kind = entry.Type;
            try
            {
                json = jsonEditorService.BuildFlatbufferJson(kind, bytes);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"JSON conversion failed:\n{ex.Message}", "Json Editor", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

	            bool allowApplyExport = kind == "TRMTR" || kind == "TRMMT" || kind == "TRMBF" || kind == "TRMSH" || kind == "TRMDL" || kind == "TRSKL";
	            using var editor = new FlatbufferJsonEditorForm(
	                $"{kind} JSON - {Path.GetFileName(entry.Path)}",
	                entry.Path,
	                json,
	                allowApplyExport,
	                allowApplyExport,
	                showPrettyToggle: kind == "TRMTR",
	                formatJson: kind == "TRMTR"
	                    ? (raw, pretty) => TrmtrFileJson.Serialize(TrmtrFileJson.Deserialize(raw), prettySamplers: pretty)
	                    : null);
	            ApplyTheme(editor);
	            editor.ApplyRequested += (_, editedJson) =>
	            {
                try
                {
                    ApplyFlatbufferJsonToScene(kind, entry, editedJson);
                    RequestMaterialPreviewUpdate();
                    UpdateMaterialVariationsGrid();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"Apply failed:\n{ex.Message}", "Json Editor", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };
            editor.ExportRequested += (_, editedJson) =>
            {
                try
                {
                    ExportFlatbufferFromJson(kind, entry, editedJson);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"Export failed:\n{ex.Message}", "Json Editor", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };
            editor.ExportReserializeRequested += (_, editedJson) =>
            {
                try
                {
                    ExportFlatbufferFromJsonReserialize(kind, entry, editedJson);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"Reserialize export failed:\n{ex.Message}", "Json Editor", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };
            editor.ShowDialog(this);
        }

        private void SyncJsonEditorToCurrentMaterialSelection()
        {
            if (jsonFilesGrid == null || jsonEditorTabPage == null)
            {
                return;
            }

            if (leftTabs.SelectedTab != jsonEditorTabPage)
            {
                return;
            }

            RefreshJsonEditorFileList();
        }
    }
}
