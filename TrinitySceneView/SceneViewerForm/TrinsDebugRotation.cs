using System.Drawing;
using System.Windows.Forms;
using GFTool.Renderer.Core;
using Trinity.Core.Utils;

namespace TrinitySceneView
{
    public partial class SceneViewerForm
    {
        private void ShowSceneDebugRotationDialog()
        {
            ShowDebugTransformDialog(
                "Scene Debug Transform",
                config.SceneDebugRotateX,
                config.SceneDebugRotateY,
                config.SceneDebugRotateZ,
                config.SceneDebugGlobalRotateX,
                config.SceneDebugGlobalRotateY,
                config.SceneDebugGlobalRotateZ,
                config.SceneDebugTranslateX,
                config.SceneDebugTranslateY,
                config.SceneDebugTranslateZ,
                config.SceneDebugLiveUpdate,
                apply: (localGroup, globalGroup, translationGroup) =>
                {
                    config.SceneDebugRotateX = (float)localGroup.X.Value;
                    config.SceneDebugRotateY = (float)localGroup.Y.Value;
                    config.SceneDebugRotateZ = (float)localGroup.Z.Value;
                    config.SceneDebugGlobalRotateX = (float)globalGroup.X.Value;
                    config.SceneDebugGlobalRotateY = (float)globalGroup.Y.Value;
                    config.SceneDebugGlobalRotateZ = (float)globalGroup.Z.Value;
                    config.SceneDebugTranslateX = (float)translationGroup.X.Value;
                    config.SceneDebugTranslateY = (float)translationGroup.Y.Value;
                    config.SceneDebugTranslateZ = (float)translationGroup.Z.Value;
                    config.SceneDebugLiveUpdate = localGroup.Group.FindForm() is Form sceneForm && sceneForm.Controls["__liveUpdateCheck"] is CheckBox sceneLive && sceneLive.Checked;
                    config.Save();
                    ReapplyLoadedSceneModelTransforms();
                    MessageHandler.Instance.AddMessage(
                        MessageType.LOG,
                        $"[Scene] Scene debug transform local=({config.SceneDebugRotateX:0.###}, {config.SceneDebugRotateY:0.###}, {config.SceneDebugRotateZ:0.###}) global=({config.SceneDebugGlobalRotateX:0.###}, {config.SceneDebugGlobalRotateY:0.###}, {config.SceneDebugGlobalRotateZ:0.###}) translate=({config.SceneDebugTranslateX:0.###}, {config.SceneDebugTranslateY:0.###}, {config.SceneDebugTranslateZ:0.###})");
                });
        }

        private void ShowTrinsDebugRotationDialog()
        {
            ShowDebugTransformDialog(
                "TRINS Debug Transform",
                config.TrinsDebugRotateX,
                config.TrinsDebugRotateY,
                config.TrinsDebugRotateZ,
                config.TrinsDebugGlobalRotateX,
                config.TrinsDebugGlobalRotateY,
                config.TrinsDebugGlobalRotateZ,
                config.TrinsDebugTranslateX,
                config.TrinsDebugTranslateY,
                config.TrinsDebugTranslateZ,
                config.TrinsDebugLiveUpdate,
                apply: ApplyTrinsDebugTransformFromInputs);
        }

        private void ShowObjectDebugTransformDialog()
        {
            using var dialog = new Form
            {
                Text = "Object Debug Transform",
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                ClientSize = new Size(340, 438)
            };

            var targetLabel = new Label
            {
                Text = "Target",
                AutoSize = true,
                Location = new Point(12, 14)
            };

            var targetInput = new TextBox
            {
                Location = new Point(62, 11),
                Size = new Size(266, 23),
                Text = config.ObjectDebugTarget ?? string.Empty
            };

            var exactMatch = new CheckBox
            {
                Text = "Exact Match",
                AutoSize = true,
                Location = new Point(12, 42),
                Checked = config.ObjectDebugExactMatch
            };

            var liveUpdate = new CheckBox
            {
                Text = "Live Update",
                AutoSize = true,
                Location = new Point(120, 42),
                Checked = config.ObjectDebugLiveUpdate
            };

            var localGroup = CreateTransformGroup("Local Rotation", 12, 68, config.ObjectDebugRotateX, config.ObjectDebugRotateY, config.ObjectDebugRotateZ);
            var globalGroup = CreateTransformGroup("Global Rotation", 12, 156, config.ObjectDebugGlobalRotateX, config.ObjectDebugGlobalRotateY, config.ObjectDebugGlobalRotateZ);
            var translationGroup = CreateTransformGroup("Translation", 12, 244, config.ObjectDebugTranslateX, config.ObjectDebugTranslateY, config.ObjectDebugTranslateZ);

            var btnApply = new Button { Text = "Apply", Location = new Point(12, 400), Size = new Size(70, 26) };
            var btnReset = new Button { Text = "Reset", Location = new Point(88, 400), Size = new Size(70, 26) };
            var btnClose = new Button { Text = "Close", Location = new Point(258, 400), Size = new Size(70, 26), DialogResult = DialogResult.OK };

            void ApplyInputs()
            {
                config.ObjectDebugTarget = string.IsNullOrWhiteSpace(targetInput.Text) ? null : targetInput.Text.Trim();
                config.ObjectDebugExactMatch = exactMatch.Checked;
                config.ObjectDebugLiveUpdate = liveUpdate.Checked;
                config.ObjectDebugRotateX = (float)localGroup.X.Value;
                config.ObjectDebugRotateY = (float)localGroup.Y.Value;
                config.ObjectDebugRotateZ = (float)localGroup.Z.Value;
                config.ObjectDebugGlobalRotateX = (float)globalGroup.X.Value;
                config.ObjectDebugGlobalRotateY = (float)globalGroup.Y.Value;
                config.ObjectDebugGlobalRotateZ = (float)globalGroup.Z.Value;
                config.ObjectDebugTranslateX = (float)translationGroup.X.Value;
                config.ObjectDebugTranslateY = (float)translationGroup.Y.Value;
                config.ObjectDebugTranslateZ = (float)translationGroup.Z.Value;
                config.Save();
                ReapplyLoadedSceneModelTransforms();
                MessageHandler.Instance.AddMessage(
                    MessageType.LOG,
                    $"[Scene] Object debug transform target='{config.ObjectDebugTarget ?? "<none>"}' exact={config.ObjectDebugExactMatch} live={config.ObjectDebugLiveUpdate} local=({config.ObjectDebugRotateX:0.###}, {config.ObjectDebugRotateY:0.###}, {config.ObjectDebugRotateZ:0.###}) global=({config.ObjectDebugGlobalRotateX:0.###}, {config.ObjectDebugGlobalRotateY:0.###}, {config.ObjectDebugGlobalRotateZ:0.###}) translate=({config.ObjectDebugTranslateX:0.###}, {config.ObjectDebugTranslateY:0.###}, {config.ObjectDebugTranslateZ:0.###})");
            }

            void ApplyInputsIfLive()
            {
                if (liveUpdate.Checked)
                {
                    ApplyInputs();
                }
            }

            btnApply.Click += (_, _) => ApplyInputs();
            btnReset.Click += (_, _) =>
            {
                targetInput.Text = string.Empty;
                exactMatch.Checked = true;
                ResetTransformGroup(localGroup);
                ResetTransformGroup(globalGroup);
                ResetTransformGroup(translationGroup);
                ApplyInputs();
            };

            targetInput.TextChanged += (_, _) => ApplyInputsIfLive();
            exactMatch.CheckedChanged += (_, _) => ApplyInputsIfLive();
            liveUpdate.CheckedChanged += (_, _) => ApplyInputsIfLive();
            localGroup.X.ValueChanged += (_, _) => ApplyInputsIfLive();
            localGroup.Y.ValueChanged += (_, _) => ApplyInputsIfLive();
            localGroup.Z.ValueChanged += (_, _) => ApplyInputsIfLive();
            globalGroup.X.ValueChanged += (_, _) => ApplyInputsIfLive();
            globalGroup.Y.ValueChanged += (_, _) => ApplyInputsIfLive();
            globalGroup.Z.ValueChanged += (_, _) => ApplyInputsIfLive();
            translationGroup.X.ValueChanged += (_, _) => ApplyInputsIfLive();
            translationGroup.Y.ValueChanged += (_, _) => ApplyInputsIfLive();
            translationGroup.Z.ValueChanged += (_, _) => ApplyInputsIfLive();

            dialog.AcceptButton = btnApply;
            dialog.CancelButton = btnClose;
            dialog.Controls.Add(targetLabel);
            dialog.Controls.Add(targetInput);
            dialog.Controls.Add(exactMatch);
            dialog.Controls.Add(liveUpdate);
            dialog.Controls.Add(localGroup.Group);
            dialog.Controls.Add(globalGroup.Group);
            dialog.Controls.Add(translationGroup.Group);
            dialog.Controls.Add(btnApply);
            dialog.Controls.Add(btnReset);
            dialog.Controls.Add(btnClose);
            dialog.ShowDialog(this);
        }

        private void ShowDebugTransformDialog(
            string title,
            float localX,
            float localY,
            float localZ,
            float globalX,
            float globalY,
            float globalZ,
            float translateX,
            float translateY,
            float translateZ,
            bool liveUpdateEnabled,
            Action<(GroupBox Group, NumericUpDown X, NumericUpDown Y, NumericUpDown Z), (GroupBox Group, NumericUpDown X, NumericUpDown Y, NumericUpDown Z), (GroupBox Group, NumericUpDown X, NumericUpDown Y, NumericUpDown Z)> apply)
        {
            using var dialog = new Form
            {
                Text = title,
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                ClientSize = new Size(340, 352)
            };

            var liveUpdate = new CheckBox
            {
                Name = "__liveUpdateCheck",
                Text = "Live Update",
                AutoSize = true,
                Location = new Point(12, 12),
                Checked = liveUpdateEnabled
            };

            var localGroup = CreateTransformGroup("Local Rotation", 12, 34, localX, localY, localZ);
            var globalGroup = CreateTransformGroup("Global Rotation", 12, 122, globalX, globalY, globalZ);
            var translationGroup = CreateTransformGroup("Translation", 12, 210, translateX, translateY, translateZ);

            var btnApply = new Button { Text = "Apply", Location = new Point(12, 314), Size = new Size(70, 26) };
            var btnReset = new Button { Text = "Reset", Location = new Point(88, 314), Size = new Size(70, 26) };
            var btnClose = new Button { Text = "Close", Location = new Point(258, 314), Size = new Size(70, 26), DialogResult = DialogResult.OK };

            void ApplyIfLive()
            {
                if (liveUpdate.Checked)
                {
                    apply(localGroup, globalGroup, translationGroup);
                }
            }

            btnApply.Click += (_, _) => apply(localGroup, globalGroup, translationGroup);
            btnReset.Click += (_, _) =>
            {
                ResetTransformGroup(localGroup);
                ResetTransformGroup(globalGroup);
                ResetTransformGroup(translationGroup);
                apply(localGroup, globalGroup, translationGroup);
            };
            liveUpdate.CheckedChanged += (_, _) => ApplyIfLive();
            localGroup.X.ValueChanged += (_, _) => ApplyIfLive();
            localGroup.Y.ValueChanged += (_, _) => ApplyIfLive();
            localGroup.Z.ValueChanged += (_, _) => ApplyIfLive();
            globalGroup.X.ValueChanged += (_, _) => ApplyIfLive();
            globalGroup.Y.ValueChanged += (_, _) => ApplyIfLive();
            globalGroup.Z.ValueChanged += (_, _) => ApplyIfLive();
            translationGroup.X.ValueChanged += (_, _) => ApplyIfLive();
            translationGroup.Y.ValueChanged += (_, _) => ApplyIfLive();
            translationGroup.Z.ValueChanged += (_, _) => ApplyIfLive();

            dialog.AcceptButton = btnApply;
            dialog.CancelButton = btnClose;
            dialog.Controls.Add(liveUpdate);
            dialog.Controls.Add(localGroup.Group);
            dialog.Controls.Add(globalGroup.Group);
            dialog.Controls.Add(translationGroup.Group);
            dialog.Controls.Add(btnApply);
            dialog.Controls.Add(btnReset);
            dialog.Controls.Add(btnClose);
            dialog.ShowDialog(this);
        }

        private static (GroupBox Group, NumericUpDown X, NumericUpDown Y, NumericUpDown Z) CreateTransformGroup(string title, int x, int y, float valueX, float valueY, float valueZ)
        {
            var group = new GroupBox
            {
                Text = title,
                Location = new Point(x, y),
                Size = new Size(316, 82)
            };

            var labelX = new Label { Text = "X", AutoSize = true, Location = new Point(10, 25) };
            var labelY = new Label { Text = "Y", AutoSize = true, Location = new Point(10, 51) };
            var labelZ = new Label { Text = "Z", AutoSize = true, Location = new Point(164, 25) };

            var inputX = CreateTransformInput(valueX, 28, 22);
            var inputY = CreateTransformInput(valueY, 28, 48);
            var inputZ = CreateTransformInput(valueZ, 182, 22);

            group.Controls.Add(labelX);
            group.Controls.Add(labelY);
            group.Controls.Add(labelZ);
            group.Controls.Add(inputX);
            group.Controls.Add(inputY);
            group.Controls.Add(inputZ);
            return (group, inputX, inputY, inputZ);
        }

        private static NumericUpDown CreateTransformInput(float value, int x, int y)
        {
            return new NumericUpDown
            {
                DecimalPlaces = 3,
                Increment = 0.5M,
                Minimum = -360,
                Maximum = 360,
                Value = (decimal)value,
                Location = new Point(x, y),
                Size = new Size(120, 23)
            };
        }

        private static void ResetTransformGroup((GroupBox Group, NumericUpDown X, NumericUpDown Y, NumericUpDown Z) group)
        {
            group.X.Value = 0;
            group.Y.Value = 0;
            group.Z.Value = 0;
        }

        private void ApplyTrinsDebugTransformFromInputs(
            (GroupBox Group, NumericUpDown X, NumericUpDown Y, NumericUpDown Z) localGroup,
            (GroupBox Group, NumericUpDown X, NumericUpDown Y, NumericUpDown Z) globalGroup,
            (GroupBox Group, NumericUpDown X, NumericUpDown Y, NumericUpDown Z) translationGroup)
        {
            config.TrinsDebugRotateX = (float)localGroup.X.Value;
            config.TrinsDebugRotateY = (float)localGroup.Y.Value;
            config.TrinsDebugRotateZ = (float)localGroup.Z.Value;
            config.TrinsDebugGlobalRotateX = (float)globalGroup.X.Value;
            config.TrinsDebugGlobalRotateY = (float)globalGroup.Y.Value;
            config.TrinsDebugGlobalRotateZ = (float)globalGroup.Z.Value;
            config.TrinsDebugTranslateX = (float)translationGroup.X.Value;
            config.TrinsDebugTranslateY = (float)translationGroup.Y.Value;
            config.TrinsDebugTranslateZ = (float)translationGroup.Z.Value;
            config.TrinsDebugLiveUpdate = localGroup.Group.FindForm() is Form trinsForm && trinsForm.Controls["__liveUpdateCheck"] is CheckBox trinsLive && trinsLive.Checked;
            config.Save();
            ReapplyLoadedSceneModelTransforms();
            MessageHandler.Instance.AddMessage(
                MessageType.LOG,
                $"[Scene] TRINS debug transform local=({config.TrinsDebugRotateX:0.###}, {config.TrinsDebugRotateY:0.###}, {config.TrinsDebugRotateZ:0.###}) global=({config.TrinsDebugGlobalRotateX:0.###}, {config.TrinsDebugGlobalRotateY:0.###}, {config.TrinsDebugGlobalRotateZ:0.###}) translate=({config.TrinsDebugTranslateX:0.###}, {config.TrinsDebugTranslateY:0.###}, {config.TrinsDebugTranslateZ:0.###})");
        }
    }
}
