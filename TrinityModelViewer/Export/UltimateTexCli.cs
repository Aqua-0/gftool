using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using BnTxx;

namespace TrinityModelViewer.Export
{
    internal static class UltimateTexCli
    {
        public static bool TryEncodeBntxFromBitmap(
            Bitmap bitmap,
            string ultimateTexCliPath,
            string outputBntxPath,
            string format,
            bool noMipmaps,
            out string error)
        {
            return TryEncodeBntxFromBitmap(bitmap, ultimateTexCliPath, outputBntxPath, format, noMipmaps, sourceBntxPath: null, out error);
        }

        public static bool TryEncodeBntxFromBitmap(
            Bitmap bitmap,
            string ultimateTexCliPath,
            string outputBntxPath,
            string format,
            bool noMipmaps,
            string? sourceBntxPath,
            out string error)
        {
            error = string.Empty;
            if (bitmap == null)
            {
                error = "Bitmap is null.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(ultimateTexCliPath) || !File.Exists(ultimateTexCliPath))
            {
                error = "ultimate_tex_cli path is missing or invalid.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(outputBntxPath))
            {
                error = "Output path is missing.";
                return false;
            }

            var outDir = Path.GetDirectoryName(outputBntxPath);
            if (!string.IsNullOrWhiteSpace(outDir))
            {
                Directory.CreateDirectory(outDir);
            }

            var tempDir = Path.Combine(Path.GetTempPath(), "TrinityModelViewer", "ultimate_tex");
            Directory.CreateDirectory(tempDir);
            var tempPng = Path.Combine(tempDir, Guid.NewGuid().ToString("N") + ".png");
            var tempBntx = Path.Combine(tempDir, Guid.NewGuid().ToString("N") + ".bntx");

            try
            {
                bitmap.Save(tempPng, System.Drawing.Imaging.ImageFormat.Png);

                string args = $"{Q(tempPng)} {Q(tempBntx)} --format {format}";
                if (noMipmaps)
                {
                    args += " --no-mipmaps";
                }

                var psi = new ProcessStartInfo
                {
                    FileName = ultimateTexCliPath,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = outDir ?? Environment.CurrentDirectory
                };

                using var p = Process.Start(psi);
                if (p == null)
                {
                    error = "Failed to start ultimate_tex_cli.";
                    return false;
                }

                string stdout = p.StandardOutput.ReadToEnd();
                string stderr = p.StandardError.ReadToEnd();
                p.WaitForExit();

                if (p.ExitCode != 0 || !File.Exists(tempBntx))
                {
                    error = (stderr ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(error))
                    {
                        error = (stdout ?? string.Empty).Trim();
                    }
                    if (string.IsNullOrWhiteSpace(error))
                    {
                        error = $"ultimate_tex_cli failed (exit={p.ExitCode}).";
                    }
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(sourceBntxPath) && File.Exists(sourceBntxPath))
                {
                    if (!TryWritePatchedBntxContainer(sourceBntxPath, tempBntx, outputBntxPath, out var patchError))
                    {
                        error = patchError;
                        return false;
                    }
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(outputBntxPath) ?? Environment.CurrentDirectory);
                    File.Copy(tempBntx, outputBntxPath, overwrite: true);
                }

                return File.Exists(outputBntxPath);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                try { if (File.Exists(tempPng)) File.Delete(tempPng); } catch { }
                try { if (File.Exists(tempBntx)) File.Delete(tempBntx); } catch { }
            }
        }

        private static string Q(string path) => $"\"{path.Replace("\"", "\\\"")}\"";

        private static bool TryWritePatchedBntxContainer(string sourceBntxPath, string encodedBntxPath, string outputBntxPath, out string error)
        {
            error = string.Empty;

            try
            {
                var srcBytes = File.ReadAllBytes(sourceBntxPath);
                var encBytes = File.ReadAllBytes(encodedBntxPath);

                if (srcBytes.Length < 16 || encBytes.Length < 16)
                {
                    error = "BNTX too small to patch.";
                    return false;
                }

                if (srcBytes[0] != (byte)'B' || srcBytes[1] != (byte)'N' || srcBytes[2] != (byte)'T' || srcBytes[3] != (byte)'X')
                {
                    error = "Source is not a BNTX.";
                    return false;
                }
                if (encBytes[0] != (byte)'B' || encBytes[1] != (byte)'N' || encBytes[2] != (byte)'T' || encBytes[3] != (byte)'X')
                {
                    error = "Encoded output is not a BNTX.";
                    return false;
                }

                var preferred = Path.GetFileNameWithoutExtension(sourceBntxPath);
                if (!BNTX.TryGetTextureLayoutFromFile(sourceBntxPath, preferred, out var srcLayout, out var srcLayoutErr))
                {
                    error = $"Failed to read source BNTX layout: {srcLayoutErr}";
                    return false;
                }
                if (!BNTX.TryGetFirstTextureDataFromFile(encodedBntxPath, out var encData, out var encErr))
                {
                    error = $"Failed to read encoded BNTX data: {encErr}";
                    return false;
                }
                if (encData.Length != srcLayout.DataLength)
                {
                    error = $"Encoded data size mismatch (src={srcLayout.DataLength}, enc={encData.Length}).";
                    return false;
                }
                if (srcLayout.BaseOffset < 0 || srcLayout.BaseOffset + srcLayout.DataLength > srcBytes.Length)
                {
                    error = "Source data region is out of range.";
                    return false;
                }

                Buffer.BlockCopy(encData, 0, srcBytes, (int)srcLayout.BaseOffset, encData.Length);

                File.WriteAllBytes(outputBntxPath, srcBytes);
                return true;
            }
            catch (Exception ex)
            {
                error = $"Failed to patch BNTX container: {ex.Message}";
                return false;
            }
        }

    }
}
