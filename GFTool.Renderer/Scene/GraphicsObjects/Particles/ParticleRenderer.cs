using GFTool.Renderer.Core;
using GFTool.Renderer.Core.Graphics;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace GFTool.Renderer.Scene.GraphicsObjects.Particles
{
    internal static class ParticleRenderer
    {
        private static int vao;
        private static int vbo;
        private static int instanceVbo;
        private static bool initialized;

        // Per-vertex: corner.xy, uv.xy
        private static readonly float[] quadVerts =
        {
            -0.5f, -0.5f, 0f, 0f,
             0.5f, -0.5f, 1f, 0f,
             0.5f,  0.5f, 1f, 1f,
            -0.5f, -0.5f, 0f, 0f,
             0.5f,  0.5f, 1f, 1f,
            -0.5f,  0.5f, 0f, 1f,
        };

        [StructLayout(LayoutKind.Sequential)]
        internal readonly struct ParticleInstance
        {
            public readonly Vector3 Center;
            public readonly float Size;
            public readonly Vector4 Color;
            public readonly float Rotation;

            public ParticleInstance(Vector3 center, float size, Vector4 color, float rotation)
            {
                Center = center;
                Size = size;
                Color = color;
                Rotation = rotation;
            }
        }

        internal static void EnsureInitialized()
        {
            if (initialized)
            {
                return;
            }

            vao = GL.GenVertexArray();
            vbo = GL.GenBuffer();
            instanceVbo = GL.GenBuffer();

            GL.BindVertexArray(vao);

            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, quadVerts.Length * sizeof(float), quadVerts, BufferUsageHint.StaticDraw);

            int stride = 4 * sizeof(float);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, stride, 0);
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, 2 * sizeof(float));

            GL.BindBuffer(BufferTarget.ArrayBuffer, instanceVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, 1, IntPtr.Zero, BufferUsageHint.StreamDraw);

            int instStride = Marshal.SizeOf<ParticleInstance>();
            int offset = 0;
            GL.EnableVertexAttribArray(2);
            GL.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, instStride, offset);
            GL.VertexAttribDivisor(2, 1);
            offset += 3 * sizeof(float);

            GL.EnableVertexAttribArray(3);
            GL.VertexAttribPointer(3, 1, VertexAttribPointerType.Float, false, instStride, offset);
            GL.VertexAttribDivisor(3, 1);
            offset += 1 * sizeof(float);

            GL.EnableVertexAttribArray(4);
            GL.VertexAttribPointer(4, 4, VertexAttribPointerType.Float, false, instStride, offset);
            GL.VertexAttribDivisor(4, 1);
            offset += 4 * sizeof(float);

            GL.EnableVertexAttribArray(5);
            GL.VertexAttribPointer(5, 1, VertexAttribPointerType.Float, false, instStride, offset);
            GL.VertexAttribDivisor(5, 1);

            GL.BindVertexArray(0);
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);

            initialized = true;
        }

        internal static void DrawBatch(
            IReadOnlyList<ParticleInstance> instances,
            int textureId,
            Matrix4 view,
            Matrix4 proj,
            bool additiveBlend)
        {
            if (instances == null || instances.Count == 0 || textureId == 0)
            {
                return;
            }

            EnsureInitialized();

            var shader = ShaderPool.Instance.GetShader("particle");
            if (shader == null)
            {
                return;
            }

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GL.Enable(EnableCap.Blend);
            if (additiveBlend)
            {
                GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One);
            }
            else
            {
                GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            }
            GL.Enable(EnableCap.DepthTest);
            GL.DepthMask(false);

            shader.Bind();
            shader.SetMatrix4("view", view);
            shader.SetMatrix4("proj", proj);

            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, textureId);
            shader.SetInt("Tex", 0);

            GL.BindVertexArray(vao);

            int sizeBytes = instances.Count * Marshal.SizeOf<ParticleInstance>();
            GL.BindBuffer(BufferTarget.ArrayBuffer, instanceVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, sizeBytes, instances.ToArray(), BufferUsageHint.StreamDraw);
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);

            GL.DrawArraysInstanced(PrimitiveType.Triangles, 0, 6, instances.Count);

            GL.BindVertexArray(0);
            shader.Unbind();

            GL.DepthMask(true);
            GL.Disable(EnableCap.Blend);
        }
    }
}
