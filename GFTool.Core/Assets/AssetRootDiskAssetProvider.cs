using System;
using System.Collections.Generic;
using System.IO;

namespace Trinity.Core.Assets
{
    /// <summary>
    /// Serves assets from an extracted game content root. Non-rooted paths are resolved
    /// relative to <see cref="AssetRoot"/> (after normalizing separators).
    /// </summary>
    public sealed class AssetRootDiskAssetProvider : IAssetProvider
    {
        public string AssetRoot { get; }

        public string DisplayName => $"AssetRoot({AssetRoot})";

        public AssetRootDiskAssetProvider(string assetRoot)
        {
            if (string.IsNullOrWhiteSpace(assetRoot))
            {
                throw new ArgumentException("Missing asset root.", nameof(assetRoot));
            }

            AssetRoot = Path.GetFullPath(assetRoot);
        }

        private string ResolvePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            var trimmed = path.Trim();
            if (Path.IsPathRooted(trimmed))
            {
                return trimmed;
            }

            // Prefer forward-slash semantics for game paths, but resolve on the current OS.
            var normalized = trimmed.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            if (normalized.StartsWith("." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                normalized = normalized.Substring(2);
            }

            return Path.Combine(AssetRoot, normalized.TrimStart(Path.DirectorySeparatorChar));
        }

        public bool Exists(string path) => File.Exists(ResolvePath(path));

        public Stream OpenRead(string path) => File.OpenRead(ResolvePath(path));

        public byte[] ReadAllBytes(string path) => File.ReadAllBytes(ResolvePath(path));

        public IEnumerable<AssetEntry> EnumerateEntries()
        {
            yield break;
        }

        public void Dispose()
        {
        }
    }
}
