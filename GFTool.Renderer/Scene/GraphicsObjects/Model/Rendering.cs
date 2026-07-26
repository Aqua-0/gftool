using GFTool.Core.Utils;
using GFTool.Renderer.Core;
using GFTool.Renderer.Core.Graphics;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using Trinity.Core.Flatbuffers.TR.Model;
using Trinity.Core.Utils;
using System.IO;
using System;
using Trinity.Core.Assets;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;


namespace GFTool.Renderer.Scene.GraphicsObjects
{
    public partial class Model : RefObject
    {
        private void EnsureLocalBounds()
        {
            if (!localBoundsDirty)
            {
                return;
            }

            if (Positions == null || Positions.Count == 0)
            {
                localBoundsCenter = Vector3.Zero;
                localBoundsRadius = 0f;
                localBoundsDirty = false;
                return;
            }

            bool hasAny = false;
            Vector3 min = default;
            Vector3 max = default;
            for (int i = 0; i < Positions.Count; i++)
            {
                var submeshPositions = Positions[i];
                if (submeshPositions == null || submeshPositions.Length == 0)
                {
                    continue;
                }

                for (int j = 0; j < submeshPositions.Length; j++)
                {
                    var p = submeshPositions[j];
                    if (!hasAny)
                    {
                        min = p;
                        max = p;
                        hasAny = true;
                    }
                    else
                    {
                        min = Vector3.ComponentMin(min, p);
                        max = Vector3.ComponentMax(max, p);
                    }
                }
            }

            if (!hasAny)
            {
                localBoundsCenter = Vector3.Zero;
                localBoundsRadius = 0f;
                localBoundsDirty = false;
                return;
            }

            localBoundsCenter = (min + max) * 0.5f;
            float radius = 0f;
            for (int i = 0; i < Positions.Count; i++)
            {
                var submeshPositions = Positions[i];
                if (submeshPositions == null)
                {
                    continue;
                }

                for (int j = 0; j < submeshPositions.Length; j++)
                {
                    float dist = (submeshPositions[j] - localBoundsCenter).Length;
                    if (dist > radius)
                    {
                        radius = dist;
                    }
                }
            }

            localBoundsRadius = radius;
            localBoundsDirty = false;
        }

        public (Vector3 Center, float Radius) GetApproximateWorldBounds()
        {
            EnsureLocalBounds();
            var center4 = new Vector4(localBoundsCenter.X, localBoundsCenter.Y, localBoundsCenter.Z, 1f) * modelMat;
            float scaleX = new Vector3(modelMat.M11, modelMat.M12, modelMat.M13).Length;
            float scaleY = new Vector3(modelMat.M21, modelMat.M22, modelMat.M23).Length;
            float scaleZ = new Vector3(modelMat.M31, modelMat.M32, modelMat.M33).Length;
            float maxScale = MathF.Max(scaleX, MathF.Max(scaleY, scaleZ));
            return (center4.Xyz, localBoundsRadius * maxScale);
        }

        public (int DrawCalls, long Triangles) GetOpaqueGeometryContribution()
        {
            if (!IsVisible || !IsGpuSetupComplete || VAOs == null || Indices == null)
            {
                return (0, 0);
            }

            int drawCalls = 0;
            long triangles = 0;
            for (int i = 0; i < VAOs.Length; i++)
            {
                if (submeshVisible != null && i < submeshVisible.Length && !submeshVisible[i])
                {
                    continue;
                }

                bool drawOpaque = true;
                if (materials != null && materials.Length > 0)
                {
                    if (!materialMap.TryGetValue(MaterialNames[i], out var mat))
                    {
                        mat = materials[0];
                    }

                    drawOpaque = !mat.IsTransparent;
                }

                if (!drawOpaque)
                {
                    continue;
                }

                drawCalls++;
                if (i < Indices.Count)
                {
                    triangles += Indices[i].Length / 3;
                }
            }

            return (drawCalls, triangles);
        }

        public bool IsEligibleForMergedStaticBatch()
        {
            return GetMergedStaticBatchRejectReason() == null;
        }

        public string? GetMergedStaticBatchRejectReason()
        {
            if (armature != null)
            {
                return "armature";
            }

            for (int i = 0; i < HasSkinning.Count; i++)
            {
                if (HasSkinning[i])
                {
                    return "skinned-submesh";
                }
            }

            return null;
        }

        public IEnumerable<(string MaterialName, int DrawCalls, long Triangles)> EnumerateOpaqueMaterialContributions()
        {
            if (!IsVisible || !IsGpuSetupComplete || VAOs == null || Indices == null)
            {
                yield break;
            }

            for (int i = 0; i < VAOs.Length; i++)
            {
                if (submeshVisible != null && i < submeshVisible.Length && !submeshVisible[i])
                {
                    continue;
                }

                string materialName = i < MaterialNames.Count ? MaterialNames[i] : string.Empty;
                bool drawOpaque = true;
                if (materials != null && materials.Length > 0)
                {
                    if (!materialMap.TryGetValue(materialName, out var mat))
                    {
                        mat = materials[0];
                    }

                    drawOpaque = !mat.IsTransparent;
                    materialName = mat?.Name ?? materialName;
                }

                if (!drawOpaque)
                {
                    continue;
                }

                long triangles = i < Indices.Count ? Indices[i].Length / 3 : 0;
                yield return (materialName ?? string.Empty, 1, triangles);
            }
        }

        public override void Setup()
        {
            BeginGpuSetup();
            while (!StepGpuSetup())
            {
                // Intentionally empty; this preserves the original synchronous behavior.
            }
        }

        public override void Draw(Matrix4 view, Matrix4 proj)
        {
            if (!IsVisible)
            {
                return;
            }
            if (!IsGpuSetupComplete)
            {
                return;
            }

            PerfCounters.RecordModelDraw();

            Matrix4[]? nodeSkinMatrices = null;
            Matrix4[]? jointInfoSkinMatrices = null;
            Matrix4[]? paletteSkinMatrices = null;
            var skinArmature = GetEffectiveArmature();
            bool canSkin = skinArmature != null && skinArmature.Bones.Count > 0;
            int nodeBoneCount = 0;
            int jointInfoBoneCount = 0;
            int paletteBoneCount = 0;

            bool AnySubmeshNeedsNodeSkinMatrices()
            {
                if (blendIndexRemapModes == null)
                {
                    return false;
                }

                for (int i = 0; i < blendIndexRemapModes.Length; i++)
                {
                    switch (blendIndexRemapModes[i])
                    {
                        case BlendIndexRemapMode.JointInfo:
                        case BlendIndexRemapMode.SkinningPalette:
                        case BlendIndexRemapMode.BoneMeta:
                            return true;
                    }
                }

                return false;
            }

            // Rigid attachments are resolved in node index space, but the active skin matrix array may be
            // joint-info or palette indexed. Always compute node-indexed skin matrices for rigid attachments.
            bool needsNodeSkinMatrices = canSkin && (rigidParentBoneIndexBySubmesh != null || AnySubmeshNeedsNodeSkinMatrices());
            if (needsNodeSkinMatrices)
            {
                nodeBoneCount = Math.Min(skinArmature!.Bones.Count, Armature.MaxSkinBones);
                nodeSkinMatrices = skinArmature.GetSkinMatrices(Armature.MaxSkinBones);
            }

            if (canSkin && RenderOptions.UseJointInfoMatrices)
            {
                jointInfoSkinMatrices = skinArmature!.GetSkinMatricesForJointInfo(Armature.MaxSkinBones, out jointInfoBoneCount);
            }

            if (canSkin && RenderOptions.UseSkinningPaletteMatrices)
            {
                var palette = skinArmature!.BuildSkinningPalette();
                paletteSkinMatrices = skinArmature.GetSkinMatricesForPalette(palette, Armature.MaxSkinBones, out paletteBoneCount);
            }

            void ResolveSkinningForSubmesh(int submeshIndex, out Matrix4[]? matrices, out int boneCount)
            {
                matrices = null;
                boneCount = 0;
                if (!canSkin || !HasSkinning[submeshIndex])
                {
                    return;
                }

                var mode = blendIndexRemapModes != null && submeshIndex < blendIndexRemapModes.Length
                    ? blendIndexRemapModes[submeshIndex]
                    : BlendIndexRemapMode.None;

                // If we remapped indices into node space, we must also skin from node-indexed matrices.
                if (mode == BlendIndexRemapMode.JointInfo ||
                    mode == BlendIndexRemapMode.SkinningPalette ||
                    mode == BlendIndexRemapMode.BoneMeta)
                {
                    matrices = nodeSkinMatrices ?? skinArmature!.GetSkinMatrices(Armature.MaxSkinBones);
                    boneCount = nodeBoneCount != 0 ? nodeBoneCount : Math.Min(skinArmature!.Bones.Count, Armature.MaxSkinBones);
                    return;
                }

                if (RenderOptions.UseJointInfoMatrices && jointInfoSkinMatrices != null)
                {
                    matrices = jointInfoSkinMatrices;
                    boneCount = jointInfoBoneCount;
                    return;
                }

                if (RenderOptions.UseSkinningPaletteMatrices && paletteSkinMatrices != null)
                {
                    matrices = paletteSkinMatrices;
                    boneCount = paletteBoneCount;
                    return;
                }

                matrices = nodeSkinMatrices ?? skinArmature!.GetSkinMatrices(Armature.MaxSkinBones);
                boneCount = nodeBoneCount != 0 ? nodeBoneCount : Math.Min(skinArmature!.Bones.Count, Armature.MaxSkinBones);
            }

            for (int i = 0; i < VAOs.Length; i++)
            {
                if (submeshVisible != null && i < submeshVisible.Length && !submeshVisible[i])
                {
                    continue;
                }

                PerfCounters.RecordSubmeshDraw();
                TryUploadPendingCpuMorphVbo(i);

                if (RenderOptions.OutlinePass)
                {
                    if (i == selectedSubmeshIndex)
                    {
                        ResolveSkinningForSubmesh(i, out var outlineSkinMatrices, out var outlineBoneCount);
                        GL.BindVertexArray(VAOs[i]);
                        DrawOutline(view, proj, Indices[i].Length, canSkin && HasSkinning[i], outlineBoneCount, outlineSkinMatrices ?? Array.Empty<Matrix4>());
                        GL.BindVertexArray(0);
                    }
                    continue;
                }
                //Bind appropriate mat
                if (materials != null && materials.Length > 0)
                {
                    if (!materialMap.TryGetValue(MaterialNames[i], out var mat))
                    {
                        mat = materials[0];
                    }
                    bool drawOpaque = !RenderOptions.TransparentPass && !mat.IsTransparent;
                    bool drawTransparent = RenderOptions.TransparentPass && mat.IsTransparent;
                    if (drawOpaque || drawTransparent)
                    {
                        Matrix4 submeshModelMat = modelMat;
                        if (!HasSkinning[i] &&
                            rigidParentBoneIndexBySubmesh != null &&
                            i < rigidParentBoneIndexBySubmesh.Length)
                        {
                            int boneIndex = rigidParentBoneIndexBySubmesh[i];
                            if (boneIndex >= 0 && nodeSkinMatrices != null && boneIndex < nodeSkinMatrices.Length)
                            {
                                submeshModelMat = modelMat * nodeSkinMatrices[boneIndex];
                            }
                        }

                        var (layerMaskOverride, aoOverride) = GetUvOverrides(i);
                        mat.Use(view, submeshModelMat, proj, HasVertexColors[i], HasTangents[i], HasBinormals[i], i < HasUv1.Count && HasUv1[i], layerMaskOverride, aoOverride);
                        ResolveSkinningForSubmesh(i, out var submeshSkinMatrices, out var submeshBoneCount);
                        mat.ApplySkinning(canSkin && HasSkinning[i], submeshBoneCount, submeshSkinMatrices ?? Array.Empty<Matrix4>());
                    }
                    else if (!RenderOptions.TransparentPass)
                    {
                        continue;
                    }
                }

                // Draw the geometry
                GL.BindVertexArray(VAOs[i]);
                if (!RenderOptions.TransparentPass)
                {
                    PerfCounters.RecordDrawCall(Indices[i].Length);
                    GL.DrawElements(PrimitiveType.Triangles, Indices[i].Length, DrawElementsType.UnsignedInt, 0);
                }
                else if (materials != null && materials.Length > 0 && materialMap.TryGetValue(MaterialNames[i], out var mat) && mat.IsTransparent)
                {
                    PerfCounters.RecordDrawCall(Indices[i].Length);
                    GL.DrawElements(PrimitiveType.Triangles, Indices[i].Length, DrawElementsType.UnsignedInt, 0);
                }

                GL.BindVertexArray(0);
            }
        }

        private void TryUploadPendingCpuMorphVbo(int submeshIndex)
        {
            if (submeshIndex < 0 || VBOs == null || submeshIndex >= VBOs.Length)
            {
                return;
            }

            bool needsUpload;
            lock (morphGate)
            {
                needsUpload = pendingMorphVboUploads.Remove(submeshIndex);
            }

            if (!needsUpload)
            {
                return;
            }

            // Only update the streams affected by CPU morphing.
            // Layout must match SetupSubmeshGpu.
            try
            {
                var vertSize = Positions[submeshIndex].Length * Vector3.SizeInBytes;
                var normSize = Normals[submeshIndex].Length * Vector3.SizeInBytes;
                var uvSize = UVs[submeshIndex].Length * Vector2.SizeInBytes;
                var uv2Size = UVs2[submeshIndex].Length * Vector2.SizeInBytes;
                var colorSize = Colors[submeshIndex].Length * Vector4.SizeInBytes;
                var tangentSize = Tangents[submeshIndex].Length * Vector4.SizeInBytes;
                var binormalSize = Binormals[submeshIndex].Length * Vector3.SizeInBytes;

                int vbo = VBOs[submeshIndex];
                if (vbo == 0)
                {
                    return;
                }

                GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);

                IntPtr offsetPos = IntPtr.Zero;
                IntPtr offsetNorm = offsetPos + vertSize;
                IntPtr offsetTangent = offsetNorm + normSize + uvSize + uv2Size + colorSize;
                IntPtr offsetBinormal = offsetTangent + tangentSize;

                GL.BufferSubData(BufferTarget.ArrayBuffer, offsetPos, vertSize, ToUnmanagedByteArray(Positions[submeshIndex]));
                GL.BufferSubData(BufferTarget.ArrayBuffer, offsetNorm, normSize, ToUnmanagedByteArray(Normals[submeshIndex]));
                GL.BufferSubData(BufferTarget.ArrayBuffer, offsetTangent, tangentSize, ToUnmanagedByteArray(Tangents[submeshIndex]));
                GL.BufferSubData(BufferTarget.ArrayBuffer, offsetBinormal, binormalSize, ToUnmanagedByteArray(Binormals[submeshIndex]));

                GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            }
            catch
            {
                try { GL.BindBuffer(BufferTarget.ArrayBuffer, 0); } catch { }
            }
        }
	    }
}
