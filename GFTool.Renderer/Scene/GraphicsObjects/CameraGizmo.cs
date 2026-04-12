using GFTool.Renderer.Core.Graphics;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;

namespace GFTool.Renderer.Scene.GraphicsObjects
{
    public sealed class CameraGizmo : RefObject
    {
        private int vao;
        private int vbo;
        private int vertexCount;
        private Shader? shader;

        private bool pendingBufferUpdate = true;

        public bool IsVisible { get; set; } = true;
        public Vector3 Color { get; set; } = new Vector3(1.0f, 0.75f, 0.2f);

        public float FovDegrees { get; private set; } = 45.0f;
        public float AspectRatio { get; private set; } = 16.0f / 9.0f;

        public void SetFrustum(float fovDegrees, float aspectRatio)
        {
            FovDegrees = Math.Clamp(fovDegrees, 1.0f, 175.0f);
            AspectRatio = Math.Clamp(aspectRatio, 0.01f, 100.0f);
            pendingBufferUpdate = true;
        }

        public override void Setup()
        {
            shader = ShaderPool.Instance.GetShader("Grid");
            if (shader == null)
            {
                return;
            }

            var vertices = BuildVertices();
            vertexCount = vertices.Length;

            vao = GL.GenVertexArray();
            vbo = GL.GenBuffer();

            GL.BindVertexArray(vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * Vector3.SizeInBytes, vertices, BufferUsageHint.DynamicDraw);

            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, Vector3.SizeInBytes, 0);
            GL.EnableVertexAttribArray(0);

            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            GL.BindVertexArray(0);

            pendingBufferUpdate = false;
            base.Setup();
        }

        public override void Draw(Matrix4 view, Matrix4 proj)
        {
            if (!IsVisible || shader == null || vertexCount == 0)
            {
                return;
            }

            if (pendingBufferUpdate)
            {
                TryUpdateBuffer();
            }

            shader.Bind();
            shader.SetMatrix4("model", BuildModelMatrix());
            shader.SetMatrix4("view", view);
            shader.SetMatrix4("projection", proj);
            shader.SetVector3("gridColor", Color);

            GL.BindVertexArray(vao);
            GL.DrawArrays(PrimitiveType.Lines, 0, vertexCount);
            GL.BindVertexArray(0);
        }

        private void TryUpdateBuffer()
        {
            if (vao == 0 || vbo == 0)
            {
                return;
            }

            var vertices = BuildVertices();
            vertexCount = vertices.Length;

            GL.BindVertexArray(vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * Vector3.SizeInBytes, vertices, BufferUsageHint.DynamicDraw);
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            GL.BindVertexArray(0);

            pendingBufferUpdate = false;
        }

        private Vector3[] BuildVertices()
        {
            var lines = new List<Vector3>(64);

            // Axes (local). In this camera convention, forward at (pitch=0,yaw=0) is +X.
            const float axis = 0.25f;
            lines.Add(Vector3.Zero); lines.Add(new Vector3(axis, 0, 0));
            lines.Add(Vector3.Zero); lines.Add(new Vector3(0, axis, 0));
            lines.Add(Vector3.Zero); lines.Add(new Vector3(0, 0, axis));

            // Simple frustum (local forward +X).
            float nearX = 0.20f;
            float farX = 0.65f;
            float fovRad = MathHelper.DegreesToRadians(FovDegrees);
            float tan = MathF.Tan(fovRad * 0.5f);

            float nearH = tan * nearX;
            float nearW = nearH * AspectRatio;
            float farH = tan * farX;
            float farW = farH * AspectRatio;

            var n0 = new Vector3(nearX, -nearH, -nearW);
            var n1 = new Vector3(nearX, -nearH, +nearW);
            var n2 = new Vector3(nearX, +nearH, +nearW);
            var n3 = new Vector3(nearX, +nearH, -nearW);

            var f0 = new Vector3(farX, -farH, -farW);
            var f1 = new Vector3(farX, -farH, +farW);
            var f2 = new Vector3(farX, +farH, +farW);
            var f3 = new Vector3(farX, +farH, -farW);

            // Near rectangle
            AddEdge(lines, n0, n1);
            AddEdge(lines, n1, n2);
            AddEdge(lines, n2, n3);
            AddEdge(lines, n3, n0);

            // Far rectangle
            AddEdge(lines, f0, f1);
            AddEdge(lines, f1, f2);
            AddEdge(lines, f2, f3);
            AddEdge(lines, f3, f0);

            // Connectors
            AddEdge(lines, n0, f0);
            AddEdge(lines, n1, f1);
            AddEdge(lines, n2, f2);
            AddEdge(lines, n3, f3);

            return lines.ToArray();
        }

        private static void AddEdge(List<Vector3> lines, Vector3 a, Vector3 b)
        {
            lines.Add(a);
            lines.Add(b);
        }

        private Matrix4 BuildModelMatrix()
        {
            var t = Matrix4.CreateTranslation(Transform.Position);
            var r = Matrix4.CreateFromQuaternion(Transform.Rotation);
            var s = Matrix4.CreateScale(Transform.Scale == Vector3.Zero ? Vector3.One : Transform.Scale);
            return t * r * s;
        }
    }
}
