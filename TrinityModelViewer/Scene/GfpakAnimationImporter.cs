using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlatSharp;
using GfAnim = Trinity.Core.Flatbuffers.GF.Animation;
using TrAnim = Trinity.Core.Flatbuffers.TR.Animation;
using Trinity.Core.Assets;
using Trinity.Core.Utils;

namespace TrinityModelViewer.Scene
{
    internal static class GfpakAnimationImporter
    {
        internal sealed record LoadedAnimation(string Key, GFTool.Renderer.Scene.GraphicsObjects.Animation Animation);

        internal static Task<IReadOnlyList<LoadedAnimation>> LoadAllAnimationsAsync(
            string gfpakPath,
            IReadOnlyCollection<string> alreadyLoadedKeys,
            CancellationToken token,
            Action<string>? debugLog)
        {
            return Task.Run(() => LoadAllAnimationsCore(gfpakPath, alreadyLoadedKeys, token, debugLog), token);
        }

        private static IReadOnlyList<LoadedAnimation> LoadAllAnimationsCore(
            string gfpakPath,
            IReadOnlyCollection<string> alreadyLoadedKeys,
            CancellationToken token,
            Action<string>? debugLog)
        {
            if (string.IsNullOrWhiteSpace(gfpakPath))
            {
                return Array.Empty<LoadedAnimation>();
            }

            using var provider = new GfpakAssetProvider(gfpakPath);

            var loaded = new List<LoadedAnimation>();
            var existing = alreadyLoadedKeys is HashSet<string> hs
                ? hs
                : new HashSet<string>(alreadyLoadedKeys ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

            var entries = provider.EnumerateEntries().ToList();
            int scanned = 0;
            int candidates = 0;
            int rejected = 0;

            foreach (var entry in entries)
            {
                token.ThrowIfCancellationRequested();
                scanned++;

                var path = entry.Path;
                if (!string.IsNullOrWhiteSpace(path))
                {
                    var ext = Path.GetExtension(path);
                    if (!string.Equals(ext, ".tranm", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(ext, ".gfbanm", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    candidates++;
                    string key = $"{gfpakPath}::{path}";
                    if (existing.Contains(key))
                    {
                        continue;
                    }

                    var bytes = provider.ReadAllBytes(path);
                    if (!TryParseAnimation(bytes, ext, out var animFile))
                    {
                        rejected++;
                        continue;
                    }
                    var name = Path.GetFileNameWithoutExtension(path);
                    var anim = new GFTool.Renderer.Scene.GraphicsObjects.Animation(animFile, name, path);
                    loaded.Add(new LoadedAnimation(key, anim));
                    continue;
                }

                // No path name (no GFPAKHashCache) - try to detect animations by parsing.
                string hashKey = $"{gfpakPath}::0x{entry.PathHash:X16}";
                if (existing.Contains(hashKey))
                {
                    continue;
                }

                try
                {
                    var bytes = provider.ReadAllBytes(entry.PathHash);
                    candidates++;
                    // No extension hint here, so we avoid brute-force parsing (too many false positives).
                    // If GFPAKHashCache.bin isn't loaded, you won't get nice names anyway.
                    if (!TryParseAnimation(bytes, extHint: null, out var animFile))
                    {
                        rejected++;
                        continue;
                    }

                    var name = $"0x{entry.PathHash:X16}";
                    var anim = new GFTool.Renderer.Scene.GraphicsObjects.Animation(animFile, name, $"gfpak://{provider.DisplayName}/{name}");
                    loaded.Add(new LoadedAnimation(hashKey, anim));
                }
                catch
                {
                    // Not an animation (or can't parse) - move on.
                }

                if (debugLog != null && scanned % 500 == 0)
                {
                    debugLog($"[Anim] GFPAK scan: scanned {scanned}/{entries.Count} entries, found {loaded.Count} anims...");
                }
            }

            debugLog?.Invoke($"[Anim] GFPAK scan done: scanned {scanned} entries, candidates={candidates}, rejected={rejected}, loaded={loaded.Count}");
            return loaded;
        }

        private static bool TryParseAnimation(byte[] bytes, string? extHint, out GfAnim.Animation anim)
        {
            anim = null!;

            // flatbuffers can parse a lot of random stuff without throwing, so be picky.
            if (bytes == null || bytes.Length < 64)
            {
                return false;
            }

            try
            {
                if (string.Equals(extHint, ".tranm", StringComparison.OrdinalIgnoreCase))
                {
                    var tranm = FlatBufferConverter.DeserializeFrom<TrAnim.TRANM>(bytes);
                    if (!LooksLikeTranm(tranm))
                    {
                        return false;
                    }

                    anim = TranmToGfAnimation.Convert(tranm);
                    return LooksLikeAnimation(anim);
                }

                if (string.Equals(extHint, ".gfbanm", StringComparison.OrdinalIgnoreCase))
                {
                    anim = FlatBufferConverter.DeserializeFrom<GfAnim.Animation>(bytes);
                    return LooksLikeAnimation(anim);
                }

                // Without a hint, don't try TR/GF guessing - it tends to accept tons of garbage.
                return false;
            }
            catch
            {
                return false;
            }
        }

        private static bool LooksLikeTranm(TrAnim.TRANM tranm)
        {
            if (tranm == null || tranm.Info == null || tranm.SkeletalAnimation == null || tranm.SkeletalAnimation.Tracks == null)
            {
                return false;
            }

            uint frameCount = tranm.Info.FrameCount;
            uint frameRate = tranm.Info.FrameRate;
            int trackCount = tranm.SkeletalAnimation.Tracks.Count;

            if (frameCount == 0 || frameCount > 10_000)
            {
                return false;
            }

            if (frameRate == 0 || frameRate > 240)
            {
                return false;
            }

            if (trackCount < 6 || trackCount > 2048)
            {
                return false;
            }

            int validNameCount = 0;
            foreach (var t in tranm.SkeletalAnimation.Tracks)
            {
                if (t == null)
                {
                    continue;
                }

                if (LooksLikeBoneName(t.BoneName))
                {
                    validNameCount++;
                }
            }

            return validNameCount >= Math.Max(1, trackCount / 3);
        }

        private static bool LooksLikeAnimation(GfAnim.Animation anim)
        {
            if (anim == null || anim.Info == null || anim.Skeleton == null || anim.Skeleton.Tracks == null)
            {
                return false;
            }

            uint keyFrames = anim.Info.KeyFrames;
            uint frameRate = anim.Info.FrameRate;
            int trackCount = anim.Skeleton.Tracks.Count;

            if (keyFrames == 0 || keyFrames > 10_000)
            {
                return false;
            }

            if (frameRate == 0 || frameRate > 240)
            {
                return false;
            }

            // If we let tiny track counts through, we end up "loading" a bunch of garbage.
            if (trackCount < 6 || trackCount > 2048)
            {
                return false;
            }

            int validNameCount = 0;
            int emptyNameCount = 0;
            int nonEmptyTrackCount = 0;
            int variableKeyTrackCount = 0;
            var uniqueNames = new HashSet<string>(StringComparer.Ordinal);

            foreach (var t in anim.Skeleton.Tracks)
            {
                if (t == null)
                {
                    continue;
                }

                string name = t.Name ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name))
                {
                    emptyNameCount++;
                }
                else
                {
                    uniqueNames.Add(name);
                    if (LooksLikeBoneName(name))
                    {
                        validNameCount++;
                    }
                }

                int s = GetVectorKeyCount(t.Scale);
                int r = GetRotationKeyCount(t.Rotate);
                int p = GetVectorKeyCount(t.Translate);

                if (t.Scale.Discriminator != 0 || t.Rotate.Discriminator != 0 || t.Translate.Discriminator != 0)
                {
                    nonEmptyTrackCount++;
                }

                if (s > 1 || r > 1 || p > 1)
                {
                    variableKeyTrackCount++;
                }
            }

            // If basically everything is unnamed, it's almost certainly a bad parse.
            if (emptyNameCount > trackCount / 2)
            {
                return false;
            }

            // Most real animations have mostly "identifier-y" bone names.
            if (validNameCount < Math.Max(1, trackCount / 3))
            {
                return false;
            }

            // If names are mostly duplicates, something's off.
            if (uniqueNames.Count < Math.Max(1, trackCount / 2))
            {
                return false;
            }

            // If the union discriminators are mostly empty, it's probably not an anim.
            if (nonEmptyTrackCount < Math.Max(1, trackCount / 3))
            {
                return false;
            }

            // At least a few tracks should actually change over time.
            if (variableKeyTrackCount == 0)
            {
                return false;
            }

            return true;
        }

        private static bool LooksLikeBoneName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > 80)
            {
                return false;
            }

            // Keep it loose: games aren't consistent, but random buffers usually produce junk.
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (char.IsLetterOrDigit(c))
                {
                    continue;
                }

                if (c is '_' or '-' or '.' or ':' or '/')
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        private static int GetVectorKeyCount(FlatBufferUnion<GfAnim.FixedVectorTrack, GfAnim.DynamicVectorTrack, GfAnim.Framed16VectorTrack, GfAnim.Framed8VectorTrack> track)
        {
            int count = 0;
            track.Switch(
                defaultCase: () => count = 0,
                case1: v => count = 1,
                case2: v => count = v?.Co?.Count ?? 0,
                case3: v => count = v?.Co?.Count ?? 0,
                case4: v => count = v?.Co?.Count ?? 0
            );
            return count;
        }

        private static int GetRotationKeyCount(FlatBufferUnion<GfAnim.FixedRotationTrack, GfAnim.DynamicRotationTrack, GfAnim.Framed16RotationTrack, GfAnim.Framed8RotationTrack> track)
        {
            int count = 0;
            track.Switch(
                defaultCase: () => count = 0,
                case1: v => count = 1,
                case2: v => count = v?.Co?.Count ?? 0,
                case3: v => count = v?.Co?.Count ?? 0,
                case4: v => count = v?.Co?.Count ?? 0
            );
            return count;
        }
    }
}
