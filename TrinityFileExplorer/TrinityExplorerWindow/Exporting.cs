using Trinity.Core.Math.Hash;
using Trinity.Core.Cache;
using Trinity.Core.Compression;
using Trinity.Core.Flatbuffers.TR.ResourceDictionary;
using Trinity.Core.Serializers.TR;
using Trinity.Core.Utils;
using System.Linq;
using System.Diagnostics;
using Trinity.Core.Flatbuffers.TR.Model;
using System.Drawing;
using System.Threading.Tasks;


namespace TrinityFileExplorer
{
    public partial class TrinityExplorerWindow : Form
    {
        private async Task<bool> ExportWithProgressAsync(ulong[] hashes, string outFolder)
        {
            if (!hasOodleDll) return false;
            if (fileDescriptor == null || fileSystem == null) return false;

            var exportWindow = new ExportProgressWindow(fileDescriptor, fileSystem);
            exportWindow.Show(this);

            try
            {
                var exported = await Task.Run(() => exportWindow.SaveFiles(hashes, outFolder));
                exportWindow.Close();

                if (!exported)
                {
                    MessageBox.Show("No files were exported.\n\nThis usually means the selected files can't be resolved to packs for this RomFS, or the file system doesn't match the file descriptor.", "Export");
                }

                return exported;
            }
            catch (Exception ex)
            {
                try { exportWindow.Close(); } catch { }
                MessageBox.Show(ex.Message, "Export failed");
                return false;
            }
        }

        private void SaveFile(ulong fileHash, string outFolder)
        {
            if (fileDescriptor == null || fileSystem == null)
            {
                return;
            }

            PackedArchive pack = GetPack(fileHash);

            for (int i = 0; i < pack.FileEntry.Length; i++)
            {
                var hash = pack.FileHashes[i];

                if (hash == fileHash)
                {
                    var fileName = GFPakHashCache.GetName(hash);
                    fileName ??= hash.ToString("X16") + ".bin";

                    var entry = pack.FileEntry[i];
                    var buffer = entry.FileBuffer;

                    if (entry.EncryptionType != -1)
                        buffer = Oodle.Decompress(buffer, (long)entry.FileSize);

                    var filepath = string.Format("{0}\\{1}", outFolder, fileName);

                    var dir = Path.GetDirectoryName(filepath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    File.WriteAllBytes(filepath, buffer);

                    break;
                }
            }
        }

	        private PackedArchive GetPack(ulong fileHash)
	        {
                if (fileDescriptor == null || fileSystem == null)
                {
                    throw new InvalidOperationException("Exporting requires a loaded file descriptor and file system.");
                }

	            var packName = fileDescriptor.GetPackName(fileHash);
                if (string.IsNullOrEmpty(packName))
                {
                    throw new InvalidOperationException($"Failed to resolve pack name for file hash {fileHash:X16}.");
                }

                ulong packHash = GFFNV.Hash(packName);
	            int fileIndex = Array.IndexOf(fileSystem.FileHashes, packHash);
                if (fileIndex < 0)
                {
                    throw new InvalidOperationException($"Failed to locate pack '{packName}' ({packHash:X16}) in file system.");
                }

            PackInfo? packInfo = fileDescriptor.GetPackInfo(fileHash);
                if (packInfo == null)
                {
                    throw new InvalidOperationException($"Failed to resolve pack info for file hash {fileHash:X16}.");
                }
            byte[] fileBytes = ONEFILESerializer.SplitTRPAK(Path.Join(ExplorerSettings.GetRomFSPath(), FilepathSettings.trpfsRel), (long)fileSystem.FileOffsets[fileIndex], (long)packInfo.FileSize);

	            PackedArchive pack = FlatBufferConverter.DeserializeFrom<PackedArchive>(fileBytes);
	            return pack;
	        }

	        private async void saveRomFSFileToolStripMenuItem_Click(object sender, EventArgs e)
	        {
	            if (!hasOodleDll) return;

            var sfd = new FolderBrowserDialog();
            if (sfd.ShowDialog() != DialogResult.OK) return;

            var grid = lastContextGrid ?? explorerFileViewer;
            foreach (DataGridViewRow row in grid.SelectedRows)
            {
                if (grid == explorerFileViewer && row.Cells["FileType"].Value.ToString() == "File Folder")
                {
                    await SaveFolderAsync(row.Cells["FileName"].Value.ToString(), sfd.SelectedPath);
                }
                else
                {
                    SaveFile(Convert.ToUInt64(row.Cells["FileHash"].Value.ToString(), 16), sfd.SelectedPath);
                }
            }
        }

        private async Task SaveFolderAsync(string? v, string selectedPath)
        {
            if (string.IsNullOrEmpty(v) || fileDescriptor == null || fileSystem == null)
            {
                return;
            }

            var cwd = explorerFileViewer.GetCwd();
            var diskPath = explorerFileViewer.GetDiskPath();
            var relativeCwd = cwd.StartsWith(diskPath, StringComparison.Ordinal)
                ? cwd.Substring(diskPath.Length)
                : cwd;
            var folderPath = $"{relativeCwd}{v}/";
            var hashes = explorerFileViewer.GetFolderPaths(folderPath).Distinct().ToArray();

            if (hashes.Length == 0)
            {
                return;
            }

            await ExportWithProgressAsync(hashes, selectedPath);
        }

        private void RemoveFromLayeredFSMenuItem_Click(object sender, EventArgs e)
        {
            explorerFileViewer.RemoveFromLayeredFS();
        }

        private void AddToLayeredFSMenuItem_Click(object sender, EventArgs e)
        {
            explorerFileViewer.AddToLayeredFS();
        }
        private async void allFilesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!hasOodleDll) return;
            if (fileDescriptor == null || fileSystem == null) return;

            var sfd = new FolderBrowserDialog();
            if (sfd.ShowDialog() != DialogResult.OK) return;

            await ExportWithProgressAsync(fileDescriptor.FileHashes.ToArray(), sfd.SelectedPath);
        }

        private async void visibleFilesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!hasOodleDll) return;
            if (fileDescriptor == null || fileSystem == null) return;
            if (activeViewer == null) return;

            var sfd = new FolderBrowserDialog();
            if (sfd.ShowDialog() != DialogResult.OK) return;

            await ExportWithProgressAsync(activeViewer.GetFiles().ToArray(), sfd.SelectedPath);

        }

        private async void unhashedFilesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!hasOodleDll) return;
            if (fileDescriptor == null || fileSystem == null) return;
            if (activeViewer == null) return;

            var sfd = new FolderBrowserDialog();
            if (sfd.ShowDialog() != DialogResult.OK) return;

            await ExportWithProgressAsync(activeViewer.GetUnhashedFiles().ToArray(), sfd.SelectedPath);
        }
    }
}
