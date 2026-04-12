using BnTxx;
using GFTool.Renderer.Core;
using GFTool.Renderer.Scene.GraphicsObjects;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TrinityModelViewer
{
    public partial class ModelViewerForm
    {
        private void materialTexturesGrid_SelectionChanged(object? sender, EventArgs e)
        {
            if (materialTexturesGrid.SelectedRows.Count == 0)
            {
                SetTexturePreview(null, ownsImage: true);
                SetUvPreview(null, ownsImage: true);
                UpdateTextureInfoLabel(null, null);
                UpdateReplaceChannelButtonState();
                return;
            }

            if (materialTexturesGrid.SelectedRows[0].Tag is Texture)
            {
                RequestMaterialPreviewUpdate();
                UpdateReplaceChannelButtonState();
                return;
            }

            SetTexturePreview(null, ownsImage: true);
            SetUvPreview(null, ownsImage: true);
            UpdateTextureInfoLabel(null, null);
            UpdateReplaceChannelButtonState();
        }

        private void SetTexturePreview(Image? image, bool ownsImage)
        {
            if (ownsTexturePreviewDisplayImage)
            {
                texturePreviewDisplayImage?.Dispose();
            }
            texturePreviewDisplayImage = null;
            ownsTexturePreviewDisplayImage = false;

            if (ownsTexturePreviewSourceImage)
            {
                texturePreviewSourceImage?.Dispose();
            }
            texturePreviewSourceImage = image;
            ownsTexturePreviewSourceImage = ownsImage;

            UpdateTexturePreviewDisplay();
        }

        private enum TexturePreviewChannel
        {
            Rgba,
            R,
            G,
            B,
            A,
            RgbIgnoreAlpha
        }

        private TexturePreviewChannel GetSelectedTexturePreviewChannel()
        {
            return materialTextureChannelCombo.SelectedIndex switch
            {
                1 => TexturePreviewChannel.R,
                2 => TexturePreviewChannel.G,
                3 => TexturePreviewChannel.B,
                4 => TexturePreviewChannel.A,
                5 => TexturePreviewChannel.RgbIgnoreAlpha,
                _ => TexturePreviewChannel.Rgba
            };
        }

        private void materialTextureChannelCombo_SelectedIndexChanged(object? sender, EventArgs e)
        {
            UpdateTexturePreviewDisplay();
            UpdateReplaceChannelButtonState();
            UpdateTextureInfoLabel(GetSelectedTexture(), texturePreviewSourceImage as Bitmap);
        }

        private void UpdateTexturePreviewDisplay()
        {
            if (ownsTexturePreviewDisplayImage)
            {
                texturePreviewDisplayImage?.Dispose();
            }
            texturePreviewDisplayImage = null;
            ownsTexturePreviewDisplayImage = false;

            if (texturePreviewSourceImage == null)
            {
                materialTexturePreview.Image = null;
                return;
            }

            var channel = GetSelectedTexturePreviewChannel();
            if (channel == TexturePreviewChannel.Rgba)
            {
                materialTexturePreview.Image = texturePreviewSourceImage;
                return;
            }

            if (texturePreviewSourceImage is not Bitmap bitmap)
            {
                materialTexturePreview.Image = texturePreviewSourceImage;
                return;
            }

            var selected = GetSelectedTexture();
            if (selected == null)
            {
                texturePreviewDisplayImage = BuildTextureChannelPreview(bitmap, channel);
                ownsTexturePreviewDisplayImage = true;
                materialTexturePreview.Image = texturePreviewDisplayImage;
                return;
            }

            var key = $"{selected.CacheKey}|ch={(int)channel}";
            if (!textureChannelCache.TryGet(key, out var cached))
            {
                cached = BuildTextureChannelPreview(bitmap, channel);
                textureChannelCache.Put(key, cached);
            }
            texturePreviewDisplayImage = cached;
            ownsTexturePreviewDisplayImage = false;
            materialTexturePreview.Image = cached;
        }

        private void UpdateReplaceChannelButtonState()
        {
            if (materialTextureReplaceChannelButton == null)
            {
                return;
            }

            var channel = GetSelectedTexturePreviewChannel();
            bool hasTexture = GetSelectedTexture() != null;
            bool isChannelMode = channel is TexturePreviewChannel.R or TexturePreviewChannel.G or TexturePreviewChannel.B or TexturePreviewChannel.A;
            materialTextureReplaceChannelButton.Enabled = isChannelMode && hasTexture;

            bool isFillMode = isChannelMode || channel is TexturePreviewChannel.Rgba or TexturePreviewChannel.RgbIgnoreAlpha;
            if (materialTextureFillBlackButton != null) materialTextureFillBlackButton.Enabled = hasTexture && isFillMode;
            if (materialTextureFillWhiteButton != null) materialTextureFillWhiteButton.Enabled = hasTexture && isFillMode;
            if (materialTextureFillGrayButton != null) materialTextureFillGrayButton.Enabled = hasTexture && isFillMode;
            if (materialTextureFillNormalButton != null) materialTextureFillNormalButton.Enabled = hasTexture && isFillMode;
        }

        private void UpdateTextureInfoLabel(Texture? texture, Bitmap? decodedBitmap)
        {
            if (materialTextureInfoLabel == null)
            {
                return;
            }

            if (texture == null)
            {
                materialTextureInfoLabel.Text = string.Empty;
                return;
            }

            string res = decodedBitmap != null
                ? $"{decodedBitmap.Width}x{decodedBitmap.Height}"
                : "?x?";

            var format = TryResolveTextureTypeLabel(texture);
            materialTextureInfoLabel.Text = string.IsNullOrWhiteSpace(format) ? res : $"{res} {format}";
        }

        private static string TryResolveTextureTypeLabel(Texture texture)
        {
            if (texture == null)
            {
                return string.Empty;
            }

            try
            {
                // CacheKey is: "<bestPath>|<preferredName>|<wrapS>|<wrapT>|<wrapR>"
                // The first segment is typically a disk path for BNTX files, which we can probe for format metadata.
                var key = texture.CacheKey ?? string.Empty;
                var split = key.Split('|');
                if (split.Length < 2)
                {
                    return string.Empty;
                }

                var path = split[0];
                var preferredName = split[1];

                if (string.IsNullOrWhiteSpace(path))
                {
                    return string.Empty;
                }

                var ext = Path.GetExtension(path);
                if (!string.IsNullOrWhiteSpace(ext) &&
                    (ext.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
                     ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                     ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                     ext.Equals(".bmp", StringComparison.OrdinalIgnoreCase)))
                {
                    return ext.TrimStart('.').ToUpperInvariant();
                }

                if (!ext.Equals(".bntx", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
                {
                    return string.Empty;
                }

                if (!BNTX.TryGetUltimateTexFormatFromFile(path, preferredName, out var ultimateFormat, out _, out _))
                {
                    return "BNTX";
                }

                return ultimateFormat switch
                {
                    "BC7RgbaUnorm" => "BC7_UNORM",
                    "BC7RgbaUnormSrgb" => "BC7_SRGB",
                    "BC5RgUnorm" => "BC5_UNORM",
                    "BC5RgSnorm" => "BC5_SNORM",
                    "BC4RUnorm" => "BC4_UNORM",
                    "BC4RSnorm" => "BC4_SNORM",
                    "BC3RgbaUnorm" => "BC3_UNORM",
                    "BC3RgbaUnormSrgb" => "BC3_SRGB",
                    "BC1RgbaUnorm" => "BC1_UNORM",
                    "BC1RgbaUnormSrgb" => "BC1_SRGB",
                    "BC2RgbaUnorm" => "BC2_UNORM",
                    "BC2RgbaUnormSrgb" => "BC2_SRGB",
                    "BC6hRgbUfloat" => "BC6H_UF16",
                    "Rgba8Unorm" => "RGBA8_UNORM",
                    "Rgba8UnormSrgb" => "RGBA8_SRGB",
                    _ => ultimateFormat
                };
            }
            catch
            {
                return string.Empty;
            }
        }

        private void materialTextureReplaceChannelButton_Click(object? sender, EventArgs e)
        {
            var texture = GetSelectedTexture();
            if (texture == null)
            {
                MessageBox.Show(this, "No texture selected.", "Replace Channel", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var channel = GetSelectedTexturePreviewChannel();
            if (channel is not (TexturePreviewChannel.R or TexturePreviewChannel.G or TexturePreviewChannel.B or TexturePreviewChannel.A))
            {
                MessageBox.Show(this, "Select a single channel (R/G/B/A) first.", "Replace Channel", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using var ofd = new OpenFileDialog();
            ofd.Title = $"Replace {channel} Channel";
            ofd.Filter = "Image files (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|All files (*.*)|*.*";
            if (ofd.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(ofd.FileName))
            {
                return;
            }

            try
            {
                using var source = texture.TryGetEditedBitmap(out var existingEdited) ? existingEdited : texture.LoadPreviewBitmap();
                if (source == null)
                {
                    MessageBox.Show(this, "Texture could not be decoded.", "Replace Channel", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                using var imported = new Bitmap(ofd.FileName);
                using var resized = imported.Width == source.Width && imported.Height == source.Height
                    ? (Bitmap)imported.Clone()
                    : ResizeBitmapNearest(imported, source.Width, source.Height);

                using var merged = ReplaceBitmapChannel(source, resized, channel);

                if (!texture.TryReplaceFromImage(merged, out var error))
                {
                    MessageBox.Show(this, $"Replace failed:\n{error}", "Replace Channel", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                texturePreviewCache.RemoveWhere(k => string.Equals(k, texture.CacheKey, StringComparison.OrdinalIgnoreCase));
                textureChannelCache.RemoveWhere(k => k.StartsWith(texture.CacheKey, StringComparison.OrdinalIgnoreCase));
                uvPreviewCache.RemoveWhere(k => k.Contains(texture.CacheKey, StringComparison.OrdinalIgnoreCase));

                RequestMaterialPreviewUpdate();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Replace failed:\n{ex.Message}", "Replace Channel", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private enum TextureFillPreset
        {
            Black,
            White,
            Gray,
            Normal
        }

        private void materialTextureFillBlackButton_Click(object? sender, EventArgs e) => FillSelectedTexture(TextureFillPreset.Black);
        private void materialTextureFillWhiteButton_Click(object? sender, EventArgs e) => FillSelectedTexture(TextureFillPreset.White);
        private void materialTextureFillGrayButton_Click(object? sender, EventArgs e) => FillSelectedTexture(TextureFillPreset.Gray);
        private void materialTextureFillNormalButton_Click(object? sender, EventArgs e) => FillSelectedTexture(TextureFillPreset.Normal);

        private void FillSelectedTexture(TextureFillPreset preset)
        {
            var texture = GetSelectedTexture();
            if (texture == null)
            {
                MessageBox.Show(this, "No texture selected.", "Fill Texture", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var channel = GetSelectedTexturePreviewChannel();
            bool isAllowed = channel is TexturePreviewChannel.Rgba or TexturePreviewChannel.RgbIgnoreAlpha or TexturePreviewChannel.R or TexturePreviewChannel.G or TexturePreviewChannel.B or TexturePreviewChannel.A;
            if (!isAllowed)
            {
                MessageBox.Show(this, "Select RGBA, RGB (ignore alpha), or a single channel (R/G/B/A) first.", "Fill Texture", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                using var source = texture.TryGetEditedBitmap(out var existingEdited) ? existingEdited : texture.LoadPreviewBitmap();
                if (source == null)
                {
                    MessageBox.Show(this, "Texture could not be decoded.", "Fill Texture", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                using var filled = FillBitmap(source, channel, preset);

                if (!texture.TryReplaceFromImage(filled, out var error))
                {
                    MessageBox.Show(this, $"Fill failed:\n{error}", "Fill Texture", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                texturePreviewCache.RemoveWhere(k => string.Equals(k, texture.CacheKey, StringComparison.OrdinalIgnoreCase));
                textureChannelCache.RemoveWhere(k => k.StartsWith(texture.CacheKey, StringComparison.OrdinalIgnoreCase));
                uvPreviewCache.RemoveWhere(k => k.Contains(texture.CacheKey, StringComparison.OrdinalIgnoreCase));

                RequestMaterialPreviewUpdate();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Fill failed:\n{ex.Message}", "Fill Texture", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static Bitmap ResizeBitmapNearest(Bitmap source, int width, int height)
        {
            var output = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(output);
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(source, new Rectangle(0, 0, width, height));
            return output;
        }

        private static Bitmap FillBitmap(Bitmap baseBitmap, TexturePreviewChannel selectedChannel, TextureFillPreset preset)
        {
            var output = EnsureArgbBitmap(baseBitmap);
            var rect = new Rectangle(0, 0, output.Width, output.Height);
            var outData = output.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadWrite, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            try
            {
                int stride = outData.Stride;
                int len = Math.Abs(stride) * output.Height;
                var bytes = new byte[len];
                Marshal.Copy(outData.Scan0, bytes, 0, bytes.Length);

                var (rFill, gFill, bFill, aFill) = GetPresetRgba(preset);
                byte selectedValue = preset switch
                {
                    TextureFillPreset.Black => 0,
                    TextureFillPreset.White => 255,
                    TextureFillPreset.Gray => 128,
                    TextureFillPreset.Normal => selectedChannel switch
                    {
                        TexturePreviewChannel.R => rFill,
                        TexturePreviewChannel.G => gFill,
                        TexturePreviewChannel.B => bFill,
                        TexturePreviewChannel.A => aFill,
                        _ => 0
                    },
                    _ => 0
                };

                for (int y = 0; y < output.Height; y++)
                {
                    int rowIndex = stride >= 0 ? y : (output.Height - 1 - y);
                    int row = rowIndex * Math.Abs(stride);

                    for (int x = 0; x < output.Width; x++)
                    {
                        int off = row + x * 4;

                        switch (selectedChannel)
                        {
                            case TexturePreviewChannel.Rgba:
                                bytes[off + 0] = bFill;
                                bytes[off + 1] = gFill;
                                bytes[off + 2] = rFill;
                                bytes[off + 3] = aFill;
                                break;

                            case TexturePreviewChannel.RgbIgnoreAlpha:
                                bytes[off + 0] = bFill;
                                bytes[off + 1] = gFill;
                                bytes[off + 2] = rFill;
                                break;

                            case TexturePreviewChannel.R:
                                bytes[off + 2] = selectedValue;
                                break;

                            case TexturePreviewChannel.G:
                                bytes[off + 1] = selectedValue;
                                break;

                            case TexturePreviewChannel.B:
                                bytes[off + 0] = selectedValue;
                                break;

                            case TexturePreviewChannel.A:
                                bytes[off + 3] = selectedValue;
                                break;
                        }
                    }
                }

                Marshal.Copy(bytes, 0, outData.Scan0, bytes.Length);
            }
            finally
            {
                output.UnlockBits(outData);
            }

            return output;
        }

        private static (byte R, byte G, byte B, byte A) GetPresetRgba(TextureFillPreset preset)
        {
            return preset switch
            {
                TextureFillPreset.Black => (0, 0, 0, 255),
                TextureFillPreset.White => (255, 255, 255, 255),
                TextureFillPreset.Gray => (128, 128, 128, 255),
                TextureFillPreset.Normal => (132, 132, 255, 255),
                _ => (0, 0, 0, 255)
            };
        }

        private static Bitmap ReplaceBitmapChannel(Bitmap baseBitmap, Bitmap channelBitmap, TexturePreviewChannel channel)
        {
            using var baseArgb = EnsureArgbBitmap(baseBitmap);
            using var channelArgb = EnsureArgbBitmap(channelBitmap);

            var output = new Bitmap(baseArgb.Width, baseArgb.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            var rect = new Rectangle(0, 0, output.Width, output.Height);
            var baseData = baseArgb.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var channelData = channelArgb.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var outData = output.LockBits(rect, System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            try
            {
                int baseStride = baseData.Stride;
                int chStride = channelData.Stride;
                int outStride = outData.Stride;
                int baseLen = Math.Abs(baseStride) * baseArgb.Height;
                int chLen = Math.Abs(chStride) * channelArgb.Height;
                int outLen = Math.Abs(outStride) * output.Height;

                var baseBytes = new byte[baseLen];
                var chBytes = new byte[chLen];
                var outBytes = new byte[outLen];

                System.Runtime.InteropServices.Marshal.Copy(baseData.Scan0, baseBytes, 0, baseBytes.Length);
                System.Runtime.InteropServices.Marshal.Copy(channelData.Scan0, chBytes, 0, chBytes.Length);

                for (int y = 0; y < output.Height; y++)
                {
                    int baseRowIndex = baseStride >= 0 ? y : (output.Height - 1 - y);
                    int chRowIndex = chStride >= 0 ? y : (output.Height - 1 - y);
                    int outRowIndex = outStride >= 0 ? y : (output.Height - 1 - y);
                    int baseRow = baseRowIndex * Math.Abs(baseStride);
                    int chRow = chRowIndex * Math.Abs(chStride);
                    int outRow = outRowIndex * Math.Abs(outStride);

                    for (int x = 0; x < output.Width; x++)
                    {
                        int baseOff = baseRow + x * 4;
                        int chOff = chRow + x * 4;
                        int outOff = outRow + x * 4;

                        byte b = baseBytes[baseOff + 0];
                        byte g = baseBytes[baseOff + 1];
                        byte r = baseBytes[baseOff + 2];
                        byte a = baseBytes[baseOff + 3];

                        byte chB = chBytes[chOff + 0];
                        byte chG = chBytes[chOff + 1];
                        byte chR = chBytes[chOff + 2];
                        byte chA = chBytes[chOff + 3];

                        byte value = channel switch
                        {
                            TexturePreviewChannel.R => chR,
                            TexturePreviewChannel.G => chG,
                            TexturePreviewChannel.B => chB,
                            TexturePreviewChannel.A => chA,
                            _ => chR
                        };

                        byte outR = r, outG = g, outB = b, outA = a;
                        switch (channel)
                        {
                            case TexturePreviewChannel.R:
                                outR = value;
                                break;
                            case TexturePreviewChannel.G:
                                outG = value;
                                break;
                            case TexturePreviewChannel.B:
                                outB = value;
                                break;
                            case TexturePreviewChannel.A:
                                outA = value;
                                break;
                        }

                        outBytes[outOff + 0] = outB;
                        outBytes[outOff + 1] = outG;
                        outBytes[outOff + 2] = outR;
                        outBytes[outOff + 3] = outA;
                    }
                }

                System.Runtime.InteropServices.Marshal.Copy(outBytes, 0, outData.Scan0, outBytes.Length);
            }
            finally
            {
                baseArgb.UnlockBits(baseData);
                channelArgb.UnlockBits(channelData);
                output.UnlockBits(outData);
            }

            return output;
        }

        private static Bitmap EnsureArgbBitmap(Image source)
        {
            if (source is Bitmap bitmap && bitmap.PixelFormat == System.Drawing.Imaging.PixelFormat.Format32bppArgb)
            {
                return (Bitmap)bitmap.Clone();
            }

            var output = new Bitmap(source.Width, source.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(output);
            g.CompositingMode = CompositingMode.SourceCopy;
            g.DrawImage(source, new Rectangle(0, 0, output.Width, output.Height));
            return output;
        }

        private static Bitmap BuildTextureChannelPreview(Bitmap source, TexturePreviewChannel channel)
        {
            var output = new Bitmap(source.Width, source.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            var srcRect = new Rectangle(0, 0, source.Width, source.Height);
            var dstRect = new Rectangle(0, 0, output.Width, output.Height);

            var srcData = source.LockBits(srcRect, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var dstData = output.LockBits(dstRect, System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            try
            {
                int srcStride = srcData.Stride;
                int dstStride = dstData.Stride;
                int srcLen = Math.Abs(srcStride) * source.Height;
                int dstLen = Math.Abs(dstStride) * output.Height;

                var srcBytes = new byte[srcLen];
                var dstBytes = new byte[dstLen];

                System.Runtime.InteropServices.Marshal.Copy(srcData.Scan0, srcBytes, 0, srcBytes.Length);

                for (int y = 0; y < source.Height; y++)
                {
                    int srcRowIndex = srcStride >= 0 ? y : (source.Height - 1 - y);
                    int dstRowIndex = dstStride >= 0 ? y : (output.Height - 1 - y);
                    int srcRow = srcRowIndex * Math.Abs(srcStride);
                    int dstRow = dstRowIndex * Math.Abs(dstStride);

                    for (int x = 0; x < source.Width; x++)
                    {
                        int srcOff = srcRow + x * 4;
                        int dstOff = dstRow + x * 4;

                        byte b = srcBytes[srcOff + 0];
                        byte g = srcBytes[srcOff + 1];
                        byte r = srcBytes[srcOff + 2];
                        byte a = srcBytes[srcOff + 3];

                        byte outR = r, outG = g, outB = b, outA = a;

                        switch (channel)
                        {
                            case TexturePreviewChannel.R:
                                outR = outG = outB = r;
                                outA = 255;
                                break;
                            case TexturePreviewChannel.G:
                                outR = outG = outB = g;
                                outA = 255;
                                break;
                            case TexturePreviewChannel.B:
                                outR = outG = outB = b;
                                outA = 255;
                                break;
                            case TexturePreviewChannel.A:
                                outR = outG = outB = a;
                                outA = 255;
                                break;
                            case TexturePreviewChannel.RgbIgnoreAlpha:
                                outR = r;
                                outG = g;
                                outB = b;
                                outA = 255;
                                break;
                        }

                        dstBytes[dstOff + 0] = outB;
                        dstBytes[dstOff + 1] = outG;
                        dstBytes[dstOff + 2] = outR;
                        dstBytes[dstOff + 3] = outA;
                    }
                }

                System.Runtime.InteropServices.Marshal.Copy(dstBytes, 0, dstData.Scan0, dstBytes.Length);
            }
            finally
            {
                source.UnlockBits(srcData);
                output.UnlockBits(dstData);
            }

            return output;
        }

    }
}
