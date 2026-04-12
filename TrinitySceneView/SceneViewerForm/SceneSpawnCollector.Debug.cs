using GFTool.Core.Flatbuffers.TR.Scene;
using GFTool.Core.Flatbuffers.TR.Scene.Components;
using GFTool.Renderer.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Trinity.Core.Utils;

namespace TrinitySceneView
{
    public partial class SceneViewerForm
    {
        private void TryLogNpcSpawnerSceneSummary(string sceneFile, TRSCN trscn)
        {
            if (!MessageHandler.Instance.DebugLogsEnabled)
            {
                return;
            }

            var positionsByName = new Dictionary<string, PlacementPosition>(StringComparer.Ordinal);
            var templatesByName = new Dictionary<string, string>(StringComparer.Ordinal);
            var spawners = new List<PlacementSpawner>();
            CollectPlacementRegistryData(trscn.Chunks ?? Array.Empty<SceneChunk>(), positionsByName, templatesByName, spawners);

            int npcSpawnerObjectTemplateCount = CountNpcSpawnerObjectTemplates(trscn.Chunks ?? Array.Empty<SceneChunk>());

            MessageHandler.Instance.AddMessage(
                MessageType.LOG,
                $"[Scene] NPC spawner subscene summary: file={Path.GetFileName(sceneFile)} placementPos={positionsByName.Count} placementTemplates={templatesByName.Count} placementSpawners={spawners.Count} npcSpawnerOT={npcSpawnerObjectTemplateCount}");

            foreach (var s in spawners.Take(5))
            {
                MessageHandler.Instance.AddMessage(
                    MessageType.LOG,
                    $"[Scene] PlacementSpawner: name={s?.Name ?? "(null)"} sceneRef={s?.Scene ?? "(null)"} args={s?.Arguments?.Count ?? 0}");
            }

            foreach (var kv in templatesByName.Take(5))
            {
                MessageHandler.Instance.AddMessage(MessageType.LOG, $"[Scene] PlacementTemplate: {kv.Key} -> {kv.Value}");
            }
        }

        private static int CountNpcSpawnerObjectTemplates(SceneChunk[] chunks)
        {
            int count = 0;
            foreach (var chunk in chunks)
            {
                if (chunk == null)
                {
                    continue;
                }

                if (chunk.Type == nameof(trinity_ObjectTemplate) && chunk.Data != null)
                {
                    try
                    {
                        var ot = FlatBufferConverter.DeserializeFrom<trinity_ObjectTemplate>(chunk.Data);
                        if (!string.IsNullOrWhiteSpace(ot?.FilePath) &&
                            ot.FilePath.Replace('\\', '/').Contains("/npc_spawner/", StringComparison.OrdinalIgnoreCase))
                        {
                            count++;
                        }
                    }
                    catch
                    {
                        // ignore
                    }
                }

                if (chunk.Children != null && chunk.Children.Length > 0)
                {
                    count += CountNpcSpawnerObjectTemplates(chunk.Children);
                }
            }
            return count;
        }
    }
}
