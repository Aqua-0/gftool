using GFTool.Renderer.Core;
using GFTool.Renderer.Scene.GraphicsObjects;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Trinity.Core.Utils;
using TrAnim = Trinity.Core.Flatbuffers.TR.Animation;

namespace TrinitySceneView
{
    public partial class SceneViewerForm
    {
        private readonly Dictionary<string, string> eventActorCurrentTranmAbsByActor = new(StringComparer.Ordinal);

        private void ApplySetMotion(List<object?> args)
        {
            // SetMotion(actorId, motionId)
            if (args.Count < 2)
            {
                return;
            }

            var actor = args[0] as string;
            if (string.IsNullOrWhiteSpace(actor))
            {
                return;
            }

            if (!TryGetFloat(args[1], out var idFloat))
            {
                return;
            }

            TryPlayActorAnimationById(actor, (int)idFloat, source: "SetMotion");
        }

        private void ApplyResetAnimation(List<object?> args)
        {
            // FObj_Reset_Animation(actorId)
            if (args.Count < 1)
            {
                return;
            }

            var actor = args[0] as string;
            if (string.IsNullOrWhiteSpace(actor))
            {
                return;
            }

            // MVP: global animation player, so we can only stop everything.
            renderCtrl?.renderer?.StopAnimation();
            renderCtrl?.renderer?.SetAnimationTargets(null);
        }

        private void ApplySetAnimationImmediately(List<object?> args)
        {
            // FObj_Set_Animation_Immediately(actorId, motionId)
            if (args.Count < 2)
            {
                return;
            }

            var actor = args[0] as string;
            if (string.IsNullOrWhiteSpace(actor))
            {
                return;
            }

            if (!TryGetFloat(args[1], out var idFloat))
            {
                return;
            }

            TryPlayActorAnimationById(actor, (int)idFloat, source: "FObj_Set_Animation_Immediately");
        }

        private bool TryPlayActorAnimationById(string actorId, int animationId, string source)
        {
            if (renderCtrl?.renderer == null)
            {
                return false;
            }

            if (!eventActorModels.TryGetValue(actorId, out var models) || models.Count == 0)
            {
                return false;
            }

            if (!TryResolveActorAnimationAbs(actorId, animationId, out var tranmAbs, out var reason))
            {
                if (!string.IsNullOrWhiteSpace(reason))
                {
                    MessageHandler.Instance.AddMessage(MessageType.WARNING, $"[Event] {source} '{actorId}' id={animationId}: {reason}");
                }
                return false;
            }

            if (eventActorCurrentTranmAbsByActor.TryGetValue(actorId, out var current) &&
                string.Equals(current, tranmAbs, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!eventAnimationCacheByAbsPath.TryGetValue(tranmAbs, out var anim))
            {
                TrAnim.TRANM tranm;
                try
                {
                    tranm = FlatBufferConverter.DeserializeFrom<TrAnim.TRANM>(tranmAbs);
                }
                catch (Exception ex)
                {
                    MessageHandler.Instance.AddMessage(MessageType.WARNING, $"[Event] Failed to parse TRANM '{Path.GetFileName(tranmAbs)}': {ex.GetType().Name}: {ex.Message}");
                    return false;
                }

                var gf = TranmToGfAnimation.Convert(tranm);
                anim = new Animation(gf, Path.GetFileNameWithoutExtension(tranmAbs), sourcePath: tranmAbs);
                eventAnimationCacheByAbsPath[tranmAbs] = anim;
            }

            renderCtrl.renderer.SetAnimationTargets(models.Select(m => m.Model));
            renderCtrl.renderer.PlayAnimation(anim);
            eventActorCurrentTranmAbsByActor[actorId] = tranmAbs;
            renderCtrl.Invalidate();
            return true;
        }

        private bool TryResolveActorAnimationAbs(string actorId, int animationId, out string tranmAbs, out string? reason)
        {
            tranmAbs = string.Empty;
            reason = null;

            if (string.IsNullOrWhiteSpace(assetRoot) || !Directory.Exists(assetRoot))
            {
                reason = "Asset root not set.";
                return false;
            }

            if (!TryResolveMotionDirAbsForActor(actorId, out var motionDirAbs, out var motionReason))
            {
                reason = motionReason;
                return false;
            }

            var deterministicOk = TryResolveVirtualAnimationTranmAbs(actorId, animationId, motionDirAbs, out tranmAbs, out var deterministicReason);
            if (deterministicOk)
            {
                return true;
            }

            // Fallback: match the numeric id embedded in the filename: *_00028_*.tranm
            var candidates = Directory.EnumerateFiles(motionDirAbs, "*.tranm", SearchOption.TopDirectoryOnly)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (candidates.Count == 0)
            {
                reason = $"No .tranm files under '{motionDirAbs}'.";
                return false;
            }

            var needle = $"_{animationId.ToString("00000", CultureInfo.InvariantCulture)}_";
            var numeric = candidates.FirstOrDefault(p => p.Contains(needle, StringComparison.OrdinalIgnoreCase));
            if (numeric != null)
            {
                tranmAbs = numeric;
                if (MessageHandler.Instance.DebugLogsEnabled && !string.IsNullOrWhiteSpace(deterministicReason))
                {
                    MessageHandler.Instance.AddMessage(
                        MessageType.LOG,
                        $"[Event] Anim fallback '{actorId}' id={animationId}: {deterministicReason}");
                }
                return true;
            }

            reason = $"No .tranm matched virtualAnimId={animationId} under '{motionDirAbs}'.";
            return false;
        }

        private bool TryResolveMotionDirAbsForActor(string actorId, out string motionDirAbs, out string? reason)
        {
            motionDirAbs = string.Empty;
            reason = null;

            if (!string.IsNullOrWhiteSpace(actorId) && eventActorMotionDirRels.TryGetValue(actorId, out var rel) && !string.IsNullOrWhiteSpace(rel))
            {
                var abs = Path.Combine(assetRoot!, rel.Replace('/', Path.DirectorySeparatorChar));
                if (Directory.Exists(abs))
                {
                    motionDirAbs = abs;
                    return true;
                }
            }

            if (TryInferMotionDirRelFromSpawnModels(actorId, out var inferredRel))
            {
                eventActorMotionDirRels[actorId] = inferredRel;
                var abs = Path.Combine(assetRoot!, inferredRel.Replace('/', Path.DirectorySeparatorChar));
                if (Directory.Exists(abs))
                {
                    motionDirAbs = abs;
                    return true;
                }
            }

            if (TryInferMotionDirRelFromNpcTemplate(actorId, out var templateRel))
            {
                eventActorMotionDirRels[actorId] = templateRel;
                var abs = Path.Combine(assetRoot!, templateRel.Replace('/', Path.DirectorySeparatorChar));
                if (Directory.Exists(abs))
                {
                    motionDirAbs = abs;
                    return true;
                }
            }

            reason = $"No motion dir mapping for '{actorId}'.";
            return false;
        }

        private bool TryInferMotionDirRelFromSpawnModels(string actorId, out string motionDirRel)
        {
            motionDirRel = string.Empty;

            if (!eventActorSourceModelRels.TryGetValue(actorId, out var rels) || rels.Count == 0)
            {
                return false;
            }

            foreach (var rel in rels)
            {
                if (TryInferMotionDirRelFromModelRel(rel, out motionDirRel))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryInferMotionDirRelFromModelRel(string modelRel, out string motionDirRel)
        {
            motionDirRel = string.Empty;
            if (string.IsNullOrWhiteSpace(modelRel))
            {
                return false;
            }

            modelRel = modelRel.Replace('\\', '/').TrimStart('/');
            const string prefix = "ik_chara/model_";
            int idx = modelRel.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                return false;
            }

            var after = modelRel.Substring(idx + prefix.Length);
            var parts = after.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                return false;
            }

            var variant = parts[0];
            var folder = parts[1];
            motionDirRel = $"ik_chara/motion_{variant}/{folder}";
            return true;
        }

        private bool TryInferMotionDirRelFromNpcTemplate(string actorId, out string motionDirRel)
        {
            motionDirRel = string.Empty;

            if (!TryResolveEventActorToNpcAssetId(actorId, out var npcAssetId))
            {
                return false;
            }

            if (!TryResolveNpcTemplateForAssetId(npcAssetId, out var templateAbs))
            {
                return false;
            }

            var file = Path.GetFileNameWithoutExtension(templateAbs);
            if (string.IsNullOrWhiteSpace(file))
            {
                return false;
            }

            // Most templates look like: tr0023_00_researcher_0.trsobt.
            var baseName = file;
            if (baseName.EndsWith("_0", StringComparison.OrdinalIgnoreCase))
            {
                baseName = baseName.Substring(0, baseName.Length - 2);
            }

            // Default to UQ; this is only a fallback when we couldn't infer from model paths.
            motionDirRel = $"ik_chara/motion_uq/{baseName}";
            return true;
        }
    }
}
