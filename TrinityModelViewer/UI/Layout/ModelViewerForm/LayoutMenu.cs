using System;
using System.Windows.Forms;

namespace TrinityModelViewer
{
    public partial class ModelViewerForm
    {
        private ToolStripMenuItem? _resetLayoutMenuItem;

        private void AddLayoutMenuItems()
        {
            if (_resetLayoutMenuItem != null)
                return;

            _resetLayoutMenuItem = new ToolStripMenuItem("Reset Layout");
            _resetLayoutMenuItem.Click += (_, _) => ResetLayout();

            viewToolStripMenuItem.DropDownItems.Add(new ToolStripSeparator());
            viewToolStripMenuItem.DropDownItems.Add(_resetLayoutMenuItem);
        }

        private void ResetLayout()
        {
            try
            {
                if (splitContainerMain.Width > 0)
                {
                    splitContainerMain.SplitterDistance = (int)Math.Round(splitContainerMain.Width * 0.75);
                }

                if (splitContainerLeft.Height > 0)
                {
                    splitContainerLeft.SplitterDistance = (int)Math.Round(splitContainerLeft.Height * 0.83);
                }

                if (splitContainer1.Height > 0)
                {
                    splitContainer1.SplitterDistance = (int)Math.Round(splitContainer1.Height * 0.50);
                }

                if (materialSplitContainer.Width > 0)
                {
                    materialSplitContainer.SplitterDistance = (int)Math.Round(materialSplitContainer.Width * 0.40);
                }

                if (materialTexturesSplit.Height > 0)
                {
                    materialTexturesSplit.SplitterDistance = (int)Math.Round(materialTexturesSplit.Height * 0.55);
                }

                if (materialUvSplit.Height > 0)
                {
                    materialUvSplit.SplitterDistance = (int)Math.Round(materialUvSplit.Height * 0.55);
                }
            }
            catch
            {
                // Best-effort; splitters can throw if called mid-layout.
            }

            EnsureBottomTabsVisible();
        }
    }
}
