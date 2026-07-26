namespace GFTool.Renderer.Scene.GraphicsObjects
{
    public partial class Model
    {
        public readonly struct PreparePerfStats
        {
            public double TotalMs { get; init; }
            public double MeshMs { get; init; }
            public double MeshReadMs { get; init; }
            public double MeshMorphMs { get; init; }
            public double MeshDecodeMs { get; init; }
            public double MaterialMs { get; init; }
            public double MaterialReadMs { get; init; }
            public double MaterialDeserializeMs { get; init; }
            public double MaterialLegacyDeserializeMs { get; init; }
            public double MaterialBuildMs { get; init; }
            public double SkeletonMs { get; init; }
            public int MeshCount { get; init; }
        }

        private PreparePerfStats lastPreparePerfStats;
        public PreparePerfStats LastPreparePerfStats => lastPreparePerfStats;

        private double prepareMeshMs;
        private double prepareMeshReadMs;
        private double prepareMeshMorphMs;
        private double prepareMeshDecodeMs;
        private double prepareMaterialMs;
        private double prepareMaterialReadMs;
        private double prepareMaterialDeserializeMs;
        private double prepareMaterialLegacyDeserializeMs;
        private double prepareMaterialBuildMs;
        private double prepareSkeletonMs;
        private int prepareMeshCount;

        private void ResetPreparePerfStats()
        {
            prepareMeshMs = 0.0;
            prepareMeshReadMs = 0.0;
            prepareMeshMorphMs = 0.0;
            prepareMeshDecodeMs = 0.0;
            prepareMaterialMs = 0.0;
            prepareMaterialReadMs = 0.0;
            prepareMaterialDeserializeMs = 0.0;
            prepareMaterialLegacyDeserializeMs = 0.0;
            prepareMaterialBuildMs = 0.0;
            prepareSkeletonMs = 0.0;
            prepareMeshCount = 0;
            lastPreparePerfStats = default;
        }

        private void FinalizePreparePerfStats(double totalMs)
        {
            lastPreparePerfStats = new PreparePerfStats
            {
                TotalMs = totalMs,
                MeshMs = prepareMeshMs,
                MeshReadMs = prepareMeshReadMs,
                MeshMorphMs = prepareMeshMorphMs,
                MeshDecodeMs = prepareMeshDecodeMs,
                MaterialMs = prepareMaterialMs,
                MaterialReadMs = prepareMaterialReadMs,
                MaterialDeserializeMs = prepareMaterialDeserializeMs,
                MaterialLegacyDeserializeMs = prepareMaterialLegacyDeserializeMs,
                MaterialBuildMs = prepareMaterialBuildMs,
                SkeletonMs = prepareSkeletonMs,
                MeshCount = prepareMeshCount
            };
        }
    }
}
