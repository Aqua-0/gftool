using GFTool.Core.Utils;
using GFTool.Renderer.Core;
using GFTool.Renderer.Core.Graphics;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System;
using System.Globalization;
using System.Drawing;
using System.Linq;
using Trinity.Core.Flatbuffers.TR.Model;
using Trinity.Core.Flatbuffers.Utils;
using Trinity.Core.Assets;

namespace GFTool.Renderer.Scene.GraphicsObjects
{
	    public partial class Material : IDisposable
	    {
        private static readonly HashSet<string> warnedMissingSkinningUniforms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> warnedMissingSamplerBindings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> warnedMissingEyeClearCoatForward = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> loggedEyeClearCoatParams = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private enum TransparentBlendMode
        {
            Alpha,
            PremultipliedAlpha,
            Additive
        }

        private static TransparentBlendMode? lastTransparentBlendMode;

        public string Name { get; set; }
        public IReadOnlyList<Texture> Textures => textures;

	        private Shader? shader;
	        private List<Texture> textures;
	        private List<Texture>? retiredTextures;
		        private readonly string shaderKey;
		        private readonly bool isTransparent;
		        private readonly TransparentBlendMode transparentBlendMode;
	        private readonly Trinity.Core.Flatbuffers.TR.Model.TrmtrCullMode? trmtrCullMode;
	        private readonly Trinity.Core.Flatbuffers.TR.Model.TrmtrFrontFace? trmtrFrontFace;
	        private readonly string? trmtrBlendStatePreset;

		        private PathString modelpath;
		        private readonly IAssetProvider? assetProvider;

	        private List<(string Name, string Value)> ShaderParams;
	        private TRFloatParameter[] floatParams;
	        private TRVec2fParameter[] vec2Params;
	        private TRVec3fParameter[] vec3Params;
	        private TRVec4fParameter[] vec4Params;
	        private TRSampler[] samplers;

        private static readonly HashSet<string> reservedOverrideUniformNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "model",
            "view",
            "projection",
            "Bones",
            "BoneCount",
            "EnableSkinning",
            "SwapBlendOrder"
        };

        private readonly object overrideLock = new object();
        private readonly Dictionary<string, object> uniformOverrides = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        private bool colorTableCacheReady;
        private int colorTableDivideCached;
        private Vector3[]? colorTableBaseColorsCached;
        private Vector3[]? colorTableShadowColorsCached;

		        public Material(
		            PathString modelPath,
		            TRMaterial trmat,
		            IAssetProvider? assetProvider = null,
		            Trinity.Core.Flatbuffers.TR.Model.TrmtrFileRasterizationState? rasterizationState = null,
		            string? blendStatePreset = null)
		        {
		            Name = trmat.Name;
		            modelpath = modelPath;
		            this.assetProvider = assetProvider;
		            trmtrCullMode = rasterizationState?.CullMode;
		            trmtrFrontFace = rasterizationState?.FrontFace;
		            trmtrBlendStatePreset = string.IsNullOrWhiteSpace(blendStatePreset) ? null : blendStatePreset;

	            ShaderParams = new List<(string Name, string Value)>();
	            floatParams = trmat.FloatParams ?? Array.Empty<TRFloatParameter>();
	            vec2Params = trmat.Vec2fParams ?? Array.Empty<TRVec2fParameter>();
	            vec3Params = trmat.Vec3fParams ?? Array.Empty<TRVec3fParameter>();
	            vec4Params = trmat.Vec4fParams ?? Array.Empty<TRVec4fParameter>();
		            samplers = trmat.Samplers ?? Array.Empty<TRSampler>();
		            textures = new List<Texture>();
		            retiredTextures = new List<Texture>();

            //I hope we dont actually have more than one shader per material
            var shaderName = trmat.Shader?.Length > 0 ? trmat.Shader[0].Name : string.Empty;
            shaderKey = ResolveShaderName(shaderName);
            // Shader compilation/linking requires a current GL context. Defer acquisition until first use
            // (or explicit warmup) so materials can be created off the render thread.
            shader = null!;

            string? techniqueName = null;
            if (trmat.Shader != null && trmat.Shader.Length > 0 && trmat.Shader[0].Values != null)
            {
                foreach (var param in trmat.Shader[0].Values)
                {
                    if (param == null)
                    {
                        continue;
                    }
                    if (string.Equals(param.Name, "__TechniqueName", StringComparison.OrdinalIgnoreCase))
                    {
                        techniqueName = param.Value;
                        break;
                    }
                }
            }

            bool isTransparentByTechnique = !string.IsNullOrWhiteSpace(techniqueName) &&
                                            techniqueName.Contains("Transparent", StringComparison.OrdinalIgnoreCase);

            bool isTransparentByAuraTextures = string.Equals(shaderKey, "IkCharacter", StringComparison.OrdinalIgnoreCase) &&
                                               (trmat.Textures?.Any(t =>
                                                   t != null &&
                                                   (string.Equals(t.Name, "AuraColorMap", StringComparison.OrdinalIgnoreCase) ||
                                                    string.Equals(t.Name, "AuraNoiseMap", StringComparison.OrdinalIgnoreCase))) ??
                                                false);

            bool isAuraShellMaskSet = false;
            if (string.Equals(shaderKey, "IkCharacter", StringComparison.OrdinalIgnoreCase) && trmat.Textures != null)
            {
                string? shadowingColor = null;
                string? shadowingMask = null;
                string? rimMask = null;
                string? baseColorMap = null;
                for (int i = 0; i < trmat.Textures.Length; i++)
                {
                    var t = trmat.Textures[i];
                    if (t == null)
                    {
                        continue;
                    }
                    if (string.Equals(t.Name, "BaseColorMap", StringComparison.OrdinalIgnoreCase))
                    {
                        baseColorMap = t.File;
                    }
                    if (string.Equals(t.Name, "ShadowingColorMap", StringComparison.OrdinalIgnoreCase))
                    {
                        shadowingColor = t.File;
                    }
                    else if (string.Equals(t.Name, "ShadowingColorMaskMap", StringComparison.OrdinalIgnoreCase))
                    {
                        shadowingMask = t.File;
                    }
                    else if (string.Equals(t.Name, "RimLightMaskMap", StringComparison.OrdinalIgnoreCase))
                    {
                        rimMask = t.File;
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

                    bool specularOff = false;
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

                    specularOff = hasSpecular && specularIntensity <= 0.0001f;
                    bool strongShadow = shadowStrength >= 0.55f;

                    isAuraShellMaskSet =
                        (isBodyBLike && layer1IsDark && layersDisabled && specularOff && strongShadow) ||
                        (isBodyBLike && isGhostAuraOverlayLike && specularOff && strongShadow);
                }
            }

            bool isTransparentByBlendPreset = false;
            if (!string.IsNullOrWhiteSpace(trmtrBlendStatePreset) &&
                !string.Equals(trmtrBlendStatePreset, "Opaque", StringComparison.OrdinalIgnoreCase))
            {
                var preset = trmtrBlendStatePreset.Trim();
                isTransparentByBlendPreset =
                    preset.Contains("Blend", StringComparison.OrdinalIgnoreCase) ||
                    preset.Contains("Alpha", StringComparison.OrdinalIgnoreCase) ||
                    preset.Contains("Add", StringComparison.OrdinalIgnoreCase);
            }

            isTransparent =
                Name.Contains("eye_lens", StringComparison.OrdinalIgnoreCase) ||
                isTransparentByTechnique ||
                isTransparentByBlendPreset ||
                isTransparentByAuraTextures ||
                isAuraShellMaskSet ||
                string.Equals(shaderKey, "Water", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(shaderKey, "Transparent", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(shaderKey, "EyeClearCoatForward", StringComparison.OrdinalIgnoreCase);

            transparentBlendMode = TransparentBlendMode.Alpha;
            if (isTransparentByTechnique)
            {
                transparentBlendMode = TransparentBlendMode.PremultipliedAlpha;
            }
            else if (isAuraShellMaskSet)
            {
                transparentBlendMode = TransparentBlendMode.PremultipliedAlpha;
            }
            else if (!string.IsNullOrWhiteSpace(trmtrBlendStatePreset))
            {
                var preset = trmtrBlendStatePreset;
                if (preset.Contains("Add", StringComparison.OrdinalIgnoreCase))
                {
                    transparentBlendMode = TransparentBlendMode.Additive;
                }
                else if (preset.Contains("Premult", StringComparison.OrdinalIgnoreCase))
                {
                    transparentBlendMode = TransparentBlendMode.PremultipliedAlpha;
                }
                else if (preset.Contains("PreMulti", StringComparison.OrdinalIgnoreCase) ||
                         preset.Contains("PreMul", StringComparison.OrdinalIgnoreCase))
                {
                    transparentBlendMode = TransparentBlendMode.PremultipliedAlpha;
                }
            }

            if (trmat.Shader != null && trmat.Shader.Length > 0 && trmat.Shader[0].Values != null)
            {
                foreach (var param in trmat.Shader[0].Values)
                {
                    ShaderParams.Add((param.Name, param.Value));
                }
            }

            var samplersBySlot = new Dictionary<uint, TRSampler>();
            if (trmat.Samplers != null)
            {
                for (int i = 0; i < trmat.Samplers.Length; i++)
                {
                    samplersBySlot[(uint)i] = trmat.Samplers[i];
                }
            }

		            foreach (var tex in trmat.Textures ?? Array.Empty<TRTexture>())
		            {
                if (!samplersBySlot.TryGetValue(tex.Slot, out var sampler) && MessageHandler.Instance.DebugLogsEnabled)
                {
                    var key = $"{modelpath}::{Name}::{tex.Name}::{tex.Slot}";
                    if (warnedMissingSamplerBindings.Add(key))
                    {
                        MessageHandler.Instance.AddMessage(
                            MessageType.WARNING,
                            $"[Sampler] Missing sampler for mat='{Name}' tex='{tex.Name}' SamplerId={tex.Slot} (defaults to ClampToEdge)");
                    }
                }
		                textures.Add(new Texture(modelPath, tex, sampler, assetProvider));
		            }

            TryApplyColorTableOverrides();
            if (MessageHandler.Instance.DebugLogsEnabled &&
                string.Equals(shaderKey, "IkCharacter", StringComparison.OrdinalIgnoreCase))
            {
                bool TryGetVec4(string name, out Vector4 value)
                {
                    for (int i = 0; i < vec4Params.Length; i++)
                    {
                        if (!string.Equals(vec4Params[i].Name, name, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        var v = vec4Params[i].Value;
                        value = new Vector4(v.W, v.X, v.Y, v.Z);
                        return true;
                    }

                    value = default;
                    return false;
                }

                bool hasUvScaleOffset = TryGetVec4("UVScaleOffset", out var uvScaleOffset);
                bool nonIdentityUv = hasUvScaleOffset &&
                                     (Math.Abs(uvScaleOffset.X - 1.0f) > 0.0001f ||
                                      Math.Abs(uvScaleOffset.Y - 1.0f) > 0.0001f ||
                                      Math.Abs(uvScaleOffset.Z) > 0.0001f ||
                                      Math.Abs(uvScaleOffset.W) > 0.0001f);

                const TextureWrapMode mirrorClampToEdge = (TextureWrapMode)0x8743;
                bool hasMirroredSampler = false;
                for (int i = 0; i < textures.Count; i++)
                {
                    var wrapS = textures[i].WrapS;
                    var wrapT = textures[i].WrapT;
                    if (wrapS == TextureWrapMode.MirroredRepeat || wrapT == TextureWrapMode.MirroredRepeat ||
                        wrapS == mirrorClampToEdge || wrapT == mirrorClampToEdge)
                    {
                        hasMirroredSampler = true;
                        break;
                    }
                }

                if (nonIdentityUv || hasMirroredSampler)
                {
                    var uvLabel = hasUvScaleOffset ? $"({uvScaleOffset.X:0.###},{uvScaleOffset.Y:0.###},{uvScaleOffset.Z:0.###},{uvScaleOffset.W:0.###})" : "(missing)";
                    var samplerLabel = string.Join(", ", textures.Select(t => $"{t.Name}[{t.WrapS}/{t.WrapT}]"));
                    MessageHandler.Instance.AddMessage(
                        MessageType.LOG,
                        $"[UV] IkCharacter mat='{Name}' UVScaleOffset={uvLabel} samplers={samplerLabel}");
                }
            }
		        }

	        public void Dispose()
	        {
	            foreach (var tex in textures)
	                tex.Dispose();
	            if (retiredTextures != null)
	            {
	                foreach (var tex in retiredTextures)
	                {
	                    tex.Dispose();
	                }
	                retiredTextures.Clear();
	            }
	        }

        public bool IsTransparent => isTransparent;
	        public IReadOnlyList<(string Name, string Value)> ShaderParameters => ShaderParams;
	        public IReadOnlyList<TRFloatParameter> FloatParameters => floatParams;
	        public IReadOnlyList<TRVec2fParameter> Vec2Parameters => vec2Params;
	        public IReadOnlyList<TRVec3fParameter> Vec3Parameters => vec3Params;
	        public IReadOnlyList<TRVec4fParameter> Vec4Parameters => vec4Params;
	        public IReadOnlyList<TRSampler> Samplers => samplers;
		        public string ShaderName => shaderKey;
		        public Trinity.Core.Flatbuffers.TR.Model.TrmtrCullMode? TrmtrCullMode => trmtrCullMode;
		        public Trinity.Core.Flatbuffers.TR.Model.TrmtrFrontFace? TrmtrFrontFace => trmtrFrontFace;

	        public bool TrySetTextureSourceFile(string textureName, string newSourceFile, out string? error)
	        {
	            error = null;
	            if (string.IsNullOrWhiteSpace(textureName))
	            {
	                error = "Texture name is required.";
	                return false;
	            }
	            if (newSourceFile == null)
	            {
	                error = "Texture file path is required.";
	                return false;
	            }

	            int index = -1;
	            Texture? existing = null;
	            for (int i = 0; i < textures.Count; i++)
	            {
	                if (string.Equals(textures[i].Name, textureName, StringComparison.OrdinalIgnoreCase))
	                {
	                    index = i;
	                    existing = textures[i];
	                    break;
	                }
	            }

	            if (index < 0 || existing == null)
	            {
	                error = $"Texture not found: '{textureName}'.";
	                return false;
	            }

	            string trimmed = newSourceFile.Trim();
	            if (string.Equals(existing.SourceFile ?? string.Empty, trimmed, StringComparison.OrdinalIgnoreCase))
	            {
	                return true;
	            }

	            var slot = existing.Slot;
	            TRSampler? sampler = null;
	            if (slot < (uint)samplers.Length)
	            {
	                sampler = samplers[(int)slot];
	            }

	            var trTex = new TRTexture
	            {
	                Name = existing.Name ?? string.Empty,
	                File = trimmed,
	                Slot = slot
	            };

	            Texture replacement;
	            try
	            {
	                replacement = new Texture(modelpath, trTex, sampler, assetProvider);
	                replacement.BeginAsyncLoadIfEnabled();
	            }
	            catch (Exception ex)
	            {
	                error = ex.Message;
	                return false;
	            }

	            var newList = new List<Texture>(textures.Count);
	            for (int i = 0; i < textures.Count; i++)
	            {
	                newList.Add(i == index ? replacement : textures[i]);
	            }

	            textures = newList;
	            retiredTextures?.Add(existing);
	            return true;
	        }

	    }
}
