using System.Drawing;
using System.Windows.Forms;

namespace TrinitySceneView
{
    public partial class SceneViewerForm
    {
        private Label? perfHudLabel;
        private Control? perfHudHost;
        private System.Windows.Forms.Timer? perfHudTimer;

        private void InitializePerfHud()
        {
            perfHudLabel = new Label
            {
                AutoSize = true,
                BackColor = Color.FromArgb(180, 0, 0, 0),
                ForeColor = Color.White,
                Font = new Font(FontFamily.GenericMonospace, 8.5f),
                Padding = new Padding(6),
                Visible = config.EnablePerfHud
            };

            perfHudHost = renderCtrl.Parent ?? this;
            perfHudHost.Controls.Add(perfHudLabel);
            perfHudLabel.BringToFront();
            perfHudLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            UpdatePerfHudPosition();
            renderCtrl.LocationChanged += (_, _) => UpdatePerfHudPosition();
            renderCtrl.SizeChanged += (_, _) => UpdatePerfHudPosition();

            perfHudTimer = new System.Windows.Forms.Timer { Interval = 200 };
            perfHudTimer.Tick += (_, _) =>
            {
                if (perfHudLabel == null || renderCtrl?.renderer == null)
                {
                    return;
                }

                if (!config.EnablePerfHud)
                {
                    if (perfHudLabel.Visible)
                    {
                        perfHudLabel.Visible = false;
                    }

                    return;
                }

                var p = renderCtrl.renderer.LastPerfFrame;
                var stats = renderCtrl.renderer.LastPerfStats;
                var counters = stats.Counters;
                var breakdown = stats.GeometryBreakdown;
                var topModels = breakdown.TopModels ?? Array.Empty<GFTool.Renderer.RenderContext.PerfGeometryEntry>();
                var topMaterials = breakdown.TopMaterials ?? Array.Empty<GFTool.Renderer.RenderContext.PerfGeometryEntry>();
                var text =
                    $"Frame {p.FrameMs,5:0.0}ms\n" +
                    $"Anim  {p.UpdateAnimationMs,5:0.0}  Geo {p.GeometryMs,5:0.0}\n" +
                    $"Light {p.LightingMs,5:0.0}  Final {p.FinalMs,5:0.0}\n" +
                    $"Grid  {p.GridMs,5:0.0}  Skel {p.SkeletonMs,5:0.0}\n" +
                    $"Trnsp {p.TransparentMs,5:0.0}  Outl {p.OutlineMs,5:0.0}\n" +
                    $"DC {counters.DrawCalls,5}  Tris {counters.Triangles,7}  Allocs {stats.GeometryAllocBytes / 1024.0,6:0.0}KB\n" +
                    $"Mats {counters.MaterialUses,4}  Tex {counters.TextureBinds,4}  Skin {counters.SkinMatrixUploads,4}  Fin {p.GeometryFinishWaitMs,4:0.0}  Present {p.PresentMs,4:0.0}\n" +
                    $"Models {breakdown.TotalModels,4}  Vis {breakdown.VisibleModels,4}  Cull {breakdown.CulledModels,4}";

                if (topModels.Length > 0)
                {
                    text += "\nTopM";
                    for (int i = 0; i < topModels.Length; i++)
                    {
                        var entry = topModels[i];
                        text += $"\n {i + 1}. {entry.Name} dc={entry.DrawCalls} tri={entry.Triangles}";
                    }
                }

                if (topMaterials.Length > 0)
                {
                    text += "\nTopMat";
                    for (int i = 0; i < topMaterials.Length; i++)
                    {
                        var entry = topMaterials[i];
                        text += $"\n {i + 1}. {entry.Name} dc={entry.DrawCalls} tri={entry.Triangles}";
                    }
                }

                perfHudLabel.Text = text;

                if (!perfHudLabel.Visible)
                {
                    perfHudLabel.Visible = true;
                }

                perfHudLabel.BringToFront();
            };
            perfHudTimer.Start();
        }

        private void SetPerfHudVisible(bool visible)
        {
            if (perfHudLabel != null)
            {
                perfHudLabel.Visible = visible;
                if (visible)
                {
                    perfHudLabel.BringToFront();
                }
            }
        }

        private void UpdatePerfHudPosition()
        {
            if (perfHudLabel == null)
            {
                return;
            }

            var host = perfHudHost ?? renderCtrl.Parent;
            if (host == null)
            {
                return;
            }

            var locationInHost = host.PointToClient(renderCtrl.PointToScreen(Point.Empty));
            perfHudLabel.Location = new Point(locationInHost.X + 8, locationInHost.Y + 8);
        }
    }
}
