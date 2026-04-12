using static OpenTK.Graphics.OpenGL.GL;

namespace TrinityUikitEditor
{
    public partial class UikitEditor : Form
    {
        private TRUiView? uiView;

        public UikitEditor()
        {
            InitializeComponent();
        }

        private void openToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var ofd = new OpenFileDialog();
            if (ofd.ShowDialog() != DialogResult.OK) return;
            sceneView.Nodes.Clear();
            uiView = new TRUiView();
            uiView.DeserializeView(ofd.FileName);
            sceneView.Nodes.Add(uiView.TreeNode);
        }

        private void ClearProperties()
        {
            InfoBox.Text = string.Empty;
        }

        private void sceneView_MouseUp(object sender, MouseEventArgs e)
        {
            Point ClickPoint = new Point(e.X, e.Y);
            TreeNode? ClickNode = sceneView.GetNodeAt(ClickPoint);
            sceneView.SelectedNode = ClickNode;
            if (ClickNode == null) return;

            if (e.Button == MouseButtons.Right)
            {
                Point ScreenPoint = sceneView.PointToScreen(ClickPoint);
                Point FormPoint = this.PointToClient(ScreenPoint);
                sceneContext.Show(this, FormPoint);
            }

            //Check for data to display
            if (uiView == null)
            {
                ClearProperties();
                return;
            }

            var meta = uiView.GetNodeMeta(sceneView.SelectedNode);
            if (!meta.HasValue || meta.Value.Data == null)
            {
                ClearProperties();
                return;
            }

            InfoBox.Text = TRUiViewProperties.GetProperties(meta.Value.Type, meta.Value.Data);
        }
    }
}
