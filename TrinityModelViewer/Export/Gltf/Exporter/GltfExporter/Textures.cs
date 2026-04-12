using GFTool.Renderer.Scene.GraphicsObjects;
using OpenTK.Graphics.OpenGL4;
using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using Trinity.Core.Flatbuffers.TR.Model;

namespace TrinityModelViewer.Export
{
    internal static partial class GltfExporter
    {
        private static Dictionary<string, int> ExportAllTextures(GltfRoot gltf, string texDir, IReadOnlyList<Material> materials)
        {
            var cache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            ExportAllTexturesInto(gltf, texDir, materials, cache, usedNames);
            return cache;
        }

        private static void ExportAllTexturesInto(
            GltfRoot gltf,
            string texDir,
            IReadOnlyList<Material> materials,
            Dictionary<string, int> cache,
            HashSet<string> usedNames)
        {
            foreach (var mat in materials)
            {
                if (mat == null) continue;
                foreach (var tex in mat.Textures)
                {
                    if (tex == null) continue;
                    var (wrapS, wrapT) = ResolveExportWrapModes(mat, tex);
                    var key = GetTextureKey(tex, wrapS, wrapT);
                    if (cache.ContainsKey(key)) continue;

                    using var bmp = tex.LoadPreviewBitmap();
                    if (bmp == null) continue;

                    string outName = MakeUniqueTextureFileName(usedNames, tex);
                    string outPath = Path.Combine(texDir, outName);
                    bmp.Save(outPath, ImageFormat.Png);

                    int samplerIndex = GetOrCreateSampler(gltf, wrapS, wrapT);
                    int imgIndex = gltf.Images.Count;
                    gltf.Images.Add(new GltfImage { Uri = $"{Path.GetFileName(texDir)}/{outName}" });
                    int texIndex = gltf.Textures.Count;
                    gltf.Textures.Add(new GltfTexture { Sampler = samplerIndex, Source = imgIndex, Name = tex.Name });

                    cache[key] = texIndex;
                }
            }
        }

        private static string GetTextureKey(Texture tex, TextureWrapMode wrapS, TextureWrapMode wrapT)
        {
            return $"{tex.Name}|{tex.SourceFile}|{(int)wrapS}|{(int)wrapT}";
        }

        private static (TextureWrapMode WrapS, TextureWrapMode WrapT) ResolveExportWrapModes(Material? material, Texture texture)
        {
            if (material?.Samplers != null &&
                texture.Slot < material.Samplers.Count)
            {
                var sampler = material.Samplers[(int)texture.Slot];
                if (sampler != null)
                {
                    return (ConvertWrapMode(sampler.RepeatU), ConvertWrapMode(sampler.RepeatV));
                }
            }

            return (texture.WrapS, texture.WrapT);
        }

        private static int GetOrCreateSampler(GltfRoot gltf, TextureWrapMode wrapS, TextureWrapMode wrapT)
        {
            int gltfWrapS = ConvertWrapModeToGltf(wrapS);
            int gltfWrapT = ConvertWrapModeToGltf(wrapT);

            for (int i = 0; i < gltf.Samplers.Count; i++)
            {
                var sampler = gltf.Samplers[i];
                if (sampler.MagFilter == 9729 &&
                    sampler.MinFilter == 9729 &&
                    sampler.WrapS == gltfWrapS &&
                    sampler.WrapT == gltfWrapT)
                {
                    return i;
                }
            }

            int samplerIndex = gltf.Samplers.Count;
            gltf.Samplers.Add(new GltfSampler
            {
                MagFilter = 9729,
                MinFilter = 9729,
                WrapS = gltfWrapS,
                WrapT = gltfWrapT
            });
            return samplerIndex;
        }

        private static int ConvertWrapModeToGltf(TextureWrapMode wrapMode)
        {
            return wrapMode switch
            {
                TextureWrapMode.Repeat => 10497,
                TextureWrapMode.MirroredRepeat => 33648,
                _ => 33071
            };
        }

        private static TextureWrapMode ConvertWrapMode(UVWrapMode? mode)
        {
            if (mode == null)
            {
                return TextureWrapMode.ClampToEdge;
            }

            const TextureWrapMode mirrorClampToEdge = (TextureWrapMode)0x8743;
            return mode.Value switch
            {
                UVWrapMode.WRAP => TextureWrapMode.Repeat,
                UVWrapMode.CLAMP => TextureWrapMode.ClampToEdge,
                UVWrapMode.MIRROR => TextureWrapMode.MirroredRepeat,
                UVWrapMode.MIRROR_ONCE => mirrorClampToEdge,
                _ => TextureWrapMode.ClampToEdge
            };
        }

        private static string MakeUniqueTextureFileName(HashSet<string> usedNames, Texture tex)
        {
            string src = tex.SourceFile ?? string.Empty;
            string baseName = Path.GetFileNameWithoutExtension(src);
            if (string.IsNullOrWhiteSpace(baseName))
            {
                baseName = tex.Name;
            }

            string fileName = $"{baseName}.png";
            fileName = SanitizeFileName(fileName);
            if (usedNames.Add(fileName))
            {
                return fileName;
            }

            for (int i = 2; i < 10000; i++)
            {
                string candidate = SanitizeFileName($"{baseName}_{i}.png");
                if (usedNames.Add(candidate))
                {
                    return candidate;
                }
            }

            // Extremely unlikely.
            return SanitizeFileName($"{baseName}_{Guid.NewGuid():N}.png");
        }

        private static string SanitizeFileName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name;
        }
    }
}
