using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System;

namespace GFTool.Renderer.Core.Graphics
{
    internal sealed class EnvironmentMap : IDisposable
    {
        public int CubemapTextureId { get; }
        public float MaxLod { get; }

        private readonly int size;

        public EnvironmentMap(int size)
        {
            this.size = Math.Max(4, size);

            GL.GenTextures(1, out int tex);
            CubemapTextureId = tex;
            GL.BindTexture(TextureTarget.TextureCubeMap, CubemapTextureId);

            GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
            GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapR, (int)TextureWrapMode.ClampToEdge);

            for (int face = 0; face < 6; face++)
            {
                GL.TexImage2D(TextureTarget.TextureCubeMapPositiveX + face, 0, PixelInternalFormat.Rgb16f, this.size, this.size, 0, PixelFormat.Rgb, PixelType.Float, IntPtr.Zero);
            }

            UploadProceduralFaces();

            GL.GenerateMipmap(GenerateMipmapTarget.TextureCubeMap);
            MaxLod = MathF.Log2(this.size);
            GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureBaseLevel, 0);
            GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMaxLevel, (int)MathF.Floor(MaxLod));

            GL.BindTexture(TextureTarget.TextureCubeMap, 0);
        }

        private static Vector3 FaceDirection(int face, float u, float v)
        {
            Vector3 dir = face switch
            {
                0 => new Vector3(1, v, -u),
                1 => new Vector3(-1, v, u),
                2 => new Vector3(u, 1, -v),
                3 => new Vector3(u, -1, v),
                4 => new Vector3(u, v, 1),
                _ => new Vector3(-u, v, -1),
            };
            return dir.Normalized();
        }

        private void UploadProceduralFaces()
        {
            Vector3 skyTop = new Vector3(0.55f, 0.68f, 0.92f);
            Vector3 skyHorizon = new Vector3(0.82f, 0.86f, 0.93f);
            Vector3 ground = new Vector3(0.08f, 0.08f, 0.09f);
            Vector3 sunDir = new Vector3(-0.2f, 0.9f, -0.25f).Normalized();

            int pixels = size * size;
            float[] rgb = new float[pixels * 3];

            for (int face = 0; face < 6; face++)
            {
                int idx = 0;
                for (int y = 0; y < size; y++)
                {
                    float vf = (2.0f * (y + 0.5f) / size) - 1.0f;
                    for (int x = 0; x < size; x++)
                    {
                        float uf = (2.0f * (x + 0.5f) / size) - 1.0f;
                        var d = FaceDirection(face, uf, vf);

                        float t = Math.Clamp((d.Y + 1.0f) * 0.5f, 0.0f, 1.0f);
                        Vector3 sky = Vector3.Lerp(skyHorizon, skyTop, MathF.Pow(t, 1.5f));
                        Vector3 baseCol = d.Y >= 0.0f ? sky : ground;

                        float sun = MathF.Max(Vector3.Dot(d, sunDir), 0.0f);
                        float sunGlow = MathF.Pow(sun, 512.0f) + 0.15f * MathF.Pow(sun, 32.0f);
                        Vector3 col = baseCol + new Vector3(1.0f, 0.95f, 0.85f) * sunGlow;

                        rgb[idx++] = col.X;
                        rgb[idx++] = col.Y;
                        rgb[idx++] = col.Z;
                    }
                }

                GL.TexSubImage2D(TextureTarget.TextureCubeMapPositiveX + face, 0, 0, 0, size, size, PixelFormat.Rgb, PixelType.Float, rgb);
            }
        }

        public void Dispose()
        {
            if (CubemapTextureId != 0)
            {
                GL.DeleteTexture(CubemapTextureId);
            }
        }
    }
}
