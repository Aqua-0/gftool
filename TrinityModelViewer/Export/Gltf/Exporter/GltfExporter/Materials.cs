using GFTool.Renderer.Scene.GraphicsObjects;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace TrinityModelViewer.Export
{
    internal static partial class GltfExporter
    {
        private static int GetOrCreateMaterial(
            GltfRoot gltf,
            Dictionary<string, int> gltfMaterialIndex,
            Dictionary<string, Material> materialByName,
            Dictionary<string, int> textureCache,
            string materialName,
            string texDir,
            bool forceRepeatU = false,
            bool forceRepeatV = false)
        {
            materialName ??= string.Empty;
            string materialKey = $"{materialName}|ru={(forceRepeatU ? 1 : 0)}|rv={(forceRepeatV ? 1 : 0)}";
            if (gltfMaterialIndex.TryGetValue(materialKey, out int existing))
            {
                return existing;
            }

            materialByName.TryGetValue(materialName, out var mat);
            var texByName = mat?.Textures?.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase) ?? new Dictionary<string, Texture>(StringComparer.OrdinalIgnoreCase);

            int? baseColorTex = TryGetTextureIndex(gltf, textureCache, mat, texByName, "BaseColorMap", forceRepeatU, forceRepeatV);
            int? normalTex = TryGetTextureIndex(gltf, textureCache, mat, texByName, "NormalMap", forceRepeatU, forceRepeatV);
            int? aoTex = TryGetTextureIndex(gltf, textureCache, mat, texByName, "AOMap", forceRepeatU, forceRepeatV);

            int? mrTex = TryAddMetallicRoughnessTexture(gltf, texDir, mat, texByName, materialName, forceRepeatU, forceRepeatV);

            var pbr = new GltfPbrMetallicRoughness();
            if (baseColorTex.HasValue)
            {
                pbr.BaseColorTexture = new GltfTextureInfo { Index = baseColorTex.Value };
            }
            pbr.BaseColorFactor = new[] { 1f, 1f, 1f, 1f };
            pbr.MetallicFactor = 1f;
            pbr.RoughnessFactor = 1f;
            if (mrTex.HasValue)
            {
                pbr.MetallicRoughnessTexture = new GltfTextureInfo { Index = mrTex.Value };
            }

            var gltfMat = new GltfMaterial
            {
                Name = string.IsNullOrWhiteSpace(materialName) ? "Material" : materialName,
                PbrMetallicRoughness = pbr,
                AlphaMode = mat?.IsTransparent == true ? "BLEND" : null,
                DoubleSided = true
            };

            if (normalTex.HasValue)
            {
                gltfMat.NormalTexture = new GltfNormalTextureInfo { Index = normalTex.Value, Scale = 1f };
            }

            if (aoTex.HasValue)
            {
                gltfMat.OcclusionTexture = new GltfOcclusionTextureInfo { Index = aoTex.Value, Strength = 1f };
            }

            int gltfIndex = gltf.Materials.Count;
            gltf.Materials.Add(gltfMat);
            gltfMaterialIndex[materialKey] = gltfIndex;
            return gltfIndex;
        }

        private static int? TryGetTextureIndex(
            GltfRoot gltf,
            Dictionary<string, int> textureCache,
            Material? material,
            Dictionary<string, Texture> texByName,
            string textureName,
            bool forceRepeatU,
            bool forceRepeatV)
        {
            if (!texByName.TryGetValue(textureName, out var tex) || tex == null)
            {
                return null;
            }

            var (wrapS, wrapT) = ResolveExportWrapModes(material, tex);
            if (textureCache.TryGetValue(GetTextureKey(tex, wrapS, wrapT), out var idx))
            {
                return ApplyWrapOverrideToTexture(gltf, idx, wrapS, wrapT, forceRepeatU, forceRepeatV);
            }

            return null;
        }

        private static int? TryAddMetallicRoughnessTexture(
            GltfRoot gltf,
            string texDir,
            Material? material,
            Dictionary<string, Texture> texByName,
            string materialName,
            bool forceRepeatU,
            bool forceRepeatV)
        {
            texByName.TryGetValue("RoughnessMap", out var roughTex);
            texByName.TryGetValue("MetallicMap", out var metalTex);
            if (roughTex == null && metalTex == null)
            {
                return null;
            }

            using var roughBmp = roughTex?.LoadPreviewBitmap();
            using var metalBmp = metalTex?.LoadPreviewBitmap();
            if (roughBmp == null && metalBmp == null) return null;

            int width = roughBmp?.Width ?? metalBmp!.Width;
            int height = roughBmp?.Height ?? metalBmp!.Height;

            using var outBmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    byte rough = 255;
                    byte metal = 0;
                    if (roughBmp != null)
                    {
                        var c = roughBmp.GetPixel(x * roughBmp.Width / width, y * roughBmp.Height / height);
                        rough = c.R;
                    }
                    if (metalBmp != null)
                    {
                        var c = metalBmp.GetPixel(x * metalBmp.Width / width, y * metalBmp.Height / height);
                        metal = c.R;
                    }
                    // glTF expects roughness in G and metallic in B.
                    outBmp.SetPixel(x, y, Color.FromArgb(255, 0, rough, metal));
                }
            }

            string baseName = string.IsNullOrWhiteSpace(materialName) ? "metallicRoughness" : $"{materialName}_metallicRoughness";
            string fileName = SanitizeFileName($"{baseName}.png");
            string outPath = Path.Combine(texDir, fileName);
            outBmp.Save(outPath, ImageFormat.Png);

            int imgIndex = gltf.Images.Count;
            gltf.Images.Add(new GltfImage { Uri = $"{Path.GetFileName(texDir)}/{fileName}" });
            int texIndex = gltf.Textures.Count;
            var samplerSource = roughTex ?? metalTex;
            var (wrapS, wrapT) = samplerSource != null ? ResolveExportWrapModes(material, samplerSource) : (OpenTK.Graphics.OpenGL4.TextureWrapMode.ClampToEdge, OpenTK.Graphics.OpenGL4.TextureWrapMode.ClampToEdge);
            if (forceRepeatU && wrapS == OpenTK.Graphics.OpenGL4.TextureWrapMode.ClampToEdge)
            {
                wrapS = OpenTK.Graphics.OpenGL4.TextureWrapMode.Repeat;
            }
            if (forceRepeatV && wrapT == OpenTK.Graphics.OpenGL4.TextureWrapMode.ClampToEdge)
            {
                wrapT = OpenTK.Graphics.OpenGL4.TextureWrapMode.Repeat;
            }
            int samplerIndex = GetOrCreateSampler(gltf, wrapS, wrapT);
            gltf.Textures.Add(new GltfTexture { Sampler = samplerIndex, Source = imgIndex, Name = "metallicRoughness" });
            return texIndex;
        }

        private static int ApplyWrapOverrideToTexture(
            GltfRoot gltf,
            int textureIndex,
            OpenTK.Graphics.OpenGL4.TextureWrapMode nativeWrapS,
            OpenTK.Graphics.OpenGL4.TextureWrapMode nativeWrapT,
            bool forceRepeatU,
            bool forceRepeatV)
        {
            if (textureIndex < 0 || textureIndex >= gltf.Textures.Count)
            {
                return textureIndex;
            }

            var desiredWrapS = forceRepeatU && nativeWrapS == OpenTK.Graphics.OpenGL4.TextureWrapMode.ClampToEdge
                ? OpenTK.Graphics.OpenGL4.TextureWrapMode.Repeat
                : nativeWrapS;
            var desiredWrapT = forceRepeatV && nativeWrapT == OpenTK.Graphics.OpenGL4.TextureWrapMode.ClampToEdge
                ? OpenTK.Graphics.OpenGL4.TextureWrapMode.Repeat
                : nativeWrapT;

            var original = gltf.Textures[textureIndex];
            int desiredSampler = GetOrCreateSampler(gltf, desiredWrapS, desiredWrapT);
            if (original.Sampler == desiredSampler)
            {
                return textureIndex;
            }

            for (int i = 0; i < gltf.Textures.Count; i++)
            {
                var tex = gltf.Textures[i];
                if (tex.Source == original.Source &&
                    tex.Sampler == desiredSampler &&
                    string.Equals(tex.Name, original.Name, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            int newTextureIndex = gltf.Textures.Count;
            gltf.Textures.Add(new GltfTexture
            {
                Name = original.Name,
                Sampler = desiredSampler,
                Source = original.Source
            });
            return newTextureIndex;
        }

        private static Bitmap FlipGreenChannel(Bitmap src)
        {
            var dst = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
            for (int y = 0; y < src.Height; y++)
            {
                for (int x = 0; x < src.Width; x++)
                {
                    var c = src.GetPixel(x, y);
                    dst.SetPixel(x, y, Color.FromArgb(c.A, c.R, 255 - c.G, c.B));
                }
            }
            return dst;
        }
    }
}
