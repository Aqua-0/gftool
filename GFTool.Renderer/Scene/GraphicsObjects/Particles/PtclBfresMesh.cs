using GFTool.Renderer.Core;
using GFTool.Renderer.Core.Graphics;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using Syroot.NintenTools.NSW.Bfres;
using Syroot.NintenTools.NSW.Bfres.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Vector4F = Syroot.Maths.Vector4F;
using BfresPrimitiveType = Syroot.NintenTools.NSW.Bfres.GFX.PrimitiveType;
using BfresModel = Syroot.NintenTools.NSW.Bfres.Model;
using BfresShape = Syroot.NintenTools.NSW.Bfres.Shape;
using BfresVertexBuffer = Syroot.NintenTools.NSW.Bfres.VertexBuffer;
using BfresSkeleton = Syroot.NintenTools.NSW.Bfres.Skeleton;
using BfresBone = Syroot.NintenTools.NSW.Bfres.Bone;
using BfresBoneFlagsRotation = Syroot.NintenTools.NSW.Bfres.BoneFlagsRotation;

namespace GFTool.Renderer.Scene.GraphicsObjects.Particles
{
    internal sealed class PtclBfresMesh : IDisposable
    {
        private const int FloatsPerVertex = 3 + 4 + 2;
        private const int StrideBytes = FloatsPerVertex * sizeof(float);

        private readonly byte[] bfresBytes;
        private Shader? shader;
        private int vao;
        private int vbo;
        private int ebo;
        private int indexCount;
        private float autoScale = 1.0f;
        private bool loggedMissingPos;
        private bool setupComplete;
        private bool disposed;

        public PtclBfresMesh(byte[] bfresBytes)
        {
            this.bfresBytes = bfresBytes ?? Array.Empty<byte>();
        }

        public void Setup()
        {
            if (setupComplete || disposed)
            {
                return;
            }
            setupComplete = true;

            if (bfresBytes.Length < 64)
            {
                return;
            }

            shader = ShaderPool.Instance.GetShader("PtclMesh");
            if (shader == null)
            {
                return;
            }

            if (!TryBuildMesh(out var vertexData, out var indices))
            {
                MessageHandler.Instance.AddMessage(MessageType.WARNING, "[PTCL] BFRES mesh had no drawable geometry.");
                return;
            }

            if (vertexData.Length == 0 || indices.Length == 0)
            {
                return;
            }

            indexCount = indices.Length;

            vao = GL.GenVertexArray();
            vbo = GL.GenBuffer();
            ebo = GL.GenBuffer();

            GL.BindVertexArray(vao);

            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, vertexData.Length * sizeof(float), vertexData, BufferUsageHint.StaticDraw);

            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, StrideBytes, 0);

            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 4, VertexAttribPointerType.Float, false, StrideBytes, 3 * sizeof(float));

            GL.EnableVertexAttribArray(2);
            GL.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, StrideBytes, (3 + 4) * sizeof(float));

            GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo);
            GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * sizeof(uint), indices, BufferUsageHint.StaticDraw);

            GL.BindVertexArray(0);
        }

        public void Draw(Matrix4 model, Matrix4 view, Matrix4 proj)
        {
            if (!setupComplete || disposed || shader == null || vao == 0 || indexCount == 0)
            {
                return;
            }

            if (autoScale != 1.0f && autoScale > 0.0f && !float.IsNaN(autoScale) && !float.IsInfinity(autoScale))
            {
                model = model * Matrix4.CreateScale(autoScale);
            }

            shader.Bind();
            shader.SetMatrix4("model", model);
            shader.SetMatrix4("view", view);
            shader.SetMatrix4("projection", proj);
            shader.SetVector4("uColor", new Vector4(RenderOptions.TeraColor.X, RenderOptions.TeraColor.Y, RenderOptions.TeraColor.Z, 0.85f));

            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.Enable(EnableCap.DepthTest);
            GL.Disable(EnableCap.CullFace);
            GL.DepthMask(false);

            GL.BindVertexArray(vao);
            GL.DrawElements(OpenTK.Graphics.OpenGL4.PrimitiveType.Triangles, indexCount, DrawElementsType.UnsignedInt, 0);
            GL.BindVertexArray(0);

            GL.DepthMask(true);
            GL.Enable(EnableCap.CullFace);
            GL.Disable(EnableCap.Blend);
        }

        private bool TryBuildMesh(out float[] vertexData, out uint[] indices)
        {
            vertexData = Array.Empty<float>();
            indices = Array.Empty<uint>();

            try
            {
                using var ms = new MemoryStream(bfresBytes, writable: false);
                var res = new ResFile(ms, leaveOpen: true);
                if (res.Models == null || res.Models.Count == 0)
                {
                    return false;
                }

                var posList = new List<Vector3>(capacity: 4096);
                var colList = new List<Vector4>(capacity: 4096);
                var uvList = new List<Vector2>(capacity: 4096);
                var inds = new List<uint>(capacity: 8192);
                int meshCount = 0;

                foreach (var mdl in res.Models)
                {
                    if (mdl == null || mdl.Shapes == null)
                    {
                        continue;
                    }

                    var skin = BuildSkinning(mdl.Skeleton);

                    foreach (var shape in mdl.Shapes)
                    {
                        if (shape == null || shape.Meshes == null || shape.Meshes.Count == 0)
                        {
                            continue;
                        }

                        var vertexBuffer = TryGetVertexBuffer(mdl, shape);
                        if (vertexBuffer == null)
                        {
                            continue;
                        }

                        var helper = new VertexBufferHelper(vertexBuffer, res.ByteOrder);
                        var pos = FindPositionAttrib(helper);
                        if (pos == null || pos.Data == null || pos.Data.Length == 0)
                        {
                            if (!loggedMissingPos)
                            {
                                loggedMissingPos = true;
                                string shapeName = shape.Name ?? "(unnamed)";
                                string attrs;
                                try
                                {
                                    var names = new List<string>();
                                    if (helper.Attributes != null)
                                    {
                                        for (int ai = 0; ai < helper.Attributes.Count && ai < 12; ai++)
                                        {
                                            var an = helper.Attributes[ai]?.Name;
                                            if (!string.IsNullOrWhiteSpace(an))
                                            {
                                                names.Add(an);
                                            }
                                        }
                                    }
                                    attrs = names.Count == 0 ? "(none)" : string.Join(", ", names);
                                }
                                catch
                                {
                                    attrs = "(error)";
                                }

                                MessageHandler.Instance.AddMessage(
                                    MessageType.WARNING,
                                    $"[PTCL] BFRES shape '{shapeName}' missing readable position attribute. attrs={attrs}");
                            }
                            continue;
                        }

                        var col = FindColorAttrib(helper);
                        var uv = FindUvAttrib(helper);
                        var boneIndexAttr = FindBoneIndexAttrib(helper);
                        var boneWeightAttr = FindBoneWeightAttrib(helper);

                        Matrix4 shapeRigidMatrix = GetShapeRigidMatrix(shape, skin);

                        int vertexBase = posList.Count;
                        int vertCount = pos.Data.Length;
                        for (int i = 0; i < vertCount; i++)
                        {
                            Vector4F v = pos.Data[i];
                            var p = new Vector3(v.X, v.Y, v.Z);
                            p = ApplySkinning(p, i, shape, shapeRigidMatrix, skin, boneIndexAttr, boneWeightAttr);
                            posList.Add(p);

                            if (col != null && col.Data != null && i < col.Data.Length)
                            {
                                var c = col.Data[i];
                                colList.Add(new Vector4(c.X, c.Y, c.Z, c.W));
                            }
                            else
                            {
                                colList.Add(Vector4.One);
                            }

                            if (uv != null && uv.Data != null && i < uv.Data.Length)
                            {
                                var t = uv.Data[i];
                                uvList.Add(new Vector2(t.X, t.Y));
                            }
                            else
                            {
                                uvList.Add(Vector2.Zero);
                            }
                        }

                        for (int meshIndex = 0; meshIndex < shape.Meshes.Count; meshIndex++)
                        {
                            var mesh = shape.Meshes[meshIndex];
                            if (mesh == null)
                            {
                                continue;
                            }

                            var meshIndices = mesh.GetIndices()?.ToArray() ?? Array.Empty<uint>();
                            if (meshIndices.Length == 0)
                            {
                                continue;
                            }

                            // mesh.FirstVertex is an offset into the referenced vertex buffer.
                            uint firstVertex = (uint)Math.Max(0, mesh.FirstVertex);

                            if (mesh.PrimitiveType == BfresPrimitiveType.Triangles)
                            {
                                int triCount = meshIndices.Length / 3;
                                for (int t = 0; t < triCount; t++)
                                {
                                    inds.Add((uint)vertexBase + firstVertex + meshIndices[t * 3 + 0]);
                                    inds.Add((uint)vertexBase + firstVertex + meshIndices[t * 3 + 1]);
                                    inds.Add((uint)vertexBase + firstVertex + meshIndices[t * 3 + 2]);
                                }
                            }
                            else if (mesh.PrimitiveType == BfresPrimitiveType.TriangleStrip)
                            {
                                AppendTriangleStrip(inds, meshIndices, (uint)vertexBase + firstVertex);
                            }
                            else
                            {
                                continue;
                            }

                            meshCount++;
                        }
                    }
                }

                var positions = posList.ToArray();
                indices = inds.ToArray();

                if (positions.Length != 0)
                {
                    var (min, max, radius) = ComputeBounds(positions);
                    autoScale = ComputeAutoScale(radius);

                    if (MathF.Abs(max.X - min.X) < 0.000001f || MathF.Abs(max.Y - min.Y) < 0.000001f || MathF.Abs(max.Z - min.Z) < 0.000001f)
                    {
                        MessageHandler.Instance.AddMessage(
                            MessageType.WARNING,
                            $"[PTCL] BFRES mesh bounds degenerate: min=({min.X:F3},{min.Y:F3},{min.Z:F3}) max=({max.X:F3},{max.Y:F3},{max.Z:F3}). ByteOrder={res.ByteOrder}");
                    }

                    MessageHandler.Instance.AddMessage(
                        MessageType.LOG,
                        $"[PTCL] BFRES mesh built meshes={meshCount} verts={positions.Length} inds={indices.Length} bounds=({min.X:F3},{min.Y:F3},{min.Z:F3})-({max.X:F3},{max.Y:F3},{max.Z:F3}) r={radius:F3} scale={autoScale:F4}");
                }

                if (positions.Length == 0 || colList.Count != positions.Length || uvList.Count != positions.Length)
                {
                    return false;
                }

                vertexData = new float[positions.Length * FloatsPerVertex];
                for (int i = 0; i < positions.Length; i++)
                {
                    int o = i * FloatsPerVertex;
                    var p = positions[i];
                    var c = colList[i];
                    var t = uvList[i];
                    vertexData[o + 0] = p.X;
                    vertexData[o + 1] = p.Y;
                    vertexData[o + 2] = p.Z;
                    vertexData[o + 3] = c.X;
                    vertexData[o + 4] = c.Y;
                    vertexData[o + 5] = c.Z;
                    vertexData[o + 6] = c.W;
                    vertexData[o + 7] = t.X;
                    vertexData[o + 8] = t.Y;
                }

                return vertexData.Length != 0 && indices.Length != 0;
            }
            catch (Exception ex)
            {
                MessageHandler.Instance.AddMessage(MessageType.WARNING, $"[PTCL] BFRES mesh parse failed: {ex.GetType().Name}");
                return false;
            }
        }

        private static (Vector3 Min, Vector3 Max, float Radius) ComputeBounds(Vector3[] vertices)
        {
            if (vertices == null || vertices.Length == 0)
            {
                return (Vector3.Zero, Vector3.Zero, 0.0f);
            }

            var min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
            for (int i = 0; i < vertices.Length; i++)
            {
                min = Vector3.ComponentMin(min, vertices[i]);
                max = Vector3.ComponentMax(max, vertices[i]);
            }

            var center = (min + max) * 0.5f;
            float r = 0.0f;
            for (int i = 0; i < vertices.Length; i++)
            {
                r = MathF.Max(r, (vertices[i] - center).Length);
            }
            return (min, max, r);
        }

        private static float ComputeAutoScale(float radius)
        {
            if (!(radius > 0.0f) || float.IsNaN(radius) || float.IsInfinity(radius))
            {
                return 1.0f;
            }

            // Keep PTCL-embedded meshes within a sane range; avoids "invisible" due to massive scale or near-zero size.
            if (radius > 50.0f)
            {
                return 1.0f / radius;
            }

            if (radius < 0.005f)
            {
                return 0.05f / MathF.Max(0.000001f, radius);
            }

            return 1.0f;
        }

        private sealed class SkinningData
        {
            public Matrix4[] BoneWorldByBone = Array.Empty<Matrix4>();
            public int[] RigidMatrixIndexByBone = Array.Empty<int>();
            public int[] SmoothMatrixIndexByBone = Array.Empty<int>();
            public Matrix4[] WorldByMatrix = Array.Empty<Matrix4>();
            public Matrix4[] InvBindByMatrix = Array.Empty<Matrix4>();
        }

        private static SkinningData BuildSkinning(BfresSkeleton? skeleton)
        {
            var skin = new SkinningData();
            if (skeleton == null || skeleton.Bones == null || skeleton.Bones.Count == 0)
            {
                return skin;
            }

            int boneCount = skeleton.Bones.Count;
            skin.BoneWorldByBone = BuildBoneWorldMatrices(skeleton);
            skin.RigidMatrixIndexByBone = new int[boneCount];
            skin.SmoothMatrixIndexByBone = new int[boneCount];

            int maxMatrix = -1;
            for (int i = 0; i < boneCount; i++)
            {
                var b = skeleton.Bones[i];
                int rigid = b?.RigidMatrixIndex ?? -1;
                int smooth = b?.SmoothMatrixIndex ?? -1;
                skin.RigidMatrixIndexByBone[i] = rigid;
                skin.SmoothMatrixIndexByBone[i] = smooth;
                if (rigid > maxMatrix) maxMatrix = rigid;
                if (smooth > maxMatrix) maxMatrix = smooth;
            }

            int matrixCount = maxMatrix >= 0 ? maxMatrix + 1 : boneCount;
            skin.WorldByMatrix = new Matrix4[matrixCount];
            skin.InvBindByMatrix = new Matrix4[matrixCount];
            for (int i = 0; i < matrixCount; i++)
            {
                skin.WorldByMatrix[i] = Matrix4.Identity;
                skin.InvBindByMatrix[i] = Matrix4.Identity;
            }

            for (int bone = 0; bone < boneCount; bone++)
            {
                var w = bone < skin.BoneWorldByBone.Length ? skin.BoneWorldByBone[bone] : Matrix4.Identity;
                int rigid = skin.RigidMatrixIndexByBone[bone];
                int smooth = skin.SmoothMatrixIndexByBone[bone];
                if (rigid >= 0 && rigid < matrixCount)
                {
                    skin.WorldByMatrix[rigid] = w;
                }
                if (smooth >= 0 && smooth < matrixCount)
                {
                    skin.WorldByMatrix[smooth] = w;
                }
            }

            TryFillInvBindMatrices(skeleton, skin, boneCount, matrixCount);

            MessageHandler.Instance.AddMessage(
                MessageType.LOG,
                $"[PTCL] BFRES skin bones={boneCount} matrices={matrixCount} invBind={(skin.InvBindByMatrix != null && skin.InvBindByMatrix.Length != 0 ? "yes" : "no")}");
            return skin;
        }

        private static Matrix4 GetShapeRigidMatrix(BfresShape shape, SkinningData skin)
        {
            int boneIndex = shape?.BoneIndex ?? -1;
            if (boneIndex < 0)
            {
                return Matrix4.Identity;
            }

            if (skin.RigidMatrixIndexByBone == null || boneIndex >= skin.RigidMatrixIndexByBone.Length)
            {
                return GetBoneWorld(skin.BoneWorldByBone, boneIndex);
            }

            int matrixIndex = skin.RigidMatrixIndexByBone[boneIndex];
            if (matrixIndex < 0)
            {
                return GetBoneWorld(skin.BoneWorldByBone, boneIndex);
            }

            return GetMatrixWorld(skin.WorldByMatrix, matrixIndex);
        }

        private static Matrix4 GetMatrixWorld(Matrix4[] worldByMatrix, int matrixIndex)
        {
            if (worldByMatrix == null || worldByMatrix.Length == 0)
            {
                return Matrix4.Identity;
            }

            if (matrixIndex < 0 || matrixIndex >= worldByMatrix.Length)
            {
                return Matrix4.Identity;
            }

            return worldByMatrix[matrixIndex];
        }

        private static Matrix4 GetSkinningMatrix(SkinningData skin, int matrixIndex)
        {
            var w = GetMatrixWorld(skin.WorldByMatrix, matrixIndex);
            if (skin.InvBindByMatrix != null && matrixIndex >= 0 && matrixIndex < skin.InvBindByMatrix.Length)
            {
                return w * skin.InvBindByMatrix[matrixIndex];
            }
            return w;
        }

        private static void TryFillInvBindMatrices(BfresSkeleton skeleton, SkinningData skin, int boneCount, int matrixCount)
        {
            try
            {
                var prop =
                    skeleton.GetType().GetProperty("MatrixToBoneList") ??
                    skeleton.GetType().GetProperty("MatrixToBone") ??
                    skeleton.GetType().GetProperty("InverseBindMatrices") ??
                    skeleton.GetType().GetProperty("InvBindMatrices");

                if (prop == null)
                {
                    return;
                }

                if (prop.GetValue(skeleton) is not System.Array arr || arr.Length == 0)
                {
                    return;
                }

                MessageHandler.Instance.AddMessage(
                    MessageType.LOG,
                    $"[PTCL] BFRES invBind src='{prop.Name}' len={arr.Length}");

                int len = arr.Length;
                if (len == matrixCount)
                {
                    for (int i = 0; i < len; i++)
                    {
                        if (TryConvertToMatrix4(arr.GetValue(i), out var m))
                        {
                            skin.InvBindByMatrix[i] = m;
                        }
                    }
                    return;
                }

                if (len == boneCount)
                {
                    for (int bone = 0; bone < boneCount; bone++)
                    {
                        int rigid = skin.RigidMatrixIndexByBone[bone];
                        if (rigid < 0 || rigid >= matrixCount)
                        {
                            continue;
                        }

                        if (TryConvertToMatrix4(arr.GetValue(bone), out var m))
                        {
                            skin.InvBindByMatrix[rigid] = m;
                        }
                    }
                }
            }
            catch
            {
            }
        }

        private static bool TryConvertToMatrix4(object? value, out Matrix4 matrix)
        {
            matrix = Matrix4.Identity;
            if (value == null)
            {
                return false;
            }

            try
            {
                var t = value.GetType();

                if (TryGetFloat(t, value, "M44", out _))
                {
                    matrix = new Matrix4(
                        GetF(t, value, "M11"), GetF(t, value, "M12"), GetF(t, value, "M13"), GetF(t, value, "M14"),
                        GetF(t, value, "M21"), GetF(t, value, "M22"), GetF(t, value, "M23"), GetF(t, value, "M24"),
                        GetF(t, value, "M31"), GetF(t, value, "M32"), GetF(t, value, "M33"), GetF(t, value, "M34"),
                        GetF(t, value, "M41"), GetF(t, value, "M42"), GetF(t, value, "M43"), GetF(t, value, "M44"));
                    return true;
                }

                if (TryGetFloat(t, value, "M34", out _))
                {
                    matrix = new Matrix4(
                        GetF(t, value, "M11"), GetF(t, value, "M12"), GetF(t, value, "M13"), GetF(t, value, "M14"),
                        GetF(t, value, "M21"), GetF(t, value, "M22"), GetF(t, value, "M23"), GetF(t, value, "M24"),
                        GetF(t, value, "M31"), GetF(t, value, "M32"), GetF(t, value, "M33"), GetF(t, value, "M34"),
                        0, 0, 0, 1);
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static float GetF(Type t, object instance, string name)
        {
            return TryGetFloat(t, instance, name, out var f) ? f : 0.0f;
        }

        private static bool TryGetFloat(Type t, object instance, string name, out float value)
        {
            value = 0.0f;
            try
            {
                var fi = t.GetField(name);
                if (fi != null)
                {
                    var v = fi.GetValue(instance);
                    if (v is float f) { value = f; return true; }
                    if (v is double d) { value = (float)d; return true; }
                }

                var pi = t.GetProperty(name);
                if (pi != null)
                {
                    var v = pi.GetValue(instance);
                    if (v is float f) { value = f; return true; }
                    if (v is double d) { value = (float)d; return true; }
                }
            }
            catch
            {
            }
            return false;
        }

        private static Matrix4[] BuildBoneWorldMatrices(BfresSkeleton? skeleton)
        {
            if (skeleton == null || skeleton.Bones == null || skeleton.Bones.Count == 0)
            {
                return Array.Empty<Matrix4>();
            }

            int count = skeleton.Bones.Count;
            var local = new Matrix4[count];
            var world = new Matrix4[count];

            for (int i = 0; i < count; i++)
            {
                var bone = skeleton.Bones[i];
                local[i] = BuildBoneLocalMatrix(bone);
            }

            for (int i = 0; i < count; i++)
            {
                var bone = skeleton.Bones[i];
                int parent = bone?.ParentIndex ?? -1;
                if (parent >= 0 && parent < count)
                {
                    world[i] = world[parent] * local[i];
                }
                else
                {
                    world[i] = local[i];
                }
            }

            return world;
        }

        private static Matrix4 BuildBoneLocalMatrix(BfresBone? bone)
        {
            if (bone == null)
            {
                return Matrix4.Identity;
            }

            try
            {
                var s = bone.Scale;
                var p = bone.Position;
                var r = bone.Rotation;

                var scale = new Vector3(s.X, s.Y, s.Z);
                var pos = new Vector3(p.X, p.Y, p.Z);

                Quaternion q;
                if (bone.FlagsRotation == BfresBoneFlagsRotation.Quaternion)
                {
                    q = new Quaternion(r.X, r.Y, r.Z, r.W);
                    q.Normalize();
                }
                else
                {
                    q = Quaternion.FromEulerAngles(r.X, r.Y, r.Z);
                }

                return
                    Matrix4.CreateTranslation(pos) *
                    Matrix4.CreateFromQuaternion(q) *
                    Matrix4.CreateScale(scale);
            }
            catch
            {
                return Matrix4.Identity;
            }
        }

        private static Matrix4 GetBoneWorld(Matrix4[] boneWorld, int boneIndex)
        {
            if (boneWorld == null || boneWorld.Length == 0)
            {
                return Matrix4.Identity;
            }

            if (boneIndex < 0 || boneIndex >= boneWorld.Length)
            {
                return Matrix4.Identity;
            }

            return boneWorld[boneIndex];
        }

        private static Vector3 ApplySkinning(
            Vector3 position,
            int vertexIndex,
            BfresShape shape,
            Matrix4 shapeRigidMatrix,
            SkinningData skin,
            VertexBufferHelperAttrib? boneIndexAttr,
            VertexBufferHelperAttrib? boneWeightAttr)
        {
            int skinCount = shape?.VertexSkinCount ?? 0;
            if (skinCount <= 0)
            {
                if (shape?.BoneIndex >= 0)
                {
                    return TransformPosition(position, shapeRigidMatrix);
                }
                return position;
            }

            if (boneIndexAttr?.Data == null || vertexIndex >= boneIndexAttr.Data.Length)
            {
                if (shape?.BoneIndex >= 0)
                {
                    return TransformPosition(position, shapeRigidMatrix);
                }
                return position;
            }

            var bi = boneIndexAttr.Data[vertexIndex];
            Vector4F bw = default;
            bool hasWeights = boneWeightAttr?.Data != null && vertexIndex < boneWeightAttr.Data.Length;
            if (hasWeights)
            {
                bw = boneWeightAttr!.Data[vertexIndex];
            }

            if (skinCount == 1)
            {
                int idx = (int)MathF.Round(bi.X);
                return TransformPosition(position, GetSkinningMatrix(skin, idx));
            }

            float w0 = hasWeights ? bw.X : 1.0f;
            float w1 = hasWeights ? bw.Y : 0.0f;
            float w2 = hasWeights ? bw.Z : 0.0f;
            float w3 = hasWeights ? bw.W : 0.0f;
            float sum = w0 + w1 + w2 + w3;
            if (sum <= 0.000001f)
            {
                w0 = 1.0f;
                sum = 1.0f;
            }
            float inv = 1.0f / sum;
            w0 *= inv; w1 *= inv; w2 *= inv; w3 *= inv;

            int i0 = (int)MathF.Round(bi.X);
            int i1 = (int)MathF.Round(bi.Y);
            int i2 = (int)MathF.Round(bi.Z);
            int i3 = (int)MathF.Round(bi.W);

            var p0 = TransformPosition(position, GetSkinningMatrix(skin, i0));
            var p1 = TransformPosition(position, GetSkinningMatrix(skin, i1));
            var p2 = TransformPosition(position, GetSkinningMatrix(skin, i2));
            var p3 = TransformPosition(position, GetSkinningMatrix(skin, i3));

            return p0 * w0 + p1 * w1 + p2 * w2 + p3 * w3;
        }

        private static Vector3 TransformPosition(Vector3 position, Matrix4 matrix)
        {
            var v = new Vector4(position.X, position.Y, position.Z, 1.0f);
            var r = matrix * v;
            float iw = MathF.Abs(r.W) > 0.000001f ? 1.0f / r.W : 1.0f;
            return new Vector3(r.X * iw, r.Y * iw, r.Z * iw);
        }

        private static BfresVertexBuffer? TryGetVertexBuffer(BfresModel model, BfresShape shape)
        {
            try
            {
                // Some Bfres versions expose Shape.VertexBuffer, others require dereferencing via VertexBufferIndex on Model.VertexBuffers.
                var vbProp = shape.GetType().GetProperty("VertexBuffer");
                if (vbProp?.GetValue(shape) is BfresVertexBuffer directVb)
                {
                    return directVb;
                }

                var idxProp = shape.GetType().GetProperty("VertexBufferIndex");
                if (idxProp == null)
                {
                    return null;
                }

                int vbIndex = idxProp.GetValue(shape) switch
                {
                    byte b => b,
                    sbyte sb => sb,
                    short s => s,
                    ushort us => us,
                    int i => i,
                    uint ui => unchecked((int)ui),
                    _ => -1
                };

                if (vbIndex < 0 || model.VertexBuffers == null || vbIndex >= model.VertexBuffers.Count)
                {
                    return null;
                }

                return model.VertexBuffers[vbIndex];
            }
            catch
            {
                return null;
            }
        }

        private static VertexBufferHelperAttrib? FindPositionAttrib(VertexBufferHelper helper)
        {
            if (helper == null || helper.Attributes == null)
            {
                return null;
            }

            // Common BFRES attribute names.
            foreach (var name in new[] { "_p0", "aPosition", "Position", "pos", "vPosition" })
            {
                try
                {
                    var a = helper[name];
                    if (a != null && a.Data != null && a.Data.Length != 0)
                    {
                        return a;
                    }
                }
                catch
                {
                }
            }

            // Fallback: first attribute with "p" in the name.
            for (int i = 0; i < helper.Attributes.Count; i++)
            {
                var a = helper.Attributes[i];
                if (a == null || string.IsNullOrWhiteSpace(a.Name) || a.Data == null || a.Data.Length == 0)
                {
                    continue;
                }

                if (a.Name.IndexOf("pos", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    a.Name.Equals("_p0", StringComparison.OrdinalIgnoreCase) ||
                    a.Name.IndexOf("_p", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return a;
                }
            }

            return null;
        }

        private static VertexBufferHelperAttrib? FindColorAttrib(VertexBufferHelper helper)
        {
            if (helper == null || helper.Attributes == null)
            {
                return null;
            }

            foreach (var name in new[] { "_c0", "aColor", "Color", "color", "vColor" })
            {
                try
                {
                    var a = helper[name];
                    if (a != null && a.Data != null && a.Data.Length != 0)
                    {
                        return a;
                    }
                }
                catch
                {
                }
            }

            return null;
        }

        private static VertexBufferHelperAttrib? FindUvAttrib(VertexBufferHelper helper)
        {
            if (helper == null || helper.Attributes == null)
            {
                return null;
            }

            foreach (var name in new[] { "_u0", "aTexCoord0", "TexCoord0", "uv0", "tex0" })
            {
                try
                {
                    var a = helper[name];
                    if (a != null && a.Data != null && a.Data.Length != 0)
                    {
                        return a;
                    }
                }
                catch
                {
                }
            }

            return null;
        }

        private static VertexBufferHelperAttrib? FindBoneIndexAttrib(VertexBufferHelper helper)
        {
            if (helper == null || helper.Attributes == null)
            {
                return null;
            }

            foreach (var name in new[] { "_i0", "aBoneIndex", "BoneIndex", "boneIndex", "vBoneIndex" })
            {
                try
                {
                    var a = helper[name];
                    if (a != null && a.Data != null && a.Data.Length != 0)
                    {
                        return a;
                    }
                }
                catch
                {
                }
            }

            return null;
        }

        private static VertexBufferHelperAttrib? FindBoneWeightAttrib(VertexBufferHelper helper)
        {
            if (helper == null || helper.Attributes == null)
            {
                return null;
            }

            foreach (var name in new[] { "_w0", "aBoneWeight", "BoneWeight", "boneWeight", "vBoneWeight" })
            {
                try
                {
                    var a = helper[name];
                    if (a != null && a.Data != null && a.Data.Length != 0)
                    {
                        return a;
                    }
                }
                catch
                {
                }
            }

            return null;
        }

        private static void AppendTriangleStrip(List<uint> dst, uint[] strip, uint baseIndex)
        {
            // Standard strip -> triangles, skipping degenerate triangles.
            const uint Restart16 = 0xFFFFu;
            const uint Restart32 = 0xFFFFFFFFu;

            bool flip = false;
            int localCount = 0;
            uint a = 0;
            uint b = 0;

            for (int i = 0; i < strip.Length; i++)
            {
                uint idx = strip[i];
                if (idx == Restart16 || idx == Restart32)
                {
                    localCount = 0;
                    flip = false;
                    continue;
                }

                if (localCount == 0)
                {
                    a = idx;
                    localCount = 1;
                    continue;
                }

                if (localCount == 1)
                {
                    b = idx;
                    localCount = 2;
                    continue;
                }

                uint c = idx;

                if (a == b || b == c || c == a)
                {
                    a = b;
                    b = c;
                    flip = !flip;
                    continue;
                }

                if (!flip)
                {
                    dst.Add(baseIndex + a);
                    dst.Add(baseIndex + b);
                    dst.Add(baseIndex + c);
                }
                else
                {
                    dst.Add(baseIndex + b);
                    dst.Add(baseIndex + a);
                    dst.Add(baseIndex + c);
                }

                a = b;
                b = c;
                flip = !flip;
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;

            if (ebo != 0)
            {
                try { GL.DeleteBuffer(ebo); } catch { }
                ebo = 0;
            }

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
