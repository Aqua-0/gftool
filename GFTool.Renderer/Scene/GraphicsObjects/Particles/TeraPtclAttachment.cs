using GFTool.Renderer.Core;
using GFTool.Renderer.Scene;
using GFTool.Renderer.Scene.GraphicsObjects;
using GFTool.Core.Flatbuffers.TR.Scene;
using GFTool.Core.Flatbuffers.TR.Scene.Components;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.IO;
using Trinity.Core.Assets;
using Trinity.Core.Utils;

namespace GFTool.Renderer.Scene.GraphicsObjects.Particles
{
    public sealed class TeraPtclAttachment : RefObject, IParticleObject, IParticleUpdatable, IDisposable
    {
        private readonly Model targetModel;
        private readonly string crownPtclPath;
        private readonly string prismPtclPath;
        private readonly string trsotPath;
        private readonly PtclEffect crown;
        private readonly PtclEffect prism;
        private int crownBoneIndex = -1;
        private int prismBoneIndex = -1;
        private string crownBoneName = "head";
        private string prismBoneName = string.Empty;
        private bool setupComplete;
        private bool disposed;

        private Vector3 crownLocalOffset = Vector3.Zero;
        private Vector3 prismLocalOffset = Vector3.Zero;
        private float crownScale = 1.0f;
        private float prismScale = 1.0f;
        private Vector3 crownEulerDeg = Vector3.Zero;
        private Vector3 prismEulerDeg = Vector3.Zero;

        private readonly List<(Model Model, Matrix4 LocalMatrix)> templateModels = new List<(Model Model, Matrix4 LocalMatrix)>();

        public TeraPtclAttachment(Model targetModel, string crownPtclPath, string prismPtclPath, string trsotPath)
        {
            this.targetModel = targetModel ?? throw new ArgumentNullException(nameof(targetModel));
            this.crownPtclPath = crownPtclPath ?? string.Empty;
            this.prismPtclPath = prismPtclPath ?? string.Empty;
            this.trsotPath = trsotPath ?? string.Empty;

            crown = new PtclEffect(this.crownPtclPath);
            prism = new PtclEffect(this.prismPtclPath);
            AddChild(crown);
            AddChild(prism);
        }

        public override void Setup()
        {
            if (setupComplete || disposed)
            {
                return;
            }

            setupComplete = true;

            var arm = targetModel.Armature;
            if (arm != null && arm.Bones != null && arm.Bones.Count > 0)
            {
                crownBoneIndex = FindBoneIndex(arm, crownBoneName);
                if (crownBoneIndex < 0)
                {
                    crownBoneIndex = FindHeadBoneIndex(arm);
                }

                prismBoneIndex = FindBoneIndex(arm, prismBoneName);
            }

            TryLoadTemplateModels();
            base.Setup();
        }

        public void Update(float dt)
        {
            if (disposed)
            {
                return;
            }

            UpdateAttachmentTransforms();
            crown.Update(dt);
            prism.Update(dt);
        }

        public void ApplyGemSettings(string modelLocatorBoneName, float modelScale, Vector3 modelOffset, Vector3 modelEulerAnglesDeg, string effectLocatorBoneName, float effectScale, Vector3 effectOffset, Vector3 effectEulerAnglesDeg)
        {
            if (!string.IsNullOrWhiteSpace(modelLocatorBoneName))
            {
                crownBoneName = modelLocatorBoneName;
            }
            if (!string.IsNullOrWhiteSpace(effectLocatorBoneName))
            {
                prismBoneName = effectLocatorBoneName;
            }

            crownScale = modelScale > 0 ? modelScale : 1.0f;
            prismScale = effectScale > 0 ? effectScale : crownScale;
            crownLocalOffset = modelOffset;
            prismLocalOffset = effectOffset;
            crownEulerDeg = modelEulerAnglesDeg;
            prismEulerDeg = effectEulerAnglesDeg;

            var arm = targetModel.Armature;
            if (arm != null && arm.Bones != null && arm.Bones.Count > 0)
            {
                crownBoneIndex = FindBoneIndex(arm, crownBoneName);
                if (crownBoneIndex < 0)
                {
                    crownBoneIndex = FindHeadBoneIndex(arm);
                }
                prismBoneIndex = FindBoneIndex(arm, prismBoneName);
            }
        }

        private void UpdateAttachmentTransforms()
        {
            var arm = targetModel.Armature;
            Matrix4 modelMat = targetModel.GetModelMatrix();

            Vector3 modelOrigin = modelMat.ExtractTranslation();
            Vector3 headPos = modelOrigin + new Vector3(0, 1.0f, 0);
            float groundY = modelOrigin.Y;

            Matrix4 crownBaseMat = Matrix4.CreateTranslation(headPos);
            Matrix4 prismBaseMat = Matrix4.CreateTranslation(new Vector3(modelOrigin.X, groundY, modelOrigin.Z));

            if (arm != null && arm.Bones != null && arm.Bones.Count > 0)
            {
                try
                {
                    var world = arm.GetWorldMatrices();
                    var crownBoneMat = Matrix4.Identity;
                    bool hasCrownBone = crownBoneIndex >= 0 && crownBoneIndex < world.Length;
                    if (hasCrownBone)
                    {
                        crownBoneMat = modelMat * world[crownBoneIndex];
                        headPos = crownBoneMat.ExtractTranslation();
                        crownBaseMat = crownBoneMat;
                    }

                    var prismBoneMat = Matrix4.Identity;
                    bool hasPrismBone = prismBoneIndex >= 0 && prismBoneIndex < world.Length;
                    if (hasPrismBone)
                    {
                        prismBoneMat = modelMat * world[prismBoneIndex];
                        prismBaseMat = prismBoneMat;
                    }

                    // crude ground plane estimate from lowest bone in current pose
                    float minY = float.PositiveInfinity;
                    for (int i = 0; i < world.Length; i++)
                    {
                        float y = (modelMat * world[i]).ExtractTranslation().Y;
                        if (y < minY) minY = y;
                    }
                    if (!float.IsInfinity(minY))
                    {
                        groundY = minY;
                    }

                    if (!hasPrismBone)
                    {
                        prismBaseMat = Matrix4.CreateTranslation(new Vector3(modelOrigin.X, groundY, modelOrigin.Z));
                    }

                }
                catch
                {
                }
            }

            Matrix4 crownLocal =
                Matrix4.CreateTranslation(crownLocalOffset) *
                Matrix4.CreateFromQuaternion(Quaternion.FromEulerAngles(
                    MathHelper.DegreesToRadians(crownEulerDeg.X),
                    MathHelper.DegreesToRadians(crownEulerDeg.Y),
                    MathHelper.DegreesToRadians(crownEulerDeg.Z))) *
                Matrix4.CreateScale(new Vector3(crownScale));
            Matrix4 crownWorld = crownBaseMat * crownLocal;
            crown.Transform.Position = crownWorld.ExtractTranslation();
            crown.Transform.Rotation = crownWorld.ExtractRotation();
            var baseScale = targetModel.Transform.Scale;
            if (baseScale.LengthSquared < 0.000001f)
            {
                baseScale = Vector3.One;
            }
            crown.Transform.Scale = baseScale * crownScale;

            Matrix4 prismLocal =
                Matrix4.CreateTranslation(prismLocalOffset) *
                Matrix4.CreateFromQuaternion(Quaternion.FromEulerAngles(
                    MathHelper.DegreesToRadians(prismEulerDeg.X),
                    MathHelper.DegreesToRadians(prismEulerDeg.Y),
                    MathHelper.DegreesToRadians(prismEulerDeg.Z))) *
                Matrix4.CreateScale(new Vector3(prismScale));
            Matrix4 prismWorld = prismBaseMat * prismLocal;
            prism.Transform.Position = prismWorld.ExtractTranslation();
            prism.Transform.Rotation = prismWorld.ExtractRotation();
            prism.Transform.Scale = baseScale * prismScale;

            if (templateModels.Count != 0)
            {
                for (int i = 0; i < templateModels.Count; i++)
                {
                    var (m, local) = templateModels[i];
                    Matrix4 mw = crownWorld * local;
                    m.Transform.Position = mw.ExtractTranslation();
                    m.Transform.Rotation = mw.ExtractRotation();
                    m.Transform.Scale = mw.ExtractScale();
                }
            }
        }

        private void TryLoadTemplateModels()
        {
            if (string.IsNullOrWhiteSpace(trsotPath) || !File.Exists(trsotPath))
            {
                return;
            }

            if (!TryCollectTemplateModelSpawns(trsotPath, out var spawns))
            {
                return;
            }

            var root = SceneGraph.Instance.GetRoot();
            if (root == null)
            {
                return;
            }

            int loaded = 0;
            int missing = 0;
            string baseDir = Path.GetDirectoryName(trsotPath) ?? string.Empty;
            for (int i = 0; i < spawns.Count; i++)
            {
                var s = spawns[i];
                string? resolvedModel = ResolveReferencedAssetPath(trsotPath, s.ModelPath);
                if (string.IsNullOrWhiteSpace(resolvedModel) || !File.Exists(resolvedModel))
                {
                    missing++;
                    continue;
                }

                var assetProvider = new InMemoryOverrideAssetProvider(new DiskAssetProvider());
                var m = new Model(assetProvider, resolvedModel, loadAllLods: false);
                root.AddChild(m);
                templateModels.Add((m, s.LocalMatrix));
                loaded++;
            }

            if (loaded != 0 || missing != 0)
            {
                MessageHandler.Instance.AddMessage(
                    MessageType.LOG,
                    $"[Tera] Loaded trsot models: ok={loaded} missing={missing} template='{Path.GetFileName(trsotPath)}'");
            }
        }

        private sealed record TemplateModelSpawn(string ModelPath, Matrix4 LocalMatrix);

        private static bool TryCollectTemplateModelSpawns(string templateFile, out List<TemplateModelSpawn> spawns)
        {
            spawns = new List<TemplateModelSpawn>();
            if (string.IsNullOrWhiteSpace(templateFile) || !File.Exists(templateFile))
            {
                return false;
            }

            TRSCN? t;
            try
            {
                t = FlatBufferConverter.DeserializeFrom<TRSCN>(templateFile);
            }
            catch
            {
                return false;
            }

            if (t?.Chunks == null)
            {
                return false;
            }

            var loadedScenes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { templateFile };
            CollectTemplateSpawnsFromChunks(templateFile, t.Chunks, Matrix4.Identity, loadedScenes, spawns);
            return spawns.Count != 0;
        }

        private static void CollectTemplateSpawnsFromChunks(
            string sceneFile,
            SceneChunk[] chunks,
            Matrix4 parentMatrix,
            HashSet<string> loadedScenes,
            List<TemplateModelSpawn> spawns)
        {
            for (int i = 0; i < chunks.Length; i++)
            {
                var chunk = chunks[i];
                if (chunk == null || string.IsNullOrWhiteSpace(chunk.Type))
                {
                    continue;
                }

                if (chunk.Type == nameof(SubScene))
                {
                    TryCollectSubSceneSpawns(sceneFile, chunk, parentMatrix, loadedScenes, spawns);
                }
                else if (chunk.Type == nameof(trinity_SceneObject))
                {
                    TryCollectSceneObjectSpawns(sceneFile, chunk, parentMatrix, loadedScenes, spawns);
                }
                else if (chunk.Type == nameof(trinity_ObjectTemplate))
                {
                    TryCollectNestedTemplateSpawns(sceneFile, chunk, parentMatrix, loadedScenes, spawns);
                }

                if (chunk.Children != null && chunk.Children.Length != 0)
                {
                    CollectTemplateSpawnsFromChunks(sceneFile, chunk.Children, parentMatrix, loadedScenes, spawns);
                }
            }
        }

        private static void TryCollectSubSceneSpawns(
            string sceneFile,
            SceneChunk chunk,
            Matrix4 parentMatrix,
            HashSet<string> loadedScenes,
            List<TemplateModelSpawn> spawns)
        {
            try
            {
                var sub = FlatBufferConverter.DeserializeFrom<SubScene>(chunk.Data);
                if (sub == null || string.IsNullOrWhiteSpace(sub.Filepath))
                {
                    return;
                }

                var resolved = ResolveSceneReference(sceneFile, sub.Filepath);
                if (resolved == null || !File.Exists(resolved) || !loadedScenes.Add(resolved))
                {
                    return;
                }

                TRSCN t = FlatBufferConverter.DeserializeFrom<TRSCN>(resolved);
                if (t?.Chunks != null)
                {
                    CollectTemplateSpawnsFromChunks(resolved, t.Chunks, parentMatrix, loadedScenes, spawns);
                }
            }
            catch
            {
            }
        }

        private static void TryCollectSceneObjectSpawns(
            string sceneFile,
            SceneChunk chunk,
            Matrix4 parentMatrix,
            HashSet<string> loadedScenes,
            List<TemplateModelSpawn> spawns)
        {
            trinity_SceneObject? so;
            try
            {
                so = FlatBufferConverter.DeserializeFrom<trinity_SceneObject>(chunk.Data);
            }
            catch
            {
                return;
            }

            if (so == null)
            {
                return;
            }

            Matrix4 soMat = parentMatrix * BuildSrtMatrix(so.Srt);

            if (chunk.Children != null)
            {
                for (int i = 0; i < chunk.Children.Length; i++)
                {
                    var child = chunk.Children[i];
                    if (child == null || string.IsNullOrWhiteSpace(child.Type))
                    {
                        continue;
                    }

                    if (child.Type == nameof(trinity_ModelComponent))
                    {
                        try
                        {
                            var mc = FlatBufferConverter.DeserializeFrom<trinity_ModelComponent>(child.Data);
                            if (mc != null && !string.IsNullOrWhiteSpace(mc.FilePath))
                            {
                                spawns.Add(new TemplateModelSpawn(mc.FilePath, soMat));
                            }
                        }
                        catch
                        {
                        }
                    }
                    else if (child.Type == nameof(trinity_ObjectTemplate))
                    {
                        TryCollectNestedTemplateSpawns(sceneFile, child, soMat, loadedScenes, spawns);
                    }
                }
            }
        }

        private static void TryCollectNestedTemplateSpawns(
            string sceneFile,
            SceneChunk templateChunk,
            Matrix4 instanceMatrix,
            HashSet<string> loadedScenes,
            List<TemplateModelSpawn> spawns)
        {
            trinity_ObjectTemplate? ot;
            try
            {
                ot = FlatBufferConverter.DeserializeFrom<trinity_ObjectTemplate>(templateChunk.Data);
            }
            catch
            {
                return;
            }

            if (ot == null || string.IsNullOrWhiteSpace(ot.FilePath))
            {
                return;
            }

            var resolved = ResolveSceneReference(sceneFile, ot.FilePath);
            if (resolved == null || !File.Exists(resolved))
            {
                return;
            }

            if (!loadedScenes.Add(resolved))
            {
                return;
            }

            try
            {
                TRSCN t = FlatBufferConverter.DeserializeFrom<TRSCN>(resolved);
                if (t?.Chunks != null)
                {
                    CollectTemplateSpawnsFromChunks(resolved, t.Chunks, instanceMatrix, loadedScenes, spawns);
                }
            }
            catch
            {
            }
        }

        private static Matrix4 BuildSrtMatrix(trinity_Transform? srt)
        {
            if (srt == null)
            {
                return Matrix4.Identity;
            }

            Vector3 scale = srt.Scale != null ? new Vector3(srt.Scale.X, srt.Scale.Y, srt.Scale.Z) : Vector3.One;
            Quaternion rot = Quaternion.Identity;
            if (srt.Rotate != null)
            {
                float rx = MathHelper.DegreesToRadians(srt.Rotate.X);
                float ry = MathHelper.DegreesToRadians(srt.Rotate.Y);
                float rz = MathHelper.DegreesToRadians(srt.Rotate.Z);
                rot = Quaternion.FromEulerAngles(rx, ry, rz);
                rot.Normalize();
            }
            Vector3 trans = srt.Translate != null ? new Vector3(srt.Translate.X, srt.Translate.Y, srt.Translate.Z) : Vector3.Zero;
            return Matrix4.CreateTranslation(trans) * Matrix4.CreateFromQuaternion(rot) * Matrix4.CreateScale(scale);
        }

        private static string? ResolveSceneReference(string sceneFile, string referencedPath)
        {
            if (string.IsNullOrWhiteSpace(sceneFile) || string.IsNullOrWhiteSpace(referencedPath))
            {
                return null;
            }

            string baseDir = Path.GetDirectoryName(sceneFile) ?? string.Empty;
            string normalized = referencedPath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);

            string candidate = Path.GetFullPath(Path.Combine(baseDir, normalized));
            if (File.Exists(candidate))
            {
                return candidate;
            }

            string ext = Path.GetExtension(candidate);
            if (!string.IsNullOrWhiteSpace(ext))
            {
                string fileNameNoExt = Path.GetFileNameWithoutExtension(candidate);
                if (fileNameNoExt.EndsWith("_0", StringComparison.OrdinalIgnoreCase) ||
                    fileNameNoExt.EndsWith("_1", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                string noExt = candidate.Substring(0, candidate.Length - ext.Length);
                string with0 = $"{noExt}_0{ext}";
                if (File.Exists(with0)) return with0;
                string with1 = $"{noExt}_1{ext}";
                if (File.Exists(with1)) return with1;
            }

            return null;
        }

        private static string? ResolveReferencedAssetPath(string sceneFile, string referencedPath)
        {
            if (string.IsNullOrWhiteSpace(sceneFile) || string.IsNullOrWhiteSpace(referencedPath))
            {
                return null;
            }

            string baseDir = Path.GetDirectoryName(sceneFile) ?? string.Empty;
            string normalized = referencedPath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);

            // First try relative to the template file.
            string candidate = Path.GetFullPath(Path.Combine(baseDir, normalized));
            if (File.Exists(candidate))
            {
                return candidate;
            }

            // If the reference looks rooted (effect/world/ui/...), derive extracted root from the trsot path.
            string? extractedRoot = TryDeriveExtractedRoot(sceneFile);
            if (!string.IsNullOrWhiteSpace(extractedRoot))
            {
                string n = normalized.TrimStart(Path.DirectorySeparatorChar);
                foreach (var rootName in new[] { "effect", "world", "ui", "field", "model", "system" })
                {
                    string needle = rootName + Path.DirectorySeparatorChar;
                    int idx = n.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
                    if (idx >= 0)
                    {
                        string rel = n.Substring(idx);
                        string c2 = Path.GetFullPath(Path.Combine(extractedRoot, rel));
                        if (File.Exists(c2))
                        {
                            return c2;
                        }
                    }
                }
            }

            return null;
        }

        private static string? TryDeriveExtractedRoot(string anyFileUnderRoot)
        {
            string normalized = anyFileUnderRoot.Replace('\\', '/');
            int idx = normalized.IndexOf("/effect/", StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                idx = normalized.IndexOf("/world/", StringComparison.OrdinalIgnoreCase);
            }
            if (idx < 0)
            {
                return null;
            }
            string root = normalized.Substring(0, idx);
            root = root.Replace('/', Path.DirectorySeparatorChar);
            return root;
        }

        private static int FindBoneIndex(Armature arm, string boneName)
        {
            if (arm == null || arm.Bones == null || arm.Bones.Count == 0 || string.IsNullOrWhiteSpace(boneName))
            {
                return -1;
            }

            for (int i = 0; i < arm.Bones.Count; i++)
            {
                if (string.Equals(arm.Bones[i].Name, boneName, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        private static int FindHeadBoneIndex(Armature arm)
        {
            int count = arm.Bones.Count;

            int FindExact(string name)
            {
                for (int i = 0; i < count; i++)
                {
                    if (string.Equals(arm.Bones[i].Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        return i;
                    }
                }
                return -1;
            }

            int idx = FindExact("head");
            if (idx >= 0) return idx;
            idx = FindExact("Head");
            if (idx >= 0) return idx;

            for (int i = 0; i < count; i++)
            {
                var n = arm.Bones[i].Name ?? string.Empty;
                if (n.IndexOf("head", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return i;
                }
            }

            // Fallback: highest Y in rest pose.
            try
            {
                var mats = arm.GetWorldMatrices();
                float bestY = float.NegativeInfinity;
                int best = 0;
                for (int i = 0; i < mats.Length; i++)
                {
                    float y = mats[i].ExtractTranslation().Y;
                    if (y > bestY)
                    {
                        bestY = y;
                        best = i;
                    }
                }
                return best;
            }
            catch
            {
                return 0;
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;

            try
            {
                var root = SceneGraph.Instance.GetRoot();
                if (root != null && templateModels.Count != 0)
                {
                    for (int i = 0; i < templateModels.Count; i++)
                    {
                        var m = templateModels[i].Model;
                        try { root.children.Remove(m); } catch { }
                    }
                }
            }
            catch
            {
            }
            templateModels.Clear();

            crown.Dispose();
            prism.Dispose();
        }
    }
}
