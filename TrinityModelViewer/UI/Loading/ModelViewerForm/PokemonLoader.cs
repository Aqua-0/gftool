using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TrinityModelViewer
{
    public partial class ModelViewerForm
    {
        public async Task LoadModelFromPathAsync(string trmdlPath, bool replaceScene = true)
        {
            if (string.IsNullOrWhiteSpace(trmdlPath))
            {
                return;
            }

            string full;
            try
            {
                full = Path.GetFullPath(trmdlPath);
            }
            catch
            {
                full = trmdlPath;
            }

            if (!File.Exists(full))
            {
                MessageBox.Show(
                    this,
                    $"Model file not found:\n{full}",
                    "Pokemon Loader",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            BeginModelLoad();
            try
            {
                if (replaceScene)
                {
                    ClearAll();
                }
                await AddModelToSceneAsync(full);
            }
            finally
            {
                EndModelLoad();
            }
        }
    }
}
