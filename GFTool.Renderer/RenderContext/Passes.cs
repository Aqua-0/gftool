using GFTool.Renderer.Core;
using GFTool.Renderer.Core.Graphics;
using GFTool.Renderer.Scene;
using GFTool.Renderer.Scene.GraphicsObjects;
using GFTool.Renderer.Scene.GraphicsObjects.Particles;
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
using Trinity.Core.Assets;

namespace GFTool.Renderer
{
    public partial class RenderContext : IDisposable
    {
        private readonly struct FrustumPlane
        {
            public readonly Vector3 Normal;
            public readonly float D;

            public FrustumPlane(Vector3 normal, float d)
            {
                float len = normal.Length;
                if (len > 0.000001f)
                {
                    Normal = normal / len;
                    D = d / len;
                }
                else
                {
                    Normal = normal;
                    D = d;
                }
            }

            public float DistanceToPoint(Vector3 point) => Vector3.Dot(Normal, point) + D;
        }

        private readonly struct ViewFrustum
        {
            private readonly FrustumPlane left;
            private readonly FrustumPlane right;
            private readonly FrustumPlane bottom;
            private readonly FrustumPlane top;
            private readonly FrustumPlane near;
            private readonly FrustumPlane far;

            public ViewFrustum(Matrix4 viewProj)
            {
                left = new FrustumPlane(new Vector3(viewProj.M14 + viewProj.M11, viewProj.M24 + viewProj.M21, viewProj.M34 + viewProj.M31), viewProj.M44 + viewProj.M41);
                right = new FrustumPlane(new Vector3(viewProj.M14 - viewProj.M11, viewProj.M24 - viewProj.M21, viewProj.M34 - viewProj.M31), viewProj.M44 - viewProj.M41);
                bottom = new FrustumPlane(new Vector3(viewProj.M14 + viewProj.M12, viewProj.M24 + viewProj.M22, viewProj.M34 + viewProj.M32), viewProj.M44 + viewProj.M42);
                top = new FrustumPlane(new Vector3(viewProj.M14 - viewProj.M12, viewProj.M24 - viewProj.M22, viewProj.M34 - viewProj.M32), viewProj.M44 - viewProj.M42);
                near = new FrustumPlane(new Vector3(viewProj.M13, viewProj.M23, viewProj.M33), viewProj.M43);
                far = new FrustumPlane(new Vector3(viewProj.M14 - viewProj.M13, viewProj.M24 - viewProj.M23, viewProj.M34 - viewProj.M33), viewProj.M44 - viewProj.M43);
            }

            public bool IntersectsSphere(Vector3 center, float radius)
            {
                return left.DistanceToPoint(center) >= -radius &&
                       right.DistanceToPoint(center) >= -radius &&
                       bottom.DistanceToPoint(center) >= -radius &&
                       top.DistanceToPoint(center) >= -radius &&
                       near.DistanceToPoint(center) >= -radius &&
                       far.DistanceToPoint(center) >= -radius;
            }
        }

        private const int GeometryBreakdownSampleIntervalMs = 200;
        private long nextGeometryBreakdownSampleTimestamp;

        private bool ShouldCollectGeometryBreakdown()
        {
            if (!RenderOptions.EnablePerfHud)
            {
                nextGeometryBreakdownSampleTimestamp = 0;
                return false;
            }

            long now = Stopwatch.GetTimestamp();
            if (nextGeometryBreakdownSampleTimestamp != 0 &&
                now < nextGeometryBreakdownSampleTimestamp)
            {
                return false;
            }

            nextGeometryBreakdownSampleTimestamp =
                now + (Stopwatch.Frequency * GeometryBreakdownSampleIntervalMs / 1000);
            return true;
        }

        private void GeometryPass()
        {
            RenderOptions.TransparentPass = false;
            //TODO: Traverse scene and only draw geometry (eventually)
            bool probeAlloc = RenderOptions.EnablePerfSpikeLog;
            long allocModelsSum = 0;
            long allocMax = 0;
            string allocMaxName = string.Empty;
            bool collectGeometryBreakdown = ShouldCollectGeometryBreakdown();
            List<PerfGeometryEntry>? modelContributions = collectGeometryBreakdown
                ? new List<PerfGeometryEntry>()
                : null;
            Dictionary<string, (int DrawCalls, long Triangles)>? materialContributions = collectGeometryBreakdown
                ? new Dictionary<string, (int DrawCalls, long Triangles)>(System.StringComparer.OrdinalIgnoreCase)
                : null;
            int totalModels = 0;
            int visibleModels = 0;
            int culledModels = 0;
            var frustum = new ViewFrustum(camera.viewMat * camera.projMat);
            foreach (var c in SceneGraph.Instance.GetRoot().children)
            {
                if (c is Model model)
                {
                    var (boundsCenter, boundsRadius) = model.GetApproximateWorldBounds();
                    bool intersectsFrustum = frustum.IntersectsSphere(boundsCenter, MathF.Max(boundsRadius, 0.01f));

                    if (collectGeometryBreakdown)
                    {
                        totalModels++;
                        if (intersectsFrustum)
                        {
                            visibleModels++;
                        }
                        else
                        {
                            culledModels++;
                        }

                        var modelContribution = model.GetOpaqueGeometryContribution();
                        if (modelContribution.DrawCalls > 0 || modelContribution.Triangles > 0)
                        {
                            modelContributions!.Add(new PerfGeometryEntry(model.Name, modelContribution.DrawCalls, modelContribution.Triangles));
                        }

                        foreach (var materialContribution in model.EnumerateOpaqueMaterialContributions())
                        {
                            string key = string.IsNullOrWhiteSpace(materialContribution.MaterialName) ? "<unnamed>" : materialContribution.MaterialName;
                            if (materialContributions!.TryGetValue(key, out var existing))
                            {
                                materialContributions[key] = (existing.DrawCalls + materialContribution.DrawCalls, existing.Triangles + materialContribution.Triangles);
                            }
                            else
                            {
                                materialContributions[key] = (materialContribution.DrawCalls, materialContribution.Triangles);
                            }
                        }
                    }

                    if (!intersectsFrustum)
                    {
                        continue;
                    }

                    long alloc0 = probeAlloc ? GetAllocatedBytesSafe() : 0;
                    model.Draw(camera.viewMat, camera.projMat);
                    if (probeAlloc && alloc0 != 0)
                    {
                        long alloc1 = GetAllocatedBytesSafe();
                        long delta = alloc1 != 0 ? alloc1 - alloc0 : 0;
                        allocModelsSum += delta;
                        if (delta > allocMax)
                        {
                            allocMax = delta;
                            allocMaxName = model.Name ?? string.Empty;
                        }
                    }
                }
            }
            if (probeAlloc)
            {
                lastAllocGeoModelsBytes = allocModelsSum;
                lastAllocGeoMaxModelBytes = allocMax;
                lastAllocGeoMaxModelName = allocMaxName;
            }

            if (collectGeometryBreakdown)
            {
                var topModels = modelContributions!
                    .OrderByDescending(x => x.DrawCalls)
                    .ThenByDescending(x => x.Triangles)
                    .Take(5)
                    .ToArray();

                var topMaterials = materialContributions!
                    .Select(kvp => new PerfGeometryEntry(kvp.Key, kvp.Value.DrawCalls, kvp.Value.Triangles))
                    .OrderByDescending(x => x.DrawCalls)
                    .ThenByDescending(x => x.Triangles)
                    .Take(5)
                    .ToArray();

                lastPerfStats = new PerfFrameStats(lastAllocGeoBytes, PerfCounters.GetSnapshot(), new PerfGeometryBreakdown(topModels, topMaterials, totalModels, visibleModels, culledModels));
            }
        }

        private void LightingPass()
        {
            RenderSsao();
            ShadowFactorPass();
        }

        private void FinalPass()
        {
            RenderOptions.CameraNear = camera.NearPlane;
            RenderOptions.CameraFar = camera.FarPlane;
            gbuffer.Draw(ssaoBlurTexture, ssaoAvailable, shadowFactorTex, RenderOptions.EnableDirectionalShadows, camera.NearPlane, camera.FarPlane, camera.viewMat, camera.projMat, camera.Transform.Position);
        }

        private void TransparentPass()
        {
            RenderOptions.TransparentPass = true;
            Material.ResetTransparentBlendStateCache();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.Enable(EnableCap.DepthTest);
            GL.DepthMask(false);

            var frustum = new ViewFrustum(camera.viewMat * camera.projMat);

            foreach (var c in SceneGraph.Instance.GetRoot().children)
            {
                if (c is Model model)
                {
                    var (boundsCenter, boundsRadius) = model.GetApproximateWorldBounds();
                    if (!frustum.IntersectsSphere(boundsCenter, MathF.Max(boundsRadius, 0.01f)))
                    {
                        continue;
                    }

                    model.Draw(camera.viewMat, camera.projMat);
                }
            }

            GL.DepthMask(true);
            GL.Disable(EnableCap.Blend);
            RenderOptions.TransparentPass = false;
        }

        private void SkeletonPass()
        {
            if (!RenderOptions.ShowSkeleton)
            {
                return;
            }

            GL.Disable(EnableCap.DepthTest);
            GL.Disable(EnableCap.CullFace);

            var frustum = new ViewFrustum(camera.viewMat * camera.projMat);

            foreach (var c in SceneGraph.Instance.GetRoot().children)
            {
                if (c is Model model)
                {
                    var (boundsCenter, boundsRadius) = model.GetApproximateWorldBounds();
                    if (!frustum.IntersectsSphere(boundsCenter, MathF.Max(boundsRadius, 0.01f)))
                    {
                        continue;
                    }

                    model.DrawSkeleton(camera.viewMat, camera.projMat);
                }
            }

            GL.Enable(EnableCap.CullFace);
            GL.Enable(EnableCap.DepthTest);
        }

        private void GridPass()
        {
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GL.Enable(EnableCap.DepthTest);
            GL.DepthMask(true);

            foreach (var c in SceneGraph.Instance.GetRoot().children)
            {
                if (c is Grid grid)
                {
                    grid.Draw(camera.viewMat, camera.projMat);
                }
                else if (c is HeightFieldMesh hf)
                {
                    hf.Draw(camera.viewMat, camera.projMat);
                }
            }
        }

        private void OutlinePass()
        {
            RenderOptions.OutlinePass = true;
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GL.DepthMask(false);
            GL.Enable(EnableCap.DepthTest);
            GL.DepthFunc(DepthFunction.Lequal);

            foreach (var c in SceneGraph.Instance.GetRoot().children)
            {
                if (c is Model model)
                {
                    model.Draw(camera.viewMat, camera.projMat);
                }
            }

            GL.DepthFunc(DepthFunction.Lequal);
            GL.DepthMask(true);
            RenderOptions.OutlinePass = false;
        }

        private void ParticlePass()
        {
            RenderOptions.ParticlePass = true;
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GL.Enable(EnableCap.DepthTest);

            foreach (var c in SceneGraph.Instance.GetRoot().children)
            {
                if (c is IParticleObject)
                {
                    c.Draw(camera.viewMat, camera.projMat);
                }
            }

            RenderOptions.ParticlePass = false;
        }

    }
}
