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
using System.Diagnostics;


namespace GFTool.Renderer.Scene.GraphicsObjects
{
		    public partial class Model : RefObject
		    {
		        private bool hasMaterialSourceEdits;
		        private readonly List<NewMaterialCloneRequest> newMaterialCloneRequests = new List<NewMaterialCloneRequest>();

		        public bool HasMaterialSourceEdits => hasMaterialSourceEdits;

		        public enum NewMaterialTrmmtCloneMode
		        {
		            None = 0,
		            Safe = 1,
		            Unsafe = 2
		        }

		        public readonly struct NewMaterialCloneRequest
		        {
		            public NewMaterialCloneRequest(string templateName, string newName, NewMaterialTrmmtCloneMode trmmtCloneMode)
		            {
		                TemplateName = templateName ?? string.Empty;
		                NewName = newName ?? string.Empty;
		                TrmmtCloneMode = trmmtCloneMode;
		            }

		            public string TemplateName { get; }
		            public string NewName { get; }
		            public NewMaterialTrmmtCloneMode TrmmtCloneMode { get; }
		        }

		        public IReadOnlyList<NewMaterialCloneRequest> GetNewMaterialCloneRequestsSnapshot()
		        {
		            return newMaterialCloneRequests.ToArray();
		        }

		        public bool HasNewMaterialCloneRequests => newMaterialCloneRequests.Count > 0;

			        public bool TryAddClonedMaterial(
			            string templateMaterialName,
			            string desiredNewMaterialName,
			            NewMaterialTrmmtCloneMode trmmtCloneMode,
			            out string createdMaterialName,
			            out string? error)
		        {
		            createdMaterialName = string.Empty;
		            error = null;

		            if (string.IsNullOrWhiteSpace(templateMaterialName))
		            {
		                error = "Template material name is required.";
		                return false;
		            }

		            if (string.IsNullOrWhiteSpace(desiredNewMaterialName))
		            {
		                error = "New material name is required.";
		                return false;
		            }

		            if (materials == null || materials.Length == 0)
		            {
		                error = "Model has no loaded materials.";
		                return false;
		            }

		            var template = materials.FirstOrDefault(m => m != null && string.Equals(m.Name, templateMaterialName, StringComparison.OrdinalIgnoreCase));
		            if (template == null)
		            {
		                error = $"Template material not found: '{templateMaterialName}'.";
		                return false;
		            }

		            string baseName = desiredNewMaterialName.Trim();
		            if (baseName.Length == 0)
		            {
		                error = "New material name is required.";
		                return false;
		            }

		            string unique = baseName;
		            int suffix = 1;
		            while (materials.Any(m => m != null && string.Equals(m.Name, unique, StringComparison.OrdinalIgnoreCase)) ||
		                   newMaterialCloneRequests.Any(r => string.Equals(r.NewName, unique, StringComparison.OrdinalIgnoreCase)))
		            {
		                unique = $"{baseName}_{suffix}";
		                suffix++;
		            }

		            var shaderParams = template.ShaderParameters
		                .Select(p => new TRStringParameter { Name = p.Name ?? string.Empty, Value = p.Value ?? string.Empty })
		                .ToArray();

		            var trmat = new TRMaterial
		            {
		                Name = unique,
		                Shader = new[]
		                {
		                    new TRMaterialShader
		                    {
		                        Name = template.ShaderName,
		                        Values = shaderParams
		                    }
		                },
		                Textures = template.Textures
		                    .Select(t => new TRTexture { Name = t.Name ?? string.Empty, File = t.SourceFile ?? string.Empty, Slot = (uint)t.Slot })
		                    .ToArray(),
		                Samplers = template.Samplers
		                    .Select(s => new TRSampler
		                    {
		                        State0 = s.State0,
		                        State1 = s.State1,
		                        State2 = s.State2,
		                        State3 = s.State3,
		                        State4 = s.State4,
		                        State5 = s.State5,
		                        State6 = s.State6,
		                        State7 = s.State7,
		                        State8 = s.State8,
		                        RepeatU = s.RepeatU,
		                        RepeatV = s.RepeatV,
		                        RepeatW = s.RepeatW,
		                        BorderColor = s.BorderColor
		                    })
		                    .ToArray(),
		                FloatParams = template.FloatParameters
		                    .Select(p => new TRFloatParameter { Name = p.Name ?? string.Empty, Value = p.Value })
		                    .ToArray(),
		                Vec2fParams = template.Vec2Parameters
		                    .Select(p => new TRVec2fParameter { Name = p.Name ?? string.Empty, Value = p.Value })
		                    .ToArray(),
		                Vec3fParams = template.Vec3Parameters
		                    .Select(p => new TRVec3fParameter { Name = p.Name ?? string.Empty, Value = p.Value })
		                    .ToArray(),
		                Vec4fParams = template.Vec4Parameters
		                    .Select(p => new TRVec4fParameter { Name = p.Name ?? string.Empty, Value = p.Value })
		                    .ToArray()
		            };

		            TrmtrFileRasterizationState? rast = null;
		            if (template.TrmtrCullMode.HasValue || template.TrmtrFrontFace.HasValue)
		            {
		                rast = new TrmtrFileRasterizationState
		                {
		                    CullMode = template.TrmtrCullMode ?? TrmtrCullMode.Back,
		                    FrontFace = template.TrmtrFrontFace ?? TrmtrFrontFace.CCW,
		                    DepthBias = 0,
		                    SlopeScaledDepthBias = 0.0f,
		                    DepthBiasClamp = 0.0f
		                };
		            }

		            var materialPath = new PathString(currentMaterialFilePath ?? string.Empty);
		            var clonedRuntime = new Material(materialPath, trmat, assetProvider, rast);

		            var newList = materials.ToList();
		            newList.Add(clonedRuntime);
		            materials = newList.ToArray();
		            BuildMaterialMap();

		            newMaterialCloneRequests.Add(new NewMaterialCloneRequest(template.Name, unique, trmmtCloneMode));
		            hasMaterialSourceEdits = true;
			            createdMaterialName = unique;
			            return true;
			        }

			        public bool TrySetMaterialTextureSourceFile(string materialName, string textureName, string newSourceFile, out string? error)
			        {
			            error = null;
			            if (string.IsNullOrWhiteSpace(materialName))
			            {
			                error = "Material name is required.";
			                return false;
			            }
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

			            if (materials == null || materials.Length == 0)
			            {
			                error = "Model has no loaded materials.";
			                return false;
			            }

			            var mat = materials.FirstOrDefault(m => m != null && string.Equals(m.Name, materialName, StringComparison.OrdinalIgnoreCase));
			            if (mat == null)
			            {
			                error = $"Material not found: '{materialName}'.";
			                return false;
			            }

			            if (!mat.TrySetTextureSourceFile(textureName, newSourceFile, out error))
			            {
			                return false;
			            }

			            hasMaterialSourceEdits = true;
			            return true;
			        }

			        public void ApplyTrmtrFile(string sourceMaterialPath, TrmtrFile trmtr, bool preserveMaterialUniformOverrides = false)
			        {
			            if (trmtr == null) throw new ArgumentNullException(nameof(trmtr));

		            currentMaterialFilePath = sourceMaterialPath;
		            hasMaterialSourceEdits = true;
		            newMaterialCloneRequests.Clear();

		            if (!preserveMaterialUniformOverrides)
		            {
		                // JSON application is intended to replace the underlying material data, so any prior uniform
		                // overrides would mask the edited values.
		                string scope = GetMaterialUniformOverrideScopeKey();
		                materialUniformOverrideStateByMaterialFile.Remove(scope);
		            }

		            if (materials != null)
		            {
		                foreach (var existing in materials)
		                {
		                    existing?.Dispose();
		                }
		            }

			            var materialPath = new PathString(sourceMaterialPath ?? string.Empty);
			            var shaderGame = ResolveEffectiveShaderGame(trmtr, assetProvider);
			            this.shaderGame = shaderGame;

				            if (trmtr.Materials == null || trmtr.Materials.Length == 0)
				            {
				                MessageHandler.Instance.AddMessage(
				                    MessageType.WARNING,
				                    $"[TRMTR] No materials found; using fallback Unlit material: {sourceMaterialPath}");
				                this.shaderGame = ShaderGame.Auto;
				                materials = new[] { CreateFallbackUnlitMaterial(new PathString(sourceMaterialPath ?? string.Empty), assetProvider, "__fallback_unlit") };
				                BuildMaterialMap();
				                ApplyMaterialMetadataOverridesToRuntimeMaterials();
				                ApplyMaterialUniformOverridesToRuntimeMaterials();
				                return;
				            }

			            var matlist = new List<Material>();
				            for (int i = 0; i < trmtr.Materials.Length; i++)
				            {
			                var src = trmtr.Materials[i];
			                var trmat = ConvertTrmtrMaterial(src, shaderGame, legacySamplers: null);
			                matlist.Add(new Material(materialPath, trmat, assetProvider, src?.RasterizationState, src?.BlendStatePreset));
			            }

		            materials = matlist.ToArray();
		            BuildMaterialMap();
		            ApplyMaterialMetadataOverridesToRuntimeMaterials();
		            ApplyMaterialUniformOverridesToRuntimeMaterials();
		        }

	        private void ParseMaterial(string file)
	        {
	            ParseMaterial(file, preserveMaterialMetadata: false);
	        }

		        private void ParseMaterial(string file, bool preserveMaterialMetadata)
		        {
                    long parseStart = Stopwatch.GetTimestamp();
		            hasMaterialSourceEdits = false;
		            currentMaterialFilePath = file;
		            currentMaterialSetName = null;
		            newMaterialCloneRequests.Clear();

		            if (!preserveMaterialMetadata)
		            {
		                defaultMaterialFilePath = file;
		                materialMetadata = null;
		                materialMetadataPath = null;
		                materialMetadataSelections.Clear();
		                materialMetadataValueOverrides.Clear();
		                materialMetadataLastAppliedUniformNames.Clear();
		            }

	            if (materials != null)
	            {
	                foreach (var existing in materials)
                {
                    existing?.Dispose();
                }
            }

			            List<Material> matlist = new List<Material>();
		            var materialPath = new PathString(file);
                    long materialReadStart = Stopwatch.GetTimestamp();
		            var trmtrBytes = assetProvider.ReadAllBytes(file);
                    prepareMaterialReadMs += Stopwatch.GetElapsedTime(materialReadStart).TotalMilliseconds;
		            TrmtrFile? trmtr = null;
                    long deserializeStart = Stopwatch.GetTimestamp();
		            try
		            {
		                trmtr = FlatBufferConverter.DeserializeFrom<TrmtrFile>(trmtrBytes);
		            }
		            catch (Exception ex)
		            {
		                trmtr = null;
		                if (MessageHandler.Instance.DebugLogsEnabled)
		                {
		                    MessageHandler.Instance.AddMessage(
		                        MessageType.WARNING,
		                        $"[TRMTR] Failed to deserialize TrmtrFile; falling back to legacy TRMTR: '{file}' ({ex.GetType().Name})");
		                }
		            }
                    prepareMaterialDeserializeMs += Stopwatch.GetElapsedTime(deserializeStart).TotalMilliseconds;

		            static bool IsLikelyValid(TrmtrFile candidate)
		            {
		                if (candidate.Materials == null || candidate.Materials.Length == 0)
		                {
		                    return false;
		                }

		                for (int i = 0; i < candidate.Materials.Length; i++)
		                {
		                    var mat = candidate.Materials[i];
		                    if (mat == null || string.IsNullOrWhiteSpace(mat.Name))
		                    {
		                        return false;
		                    }

		                    var shaders = mat.Shaders;
		                    if (shaders == null || shaders.Length == 0 || shaders[0] == null || string.IsNullOrWhiteSpace(shaders[0].Name))
		                    {
		                        return false;
		                    }
		                }

		                return true;
		            }

		            if (trmtr != null && !IsLikelyValid(trmtr))
		            {
		                trmtr = null;
		                if (MessageHandler.Instance.DebugLogsEnabled)
		                {
		                    MessageHandler.Instance.AddMessage(
		                        MessageType.WARNING,
		                        $"[TRMTR] TrmtrFile failed validation; falling back to legacy TRMTR: '{file}'");
		                }
		            }

                    TRMTR? legacyTrmtr = null;
                    bool requiresLegacyMaterials = trmtr?.Materials == null || trmtr.Materials.Length == 0;
                    bool mayNeedLegacySamplers =
                        trmtr?.Materials?.Any(material => material?.Textures != null && material.Textures.Length > 0) == true;

                    if (requiresLegacyMaterials || mayNeedLegacySamplers)
                    {
                        long legacyDeserializeStart = Stopwatch.GetTimestamp();
                        try
                        {
                            legacyTrmtr = FlatBufferConverter.DeserializeFrom<TRMTR>(trmtrBytes);
                        }
                        catch
                        {
                            legacyTrmtr = null;
                        }
                        prepareMaterialLegacyDeserializeMs += Stopwatch.GetElapsedTime(legacyDeserializeStart).TotalMilliseconds;
                    }

                    var legacySamplersByMaterialName =
                        new Dictionary<string, TRSampler[]>(StringComparer.OrdinalIgnoreCase);
                    if (legacyTrmtr?.Materials != null)
                    {
                        foreach (var legacyMaterial in legacyTrmtr.Materials)
                        {
                            if (legacyMaterial == null || string.IsNullOrWhiteSpace(legacyMaterial.Name))
                            {
                                continue;
                            }

                            legacySamplersByMaterialName[legacyMaterial.Name] =
                                legacyMaterial.Samplers ?? Array.Empty<TRSampler>();
                        }
                    }

				            if (requiresLegacyMaterials)
				            {
                                long buildStart = Stopwatch.GetTimestamp();
				                if (legacyTrmtr?.Materials == null || legacyTrmtr.Materials.Length == 0)
				                {
				                    MessageHandler.Instance.AddMessage(
				                        MessageType.WARNING,
				                        $"[TRMTR] No materials found (new+legacy); using fallback Unlit material: {file}");
				                    this.shaderGame = ShaderGame.Auto;
				                    matlist.Add(CreateFallbackUnlitMaterial(materialPath, assetProvider, "__fallback_unlit"));
				                    materials = matlist.ToArray();
				                    BuildMaterialMap();
				                    ApplyMaterialMetadataOverridesToRuntimeMaterials();
				                    ApplyMaterialUniformOverridesToRuntimeMaterials();
                                    prepareMaterialBuildMs += Stopwatch.GetElapsedTime(buildStart).TotalMilliseconds;
                                    prepareMaterialMs += Stopwatch.GetElapsedTime(parseStart).TotalMilliseconds;
				                    return;
				                }

				                this.shaderGame = ShaderGame.SCVI;
                                matlist = new List<Material>(legacyTrmtr.Materials.Length);
				                for (int i = 0; i < legacyTrmtr.Materials.Length; i++)
				                {
			                    var src = legacyTrmtr.Materials[i];
			                    if (src == null)
			                    {
			                        continue;
			                    }
			                    matlist.Add(new Material(materialPath, src, assetProvider));
			                }
                                prepareMaterialBuildMs += Stopwatch.GetElapsedTime(buildStart).TotalMilliseconds;
			            }
			            else
			            {
                                var validTrmtr = trmtr!;
                                long buildStart = Stopwatch.GetTimestamp();
			                var shaderGame = ResolveEffectiveShaderGame(validTrmtr, assetProvider);
			                this.shaderGame = shaderGame;
                                matlist = new List<Material>(validTrmtr.Materials.Length);

				                for (int i = 0; i < validTrmtr.Materials.Length; i++)
				                {
				                    var src = validTrmtr.Materials[i];
                                    legacySamplersByMaterialName.TryGetValue(
                                        src?.Name ?? string.Empty,
                                        out var legacySamplers);
                                    if ((legacySamplers == null || legacySamplers.Length == 0) &&
                                        legacyTrmtr?.Materials != null &&
                                        i < legacyTrmtr.Materials.Length)
                                    {
                                        legacySamplers = legacyTrmtr.Materials[i]?.Samplers;
                                    }

				                    var trmat = ConvertTrmtrMaterial(src, shaderGame, legacySamplers);
				                    matlist.Add(new Material(materialPath, trmat, assetProvider, src?.RasterizationState, src?.BlendStatePreset));
				                }
                                prepareMaterialBuildMs += Stopwatch.GetElapsedTime(buildStart).TotalMilliseconds;
			            }
					            materials = matlist.ToArray();
					            BuildMaterialMap();
					            ApplyMaterialMetadataOverridesToRuntimeMaterials();
					            ApplyMaterialUniformOverridesToRuntimeMaterials();
                                prepareMaterialMs += Stopwatch.GetElapsedTime(parseStart).TotalMilliseconds;
				        }

		        private static Material CreateFallbackUnlitMaterial(PathString materialPath, IAssetProvider assetProvider, string name)
		        {
		            var trmat = new TRMaterial
		            {
		                Name = name,
		                Shader = new[]
		                {
		                    new TRMaterialShader
		                    {
		                        Name = "Unlit",
		                        Values = new[]
		                        {
		                            new TRStringParameter { Name = "__TechniqueName", Value = "Standard" }
		                        }
		                    }
		                },
		                Textures = Array.Empty<TRTexture>(),
		                Samplers = Array.Empty<TRSampler>(),
		                FloatParams = Array.Empty<TRFloatParameter>(),
		                Vec2fParams = Array.Empty<TRVec2fParameter>(),
		                Vec3fParams = Array.Empty<TRVec3fParameter>(),
		                Vec4fParams = Array.Empty<TRVec4fParameter>()
		            };

		            return new Material(materialPath, trmat, assetProvider);
		        }

	        private static TRMaterial ConvertTrmtrMaterial(TrmtrFileMaterial? src, ShaderGame game, TRSampler[]? legacySamplers)
	        {
	            var shaderParams = new List<TRStringParameter>();

	            string techniqueName = src?.Shaders?.FirstOrDefault()?.Name ?? "Standard";
	            shaderParams.Add(new TRStringParameter { Name = "__TechniqueName", Value = techniqueName });

	            if (src?.Shaders != null)
	            {
	                foreach (var shader in src.Shaders)
	                {
	                    if (shader?.Values == null) continue;
	                    foreach (var p in shader.Values)
	                    {
	                        if (p == null || string.IsNullOrWhiteSpace(p.Name)) continue;
	                        shaderParams.Add(new TRStringParameter { Name = p.Name, Value = p.Value });
	                    }
	                }
	            }

	            if (src?.IntParameters != null)
	            {
	                foreach (var p in src.IntParameters)
	                {
	                    if (p == null || string.IsNullOrWhiteSpace(p.Name)) continue;
	                    shaderParams.Add(new TRStringParameter { Name = p.Name, Value = p.Value.ToString() });
	                }
	            }

	            string shaderName = MapTechniqueToShaderName(techniqueName, game);

	            var textures = src?.Textures?.Select(t => new TRTexture
	            {
	                Name = t?.Name ?? string.Empty,
	                File = t?.File ?? string.Empty,
	                Slot = t?.Slot ?? 0
	            }).ToArray() ?? Array.Empty<TRTexture>();

            var samplers = src?.Samplers?.Select(s => new TRSampler
            {
                State0 = s?.State0 ?? 0,
                State1 = s?.State1 ?? 0,
                State2 = s?.State2 ?? 0,
	                State3 = s?.State3 ?? 0,
	                State4 = s?.State4 ?? 0,
	                State5 = s?.State5 ?? 0,
	                State6 = s?.State6 ?? 0,
	                State7 = s?.State7 ?? 0,
	                State8 = s?.State8 ?? 0,
	                RepeatU = s?.RepeatU ?? UVWrapMode.WRAP,
                RepeatV = s?.RepeatV ?? UVWrapMode.WRAP,
                RepeatW = s?.RepeatW ?? UVWrapMode.WRAP,
                BorderColor = s?.BorderColor ?? new Trinity.Core.Flatbuffers.Utils.RGBA(),
            }).Select(NormalizeSamplerWrapModes).ToArray() ?? Array.Empty<TRSampler>();

            if (legacySamplers != null && legacySamplers.Length > 0)
            {
                bool shouldPreferLegacy = samplers.Length == 0;
	                if (!shouldPreferLegacy)
	                {
	                    int check = Math.Min(samplers.Length, legacySamplers.Length);
	                    int swapMatches = 0;
	                    int considered = 0;
	                    for (int i = 0; i < check; i++)
	                    {
	                        var a = samplers[i];
	                        var b = legacySamplers[i];
	                        if (a == null || b == null)
	                        {
	                            continue;
	                        }

	                        considered++;
	                        if (a.RepeatV == b.RepeatW && a.RepeatW == b.RepeatV)
	                        {
	                            swapMatches++;
	                        }
	                    }

	                    if (considered > 0 && swapMatches >= Math.Max(1, considered / 2))
	                    {
	                        shouldPreferLegacy = true;
	                    }
	                }

	                if (shouldPreferLegacy)
	                {
	                    // Use legacy samplers if present so wrap/filter state isn't silently lost or mis-decoded
	                    // (defaults to ClampToEdge looks like broken UVs / collapsed previews).
	                    samplers = legacySamplers.Select(NormalizeLegacySampler).Select(NormalizeSamplerWrapModes).ToArray();
	                }
                    else if (legacySamplers.Length > samplers.Length)
                    {
                        // The newer schema is authoritative for slots it contains, but some
                        // files omit trailing sampler records even though their textures use
                        // those slots. Fill only the missing tail from the legacy schema.
                        var combined = new TRSampler[legacySamplers.Length];
                        Array.Copy(samplers, combined, samplers.Length);
                        for (int i = samplers.Length; i < combined.Length; i++)
                        {
                            combined[i] = NormalizeSamplerWrapModes(
                                NormalizeLegacySampler(legacySamplers[i]));
                        }

                        samplers = combined;
                    }
	            }

            samplers = EnsureSamplersForTextures(textures, samplers);

	            static TRSampler NormalizeLegacySampler(TRSampler srcSampler)
	            {
	                if (srcSampler == null)
	                {
	                    return new TRSampler();
	                }

	                return new TRSampler
	                {
	                    State0 = srcSampler.State0,
	                    State1 = srcSampler.State1,
	                    State2 = srcSampler.State2,
	                    State3 = srcSampler.State3,
	                    State4 = srcSampler.State4,
	                    State5 = srcSampler.State5,
	                    State6 = srcSampler.State6,
	                    State7 = srcSampler.State7,
	                    State8 = srcSampler.State8,
	                    RepeatU = srcSampler.RepeatU,
	                    RepeatV = srcSampler.RepeatV,
	                    RepeatW = srcSampler.RepeatW,
	                    BorderColor = srcSampler.BorderColor ?? new Trinity.Core.Flatbuffers.Utils.RGBA(),
	                };
	            }

	            static TRSampler NormalizeSamplerWrapModes(TRSampler sampler)
	            {
	                if (sampler == null)
	                {
	                    return new TRSampler();
	                }

	                var repeatU = sampler.RepeatU;
	                var repeatV = sampler.RepeatV;
	                var repeatW = sampler.RepeatW;

	                // Heuristic: some TRMTR variants appear to deserialize sampler wrap modes with RepeatV/RepeatW swapped.
	                // Symptom: V is always CLAMP because W is commonly CLAMP, while W contains the real V mode (WRAP/MIRROR).
	                if (repeatV == UVWrapMode.CLAMP &&
	                    repeatW != UVWrapMode.CLAMP &&
	                    (repeatW == UVWrapMode.WRAP || repeatW == UVWrapMode.MIRROR || repeatW == UVWrapMode.MIRROR_ONCE))
	                {
	                    (repeatV, repeatW) = (repeatW, repeatV);
	                }

	                // Heuristic: some assets appear to clamp V unexpectedly while still mirroring/repeating U.
	                // This produces the "UV collapsed to an edge" look in previews and breaks layered masks.
	                // If U is explicitly mirrored and both V/W are clamp, assume V should also wrap.
	                if ((repeatU == UVWrapMode.MIRROR || repeatU == UVWrapMode.MIRROR_ONCE) &&
	                    repeatV == UVWrapMode.CLAMP &&
	                    repeatW == UVWrapMode.CLAMP)
	                {
	                    repeatV = UVWrapMode.WRAP;
	                }

	                sampler.RepeatU = repeatU;
	                sampler.RepeatV = repeatV;
	                sampler.RepeatW = repeatW;
	                return sampler;
	            }

		            var floatParams = src?.FloatParameters?.Select(p => new TRFloatParameter
		            {
		                Name = p?.Name ?? string.Empty,
		                Value = p?.Value ?? 0.0f
		            }).ToArray() ?? Array.Empty<TRFloatParameter>();

		            static IEnumerable<TRVec4fParameter> ConvertFloat4Params(TrmtrFileFloat4Parameter[]? srcParams)
	            {
	                if (srcParams == null) yield break;
	                foreach (var p in srcParams)
	                {
	                    if (p == null || string.IsNullOrWhiteSpace(p.Name) || p.Value == null) continue;
	                    yield return new TRVec4fParameter
	                    {
	                        Name = p.Name,
	                        Value = new Trinity.Core.Flatbuffers.Utils.Vector4f
	                        {
	                            W = p.Value.R,
	                            X = p.Value.G,
	                            Y = p.Value.B,
	                            Z = p.Value.A
	                        }
	                    };
	                }
		            }

		            var vec4Params = ConvertFloat4Params(src?.Float4Parameters)
		                .GroupBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
		                .Select(g => g.First())
		                .OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
		                .ToArray();

		            var vec2Params = src?.Vector2fParameters?.Select(p => new TRVec2fParameter
		            {
		                Name = p?.Name ?? string.Empty,
		                Value = p?.Value ?? new Trinity.Core.Flatbuffers.Utils.Vector2f()
		            }).ToArray() ?? Array.Empty<TRVec2fParameter>();

		            var vec3Params = src?.Vector3fParameters?.Select(p => new TRVec3fParameter
		            {
		                Name = p?.Name ?? string.Empty,
		                Value = p?.Value ?? new Trinity.Core.Flatbuffers.Utils.Vector3f()
		            }).ToArray() ?? Array.Empty<TRVec3fParameter>();

		            return new TRMaterial
		            {
		                Name = src?.Name ?? "Material",
		                Shader = new[] { new TRMaterialShader { Name = shaderName, Values = shaderParams.ToArray() } },
		                Textures = textures,
		                Samplers = samplers,
		                FloatParams = floatParams,
		                Vec2fParams = vec2Params,
		                Vec3fParams = vec3Params,
		                Vec4fParams = vec4Params,
		            };
		        }

        public IReadOnlyList<Material> GetMaterials()
        {
            return materials ?? Array.Empty<Material>();
        }

        public void ReplaceMaterials(IReadOnlyList<Material> newMaterials)
        {
            if (newMaterials == null)
            {
                return;
            }

            materials = newMaterials.Where(m => m != null).ToArray();
            BuildMaterialMap();
        }

        public Armature? GetArmature()
        {
            return armature;
        }

        public IReadOnlyList<string> GetSubmeshNames()
        {
            return SubmeshNames;
        }

        public IReadOnlyList<string> GetSubmeshMaterials()
        {
            return MaterialNames;
        }

        public void SetSubmeshMaterialName(int submeshIndex, string materialName)
        {
            if (submeshIndex < 0)
            {
                return;
            }

            if (submeshIndex >= MaterialNames.Count)
            {
                return;
            }

            MaterialNames[submeshIndex] = materialName ?? string.Empty;
        }

        public IReadOnlyList<UvSet> GetUvSetsForMaterial(string materialName)
        {
            return GetUvSetsForMaterial(materialName, 0);
        }

        public IReadOnlyList<UvSet> GetUvSetsForMaterial(string materialName, int uvIndex)
        {
            var result = new List<UvSet>();
            if (string.IsNullOrWhiteSpace(materialName))
            {
                return result;
            }

            IReadOnlyList<Vector2[]> uvsSource = uvIndex == 1 ? UVs2 : UVs;

            var count = Math.Min(MaterialNames.Count, Math.Min(uvsSource.Count, Indices.Count));
            for (int i = 0; i < count; i++)
            {
                if (MatchesMaterial(MaterialNames[i], materialName))
                {
                    var submeshName = i < SubmeshNames.Count ? SubmeshNames[i] : $"Submesh {i}";
                    result.Add(new UvSet(uvsSource[i], Indices[i], submeshName));
                }
            }

            return result;
        }

        private static TRSampler[] EnsureSamplersForTextures(TRTexture[] textures, TRSampler[] existingSamplers)
        {
            if (textures == null || textures.Length == 0)
            {
                return existingSamplers ?? Array.Empty<TRSampler>();
            }

            existingSamplers ??= Array.Empty<TRSampler>();

            uint maxSlot = 0;
            bool hasTextureSlots = false;
            for (int i = 0; i < textures.Length; i++)
            {
                var tex = textures[i];
                if (tex == null)
                {
                    continue;
                }

                hasTextureSlots = true;
                if (tex.Slot > maxSlot)
                {
                    maxSlot = tex.Slot;
                }
            }

            if (!hasTextureSlots)
            {
                return existingSamplers;
            }

            int requiredLength = checked((int)maxSlot + 1);
            if (existingSamplers.Length >= requiredLength)
            {
                return existingSamplers;
            }

            var expanded = new TRSampler[requiredLength];
            Array.Copy(existingSamplers, expanded, existingSamplers.Length);
            for (int i = 0; i < expanded.Length; i++)
            {
                expanded[i] ??= new TRSampler
                {
                    RepeatU = UVWrapMode.WRAP,
                    RepeatV = UVWrapMode.WRAP,
                    RepeatW = UVWrapMode.WRAP,
                    BorderColor = new Trinity.Core.Flatbuffers.Utils.RGBA(),
                };
            }

            return expanded;
        }

        private static bool MatchesMaterial(string name, string target)
        {
            if (string.Equals(name, target, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(target))
            {
                return false;
            }

            if (name.Contains(':'))
            {
                name = name.Split(':')[0];
            }

            if (target.Contains(':'))
            {
                target = target.Split(':')[0];
            }

            return name.StartsWith(target, StringComparison.OrdinalIgnoreCase) ||
                   target.StartsWith(name, StringComparison.OrdinalIgnoreCase);
        }

        private void BuildMaterialMap()
        {
            materialMap.Clear();
            if (materials == null) return;
            foreach (var mat in materials)
            {
                if (mat == null || string.IsNullOrEmpty(mat.Name)) continue;
                if (!materialMap.ContainsKey(mat.Name))
                {
                    materialMap.Add(mat.Name, mat);
                }

                // Some DCC tools append numeric suffixes like ".001" when duplicating materials.
                // Add an alias without the suffix so imported meshes can still resolve materials
                // without requiring the user to manually rename them back.
                var baseName = StripDccNumericSuffix(mat.Name);
                if (!string.IsNullOrWhiteSpace(baseName) && !materialMap.ContainsKey(baseName))
                {
                    materialMap.Add(baseName, mat);
                }
            }
        }

        private static string StripDccNumericSuffix(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length < 5)
            {
                return name;
            }

            // Matches ".000" .. ".999" suffixes.
            int dot = name.LastIndexOf('.');
            if (dot < 0 || dot >= name.Length - 4)
            {
                return name;
            }

            if (name.Length - dot != 4)
            {
                return name;
            }

            char c1 = name[dot + 1];
            char c2 = name[dot + 2];
            char c3 = name[dot + 3];
            if (!char.IsDigit(c1) || !char.IsDigit(c2) || !char.IsDigit(c3))
            {
                return name;
            }

            return name.Substring(0, dot);
        }
	    }
}
