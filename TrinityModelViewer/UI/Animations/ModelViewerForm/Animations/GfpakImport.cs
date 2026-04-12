using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GFTool.Renderer.Core;
using TrinityModelViewer.Scene;

namespace TrinityModelViewer
{
    public partial class ModelViewerForm
    {
        private async Task LoadAnimationsFromGfpakFromDialogAsync()
        {
            using var ofd = new OpenFileDialog();
            ofd.Filter = "GFPAK (*.gfpak)|*.gfpak|All files (*.*)|*.*";
            if (ofd.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(ofd.FileName))
            {
                return;
            }

            var debugLog = MessageHandler.Instance.DebugLogsEnabled
                ? new Action<string>(msg => MessageHandler.Instance.AddMessage(MessageType.LOG, msg))
                : null;

            string gfpakPath = ofd.FileName;
            var previousCursor = Cursor;
            Cursor = Cursors.WaitCursor;

            try
            {
                HashSet<string> existing;
                lock (animationLoadGate)
                {
                    existing = new HashSet<string>(loadedAnimationPaths, StringComparer.OrdinalIgnoreCase);
                }

                var loaded = await GfpakAnimationImporter.LoadAllAnimationsAsync(
                    gfpakPath,
                    existing,
                    CancellationToken.None,
                    debugLog);

                if (loaded.Count == 0)
                {
                    MessageBox.Show(this, "No animations were found in that GFPAK.", "Load Animations from GFPAK",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                int added = 0;
                foreach (var item in loaded)
                {
                    if (!loadedAnimationPaths.Add(item.Key))
                    {
                        continue;
                    }

                    animations.Add(item.Animation);
                    animationsList.Items.Add(new ListViewItem(item.Animation.Name) { Tag = item.Animation });
                    added++;
                }

                if (added > 0)
                {
                    animationsList.AutoResizeColumns(ColumnHeaderAutoResizeStyle.ColumnContent);
                }

                MessageHandler.Instance.AddMessage(
                    MessageType.LOG,
                    $"[Anim] Imported {added}/{loaded.Count} animations from GFPAK '{Path.GetFileName(gfpakPath)}'");
            }
            catch (DllNotFoundException ex)
            {
                MessageBox.Show(this,
                    $"This GFPAK appears to require Oodle decompression.\n\nPlace `oo2core_8_win64.dll` next to the executable, then try again.\n\n{ex.Message}",
                    "Missing Oodle", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Failed to load animations from GFPAK:\n{ex.Message}", "Load Animations from GFPAK",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor = previousCursor;
            }
        }
    }
}
