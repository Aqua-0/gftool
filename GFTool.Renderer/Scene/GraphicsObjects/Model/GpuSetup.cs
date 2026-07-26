using GFTool.Renderer.Core;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System;
using System.Linq;

namespace GFTool.Renderer.Scene.GraphicsObjects
{
    public partial class Model
    {
        private int gpuSetupIndex = -1;
        private bool gpuSetupComplete;

        internal bool IsGpuSetupComplete => gpuSetupComplete;

        internal void BeginGpuSetup()
        {
            if (gpuSetupComplete || gpuSetupIndex >= 0)
            {
                return;
            }

            int submeshCnt = Positions.Count;
            VAOs = new int[submeshCnt];
            VBOs = new int[submeshCnt];
            EBOs = new int[Indices.Count()];
            blendIndexOffsets = new int[submeshCnt];
            blendIndexByteSizes = new int[submeshCnt];
            EnsureSubmeshVisibilitySize(submeshCnt);
            gpuSetupIndex = 0;
        }

        internal bool StepGpuSetup()
        {
            if (gpuSetupComplete)
            {
                return true;
            }

            if (gpuSetupIndex < 0)
            {
                BeginGpuSetup();
            }

            if (VAOs == null || VBOs == null || EBOs == null)
            {
                return true;
            }

            if (gpuSetupIndex >= VAOs.Length)
            {
                FinalizeGpuSetup();
                return true;
            }

            SetupSubmeshGpu(gpuSetupIndex);
            gpuSetupIndex++;
            if (gpuSetupIndex >= VAOs.Length)
            {
                FinalizeGpuSetup();
                return true;
            }

            return false;
        }

        private void FinalizeGpuSetup()
        {
            gpuSetupComplete = true;
            gpuSetupIndex = VAOs?.Length ?? -1;

            // Grab any errors from setup.
            ErrorCode error;
            while ((error = GL.GetError()) != ErrorCode.NoError)
            {
                MessageHandler.Instance.AddMessage(MessageType.ERROR, $"Error in model \"{Name}\": {error}");
            }

            base.Setup();
        }

        private void SetupSubmeshGpu(int i)
        {
            ClearGlErrors();

            int posCount = Positions[i].Length;
            int normCount = Normals[i].Length;
            int uvCount = UVs[i].Length;
            int uv2Count = UVs2[i].Length;
            int colorCount = Colors[i].Length;
            int tangentCount = Tangents[i].Length;
            int binormalCount = Binormals[i].Length;
            int blendIndexCount = BlendIndicies[i].Length;
            int blendWeightCount = BlendWeights[i].Length;
            int indexCount = Indices[i].Length;

            // VAO
            GL.GenVertexArrays(1, out VAOs[i]);
            GL.BindVertexArray(VAOs[i]);
            LogGlErrors(
                i,
                "BindVertexArray",
                posCount,
                normCount,
                uvCount,
                uv2Count,
                colorCount,
                tangentCount,
                binormalCount,
                blendIndexCount,
                blendWeightCount,
                indexCount);

            // Sizes
            var vertSize = posCount * Vector3.SizeInBytes;
            var normSize = normCount * Vector3.SizeInBytes;
            var uvSize = uvCount * Vector2.SizeInBytes;
            var uv2Size = uv2Count * Vector2.SizeInBytes;
            var colorSize = colorCount * Vector4.SizeInBytes;
            var tangentSize = tangentCount * Vector4.SizeInBytes;
            var binormalSize = binormalCount * Vector3.SizeInBytes;
            var blendIndexSize = blendIndexCount * Vector4.SizeInBytes;
            var blendWeightSize = blendWeightCount * Vector4.SizeInBytes;
            var totalSize = vertSize + normSize + uvSize + uv2Size + colorSize + tangentSize + binormalSize + blendIndexSize + blendWeightSize;

            blendIndexOffsets[i] = vertSize + normSize + uvSize + uv2Size + colorSize + tangentSize + binormalSize;
            blendIndexByteSizes[i] = blendIndexSize;

            // VBO
            GL.GenBuffers(1, out VBOs[i]);
            GL.BindBuffer(BufferTarget.ArrayBuffer, VBOs[i]);
            GL.BufferData(BufferTarget.ArrayBuffer, totalSize, IntPtr.Zero, BufferUsageHint.StaticDraw);
            LogGlErrors(
                i,
                $"BufferData(ArrayBuffer,total={totalSize})",
                posCount,
                normCount,
                uvCount,
                uv2Count,
                colorCount,
                tangentCount,
                binormalCount,
                blendIndexCount,
                blendWeightCount,
                indexCount);

            // Upload vertex data to the buffer
            IntPtr offset = IntPtr.Zero;
            GL.BufferSubData(BufferTarget.ArrayBuffer, offset, vertSize, ToUnmanagedByteArray(Positions[i])); offset += vertSize;
            LogGlErrors(i, "BufferSubData(Position)", posCount, normCount, uvCount, uv2Count, colorCount, tangentCount, binormalCount, blendIndexCount, blendWeightCount, indexCount);
            GL.BufferSubData(BufferTarget.ArrayBuffer, offset, normSize, ToUnmanagedByteArray(Normals[i])); offset += normSize;
            LogGlErrors(i, "BufferSubData(Normal)", posCount, normCount, uvCount, uv2Count, colorCount, tangentCount, binormalCount, blendIndexCount, blendWeightCount, indexCount);
            GL.BufferSubData(BufferTarget.ArrayBuffer, offset, uvSize, ToUnmanagedByteArray(UVs[i])); offset += uvSize;
            LogGlErrors(i, "BufferSubData(UV0)", posCount, normCount, uvCount, uv2Count, colorCount, tangentCount, binormalCount, blendIndexCount, blendWeightCount, indexCount);
            GL.BufferSubData(BufferTarget.ArrayBuffer, offset, uv2Size, ToUnmanagedByteArray(UVs2[i])); offset += uv2Size;
            LogGlErrors(i, "BufferSubData(UV1)", posCount, normCount, uvCount, uv2Count, colorCount, tangentCount, binormalCount, blendIndexCount, blendWeightCount, indexCount);
            GL.BufferSubData(BufferTarget.ArrayBuffer, offset, colorSize, ToUnmanagedByteArray(Colors[i])); offset += colorSize;
            LogGlErrors(i, "BufferSubData(Color)", posCount, normCount, uvCount, uv2Count, colorCount, tangentCount, binormalCount, blendIndexCount, blendWeightCount, indexCount);
            GL.BufferSubData(BufferTarget.ArrayBuffer, offset, tangentSize, ToUnmanagedByteArray(Tangents[i])); offset += tangentSize;
            LogGlErrors(i, "BufferSubData(Tangent)", posCount, normCount, uvCount, uv2Count, colorCount, tangentCount, binormalCount, blendIndexCount, blendWeightCount, indexCount);
            GL.BufferSubData(BufferTarget.ArrayBuffer, offset, binormalSize, ToUnmanagedByteArray(Binormals[i])); offset += binormalSize;
            LogGlErrors(i, "BufferSubData(Binormal)", posCount, normCount, uvCount, uv2Count, colorCount, tangentCount, binormalCount, blendIndexCount, blendWeightCount, indexCount);
            GL.BufferSubData(BufferTarget.ArrayBuffer, offset, blendIndexSize, ToUnmanagedByteArray(BlendIndicies[i])); offset += blendIndexSize;
            LogGlErrors(i, "BufferSubData(BlendIndex)", posCount, normCount, uvCount, uv2Count, colorCount, tangentCount, binormalCount, blendIndexCount, blendWeightCount, indexCount);
            GL.BufferSubData(BufferTarget.ArrayBuffer, offset, blendWeightSize, ToUnmanagedByteArray(BlendWeights[i])); offset += blendWeightSize;
            LogGlErrors(i, "BufferSubData(BlendWeight)", posCount, normCount, uvCount, uv2Count, colorCount, tangentCount, binormalCount, blendIndexCount, blendWeightCount, indexCount);

            // EBO (indices)
            GL.GenBuffers(1, out EBOs[i]);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, EBOs[i]);
            GL.BufferData(BufferTarget.ElementArrayBuffer, Indices[i].Length * sizeof(uint), Indices[i].ToArray(), BufferUsageHint.StaticDraw);
            LogGlErrors(i, $"BufferData(ElementArray,indexBytes={Indices[i].Length * sizeof(uint)})", posCount, normCount, uvCount, uv2Count, colorCount, tangentCount, binormalCount, blendIndexCount, blendWeightCount, indexCount);

            offset = IntPtr.Zero;

            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, Vector3.SizeInBytes, offset); offset += vertSize;
            GL.EnableVertexAttribArray(0);
            LogGlErrors(i, "VertexAttrib(0)", posCount, normCount, uvCount, uv2Count, colorCount, tangentCount, binormalCount, blendIndexCount, blendWeightCount, indexCount);

            GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, Vector3.SizeInBytes, offset); offset += normSize;
            GL.EnableVertexAttribArray(1);
            LogGlErrors(i, "VertexAttrib(1)", posCount, normCount, uvCount, uv2Count, colorCount, tangentCount, binormalCount, blendIndexCount, blendWeightCount, indexCount);

            GL.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, Vector2.SizeInBytes, offset); offset += uvSize;
            GL.EnableVertexAttribArray(2);
            LogGlErrors(i, "VertexAttrib(2)", posCount, normCount, uvCount, uv2Count, colorCount, tangentCount, binormalCount, blendIndexCount, blendWeightCount, indexCount);

            GL.VertexAttribPointer(8, 2, VertexAttribPointerType.Float, false, Vector2.SizeInBytes, offset); offset += uv2Size;
            GL.EnableVertexAttribArray(8);
            LogGlErrors(i, "VertexAttrib(8)", posCount, normCount, uvCount, uv2Count, colorCount, tangentCount, binormalCount, blendIndexCount, blendWeightCount, indexCount);

            GL.VertexAttribPointer(3, 4, VertexAttribPointerType.Float, false, Vector4.SizeInBytes, offset); offset += colorSize;
            GL.EnableVertexAttribArray(3);
            LogGlErrors(i, "VertexAttrib(3)", posCount, normCount, uvCount, uv2Count, colorCount, tangentCount, binormalCount, blendIndexCount, blendWeightCount, indexCount);

            GL.VertexAttribPointer(4, 4, VertexAttribPointerType.Float, false, Vector4.SizeInBytes, offset); offset += tangentSize;
            GL.EnableVertexAttribArray(4);
            LogGlErrors(i, "VertexAttrib(4)", posCount, normCount, uvCount, uv2Count, colorCount, tangentCount, binormalCount, blendIndexCount, blendWeightCount, indexCount);

            GL.VertexAttribPointer(5, 3, VertexAttribPointerType.Float, false, Vector3.SizeInBytes, offset); offset += binormalSize;
            GL.EnableVertexAttribArray(5);
            LogGlErrors(i, "VertexAttrib(5)", posCount, normCount, uvCount, uv2Count, colorCount, tangentCount, binormalCount, blendIndexCount, blendWeightCount, indexCount);

            GL.VertexAttribPointer(6, 4, VertexAttribPointerType.Float, false, Vector4.SizeInBytes, offset); offset += blendIndexSize;
            GL.EnableVertexAttribArray(6);
            LogGlErrors(i, "VertexAttrib(6)", posCount, normCount, uvCount, uv2Count, colorCount, tangentCount, binormalCount, blendIndexCount, blendWeightCount, indexCount);

            GL.VertexAttribPointer(7, 4, VertexAttribPointerType.Float, false, Vector4.SizeInBytes, offset); offset += blendWeightSize;
            GL.EnableVertexAttribArray(7);
            LogGlErrors(i, "VertexAttrib(7)", posCount, normCount, uvCount, uv2Count, colorCount, tangentCount, binormalCount, blendIndexCount, blendWeightCount, indexCount);

            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            GL.BindVertexArray(0);
        }

        private static void ClearGlErrors()
        {
            // Per-call GL error probing is intentionally diagnostic-only. GL.GetError can
            // serialize with the driver and is far too expensive for normal bulk uploads.
            if (!MessageHandler.Instance.DebugLogsEnabled)
            {
                return;
            }

            while (GL.GetError() != ErrorCode.NoError)
            {
            }
        }

        private void LogGlErrors(
            int submeshIndex,
            string step,
            int posCount,
            int normCount,
            int uvCount,
            int uv2Count,
            int colorCount,
            int tangentCount,
            int binormalCount,
            int blendIndexCount,
            int blendWeightCount,
            int indexCount)
        {
            if (!MessageHandler.Instance.DebugLogsEnabled)
            {
                return;
            }

            ErrorCode error;
            while ((error = GL.GetError()) != ErrorCode.NoError)
            {
                MessageHandler.Instance.AddMessage(
                    MessageType.ERROR,
                    $"[GLSetup] model=\"{Name}\" submesh={submeshIndex} step={step} error={error} counts(pos={posCount},norm={normCount},uv0={uvCount},uv1={uv2Count},color={colorCount},tan={tangentCount},binorm={binormalCount},blendIdx={blendIndexCount},blendW={blendWeightCount},indices={indexCount})");
            }
        }
    }
}
