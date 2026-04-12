using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Trinity.Core.Assets;
using Trinity.Core.Flatbuffers.TR.Model;
using Trinity.Core.Flatbuffers.Utils;
using Trinity.Core.Utils;

namespace TrinityModelViewer.Export
{
    internal static class GfpakModelSetExtractor
    {
        public static void ExportCopy(
            string gfpakPath,
            string trmdlEntryPath,
            string outputTrmdlPath,
            bool includeProtagBaseSkeleton = true)
        {
            if (string.IsNullOrWhiteSpace(gfpakPath)) throw new ArgumentException("Missing GFPAK path.", nameof(gfpakPath));
            if (string.IsNullOrWhiteSpace(trmdlEntryPath)) throw new ArgumentException("Missing TRMDL entry path.", nameof(trmdlEntryPath));
            if (string.IsNullOrWhiteSpace(outputTrmdlPath)) throw new ArgumentException("Missing output TRMDL path.", nameof(outputTrmdlPath));

            using var provider = new GfpakAssetProvider(gfpakPath);
            ExportCopy(provider, trmdlEntryPath, outputTrmdlPath, includeProtagBaseSkeleton);
        }

        public static void ExportCopy(
            GfpakAssetProvider provider,
            string trmdlEntryPath,
            string outputTrmdlPath,
            bool includeProtagBaseSkeleton = true)
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            if (string.IsNullOrWhiteSpace(trmdlEntryPath)) throw new ArgumentException("Missing TRMDL entry path.", nameof(trmdlEntryPath));
            if (string.IsNullOrWhiteSpace(outputTrmdlPath)) throw new ArgumentException("Missing output TRMDL path.", nameof(outputTrmdlPath));

            var outputDir = Path.GetDirectoryName(outputTrmdlPath) ?? Environment.CurrentDirectory;
            Directory.CreateDirectory(outputDir);

            string trmdlPathInPack = NormalizePackPath(trmdlEntryPath);
            var trmdlDirInPack = GetPackDirectory(trmdlPathInPack);

            var trmdlBytes = provider.ReadAllBytes(trmdlPathInPack);
            var trmdl = FlatBufferConverter.DeserializeFrom<TRMDL>(trmdlBytes);
            if (trmdl == null)
            {
                throw new InvalidOperationException($"Failed to deserialize TRMDL from GFPAK entry '{trmdlEntryPath}'.");
            }

            var relPaths = CollectModelDependencyPaths(provider, trmdl, trmdlDirInPack);

            // Write the TRMDL to the selected output filename (do not rewrite internal paths).
            WriteFile(outputTrmdlPath, trmdlBytes);

            // Write referenced dependencies using the TRMDL-provided relative paths.
            foreach (var rel in relPaths)
            {
                var inPack = ResolveInPackPath(trmdlDirInPack, rel);
                if (!TryRead(provider, inPack, out var bytes))
                {
                    continue;
                }

                var dst = Path.Combine(outputDir, PackPathToOsRelativePath(rel));
                WriteFile(dst, bytes);
            }

            if (includeProtagBaseSkeleton)
            {
                TryExtractLaProtagBaseSkeleton(provider, trmdl, trmdlDirInPack, outputDir);
            }
        }

        public static void ExportReserializeCopy(
            string gfpakPath,
            string trmdlEntryPath,
            string outputTrmdlPath,
            GFTool.Renderer.Scene.GraphicsObjects.Model? model = null,
            bool includeProtagBaseSkeleton = true)
        {
            if (string.IsNullOrWhiteSpace(gfpakPath)) throw new ArgumentException("Missing GFPAK path.", nameof(gfpakPath));
            if (string.IsNullOrWhiteSpace(trmdlEntryPath)) throw new ArgumentException("Missing TRMDL entry path.", nameof(trmdlEntryPath));
            if (string.IsNullOrWhiteSpace(outputTrmdlPath)) throw new ArgumentException("Missing output TRMDL path.", nameof(outputTrmdlPath));

            using var provider = new GfpakAssetProvider(gfpakPath);
            ExportReserializeCopy(provider, trmdlEntryPath, outputTrmdlPath, model, includeProtagBaseSkeleton);
        }

        public static void ExportReserializeCopy(
            GfpakAssetProvider provider,
            string trmdlEntryPath,
            string outputTrmdlPath,
            GFTool.Renderer.Scene.GraphicsObjects.Model? model = null,
            bool includeProtagBaseSkeleton = true)
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            if (string.IsNullOrWhiteSpace(trmdlEntryPath)) throw new ArgumentException("Missing TRMDL entry path.", nameof(trmdlEntryPath));
            if (string.IsNullOrWhiteSpace(outputTrmdlPath)) throw new ArgumentException("Missing output TRMDL path.", nameof(outputTrmdlPath));

            var outputDir = Path.GetDirectoryName(outputTrmdlPath) ?? Environment.CurrentDirectory;
            Directory.CreateDirectory(outputDir);

            string trmdlPathInPack = NormalizePackPath(trmdlEntryPath);
            var trmdlDirInPack = GetPackDirectory(trmdlPathInPack);

            var trmdlBytes = provider.ReadAllBytes(trmdlPathInPack);
            var trmdl = FlatBufferConverter.DeserializeFrom<TRMDL>(trmdlBytes);
            if (trmdl == null)
            {
                throw new InvalidOperationException($"Failed to deserialize TRMDL from GFPAK entry '{trmdlEntryPath}'.");
            }

            var relPaths = CollectModelDependencyPaths(provider, trmdl, trmdlDirInPack);

            var trmdlReserialized = FlatBufferConverter.SerializeFrom(trmdl);
            WriteFile(outputTrmdlPath, trmdlReserialized);

            bool hasMaterialEdits = model != null &&
                (model.GetMaterials().Any(m => m.HasUniformOverrides) ||
                 model.GetMaterials().Any(m => m.HasSamplerOverrides) ||
                 model.HasMaterialSourceEdits);

            foreach (var rel in relPaths)
            {
                var inPack = ResolveInPackPath(trmdlDirInPack, rel);
                if (!TryRead(provider, inPack, out var bytes))
                {
                    continue;
                }

                var dst = Path.Combine(outputDir, PackPathToOsRelativePath(rel));
                var ext = Path.GetExtension(rel);
                var outBytes = ReserializeBytes(ext, bytes, model, hasMaterialEdits);
                WriteFile(dst, outBytes);
            }

            if (includeProtagBaseSkeleton)
            {
                TryExtractLaProtagBaseSkeleton(provider, trmdl, trmdlDirInPack, outputDir);
            }
        }

        public static string ExtractModelSetToDirectory(
            string gfpakPath,
            string trmdlEntryPath,
            string outputRoot,
            bool includeProtagBaseSkeleton = true)
        {
            if (string.IsNullOrWhiteSpace(gfpakPath)) throw new ArgumentException("Missing GFPAK path.", nameof(gfpakPath));
            if (string.IsNullOrWhiteSpace(trmdlEntryPath)) throw new ArgumentException("Missing TRMDL entry path.", nameof(trmdlEntryPath));
            if (string.IsNullOrWhiteSpace(outputRoot)) throw new ArgumentException("Missing output root.", nameof(outputRoot));

            Directory.CreateDirectory(outputRoot);
            using var provider = new GfpakAssetProvider(gfpakPath);
            return ExtractModelSetToDirectory(provider, trmdlEntryPath, outputRoot, includeProtagBaseSkeleton);
        }

        public static string ExtractModelSetToDirectory(
            GfpakAssetProvider provider,
            string trmdlEntryPath,
            string outputRoot,
            bool includeProtagBaseSkeleton = true)
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            if (string.IsNullOrWhiteSpace(trmdlEntryPath)) throw new ArgumentException("Missing TRMDL entry path.", nameof(trmdlEntryPath));
            if (string.IsNullOrWhiteSpace(outputRoot)) throw new ArgumentException("Missing output root.", nameof(outputRoot));

            Directory.CreateDirectory(outputRoot);

            string trmdlPathInPack = NormalizePackPath(trmdlEntryPath);
            var trmdlDirInPack = GetPackDirectory(trmdlPathInPack);

            var trmdlBytes = provider.ReadAllBytes(trmdlPathInPack);
            var trmdl = FlatBufferConverter.DeserializeFrom<TRMDL>(trmdlBytes);
            if (trmdl == null)
            {
                throw new InvalidOperationException($"Failed to deserialize TRMDL from GFPAK entry '{trmdlEntryPath}'.");
            }

            var relPaths = CollectModelDependencyPaths(provider, trmdl, trmdlDirInPack);

            // Always write the TRMDL first (keeps the output path stable for callers).
            var outTrmdl = Path.Combine(outputRoot, PackPathToOsRelativePath(trmdlPathInPack));
            WriteFile(outTrmdl, trmdlBytes);

            // Write dependencies.
            foreach (var rel in relPaths)
            {
                var inPack = ResolveInPackPath(trmdlDirInPack, rel);
                if (!TryRead(provider, inPack, out var bytes))
                {
                    continue;
                }

                var dst = Path.Combine(outputRoot, PackPathToOsRelativePath(inPack));
                WriteFile(dst, bytes);
            }

            if (includeProtagBaseSkeleton)
            {
                TryExtractLaProtagBaseSkeleton(provider, trmdl, trmdlDirInPack, outputRoot);
            }

            return outTrmdl;
        }

        private static HashSet<string> CollectModelDependencyPaths(GfpakAssetProvider provider, TRMDL trmdl, string trmdlDirInPack)
        {
            var relPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void AddRel(string? rel)
            {
                if (string.IsNullOrWhiteSpace(rel))
                {
                    return;
                }

                relPaths.Add(NormalizePackPath(rel));
            }

            if (trmdl.Meshes != null)
            {
                foreach (var mesh in trmdl.Meshes)
                {
                    AddRel(mesh?.PathName);
                }
            }

            AddRel(trmdl.Skeleton?.PathName);

            if (trmdl.Materials != null)
            {
                foreach (var mat in trmdl.Materials)
                {
                    AddRel(mat);
                }
            }

            // Copy TRMBF dependencies referenced from TRMSH.
            foreach (var meshPath in relPaths.Where(p => p.EndsWith(".trmsh", StringComparison.OrdinalIgnoreCase)).ToList())
            {
                var meshInPack = ResolveInPackPath(trmdlDirInPack, meshPath);
                if (!TryRead(provider, meshInPack, out var trmshBytes))
                {
                    continue;
                }

                try
                {
                    var trmsh = FlatBufferConverter.DeserializeFrom<TRMSH>(trmshBytes);
                    AddRel(trmsh?.bufferFilePath);
                }
                catch
                {
                    // ignore, we'll still extract whatever else we can
                }
            }

            return relPaths;
        }

        private static void TryExtractLaProtagBaseSkeleton(GfpakAssetProvider localProvider, TRMDL trmdl, string trmdlDirInPack, string outputRoot)
        {
            // For LA player clothing, the base skeleton lives in a sibling GFPAK under ../base.
            // Not referenced directly from the TRMDL, but needed for merged skeleton workflows.
            var localSkelRel = trmdl?.Skeleton?.PathName;
            if (string.IsNullOrWhiteSpace(localSkelRel))
            {
                return;
            }

            string localSkelInPack = ResolveInPackPath(trmdlDirInPack, NormalizePackPath(localSkelRel));
            var localSkelFile = Path.GetFileName(localSkelInPack);
            if (!TryGetProtagPrefix(localSkelFile, out var prefix))
            {
                return;
            }

            string? localPackDir = Path.GetDirectoryName(localProvider.ContainerPath);
            if (string.IsNullOrWhiteSpace(localPackDir))
            {
                return;
            }

            string basePackPath = Path.GetFullPath(Path.Combine(localPackDir, "..", "base", $"{prefix}_base0001_00_default.gfpak"));
            if (!File.Exists(basePackPath))
            {
                return;
            }

            try
            {
                using var baseProvider = new GfpakAssetProvider(basePackPath);
                var baseTrskl = FindNamedEntry(baseProvider, $"{prefix}_base0001_00_default.trskl");
                if (string.IsNullOrWhiteSpace(baseTrskl))
                {
                    return;
                }

                var bytes = baseProvider.ReadAllBytes(baseTrskl);
                var dst = Path.Combine(outputRoot, PackPathToOsRelativePath(NormalizePackPath(baseTrskl)));
                WriteFile(dst, bytes);
            }
            catch
            {
                // If base is missing/oodle/etc, just skip; extracting the local set still works.
            }
        }

        private static byte[] ReserializeBytes(string ext, byte[] bytes, GFTool.Renderer.Scene.GraphicsObjects.Model? model, bool hasMaterialEdits)
        {
            if (string.Equals(ext, ".trmsh", StringComparison.OrdinalIgnoreCase))
            {
                var v = FlatBufferConverter.DeserializeFrom<TRMSH>(bytes);
                return FlatBufferConverter.SerializeFrom(v);
            }

            if (string.Equals(ext, ".trmbf", StringComparison.OrdinalIgnoreCase))
            {
                var v = FlatBufferConverter.DeserializeFrom<TRMBF>(bytes);
                return FlatBufferConverter.SerializeFrom(v);
            }

            if (string.Equals(ext, ".trskl", StringComparison.OrdinalIgnoreCase))
            {
                var v = FlatBufferConverter.DeserializeFrom<TRSKL>(bytes);
                return FlatBufferConverter.SerializeFrom(v);
            }

            if (string.Equals(ext, ".trmtr", StringComparison.OrdinalIgnoreCase))
            {
                var v = FlatBufferConverter.DeserializeFrom<TrmtrFile>(bytes);
                return hasMaterialEdits && model != null
                    ? TrmtrReserializePatcher.BuildEditedTrmtrBytes(v, model)
                    : FlatBufferConverter.SerializeFrom(v);
            }

            return bytes;
        }

        private static string? FindNamedEntry(GfpakAssetProvider provider, string fileName)
        {
            foreach (var entry in provider.EnumerateEntries())
            {
                var path = entry.Path;
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                if (string.Equals(Path.GetFileName(path), fileName, StringComparison.OrdinalIgnoreCase))
                {
                    return NormalizePackPath(path);
                }
            }

            return null;
        }

        private static bool TryRead(GfpakAssetProvider provider, string pathInPack, out byte[] bytes)
        {
            bytes = Array.Empty<byte>();
            try
            {
                bytes = provider.ReadAllBytes(pathInPack);
                return bytes.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        private static void WriteFile(string outputPath, byte[] bytes)
        {
            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllBytes(outputPath, bytes);
        }

        private static string ResolveInPackPath(string baseDirInPack, string relOrAbsInPack)
        {
            relOrAbsInPack = NormalizePackPath(relOrAbsInPack);
            if (string.IsNullOrWhiteSpace(relOrAbsInPack))
            {
                return relOrAbsInPack;
            }

            // Some TRMDLs store absolute in-pack paths (bin/...); others are relative.
            if (LooksPackAbsolute(relOrAbsInPack))
            {
                return relOrAbsInPack;
            }

            if (string.IsNullOrWhiteSpace(baseDirInPack))
            {
                return relOrAbsInPack;
            }

            return NormalizePackPath($"{baseDirInPack}/{relOrAbsInPack}");
        }

        private static bool LooksPackAbsolute(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            path = NormalizePackPath(path);
            return path.StartsWith("bin/", StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith("rom:", StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith("/", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizePackPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            var p = path.Trim().Replace('\\', '/');
            while (p.StartsWith("./", StringComparison.Ordinal))
            {
                p = p.Substring(2);
            }
            while (p.StartsWith("/", StringComparison.Ordinal))
            {
                p = p.Substring(1);
            }
            return p;
        }

        private static string GetPackDirectory(string packPath)
        {
            packPath = NormalizePackPath(packPath);
            int lastSlash = packPath.LastIndexOf('/');
            return lastSlash >= 0 ? packPath.Substring(0, lastSlash) : string.Empty;
        }

        private static string PackPathToOsRelativePath(string packPath)
        {
            packPath = NormalizePackPath(packPath);
            return packPath.Replace('/', Path.DirectorySeparatorChar);
        }

        private static bool TryGetProtagPrefix(string fileName, out string prefix)
        {
            prefix = string.Empty;
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return false;
            }

            if (fileName.StartsWith("p1", StringComparison.OrdinalIgnoreCase))
            {
                prefix = "p1";
                return true;
            }

            if (fileName.StartsWith("p2", StringComparison.OrdinalIgnoreCase))
            {
                prefix = "p2";
                return true;
            }

            return false;
        }
    }
}
