using GFTool.Renderer.Core;
using GFTool.Renderer.Core.Graphics;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace GFTool.Renderer.Scene.GraphicsObjects;

public partial class Model
{
    public void DrawShadow(Matrix4 lightView, Matrix4 lightProjection)
    {
        if (!IsVisible) return;
        if (!IsGpuSetupComplete) return;

        var shader = ShaderPool.Instance.GetShader("ShadowDepth");
        if (shader == null) return;

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
            if (blendIndexRemapModes == null) return false;
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
            if (!canSkin || !HasSkinning[submeshIndex]) return;

            var mode = blendIndexRemapModes != null && submeshIndex < blendIndexRemapModes.Length
                ? blendIndexRemapModes[submeshIndex]
                : BlendIndexRemapMode.None;

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

        shader.Bind();
        shader.SetMatrix4("view", lightView);
        shader.SetMatrix4("projection", lightProjection);
        shader.SetBoolIfExists("SwapBlendOrder", RenderOptions.SwapBlendOrder);

        for (int i = 0; i < VAOs.Length; i++)
        {
            if (submeshVisible != null && i < submeshVisible.Length && !submeshVisible[i]) continue;
            TryUploadPendingCpuMorphVbo(i);

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

            shader.SetMatrix4("model", submeshModelMat);
            ResolveSkinningForSubmesh(i, out var matrices, out var boneCount);
            bool enabled = canSkin && HasSkinning[i];
            shader.SetBoolIfExists("EnableSkinning", enabled);
            shader.SetIntIfExists("BoneCount", enabled ? boneCount : 0);
            if (enabled && matrices != null)
            {
                shader.SetMatrix4ArrayIfExists("Bones", matrices, RenderOptions.TransposeSkinMatrices);
            }

            GL.BindVertexArray(VAOs[i]);
            GL.DrawElements(PrimitiveType.Triangles, Indices[i].Length, DrawElementsType.UnsignedInt, 0);
            GL.BindVertexArray(0);
        }
    }
}
