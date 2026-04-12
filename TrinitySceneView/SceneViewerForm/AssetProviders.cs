using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using GFTool.Renderer.Core;
using Trinity.Core.Assets;
using Trinity.Core.Cache;
using Trinity.Core.Utils;
using TrinitySceneView.UI;

namespace TrinitySceneView
{
    public partial class SceneViewerForm
    {
        private readonly object assetProviderLock = new();
        private readonly List<IAssetProvider> extraAssetProviders = new();
        private IAssetProvider? sceneAssetProvider;
        private string? archiveFieldRoot;
        private readonly Dictionary<ulong, string> gfpakHashToPackRelPath = new();
        private AssetRootDiskAssetProvider? assetRootProvider;
        private string? assetRootProviderRoot;
        private DynamicSceneAssetProvider? dynamicSceneAssetProvider;

        private void RebuildSceneAssetProvider()
        {
            lock (assetProviderLock)
            {
                if (!string.IsNullOrWhiteSpace(assetRoot) && Directory.Exists(assetRoot))
                {
                    if (!string.Equals(assetRootProviderRoot, assetRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        assetRootProviderRoot = assetRoot;
                        assetRootProvider = new AssetRootDiskAssetProvider(assetRoot);
                    }
                }
                else
                {
                    assetRootProviderRoot = null;
                    assetRootProvider = null;
                }

                dynamicSceneAssetProvider ??= new DynamicSceneAssetProvider(this);
                sceneAssetProvider = dynamicSceneAssetProvider;
            }
        }

        private void AutoMountGfpaksFromAssetRoot()
        {
            var detected = DetectArchiveFieldRoot(assetRoot);
            if (!string.IsNullOrWhiteSpace(detected))
            {
                archiveFieldRoot = detected;
            }

            if (string.IsNullOrWhiteSpace(archiveFieldRoot) || !Directory.Exists(archiveFieldRoot))
            {
                return;
            }

            // Mount only the common packs eagerly; area model packs are mounted on-demand.
            TryMountGfpakIfPresent(Path.Combine(archiveFieldRoot, "resident_release.gfpak"));
            TryMountGfpakIfPresent(Path.Combine(archiveFieldRoot, "light.gfpak"));
        }

        private static string? DetectArchiveFieldRoot(string? rootCandidate)
        {
            if (string.IsNullOrWhiteSpace(rootCandidate))
            {
                return null;
            }

            try
            {
                var full = Path.GetFullPath(rootCandidate);

                // Common case: user points at game root that contains archive/field/.
                var archiveField = Path.Combine(full, "archive", "field");
                if (Directory.Exists(archiveField))
                {
                    return archiveField;
                }

                // Also allow pointing directly at the field archive folder.
                if (Directory.Exists(full) && Directory.EnumerateFiles(full, "*.gfpak", SearchOption.TopDirectoryOnly).Any())
                {
                    return full;
                }
            }
            catch
            {
                // Ignore path issues.
            }

            return null;
        }

        private bool TryMountGfpakIfPresent(string gfpakPath)
        {
            if (string.IsNullOrWhiteSpace(gfpakPath) || !File.Exists(gfpakPath))
            {
                return false;
            }

            lock (assetProviderLock)
            {
                if (extraAssetProviders.OfType<GfpakAssetProvider>().Any(p =>
                        string.Equals(p.ContainerPath, gfpakPath, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }

            try
            {
                lock (assetProviderLock)
                {
                    extraAssetProviders.Add(new GfpakAssetProvider(gfpakPath));
                }
                MessageHandler.Instance.AddMessage(MessageType.LOG, $"[GFPAK] Mounted: {gfpakPath}");
                return true;
            }
            catch (Exception ex)
            {
                MessageHandler.Instance.AddMessage(MessageType.WARNING, $"[GFPAK] Failed to mount '{gfpakPath}': {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        private void DisposeExtraAssetProviders()
        {
            lock (assetProviderLock)
            {
                foreach (var p in extraAssetProviders.ToList())
                {
                    try { p.Dispose(); } catch { }
                }
                extraAssetProviders.Clear();
            }

            RebuildSceneAssetProvider();
        }

        private void addGfpaks_Click(object? sender, EventArgs e)
        {
            using var ofd = new OpenFileDialog
            {
                Filter = "GFPAK (*.gfpak)|*.gfpak|All files (*.*)|*.*",
                Multiselect = true
            };
            if (ofd.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            int opened = 0;
            foreach (var path in ofd.FileNames ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                try
                {
                    lock (assetProviderLock)
                    {
                        extraAssetProviders.Add(new GfpakAssetProvider(path));
                    }
                    opened++;
                }
                catch (Exception ex)
                {
                    MessageHandler.Instance.AddMessage(MessageType.WARNING, $"[GFPAK] Failed to open '{path}': {ex.GetType().Name}: {ex.Message}");
                }
            }

            if (archiveFieldRoot == null)
            {
                GfpakAssetProvider? any;
                lock (assetProviderLock)
                {
                    any = extraAssetProviders.OfType<GfpakAssetProvider>().FirstOrDefault();
                }
                if (any != null)
                {
                    archiveFieldRoot = Path.GetDirectoryName(any.ContainerPath);
                }
            }

            RebuildSceneAssetProvider();
            MessageHandler.Instance.AddMessage(MessageType.LOG, $"[GFPAK] Opened {opened} pack(s).");
        }

        private void clearGfpaks_Click(object? sender, EventArgs e)
        {
            DisposeExtraAssetProviders();
            MessageHandler.Instance.AddMessage(MessageType.LOG, "[GFPAK] Cleared registered pack(s).");
        }

        private IAssetProvider? PickGfpakProvider()
        {
            List<IAssetProvider> gfpaks;
            lock (assetProviderLock)
            {
                gfpaks = extraAssetProviders.OfType<GfpakAssetProvider>().Cast<IAssetProvider>().ToList();
            }
            if (gfpaks.Count == 0)
            {
                MessageHandler.Instance.AddMessage(MessageType.WARNING, "[GFPAK] No GFPAK is open. Use File -> Add GFPAK(s)... first.");
                return null;
            }

            if (gfpaks.Count == 1)
            {
                return gfpaks[0];
            }

            using var picker = new GfpakPickerForm(gfpaks);
            if (picker.ShowDialog(this) != DialogResult.OK)
            {
                return null;
            }

            return picker.SelectedProvider;
        }

        private static bool TryReadAllBytes(IAssetProvider provider, AssetEntry entry, out byte[] bytes, out string displayName)
        {
            bytes = Array.Empty<byte>();
            displayName = string.Empty;

            if (!string.IsNullOrWhiteSpace(entry.Path))
            {
                bytes = provider.ReadAllBytes(entry.Path);
                displayName = entry.Path;
                return true;
            }

            if (entry.PathHash != 0 && provider is GfpakAssetProvider gfpak)
            {
                bytes = gfpak.ReadAllBytes(entry.PathHash);
                displayName = $"0x{entry.PathHash:X16}";
                return true;
            }

            return false;
        }

        private bool TryAutoMountAreaModelPack(string areaName)
        {
            if (string.IsNullOrWhiteSpace(areaName))
            {
                return false;
            }

            string baseArea = areaName;
            int subIdx = baseArea.IndexOf("_s", StringComparison.OrdinalIgnoreCase);
            if (subIdx > 0)
            {
                baseArea = baseArea.Substring(0, subIdx);
            }

            if (extraAssetProviders.OfType<GfpakAssetProvider>().Any(p =>
                    string.Equals(Path.GetFileNameWithoutExtension(p.ContainerPath), baseArea, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            var fieldDir = archiveFieldRoot;
            if (string.IsNullOrWhiteSpace(fieldDir))
            {
                var anyGfpak = extraAssetProviders.OfType<GfpakAssetProvider>().FirstOrDefault();
                if (anyGfpak == null)
                {
                    return false;
                }
                fieldDir = Path.GetDirectoryName(anyGfpak.ContainerPath) ?? string.Empty;
            }
            var candidate = Path.Combine(fieldDir, "model", "pack", $"{baseArea}.gfpak");
            return TryMountGfpakIfPresent(candidate);
        }

        private bool TryMountPackForHash(ulong pathHash)
        {
            if (pathHash == 0)
            {
                return false;
            }

            string? packRel;
            lock (assetProviderLock)
            {
                gfpakHashToPackRelPath.TryGetValue(pathHash, out packRel);
            }

            if (string.IsNullOrWhiteSpace(packRel) || string.IsNullOrWhiteSpace(archiveFieldRoot))
            {
                return false;
            }

            var full = Path.Combine(archiveFieldRoot, packRel.Replace('/', Path.DirectorySeparatorChar));
            return TryMountGfpakIfPresent(full);
        }

        private void LoadGfpakHashIndex(IEnumerable<string> lines)
        {
            lock (assetProviderLock)
            {
                gfpakHashToPackRelPath.Clear();

                foreach (var raw in lines ?? Array.Empty<string>())
                {
                    if (string.IsNullOrWhiteSpace(raw))
                    {
                        continue;
                    }

                    var parts = raw.Split('\t');
                    if (parts.Length < 2)
                    {
                        continue;
                    }

                    var hashStr = parts[0].Trim();
                    if (!ulong.TryParse(hashStr, System.Globalization.NumberStyles.HexNumber, null, out var hash))
                    {
                        continue;
                    }

                    var owner = parts[1].Trim();
                    var rel = TryExtractPackPathRelativeToArchiveField(owner);
                    if (string.IsNullOrWhiteSpace(rel))
                    {
                        continue;
                    }

                    gfpakHashToPackRelPath[hash] = rel!;
                }
            }
        }

        private static string? TryExtractPackPathRelativeToArchiveField(string ownerColumn)
        {
            if (string.IsNullOrWhiteSpace(ownerColumn))
            {
                return null;
            }

            var s = ownerColumn.Trim();
            int gfpakIdx = s.IndexOf(".gfpak", StringComparison.OrdinalIgnoreCase);
            if (gfpakIdx < 0)
            {
                return null;
            }

            var upToPack = s.Substring(0, gfpakIdx + ".gfpak".Length);
            var norm = upToPack.Replace('/', '\\');
            var marker = "\\archive\\field\\";
            int m = norm.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (m < 0)
            {
                return Path.GetFileName(norm);
            }

            var rel = norm.Substring(m + marker.Length);
            return rel.Replace('\\', '/');
        }

        private void importGfpakHashList_Click(object? sender, EventArgs e)
        {
            using var ofd = new OpenFileDialog
            {
                Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*"
            };
            if (ofd.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            try
            {
                GFPakHashCache.Open();
                var lines = File.ReadAllLines(ofd.FileName).ToList();
                GFPakHashCache.AddHashFromList(lines);
                GFPakHashCache.Save();
                LoadGfpakHashIndex(lines);
                MessageHandler.Instance.AddMessage(MessageType.LOG, $"[GFPAK] Hash cache updated. ({GFPakHashCache.Count} entries)");
            }
            catch (Exception ex)
            {
                MessageHandler.Instance.AddMessage(MessageType.ERROR, $"[GFPAK] Failed to import hash list: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private sealed class DynamicSceneAssetProvider : IAssetProvider
        {
            private readonly SceneViewerForm owner;
            private readonly DiskAssetProvider disk = new();

            public DynamicSceneAssetProvider(SceneViewerForm owner)
            {
                this.owner = owner;
            }

            public string DisplayName => "DynamicSceneProvider";

            public bool Exists(string path)
            {
                foreach (var candidate in ExpandCandidates(path))
                {
                    if (TryExistsCandidate(candidate))
                    {
                        return true;
                    }
                }
                return false;
            }

            public Stream OpenRead(string path)
            {
                var bytes = ReadAllBytes(path);
                return new MemoryStream(bytes, writable: false);
            }

            public byte[] ReadAllBytes(string path)
            {
                foreach (var candidate in ExpandCandidates(path))
                {
                    if (TryReadCandidate(candidate, out var bytes))
                    {
                        return bytes;
                    }

                    // Try lazy-mount by hash, then retry once.
                    var hash = HashPath(candidate);
                    if (owner.TryMountPackForHash(hash) && TryReadCandidate(candidate, out bytes))
                    {
                        return bytes;
                    }
                }

                throw new FileNotFoundException($"Asset not found: '{path}'", path);
            }

            public IEnumerable<AssetEntry> EnumerateEntries()
            {
                // Not used by SceneViewer; keep as empty to avoid blowing up memory.
                yield break;
            }

            private bool TryExistsCandidate(string candidate)
            {
                AssetRootDiskAssetProvider? rootProvider;
                List<IAssetProvider> providers;
                lock (owner.assetProviderLock)
                {
                    rootProvider = owner.assetRootProvider;
                    providers = owner.extraAssetProviders.ToList();
                }

                if (rootProvider != null && rootProvider.Exists(candidate))
                {
                    return true;
                }

                foreach (var p in providers)
                {
                    if (p.Exists(candidate))
                    {
                        return true;
                    }
                }

                return disk.Exists(candidate);
            }

            private bool TryReadCandidate(string candidate, out byte[] bytes)
            {
                bytes = Array.Empty<byte>();

                AssetRootDiskAssetProvider? rootProvider;
                List<IAssetProvider> providers;
                lock (owner.assetProviderLock)
                {
                    rootProvider = owner.assetRootProvider;
                    providers = owner.extraAssetProviders.ToList();
                }

                if (rootProvider != null && rootProvider.Exists(candidate))
                {
                    bytes = rootProvider.ReadAllBytes(candidate);
                    return true;
                }

                foreach (var p in providers)
                {
                    if (p.Exists(candidate))
                    {
                        bytes = p.ReadAllBytes(candidate);
                        return true;
                    }
                }

                if (disk.Exists(candidate))
                {
                    bytes = disk.ReadAllBytes(candidate);
                    return true;
                }

                return false;
            }

            private static ulong HashPath(string path)
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    return 0;
                }

                var normalized = path.Trim().Replace('\\', '/');
                if (normalized.StartsWith("./", StringComparison.Ordinal))
                {
                    normalized = normalized.Substring(2);
                }

                return Trinity.Core.Math.Hash.GFFNV.Hash(normalized);
            }

            private static readonly string[] KnownRoots =
            {
                "bin",
                "field_graphic",
                "field",
                "light",
                "sound",
                "shader",
                "ui",
            };

            private static IEnumerable<string> ExpandCandidates(string path)
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    yield break;
                }

                var trimmed = path.Trim();
                yield return trimmed;

                var slash = trimmed.Replace('\\', '/');
                if (!string.Equals(trimmed, slash, StringComparison.Ordinal))
                {
                    yield return slash;
                }

                if (slash.StartsWith("./", StringComparison.Ordinal))
                {
                    yield return slash.Substring(2);
                }

                var lower = slash.ToLowerInvariant();
                foreach (var root in KnownRoots)
                {
                    string token = "/" + root.ToLowerInvariant() + "/";
                    int idx = lower.IndexOf(token, StringComparison.Ordinal);
                    if (idx >= 0)
                    {
                        yield return slash.Substring(idx + 1);
                    }
                }
            }

            public void Dispose()
            {
            }
        }
    }
}
