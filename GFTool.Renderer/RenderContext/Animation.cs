using GFTool.Renderer.Core;
using GFTool.Renderer.Core.Graphics;
using GFTool.Renderer.Scene;
using GFTool.Renderer.Scene.GraphicsObjects;
using OpenTK;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;
using System.Linq;
using System.IO;
using Trinity.Core.Assets;

namespace GFTool.Renderer
{
    public partial class RenderContext : IDisposable
    {
        private BlendShapeAnimation? activeBlendShapeAnimation;
        private string? activeBlendShapeSourcePath;
        private string? lastBlendShapeApplyError;

        private MeshVisibilityAnimation? activeVisibilityAnimation;
        private string? activeVisibilitySourcePath;

        private MaterialParameterAnimation? activeMaterialAnimation;
        private string? activeMaterialSourcePath;

        public void PlayAnimation(Animation animation)
        {
            PlayAnimation(animation, null);
        }

        public void PlayAnimation(Animation animation, Animation? fallbackAnimation)
        {
            activeAnimation = animation;
            activeAnimationFallback = fallbackAnimation;
            animationTimeSeconds = 0;
            lastAnimationTicks = 0;
            animationPaused = false;

            lastBlendShapeApplyError = null;
            TryAttachBlendShapeAnimation(animation);
            TryAttachVisibilityAnimation(animation);
            TryAttachMaterialAnimation(animation);
            TryAttachModelsToPrimaryArmature(animation);
            if (MessageHandler.Instance.DebugLogsEnabled)
            {
                MessageHandler.Instance.AddMessage(
                    MessageType.LOG,
                    $"[Anim] Play '{animation.Name}' frames={animation.FrameCount} fps={animation.FrameRate} tracks={animation.TrackCount}" +
                    (fallbackAnimation != null ? $" fallback='{fallbackAnimation.Name}'" : string.Empty));

                foreach (var c in SceneGraph.Instance.GetRoot().children)
                {
                    if (c is Model model)
                    {
                        var armature = model.Armature;
                        if (armature == null)
                        {
                            MessageHandler.Instance.AddMessage(MessageType.LOG, $"[Anim] Model '{model.Name}': no armature");
                            continue;
                        }

                        int matches = 0;
                        foreach (var bone in armature.Bones)
                        {
                            if (animation.HasTrack(bone.Name) || (fallbackAnimation != null && fallbackAnimation.HasTrack(bone.Name)))
                            {
                                matches++;
                            }
                        }

                        MessageHandler.Instance.AddMessage(MessageType.LOG, $"[Anim] Model '{model.Name}': bones={armature.Bones.Count} trackMatches={matches}");
                    }
                }
            }
        }

        private void TryAttachBlendShapeAnimation(Animation animation)
        {
            if (animation == null || string.IsNullOrWhiteSpace(animation.SourcePath))
            {
                activeBlendShapeAnimation = null;
                activeBlendShapeSourcePath = null;
                return;
            }

            string source = animation.SourcePath!;
            string tracmPath;
            try
            {
                tracmPath = Path.ChangeExtension(source, ".tracm");
            }
            catch
            {
                tracmPath = string.Empty;
            }

            if (string.IsNullOrWhiteSpace(tracmPath) || !File.Exists(tracmPath))
            {
                activeBlendShapeAnimation = null;
                activeBlendShapeSourcePath = null;
                return;
            }

            if (string.Equals(activeBlendShapeSourcePath, tracmPath, StringComparison.OrdinalIgnoreCase) &&
                activeBlendShapeAnimation != null)
            {
                return;
            }

            try
            {
                activeBlendShapeAnimation = BlendShapeAnimation.Load(tracmPath);
                activeBlendShapeSourcePath = tracmPath;
                if (MessageHandler.Instance.DebugLogsEnabled)
                {
                    MessageHandler.Instance.AddMessage(
                        MessageType.LOG,
                        $"[Anim] Loaded TRACM '{activeBlendShapeAnimation.Name}' file='{tracmPath}' frames={activeBlendShapeAnimation.FrameCount} fps={activeBlendShapeAnimation.FrameRate}");
                }
            }
            catch (Exception ex)
            {
                activeBlendShapeAnimation = null;
                activeBlendShapeSourcePath = null;
                if (MessageHandler.Instance.DebugLogsEnabled)
                {
                    MessageHandler.Instance.AddMessage(MessageType.WARNING, $"[Anim] Failed to load TRACM (blendshape) '{tracmPath}': {ex.Message}");
                }
            }
        }

        private void TryAttachVisibilityAnimation(Animation animation)
        {
            if (animation == null || string.IsNullOrWhiteSpace(animation.SourcePath))
            {
                DetachVisibilityAnimation(resetMeshes: true);
                return;
            }

            string source = animation.SourcePath!;
            string tracmPath;
            try
            {
                tracmPath = Path.ChangeExtension(source, ".tracm");
            }
            catch
            {
                tracmPath = string.Empty;
            }

            if (string.IsNullOrWhiteSpace(tracmPath) || !File.Exists(tracmPath))
            {
                DetachVisibilityAnimation(resetMeshes: true);
                return;
            }

            if (string.Equals(activeVisibilitySourcePath, tracmPath, StringComparison.OrdinalIgnoreCase) &&
                activeVisibilityAnimation != null)
            {
                return;
            }

            DetachVisibilityAnimation(resetMeshes: true);

            if (!MeshVisibilityAnimation.TryLoad(tracmPath, out var vis))
            {
                activeVisibilityAnimation = null;
                activeVisibilitySourcePath = null;
                return;
            }

            activeVisibilityAnimation = vis;
            activeVisibilitySourcePath = tracmPath;
            if (MessageHandler.Instance.DebugLogsEnabled)
            {
                MessageHandler.Instance.AddMessage(
                    MessageType.LOG,
                    $"[Anim] Loaded visibility TRACM '{vis!.Name}' file='{tracmPath}' frames={vis.FrameCount} fps={vis.FrameRate} meshes={vis.ControlledMeshNames.Count}");
            }
        }

        private void DetachVisibilityAnimation(bool resetMeshes)
        {
            if (resetMeshes && activeVisibilityAnimation != null)
            {
                foreach (var model in SceneGraph.Instance.GetRoot().children.OfType<Model>())
                {
                    activeVisibilityAnimation.ResetControlledMeshesToVisible(model);
                }
            }

            activeVisibilityAnimation = null;
            activeVisibilitySourcePath = null;
        }

        private void TryAttachMaterialAnimation(Animation animation)
        {
            if (animation == null || string.IsNullOrWhiteSpace(animation.SourcePath))
            {
                DetachMaterialAnimation(resetMaterials: true);
                return;
            }

            string source = animation.SourcePath!;
            string tracmPath;
            try
            {
                tracmPath = Path.ChangeExtension(source, ".tracm");
            }
            catch
            {
                tracmPath = string.Empty;
            }

            if (string.IsNullOrWhiteSpace(tracmPath) || !File.Exists(tracmPath))
            {
                DetachMaterialAnimation(resetMaterials: true);
                return;
            }

            if (string.Equals(activeMaterialSourcePath, tracmPath, StringComparison.OrdinalIgnoreCase) &&
                activeMaterialAnimation != null)
            {
                return;
            }

            DetachMaterialAnimation(resetMaterials: true);

            if (!MaterialParameterAnimation.TryLoad(tracmPath, out var matAnim))
            {
                activeMaterialAnimation = null;
                activeMaterialSourcePath = null;
                return;
            }

            activeMaterialAnimation = matAnim;
            activeMaterialSourcePath = tracmPath;
            if (MessageHandler.Instance.DebugLogsEnabled)
            {
                MessageHandler.Instance.AddMessage(
                    MessageType.LOG,
                    $"[Anim] Loaded material TRACM '{matAnim!.Name}' file='{tracmPath}' frames={matAnim.FrameCount} fps={matAnim.FrameRate} " +
                    $"float={matAnim.FloatChannelCount} float4={matAnim.Float4ChannelCount}");

                foreach (var line in matAnim.GetDebugSummaryLines(maxLines: 6))
                {
                    MessageHandler.Instance.AddMessage(MessageType.LOG, line);
                }
            }
        }

        private void DetachMaterialAnimation(bool resetMaterials)
        {
            if (resetMaterials && activeMaterialAnimation != null)
            {
                foreach (var model in SceneGraph.Instance.GetRoot().children.OfType<Model>())
                {
                    activeMaterialAnimation.ResetOverridesForModel(model);
                }
            }

            activeMaterialAnimation = null;
            activeMaterialSourcePath = null;
        }

        private void TryAttachModelsToPrimaryArmature(Animation animation)
        {
            if (RenderOptions.DeterministicSkinningAndAnimation)
            {
                // Deterministic mode: don't override armature bindings automatically.
                return;
            }

            var models = SceneGraph.Instance.GetRoot().children.OfType<Model>().ToList();
            if (models.Count == 0)
            {
                return;
            }

            var trackMatches = new Dictionary<Model, int>();
            Model? primaryModel = null;
            int bestMatches = -1;

            foreach (var model in models)
            {
                int matches = 0;
                var armature = model.Armature;
                if (armature != null)
                {
                    foreach (var bone in armature.Bones)
                    {
                        if (animation.HasTrack(bone.Name))
                        {
                            matches++;
                        }
                    }
                }

                trackMatches[model] = matches;
                if (armature != null && matches > bestMatches)
                {
                    bestMatches = matches;
                    primaryModel = model;
                }
            }

            var primaryArmature = primaryModel?.Armature;
            if (primaryArmature == null)
            {
                return;
            }

            foreach (var model in models)
            {
                if (ReferenceEquals(model, primaryModel))
                {
                    model.SetArmatureOverride(null);
                    continue;
                }

                int matches = trackMatches.TryGetValue(model, out var m) ? m : 0;
                if (model.Armature == null || matches == 0)
                {
                    model.SetArmatureOverride(primaryArmature);
                }
                else
                {
                    model.SetArmatureOverride(null);
                }
            }
        }

        public void SetAnimationTargets(IEnumerable<Model>? models)
        {
            animationTargets.Clear();
            if (models == null)
            {
                useAnimationTargets = false;
                return;
            }

            foreach (var model in models)
            {
                if (model != null)
                {
                    animationTargets.Add(model);
                }
            }

            useAnimationTargets = animationTargets.Count > 0;
        }

        public void StopAnimation()
        {
            activeAnimation = null;
            animationTimeSeconds = 0;
            lastAnimationTicks = 0;
            animationPaused = false;
            activeBlendShapeAnimation = null;
            activeBlendShapeSourcePath = null;
            lastBlendShapeApplyError = null;
            DetachVisibilityAnimation(resetMeshes: true);
            DetachMaterialAnimation(resetMaterials: true);
            var models = SceneGraph.Instance.GetRoot().children.OfType<Model>().ToList();
            foreach (var model in models)
            {
                model.ResetPose();
                if (model.HasCpuFullMorphTargets)
                {
                    model.ResetCpuFullMorphWeights();
                    model.TryApplyCpuFullMorphs(out _);
                }
                model.SetArmatureOverride(null);
            }
        }

        public void SetAnimationPaused(bool paused)
        {
            animationPaused = paused;
        }

        public bool IsAnimationPaused()
        {
            return animationPaused;
        }

        public bool HasActiveAnimation()
        {
            return activeAnimation != null;
        }

        public void SetLoopAnimationOverride(bool enabled)
        {
            loopAnimationOverride = enabled;
        }

        public bool GetLoopAnimationOverride()
        {
            return loopAnimationOverride;
        }

        public double GetAnimationTimeSeconds()
        {
            return animationTimeSeconds;
        }

        public double GetActiveAnimationDurationSeconds()
        {
            if (activeAnimation == null)
            {
                return 0;
            }

            double fps = activeAnimation.FrameRate > 0 ? activeAnimation.FrameRate : 30.0;
            double frames = activeAnimation.FrameCount;
            if (fps <= 0 || frames <= 0)
            {
                return 0;
            }

            // For looping playback (either authored looping, or user-forced), treat the duration as FrameCount/fps
            // so time wraps cleanly and the last frame can be reached.
            if (loopAnimationOverride || activeAnimation.LoopType == Animation.PlayType.Looped)
            {
                return frames / fps;
            }

            // For one-shot playback, last frame is at (FrameCount-1)/fps.
            return Math.Max(0, (frames - 1) / fps);
        }

        public void SetAnimationTimeSeconds(double timeSeconds)
        {
            if (activeAnimation == null)
            {
                return;
            }

            var duration = GetActiveAnimationDurationSeconds();
            if (duration > 0)
            {
                if (loopAnimationOverride || activeAnimation.LoopType == Animation.PlayType.Looped)
                {
                    timeSeconds %= duration;
                    if (timeSeconds < 0) timeSeconds += duration;
                }
                else
                {
                    timeSeconds = Math.Clamp(timeSeconds, 0, duration);
                }
            }
            else
            {
                timeSeconds = Math.Max(0, timeSeconds);
            }

            animationTimeSeconds = timeSeconds;
            lastAnimationTicks = Stopwatch.GetTimestamp();

            float frame = GetAnimationFrame(activeAnimation, (float)animationTimeSeconds);
            ApplyAnimationFrameToScene(frame);
        }

    }
}
