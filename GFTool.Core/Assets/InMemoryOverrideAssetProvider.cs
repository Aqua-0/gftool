using System;
using System.Collections.Generic;
using System.IO;

namespace Trinity.Core.Assets
{
    /// <summary>
    /// Wraps an existing <see cref="IAssetProvider"/> and allows overriding specific paths with in-memory bytes.
    /// Intended for workflows like JSON editing where the runtime should read edited FlatBuffer binaries without
    /// requiring temporary files on disk.
    /// </summary>
    public sealed class InMemoryOverrideAssetProvider : IAssetProvider
    {
        private readonly IAssetProvider inner;
        private readonly Dictionary<string, byte[]> overrides = new(StringComparer.OrdinalIgnoreCase);

        public InMemoryOverrideAssetProvider(IAssetProvider inner)
        {
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public IAssetProvider Inner => inner;

        public string DisplayName => $"InMemoryOverride({inner.DisplayName})";

        private static string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            var normalized = path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            try
            {
                return Path.GetFullPath(normalized);
            }
            catch
            {
                return normalized;
            }
        }

        public void SetOverride(string path, byte[] bytes)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Missing path.", nameof(path));
            overrides[Normalize(path)] = bytes ?? Array.Empty<byte>();
        }

        public bool RemoveOverride(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            return overrides.Remove(Normalize(path));
        }

        public bool Exists(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            return overrides.ContainsKey(Normalize(path)) || inner.Exists(path);
        }

        public Stream OpenRead(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Missing path.", nameof(path));
            }

            if (overrides.TryGetValue(Normalize(path), out var bytes))
            {
                return new MemoryStream(bytes, writable: false);
            }

            return inner.OpenRead(path);
        }

        public byte[] ReadAllBytes(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return Array.Empty<byte>();
            }

            if (overrides.TryGetValue(Normalize(path), out var bytes))
            {
                return bytes;
            }

            return inner.ReadAllBytes(path);
        }

        public IEnumerable<AssetEntry> EnumerateEntries() => inner.EnumerateEntries();

        public void Dispose()
        {
            try { inner.Dispose(); } catch { }
            overrides.Clear();
        }
    }
}
