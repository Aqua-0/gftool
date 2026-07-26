using GFTool.Core.Flatbuffers.TR.Scene.Components;
using GFTool.Core.Flatbuffers.TR.Scene;
using GFTool.Renderer;
using GFTool.Renderer.Core;
using GFTool.Renderer.Core.Graphics;
using GFTool.Renderer.Scene.GraphicsObjects;
using OpenTK.Mathematics;
using System;
using System.Drawing;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using Trinity.Core.Utils;
using Trinity.Core.Assets;
using System.Linq;
using Point = System.Drawing.Point;


namespace TrinitySceneView
{
    public partial class SceneViewerForm : Form
    {
        private const int MergedStaticBatchMinCount = 3;
        private NpcSpawnerDbCache? npcSpawnerDb;
        private string? selectedSpawnerId;

        private CancellationTokenSource? sceneLoadCts;
        private bool isSceneLoading;
        private int sceneLoadVersion;

        private Task TryLoadSceneModelsAsync(string sceneFile)
        {
            return TryLoadSceneModelsAsync(sceneFile, CancellationToken.None);
        }

        private async Task TryLoadSceneModelsAsync(string sceneFile, CancellationToken externalToken)
        {
            if (renderCtrl?.renderer == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(assetRoot) || !Directory.Exists(assetRoot))
            {
                MessageHandler.Instance.AddMessage(MessageType.WARNING, "[Scene] Asset root not set; use File -> Set Asset Root... to enable model loading.");
                return;
            }

            CancelSceneLoad();
            int loadVersion = Interlocked.Increment(ref sceneLoadVersion);
            var myCts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
            sceneLoadCts = myCts;
            var token = myCts.Token;

            isSceneLoading = true;
            if (!config.AdditiveLoads)
            {
                renderCtrl.renderer.ClearScene();
                ClearModelsList();
                loadedSceneModelInstances.Clear();
            }
            renderCtrl.Invalidate();

            BeginSceneLoadUi(loadVersion, "Scanning scene...");
            var loadStopwatch = Stopwatch.StartNew();
            bool fastOverviewLoading = config.FastOverviewLoading;
            bool diskSceneCache = config.DiskSceneCache;
            bool loadAllLods = config.LoadAllLods;

            try
            {
                SceneModelSpawnCollectionResult result;
                if (diskSceneCache && TryLoadCachedSceneModelSpawns(sceneFile, token, out var cachedResult))
                {
                    result = cachedResult;
                }
                else
                {
                    result = await Task.Run(() => CollectSceneModelSpawns(sceneFile, token), token);
                    if (diskSceneCache)
                    {
                        TryWriteCachedSceneModelSpawns(sceneFile, result);
                    }
                }

                double scanElapsedMs = loadStopwatch.Elapsed.TotalMilliseconds;
                npcSpawnerDb = result.NpcDb;
                var spawns = result.Spawns;

                if (spawns.Count == 0)
                {
                    EndSceneLoadUi(loadVersion, "No models found.");
                    return;
                }

                var spawnedPositions = new List<Vector3>(spawns.Count);
                int completed = 0;
                int total = spawns.Count;
                int sharedInstanceCount = 0;
                int maxCpuParallelism = GetSceneLoadCpuParallelism();
                using var buildSemaphore = new SemaphoreSlim(maxCpuParallelism, maxCpuParallelism);
                var resolvedPaths = new string?[spawns.Count];
                var prepareByResolvedPath = new Dictionary<string, Task<PreparedSceneModel>>(StringComparer.OrdinalIgnoreCase);
                var templateByResolvedPath = new Dictionary<string, Model>(StringComparer.OrdinalIgnoreCase);
                var spawnCountByResolvedPath = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                double uniquePrepareTotalMs = 0.0;
                double uniquePrepareMaxMs = 0.0;
                string? uniquePrepareMaxPath = null;
                double uniquePrepareMeshTotalMs = 0.0;
                double uniquePrepareMaterialTotalMs = 0.0;
                double uniquePrepareSkeletonTotalMs = 0.0;
                double uniquePrepareMeshReadTotalMs = 0.0;
                double uniquePrepareMeshMorphTotalMs = 0.0;
                double uniquePrepareMeshDecodeTotalMs = 0.0;
                double uniquePrepareMaterialReadTotalMs = 0.0;
                double uniquePrepareMaterialDeserializeTotalMs = 0.0;
                double uniquePrepareMaterialLegacyDeserializeTotalMs = 0.0;
                double uniquePrepareMaterialBuildTotalMs = 0.0;
                double uniquePrepareMeshMaxMs = 0.0;
                string? uniquePrepareMeshMaxPath = null;
                double uniquePrepareMaterialMaxMs = 0.0;
                string? uniquePrepareMaterialMaxPath = null;
                double uniquePrepareSkeletonMaxMs = 0.0;
                string? uniquePrepareSkeletonMaxPath = null;
                int preparedMeshCacheHitCount = 0;
                double uniqueUploadTotalMs = 0.0;
                double uniqueUploadMaxMs = 0.0;
                string? uniqueUploadMaxPath = null;
                double uniqueUploadGpuSetupTotalMs = 0.0;
                double uniqueUploadShaderWarmupTotalMs = 0.0;
                double uniqueUploadTextureTotalMs = 0.0;
                double uniqueUploadGpuSetupMaxMs = 0.0;
                string? uniqueUploadGpuSetupMaxPath = null;
                double uniqueUploadShaderWarmupMaxMs = 0.0;
                string? uniqueUploadShaderWarmupMaxPath = null;
                double uniqueUploadTextureMaxMs = 0.0;
                string? uniqueUploadTextureMaxPath = null;
                double sharedCloneTotalMs = 0.0;
                double sharedCloneMaxMs = 0.0;
                string? sharedCloneMaxPath = null;
                var fastBatchedSpawns = new HashSet<SceneModelSpawn>();
                FastOverviewLoadPlan? fastOverviewPlan = null;
                int fastOverviewBatchCount = 0;
                int fastOverviewMergedInstanceCount = 0;
                int fastOverviewCacheHitCount = 0;
                double fastOverviewUploadTotalMs = 0.0;
                double fastOverviewUploadMaxMs = 0.0;
                string? fastOverviewUploadMaxPath = null;

                for (int i = 0; i < spawns.Count; i++)
                {
                    var spawn = spawns[i];
                    var resolved = ResolveModelPath(spawn.ModelPath);
                    resolvedPaths[i] = resolved;
                    if (resolved == null)
                    {
                        continue;
                    }

                    if (spawnCountByResolvedPath.TryGetValue(resolved, out int existingCount))
                    {
                        spawnCountByResolvedPath[resolved] = existingCount + 1;
                    }
                    else
                    {
                        spawnCountByResolvedPath[resolved] = 1;
                    }

                    if (prepareByResolvedPath.ContainsKey(resolved))
                    {
                        continue;
                    }

                    string? preparedMeshCachePath = null;
                    if (diskSceneCache)
                    {
                        var preparedMeshCacheKey = SceneDiskCache.BuildPreparedModelCacheKey(resolved, loadAllLods);
                        preparedMeshCachePath = SceneDiskCache.GetWritableCacheFilePath(
                            config.SceneDiskCacheDirectory,
                            preparedMeshCacheKey,
                            SceneDiskCache.PreparedModelCacheExtension);
                    }

                    prepareByResolvedPath[resolved] = PrepareSceneModelAsync(
                        resolved,
                        loadAllLods,
                        preparedMeshCachePath,
                        buildSemaphore,
                        token);
                }

                int uniqueModelCount = prepareByResolvedPath.Count;
                var preparePerfByPath = new List<(string Path, double Total, double Mesh, double MeshRead, double MeshMorph, double MeshDecode, double Material, double MaterialRead, double MaterialDeserialize, double MaterialLegacyDeserialize, double MaterialBuild, double Skeleton)>(uniqueModelCount);
                var recordedPreparePerfPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                void RecordPreparedPerf(PreparedSceneModel prepared)
                {
                    if (!recordedPreparePerfPaths.Add(prepared.ResolvedPath))
                    {
                        return;
                    }

                    uniquePrepareTotalMs += prepared.PrepareElapsedMs;
                    uniquePrepareMeshTotalMs += prepared.PrepareMeshMs;
                    uniquePrepareMeshReadTotalMs += prepared.PrepareMeshReadMs;
                    uniquePrepareMeshMorphTotalMs += prepared.PrepareMeshMorphMs;
                    uniquePrepareMeshDecodeTotalMs += prepared.PrepareMeshDecodeMs;
                    uniquePrepareMaterialTotalMs += prepared.PrepareMaterialMs;
                    uniquePrepareMaterialReadTotalMs += prepared.PrepareMaterialReadMs;
                    uniquePrepareMaterialDeserializeTotalMs += prepared.PrepareMaterialDeserializeMs;
                    uniquePrepareMaterialLegacyDeserializeTotalMs += prepared.PrepareMaterialLegacyDeserializeMs;
                    uniquePrepareMaterialBuildTotalMs += prepared.PrepareMaterialBuildMs;
                    uniquePrepareSkeletonTotalMs += prepared.PrepareSkeletonMs;
                    if (prepared.PersistentMeshCacheHit)
                    {
                        preparedMeshCacheHitCount++;
                    }
                    preparePerfByPath.Add((prepared.ResolvedPath, prepared.PrepareElapsedMs, prepared.PrepareMeshMs, prepared.PrepareMeshReadMs, prepared.PrepareMeshMorphMs, prepared.PrepareMeshDecodeMs, prepared.PrepareMaterialMs, prepared.PrepareMaterialReadMs, prepared.PrepareMaterialDeserializeMs, prepared.PrepareMaterialLegacyDeserializeMs, prepared.PrepareMaterialBuildMs, prepared.PrepareSkeletonMs));
                    if (prepared.PrepareElapsedMs > uniquePrepareMaxMs)
                    {
                        uniquePrepareMaxMs = prepared.PrepareElapsedMs;
                        uniquePrepareMaxPath = prepared.ResolvedPath;
                    }
                    if (prepared.PrepareMeshMs > uniquePrepareMeshMaxMs)
                    {
                        uniquePrepareMeshMaxMs = prepared.PrepareMeshMs;
                        uniquePrepareMeshMaxPath = prepared.ResolvedPath;
                    }
                    if (prepared.PrepareMaterialMs > uniquePrepareMaterialMaxMs)
                    {
                        uniquePrepareMaterialMaxMs = prepared.PrepareMaterialMs;
                        uniquePrepareMaterialMaxPath = prepared.ResolvedPath;
                    }
                    if (prepared.PrepareSkeletonMs > uniquePrepareSkeletonMaxMs)
                    {
                        uniquePrepareSkeletonMaxMs = prepared.PrepareSkeletonMs;
                        uniquePrepareSkeletonMaxPath = prepared.ResolvedPath;
                    }
                }

                if (fastOverviewLoading)
                {
                    fastOverviewPlan = BuildFastOverviewLoadPlan(spawns, resolvedPaths, MergedStaticBatchMinCount);
                    if (SceneDiagnosticsEnabled)
                    {
                        MessageHandler.Instance.AddMessage(
                            MessageType.LOG,
                            $"[Scene][FastOverviewPlan] candidates={fastOverviewPlan.BatchGroups.Count} candidateInstances={fastOverviewPlan.BatchGroups.Sum(g => g.Spawns.Count)} individual={fastOverviewPlan.IndividualSpawns.Count}");
                    }

                    foreach (var group in fastOverviewPlan.BatchGroups)
                    {
                        token.ThrowIfCancellationRequested();
                        var batchResult = await TryLoadFastOverviewBatchAsync(
                            group,
                            prepareByResolvedPath,
                            spawnedPositions,
                            loadVersion,
                            completed,
                            total,
                            diskSceneCache,
                            loadAllLods,
                            token,
                            RecordPreparedPerf);

                        if (!batchResult.Applied)
                        {
                            continue;
                        }

                        foreach (var spawn in group.Spawns)
                        {
                            fastBatchedSpawns.Add(spawn);
                        }

                        completed += batchResult.CompletedSpawns;
                        fastOverviewBatchCount++;
                        fastOverviewMergedInstanceCount += batchResult.MergedInstanceCount;
                        if (batchResult.PersistentCacheHit)
                        {
                            fastOverviewCacheHitCount++;
                        }
                        fastOverviewUploadTotalMs += batchResult.UploadElapsedMs;
                        if (batchResult.UploadElapsedMs > fastOverviewUploadMaxMs)
                        {
                            fastOverviewUploadMaxMs = batchResult.UploadElapsedMs;
                            fastOverviewUploadMaxPath = batchResult.SourcePath;
                        }

                        int percentBatch = ComputeOverallPercent(total, completed, 1.0f);
                        ReportSceneLoadUi(loadVersion, percentBatch, $"Loaded {completed}/{total}");
                    }
                }

                for (int i = 0; i < spawns.Count; i++)
                {
                    token.ThrowIfCancellationRequested();

                    var spawn = spawns[i];
                    if (fastBatchedSpawns.Contains(spawn))
                    {
                        continue;
                    }

                    var resolvedPath = resolvedPaths[i];
                    if (resolvedPath == null)
                    {
                        completed++;
                        int percentMissing = ComputeOverallPercent(total, completed, 1.0f);
                        ReportSceneLoadUi(loadVersion, percentMissing, $"Missing model ({completed}/{total})");
                        MessageHandler.Instance.AddMessage(
                            MessageType.WARNING,
                            $"[Scene] Missing model file: {spawn.ModelPath} (SceneObject={spawn.SceneObjectName ?? "(null)"}, scene={Path.GetFileName(spawn.SceneFile)})");
                        continue;
                    }

                    Model model;
                    string loadedPath = resolvedPath;
                    bool reusedTemplate = false;
                    if (templateByResolvedPath.TryGetValue(resolvedPath, out var templateModel))
                    {
                        var cloneStopwatch = Stopwatch.StartNew();
                        model = templateModel.CreateSharedSceneInstance();
                        renderCtrl.renderer.AddSceneModelDeferred(model);
                        cloneStopwatch.Stop();
                        reusedTemplate = true;
                        sharedInstanceCount++;
                        sharedCloneTotalMs += cloneStopwatch.Elapsed.TotalMilliseconds;
                        if (cloneStopwatch.Elapsed.TotalMilliseconds > sharedCloneMaxMs)
                        {
                            sharedCloneMaxMs = cloneStopwatch.Elapsed.TotalMilliseconds;
                            sharedCloneMaxPath = resolvedPath;
                        }
                    }
                    else
                    {
                        var preparedTask = prepareByResolvedPath[resolvedPath];
                        PreparedSceneModel prepared;
                        try
                        {
                            prepared = await preparedTask;
                            token.ThrowIfCancellationRequested();
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            completed++;
                            int percentFailed = ComputeOverallPercent(total, completed, 1.0f);
                            ReportSceneLoadUi(loadVersion, percentFailed, $"Failed ({completed}/{total})");
                            MessageHandler.Instance.AddMessage(
                                MessageType.WARNING,
                                $"[Scene] Failed to prepare model '{spawn.ModelPath}' (SceneObject={spawn.SceneObjectName ?? "(null)"}): {ex.GetType().Name}: {ex.Message}");
                            continue;
                        }

                        RecordPreparedPerf(prepared);

                        var progress = new Progress<float>(p =>
                        {
                            int percent = ComputeOverallPercent(total, completed, p);
                            ReportSceneLoadUi(loadVersion, percent, $"Loading {completed + 1}/{total}: {Path.GetFileNameWithoutExtension(prepared.ResolvedPath)}");
                        });

                        var uploadStopwatch = Stopwatch.StartNew();
                        try
                        {
                            model = await renderCtrl.renderer.AddPreparedSceneModelAsync(prepared.Model, token: token, progress: progress);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            completed++;
                            int percentFailed = ComputeOverallPercent(total, completed, 1.0f);
                            ReportSceneLoadUi(loadVersion, percentFailed, $"Failed ({completed}/{total})");
                            MessageHandler.Instance.AddMessage(
                                MessageType.WARNING,
                                $"[Scene] Failed to load model '{prepared.ResolvedPath}' (SceneObject={spawn.SceneObjectName ?? "(null)"}): {ex.GetType().Name}: {ex.Message}");
                            continue;
                        }
                        finally
                        {
                            uploadStopwatch.Stop();
                        }

                        uniqueUploadTotalMs += uploadStopwatch.Elapsed.TotalMilliseconds;
                        if (uploadStopwatch.Elapsed.TotalMilliseconds > uniqueUploadMaxMs)
                        {
                            uniqueUploadMaxMs = uploadStopwatch.Elapsed.TotalMilliseconds;
                            uniqueUploadMaxPath = prepared.ResolvedPath;
                        }

                        var uploadPerf = model.LastAsyncLoadPerfStats;
                        uniqueUploadGpuSetupTotalMs += uploadPerf.GpuSetupMs;
                        uniqueUploadShaderWarmupTotalMs += uploadPerf.ShaderWarmupMs;
                        uniqueUploadTextureTotalMs += uploadPerf.TextureUploadMs;
                        if (uploadPerf.GpuSetupMs > uniqueUploadGpuSetupMaxMs)
                        {
                            uniqueUploadGpuSetupMaxMs = uploadPerf.GpuSetupMs;
                            uniqueUploadGpuSetupMaxPath = prepared.ResolvedPath;
                        }
                        if (uploadPerf.ShaderWarmupMs > uniqueUploadShaderWarmupMaxMs)
                        {
                            uniqueUploadShaderWarmupMaxMs = uploadPerf.ShaderWarmupMs;
                            uniqueUploadShaderWarmupMaxPath = prepared.ResolvedPath;
                        }
                        if (uploadPerf.TextureUploadMs > uniqueUploadTextureMaxMs)
                        {
                            uniqueUploadTextureMaxMs = uploadPerf.TextureUploadMs;
                            uniqueUploadTextureMaxPath = prepared.ResolvedPath;
                        }

                        loadedPath = prepared.ResolvedPath;
                        templateByResolvedPath[resolvedPath] = model;

                    }

                    var attachMat = ApplyAttachTransformOverride(
                        spawn.ModelMatrix,
                        spawn.LocalMatrix,
                        spawn.ParentSceneObjectName,
                        spawn.ParentSceneObjectWorldMatrix,
                        spawn.AttachTransformEnable,
                        spawn.AttachJointName,
                        spawn.KeepWorldSrt);
                    var mat = GetFinalSceneModelMatrix(attachMat, spawn.IsTrinsInstance, spawn.SceneObjectName, spawn.ModelPath, out var position, out var scale);
                    position = new Vector3(mat.M41, mat.M42, mat.M43);

                    model.SetModelMatrix(mat);
                    spawnedPositions.Add(position);
                    AddModelToList(spawn.SceneObjectName, spawn.ModelPath, model);
                    loadedSceneModelInstances.Add(new LoadedSceneModelInstance
                    {
                        Name = string.IsNullOrWhiteSpace(spawn.SceneObjectName)
                            ? Path.GetFileNameWithoutExtension(spawn.ModelPath)
                            : spawn.SceneObjectName,
                        SourcePath = loadedPath,
                        BaseTransform = spawn.ModelMatrix,
                        LocalTransform = spawn.LocalMatrix,
                        IsTrinsInstance = spawn.IsTrinsInstance,
                        KeepWorldSrt = spawn.KeepWorldSrt,
                        AttachTransformEnable = spawn.AttachTransformEnable,
                        AttachJointName = spawn.AttachJointName,
                        ParentSceneObjectName = spawn.ParentSceneObjectName,
                        ParentSceneObjectWorldMatrix = spawn.ParentSceneObjectWorldMatrix,
                        Transform = mat,
                        Model = model
                    });

                    completed++;
                    int percentDone = ComputeOverallPercent(total, completed, 1.0f);
                    ReportSceneLoadUi(loadVersion, percentDone, $"Loaded {completed}/{total}");

                    if (SceneDiagnosticsMatchesTarget(spawn.SceneObjectName, spawn.ModelPath))
                    {
                        var rotation = ExtractNormalizedRotation(mat);
                        MessageHandler.Instance.AddMessage(
                            MessageType.LOG,
                            $"[Scene][TargetFinal] obj='{spawn.SceneObjectName}' model='{spawn.ModelPath}' parent='{spawn.ParentSceneObjectName ?? ""}' attach={spawn.AttachTransformEnable} keepWorld={spawn.KeepWorldSrt} pos={position} rot=({rotation.W}, {rotation.X}, {rotation.Y}, {rotation.Z}) scale={scale}{(reusedTemplate ? " (shared instance)" : string.Empty)}{(config.SpawnModelsAtOrigin ? " (origin override)" : "")}");
                    }
                }

                if (fastOverviewLoading)
                {
                    await TryBatchRepeatedStaticModelsAsync(
                        loadVersion,
                        diskSceneCache,
                        loadAllLods,
                        token);
                }

                if (spawnedPositions.Count > 0)
                {
                    var min = new Vector3(float.PositiveInfinity);
                    var max = new Vector3(float.NegativeInfinity);
                    foreach (var p in spawnedPositions)
                    {
                        min = Vector3.ComponentMin(min, p);
                        max = Vector3.ComponentMax(max, p);
                    }

                    var center = (min + max) * 0.5f;
                    var radius = (max - min).Length * 0.5f;
                    // Start close for small props, but auto-dolly out for large scenes.
                    var distance = MathF.Max(2.5f, radius * 2.5f);
                    renderCtrl.renderer.FocusCamera(center, distance);
                    ApplySceneClipPlanes(center, radius);

                    if (SceneDiagnosticsEnabled)
                    {
                        MessageHandler.Instance.AddMessage(
                        MessageType.LOG,
                        $"[Scene] Focus camera at {center} (models={spawnedPositions.Count}, radius≈{radius:0.###}, dist≈{distance:0.###}).");
	                    }
	                }

		                EndSceneLoadUi(loadVersion, $"Loaded {completed}/{total} model(s).");
                    loadStopwatch.Stop();
                    double finalizeElapsedMs = loadStopwatch.Elapsed.TotalMilliseconds - scanElapsedMs - uniqueUploadTotalMs;
                    if (SceneDiagnosticsEnabled)
                    {
                        MessageHandler.Instance.AddMessage(
                            MessageType.LOG,
                            $"[Scene] Load finished in {loadStopwatch.Elapsed.TotalSeconds:0.###}s ({completed}/{total} model(s), unique={uniqueModelCount}, shared={sharedInstanceCount}, prepared-cache={preparedMeshCacheHitCount}/{uniqueModelCount}, cpu-parallelism={maxCpuParallelism}).");
                        MessageHandler.Instance.AddMessage(
                            MessageType.LOG,
                            $"[Scene][Timing] scan={scanElapsedMs:0.###}ms prepare(sum={uniquePrepareTotalMs:0.###}ms,max={uniquePrepareMaxMs:0.###}ms:{Path.GetFileNameWithoutExtension(uniquePrepareMaxPath ?? string.Empty)}) upload(sum={uniqueUploadTotalMs:0.###}ms,max={uniqueUploadMaxMs:0.###}ms:{Path.GetFileNameWithoutExtension(uniqueUploadMaxPath ?? string.Empty)}) shared(sum={sharedCloneTotalMs:0.###}ms,max={sharedCloneMaxMs:0.###}ms:{Path.GetFileNameWithoutExtension(sharedCloneMaxPath ?? string.Empty)}) finalize≈{Math.Max(0.0, finalizeElapsedMs):0.###}ms.");
                        if (fastOverviewPlan != null)
                        {
                            MessageHandler.Instance.AddMessage(
                                MessageType.LOG,
                                $"[Scene][FastOverviewSummary] batches={fastOverviewBatchCount}/{fastOverviewPlan.BatchGroups.Count} cacheHits={fastOverviewCacheHitCount} mergedInstances={fastOverviewMergedInstanceCount} individual={fastOverviewPlan.IndividualSpawns.Count} upload(sum={fastOverviewUploadTotalMs:0.###}ms,max={fastOverviewUploadMaxMs:0.###}ms:{Path.GetFileNameWithoutExtension(fastOverviewUploadMaxPath ?? string.Empty)})");
                        }
                        MessageHandler.Instance.AddMessage(
                            MessageType.LOG,
                            $"[Scene][PrepareTiming] mesh(sum={uniquePrepareMeshTotalMs:0.###}ms,max={uniquePrepareMeshMaxMs:0.###}ms:{Path.GetFileNameWithoutExtension(uniquePrepareMeshMaxPath ?? string.Empty)}) material(sum={uniquePrepareMaterialTotalMs:0.###}ms,max={uniquePrepareMaterialMaxMs:0.###}ms:{Path.GetFileNameWithoutExtension(uniquePrepareMaterialMaxPath ?? string.Empty)}) skeleton(sum={uniquePrepareSkeletonTotalMs:0.###}ms,max={uniquePrepareSkeletonMaxMs:0.###}ms:{Path.GetFileNameWithoutExtension(uniquePrepareSkeletonMaxPath ?? string.Empty)}).");
                        MessageHandler.Instance.AddMessage(
                            MessageType.LOG,
                            $"[Scene][PrepareDetailTiming] meshRead(sum={uniquePrepareMeshReadTotalMs:0.###}ms) meshMorph(sum={uniquePrepareMeshMorphTotalMs:0.###}ms) meshDecode(sum={uniquePrepareMeshDecodeTotalMs:0.###}ms) materialRead(sum={uniquePrepareMaterialReadTotalMs:0.###}ms) materialDeserialize(sum={uniquePrepareMaterialDeserializeTotalMs:0.###}ms) materialLegacy(sum={uniquePrepareMaterialLegacyDeserializeTotalMs:0.###}ms) materialBuild(sum={uniquePrepareMaterialBuildTotalMs:0.###}ms).");
                        MessageHandler.Instance.AddMessage(
                            MessageType.LOG,
                            $"[Scene][UploadTiming] gpuSetup(sum={uniqueUploadGpuSetupTotalMs:0.###}ms,max={uniqueUploadGpuSetupMaxMs:0.###}ms:{Path.GetFileNameWithoutExtension(uniqueUploadGpuSetupMaxPath ?? string.Empty)}) shaderWarmup(sum={uniqueUploadShaderWarmupTotalMs:0.###}ms,max={uniqueUploadShaderWarmupMaxMs:0.###}ms:{Path.GetFileNameWithoutExtension(uniqueUploadShaderWarmupMaxPath ?? string.Empty)}) textureUpload(sum={uniqueUploadTextureTotalMs:0.###}ms,max={uniqueUploadTextureMaxMs:0.###}ms:{Path.GetFileNameWithoutExtension(uniqueUploadTextureMaxPath ?? string.Empty)}).");

                        foreach (var perf in preparePerfByPath
                            .OrderByDescending(p => p.Total)
                            .Take(5))
                        {
                            MessageHandler.Instance.AddMessage(
                                MessageType.LOG,
                                $"[Scene][PrepareTop] {Path.GetFileNameWithoutExtension(perf.Path)} total={perf.Total:0.###}ms mesh={perf.Mesh:0.###}ms(read={perf.MeshRead:0.###},morph={perf.MeshMorph:0.###},decode={perf.MeshDecode:0.###}) material={perf.Material:0.###}ms(read={perf.MaterialRead:0.###},deser={perf.MaterialDeserialize:0.###},legacy={perf.MaterialLegacyDeserialize:0.###},build={perf.MaterialBuild:0.###}) skeleton={perf.Skeleton:0.###}ms");
                        }

                        foreach (var batchGroup in loadedSceneModelInstances
                        .Where(x => x.Model != null && !string.IsNullOrWhiteSpace(x.SourcePath))
                        .GroupBy(x => x.SourcePath, StringComparer.OrdinalIgnoreCase)
                        .Select(g =>
                        {
                            int totalOpaqueDrawCalls = 0;
                            long totalOpaqueTriangles = 0;
                            int trinsInstances = 0;
                            foreach (var instance in g)
                            {
                                if (instance.IsTrinsInstance)
                                {
                                    trinsInstances++;
                                }

                                var contribution = instance.Model.GetOpaqueGeometryContribution();
                                totalOpaqueDrawCalls += contribution.DrawCalls;
                                totalOpaqueTriangles += contribution.Triangles;
                            }

                            return new
                            {
                                SourcePath = g.Key,
                                Count = g.Count(),
                                TrinsCount = trinsInstances,
                                OpaqueDrawCalls = totalOpaqueDrawCalls,
                                OpaqueTriangles = totalOpaqueTriangles
                            };
                        })
                        .Where(x => x.Count > 1)
                        .OrderByDescending(x => x.OpaqueDrawCalls)
                        .ThenByDescending(x => x.Count)
                        .Take(10))
                        {
                            MessageHandler.Instance.AddMessage(
                                MessageType.LOG,
                                $"[Scene][BatchCandidate] {Path.GetFileNameWithoutExtension(batchGroup.SourcePath)} count={batchGroup.Count} trins={batchGroup.TrinsCount} opaqueDC={batchGroup.OpaqueDrawCalls} opaqueTri={batchGroup.OpaqueTriangles}");
                        }

                        foreach (var batchReject in loadedSceneModelInstances
                        .Where(x => x.Model != null &&
                                    !x.IsMergedBatch &&
                                    !string.IsNullOrWhiteSpace(x.SourcePath))
                        .GroupBy(x => x.SourcePath, StringComparer.OrdinalIgnoreCase)
                        .Select(g =>
                        {
                            int totalOpaqueDrawCalls = 0;
                            long totalOpaqueTriangles = 0;
                            int trinsInstances = 0;
                            foreach (var instance in g)
                            {
                                if (instance.IsTrinsInstance)
                                {
                                    trinsInstances++;
                                }

                                var contribution = instance.Model.GetOpaqueGeometryContribution();
                                totalOpaqueDrawCalls += contribution.DrawCalls;
                                totalOpaqueTriangles += contribution.Triangles;
                            }

                            string reason = GetMergedStaticBatchGroupRejectReason(g);
                            return new
                            {
                                SourcePath = g.Key,
                                Count = g.Count(),
                                TrinsCount = trinsInstances,
                                OpaqueDrawCalls = totalOpaqueDrawCalls,
                                OpaqueTriangles = totalOpaqueTriangles,
                                Reason = reason
                            };
                        })
                        .Where(x => x.Count > 1 && !string.IsNullOrWhiteSpace(x.Reason))
                        .OrderByDescending(x => x.OpaqueDrawCalls)
                        .ThenByDescending(x => x.Count)
                        .Take(10))
                        {
                            MessageHandler.Instance.AddMessage(
                                MessageType.LOG,
                                $"[Scene][BatchRejected] {Path.GetFileNameWithoutExtension(batchReject.SourcePath)} count={batchReject.Count} trins={batchReject.TrinsCount} opaqueDC={batchReject.OpaqueDrawCalls} opaqueTri={batchReject.OpaqueTriangles} reason={batchReject.Reason}");
                        }
                    }
	            }
	            catch (OperationCanceledException)
	            {
                    loadStopwatch.Stop();
	                EndSceneLoadUi(loadVersion, "Load canceled.");
	                MessageHandler.Instance.AddMessage(MessageType.LOG, $"[Scene] Load canceled after {loadStopwatch.Elapsed.TotalSeconds:0.###}s.");
            }
            catch (Exception ex)
            {
                loadStopwatch.Stop();
                EndSceneLoadUi(loadVersion, "Load failed.");
                MessageHandler.Instance.AddMessage(MessageType.ERROR, $"[Scene] Load failed after {loadStopwatch.Elapsed.TotalSeconds:0.###}s: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                if (sceneLoadVersion == loadVersion)
                {
                    isSceneLoading = false;
                    renderCtrl.Invalidate();
                }

                try { myCts.Dispose(); } catch { }
                if (ReferenceEquals(sceneLoadCts, myCts))
                {
                    sceneLoadCts = null;
                }
            }
        }

        private async Task<FastOverviewBatchLoadResult> TryLoadFastOverviewBatchAsync(
            FastOverviewBatchGroup group,
            Dictionary<string, Task<PreparedSceneModel>> prepareByResolvedPath,
            List<Vector3> spawnedPositions,
            int loadVersion,
            int completedBeforeBatch,
            int total,
            bool diskSceneCache,
            bool loadAllLods,
            CancellationToken token,
            Action<PreparedSceneModel> recordPrepared)
        {
            if (renderCtrl?.renderer == null ||
                group.Spawns.Count == 0 ||
                !prepareByResolvedPath.TryGetValue(group.ResolvedPath, out var preparedTask))
            {
                return new FastOverviewBatchLoadResult { Applied = false };
            }

            PreparedSceneModel prepared;
            try
            {
                prepared = await preparedTask;
                token.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                MessageHandler.Instance.AddMessage(
                    MessageType.WARNING,
                    $"[Scene][FastOverview] Failed to prepare batch source '{group.ResolvedPath}': {ex.GetType().Name}: {ex.Message}");
                return new FastOverviewBatchLoadResult { Applied = false };
            }

            recordPrepared(prepared);
            var rejectReason = prepared.Model.GetMergedStaticBatchRejectReason();
            if (rejectReason != null)
            {
                if (SceneDiagnosticsEnabled)
                {
                    MessageHandler.Instance.AddMessage(
                        MessageType.LOG,
                        $"[Scene][FastOverviewReject] {Path.GetFileNameWithoutExtension(prepared.ResolvedPath)} count={group.Spawns.Count} reason={rejectReason}");
                }

                return new FastOverviewBatchLoadResult { Applied = false };
            }

            var transforms = new List<Matrix4>(group.Spawns.Count);
            var positions = new List<Vector3>(group.Spawns.Count);
            foreach (var spawn in group.Spawns)
            {
                token.ThrowIfCancellationRequested();
                var attachMat = ApplyAttachTransformOverride(
                    spawn.ModelMatrix,
                    spawn.LocalMatrix,
                    spawn.ParentSceneObjectName,
                    spawn.ParentSceneObjectWorldMatrix,
                    spawn.AttachTransformEnable,
                    spawn.AttachJointName,
                    spawn.KeepWorldSrt);
                var mat = GetFinalSceneModelMatrix(attachMat, spawn.IsTrinsInstance, spawn.SceneObjectName, spawn.ModelPath, out var position, out _);
                transforms.Add(mat);
                positions.Add(position);
            }

            string mergedName = $"{Path.GetFileNameWithoutExtension(prepared.ResolvedPath)}__fast_overview";
            Model merged;
            bool persistentCacheHit = false;
            try
            {
                string? mergedCachePath = null;
                if (diskSceneCache)
                {
                    var cacheKey = SceneDiskCache.BuildMergedBatchCacheKey(prepared.ResolvedPath, transforms, loadAllLods);
                    mergedCachePath = SceneDiskCache.GetWritableCacheFilePath(
                        config.SceneDiskCacheDirectory,
                        cacheKey,
                        SceneDiskCache.MergedBatchCacheExtension);
                    if (SceneDiagnosticsEnabled)
                    {
                        MessageHandler.Instance.AddMessage(
                            MessageType.LOG,
                            $"[SceneCache] merged key={cacheKey[..Math.Min(16, cacheKey.Length)]} model={Path.GetFileNameWithoutExtension(prepared.ResolvedPath)} instances={transforms.Count}");
                    }
                }

                merged = prepared.Model.CreateMergedStaticSceneInstance(
                    transforms,
                    mergedName,
                    mergedCachePath,
                    out persistentCacheHit);
            }
            catch (Exception ex)
            {
                MessageHandler.Instance.AddMessage(
                    MessageType.WARNING,
                    $"[Scene][FastOverview] Failed to build merged model '{mergedName}': {ex.GetType().Name}: {ex.Message}");
                return new FastOverviewBatchLoadResult { Applied = false };
            }

            var progress = new Progress<float>(p =>
            {
                int percent = ComputeOverallPercent(total, completedBeforeBatch, p);
                ReportSceneLoadUi(loadVersion, percent, $"Fast batch {completedBeforeBatch + group.Spawns.Count}/{total}: {Path.GetFileNameWithoutExtension(prepared.ResolvedPath)}");
            });

            var uploadStopwatch = Stopwatch.StartNew();
            try
            {
                merged = await renderCtrl.renderer.AddPreparedSceneModelAsync(merged, token: token, progress: progress);
                token.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                MessageHandler.Instance.AddMessage(
                    MessageType.WARNING,
                    $"[Scene][FastOverview] Failed to upload merged model '{mergedName}': {ex.GetType().Name}: {ex.Message}");
                return new FastOverviewBatchLoadResult { Applied = false };
            }
            finally
            {
                uploadStopwatch.Stop();
            }

            foreach (var position in positions)
            {
                spawnedPositions.Add(position);
            }

            AddModelToList(mergedName, prepared.ResolvedPath, merged);
            loadedSceneModelInstances.Add(new LoadedSceneModelInstance
            {
                Name = mergedName,
                SourcePath = prepared.ResolvedPath,
                BaseTransform = Matrix4.Identity,
                LocalTransform = Matrix4.Identity,
                IsTrinsInstance = group.Spawns.All(x => x.IsTrinsInstance),
                IsMergedBatch = true,
                FastOverviewBatch = true,
                MergedInstanceCount = group.Spawns.Count,
                Transform = Matrix4.Identity,
                Model = merged
            });

            if (SceneDiagnosticsEnabled)
            {
                MessageHandler.Instance.AddMessage(
                    MessageType.LOG,
                    $"[Scene][FastOverviewApplied] {Path.GetFileNameWithoutExtension(prepared.ResolvedPath)} merged={group.Spawns.Count} trins={group.Spawns.Count(x => x.IsTrinsInstance)} cache={(persistentCacheHit ? "hit" : "miss")}");
            }

            return new FastOverviewBatchLoadResult
            {
                Applied = true,
                PersistentCacheHit = persistentCacheHit,
                CompletedSpawns = group.Spawns.Count,
                MergedInstanceCount = group.Spawns.Count,
                UploadElapsedMs = uploadStopwatch.Elapsed.TotalMilliseconds,
                SourcePath = prepared.ResolvedPath
            };
        }

        private async Task TryBatchRepeatedStaticModelsAsync(
            int loadVersion,
            bool diskSceneCache,
            bool loadAllLods,
            CancellationToken token)
        {
            if (renderCtrl?.renderer == null)
            {
                return;
            }

            var groups = loadedSceneModelInstances
                .Where(x => IsEligibleForMergedStaticBatch(x))
                .GroupBy(x => x.SourcePath, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() >= MergedStaticBatchMinCount)
                .OrderByDescending(g => g.Count())
                .ToList();

            foreach (var group in groups)
            {
                token.ThrowIfCancellationRequested();

                var instances = group.ToList();
                var template = instances[0].Model;
                if (template == null)
                {
                    continue;
                }

                string sourcePath = group.Key;
                string mergedName = $"{Path.GetFileNameWithoutExtension(sourcePath)}__merged";
                Model merged;
                try
                {
                    var transforms = instances.Select(x => x.Transform).ToList();
                    string? mergedCachePath = null;
                    if (diskSceneCache)
                    {
                        var cacheKey = SceneDiskCache.BuildMergedBatchCacheKey(sourcePath, transforms, loadAllLods);
                        mergedCachePath = SceneDiskCache.GetWritableCacheFilePath(
                            config.SceneDiskCacheDirectory,
                            cacheKey,
                            SceneDiskCache.MergedBatchCacheExtension);
                    }

                    merged = template.CreateMergedStaticSceneInstance(
                        transforms,
                        mergedName,
                        mergedCachePath,
                        out bool persistentCacheHit);
                    if (SceneDiagnosticsEnabled)
                    {
                        MessageHandler.Instance.AddMessage(
                            MessageType.LOG,
                            $"[SceneCache] post-load merged model={Path.GetFileNameWithoutExtension(sourcePath)} instances={transforms.Count} cache={(persistentCacheHit ? "hit" : "miss")}");
                    }
                }
                catch (Exception ex)
                {
                    MessageHandler.Instance.AddMessage(
                        MessageType.WARNING,
                        $"[Scene][Batch] Failed to build merged model '{mergedName}': {ex.GetType().Name}: {ex.Message}");
                    continue;
                }

                try
                {
                    await renderCtrl.renderer.AddPreparedSceneModelAsync(merged, token: token);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    MessageHandler.Instance.AddMessage(
                        MessageType.WARNING,
                        $"[Scene][Batch] Failed to upload merged model '{mergedName}': {ex.GetType().Name}: {ex.Message}");
                    continue;
                }

                for (int i = 0; i < instances.Count; i++)
                {
                    instances[i].Model.SetVisible(false);
                }

                loadedSceneModelInstances.Add(new LoadedSceneModelInstance
                {
                    Name = mergedName,
                    SourcePath = sourcePath,
                    BaseTransform = Matrix4.Identity,
                    IsTrinsInstance = instances.All(x => x.IsTrinsInstance),
                    IsMergedBatch = true,
                    MergedInstanceCount = instances.Count,
                    Transform = Matrix4.Identity,
                    Model = merged
                });

                if (SceneDiagnosticsEnabled)
                {
                    MessageHandler.Instance.AddMessage(
                        MessageType.LOG,
                        $"[Scene][BatchApplied] {Path.GetFileNameWithoutExtension(sourcePath)} merged={instances.Count} trins={instances.Count(x => x.IsTrinsInstance)} opaqueDC={instances.Sum(x => x.Model.GetOpaqueGeometryContribution().DrawCalls)}");
                }
            }
        }

        private static bool IsEligibleForMergedStaticBatch(LoadedSceneModelInstance instance)
        {
            if (instance.Model == null ||
                instance.IsMergedBatch ||
                string.IsNullOrWhiteSpace(instance.SourcePath))
            {
                return false;
            }

            return instance.Model.IsEligibleForMergedStaticBatch();
        }

        private static string GetMergedStaticBatchGroupRejectReason(IGrouping<string, LoadedSceneModelInstance> group)
        {
            if (group.Count() < MergedStaticBatchMinCount)
            {
                return "threshold";
            }

            foreach (var instance in group)
            {
                var reason = GetMergedStaticBatchRejectReason(instance);
                if (reason != null)
                {
                    return reason;
                }
            }

            return string.Empty;
        }

        private static string? GetMergedStaticBatchRejectReason(LoadedSceneModelInstance instance)
        {
            if (instance.Model == null)
            {
                return "no-model";
            }

            if (instance.IsMergedBatch)
            {
                return "already-merged";
            }

            if (string.IsNullOrWhiteSpace(instance.SourcePath))
            {
                return "no-source";
            }

            return instance.Model.GetMergedStaticBatchRejectReason();
        }

        private sealed class PreparedSceneModel
        {
            public required string ResolvedPath { get; init; }
            public required Model Model { get; init; }
            public bool PersistentMeshCacheHit { get; init; }
            public double PrepareElapsedMs { get; init; }
            public double PrepareMeshMs { get; init; }
            public double PrepareMeshReadMs { get; init; }
            public double PrepareMeshMorphMs { get; init; }
            public double PrepareMeshDecodeMs { get; init; }
            public double PrepareMaterialMs { get; init; }
            public double PrepareMaterialReadMs { get; init; }
            public double PrepareMaterialDeserializeMs { get; init; }
            public double PrepareMaterialLegacyDeserializeMs { get; init; }
            public double PrepareMaterialBuildMs { get; init; }
            public double PrepareSkeletonMs { get; init; }
        }

        private static async Task<PreparedSceneModel> PrepareSceneModelAsync(
            string resolvedPath,
            bool loadAllLods,
            string? persistentMeshCachePath,
            SemaphoreSlim buildSemaphore,
            CancellationToken token)
        {
            await buildSemaphore.WaitAsync(token);
            try
            {
                return await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    var stopwatch = Stopwatch.StartNew();
                    var assetProvider = new InMemoryOverrideAssetProvider(new DiskAssetProvider());
                    Model model;
                    bool persistentMeshCacheHit;
                    if (!string.IsNullOrWhiteSpace(persistentMeshCachePath))
                    {
                        model = Model.CreateWithPersistentMeshCache(
                            assetProvider,
                            resolvedPath,
                            loadAllLods,
                            enableCpuMorphRegistration: false,
                            persistentMeshCachePath,
                            out persistentMeshCacheHit);
                    }
                    else
                    {
                        model = new Model(assetProvider, resolvedPath, loadAllLods, enableCpuMorphRegistration: false);
                        persistentMeshCacheHit = false;
                    }
                    stopwatch.Stop();
                    var preparePerf = model.LastPreparePerfStats;
                    return new PreparedSceneModel
                    {
                        ResolvedPath = resolvedPath,
                        Model = model,
                        PersistentMeshCacheHit = persistentMeshCacheHit,
                        PrepareElapsedMs = stopwatch.Elapsed.TotalMilliseconds,
                        PrepareMeshMs = preparePerf.MeshMs,
                        PrepareMeshReadMs = preparePerf.MeshReadMs,
                        PrepareMeshMorphMs = preparePerf.MeshMorphMs,
                        PrepareMeshDecodeMs = preparePerf.MeshDecodeMs,
                        PrepareMaterialMs = preparePerf.MaterialMs,
                        PrepareMaterialReadMs = preparePerf.MaterialReadMs,
                        PrepareMaterialDeserializeMs = preparePerf.MaterialDeserializeMs,
                        PrepareMaterialLegacyDeserializeMs = preparePerf.MaterialLegacyDeserializeMs,
                        PrepareMaterialBuildMs = preparePerf.MaterialBuildMs,
                        PrepareSkeletonMs = preparePerf.SkeletonMs
                    };
                }, token);
            }
            finally
            {
                buildSemaphore.Release();
            }
        }

        private void CancelSceneLoad()
        {
            try
            {
                sceneLoadCts?.Cancel();
            }
            catch
            {
                // ignore
            }
        }

        private void BeginSceneLoadUi(int loadVersion, string message)
        {
            if (InvokeRequired)
            {
                BeginInvoke((Action)(() => BeginSceneLoadUi(loadVersion, message)));
                return;
            }

            if (sceneLoadVersion != loadVersion)
            {
                return;
            }

            loadingProgressBar.Value = 0;
            loadingProgressBar.Visible = true;
            statusLbl.Text = message;
        }

        private void ReportSceneLoadUi(int loadVersion, int percent, string message)
        {
            if (InvokeRequired)
            {
                BeginInvoke((Action)(() => ReportSceneLoadUi(loadVersion, percent, message)));
                return;
            }

            if (sceneLoadVersion != loadVersion)
            {
                return;
            }

            loadingProgressBar.Value = Math.Clamp(percent, 0, 100);
            statusLbl.Text = message;
        }

        private void EndSceneLoadUi(int loadVersion, string message)
        {
            if (InvokeRequired)
            {
                BeginInvoke((Action)(() => EndSceneLoadUi(loadVersion, message)));
                return;
            }

            if (sceneLoadVersion != loadVersion)
            {
                return;
            }

            loadingProgressBar.Value = 100;
            statusLbl.Text = message;
            loadingProgressBar.Visible = false;
        }

        private static int ComputeOverallPercent(int total, int completed, float currentModelProgress)
        {
            if (total <= 0)
            {
                return 0;
            }

            float scanPortion = 0.05f;
            float loadPortion = 1.0f - scanPortion;

            float doneModels = Math.Clamp(completed, 0, total);
            float modelP = Math.Clamp(currentModelProgress, 0f, 1f);
            float overall = scanPortion + loadPortion * ((doneModels + modelP) / total);
            return (int)Math.Round(overall * 100.0f);
        }

        private void ClearModelsList()
        {
            suppressModelListEvents = true;
            try
            {
                modelsListView.Items.Clear();
            }
            finally
            {
                suppressModelListEvents = false;
            }
        }

        private void ApplySceneClipPlanes(Vector3 center, float radius)
        {
            if (renderCtrl?.renderer == null)
            {
                return;
            }

            if (!config.LargeClipPlanes)
            {
                renderCtrl.renderer.SetCameraClipPlanes(0.1f, 100.0f);
                return;
            }

            // Keep far clip generous for large-world Trinity scenes.
            var far = MathF.Max(10_000.0f, radius * 200.0f);
            renderCtrl.renderer.SetCameraClipPlanes(0.1f, far);
        }

        private void AddModelToList(string? sceneObjectName, string modelPath, Model model)
        {
            string name = string.IsNullOrWhiteSpace(sceneObjectName)
                ? Path.GetFileNameWithoutExtension(modelPath)
                : sceneObjectName;

            var item = new ListViewItem(name)
            {
                Checked = model.IsVisible,
                Tag = model
            };
            item.SubItems.Add(modelPath);

            suppressModelListEvents = true;
            try
            {
                modelsListView.Items.Add(item);
            }
            finally
            {
                suppressModelListEvents = false;
            }
        }

        private void modelsListView_ItemChecked(object sender, ItemCheckedEventArgs e)
        {
            if (suppressModelListEvents)
            {
                return;
            }

            if (e.Item?.Tag is Model model)
            {
                model.SetVisible(e.Item.Checked);
                renderCtrl.Invalidate();
            }
        }

        private static Matrix4 ApplyViewerMatrixOptions(Matrix4 baseMat, bool forceOrigin, bool rotate180x, bool rotate180y, out Vector3 position, out Vector3 scaleOut)
        {
            var mat = baseMat;

            if (forceOrigin)
            {
                mat.M41 = 0f;
                mat.M42 = 0f;
                mat.M43 = 0f;
            }
            position = new Vector3(mat.M41, mat.M42, mat.M43);

            scaleOut = new Vector3(
                new Vector3(mat.M11, mat.M12, mat.M13).Length,
                new Vector3(mat.M21, mat.M22, mat.M23).Length,
                new Vector3(mat.M31, mat.M32, mat.M33).Length);

            if (!rotate180x)
            {
                if (!rotate180y)
                {
                    return mat;
                }
            }

            if (rotate180y)
            {
                mat = Matrix4.CreateRotationY(MathHelper.Pi) * mat;
            }

            if (rotate180x)
            {
                mat = Matrix4.CreateRotationX(MathHelper.Pi) * mat;
            }

            return mat;
        }

        private Matrix4 GetFinalSceneModelMatrix(Matrix4 baseMat, bool isTrinsInstance, string? sceneObjectName, string? modelPath, out Vector3 position, out Vector3 scale)
        {
            var sceneBaseMat = baseMat;
            var bakedRootCorrected = false;
            if (!isTrinsInstance)
            {
                sceneBaseMat = SceneTransformMath.ApplyZaWorldBakedFieldRootTransform(baseMat, modelPath);
                bakedRootCorrected = sceneBaseMat != baseMat;
            }

            var mat = isTrinsInstance
                ? ApplyTrinsDebugTransform(baseMat)
                : ApplySceneDebugTransform(sceneBaseMat);
            mat = ApplyObjectDebugTransform(mat, sceneObjectName, modelPath);
            var final = ApplyViewerMatrixOptions(mat, config.SpawnModelsAtOrigin, config.RotateModels180X, config.RotateModels180Y, out position, out scale);

            if (SceneDiagnosticsMatchesTarget(sceneObjectName, modelPath))
            {
                MessageHandler.Instance.AddMessage(
                    MessageType.LOG,
                    $"[Scene][TargetMatrix] obj='{sceneObjectName ?? ""}' model='{modelPath ?? ""}' trins={isTrinsInstance} bakedRootCorrected={bakedRootCorrected} basePos=({baseMat.M41:0.###}, {baseMat.M42:0.###}, {baseMat.M43:0.###}) sceneBasePos=({sceneBaseMat.M41:0.###}, {sceneBaseMat.M42:0.###}, {sceneBaseMat.M43:0.###}) finalPos=({final.M41:0.###}, {final.M42:0.###}, {final.M43:0.###})");
            }

            return final;
        }

        private Matrix4 ApplySceneDebugTransform(Matrix4 baseMat)
        {
            var corrected = config.ApplyLegacySceneCorrection
                ? ApplyDebugTransform(
                    baseMat,
                    0f,
                    0f,
                    0f,
                    180f,
                    -35.5f,
                    0f,
                    0f,
                    0f,
                    0f)
                : baseMat;

            return ApplyDebugTransform(
                corrected,
                config.SceneDebugRotateX,
                config.SceneDebugRotateY,
                config.SceneDebugRotateZ,
                config.SceneDebugGlobalRotateX,
                config.SceneDebugGlobalRotateY,
                config.SceneDebugGlobalRotateZ,
                config.SceneDebugTranslateX,
                config.SceneDebugTranslateY,
                config.SceneDebugTranslateZ);
        }

        private Matrix4 ApplyAttachTransformOverride(
            Matrix4 currentWorldMatrix,
            Matrix4 localMatrix,
            string? parentSceneObjectName,
            Matrix4? parentSceneObjectWorldMatrix,
            bool attachTransformEnable,
            string? attachJointName,
            bool keepWorldSrt)
        {
            if (!attachTransformEnable || keepWorldSrt)
            {
                return currentWorldMatrix;
            }

            if (string.IsNullOrWhiteSpace(parentSceneObjectName))
            {
                if (parentSceneObjectWorldMatrix.HasValue)
                {
                    return localMatrix * parentSceneObjectWorldMatrix.Value;
                }

                return currentWorldMatrix;
            }

            LoadedSceneModelInstance? parentInstance = null;
            foreach (var instance in loadedSceneModelInstances)
            {
                if (instance.IsMergedBatch)
                {
                    continue;
                }

                if (string.Equals(instance.Name, parentSceneObjectName, StringComparison.Ordinal))
                {
                    parentInstance = instance;
                    break;
                }
            }

            if (parentInstance == null)
            {
                if (parentSceneObjectWorldMatrix.HasValue)
                {
                    return localMatrix * parentSceneObjectWorldMatrix.Value;
                }

                return currentWorldMatrix;
            }

            if (string.IsNullOrWhiteSpace(attachJointName))
            {
                return localMatrix * parentInstance.BaseTransform;
            }

            var armature = parentInstance.Model.Armature;
            if (armature == null || !armature.TryGetRestWorldMatrix(attachJointName, out var jointModelMatrix))
            {
                return localMatrix * parentInstance.Transform;
            }

            return localMatrix * jointModelMatrix * parentInstance.BaseTransform;
        }

        private Matrix4 ApplyTrinsDebugTransform(Matrix4 baseMat)
        {
            var corrected = SceneTransformMath.ApplyTrinsInstanceDefaultTransform(baseMat);

            return ApplyDebugTransform(
                corrected,
                config.TrinsDebugRotateX,
                config.TrinsDebugRotateY,
                config.TrinsDebugRotateZ,
                config.TrinsDebugGlobalRotateX,
                config.TrinsDebugGlobalRotateY,
                config.TrinsDebugGlobalRotateZ,
                config.TrinsDebugTranslateX,
                config.TrinsDebugTranslateY,
                config.TrinsDebugTranslateZ);
        }

        private Matrix4 ApplyObjectDebugTransform(Matrix4 baseMat, string? sceneObjectName, string? modelPath)
        {
            if (!ObjectDebugMatchesTarget(sceneObjectName, modelPath))
            {
                return baseMat;
            }

            return ApplyDebugTransform(
                baseMat,
                config.ObjectDebugRotateX,
                config.ObjectDebugRotateY,
                config.ObjectDebugRotateZ,
                config.ObjectDebugGlobalRotateX,
                config.ObjectDebugGlobalRotateY,
                config.ObjectDebugGlobalRotateZ,
                config.ObjectDebugTranslateX,
                config.ObjectDebugTranslateY,
                config.ObjectDebugTranslateZ);
        }

        private static Matrix4 ApplyDebugTransform(
            Matrix4 baseMat,
            float localRotateX,
            float localRotateY,
            float localRotateZ,
            float globalRotateX,
            float globalRotateY,
            float globalRotateZ,
            float translateX,
            float translateY,
            float translateZ)
        {
            bool hasLocalRotation = localRotateX != 0f || localRotateY != 0f || localRotateZ != 0f;
            bool hasGlobalRotation = globalRotateX != 0f || globalRotateY != 0f || globalRotateZ != 0f;
            bool hasTranslation = translateX != 0f || translateY != 0f || translateZ != 0f;

            if (!hasLocalRotation && !hasGlobalRotation && !hasTranslation)
            {
                return baseMat;
            }

            var localRotation =
                Matrix4.CreateRotationZ(MathHelper.DegreesToRadians(localRotateZ)) *
                Matrix4.CreateRotationY(MathHelper.DegreesToRadians(localRotateY)) *
                Matrix4.CreateRotationX(MathHelper.DegreesToRadians(localRotateX));

            var globalRotation =
                Matrix4.CreateRotationZ(MathHelper.DegreesToRadians(globalRotateZ)) *
                Matrix4.CreateRotationY(MathHelper.DegreesToRadians(globalRotateY)) *
                Matrix4.CreateRotationX(MathHelper.DegreesToRadians(globalRotateX));

            var translation = Matrix4.CreateTranslation(
                translateX,
                translateY,
                translateZ);

            var mat = baseMat;
            if (hasLocalRotation)
            {
                mat = localRotation * mat;
            }

            if (hasGlobalRotation)
            {
                mat *= globalRotation;
            }

            if (hasTranslation)
            {
                mat *= translation;
            }

            return mat;
        }

        private void ReapplyLoadedSceneModelTransforms()
        {
            if (renderCtrl?.renderer == null || loadedSceneModelInstances.Count == 0)
            {
                return;
            }

            var spawnedPositions = new List<Vector3>(loadedSceneModelInstances.Count);
            foreach (var instance in loadedSceneModelInstances)
            {
                if (instance.IsMergedBatch)
                {
                    var mergedPosition = new Vector3(instance.Transform.M41, instance.Transform.M42, instance.Transform.M43);
                    spawnedPositions.Add(mergedPosition);
                    continue;
                }

                var attachMat = ApplyAttachTransformOverride(
                    instance.BaseTransform,
                    instance.LocalTransform,
                    instance.ParentSceneObjectName,
                    instance.ParentSceneObjectWorldMatrix,
                    instance.AttachTransformEnable,
                    instance.AttachJointName,
                    instance.KeepWorldSrt);
                var mat = GetFinalSceneModelMatrix(attachMat, instance.IsTrinsInstance, instance.Name, instance.SourcePath, out var position, out _);
                position = new Vector3(mat.M41, mat.M42, mat.M43);
                instance.Model.SetModelMatrix(mat);
                spawnedPositions.Add(position);
            }

            if (spawnedPositions.Count > 0)
            {
                var min = new Vector3(float.PositiveInfinity);
                var max = new Vector3(float.NegativeInfinity);
                foreach (var p in spawnedPositions)
                {
                    min = Vector3.ComponentMin(min, p);
                    max = Vector3.ComponentMax(max, p);
                }

                var center = (min + max) * 0.5f;
                var radius = (max - min).Length * 0.5f;
                ApplySceneClipPlanes(center, radius);
            }

            renderCtrl.Invalidate();
        }

        private string? ResolveModelPath(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(assetRoot))
            {
                return null;
            }

            // Allow absolute paths.
            if (Path.IsPathRooted(filePath))
            {
                if (File.Exists(filePath))
                {
                    return filePath;
                }

                // Some scene files embed absolute authoring paths. If the file isn't present, try to
                // re-root under the configured asset root by stripping to a known content folder.
                var rerooted = TryRerootUnderAssetRoot(filePath, assetRoot);
                if (rerooted != null)
                {
                    return rerooted;
                }
            }

            var normalized = filePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            var combined = Path.GetFullPath(Path.Combine(assetRoot, normalized));
            if (File.Exists(combined))
            {
                return combined;
            }

            // Be forgiving if the configured asset root is a subfolder of the real dump root.
            // Try a few parents so references like "ik_chara/..." can still resolve.
            try
            {
                var cur = assetRoot;
                for (int i = 0; i < 3; i++)
                {
                    var parent = Directory.GetParent(cur);
                    if (parent == null)
                    {
                        break;
                    }

                    cur = parent.FullName;
                    var parentCombined = Path.GetFullPath(Path.Combine(cur, normalized));
                    if (File.Exists(parentCombined))
                    {
                        return parentCombined;
                    }
                }
            }
            catch
            {
                // ignore
            }

            return null;
        }

        private static string? TryRerootUnderAssetRoot(string rootedPath, string assetRoot)
        {
            if (string.IsNullOrWhiteSpace(rootedPath) || string.IsNullOrWhiteSpace(assetRoot))
            {
                return null;
            }

            string normalized = rootedPath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            // Prefer earlier matches (closest to drive root).
            string[] knownRoots =
            {
                "ai_influence",
                "avalon",
                "field_graphic",
                "field",
                "ik_ai_behavior",
                "ik_chara",
                "ik_demo",
                "ik_effect",
                "ik_event",
                "ik_message",
                "ik_pokemon",
                "light",
                "param_ai",
                "param_chr",
                "script",
                "system",
                "system_resource",
                "world",
                "common",
                "legend",
                "model",
                "effect",
                "ui"
            };

            foreach (var root in knownRoots)
            {
                string needle = Path.DirectorySeparatorChar + root + Path.DirectorySeparatorChar;
                int idx = normalized.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
                if (idx < 0)
                {
                    continue;
                }

                // Keep the root folder name itself (e.g. "field\...").
                string relative = normalized.Substring(idx + 1);
                string candidate = Path.GetFullPath(Path.Combine(assetRoot, relative));
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        private string? ResolveSceneReference(string sceneFile, string referencedPath)
        {
            return SceneReferencePlanner.ResolveSceneReference(sceneFile, referencedPath, preferredSceneVariant);
        }

        private static int? TryDetectVariantFromPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            string name = Path.GetFileNameWithoutExtension(path);
            if (name.EndsWith("_0", StringComparison.OrdinalIgnoreCase)) return 0;
            if (name.EndsWith("_1", StringComparison.OrdinalIgnoreCase)) return 1;
            return null;
        }

        private static Quaternion ExtractNormalizedRotation(Matrix4 matrix)
        {
            var rotation = matrix.ExtractRotation();
            if (rotation.LengthSquared <= 0f)
            {
                return Quaternion.Identity;
            }

            rotation.Normalize();
            return rotation;
        }
    }
}
