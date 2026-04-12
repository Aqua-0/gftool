namespace GFTool.Renderer.Core
{
    public enum ShaderGame
    {
        Auto = 0,
        SCVI = 1,
        ZA = 2,
        LA = 3
    }

    public enum UvSetOverride
    {
        Material = 0,
        Uv0 = 1,
        Uv1 = 2
    }

    public static class RenderOptions
    {
        public static bool EnableNormalMaps { get; set; } = true;
        public static bool EnableAO { get; set; } = true;
        public static bool EnableVertexColors { get; set; } = true;
        public static bool FlipNormalY { get; set; } = true;
        public static bool ReconstructNormalZ { get; set; } = true;
        public static bool LegacyMode { get; set; } = false;
        public static OpenTK.Mathematics.Vector3 WorldLightDirection { get; set; } = new OpenTK.Mathematics.Vector3(-0.681f, -0.096f, -3.139f).Normalized();
        public static float LightWrap { get; set; } = 0.5f;
        public static float SpecularScale { get; set; } = 0.45f;
        public static float LensOpacity { get; set; } = 0.35f;
        public static bool TransparentPass { get; set; } = false;
        public static OpenTK.Mathematics.Vector3 OutlineColor { get; set; } = new OpenTK.Mathematics.Vector3(0.65f, 0.65f, 0.65f);
        public static float OutlineAlpha { get; set; } = 1.0f;
        public static bool OutlinePass { get; set; } = false;
        public static bool ParticlePass { get; set; } = false;
        public static bool ShowSkeleton { get; set; } = false;
        public static bool UseTrsklInverseBind { get; set; } = true;
        public static bool SwapBlendOrder { get; set; } = false;
        public static bool AutoMapBlendIndices { get; set; } = true;
        public static bool MapBlendIndicesViaBoneMeta { get; set; } = false;
        public static bool TransposeSkinMatrices { get; set; } = false;
        public static bool MapBlendIndicesViaSkinningPalette { get; set; } = false;
        public static bool UseSkinningPaletteMatrices { get; set; } = false;
        public static bool MapBlendIndicesViaJointInfo { get; set; } = false;
        public static bool UseJointInfoMatrices { get; set; } = false;
        public static bool DeterministicSkinningAndAnimation { get; set; } = false;

        public static UvSetOverride LayerMaskUvOverride { get; set; } = UvSetOverride.Material;
        public static UvSetOverride AOUvOverride { get; set; } = UvSetOverride.Material;

        public static bool UseRareTrmtrMaterials { get; set; } = false;

        public static bool UseBackupIkCharacterShader { get; set; } = false;

        public static int ShaderDebugMode { get; set; } = 0;

        public static bool EnablePerfHud { get; set; } = false;
        public static bool EnablePerfSpikeLog { get; set; } = false;
        public static float PerfSpikeThresholdMs { get; set; } = 10.0f;

        public static ShaderGame ShaderGame { get; set; } = ShaderGame.Auto;

        // Async loading (keeps UI/render loop responsive by slicing GL work across frames).
        public static bool EnableAsyncResourceLoading { get; set; } = true;
        public static float AsyncGpuWorkBudgetMs { get; set; } = 3.0f;
        public static int AsyncTextureDecodeConcurrency { get; set; } = 2;

        // Render targets (set by RenderContext on resize / frame).
        public static int RenderTargetWidth { get; set; } = 1;
        public static int RenderTargetHeight { get; set; } = 1;
        public static float CameraNear { get; set; } = 0.1f;
        public static float CameraFar { get; set; } = 1000.0f;

        // Post-deferred scene textures (set by GBuffer final pass).
        // Used for forward/transparent effects (water refraction, etc).
        public static int SceneColorTextureId { get; set; } = 0;
        public static int SceneDepthTextureId { get; set; } = 0;
        public static int EnvCubemapTextureId { get; set; } = 0;
        public static float EnvMaxLod { get; set; } = 1.0f;
        public static float EnvIntensity { get; set; } = 1.0f;

        public static bool EnableTeraEffect { get; set; } = false;
        public static OpenTK.Mathematics.Vector3 TeraColor { get; set; } = new OpenTK.Mathematics.Vector3(1.0f, 1.0f, 1.0f);
        public static float TeraStrength { get; set; } = 1.0f;

        public static bool EnableDirectionalShadows { get; set; } = false;
        public static int ShadowCascadeCount { get; set; } = 4;
        public static int ShadowMapResolution { get; set; } = 2048;
        public static float ShadowMaxDistance { get; set; } = 120.0f;
        public static float ShadowCascadeLambda { get; set; } = 0.6f;
        public static float ShadowDepthBias { get; set; } = 0.0006f;
        public static float ShadowNormalBias { get; set; } = 0.0018f;
        public static float ShadowPcfRadius { get; set; } = 1.2f;
        public static bool EnableScreenSpaceShadows { get; set; } = true;
        public static int ScreenSpaceShadowSteps { get; set; } = 16;
        public static float ScreenSpaceShadowStepSize { get; set; } = 0.35f;
        public static float ScreenSpaceShadowThickness { get; set; } = 0.35f;

        // Optional disk fallback for loading shared textures/motions from an extracted "out" tree.
        public static bool EnableExtractedOutFallback { get; set; } = false;
        public static string ExtractedOutRoot { get; set; } = string.Empty;
        public static string ExtractedOutGame { get; set; } = "ZA";
    }
}
