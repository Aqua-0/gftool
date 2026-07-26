using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using GFTool.Renderer.Core;
using OpenTK.Mathematics;

namespace TrinitySceneView
{
    public static class SceneDiskCache
    {
        public const string DefaultCacheDirectoryName = ".trinity_scene_cache";
        public const string MarkerFileName = ".trinity_scene_cache_marker";
        public const string PreparedModelCacheExtension = ".prepared-model.bin";
        public const string MergedBatchCacheExtension = ".merged-model.bin";
        public const int CacheFormatVersion = 5;

        public static string GetCacheRoot(string? configuredDirectory)
        {
            if (!string.IsNullOrWhiteSpace(configuredDirectory))
            {
                return configuredDirectory;
            }

            return Path.Combine(AppContext.BaseDirectory, DefaultCacheDirectoryName);
        }

        public static void Clear(string? configuredDirectory)
        {
            var root = GetCacheRoot(configuredDirectory);
            if (!Directory.Exists(root))
            {
                return;
            }

            if (!IsSafeCacheRoot(root))
            {
                MessageHandler.Instance.AddMessage(MessageType.WARNING, $"[SceneCache] Refusing to clear unmarked cache directory: {root}");
                return;
            }

            Directory.Delete(root, recursive: true);
            MessageHandler.Instance.AddMessage(MessageType.LOG, $"[SceneCache] Cleared {root}");
        }

        public static bool TryReadText(string? configuredDirectory, string cacheKey, string extension, out string text)
        {
            text = string.Empty;
            var root = GetCacheRoot(configuredDirectory);
            if (!IsSafeCacheRoot(root))
            {
                return false;
            }

            var path = GetCacheFilePath(root, cacheKey, extension);
            if (!File.Exists(path))
            {
                return false;
            }

            text = File.ReadAllText(path);
            return true;
        }

        public static void WriteText(string? configuredDirectory, string cacheKey, string extension, string text)
        {
            var path = GetWritableCacheFilePath(configuredDirectory, cacheKey, extension);
            var tempPath = path + ".tmp";
            File.WriteAllText(tempPath, text ?? string.Empty);
            File.Move(tempPath, path, overwrite: true);
        }

        public static string GetWritableCacheFilePath(string? configuredDirectory, string cacheKey, string extension)
        {
            var root = EnsureCacheRoot(configuredDirectory);
            return GetCacheFilePath(root, cacheKey, extension);
        }

        public static string ComputeSha256Hex(string text)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text ?? string.Empty));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        public static string BuildMergedBatchCacheKey(
            string resolvedModelPath,
            IReadOnlyList<Matrix4> transforms,
            bool loadAllLods)
        {
            var normalizedPath = Path.GetFullPath(resolvedModelPath)
                .Replace('\\', '/');
            var builder = new StringBuilder();
            builder.Append("merged-static-v").Append(CacheFormatVersion).Append('\n');
            builder.Append("lod=").Append(loadAllLods ? "all" : "default").Append('\n');
            builder.Append("path=").Append(normalizedPath).Append('\n');

            if (File.Exists(resolvedModelPath))
            {
                var info = new FileInfo(resolvedModelPath);
                builder.Append("length=").Append(info.Length.ToString(CultureInfo.InvariantCulture)).Append('\n');
                builder.Append("mtime=").Append(info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture)).Append('\n');
            }
            else
            {
                builder.Append("missing-source\n");
            }

            builder.Append("count=").Append(transforms.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
            foreach (var transform in transforms)
            {
                AppendMatrix(builder, transform);
            }

            return ComputeSha256Hex(builder.ToString());
        }

        public static string BuildPreparedModelCacheKey(string resolvedModelPath, bool loadAllLods)
        {
            var normalizedPath = Path.GetFullPath(resolvedModelPath)
                .Replace('\\', '/');
            return ComputeSha256Hex(
                string.Join(
                    "\n",
                    $"prepared-model-v{CacheFormatVersion}",
                    $"lod={(loadAllLods ? "all" : "default")}",
                    "cpu-morph=off",
                    $"path={normalizedPath}"));
        }

        private static string EnsureCacheRoot(string? configuredDirectory)
        {
            var root = GetCacheRoot(configuredDirectory);
            Directory.CreateDirectory(root);
            var markerPath = Path.Combine(root, MarkerFileName);
            if (!File.Exists(markerPath))
            {
                File.WriteAllText(markerPath, $"TrinitySceneView scene cache v{CacheFormatVersion}{Environment.NewLine}");
            }
            return root;
        }

        private static bool IsSafeCacheRoot(string root)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                return false;
            }

            if (string.Equals(Path.GetFileName(Path.TrimEndingDirectorySeparator(root)), DefaultCacheDirectoryName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return File.Exists(Path.Combine(root, MarkerFileName));
        }

        private static string GetCacheFilePath(string root, string cacheKey, string extension)
        {
            var safeExtension = string.IsNullOrWhiteSpace(extension)
                ? ".cache"
                : (extension.StartsWith('.') ? extension : "." + extension);
            return Path.Combine(root, ComputeSha256Hex(cacheKey ?? string.Empty) + safeExtension);
        }

        private static void AppendMatrix(StringBuilder builder, Matrix4 matrix)
        {
            AppendFloat(builder, matrix.M11);
            AppendFloat(builder, matrix.M12);
            AppendFloat(builder, matrix.M13);
            AppendFloat(builder, matrix.M14);
            AppendFloat(builder, matrix.M21);
            AppendFloat(builder, matrix.M22);
            AppendFloat(builder, matrix.M23);
            AppendFloat(builder, matrix.M24);
            AppendFloat(builder, matrix.M31);
            AppendFloat(builder, matrix.M32);
            AppendFloat(builder, matrix.M33);
            AppendFloat(builder, matrix.M34);
            AppendFloat(builder, matrix.M41);
            AppendFloat(builder, matrix.M42);
            AppendFloat(builder, matrix.M43);
            AppendFloat(builder, matrix.M44);
            builder.Append('\n');
        }

        private static void AppendFloat(StringBuilder builder, float value)
        {
            builder.Append(value.ToString("R", CultureInfo.InvariantCulture));
            builder.Append(',');
        }
    }
}
