using GFTool.Renderer.Core.Graphics;
using System.Drawing;
using Trinity.Core.Utils;
using GFTool.Renderer.Core;

namespace TrinitySceneView
{
    public partial class SceneViewerForm
    {
        private void ShowSceneLoadTuningDialog()
        {
            using var form = new Form
            {
                Text = "Scene Load Tuning",
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                StartPosition = FormStartPosition.CenterParent,
                ClientSize = new Size(320, 150)
            };

            var label = new Label
            {
                Left = 12,
                Top = 12,
                Width = 290,
                Height = 36,
                Text = "CPU prepare parallelism and GPU work budget.\r\nHigher values load faster but can impact frame smoothness."
            };

            var cpuLabel = new Label
            {
                Left = 12,
                Top = 58,
                Width = 140,
                Text = "CPU Parallelism"
            };

            var numeric = new NumericUpDown
            {
                Left = 12,
                Top = 78,
                Width = 120,
                Minimum = 0,
                Maximum = Math.Max(64, Environment.ProcessorCount * 2),
                DecimalPlaces = 0,
                Value = Math.Clamp(config.SceneLoadCpuParallelism, 0, Math.Max(64, Environment.ProcessorCount * 2))
            };

            var gpuLabel = new Label
            {
                Left = 172,
                Top = 58,
                Width = 140,
                Text = "GPU Work Budget (ms)"
            };

            var gpuBudget = new NumericUpDown
            {
                Left = 172,
                Top = 78,
                Width = 120,
                Minimum = 1,
                Maximum = 33,
                DecimalPlaces = 1,
                Increment = 0.5m,
                Value = Math.Clamp((decimal)(config.AsyncGpuWorkBudgetMs > 0f ? config.AsyncGpuWorkBudgetMs : 3.0f), 1m, 33m)
            };

            var apply = new Button
            {
                Text = "Apply",
                Left = 12,
                Top = 112,
                Width = 80,
                DialogResult = DialogResult.OK
            };

            var close = new Button
            {
                Text = "Close",
                Left = 228,
                Top = 112,
                Width = 80,
                DialogResult = DialogResult.Cancel
            };

            apply.Click += (_, _) =>
            {
                config.SceneLoadCpuParallelism = (int)numeric.Value;
                config.AsyncGpuWorkBudgetMs = (float)gpuBudget.Value;
                config.Save();
                int effective = GetSceneLoadCpuParallelism();
                RenderOptions.AsyncGpuWorkBudgetMs = config.AsyncGpuWorkBudgetMs;
                MessageHandler.Instance.AddMessage(
                    MessageType.LOG,
                    $"[Scene] Scene load tuning set cpu={config.SceneLoadCpuParallelism} (effective={effective}) gpu-budget={config.AsyncGpuWorkBudgetMs:0.0}ms.");
                form.Close();
            };

            form.Controls.Add(label);
            form.Controls.Add(cpuLabel);
            form.Controls.Add(numeric);
            form.Controls.Add(gpuLabel);
            form.Controls.Add(gpuBudget);
            form.Controls.Add(apply);
            form.Controls.Add(close);
            form.AcceptButton = apply;
            form.CancelButton = close;
            form.ShowDialog(this);
        }

        private int GetSceneLoadCpuParallelism()
        {
            int configured = config.SceneLoadCpuParallelism;
            if (configured > 0)
            {
                return configured;
            }

            return Math.Max(2, Environment.ProcessorCount / 2);
        }
    }
}
