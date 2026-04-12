using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Trinity.Core.Assets;

namespace TrinitySceneView.UI
{
    public sealed class GfpakPickerForm : Form
    {
        private readonly ListBox listBox;
        private readonly Button okButton;
        private readonly Button cancelButton;
        private readonly IAssetProvider[] providers;

        public IAssetProvider? SelectedProvider { get; private set; }

        public GfpakPickerForm(IReadOnlyList<IAssetProvider> providers)
        {
            if (providers == null || providers.Count == 0)
            {
                throw new ArgumentException("At least one provider is required.", nameof(providers));
            }

            this.providers = providers.ToArray();

            Text = "Select GFPAK";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(540, 340);
            Size = new Size(640, 380);

            listBox = new ListBox
            {
                Dock = DockStyle.Fill
            };
            foreach (var p in this.providers)
            {
                listBox.Items.Add(p.DisplayName);
            }
            listBox.SelectedIndex = 0;
            listBox.DoubleClick += (_, _) => TryAccept();

            var bottomPanel = new Panel { Dock = DockStyle.Bottom, Height = 44 };
            okButton = new Button { Text = "OK", Anchor = AnchorStyles.Right | AnchorStyles.Bottom, Size = new Size(100, 28) };
            cancelButton = new Button { Text = "Cancel", Anchor = AnchorStyles.Right | AnchorStyles.Bottom, Size = new Size(100, 28), DialogResult = DialogResult.Cancel };
            okButton.Location = new Point(bottomPanel.Width - 220, 8);
            cancelButton.Location = new Point(bottomPanel.Width - 110, 8);
            bottomPanel.Resize += (_, _) =>
            {
                okButton.Location = new Point(bottomPanel.Width - 220, 8);
                cancelButton.Location = new Point(bottomPanel.Width - 110, 8);
            };
            okButton.Click += (_, _) => TryAccept();

            bottomPanel.Controls.Add(okButton);
            bottomPanel.Controls.Add(cancelButton);

            Controls.Add(listBox);
            Controls.Add(bottomPanel);

            AcceptButton = okButton;
            CancelButton = cancelButton;
        }

        private void TryAccept()
        {
            int idx = listBox.SelectedIndex;
            if (idx < 0 || idx >= providers.Length)
            {
                return;
            }

            SelectedProvider = providers[idx];
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
