using System;
using System.Collections.Generic;
using Trinity.Core.Flatbuffers.TR.Model;
using Trinity.Core.Utils;

namespace GFTool.Renderer.Scene.GraphicsObjects
{
    public partial class Model
    {
        private string? currentSkeletonPath;
        private string? dirtyTrmdlPath;
        private string? dirtyTrsklPath;
        private readonly HashSet<string> dirtyTrmshPaths = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> dirtyTrmbfPaths = new(StringComparer.OrdinalIgnoreCase);

        public string TrmdlSourcePath => trmdlSourcePath;
        public string? CurrentSkeletonPath => currentSkeletonPath;
        public IReadOnlyCollection<string> DirtyTrmshPaths => dirtyTrmshPaths;
        public IReadOnlyCollection<string> DirtyTrmbfPaths => dirtyTrmbfPaths;
        public string? DirtyTrmdlPath => dirtyTrmdlPath;
        public string? DirtyTrsklPath => dirtyTrsklPath;

        public bool IsTrmdlDirty => !string.IsNullOrWhiteSpace(dirtyTrmdlPath);
        public bool IsTrsklDirty => !string.IsNullOrWhiteSpace(dirtyTrsklPath);
        public bool HasDirtyMeshes => dirtyTrmshPaths.Count > 0;
        public bool HasDirtyBuffers => dirtyTrmbfPaths.Count > 0;

        public void ClearDirtyFlags()
        {
            dirtyTrmdlPath = null;
            dirtyTrsklPath = null;
            dirtyTrmshPaths.Clear();
            dirtyTrmbfPaths.Clear();
        }

        public void MarkTrmdlDirty(string path)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                dirtyTrmdlPath = path;
            }
        }

        public void MarkTrsklDirty(string path)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                dirtyTrsklPath = path;
            }
        }

        public void MarkTrmshDirty(string path)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                dirtyTrmshPaths.Add(path);
            }
        }

        public void MarkTrmbfDirty(string path)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                dirtyTrmbfPaths.Add(path);
            }
        }

        public void MarkLoadedMeshesDirty(bool includeBuffers)
        {
            foreach (var meshPath in loadedMeshFiles)
            {
                MarkTrmshDirty(meshPath);
                if (!includeBuffers)
                {
                    continue;
                }

                try
                {
                    var trmsh = FlatBufferConverter.DeserializeFrom<TRMSH>(assetProvider.ReadAllBytes(meshPath));
                    var bufferRel = trmsh?.bufferFilePath;
                    if (string.IsNullOrWhiteSpace(bufferRel))
                    {
                        continue;
                    }
                    var bufferPath = modelPath.Combine(bufferRel);
                    MarkTrmbfDirty(bufferPath);
                }
                catch
                {
                    // ignore
                }
            }
        }

        public string ResolveTrmbfPath(string bufferRel)
        {
            return string.IsNullOrWhiteSpace(bufferRel) ? string.Empty : modelPath.Combine(bufferRel);
        }

        public byte[] ReadAssetBytes(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? Array.Empty<byte>() : assetProvider.ReadAllBytes(path);
        }
    }
}
