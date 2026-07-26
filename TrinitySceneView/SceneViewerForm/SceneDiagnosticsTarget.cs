using System.Drawing;
using System.Windows.Forms;
using GFTool.Renderer.Core;

namespace TrinitySceneView
{
    public partial class SceneViewerForm
    {
        private void ShowSceneDiagnosticsTargetDialog()
        {
            using var form = new Form
            {
                Text = "Scene Diagnostics Target",
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                StartPosition = FormStartPosition.CenterParent,
                ClientSize = new Size(420, 126)
            };

            var label = new Label
            {
                Left = 12,
                Top = 12,
                Width = 392,
                Height = 34,
                Text = "Substring match against scene object name and model path.\r\nExample: t4_b_justice"
            };

            var textBox = new TextBox
            {
                Left = 12,
                Top = 52,
                Width = 392,
                Text = SceneDiagnosticsTarget
            };

            var apply = new Button
            {
                Text = "Apply",
                Left = 12,
                Top = 86,
                Width = 80,
                DialogResult = DialogResult.OK
            };

            var clear = new Button
            {
                Text = "Clear",
                Left = 98,
                Top = 86,
                Width = 80
            };

            var close = new Button
            {
                Text = "Close",
                Left = 324,
                Top = 86,
                Width = 80,
                DialogResult = DialogResult.Cancel
            };

            apply.Click += (_, _) =>
            {
                config.SceneDiagnosticsTarget = string.IsNullOrWhiteSpace(textBox.Text) ? null : textBox.Text.Trim();
                config.Save();
                MessageHandler.Instance.AddMessage(
                    MessageType.LOG,
                    $"[Logs] Scene diagnostics target set to '{config.SceneDiagnosticsTarget ?? "<none>"}'.");
                form.Close();
            };

            clear.Click += (_, _) =>
            {
                textBox.Text = string.Empty;
                config.SceneDiagnosticsTarget = null;
                config.Save();
                MessageHandler.Instance.AddMessage(MessageType.LOG, "[Logs] Scene diagnostics target cleared.");
            };

            form.Controls.Add(label);
            form.Controls.Add(textBox);
            form.Controls.Add(apply);
            form.Controls.Add(clear);
            form.Controls.Add(close);
            form.AcceptButton = apply;
            form.CancelButton = close;
            form.ShowDialog(this);
        }

        private bool SceneDiagnosticsMatchesTarget(string? sceneObjectName, string? modelPath)
        {
            if (!SceneDiagnosticsEnabled)
            {
                return false;
            }

            string target = SceneDiagnosticsTarget;
            if (string.IsNullOrWhiteSpace(target))
            {
                return false;
            }

            return (!string.IsNullOrWhiteSpace(sceneObjectName) && sceneObjectName.Contains(target, StringComparison.OrdinalIgnoreCase)) ||
                   (!string.IsNullOrWhiteSpace(modelPath) && modelPath.Contains(target, StringComparison.OrdinalIgnoreCase));
        }

        private bool ObjectDebugMatchesTarget(string? sceneObjectName, string? modelPath)
        {
            string target = config.ObjectDebugTarget?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(target))
            {
                return false;
            }

            if (config.ObjectDebugExactMatch)
            {
                if (!string.IsNullOrWhiteSpace(sceneObjectName) &&
                    string.Equals(sceneObjectName, target, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (!string.IsNullOrWhiteSpace(modelPath))
                {
                    if (string.Equals(modelPath, target, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    string fileName = Path.GetFileNameWithoutExtension(modelPath);
                    if (string.Equals(fileName, target, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                return false;
            }

            return (!string.IsNullOrWhiteSpace(sceneObjectName) && sceneObjectName.Contains(target, StringComparison.OrdinalIgnoreCase)) ||
                   (!string.IsNullOrWhiteSpace(modelPath) && modelPath.Contains(target, StringComparison.OrdinalIgnoreCase));
        }
    }
}
