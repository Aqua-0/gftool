using System.Drawing;
using System.Windows.Forms;

namespace TrinitySceneView
{
    public partial class SceneViewerForm
    {
        private void InitializeNpcActorsTab()
        {
            // Replace the old spawner-candidates UX with an "event actors" list.
            npcTabActorsMode = true;

            spawnerLookupTextBox.Visible = false;
            sceneSpawnerComboBox.Visible = false;

            btnListSceneSpawners.Text = "Refresh actors";
            btnLookupSpawner.Text = "Spawn all";
            btnSpawnCandidate.Text = "Spawn";

            btnListSceneSpawners.Location = new Point(6, 6);
            btnLookupSpawner.Location = new Point(120, 6);
            btnSpawnCandidate.Location = new Point(206, 6);

            btnListSceneSpawners.Size = new Size(110, 23);
            btnLookupSpawner.Size = new Size(82, 23);
            btnSpawnCandidate.Size = new Size(76, 23);

            spawnerLookupPanel.Height = 34;

            ConfigureNpcActorsListView();
            UpdateNpcActorsUiEnabled(false);
        }

        private void ConfigureNpcActorsListView()
        {
            spawnerCandidatesListView.MultiSelect = true;
            spawnerCandidatesListView.Columns.Clear();
            spawnerCandidatesListView.Columns.Add("Actor", 170);
            spawnerCandidatesListView.Columns.Add("Kind", 60);
            spawnerCandidatesListView.Columns.Add("Spawned", 60);

            spawnerCandidateDetailsTextBox.Text = string.Empty;
        }
    }
}
