using System.Text.Json;
using System.Text.Json.Nodes;

namespace TrinitySceneView
{
    public sealed class SceneViewerConfig
    {
        public string? AssetRoot { get; set; }
        public string? LastSceneDirectory { get; set; }
        public bool DebugLogs { get; set; } = false;
        public bool SceneDiagnosticsLogs { get; set; } = false;
        public string? SceneDiagnosticsTarget { get; set; }
        public bool DarkMode { get; set; } = false;
        public bool SpawnModelsAtOrigin { get; set; } = false;
        public bool LargeClipPlanes { get; set; } = true;
        public bool LoadNpcSpawnerModels { get; set; } = true;
        public bool LoadStreamingGroupSubScenes { get; set; } = false;
        public bool FastOverviewLoading { get; set; } = true;
        public bool DiskSceneCache { get; set; } = true;
        public string? SceneDiskCacheDirectory { get; set; }
        public bool ApplyLegacySceneCorrection { get; set; } = false;
        public bool RotateModels180X { get; set; } = false;
        public bool RotateModels180Y { get; set; } = false;
        public bool RotateActors180X { get; set; } = false;
        public bool RotateActors180Y { get; set; } = false;
        public bool ApplySceneRotationToActors { get; set; } = true;
        public bool ApplySceneRotationToEventCamera { get; set; } = true;
        public bool LoadAllLods { get; set; } = false;
        public int SceneLoadCpuParallelism { get; set; } = 0;
        public int HeightFieldMaxSegments { get; set; } = 400_000;
        public bool AdditiveLoads { get; set; } = false;
        public bool EnablePerfHud { get; set; } = false;
        public bool EnablePerfSpikeLog { get; set; } = false;
        public float AsyncGpuWorkBudgetMs { get; set; } = 3.0f;
        public float TrinsDebugRotateX { get; set; } = 0f;
        public float TrinsDebugRotateY { get; set; } = 0f;
        public float TrinsDebugRotateZ { get; set; } = 0f;
        public float TrinsDebugGlobalRotateX { get; set; } = 0f;
        public float TrinsDebugGlobalRotateY { get; set; } = 0f;
        public float TrinsDebugGlobalRotateZ { get; set; } = 0f;
        public float TrinsDebugTranslateX { get; set; } = 0f;
        public float TrinsDebugTranslateY { get; set; } = 0f;
        public float TrinsDebugTranslateZ { get; set; } = 0f;
        public bool TrinsDebugLiveUpdate { get; set; } = true;
        public float SceneDebugRotateX { get; set; } = 0f;
        public float SceneDebugRotateY { get; set; } = 0f;
        public float SceneDebugRotateZ { get; set; } = 0f;
        public float SceneDebugGlobalRotateX { get; set; } = 0f;
        public float SceneDebugGlobalRotateY { get; set; } = 0f;
        public float SceneDebugGlobalRotateZ { get; set; } = 0f;
        public float SceneDebugTranslateX { get; set; } = 0f;
        public float SceneDebugTranslateY { get; set; } = 0f;
        public float SceneDebugTranslateZ { get; set; } = 0f;
        public bool SceneDebugLiveUpdate { get; set; } = true;
        public string? ObjectDebugTarget { get; set; }
        public float ObjectDebugRotateX { get; set; } = 0f;
        public float ObjectDebugRotateY { get; set; } = 0f;
        public float ObjectDebugRotateZ { get; set; } = 0f;
        public float ObjectDebugGlobalRotateX { get; set; } = 0f;
        public float ObjectDebugGlobalRotateY { get; set; } = 0f;
        public float ObjectDebugGlobalRotateZ { get; set; } = 0f;
        public float ObjectDebugTranslateX { get; set; } = 0f;
        public float ObjectDebugTranslateY { get; set; } = 0f;
        public float ObjectDebugTranslateZ { get; set; } = 0f;
        public bool ObjectDebugExactMatch { get; set; } = true;
        public bool ObjectDebugLiveUpdate { get; set; } = true;

        public static SceneViewerConfig Load()
        {
            try
            {
                var path = GetConfigPath();
                if (!File.Exists(path))
                {
                    return new SceneViewerConfig();
                }

                var json = File.ReadAllText(path);
                var root = JsonNode.Parse(json) as JsonObject;
                if (root != null)
                {
                    var viewerNode = root["sceneViewer"];
                    if (viewerNode != null)
                    {
                        return viewerNode.Deserialize<SceneViewerConfig>() ?? new SceneViewerConfig();
                    }
                }

                return JsonSerializer.Deserialize<SceneViewerConfig>(json) ?? new SceneViewerConfig();
            }
            catch
            {
                return new SceneViewerConfig();
            }
        }

        public void Save()
        {
            try
            {
                var path = GetConfigPath();
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var root = new JsonObject();
                if (File.Exists(path))
                {
                    var existing = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
                    if (existing != null)
                    {
                        root = existing;
                    }
                }

                root["sceneViewer"] = JsonSerializer.SerializeToNode(this);
                var json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
            }
            catch
            {
                // Best-effort; ignore config save failures.
            }
        }

        private static string GetConfigPath()
        {
            return Path.Combine(Directory.GetCurrentDirectory(), "settings.json");
        }
    }
}
