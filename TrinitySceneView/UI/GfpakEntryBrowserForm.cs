using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Trinity.Core.Assets;

namespace TrinitySceneView.UI
{
    public sealed class GfpakEntryBrowserForm : Form
    {
        private readonly IAssetProvider provider;
        private readonly Func<AssetEntry, bool>? filter;
        private readonly TextBox filterTextBox;
        private readonly TextBox openPathTextBox;
        private readonly ListView listView;
        private readonly Button openButton;
        private readonly Button cancelButton;
        private readonly Label hintLabel;
        private List<AssetEntry> entries = new();

        public AssetEntry? SelectedEntry { get; private set; }

        public string? SelectedPath => SelectedEntry?.Path;
        public ulong? SelectedHash => SelectedEntry?.PathHash;

        public GfpakEntryBrowserForm(IAssetProvider provider, string title, string hint, Func<AssetEntry, bool>? filter = null)
        {
            this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
            this.filter = filter;

            Text = $"{title} - {provider.DisplayName}";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(720, 520);
            Size = new Size(900, 620);

            hintLabel = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Top,
                Height = 56,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(10, 6, 10, 6),
                Text = hint
            };

            filterTextBox = new TextBox
            {
                Dock = DockStyle.Top,
                PlaceholderText = "Filter (requires names; otherwise filters hash)…"
            };
            filterTextBox.TextChanged += (_, _) => RebuildList();

            listView = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false
            };
            listView.Columns.Add("Path", 560);
            listView.Columns.Add("Hash", 240);
            listView.DoubleClick += (_, _) => TryAcceptSelectedListItem();

            openPathTextBox = new TextBox
            {
                Dock = DockStyle.Bottom,
                PlaceholderText = "Open by path inside pack (example: bin/field/param/placement/ha_area04/bg_parts/bg_parts.bin)"
            };

            var bottomPanel = new Panel { Dock = DockStyle.Bottom, Height = 44 };
            openButton = new Button { Text = "Open", Anchor = AnchorStyles.Right | AnchorStyles.Bottom, Size = new Size(100, 28) };
            cancelButton = new Button { Text = "Cancel", Anchor = AnchorStyles.Right | AnchorStyles.Bottom, Size = new Size(100, 28), DialogResult = DialogResult.Cancel };
            openButton.Location = new Point(bottomPanel.Width - 220, 8);
            cancelButton.Location = new Point(bottomPanel.Width - 110, 8);
            bottomPanel.Resize += (_, _) =>
            {
                openButton.Location = new Point(bottomPanel.Width - 220, 8);
                cancelButton.Location = new Point(bottomPanel.Width - 110, 8);
            };
            openButton.Click += (_, _) => TryAccept();

            bottomPanel.Controls.Add(openButton);
            bottomPanel.Controls.Add(cancelButton);

            Controls.Add(listView);
            Controls.Add(filterTextBox);
            Controls.Add(hintLabel);
            Controls.Add(openPathTextBox);
            Controls.Add(bottomPanel);

            AcceptButton = openButton;
            CancelButton = cancelButton;

            LoadEntries();
        }

        private void LoadEntries()
        {
            entries = provider.EnumerateEntries().ToList();
            RebuildList();
        }

        private void RebuildList()
        {
            string filterText = filterTextBox.Text?.Trim() ?? string.Empty;

            IEnumerable<AssetEntry> filtered = entries;
            if (filter != null)
            {
                filtered = filtered.Where(filter);
            }

            if (!string.IsNullOrWhiteSpace(filterText))
            {
                filtered = filtered.Where(e =>
                    (!string.IsNullOrEmpty(e.Path) && e.Path.Contains(filterText, StringComparison.OrdinalIgnoreCase)) ||
                    $"0x{e.PathHash:X16}".Contains(filterText, StringComparison.OrdinalIgnoreCase));
            }

            filtered = filtered
                .OrderBy(e => e.Path ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.PathHash);

            listView.BeginUpdate();
            try
            {
                listView.Items.Clear();
                foreach (var entry in filtered.Take(20000))
                {
                    string displayPath = entry.Path ?? "(unknown path)";
                    string hash = $"0x{entry.PathHash:X16}";

                    var item = new ListViewItem(displayPath);
                    item.SubItems.Add(hash);
                    item.Tag = entry;
                    listView.Items.Add(item);
                }
            }
            finally
            {
                listView.EndUpdate();
            }
        }

        private void TryAcceptSelectedListItem()
        {
            if (listView.SelectedItems.Count == 0)
            {
                return;
            }

            var entry = (AssetEntry)listView.SelectedItems[0].Tag;
            SelectedEntry = entry;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void TryAccept()
        {
            string directPath = openPathTextBox.Text?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(directPath))
            {
                SelectedEntry = new AssetEntry(0, directPath);
                DialogResult = DialogResult.OK;
                Close();
                return;
            }

            TryAcceptSelectedListItem();
        }
    }
}
