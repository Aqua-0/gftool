using GFTool.Renderer.Core.Graphics;
using GFTool.Renderer.Scene.GraphicsObjects;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Trinity.Core.Assets;

namespace GFTool.Renderer
{
    public partial class RenderContext
    {
        public async Task<Model> AddSceneModelAsync(string file, bool loadAllLods = false, CancellationToken token = default, IProgress<float>? progress = null)
        {
            return await AddSceneModelAsync(new InMemoryOverrideAssetProvider(new DiskAssetProvider()), file, loadAllLods, token, progress);
        }

        public async Task<Model> AddSceneModelAsync(IAssetProvider assetProvider, string file, bool loadAllLods = false, CancellationToken token = default, IProgress<float>? progress = null)
        {
            if (assetProvider == null) throw new ArgumentNullException(nameof(assetProvider));
            if (string.IsNullOrWhiteSpace(file)) throw new ArgumentException("Missing model path.", nameof(file));

            progress?.Report(0.05f);

            var model = await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                return new Model(assetProvider, file, loadAllLods);
            }, token);

            progress?.Report(0.30f);

            return await AddPreparedSceneModelAsync(model, token, progress);
        }

        public async Task<Model> AddPreparedSceneModelAsync(Model model, CancellationToken token = default, IProgress<float>? progress = null)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));

            AddSceneModelDeferred(model);

            progress?.Report(0.35f);
            try
            {
                await LoadModelResourcesAsync(model, token, progress);
            }
            catch
            {
                try { RemoveSceneModel(model); } catch { }
                throw;
            }

            progress?.Report(1.0f);
            return model;
        }

        public Task LoadModelResourcesAsync(Model model, CancellationToken token = default, IProgress<float>? progress = null)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));

            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            EnqueueGlWork(new ModelResourceLoadWorkItem(model, token, progress, tcs));
            return tcs.Task;
        }

        private sealed class ModelResourceLoadWorkItem : IGlWorkItem
        {
            private enum Stage
            {
                GpuSetup = 0,
                ShaderWarmup = 1,
                TextureUpload = 2,
                Done = 3
            }

            private readonly Model model;
            private readonly CancellationToken token;
            private readonly IProgress<float>? progress;
            private readonly TaskCompletionSource tcs;
            private Stage stage;

            private readonly string[] shaderNames;
            private int shaderIndex;

            private readonly Texture[] textures;
            private int textureScanStart;
            private int texturesComplete;
            private readonly Stopwatch stopwatch = Stopwatch.StartNew();
            private double gpuSetupMs;
            private double shaderWarmupMs;
            private double textureUploadMs;
            private int gpuSetupSteps;
            private int shaderWarmupSteps;
            private int textureUploadSteps;

            public ModelResourceLoadWorkItem(Model model, CancellationToken token, IProgress<float>? progress, TaskCompletionSource tcs)
            {
                this.model = model;
                this.token = token;
                this.progress = progress;
                this.tcs = tcs;
                stage = Stage.GpuSetup;

                shaderNames = model
                    .GetMaterials()
                    .Select(m => m?.ShaderName)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                textures = model
                    .GetMaterials()
                    .SelectMany(m => m?.Textures ?? Array.Empty<Texture>())
                    .Where(t => t != null)
                    .Select(t => t!)
                    .ToArray();

                if (textures.Length > 1)
                {
                    var unique = new List<Texture>(textures.Length);
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < textures.Length; i++)
                    {
                        if (seen.Add(textures[i].CacheKey))
                        {
                            unique.Add(textures[i]);
                        }
                    }
                    textures = unique.ToArray();
                }

                for (int i = 0; i < textures.Length; i++)
                {
                    textures[i].BeginAsyncLoadIfEnabled();
                }
            }

            public bool Step()
            {
                if (tcs.Task.IsCompleted)
                {
                    return true;
                }

                if (token.IsCancellationRequested)
                {
                    tcs.TrySetCanceled(token);
                    return true;
                }

                try
                {
                    switch (stage)
                    {
                        case Stage.GpuSetup:
                            progress?.Report(0.35f);
                            if (MeasureGpuSetupStep())
                            {
                                stage = Stage.ShaderWarmup;
                            }
                            return false;

                        case Stage.ShaderWarmup:
                            progress?.Report(0.60f);
                            if (shaderIndex < shaderNames.Length)
                            {
                                MeasureShaderWarmupStep(shaderNames[shaderIndex]);
                                shaderIndex++;
                                return false;
                            }
                            stage = Stage.TextureUpload;
                            return false;

                        case Stage.TextureUpload:
                            if (textures.Length == 0)
                            {
                                stage = Stage.Done;
                                return false;
                            }

                            float baseProgress = 0.60f;
                            float endProgress = 0.98f;
                            float p = baseProgress + (endProgress - baseProgress) * (texturesComplete / (float)textures.Length);
                            progress?.Report(p);

                            int start = textureScanStart;
                            for (int iter = 0; iter < textures.Length; iter++)
                            {
                                int idx = (start + iter) % textures.Length;
                                var tex = textures[idx];

                                if (tex.IsAsyncLoadComplete)
                                {
                                    continue;
                                }

                                if (MeasureTextureUploadStep(tex))
                                {
                                    texturesComplete++;
                                }

                                textureScanStart = (idx + 1) % textures.Length;
                                return false;
                            }

                            // All textures are complete (uploaded or failed decode).
                            stage = Stage.Done;
                            return false;

                        case Stage.Done:
                            progress?.Report(1.0f);
                            model.SetAsyncLoadPerfStats(new Model.AsyncLoadPerfStats
                            {
                                TotalMs = stopwatch.Elapsed.TotalMilliseconds,
                                GpuSetupMs = gpuSetupMs,
                                ShaderWarmupMs = shaderWarmupMs,
                                TextureUploadMs = textureUploadMs,
                                GpuSetupSteps = gpuSetupSteps,
                                ShaderWarmupSteps = shaderWarmupSteps,
                                TextureUploadSteps = textureUploadSteps
                            });
                            tcs.TrySetResult();
                            return true;
                    }
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                    return true;
                }

                tcs.TrySetResult();
                return true;
            }

            private bool MeasureGpuSetupStep()
            {
                long start = Stopwatch.GetTimestamp();
                bool done = model.StepGpuSetup();
                gpuSetupMs += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                gpuSetupSteps++;
                return done;
            }

            private void MeasureShaderWarmupStep(string shaderName)
            {
                long start = Stopwatch.GetTimestamp();
                ShaderPool.Instance.GetShader(shaderName);
                shaderWarmupMs += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                shaderWarmupSteps++;
            }

            private bool MeasureTextureUploadStep(Texture texture)
            {
                long start = Stopwatch.GetTimestamp();
                bool uploaded = texture.TryUploadDecodedOnGlThread();
                textureUploadMs += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                textureUploadSteps++;
                return uploaded;
            }
        }
    }
}
