using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Trinity.Core.Assets
{
    /// <summary>
    /// Tries multiple providers in order, returning the first match.
    /// Useful when assets are split across extracted roots and GFPAKs.
    /// </summary>
    public sealed class ChainedAssetProvider : IAssetProvider
    {
        private readonly IAssetProvider[] providers;

        public ChainedAssetProvider(IEnumerable<IAssetProvider> providers)
        {
            if (providers == null) throw new ArgumentNullException(nameof(providers));
            this.providers = providers.Where(p => p != null).ToArray()!;
            if (this.providers.Length == 0)
            {
                throw new ArgumentException("At least one provider is required.", nameof(providers));
            }
        }

        public IReadOnlyList<IAssetProvider> Providers => providers;

        public string DisplayName => $"Chained({string.Join(", ", providers.Select(p => p.DisplayName))})";

        public bool Exists(string path) => providers.Any(p => p.Exists(path));

        public Stream OpenRead(string path)
        {
            foreach (var p in providers)
            {
                if (!p.Exists(path))
                {
                    continue;
                }

                return p.OpenRead(path);
            }

            throw new FileNotFoundException($"Asset not found in chained providers: '{path}'", path);
        }

        public byte[] ReadAllBytes(string path)
        {
            foreach (var p in providers)
            {
                if (!p.Exists(path))
                {
                    continue;
                }

                return p.ReadAllBytes(path);
            }

            throw new FileNotFoundException($"Asset not found in chained providers: '{path}'", path);
        }

        public IEnumerable<AssetEntry> EnumerateEntries()
        {
            foreach (var p in providers)
            {
                foreach (var e in p.EnumerateEntries())
                {
                    yield return e;
                }
            }
        }

        public void Dispose()
        {
            // Providers are owned/managed by the caller.
        }
    }
}
