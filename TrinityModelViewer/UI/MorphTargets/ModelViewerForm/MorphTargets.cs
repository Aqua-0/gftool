using GFTool.Renderer.Core;
using GFTool.Renderer.Scene.GraphicsObjects;
using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace TrinityModelViewer
{
    public partial class ModelViewerForm
    {
        private sealed record MorphListItem(string MeshShapeName, string TargetName);

        private void SetupMorphTargetsTab()
        {
            if (morphTargetsTabPage != null)
            {
                return;
            }

            morphTargetsTabPage = new TabPage { Text = "Morph Targets" };

            var root = new Panel { Dock = DockStyle.Fill };

            var header = new Panel { Dock = DockStyle.Top, Height = 34 };
            morphRefreshButton = new Button
            {
                Text = "Refresh",
                Dock = DockStyle.Right,
                Width = 90
            };
            morphRefreshButton.Click += (s, e) => RefreshMorphTargetsList();

            morphResetButton = new Button
            {
                Text = "Reset All",
                Dock = DockStyle.Right,
                Width = 90
            };
            morphResetButton.Click += (s, e) => ResetAllMorphWeights();

            morphSelectionLabel = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(6, 0, 6, 0),
                Text = "Select a model, then pick a morph target."
            };

            header.Controls.Add(morphRefreshButton);
            header.Controls.Add(morphResetButton);
            header.Controls.Add(morphSelectionLabel);

            morphTargetsListView = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false,
                MultiSelect = false
            };
            morphTargetsListView.Columns.Add("Mesh", 160);
            morphTargetsListView.Columns.Add("Target", 220);
            morphTargetsListView.Columns.Add("Weight", 70);
            morphTargetsListView.SelectedIndexChanged += (s, e) => UpdateMorphSelectionUi();

            var footer = new Panel { Dock = DockStyle.Bottom, Height = 64 };
            var sliderLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 18,
                Text = "Weight (0..1)",
                Padding = new Padding(6, 0, 6, 0)
            };

            morphWeightTrackBar = new TrackBar
            {
                Dock = DockStyle.Fill,
                Minimum = 0,
                Maximum = 1000,
                TickStyle = TickStyle.None,
                SmallChange = 1,
                LargeChange = 10
            };
            morphWeightTrackBar.ValueChanged += (s, e) =>
            {
                if (isUpdatingMorphUi)
                {
                    return;
                }
                var value = morphWeightTrackBar.Value / 1000.0m;
                if (morphWeightNumeric != null)
                {
                    isUpdatingMorphUi = true;
                    try { morphWeightNumeric.Value = value; } finally { isUpdatingMorphUi = false; }
                }
                ApplySelectedMorphWeight();
            };

            morphWeightNumeric = new NumericUpDown
            {
                Dock = DockStyle.Right,
                Width = 90,
                DecimalPlaces = 3,
                Increment = 0.01m,
                Minimum = 0,
                Maximum = 1
            };
            morphWeightNumeric.ValueChanged += (s, e) =>
            {
                if (isUpdatingMorphUi)
                {
                    return;
                }
                int track = (int)Math.Round(morphWeightNumeric.Value * 1000.0m);
                track = Math.Clamp(track, morphWeightTrackBar.Minimum, morphWeightTrackBar.Maximum);
                isUpdatingMorphUi = true;
                try { morphWeightTrackBar.Value = track; } finally { isUpdatingMorphUi = false; }
                ApplySelectedMorphWeight();
            };

            footer.Controls.Add(morphWeightTrackBar);
            footer.Controls.Add(morphWeightNumeric);
            footer.Controls.Add(sliderLabel);

            root.Controls.Add(morphTargetsListView);
            root.Controls.Add(footer);
            root.Controls.Add(header);

            morphTargetsTabPage.Controls.Add(root);
            leftTabs.TabPages.Add(morphTargetsTabPage);

            leftTabs.SelectedIndexChanged += (s, e) =>
            {
                if (leftTabs.SelectedTab == morphTargetsTabPage)
                {
                    RefreshMorphTargetsList();
                }
            };

            ApplyTheme(morphTargetsTabPage);
            UpdateMorphControlsEnabled(false);
        }

        private void RefreshMorphTargetsList()
        {
            if (morphTargetsListView == null)
            {
                return;
            }

            var mdl = GetSelectedModelFromSceneTree() ?? currentMaterialsModel;
            morphTargetsListView.BeginUpdate();
            try
            {
                morphTargetsListView.Items.Clear();

                if (mdl == null)
                {
                    morphSelectionLabel!.Text = "No model selected.";
                    UpdateMorphControlsEnabled(false);
                    return;
                }

                var targets = mdl.GetCpuFullMorphTargetNames();
                if (targets.Count == 0)
                {
                    var debug = mdl.GetCpuFullMorphTargetDebugSummary();
                    morphSelectionLabel!.Text = string.IsNullOrWhiteSpace(debug)
                        ? "No supported full-vertex morph targets found for this model."
                        : $"No supported full-vertex morph targets found for this model ({debug}).";
                    UpdateMorphControlsEnabled(false);
                    return;
                }

                foreach (var (mesh, target) in targets.OrderBy(t => t.MeshShapeName, StringComparer.OrdinalIgnoreCase).ThenBy(t => t.TargetName, StringComparer.OrdinalIgnoreCase))
                {
                    float w = mdl.GetCpuFullMorphWeight(mesh, target);
                    var item = new ListViewItem(mesh);
                    item.SubItems.Add(target);
                    item.SubItems.Add(w.ToString("0.###", CultureInfo.InvariantCulture));
                    item.Tag = new MorphListItem(mesh, target);
                    morphTargetsListView.Items.Add(item);
                }

                morphTargetsListView.AutoResizeColumns(ColumnHeaderAutoResizeStyle.ColumnContent);
                morphSelectionLabel!.Text = $"Morph targets: {targets.Count}";
            }
            finally
            {
                morphTargetsListView.EndUpdate();
            }
        }

        private void UpdateMorphSelectionUi()
        {
            if (morphTargetsListView == null || morphWeightTrackBar == null || morphWeightNumeric == null)
            {
                return;
            }

            var mdl = GetSelectedModelFromSceneTree() ?? currentMaterialsModel;
            if (mdl == null || morphTargetsListView.SelectedItems.Count == 0)
            {
                UpdateMorphControlsEnabled(false);
                return;
            }

            if (morphTargetsListView.SelectedItems[0].Tag is not MorphListItem sel)
            {
                UpdateMorphControlsEnabled(false);
                return;
            }

            float w = mdl.GetCpuFullMorphWeight(sel.MeshShapeName, sel.TargetName);
            isUpdatingMorphUi = true;
            try
            {
                morphWeightTrackBar.Value = Math.Clamp((int)Math.Round(w * 1000.0f), morphWeightTrackBar.Minimum, morphWeightTrackBar.Maximum);
                morphWeightNumeric.Value = (decimal)Math.Clamp(w, 0.0f, 1.0f);
            }
            finally
            {
                isUpdatingMorphUi = false;
            }

            morphSelectionLabel!.Text = $"{sel.MeshShapeName} / {sel.TargetName}";
            UpdateMorphControlsEnabled(true);
        }

        private void ApplySelectedMorphWeight()
        {
            if (morphTargetsListView == null || morphWeightNumeric == null)
            {
                return;
            }

            var mdl = GetSelectedModelFromSceneTree() ?? currentMaterialsModel;
            if (mdl == null || morphTargetsListView.SelectedItems.Count == 0)
            {
                return;
            }

            if (morphTargetsListView.SelectedItems[0].Tag is not MorphListItem sel)
            {
                return;
            }

            float w = (float)morphWeightNumeric.Value;
            mdl.SetCpuFullMorphWeight(sel.MeshShapeName, sel.TargetName, w);
            if (mdl.TryApplyCpuFullMorphs(out var error))
            {
                morphTargetsListView.SelectedItems[0].SubItems[2].Text = w.ToString("0.###", CultureInfo.InvariantCulture);
                renderCtrl.Invalidate();
            }

            if (!string.IsNullOrWhiteSpace(error))
            {
                MessageHandler.Instance.AddMessage(MessageType.WARNING, $"[Morph] {error}");
            }
        }

        private void ResetAllMorphWeights()
        {
            var mdl = GetSelectedModelFromSceneTree() ?? currentMaterialsModel;
            if (mdl == null)
            {
                return;
            }

            mdl.ResetCpuFullMorphWeights();
            mdl.TryApplyCpuFullMorphs(out _);
            RefreshMorphTargetsList();
            renderCtrl.Invalidate();
        }

        private void UpdateMorphControlsEnabled(bool enabled)
        {
            if (morphWeightTrackBar != null) morphWeightTrackBar.Enabled = enabled;
            if (morphWeightNumeric != null) morphWeightNumeric.Enabled = enabled;
            if (morphResetButton != null) morphResetButton.Enabled = enabled;
        }
    }
}
