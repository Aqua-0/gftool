using GFTool.Core.Flatbuffers.TR.Scene.Components;
using GFTool.Core.Flatbuffers.TR.Scene;
using GFTool.Renderer;
using GFTool.Renderer.Core;
using GFTool.Renderer.Core.Graphics;
using GFTool.Renderer.Scene.GraphicsObjects;
using OpenTK.Mathematics;
using System.Drawing;
using System.Text;
using Trinity.Core.Utils;
using Point = System.Drawing.Point;


namespace TrinitySceneView
{
    public partial class SceneViewerForm : Form
    {
        private System.Windows.Forms.Timer? cameraStatusTimer;
        private Label? cameraStatusLbl;

        //Update camera position info
        private void glCtxt_Paint(object sender, PaintEventArgs e)
        {
            if (isSceneLoading)
            {
                return;
            }

            UpdateCameraStatusLabel();
        }

        private void UpdateCameraStatusLabel()
        {
            if (renderCtrl?.renderer == null)
            {
                return;
            }

            if (cameraStatusLbl == null)
            {
                return;
            }

            var cam = renderCtrl.renderer.GetCameraTransform();
            var euler = cam.Rotation.ToEulerAngles();

            string mode = eventUseCameraCheckBox?.Checked == true ? "EventCam" : "FreeCam";
            string rot = (config.RotateModels180X ? "RotX" : config.RotateModels180Y ? "RotY" : "RotNone");
            string map = $"MapA={(config.ApplySceneRotationToActors ? "On" : "Off")} MapC={(config.ApplySceneRotationToEventCamera ? "On" : "Off")}";
            string perf = renderCtrl.ApproxFrameMs > 0.0001f
                ? $" FPS={renderCtrl.ApproxFps:0.0} Frame={renderCtrl.ApproxFrameMs:0.00}ms"
                : string.Empty;

            var text = $"Camera({mode},{rot},{map}) Pos={cam.Position} Euler={euler}{perf}";

            // When using event cam, show the raw script camera too so coords can be compared.
            if (eventShowCameraCheckBox?.Checked == true || eventUseCameraCheckBox?.Checked == true)
            {
                text += $" | EventRaw Pos={eventCameraPos} Rot={eventCameraRotDeg} Fov={eventCameraFovDeg:0.##}";
            }

            cameraStatusLbl.Text = text;
        }

        private void toolstripGBuf_Clicked(object? sender, EventArgs e)
        {
            var item = sender as ToolStripMenuItem;
            if (item == null)
            {
                return;
            }
            if (item.Checked) return;

            GBuffer.DisplayType disp = GBuffer.DisplayType.DISPLAY_ALL;
            switch (item.Name)
            {
                case "toolstripGBuf_All": disp = GBuffer.DisplayType.DISPLAY_ALL; break;
                case "toolstripGBuf_Albedo": disp = GBuffer.DisplayType.DISPLAY_ALBEDO; break;
                case "toolstripGBuf_Normal": disp = GBuffer.DisplayType.DISPLAY_NORMAL; break;
                case "toolstripGBuf_Specular": disp = GBuffer.DisplayType.DISPLAY_SPECULAR; break;
                case "toolstripGBuf_AO": disp = GBuffer.DisplayType.DISPLAY_AO; break;
                case "toolstripGBuf_Depth": disp = GBuffer.DisplayType.DISPLAY_DEPTH; break;
            }

            //Only one checked at a time
            toolstripGBuf_All.CheckState = item.Name == "toolstripGBuf_All" ? CheckState.Checked : CheckState.Unchecked;
            toolstripGBuf_Albedo.CheckState = item.Name == "toolstripGBuf_Albedo" ? CheckState.Checked : CheckState.Unchecked;
            toolstripGBuf_Normal.CheckState = item.Name == "toolstripGBuf_Normal" ? CheckState.Checked : CheckState.Unchecked;
            toolstripGBuf_Specular.CheckState = item.Name == "toolstripGBuf_Specular" ? CheckState.Checked : CheckState.Unchecked;
            toolstripGBuf_AO.CheckState = item.Name == "toolstripGBuf_AO" ? CheckState.Checked : CheckState.Unchecked;
            toolstripGBuf_Depth.CheckState = item.Name == "toolstripGBuf_Depth" ? CheckState.Checked : CheckState.Unchecked;

            renderCtrl.renderer.SetGBufferDisplayMode(disp);
        }

        private void glCtxt_KeyDown(object? sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Home:
                    ResetCameraToOrigin();
                    e.Handled = true;
                    break;
                case Keys.W: KeyboardControls.Forward = true; break;
                case Keys.A: KeyboardControls.Left = true; break;
                case Keys.S: KeyboardControls.Backward = true; break;
                case Keys.D: KeyboardControls.Right = true; break;
                case Keys.Q: KeyboardControls.Up = true; break;
                case Keys.E: KeyboardControls.Down = true; break;
            }
        }

        private void glCtxt_KeyUp(object? sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.W: KeyboardControls.Forward = false; break;
                case Keys.A: KeyboardControls.Left = false; break;
                case Keys.S: KeyboardControls.Backward = false; break;
                case Keys.D: KeyboardControls.Right = false; break;
                case Keys.Q: KeyboardControls.Up = false; break;
                case Keys.E: KeyboardControls.Down = false; break;
            }
        }

        //Setup message list
        private void glCtxt_Load(object sender, EventArgs e)
        {
            //Connect to message handler
            MessageHandler.Instance.MessageCallback += messageHandler_Callback;
            var messageIcons = new ImageList();
            messageIcons.Images.Add("Log", SystemIcons.Information.ToBitmap());
            messageIcons.Images.Add("Warning", SystemIcons.Warning.ToBitmap());
            messageIcons.Images.Add("Error", SystemIcons.Error.ToBitmap());
            messageListView.SmallImageList = messageIcons;
            messageListView.FullRowSelect = true;
            messageListView.AutoResizeColumn(0, ColumnHeaderAutoResizeStyle.HeaderSize);

            if (cameraStatusLbl == null)
            {
                cameraStatusLbl = new Label
                {
                    Dock = DockStyle.Top,
                    Height = 14,
                    Text = "Camera: (loading...)"
                };
                bottomPanel.Controls.Add(cameraStatusLbl);
                bottomPanel.Controls.SetChildIndex(cameraStatusLbl, 0);
            }

            if (cameraStatusTimer == null)
            {
                cameraStatusTimer = new System.Windows.Forms.Timer { Interval = 100, Enabled = true };
                cameraStatusTimer.Tick += (_, _) =>
                {
                    try
                    {
                        UpdateCameraStatusLabel();
                    }
                    catch
                    {
                        // ignore
                    }
                };
            }
        }

        //Message handler
        private void messageHandler_Callback(object? sender, GFTool.Renderer.Core.Message e)
        {
            if (IsHandleCreated && InvokeRequired)
            {
                try
                {
                    BeginInvoke((Action)(() => messageHandler_Callback(sender, e)));
                }
                catch
                {
                    // Ignore shutdown races / handle disposal.
                }
                return;
            }

            var item = new ListViewItem();
            item.Name = e.GetHashCode().ToString();
            item.Text = e.Content;
            item.ImageKey = e.Type switch
            {
                MessageType.LOG => "Log",
                MessageType.WARNING => "Warning",
                MessageType.ERROR => "Error",
                _ => "Log"
            };

            //Only unique errors
            if (!messageListView.Items.ContainsKey(e.GetHashCode().ToString()))
            {
                messageListView.Items.Add(item);
                messageListView.EnsureVisible(messageListView.Items.Count - 1);
            }
        }

        private void ResetCameraToOrigin()
        {
            KeyboardControls.Forward = false;
            KeyboardControls.Left = false;
            KeyboardControls.Backward = false;
            KeyboardControls.Right = false;
            KeyboardControls.Up = false;
            KeyboardControls.Down = false;

            if (renderCtrl?.renderer == null)
            {
                return;
            }

            renderCtrl.renderer.FocusCamera(Vector3.Zero, 5.0f);
            renderCtrl.renderer.SetCameraClipPlanes(0.1f, 10_000.0f);
            renderCtrl.Invalidate();
        }
    }
}
