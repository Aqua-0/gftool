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
using Trinity.Core.Flatbuffers.Gfx2;


namespace GFTool.Renderer.Scene.GraphicsObjects
{
	    public partial class Model : RefObject
	    {
        private readonly IAssetProvider assetProvider;
        private PathString modelPath;
        private string trmdlSourcePath;
        private bool loadedAllLods;
        private string? baseSkeletonCategoryHint;
        private readonly List<string> loadedMeshFiles = new List<string>();

        public string Name { get; private set; } = string.Empty;
        private long lastAnimAllocPoseComputeBytes;
        private long lastAnimAllocWriteBackBytes;

        private int[] VAOs = Array.Empty<int>();
        private int[] VBOs = Array.Empty<int>();
        private int[] EBOs = Array.Empty<int>();

        private List<Vector3[]> Positions = new List<Vector3[]>();
        private List<Vector3[]> Normals = new List<Vector3[]>();
        private List<Vector2[]> UVs = new List<Vector2[]>();
        private List<Vector2[]> UVs2 = new List<Vector2[]>();
        private List<bool> HasUv1 = new List<bool>();
        private List<Vector4[]> Colors = new List<Vector4[]>();
        private List<Vector4[]> Tangents = new List<Vector4[]>();
        private List<Vector3[]> Binormals = new List<Vector3[]>();
        private List<Vector4[]> BlendIndicies = new List<Vector4[]>();
        private List<Vector4[]> BlendWeights = new List<Vector4[]>();
        private List<TRBoneWeight[]?> BlendBoneWeights = new List<TRBoneWeight[]?>();
        private List<Vector4[]> BlendIndiciesOriginal = new List<Vector4[]>();
        private List<string> BlendMeshNames = new List<string>();

        private List<uint[]> Indices = new List<uint[]>();
        private List<bool> HasVertexColors = new List<bool>();
        private List<bool> HasTangents = new List<bool>();
        private List<bool> HasBinormals = new List<bool>();
        private List<bool> HasSkinning = new List<bool>();

        private Material[] materials = Array.Empty<Material>();
        private List<string> MaterialNames = new List<string>();
        private Dictionary<string, Material> materialMap = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
        private List<string> SubmeshNames = new List<string>();
        private readonly List<string?> SubmeshParentNodeNames = new List<string?>();
        private int[]? rigidParentBoneIndexBySubmesh;

        private Armature? armature;
        public Armature? Armature => armature;
        private Armature? armatureOverride;
        private static int skeletonVao;
        private static int skeletonVbo;
        private static readonly float[] unitBoneVerts = BuildUnitBoneVerts();

        private Matrix4 modelMat;
        private int selectedSubmeshIndex = -1;
        private BlendIndexStats blendIndexStats = new BlendIndexStats();
        private int[] blendIndexOffsets = Array.Empty<int>();
        private int[] blendIndexByteSizes = Array.Empty<int>();
        private BlendIndexRemapMode[]? blendIndexRemapModes;
        private string? lastAnimSkinDebugName;
        public bool IsVisible { get; private set; } = true;
        private bool[]? submeshVisible;
        private readonly Dictionary<int, (UvSetOverride LayerMask, UvSetOverride AO)> uvOverridesBySubmesh =
            new Dictionary<int, (UvSetOverride LayerMask, UvSetOverride AO)>();

        private T LoadFlat<T>(string path) where T : class
        {
            return FlatBufferConverter.DeserializeFrom<T>(assetProvider.ReadAllBytes(path));
        }

        public Model(string model, bool loadAllLods)
            : this(new DiskAssetProvider(), model, loadAllLods)
        {
        }

	        public Model(IAssetProvider assetProvider, string model, bool loadAllLods)
	        {
	            this.assetProvider = assetProvider ?? throw new ArgumentNullException(nameof(assetProvider));
	            ClearDirtyFlags();
	            Name = Path.GetFileNameWithoutExtension(model);
	            modelMat = Matrix4.Identity;
	            modelPath = new PathString(model);
	            trmdlSourcePath = model;
	            loadedAllLods = loadAllLods;
	            preferredMaterialMetadataPath = Path.ChangeExtension(model, ".trmmt");

	            var mdl = FlatBufferConverter.DeserializeFrom<TRMDL>(this.assetProvider.ReadAllBytes(model));

            //Meshes
            if (mdl.Meshes == null || mdl.Meshes.Length == 0)
            {
                MessageHandler.Instance.AddMessage(
                    MessageType.WARNING,
                    $"[TRMDL] '{model}': no meshes (Meshes.Length=0).");
            }
            else if (loadAllLods)
            {
                foreach (var mesh in mdl.Meshes)
                {
                    var meshPath = modelPath.Combine(mesh.PathName);
                    loadedMeshFiles.Add(meshPath);
                    ParseMesh(meshPath);
                }
            }
            else
            {
                var mesh = mdl.Meshes[0]; //LOD0
                var meshPath = modelPath.Combine(mesh.PathName);
                loadedMeshFiles.Add(meshPath);
                ParseMesh(meshPath);
            }

            baseSkeletonCategoryHint = GuessBaseSkeletonCategory(
                model,
                mdl.Meshes != null && mdl.Meshes.Length > 0 ? mdl.Meshes[0].PathName : null,
                mdl.Skeleton?.PathName);
            if (MessageHandler.Instance.DebugLogsEnabled)
            {
                MessageHandler.Instance.AddMessage(
                    MessageType.LOG,
                    $"[TRSKL] baseHint='{baseSkeletonCategoryHint ?? "<none>"}' trmdl='{model}' skel='{mdl.Skeleton?.PathName ?? "<none>"}' mesh='{(mdl.Meshes != null && mdl.Meshes.Length > 0 ? mdl.Meshes[0].PathName : "<none>")}'");
            }

            //Materials
            if (mdl.Materials != null && mdl.Materials.Length > 0)
            {
                // because ParseMaterial() replaces the whole runtime material set. Avoid repeated reparses.
                var resolvedMaterials = new List<string>(mdl.Materials.Length);
                foreach (var mat in mdl.Materials)
                {
                    if (string.IsNullOrWhiteSpace(mat))
                    {
                        continue;
                    }
                    resolvedMaterials.Add(ResolveTrmtrPath(modelPath.Combine(mat), this.assetProvider));
                }

                if (resolvedMaterials.Count > 0)
                {
                    var chosen = resolvedMaterials[resolvedMaterials.Count - 1];
                    if (MessageHandler.Instance.DebugLogsEnabled && resolvedMaterials.Count > 1)
                    {
                        MessageHandler.Instance.AddMessage(
                            MessageType.LOG,
                            $"[TRMDL] Multiple material paths ({resolvedMaterials.Count}); using '{chosen}'");
                    }
                    ParseMaterial(chosen);
                }
            }

            defaultMaterialFilePath = currentMaterialFilePath;

            //Skeleton
            if (mdl.Skeleton != null)
            {
                if (!string.IsNullOrWhiteSpace(mdl.Skeleton.PathName))
                {
                    ParseArmature(modelPath.Combine(mdl.Skeleton.PathName));
                }
                else
                {
                    TryParseBaseArmature(model, baseSkeletonCategoryHint);
                }
            }
            else
            {
                TryParseBaseArmature(model, baseSkeletonCategoryHint);
            }

            ResolveRigidParentAttachments();
        }

        public void ReloadFromTrmdlSource(string trmdlPath, bool loadAllLods)
        {
            if (string.IsNullOrWhiteSpace(trmdlPath))
            {
                throw new ArgumentException("Missing TRMDL path.", nameof(trmdlPath));
            }

            ClearDirtyFlags();
            trmdlSourcePath = trmdlPath;
            loadedAllLods = loadAllLods;
            modelPath = new PathString(trmdlPath);
            preferredMaterialMetadataPath = Path.ChangeExtension(trmdlPath, ".trmmt");

            // Clear mesh-derived data and runtime state.
            Positions.Clear();
            Normals.Clear();
            UVs.Clear();
            UVs2.Clear();
            HasUv1.Clear();
            Colors.Clear();
            Tangents.Clear();
            Binormals.Clear();
            BlendIndicies.Clear();
            BlendIndiciesOriginal.Clear();
            BlendWeights.Clear();
            BlendBoneWeights.Clear();
            BlendMeshNames.Clear();

            Indices.Clear();
            HasVertexColors.Clear();
            HasTangents.Clear();
            HasBinormals.Clear();
            HasSkinning.Clear();

            MaterialNames.Clear();
            SubmeshNames.Clear();
            SubmeshParentNodeNames.Clear();
            rigidParentBoneIndexBySubmesh = null;
            uvOverridesBySubmesh.Clear();
            selectedSubmeshIndex = -1;
            loadedMeshFiles.Clear();

            lock (morphGate)
            {
                fullMorphTargetsByMeshShapeName.Clear();
                partialMorphTargetsByMeshShapeName.Clear();
                fullMorphWeights.Clear();
                vertexBuffersByMeshShapeName.Clear();
                pendingMorphVboUploads.Clear();
                unsupportedFullMorphTargets.Clear();
                cpuFullMorphRegistrationStatus = null;
            }
            basePositionsBySubmeshIndex.Clear();
            baseNormalsBySubmeshIndex.Clear();
            baseTangentsBySubmeshIndex.Clear();
            baseBinormalsBySubmeshIndex.Clear();

            baseSkeletonCategoryHint = null;
            armature = null;
            armatureOverride = null;

            hasMaterialSourceEdits = false;
            newMaterialCloneRequests.Clear();
            currentMaterialFilePath = null;
            currentMaterialSetName = null;
            defaultMaterialFilePath = null;
            materialMetadata = null;
            materialMetadataPath = null;
            materialMetadataSelections.Clear();
            materialMetadataValueOverrides.Clear();
            materialMetadataLastAppliedUniformNames.Clear();

            if (materials != null)
            {
                foreach (var existing in materials)
                {
                    existing?.Dispose();
                }
            }
            materials = Array.Empty<Material>();
            materialMap.Clear();

            // Reset GPU setup so the renderer can re-upload buffers.
            gpuSetupComplete = false;
            gpuSetupIndex = -1;
            VAOs = Array.Empty<int>();
            VBOs = Array.Empty<int>();
            EBOs = Array.Empty<int>();

            var mdl = FlatBufferConverter.DeserializeFrom<TRMDL>(assetProvider.ReadAllBytes(trmdlPath));

            // Meshes
            if (loadAllLods)
            {
                foreach (var mesh in mdl.Meshes)
                {
                    var meshPath = modelPath.Combine(mesh.PathName);
                    loadedMeshFiles.Add(meshPath);
                    ParseMesh(meshPath);
                }
            }
            else
            {
                var mesh = mdl.Meshes[0]; // LOD0
                var meshPath = modelPath.Combine(mesh.PathName);
                loadedMeshFiles.Add(meshPath);
                ParseMesh(meshPath);
            }

            baseSkeletonCategoryHint = GuessBaseSkeletonCategory(
                trmdlPath,
                mdl.Meshes != null && mdl.Meshes.Length > 0 ? mdl.Meshes[0].PathName : null,
                mdl.Skeleton?.PathName);

            // Materials
            if (mdl.Materials != null && mdl.Materials.Length > 0)
            {
                var resolvedMaterials = new List<string>(mdl.Materials.Length);
                foreach (var mat in mdl.Materials)
                {
                    if (string.IsNullOrWhiteSpace(mat))
                    {
                        continue;
                    }
                    resolvedMaterials.Add(ResolveTrmtrPath(modelPath.Combine(mat), this.assetProvider));
                }

                if (resolvedMaterials.Count > 0)
                {
                    ParseMaterial(resolvedMaterials[resolvedMaterials.Count - 1]);
                }
            }

            defaultMaterialFilePath = currentMaterialFilePath;

            // Skeleton
            if (mdl.Skeleton != null)
            {
                if (!string.IsNullOrWhiteSpace(mdl.Skeleton.PathName))
                {
                    ParseArmature(modelPath.Combine(mdl.Skeleton.PathName));
                }
                else
                {
                    TryParseBaseArmature(trmdlPath, baseSkeletonCategoryHint);
                }
            }
            else
            {
                TryParseBaseArmature(trmdlPath, baseSkeletonCategoryHint);
            }

            ResolveRigidParentAttachments();
        }

	        private bool TryParseBaseArmature(string trmdlPath, string? category)
	        {
	            var provider = assetProvider is InMemoryOverrideAssetProvider mem ? mem.Inner : assetProvider;
	            if (provider is not (DiskAssetProvider or Trinity.Core.Assets.OverlayDiskAssetProvider) || string.IsNullOrWhiteSpace(category))
	            {
	                return false;
	            }

	            var modelDir = Path.GetDirectoryName(trmdlPath);
	            if (string.IsNullOrWhiteSpace(modelDir))
	            {
	                return false;
	            }

	            var basePath = ResolveBaseTrsklPath(modelDir, category!, localSkel: null);
	            if (string.IsNullOrWhiteSpace(basePath) || !provider.Exists(basePath))
	            {
	                if (MessageHandler.Instance.DebugLogsEnabled)
	                {
	                    MessageHandler.Instance.AddMessage(
	                        MessageType.LOG,
                        $"[TRSKL] no local skeleton; base-only not found category={category} trmdl='{trmdlPath}'");
                }
                return false;
            }

            try
            {
                var baseSkel = LoadFlat<TRSKL>(basePath);
                armature = new Armature(baseSkel, basePath);
                ApplyBlendIndexMapping(
                    RenderOptions.MapBlendIndicesViaJointInfo,
                    RenderOptions.MapBlendIndicesViaSkinningPalette,
                    RenderOptions.MapBlendIndicesViaBoneMeta,
                    RenderOptions.AutoMapBlendIndices);

                if (MessageHandler.Instance.DebugLogsEnabled)
                {
                    MessageHandler.Instance.AddMessage(
                        MessageType.LOG,
                        $"[TRSKL] using base-only skeleton category={category} base='{basePath}'");
                }

                return true;
            }
            catch (Exception ex)
            {
                if (MessageHandler.Instance.DebugLogsEnabled)
                {
                    MessageHandler.Instance.AddMessage(
                        MessageType.WARNING,
                        $"[TRSKL] base-only load failed category={category} base='{basePath}': {ex.Message}");
                }
                return false;
            }
        }

        private static string? GuessBaseSkeletonCategory(string trmdlPath, string? meshPathName, string? skeletonPathName)
        {
            static string FileNameOrEmpty(string? path) => string.IsNullOrWhiteSpace(path) ? string.Empty : Path.GetFileName(path);
            static bool StartsWithAny(string file, params string[] prefixes) =>
                prefixes.Any(p => file.StartsWith(p, StringComparison.OrdinalIgnoreCase));

            string meshFile = FileNameOrEmpty(meshPathName);
            string skelFile = FileNameOrEmpty(skeletonPathName);

            if (string.IsNullOrWhiteSpace(meshFile) && string.IsNullOrWhiteSpace(skelFile))
            {
                return null;
            }

            // Protagonist models often reference a local skeleton but require the base skeleton (p0_base)
            // located relative to the model folder (see ResolveBaseTrsklPath). Mesh filenames are not
            // reliably prefixed with p0/p1/p2 for clothing/accessory sets, so also use directory hints.
            if (StartsWithAny(meshFile, "p0", "p1", "p2") || StartsWithAny(skelFile, "p0", "p1", "p2"))
            {
                return "Protag";
            }

            string trmdlLower = (trmdlPath ?? string.Empty).Replace('\\', '/').ToLowerInvariant();
            if (trmdlLower.Contains("/ik_chara/") && (trmdlLower.Contains("/model_pc/") || trmdlLower.Contains("/model_pc_base/")))
            {
                return "Protag";
            }

            // Common NPC prefixes
            if (meshFile.StartsWith("bu_", StringComparison.OrdinalIgnoreCase)) return "CommonNPCbu";
            if (meshFile.StartsWith("dm_", StringComparison.OrdinalIgnoreCase)) return "CommonNPCdm";
            if (meshFile.StartsWith("df_", StringComparison.OrdinalIgnoreCase)) return "CommonNPCdf";
            if (meshFile.StartsWith("em_", StringComparison.OrdinalIgnoreCase)) return "CommonNPCem";
            if (meshFile.StartsWith("fm_", StringComparison.OrdinalIgnoreCase)) return "CommonNPCfm";
            if (meshFile.StartsWith("ff_", StringComparison.OrdinalIgnoreCase)) return "CommonNPCff";
            if (meshFile.StartsWith("gm_", StringComparison.OrdinalIgnoreCase)) return "CommonNPCgm";
            if (meshFile.StartsWith("gf_", StringComparison.OrdinalIgnoreCase)) return "CommonNPCgf";
            if (meshFile.StartsWith("rv_", StringComparison.OrdinalIgnoreCase)) return "CommonNPCrv";

            return null;
        }

        private static string ResolveTrmtrPath(string trmtrPath, IAssetProvider assetProvider)
        {
            if (!RenderOptions.UseRareTrmtrMaterials)
            {
                return trmtrPath;
            }

            if (string.IsNullOrWhiteSpace(trmtrPath) || !trmtrPath.EndsWith(".trmtr", StringComparison.OrdinalIgnoreCase))
            {
                return trmtrPath;
            }

            var dir = Path.GetDirectoryName(trmtrPath);
            var stem = Path.GetFileNameWithoutExtension(trmtrPath);
            if (string.IsNullOrWhiteSpace(dir) || string.IsNullOrWhiteSpace(stem))
            {
                return trmtrPath;
            }

            var rarePath = Path.Combine(dir, $"{stem}_rare.trmtr");
            return assetProvider.Exists(rarePath) ? rarePath : trmtrPath;
        }

        public (UvSetOverride LayerMask, UvSetOverride AO) GetUvOverrides(int submeshIndex)
        {
            if (submeshIndex < 0)
            {
                return (UvSetOverride.Material, UvSetOverride.Material);
            }

            if (uvOverridesBySubmesh.TryGetValue(submeshIndex, out var overrides))
            {
                return overrides;
            }

            return (UvSetOverride.Material, UvSetOverride.Material);
        }

        public void SetUvOverrides(int submeshIndex, UvSetOverride layerMaskOverride, UvSetOverride aoOverride)
        {
            if (submeshIndex < 0)
            {
                return;
            }

            if (layerMaskOverride == UvSetOverride.Material && aoOverride == UvSetOverride.Material)
            {
                uvOverridesBySubmesh.Remove(submeshIndex);
                return;
            }

            uvOverridesBySubmesh[submeshIndex] = (layerMaskOverride, aoOverride);
        }

        public bool TrySetAssetOverrideBytes(string path, byte[] bytes)
        {
            if (assetProvider is Trinity.Core.Assets.InMemoryOverrideAssetProvider overlay)
            {
                overlay.SetOverride(path, bytes);
                return true;
            }

            return false;
        }

        public void ReloadMeshesFromSource()
        {
            // Clear mesh-derived data.
            Positions.Clear();
            Normals.Clear();
            UVs.Clear();
            UVs2.Clear();
            HasUv1.Clear();
            Colors.Clear();
            Tangents.Clear();
            Binormals.Clear();
            BlendIndicies.Clear();
            BlendIndiciesOriginal.Clear();
            BlendWeights.Clear();
            BlendBoneWeights.Clear();
            BlendMeshNames.Clear();

            Indices.Clear();
            HasVertexColors.Clear();
            HasTangents.Clear();
            HasBinormals.Clear();
            HasSkinning.Clear();

            MaterialNames.Clear();
            SubmeshNames.Clear();
            SubmeshParentNodeNames.Clear();
            rigidParentBoneIndexBySubmesh = null;
            uvOverridesBySubmesh.Clear();
            selectedSubmeshIndex = -1;

            // Reset GPU setup so the renderer can re-upload buffers.
            gpuSetupComplete = false;
            gpuSetupIndex = -1;
            VAOs = Array.Empty<int>();
            VBOs = Array.Empty<int>();
            EBOs = Array.Empty<int>();

            foreach (var meshFile in loadedMeshFiles)
            {
                ParseMesh(meshFile);
            }

            ResolveRigidParentAttachments();
        }

        public void ApplyTrsklFile(string trsklPath, TRSKL trskl)
        {
            if (string.IsNullOrWhiteSpace(trsklPath)) throw new ArgumentException("Missing TRSKL path.", nameof(trsklPath));
            if (trskl == null) throw new ArgumentNullException(nameof(trskl));

            var bytes = FlatBufferConverter.SerializeFrom(trskl);
            if (!TrySetAssetOverrideBytes(trsklPath, bytes))
            {
                throw new InvalidOperationException("Model does not support in-memory asset overrides (expected InMemoryOverrideAssetProvider).");
            }

            ParseArmature(trsklPath);
            ResolveRigidParentAttachments();
            MarkTrsklDirty(trsklPath);
        }
	    }
}
