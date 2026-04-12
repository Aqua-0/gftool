using BnTxx;
using GFTool.Core.Particles.Ptcl;
using GFTool.Renderer.Core;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace GFTool.Renderer.Scene.GraphicsObjects.Particles
{
	    public sealed class PtclEffect : RefObject, IParticleObject, IParticleUpdatable, IDisposable
	    {
	        private static readonly HashSet<string> loggedPtclPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
	        private readonly string ptclPath;
	        private readonly Dictionary<ulong, string> textureNameById = new Dictionary<ulong, string>();
	        private readonly Dictionary<string, int> glTextureByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
	        private readonly Dictionary<int, List<ParticleRenderer.ParticleInstance>> batchByTexture = new Dictionary<int, List<ParticleRenderer.ParticleInstance>>();
	        private readonly List<RuntimeEmitter> emitters = new List<RuntimeEmitter>();
	        private PtclBfresMesh? bfresMesh;
	        private bool setupComplete;
	        private bool disposed;

        private sealed class RuntimeEmitter
        {
            public string Name = string.Empty;
            public int TextureId;
            public Vector4 Color;
            public Vector4 Color1;
            public Vector4[] Color0Keys = Array.Empty<Vector4>();
            public Vector4[] Alpha0Keys = Array.Empty<Vector4>();
            public uint Color0KeyCount;
            public uint Alpha0KeyCount;
            public float SpawnRate = 24f;
            public float ParticleLife = 1.2f;
            public float ParticleSize = 0.18f;
            public bool Additive = true;
            public float Accumulator;
            public readonly List<RuntimeParticle> Particles = new List<RuntimeParticle>(capacity: 256);
        }

        private struct RuntimeParticle
        {
            public Vector3 Pos;
            public Vector3 Vel;
            public float Life;
            public float MaxLife;
            public float Size;
            public float Rot;
        }

        private readonly Random rng = new Random();

        public PtclEffect(string ptclPath)
        {
            this.ptclPath = ptclPath ?? string.Empty;
        }

        public override void Setup()
        {
            if (setupComplete || disposed)
            {
                return;
            }

            setupComplete = true;

            if (string.IsNullOrWhiteSpace(ptclPath) || !File.Exists(ptclPath))
            {
                MessageHandler.Instance.AddMessage(MessageType.WARNING, $"[PTCL] Missing ptcl: {ptclPath}");
                return;
            }

            if (!PtclFile.TryLoadFromFile(ptclPath, out var ptcl, out var error))
            {
                MessageHandler.Instance.AddMessage(MessageType.WARNING, $"[PTCL] Failed reading '{ptclPath}': {error}");
                return;
            }

	            if (loggedPtclPaths.Add(ptclPath))
	            {
	                MessageHandler.Instance.AddMessage(
	                    MessageType.LOG,
	                    $"[PTCL] Loaded '{Path.GetFileName(ptclPath)}' vfx={ptcl.VfxVersion} emitters={ptcl.Emitters.Count} textures={ptcl.Textures.Count} bfres={(ptcl.EmbeddedBfresBytes != null && ptcl.EmbeddedBfresBytes.Length != 0 ? "yes" : "no")}");
	            }

            if (ptcl.Textures != null)
            {
                foreach (var t in ptcl.Textures)
                {
                    if (!string.IsNullOrWhiteSpace(t.Name))
                    {
                        textureNameById[t.TextureId] = t.Name;
                    }
                }
            }

	            if (ptcl.EmbeddedBfresBytes != null && ptcl.EmbeddedBfresBytes.Length != 0)
	            {
	                bfresMesh = new PtclBfresMesh(ptcl.EmbeddedBfresBytes);
	                bfresMesh.Setup();
	            }

	            if (ptcl.EmbeddedBntxBytes == null || ptcl.EmbeddedBntxBytes.Length == 0)
	            {
	                MessageHandler.Instance.AddMessage(MessageType.WARNING, $"[PTCL] No embedded BNTX in '{ptclPath}'. Sprite emitters disabled.");
	                return;
	            }

            foreach (var em in ptcl.Emitters)
            {
                int tex = 0;
                foreach (var s in em.Samplers)
                {
                    if (s.TextureId == 0) continue;
                    if (textureNameById.TryGetValue(s.TextureId, out var texName) && !string.IsNullOrWhiteSpace(texName))
                    {
                        tex = GetOrCreateGlTexture(ptcl.EmbeddedBntxBytes, texName);
                        if (tex != 0) break;
                    }
                }

                if (tex == 0)
                {
                    continue;
                }

                emitters.Add(new RuntimeEmitter
                {
                    Name = em.Name,
                    TextureId = tex,
                    Color = new Vector4(em.ConstantColor0.X, em.ConstantColor0.Y, em.ConstantColor0.Z, Math.Clamp(em.ConstantColor0.W, 0.0f, 1.0f)),
                    Color1 = new Vector4(em.ConstantColor1.X, em.ConstantColor1.Y, em.ConstantColor1.Z, Math.Clamp(em.ConstantColor1.W, 0.0f, 1.0f)),
                    Color0Keys = em.Color0Keys.Length != 0 ? ToOpenTkKeys(em.Color0Keys) : Array.Empty<Vector4>(),
                    Alpha0Keys = em.Alpha0Keys.Length != 0 ? ToOpenTkKeys(em.Alpha0Keys) : Array.Empty<Vector4>(),
                    Color0KeyCount = em.Color0KeyCount,
                    Alpha0KeyCount = em.Alpha0KeyCount,
                    SpawnRate = 18f + (float)rng.NextDouble() * 24f,
                    ParticleLife = 0.9f + (float)rng.NextDouble() * 1.2f,
                    ParticleSize = 0.08f + (float)rng.NextDouble() * 0.24f,
                    Additive = true
                });
            }

	            if (emitters.Count == 0)
	            {
	                MessageHandler.Instance.AddMessage(MessageType.WARNING, $"[PTCL] No drawable emitters in '{ptclPath}'.");
	            }

	            // BFRES mesh is loaded above (independent of BNTX).
	        }

        private static Vector4[] ToOpenTkKeys(System.Numerics.Vector4[] keys)
        {
            if (keys == null || keys.Length == 0)
            {
                return Array.Empty<Vector4>();
            }

            var converted = new Vector4[keys.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                var k = keys[i];
                converted[i] = new Vector4(k.X, k.Y, k.Z, k.W);
            }
            return converted;
        }

        private int GetOrCreateGlTexture(byte[] bntxBytes, string textureName)
        {
            if (glTextureByName.TryGetValue(textureName, out var existing))
            {
                return existing;
            }

            if (!BNTX.TryLoadFromBytes(bntxBytes, textureName, out Bitmap bitmap, out var error))
            {
                if (!string.IsNullOrWhiteSpace(error))
                {
                    MessageHandler.Instance.AddMessage(MessageType.WARNING, $"[PTCL] Failed decoding embedded texture '{textureName}': {error}");
                }
                return 0;
            }

            int id = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, id);

            var bmp = bitmap;
            var imgPixelFormat = bmp.PixelFormat == System.Drawing.Imaging.PixelFormat.Format32bppArgb
                ? bmp.PixelFormat
                : System.Drawing.Imaging.PixelFormat.Format32bppArgb;
            if (bmp.PixelFormat != System.Drawing.Imaging.PixelFormat.Format32bppArgb)
            {
                var converted = new Bitmap(bmp.Width, bmp.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(converted))
                {
                    g.DrawImage(bmp, 0, 0, bmp.Width, bmp.Height);
                }
                bmp.Dispose();
                bmp = converted;
            }

            BitmapData bitmapData = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, imgPixelFormat);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, bitmapData.Width, bitmapData.Height, 0, OpenTK.Graphics.OpenGL4.PixelFormat.Bgra, PixelType.UnsignedByte, bitmapData.Scan0);
            bmp.UnlockBits(bitmapData);

            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

            bmp.Dispose();

            glTextureByName[textureName] = id;
            return id;
        }

        public void Update(float dt)
        {
            if (!setupComplete || disposed)
            {
                return;
            }

            var origin = Transform.Position;
            float scale = Transform.Scale.X;
            if (scale <= 0) scale = 1.0f;
            foreach (var em in emitters)
            {
                em.Accumulator += dt * em.SpawnRate;
                int spawn = (int)em.Accumulator;
                if (spawn > 0)
                {
                    em.Accumulator -= spawn;
                    for (int i = 0; i < spawn; i++)
                    {
                        if (em.Particles.Count > 768)
                        {
                            break;
                        }

                        var dir = RandomUnitSphere();
                        var p = new RuntimeParticle
                        {
                            Pos = origin + dir * (0.10f + (float)rng.NextDouble() * 0.25f) * scale,
                            Vel = new Vector3(
                                (float)(rng.NextDouble() * 2 - 1) * 0.25f,
                                0.15f + (float)rng.NextDouble() * 0.35f,
                                (float)(rng.NextDouble() * 2 - 1) * 0.25f),
                            Life = em.ParticleLife,
                            MaxLife = em.ParticleLife,
                            Size = em.ParticleSize * scale,
                            Rot = (float)rng.NextDouble() * MathF.PI * 2f
                        };
                        em.Particles.Add(p);
                    }
                }

                for (int i = em.Particles.Count - 1; i >= 0; i--)
                {
                    var p = em.Particles[i];
                    p.Life -= dt;
                    if (p.Life <= 0)
                    {
                        em.Particles.RemoveAt(i);
                        continue;
                    }
                    p.Pos += p.Vel * dt;
                    p.Vel *= MathF.Pow(0.15f, dt);
                    p.Rot += dt * 0.6f;
                    em.Particles[i] = p;
                }
            }
        }

        private Vector3 RandomUnitSphere()
        {
            float z = (float)(rng.NextDouble() * 2 - 1);
            float t = (float)(rng.NextDouble() * Math.PI * 2);
            float r = MathF.Sqrt(MathF.Max(0.0f, 1.0f - z * z));
            return new Vector3(r * MathF.Cos(t), z, r * MathF.Sin(t));
        }

	        public override void Draw(Matrix4 view, Matrix4 proj)
	        {
	            if (!RenderOptions.ParticlePass || disposed)
	            {
	                return;
	            }

	            if (bfresMesh != null)
	            {
	                var t = Transform;
                    var scale = t.Scale;
                    if (scale.LengthSquared < 0.000001f)
                    {
                        scale = Vector3.One;
                    }
	                Matrix4 model =
	                    Matrix4.CreateTranslation(t.Position) *
	                    Matrix4.CreateFromQuaternion(t.Rotation) *
	                    Matrix4.CreateScale(scale);
	                bfresMesh.Draw(model, view, proj);
	            }

	            if (emitters.Count == 0)
	            {
	                return;
	            }

            batchByTexture.Clear();
            foreach (var em in emitters)
            {
                if (em.TextureId == 0 || em.Particles.Count == 0)
                {
                    continue;
                }

                if (!batchByTexture.TryGetValue(em.TextureId, out var list))
                {
                    list = new List<ParticleRenderer.ParticleInstance>(capacity: Math.Min(256, em.Particles.Count));
                    batchByTexture[em.TextureId] = list;
                }

                for (int i = 0; i < em.Particles.Count; i++)
                {
                    var p = em.Particles[i];
                    float t = 1.0f - Math.Clamp(p.Life / Math.Max(0.0001f, p.MaxLife), 0.0f, 1.0f);
                    var c = EvaluateColor(em, t);
                    list.Add(new ParticleRenderer.ParticleInstance(p.Pos, p.Size, c, p.Rot));
                }
            }

            foreach (var kv in batchByTexture)
            {
                ParticleRenderer.DrawBatch(kv.Value, kv.Key, view, proj, additiveBlend: true);
            }
        }

        private static Vector4 EvaluateColor(RuntimeEmitter emitter, float t)
        {
            t = Math.Clamp(t, 0.0f, 1.0f);
            Vector4 color = emitter.Color;
            if (emitter.Color0KeyCount != 0 && emitter.Color0Keys.Length == 8)
            {
                color = EvaluateRgbKeys(emitter.Color0Keys, t, fallback: color);
            }

            if (emitter.Alpha0KeyCount != 0 && emitter.Alpha0Keys.Length == 8)
            {
                // Alpha key table stores alpha in X, time in W (following Switch-Toolbox naming).
                float a = EvaluateScalarKeys(emitter.Alpha0Keys, t, fallback: color.W);
                color.W = a;
            }

            return color;
        }

        private static Vector4 EvaluateRgbKeys(Vector4[] keys, float t, Vector4 fallback)
        {
            float bestT0 = -1;
            float bestT1 = 2;
            Vector4 k0 = fallback;
            Vector4 k1 = fallback;
            for (int i = 0; i < 8; i++)
            {
                float kt = keys[i].W;
                if (kt <= t && kt >= bestT0)
                {
                    bestT0 = kt;
                    k0 = keys[i];
                }
                if (kt >= t && kt <= bestT1)
                {
                    bestT1 = kt;
                    k1 = keys[i];
                }
            }

            if (bestT0 < 0 || bestT1 > 1.5f || Math.Abs(bestT1 - bestT0) < 1e-6f)
            {
                return new Vector4(k0.X, k0.Y, k0.Z, fallback.W);
            }

            float a = (t - bestT0) / Math.Max(1e-6f, bestT1 - bestT0);
            float r = MathHelper.Lerp(k0.X, k1.X, a);
            float g = MathHelper.Lerp(k0.Y, k1.Y, a);
            float b = MathHelper.Lerp(k0.Z, k1.Z, a);
            return new Vector4(r, g, b, fallback.W);
        }

        private static float EvaluateScalarKeys(Vector4[] keys, float t, float fallback)
        {
            float bestT0 = -1;
            float bestT1 = 2;
            float v0 = fallback;
            float v1 = fallback;
            for (int i = 0; i < 8; i++)
            {
                float kt = keys[i].W;
                float val = keys[i].X;
                if (kt <= t && kt >= bestT0)
                {
                    bestT0 = kt;
                    v0 = val;
                }
                if (kt >= t && kt <= bestT1)
                {
                    bestT1 = kt;
                    v1 = val;
                }
            }

            if (bestT0 < 0 || bestT1 > 1.5f || Math.Abs(bestT1 - bestT0) < 1e-6f)
            {
                return v0;
            }

            float a2 = (t - bestT0) / Math.Max(1e-6f, bestT1 - bestT0);
            return MathHelper.Lerp(v0, v1, a2);
        }

	        public void Dispose()
	        {
	            if (disposed)
	            {
	                return;
	            }

	            disposed = true;
	            try { bfresMesh?.Dispose(); } catch { }
	            bfresMesh = null;
	            foreach (var kv in glTextureByName)
	            {
	                try { GL.DeleteTexture(kv.Value); } catch { }
	            }
            glTextureByName.Clear();
            emitters.Clear();
            batchByTexture.Clear();
        }
    }
}
