using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Trinity.Core.Assets
{
    /// <summary>
    /// Wraps an <see cref="IAssetProvider"/> and attempts to resolve authoring/absolute paths
    /// by stripping to a known content root segment (ex: "bin/", "field_graphic/", etc).
    /// This helps when assets inside GFPAKs refer to Windows absolute paths.
    /// </summary>
    public sealed class RerootingAssetProvider : IAssetProvider
    {
        private static readonly string[] KnownRoots =
        {
            "bin",
            "ai_influence",
            "avalon",
            "field_graphic",
            "field",
            "ik_ai_behavior",
            "ik_chara",
            "ik_demo",
            "ik_effect",
            "ik_event",
            "ik_message",
            "ik_pokemon",
            "light",
            "param_ai",
            "pokemon",
            "resident",
            "shader",
            "sound",
            "ui",
        };

        private readonly IAssetProvider inner;

        public RerootingAssetProvider(IAssetProvider inner)
        {
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public IAssetProvider Inner => inner;

        public string DisplayName => $"Reroot({inner.DisplayName})";

        public bool Exists(string path)
        {
            foreach (var candidate in ExpandCandidates(path))
            {
                if (inner.Exists(candidate))
                {
                    return true;
                }
            }
            return false;
        }

        public Stream OpenRead(string path)
        {
            foreach (var candidate in ExpandCandidates(path))
            {
                if (inner.Exists(candidate))
                {
                    return inner.OpenRead(candidate);
                }
            }

            throw new FileNotFoundException($"Asset not found (rerooted): '{path}'", path);
        }

        public byte[] ReadAllBytes(string path)
        {
            foreach (var candidate in ExpandCandidates(path))
            {
                if (inner.Exists(candidate))
                {
                    return inner.ReadAllBytes(candidate);
                }
            }

            throw new FileNotFoundException($"Asset not found (rerooted): '{path}'", path);
        }

        public IEnumerable<AssetEntry> EnumerateEntries() => inner.EnumerateEntries();

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

            // Collapse "foo/../bar" style segments best-effort (GFPAK paths are generally normalized).
            var collapsed = CollapseDotDot(slash);
            if (!string.Equals(collapsed, slash, StringComparison.Ordinal))
            {
                yield return collapsed;
            }

            // Strip to a known root segment.
            var lower = slash.ToLowerInvariant();
            foreach (var root in KnownRoots)
            {
                string token = "/" + root.ToLowerInvariant() + "/";
                int idx = lower.IndexOf(token, StringComparison.Ordinal);
                if (idx < 0)
                {
                    // Also allow start-of-string root.
                    token = root.ToLowerInvariant() + "/";
                    if (lower.StartsWith(token, StringComparison.Ordinal))
                    {
                        yield return slash;
                    }
                    continue;
                }

                // Strip leading slash.
                var sub = slash.Substring(idx + 1);
                yield return sub;
            }
        }

        private static string CollapseDotDot(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            var parts = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            var stack = new Stack<string>(parts.Length);
            foreach (var p in parts)
            {
                if (p == ".")
                {
                    continue;
                }

                if (p == "..")
                {
                    if (stack.Count > 0)
                    {
                        stack.Pop();
                    }
                    continue;
                }

                stack.Push(p);
            }

            return string.Join("/", stack.Reverse());
        }

        public void Dispose()
        {
            // Inner provider lifetime is owned by the caller.
        }
    }
}
