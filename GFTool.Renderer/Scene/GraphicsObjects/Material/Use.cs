using GFTool.Core.Utils;
using GFTool.Renderer.Core;
using GFTool.Renderer.Core.Graphics;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System;
using System.Globalization;
using System.Drawing;
using System.IO;
using System.Linq;
using Trinity.Core.Flatbuffers.TR.Model;
using Trinity.Core.Flatbuffers.Utils;
using Trinity.Core.Assets;

namespace GFTool.Renderer.Scene.GraphicsObjects
{
    public partial class Material : IDisposable
    {
	        public void Use(
	            Matrix4 view,
	            Matrix4 model,
	            Matrix4 proj,
            bool hasVertexColors,
            bool hasTangents,
            bool hasBinormals,
            bool hasUv1,
            UvSetOverride layerMaskUvOverride = UvSetOverride.Material,
            UvSetOverride aoUvOverride = UvSetOverride.Material)
        {
	            var activeShader = GetActiveShader();
	            if (activeShader == null) return;

	            GL.FrontFace(FrontFaceDirection.Ccw);
	            GL.Enable(EnableCap.CullFace);
	            GL.CullFace(CullFaceMode.Back);

	            if (trmtrFrontFace.HasValue)
	            {
	                GL.FrontFace(trmtrFrontFace.Value == Trinity.Core.Flatbuffers.TR.Model.TrmtrFrontFace.CW
	                    ? FrontFaceDirection.Cw
	                    : FrontFaceDirection.Ccw);
	            }

	            if (trmtrCullMode.HasValue)
	            {
	                switch (trmtrCullMode.Value)
	                {
	                    case Trinity.Core.Flatbuffers.TR.Model.TrmtrCullMode.None:
	                        GL.Disable(EnableCap.CullFace);
	                        break;
	                    case Trinity.Core.Flatbuffers.TR.Model.TrmtrCullMode.Front:
	                        GL.Enable(EnableCap.CullFace);
	                        GL.CullFace(CullFaceMode.Front);
	                        break;
	                    case Trinity.Core.Flatbuffers.TR.Model.TrmtrCullMode.FrontAndBack:
	                        GL.Enable(EnableCap.CullFace);
	                        GL.CullFace(CullFaceMode.Back);
	                        break;
	                    case Trinity.Core.Flatbuffers.TR.Model.TrmtrCullMode.Back:
	                    default:
	                        GL.Enable(EnableCap.CullFace);
	                        GL.CullFace(CullFaceMode.Back);
	                        break;
	                }
	            }

	            PerfCounters.RecordMaterialUse();

	            activeShader.Bind();
            activeShader.SetBoolIfExists("TransparentPass", RenderOptions.TransparentPass);
            ApplyTransparentBlendState();
            activeShader.SetBoolIfExists("PremultiplyAlpha", RenderOptions.TransparentPass && isTransparent && transparentBlendMode == TransparentBlendMode.PremultipliedAlpha);
            ResetCommonUniformDefaults(activeShader);
            var usedSlots = new HashSet<int>();
            var textureNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int nextSlot = 0;
            bool baseColorMapIsPlaceholder = false;
            for (int i = 0; i < textures.Count; i++)
            {
                textures[i].EnsureLoaded();
                if (RenderOptions.EnableAsyncResourceLoading)
                {
                    // When models/materials are swapped after the initial async load (e.g. changing TRMMT sets),
                    // textures can end up decoded on a worker thread but never uploaded since the async loader
                    // work item isn't running. Opportunistically upload here while we are on the GL thread.
                    if (!textures[i].IsAsyncLoadComplete)
                    {
                        textures[i].TryUploadDecodedOnGlThread();
                    }
                }
                textureNames.Add(textures[i].Name);
                if (!baseColorMapIsPlaceholder &&
                    string.Equals(textures[i].Name, "BaseColorMap", StringComparison.OrdinalIgnoreCase) &&
                    IsPlaceholderMaskTexturePath(textures[i].SourceFile))
                {
                    baseColorMapIsPlaceholder = true;
                }
                int slot = (int)textures[i].Slot;
                if (slot < 0 || slot > 31 || usedSlots.Contains(slot))
                {
                    while (usedSlots.Contains(nextSlot) && nextSlot < 32) nextSlot++;
                    slot = Math.Min(nextSlot, 31);
                }
                usedSlots.Add(slot);

                GL.ActiveTexture(TextureUnit.Texture0 + slot);
                GL.BindTexture(TextureTarget.Texture2D, textures[i].textureId);

                var aliases = GetTextureNameAliases(textures[i].Name);
                if (aliases.Count > 0)
                {
                    activeShader.SetIntIfExists(textures[i].Name, slot);
                }
                else
                {
                    activeShader.SetIntIfExists(textures[i].Name, slot);
                }

                foreach (var alias in aliases)
                {
                    textureNames.Add(alias);
                    activeShader.SetIntIfExists(alias, slot);
                }
            }

            PerfCounters.RecordTextureBind(textures.Count);

            activeShader.SetBoolIfExists(
                "HasAuraTextures",
                textureNames.Contains("AuraColorMap") || textureNames.Contains("AuraNoiseMap"));

            bool isAuraShell = false;
            if (string.Equals(shaderKey, "IkCharacter", StringComparison.OrdinalIgnoreCase))
            {
                string? shadowingColor = null;
                string? shadowingMask = null;
                string? rimMask = null;
                string? baseColorMap = null;
                for (int i = 0; i < textures.Count; i++)
                {
                    var t = textures[i];
                    if (t == null)
                    {
                        continue;
                    }

                    if (string.Equals(t.Name, "BaseColorMap", StringComparison.OrdinalIgnoreCase))
                    {
                        baseColorMap = t.SourceFile;
                    }
                    if (string.Equals(t.Name, "ShadowingColorMap", StringComparison.OrdinalIgnoreCase))
                    {
                        shadowingColor = t.SourceFile;
                    }
                    else if (string.Equals(t.Name, "ShadowingColorMaskMap", StringComparison.OrdinalIgnoreCase))
                    {
                        shadowingMask = t.SourceFile;
                    }
                    else if (string.Equals(t.Name, "RimLightMaskMap", StringComparison.OrdinalIgnoreCase))
                    {
                        rimMask = t.SourceFile;
                    }
                }

                bool hasAuraMaskTrio =
                    !string.IsNullOrWhiteSpace(shadowingColor) &&
                    !string.IsNullOrWhiteSpace(shadowingMask) &&
                    !string.IsNullOrWhiteSpace(rimMask) &&
                    shadowingColor.Contains("white_msk", StringComparison.OrdinalIgnoreCase) &&
                    shadowingMask.Contains("black_msk", StringComparison.OrdinalIgnoreCase) &&
                    rimMask.Contains("rim_msk", StringComparison.OrdinalIgnoreCase);

                if (hasAuraMaskTrio)
                {
                    bool isBodyBLike =
                        (!string.IsNullOrWhiteSpace(baseColorMap) &&
                         baseColorMap.Contains("body_b", StringComparison.OrdinalIgnoreCase)) ||
                        Name.Contains("body_b", StringComparison.OrdinalIgnoreCase);

                    bool hasLayer2 = false;
                    bool hasLayer3 = false;
                    bool hasLayer4 = false;
                    float layer2Alpha = 1.0f;
                    float layer3Alpha = 1.0f;
                    float layer4Alpha = 1.0f;
                    Vector3 layer1Rgb = default;
                    Vector3 layer3Rgb = default;
                    bool hasLayer1Rgb = false;
                    bool hasLayer3Rgb = false;
                    for (int i = 0; i < vec4Params.Length; i++)
                    {
                        var p = vec4Params[i];
                        if (p == null || string.IsNullOrWhiteSpace(p.Name))
                        {
                            continue;
                        }

                        if (string.Equals(p.Name, "BaseColorLayer2", StringComparison.OrdinalIgnoreCase))
                        {
                            hasLayer2 = true;
                            layer2Alpha = p.Value.Z;
                        }
                        else if (string.Equals(p.Name, "BaseColorLayer3", StringComparison.OrdinalIgnoreCase))
                        {
                            hasLayer3 = true;
                            layer3Alpha = p.Value.Z;
                            layer3Rgb = new Vector3(p.Value.W, p.Value.X, p.Value.Y);
                            hasLayer3Rgb = true;
                        }
                        else if (string.Equals(p.Name, "BaseColorLayer4", StringComparison.OrdinalIgnoreCase))
                        {
                            hasLayer4 = true;
                            layer4Alpha = p.Value.Z;
                        }
                        else if (string.Equals(p.Name, "BaseColorLayer1", StringComparison.OrdinalIgnoreCase))
                        {
                            layer1Rgb = new Vector3(p.Value.W, p.Value.X, p.Value.Y);
                            hasLayer1Rgb = true;
                        }
                    }

                    bool layersDisabled = hasLayer2 && hasLayer3 && hasLayer4 &&
                                          layer2Alpha <= 0.0001f &&
                                          layer3Alpha <= 0.0001f &&
                                          layer4Alpha <= 0.0001f;

                    bool layer1IsDark = hasLayer1Rgb &&
                                        MathF.Max(layer1Rgb.X, MathF.Max(layer1Rgb.Y, layer1Rgb.Z)) <= 0.12f;

                    bool isGhostAuraOverlayLike = false;
                    if (!layersDisabled && hasLayer2 && hasLayer3 && hasLayer4 && hasLayer1Rgb && hasLayer3Rgb)
                    {
                        bool layer4OffOnly = layer4Alpha <= 0.0001f && layer2Alpha >= 0.999f && layer3Alpha >= 0.999f;
                        bool layer3MatchesLayer1 = (layer3Rgb - layer1Rgb).Length <= 0.03f;
                        isGhostAuraOverlayLike = layer4OffOnly && layer1IsDark && layer3MatchesLayer1;
                    }

                    bool hasSpecular = false;
                    float specularIntensity = 1.0f;
                    float shadowStrength = 0.0f;
                    for (int i = 0; i < floatParams.Length; i++)
                    {
                        var p = floatParams[i];
                        if (p == null || string.IsNullOrWhiteSpace(p.Name))
                        {
                            continue;
                        }

                        if (string.Equals(p.Name, "SpecularIntensity", StringComparison.OrdinalIgnoreCase))
                        {
                            hasSpecular = true;
                            specularIntensity = p.Value;
                        }
                        else if (string.Equals(p.Name, "ShadowStrength", StringComparison.OrdinalIgnoreCase))
                        {
                            shadowStrength = p.Value;
                        }
                    }

                    bool specularOff = hasSpecular && specularIntensity <= 0.0001f;
                    bool strongShadow = shadowStrength >= 0.55f;

                    isAuraShell =
                        (isBodyBLike && layer1IsDark && layersDisabled && specularOff && strongShadow) ||
                        (isBodyBLike && isGhostAuraOverlayLike && specularOff && strongShadow);
                }
            }

            activeShader.SetBoolIfExists("IsAuraShell", isAuraShell);

            if (RenderOptions.TransparentPass)
            {
                int BindGlobalTextureIfExists(string uniformName, int textureId, int preferredSlot)
                {
                    if (textureId == 0)
                    {
                        return preferredSlot;
                    }

                    int slot = preferredSlot;
                    if (slot < 0 || slot > 31 || usedSlots.Contains(slot))
                    {
                        while (usedSlots.Contains(nextSlot) && nextSlot < 32) nextSlot++;
                        slot = Math.Min(nextSlot, 31);
                    }

                    usedSlots.Add(slot);
                    GL.ActiveTexture(TextureUnit.Texture0 + slot);
                    GL.BindTexture(TextureTarget.Texture2D, textureId);
                    activeShader.SetIntIfExists(uniformName, slot);
                    return slot;
                }

                BindGlobalTextureIfExists("SceneColor", RenderOptions.SceneColorTextureId, 30);
                BindGlobalTextureIfExists("SceneDepth", RenderOptions.SceneDepthTextureId, 31);
                BindGlobalTextureIfExists("EnvCubemap", RenderOptions.EnvCubemapTextureId, 29);
            }

            ApplyShaderParams(activeShader, layerMaskUvOverride, aoUvOverride, baseColorMapIsPlaceholder);
            if (MessageHandler.Instance.DebugLogsEnabled &&
                string.Equals(shaderKey, "IkCharacter", StringComparison.OrdinalIgnoreCase) &&
                RenderOptions.UseBackupIkCharacterShader)
            {
                LogIkCharacterColorTableStateOnce();
            }
            bool layerMaskIsPlaceholder = textures.Any(t =>
                string.Equals(t.Name, "LayerMaskMap", StringComparison.OrdinalIgnoreCase) &&
                IsPlaceholderMaskTexturePath(t.SourceFile));
            SetTextureFlags(activeShader, textureNames, layerMaskIsPlaceholder);
            ApplyUnlitAutoScrollFallback(activeShader, textureNames);

            if (MessageHandler.Instance.DebugLogsEnabled &&
                string.Equals(shaderKey, "EyeClearCoat", StringComparison.OrdinalIgnoreCase) &&
                loggedEyeClearCoatParams.Add($"{modelpath}::{Name}"))
            {
                TryGetShaderParamIntWithOverrides("NumMaterialLayer", out int layers);
                TryGetShaderParamIntWithOverrides("UVIndexLayerMask", out int uvMask);
                bool? enableLayerMaskOpt = null;
                for (int i = 0; i < ShaderParams.Count; i++)
                {
                    if (!string.Equals(ShaderParams[i].Name, "EnableLayerMaskMap", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var v = ShaderParams[i].Value?.Trim();
                    if (string.Equals(v, "true", StringComparison.OrdinalIgnoreCase) || string.Equals(v, "1", StringComparison.OrdinalIgnoreCase))
                    {
                        enableLayerMaskOpt = true;
                    }
                    else if (string.Equals(v, "false", StringComparison.OrdinalIgnoreCase) || string.Equals(v, "0", StringComparison.OrdinalIgnoreCase))
                    {
                        enableLayerMaskOpt = false;
                    }
                    break;
                }

                var enableLayerMaskLabel = enableLayerMaskOpt.HasValue ? enableLayerMaskOpt.Value.ToString() : "(missing)";
                MessageHandler.Instance.AddMessage(
                    MessageType.LOG,
                    $"[EyeClearCoat] mat='{Name}' layers={layers} hasLayerMaskTex={textureNames.Contains("LayerMaskMap")} EnableLayerMaskMap={enableLayerMaskLabel} UVIndexLayerMask={uvMask}");
            }

            activeShader.SetBoolIfExists("EnableVertexColor", RenderOptions.EnableVertexColors && hasVertexColors);
            if (!hasVertexColors)
            {
                activeShader.SetBoolIfExists("EnableVertexFoamMask", false);
                activeShader.SetBoolIfExists("EnableVertexAnimationMask", false);
                activeShader.SetBoolIfExists("EnableVertexAlpha", false);
            }
            activeShader.SetBoolIfExists("HasTangents", hasTangents);
            activeShader.SetBoolIfExists("HasBinormals", hasBinormals);
            activeShader.SetBoolIfExists("HasUv1", hasUv1);
            activeShader.SetBoolIfExists("FlipNormalY", RenderOptions.FlipNormalY);
            activeShader.SetBoolIfExists("ReconstructNormalZ", RenderOptions.ReconstructNormalZ);
            SetLightingUniforms(activeShader, view);
            ApplyUniformOverrides(activeShader);
            activeShader.SetMatrix4("model", model);
            activeShader.SetMatrix4("view", view);
            activeShader.SetMatrix4("projection", proj);
        }

        private void ApplyUnlitAutoScrollFallback(Shader activeShader, HashSet<string> textureNames)
        {
            if (!string.Equals(shaderKey, "Unlit", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!textureNames.Contains("DisplacementMap"))
            {
                return;
            }

            if (TryGetUniformOverride("ScrollUVSpeed", out _))
            {
                return;
            }

            if (vec4Params.Any(p => string.Equals(p.Name, "ScrollUVSpeed", StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            if (!activeShader.TryGetUniformType("ScrollUVSpeed", out _))
            {
                return;
            }

            // Fire-like Unlit materials (e.g. Emboar) often rely on shader/time scrolling even without an explicit TRMTR param.
            // Keep this conservative: only apply when a DisplacementMap exists and no other source set ScrollUVSpeed.
            activeShader.SetVector4IfExists("ScrollUVSpeed", new Vector4(0.0f, 0.15f, 0.0f, 0.0f));
        }

        private static readonly HashSet<string> loggedIkCharacterColorTableStates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private void LogIkCharacterColorTableStateOnce()
        {
            var key = $"{modelpath}::{Name}";
            if (!loggedIkCharacterColorTableStates.Add(key))
            {
                return;
            }

            bool TryGetOpt(string name, out string value)
            {
                value = string.Empty;
                for (int i = 0; i < ShaderParams.Count; i++)
                {
                    if (!string.Equals(ShaderParams[i].Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    value = ShaderParams[i].Value ?? string.Empty;
                    return true;
                }
                return false;
            }

            string enableStr = "(missing)";
            if (TryGetUniformOverride("EnableColorTableMap", out var enableOverride))
            {
                enableStr = $"override={enableOverride}";
            }
            else if (TryGetOpt("EnableColorTableMap", out var opt))
            {
                enableStr = $"trmtr={opt}";
            }

            int divide = 0;
            bool hasDivide = TryGetShaderParamIntEffective("ColorTableDivideNumber", out divide);
            int i1 = 0, i2 = 0, i3 = 0, i4 = 0;
            bool hasI1 = TryGetShaderParamIntEffective("BaseColorIndex1", out i1);
            bool hasI2 = TryGetShaderParamIntEffective("BaseColorIndex2", out i2);
            bool hasI3 = TryGetShaderParamIntEffective("BaseColorIndex3", out i3);
            bool hasI4 = TryGetShaderParamIntEffective("BaseColorIndex4", out i4);

            bool hasLayerColors =
                vec4Params.Any(p => string.Equals(p.Name, "BaseColorLayer1", StringComparison.OrdinalIgnoreCase)) ||
                vec4Params.Any(p => string.Equals(p.Name, "ShadowingColorLayer1", StringComparison.OrdinalIgnoreCase));

            MessageHandler.Instance.AddMessage(
                MessageType.LOG,
                $"[ColorTable] IkCharacter mat='{Name}' EnableColorTableMap={enableStr} Divide={(hasDivide ? divide.ToString() : "(missing)")} " +
                $"Idx={(hasI1 ? i1.ToString() : "?")},{(hasI2 ? i2.ToString() : "?")},{(hasI3 ? i3.ToString() : "?")},{(hasI4 ? i4.ToString() : "?")} " +
                $"HasLayerColors={hasLayerColors}");
        }

    }
}
