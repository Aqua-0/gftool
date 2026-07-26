namespace GFTool.Renderer.Scene.GraphicsObjects
{
    public partial class Model
    {
        public readonly struct AsyncLoadPerfStats
        {
            public double TotalMs { get; init; }
            public double GpuSetupMs { get; init; }
            public double ShaderWarmupMs { get; init; }
            public double TextureUploadMs { get; init; }
            public int GpuSetupSteps { get; init; }
            public int ShaderWarmupSteps { get; init; }
            public int TextureUploadSteps { get; init; }
        }

        private AsyncLoadPerfStats lastAsyncLoadPerfStats;
        public AsyncLoadPerfStats LastAsyncLoadPerfStats => lastAsyncLoadPerfStats;

        internal void SetAsyncLoadPerfStats(AsyncLoadPerfStats stats)
        {
            lastAsyncLoadPerfStats = stats;
        }
    }
}
