using GFTool.Renderer.Core.Graphics;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System;

namespace GFTool.Renderer.Scene.GraphicsObjects
{
    public sealed class HeightFieldMesh : RefObject, IDisposable
    {
        private readonly Vector3[] vertices;
        private int vao;
        private int vbo;
        private int vertexCount;
        private Shader? shader;

        public Vector3 Color { get; set; } = new Vector3(0.15f, 0.85f, 0.25f);

        public HeightFieldMesh(Vector3[] lineVertices)
        {
            vertices = lineVertices ?? Array.Empty<Vector3>();
        }

        public override void Setup()
        {
            shader = ShaderPool.Instance.GetShader("Grid");
            if (shader == null || vertices.Length == 0)
            {
                return;
            }

            vertexCount = vertices.Length;

            vao = GL.GenVertexArray();
            vbo = GL.GenBuffer();

            GL.BindVertexArray(vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * Vector3.SizeInBytes, vertices, BufferUsageHint.StaticDraw);

            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, Vector3.SizeInBytes, 0);
            GL.EnableVertexAttribArray(0);

            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            GL.BindVertexArray(0);

            base.Setup();
        }

        public override void Draw(Matrix4 view, Matrix4 proj)
        {
            if (shader == null || vertexCount == 0)
            {
                return;
            }

            shader.Bind();
            shader.SetMatrix4("model", Matrix4.Identity);
            shader.SetMatrix4("view", view);
            shader.SetMatrix4("projection", proj);
            shader.SetVector3("gridColor", Color);

            GL.BindVertexArray(vao);
            GL.DrawArrays(PrimitiveType.Lines, 0, vertexCount);
            GL.BindVertexArray(0);
        }

        public void Dispose()
        {
            if (vbo != 0)
            {
                try { GL.DeleteBuffer(vbo); } catch { }
                vbo = 0;
            }

            if (vao != 0)
            {
                try { GL.DeleteVertexArray(vao); } catch { }
                vao = 0;
            }
        }
    }
}
