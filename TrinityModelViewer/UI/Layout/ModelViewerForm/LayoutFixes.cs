using System;
using System.Drawing;
using System.Windows.Forms;

namespace TrinityModelViewer
{
    public partial class ModelViewerForm
    {
        private bool _layoutFixesInitialized;

        private void InitializeLayoutFixes()
        {
            if (_layoutFixesInitialized)
                return;
            _layoutFixesInitialized = true;

            // Prefer docking over anchoring here; it is more resilient under Win11 DPI/font scaling.
            modelProperties.Dock = DockStyle.Fill;
            modelProperties.Location = new Point(0, 0);
            modelProperties.Margin = Padding.Empty;

            // Keep the bottom tabs visible even if scaling makes the initial layout tight.
            // (Symptom: "Object" + "Materials" tabs appear missing.)
            splitContainer1.Panel2MinSize = Math.Max(splitContainer1.Panel2MinSize, 160);

            Shown += (_, _) => EnsureBottomTabsVisible();
            splitContainer1.SizeChanged += (_, _) => EnsureBottomTabsVisible();
        }

        private void EnsureBottomTabsVisible()
        {
            if (splitContainer1.Orientation != Orientation.Horizontal)
                return;

            // If Panel2 gets squeezed too small, the TabControl header can be fully clipped, making it look like
            // the "Object"/"Materials" tabs don't exist.
            var minPanel2 = Math.Max(splitContainer1.Panel2MinSize, 160);
            var desiredPanel1Max = Math.Max(0, splitContainer1.Height - minPanel2 - splitContainer1.SplitterWidth);
            if (splitContainer1.SplitterDistance > desiredPanel1Max)
            {
                try
                {
                    splitContainer1.SplitterDistance = desiredPanel1Max;
                }
                catch
                {
                    // Best-effort; SplitterDistance can throw if called mid-layout.
                }
            }
        }
    }
}
