using GFTool.Core.Utils;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.Linq;
using Trinity.Core.Assets;
using Trinity.Core.Flatbuffers.TR.Model;

namespace GFTool.Renderer.Scene.GraphicsObjects
{
    public partial class Model
    {
        private Model(IAssetProvider assetProvider, bool enableCpuMorphRegistration)
        {
            this.assetProvider = assetProvider ?? throw new ArgumentNullException(nameof(assetProvider));
            this.enableCpuMorphRegistration = enableCpuMorphRegistration;
            modelMat = Matrix4.Identity;
            modelPath = new PathString(string.Empty);
            trmdlSourcePath = string.Empty;
            gpuSetupIndex = -1;
            gpuSetupComplete = false;
            localBoundsDirty = true;
        }

        public Model CreateSharedSceneInstance()
        {
            return new Model(this, CreateCloneAssetProvider(assetProvider));
        }

        public Model CreateMergedStaticSceneInstance(IReadOnlyList<Matrix4> instanceTransforms, string? mergedName = null)
        {
            return CreateMergedStaticSceneInstance(
                instanceTransforms,
                mergedName,
                persistentCachePath: null,
                out _);
        }

        public Model CreateMergedStaticSceneInstance(
            IReadOnlyList<Matrix4> instanceTransforms,
            string? mergedName,
            string? persistentCachePath,
            out bool cacheHit)
        {
            if (instanceTransforms == null) throw new ArgumentNullException(nameof(instanceTransforms));
            if (instanceTransforms.Count == 0) throw new ArgumentException("At least one instance transform is required.", nameof(instanceTransforms));

            var rejectReason = GetMergedStaticBatchRejectReason();
            if (rejectReason != null)
            {
                throw new InvalidOperationException($"Merged static scene instances do not support '{rejectReason}'.");
            }

            if (!string.IsNullOrWhiteSpace(persistentCachePath) &&
                PersistentMeshCache.TryLoadFile(persistentCachePath, out var cachedSnapshot))
            {
                var cached = CreateMergedStaticShell(mergedName);
                cached.ApplyPersistentMeshCache(cachedSnapshot);
                cached.BuildMaterialMap();
                cached.localBoundsDirty = true;
                cacheHit = true;
                return cached;
            }

            cacheHit = false;
            var export = CreateExportData();
            var merged = CreateMergedStaticShell(mergedName);

            foreach (var src in export.Submeshes)
            {
                int instanceCount = instanceTransforms.Count;
                int sourceVertexCount = src.Positions.Length;
                int sourceIndexCount = src.Indices.Length;
                int totalVertexCount = sourceVertexCount * instanceCount;
                int totalIndexCount = sourceIndexCount * instanceCount;

                var positions = new Vector3[totalVertexCount];
                var normals = new Vector3[totalVertexCount];
                var uvs = new Vector2[totalVertexCount];
                var colors = new Vector4[totalVertexCount];
                var tangents = new Vector4[totalVertexCount];
                var binormals = new Vector3[totalVertexCount];
                var blendIndices = new Vector4[totalVertexCount];
                var blendWeights = new Vector4[totalVertexCount];
                var indices = new uint[totalIndexCount];

                for (int instanceIndex = 0; instanceIndex < instanceCount; instanceIndex++)
                {
                    var transform = instanceTransforms[instanceIndex];
                    int vertexBase = instanceIndex * sourceVertexCount;
                    int indexBase = instanceIndex * sourceIndexCount;

                    for (int v = 0; v < sourceVertexCount; v++)
                    {
                        positions[vertexBase + v] = TransformPoint(src.Positions[v], transform);
                        normals[vertexBase + v] = TransformDirection(src.Normals, v, transform, Vector3.UnitY);
                        uvs[vertexBase + v] = v < src.UVs.Length ? src.UVs[v] : Vector2.Zero;
                        colors[vertexBase + v] = v < src.Colors.Length ? src.Colors[v] : Vector4.One;
                        tangents[vertexBase + v] = TransformTangent(src.Tangents, v, transform);
                        binormals[vertexBase + v] = TransformDirection(src.Binormals, v, transform, Vector3.UnitY);
                        blendIndices[vertexBase + v] = Vector4.Zero;
                        blendWeights[vertexBase + v] = Vector4.Zero;
                    }

                    for (int idx = 0; idx < sourceIndexCount; idx++)
                    {
                        indices[indexBase + idx] = (uint)vertexBase + src.Indices[idx];
                    }
                }

                merged.Positions.Add(positions);
                merged.Normals.Add(normals);
                merged.UVs.Add(uvs);
                merged.UVs2.Add(new Vector2[totalVertexCount]);
                merged.HasUv1.Add(false);
                merged.Colors.Add(colors);
                merged.HasVertexColors.Add(src.HasVertexColors);
                merged.Tangents.Add(tangents);
                merged.HasTangents.Add(src.HasTangents);
                merged.Binormals.Add(binormals);
                merged.HasBinormals.Add(src.HasBinormals);
                merged.BlendIndicies.Add(blendIndices);
                merged.BlendIndiciesOriginal.Add(blendIndices.ToArray());
                merged.BlendWeights.Add(blendWeights);
                merged.BlendBoneWeights.Add(null);
                merged.BlendMeshNames.Add(src.Name);
                merged.HasSkinning.Add(false);
                merged.Indices.Add(indices);
                merged.MaterialNames.Add(src.MaterialName);
                merged.SubmeshNames.Add(src.Name);
                merged.SubmeshParentNodeNames.Add(null);
            }

            merged.BuildMaterialMap();
            merged.localBoundsDirty = true;

            if (!string.IsNullOrWhiteSpace(persistentCachePath))
            {
                PersistentMeshCache.QueueStoreAtPath(merged, persistentCachePath);
            }

            return merged;
        }

        private Model CreateMergedStaticShell(string? mergedName)
        {
            var merged = new Model(CreateCloneAssetProvider(assetProvider), enableCpuMorphRegistration: false)
            {
                Name = string.IsNullOrWhiteSpace(mergedName) ? $"{Name}_merged" : mergedName!,
                modelPath = modelPath,
                trmdlSourcePath = trmdlSourcePath,
                loadedAllLods = loadedAllLods,
                baseSkeletonCategoryHint = baseSkeletonCategoryHint,
                preferredMaterialMetadataPath = preferredMaterialMetadataPath,
                currentMaterialFilePath = currentMaterialFilePath,
                defaultMaterialFilePath = defaultMaterialFilePath,
                currentMaterialSetName = currentMaterialSetName,
                currentSkeletonPath = null,
                armature = null,
                armatureOverride = null,
                rigidParentBoneIndexBySubmesh = null,
                blendIndexRemapModes = null,
                blendIndexOffsets = Array.Empty<int>(),
                blendIndexByteSizes = Array.Empty<int>(),
                blendIndexStats = new BlendIndexStats()
            };

            merged.loadedMeshFiles.AddRange(loadedMeshFiles);

            merged.CloneRuntimeMaterialsFrom(this);
            return merged;
        }

        private Model(Model source, IAssetProvider assetProvider)
        {
            this.assetProvider = assetProvider ?? throw new ArgumentNullException(nameof(assetProvider));
            enableCpuMorphRegistration = source.enableCpuMorphRegistration;

            Name = source.Name;
            modelMat = Matrix4.Identity;
            modelPath = source.modelPath;
            trmdlSourcePath = source.trmdlSourcePath;
            loadedAllLods = source.loadedAllLods;
            baseSkeletonCategoryHint = source.baseSkeletonCategoryHint;
            preferredMaterialMetadataPath = source.preferredMaterialMetadataPath;
            blendIndexStats = source.blendIndexStats;
            gpuSetupIndex = source.gpuSetupIndex;
            gpuSetupComplete = source.gpuSetupComplete;
            VAOs = source.VAOs;
            VBOs = source.VBOs;
            EBOs = source.EBOs;
            blendIndexOffsets = source.blendIndexOffsets;
            blendIndexByteSizes = source.blendIndexByteSizes;
            blendIndexRemapModes = source.blendIndexRemapModes;

            Positions = source.Positions;
            Normals = source.Normals;
            UVs = source.UVs;
            UVs2 = source.UVs2;
            HasUv1 = source.HasUv1;
            Colors = source.Colors;
            Tangents = source.Tangents;
            Binormals = source.Binormals;
            BlendIndicies = source.BlendIndicies;
            BlendWeights = source.BlendWeights;
            BlendBoneWeights = source.BlendBoneWeights;
            BlendIndiciesOriginal = source.BlendIndiciesOriginal;
            BlendMeshNames = source.BlendMeshNames;
            Indices = source.Indices;
            HasVertexColors = source.HasVertexColors;
            HasTangents = source.HasTangents;
            HasBinormals = source.HasBinormals;
            HasSkinning = source.HasSkinning;
            MaterialNames = new List<string>(source.MaterialNames);
            SubmeshNames = source.SubmeshNames;
            rigidParentBoneIndexBySubmesh = source.rigidParentBoneIndexBySubmesh != null
                ? (int[])source.rigidParentBoneIndexBySubmesh.Clone()
                : null;
            submeshVisible = null;
            EnsureSubmeshVisibilitySize(Positions.Count);

            loadedMeshFiles.AddRange(source.loadedMeshFiles);
            SubmeshParentNodeNames.AddRange(source.SubmeshParentNodeNames);

            CopyMorphMetadataFrom(source);
            CloneRuntimeMaterialsFrom(source);
            CloneArmatureFrom(source);
            ResolveRigidParentAttachments();
        }

        private static IAssetProvider CreateCloneAssetProvider(IAssetProvider sourceAssetProvider)
        {
            if (sourceAssetProvider is InMemoryOverrideAssetProvider overlay)
            {
                return new InMemoryOverrideAssetProvider(overlay.Inner);
            }

            if (sourceAssetProvider is DiskAssetProvider disk)
            {
                return new InMemoryOverrideAssetProvider(disk);
            }

            return sourceAssetProvider;
        }

        private static Vector3 TransformPoint(Vector3 point, Matrix4 transform)
        {
            var v = new Vector4(point.X, point.Y, point.Z, 1f) * transform;
            return v.Xyz;
        }

        private static Vector3 TransformDirection(Vector3[] source, int index, Matrix4 transform, Vector3 fallback)
        {
            if (source == null || index >= source.Length)
            {
                return fallback;
            }

            var v = new Vector4(source[index].X, source[index].Y, source[index].Z, 0f) * transform;
            var xyz = v.Xyz;
            if (xyz.LengthSquared <= 0.000001f)
            {
                return fallback;
            }

            xyz.Normalize();
            return xyz;
        }

        private static Vector4 TransformTangent(Vector4[] source, int index, Matrix4 transform)
        {
            if (source == null || index >= source.Length)
            {
                return new Vector4(1f, 0f, 0f, 1f);
            }

            var src = source[index];
            var v = new Vector4(src.X, src.Y, src.Z, 0f) * transform;
            var xyz = v.Xyz;
            if (xyz.LengthSquared <= 0.000001f)
            {
                xyz = Vector3.UnitX;
            }
            else
            {
                xyz.Normalize();
            }

            return new Vector4(xyz, src.W);
        }

        private void CopyMorphMetadataFrom(Model source)
        {
            lock (source.morphGate)
            {
                foreach (var kv in source.fullMorphTargetsByMeshShapeName)
                {
                    fullMorphTargetsByMeshShapeName[kv.Key] = kv.Value.ToList();
                }

                foreach (var kv in source.partialMorphTargetsByMeshShapeName)
                {
                    partialMorphTargetsByMeshShapeName[kv.Key] = kv.Value.ToList();
                }

                foreach (var kv in source.vertexBuffersByMeshShapeName)
                {
                    vertexBuffersByMeshShapeName[kv.Key] = kv.Value;
                }

                unsupportedFullMorphTargets.AddRange(source.unsupportedFullMorphTargets);
                cpuFullMorphRegistrationStatus = source.cpuFullMorphRegistrationStatus;
            }
        }

        private void CloneRuntimeMaterialsFrom(Model source)
        {
            if (!string.IsNullOrWhiteSpace(source.currentMaterialFilePath) && assetProvider.Exists(source.currentMaterialFilePath))
            {
                ParseMaterial(source.currentMaterialFilePath);
                defaultMaterialFilePath = source.defaultMaterialFilePath;
                preferredMaterialMetadataPath = source.preferredMaterialMetadataPath;
                return;
            }

            if (source.materials == null || source.materials.Length == 0)
            {
                materials = Array.Empty<Material>();
                materialMap.Clear();
                return;
            }

            var cloned = new List<Material>(source.materials.Length);
            foreach (var src in source.materials)
            {
                if (src == null)
                {
                    continue;
                }

                cloned.Add(CloneRuntimeMaterial(src));
            }

            materials = cloned.ToArray();
            BuildMaterialMap();
            currentMaterialFilePath = source.currentMaterialFilePath;
            defaultMaterialFilePath = source.defaultMaterialFilePath;
            preferredMaterialMetadataPath = source.preferredMaterialMetadataPath;
        }

        private Material CloneRuntimeMaterial(Material source)
        {
            var shaderParams = source.ShaderParameters
                .Select(p => new TRStringParameter { Name = p.Name ?? string.Empty, Value = p.Value ?? string.Empty })
                .ToArray();

            var trmat = new TRMaterial
            {
                Name = source.Name,
                Shader = new[]
                {
                    new TRMaterialShader
                    {
                        Name = source.ShaderName,
                        Values = shaderParams
                    }
                },
                Textures = source.Textures
                    .Select(t => new TRTexture
                    {
                        Name = t.Name ?? string.Empty,
                        File = t.SourceFile ?? string.Empty,
                        Slot = t.Slot
                    })
                    .ToArray(),
                Samplers = source.Samplers
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
                FloatParams = source.FloatParameters
                    .Select(p => new TRFloatParameter { Name = p.Name ?? string.Empty, Value = p.Value })
                    .ToArray(),
                Vec2fParams = source.Vec2Parameters
                    .Select(p => new TRVec2fParameter { Name = p.Name ?? string.Empty, Value = p.Value })
                    .ToArray(),
                Vec3fParams = source.Vec3Parameters
                    .Select(p => new TRVec3fParameter { Name = p.Name ?? string.Empty, Value = p.Value })
                    .ToArray(),
                Vec4fParams = source.Vec4Parameters
                    .Select(p => new TRVec4fParameter { Name = p.Name ?? string.Empty, Value = p.Value })
                    .ToArray()
            };

            TrmtrFileRasterizationState? rast = null;
            if (source.TrmtrCullMode.HasValue || source.TrmtrFrontFace.HasValue)
            {
                rast = new TrmtrFileRasterizationState
                {
                    CullMode = source.TrmtrCullMode ?? TrmtrCullMode.Back,
                    FrontFace = source.TrmtrFrontFace ?? TrmtrFrontFace.CCW,
                    DepthBias = 0,
                    SlopeScaledDepthBias = 0.0f,
                    DepthBiasClamp = 0.0f
                };
            }

            return new Material(new PathString(currentMaterialFilePath ?? string.Empty), trmat, assetProvider, rast);
        }

        private void CloneArmatureFrom(Model source)
        {
            if (!string.IsNullOrWhiteSpace(source.currentSkeletonPath) && assetProvider.Exists(source.currentSkeletonPath))
            {
                currentSkeletonPath = source.currentSkeletonPath;
                ParseArmature(source.currentSkeletonPath);
                return;
            }

            armature = source.armature;
        }
    }
}
