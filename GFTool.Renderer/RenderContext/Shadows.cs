using GFTool.Renderer.Core;
using GFTool.Renderer.Core.Graphics;
using GFTool.Renderer.Scene;
using GFTool.Renderer.Scene.GraphicsObjects;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System;
using System.Linq;

namespace GFTool.Renderer
{
    public partial class RenderContext : IDisposable
    {
        private int shadowMapFbo;
        private int shadowMapTex;
        private int shadowFactorFbo;
        private int shadowFactorTex;
        private bool shadowAvailable;

        private readonly Matrix4[] shadowMatrices = new Matrix4[4];
        private readonly float[] cascadeSplits = new float[4];

        private void CreateShadowTargets(int width, int height)
        {
            DeleteShadowTargets();

            int cascades = Math.Clamp(RenderOptions.ShadowCascadeCount, 1, 4);
            int res = Math.Clamp(RenderOptions.ShadowMapResolution, 256, 8192);

            GL.GenFramebuffers(1, out shadowMapFbo);

            shadowMapTex = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2DArray, shadowMapTex);
            GL.TexImage3D(TextureTarget.Texture2DArray, 0, PixelInternalFormat.DepthComponent32f, res, res, cascades, 0, PixelFormat.DepthComponent, PixelType.Float, IntPtr.Zero);
            GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureCompareMode, (int)TextureCompareMode.CompareRefToTexture);
            GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureCompareFunc, (int)All.Lequal);

            GL.GenFramebuffers(1, out shadowFactorFbo);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, shadowFactorFbo);
            shadowFactorTex = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, shadowFactorTex);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.R16f, Math.Max(1, width), Math.Max(1, height), 0, PixelFormat.Red, PixelType.Float, IntPtr.Zero);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, shadowFactorTex, 0);
            GL.DrawBuffer(DrawBufferMode.ColorAttachment0);
            GL.ReadBuffer(ReadBufferMode.ColorAttachment0);

            if (GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != FramebufferErrorCode.FramebufferComplete)
            {
                shadowAvailable = false;
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
                return;
            }

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            shadowAvailable = true;
        }

        private void DeleteShadowTargets()
        {
            if (shadowMapFbo != 0)
            {
                GL.DeleteFramebuffer(shadowMapFbo);
                shadowMapFbo = 0;
            }
            if (shadowMapTex != 0)
            {
                GL.DeleteTexture(shadowMapTex);
                shadowMapTex = 0;
            }
            if (shadowFactorFbo != 0)
            {
                GL.DeleteFramebuffer(shadowFactorFbo);
                shadowFactorFbo = 0;
            }
            if (shadowFactorTex != 0)
            {
                GL.DeleteTexture(shadowFactorTex);
                shadowFactorTex = 0;
            }
            shadowAvailable = false;
        }

        private void ShadowMapPass()
        {
            if (!RenderOptions.EnableDirectionalShadows) return;
            if (!shadowAvailable) return;
            if (shadowMapFbo == 0 || shadowMapTex == 0) return;

            var shader = ShaderPool.Instance.GetShader("ShadowDepth");
            if (shader == null) return;

            int cascades = Math.Clamp(RenderOptions.ShadowCascadeCount, 1, 4);
            int res = Math.Clamp(RenderOptions.ShadowMapResolution, 256, 8192);

            float near = camera.NearPlane;
            float maxDist = Math.Max(near + 0.1f, RenderOptions.ShadowMaxDistance);
            float lambda = Math.Clamp(RenderOptions.ShadowCascadeLambda, 0.0f, 1.0f);
            float ratio = maxDist / Math.Max(near, 0.001f);

            for (int i = 0; i < 4; i++)
                cascadeSplits[i] = maxDist;

            for (int i = 0; i < cascades; i++)
            {
                float p = (i + 1) / (float)cascades;
                float log = near * MathF.Pow(ratio, p);
                float uni = near + (maxDist - near) * p;
                cascadeSplits[i] = uni * (1.0f - lambda) + log * lambda;
            }

            Matrix4.Invert(camera.viewMat, out var invView);
            float tanHalfFovY = 1.0f / Math.Max(0.0001f, camera.projMat.M22);
            float tanHalfFovX = 1.0f / Math.Max(0.0001f, camera.projMat.M11);

            Vector3 lightDir = (-RenderOptions.WorldLightDirection).Normalized();
            Vector3 up = MathF.Abs(Vector3.Dot(lightDir, Vector3.UnitY)) > 0.95f ? Vector3.UnitZ : Vector3.UnitY;

            static Vector4 Mul(Matrix4 m, Vector4 v)
            {
                return new Vector4(
                    m.M11 * v.X + m.M12 * v.Y + m.M13 * v.Z + m.M14 * v.W,
                    m.M21 * v.X + m.M22 * v.Y + m.M23 * v.Z + m.M24 * v.W,
                    m.M31 * v.X + m.M32 * v.Y + m.M33 * v.Z + m.M34 * v.W,
                    m.M41 * v.X + m.M42 * v.Y + m.M43 * v.Z + m.M44 * v.W);
            }

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, shadowMapFbo);
            GL.Viewport(0, 0, res, res);
            GL.DrawBuffer(DrawBufferMode.None);
            GL.ReadBuffer(ReadBufferMode.None);
            GL.Enable(EnableCap.DepthTest);
            GL.DepthFunc(DepthFunction.Lequal);
            GL.Disable(EnableCap.Blend);
            GL.Enable(EnableCap.CullFace);
            GL.CullFace(CullFaceMode.Front);
            GL.Enable(EnableCap.PolygonOffsetFill);
            GL.PolygonOffset(2.0f, 4.0f);

            float prevSplit = near;
            for (int c = 0; c < cascades; c++)
            {
                float split = cascadeSplits[c];

                Vector3[] cornersWs = new Vector3[8];
                int idx = 0;
                void AddCorner(float sx, float sy, float dist)
                {
                    float x = sx * dist * tanHalfFovX;
                    float y = sy * dist * tanHalfFovY;
                    float z = -dist;
                    var v = new Vector4(x, y, z, 1.0f);
                    var w = Mul(invView, v);
                    cornersWs[idx++] = w.Xyz / MathF.Max(0.00001f, w.W);
                }

                AddCorner(-1, -1, prevSplit);
                AddCorner(1, -1, prevSplit);
                AddCorner(1, 1, prevSplit);
                AddCorner(-1, 1, prevSplit);
                AddCorner(-1, -1, split);
                AddCorner(1, -1, split);
                AddCorner(1, 1, split);
                AddCorner(-1, 1, split);

                Vector3 center = Vector3.Zero;
                for (int i = 0; i < 8; i++) center += cornersWs[i];
                center /= 8.0f;

                float radius = 0.0f;
                for (int i = 0; i < 8; i++)
                    radius = MathF.Max(radius, (cornersWs[i] - center).Length);
                radius = MathF.Max(radius, 1.0f);

                Vector3 lightPos = center - lightDir * (radius + 50.0f);
                var lightView = Matrix4.LookAt(lightPos, center, up);

                float minX = float.PositiveInfinity, minY = float.PositiveInfinity, minZ = float.PositiveInfinity;
                float maxX = float.NegativeInfinity, maxY = float.NegativeInfinity, maxZ = float.NegativeInfinity;
                for (int i = 0; i < 8; i++)
                {
                    var ls = Mul(lightView, new Vector4(cornersWs[i], 1.0f));
                    minX = MathF.Min(minX, ls.X);
                    minY = MathF.Min(minY, ls.Y);
                    minZ = MathF.Min(minZ, ls.Z);
                    maxX = MathF.Max(maxX, ls.X);
                    maxY = MathF.Max(maxY, ls.Y);
                    maxZ = MathF.Max(maxZ, ls.Z);
                }

                float extentX = maxX - minX;
                float extentY = maxY - minY;
                float unitsPerTexelX = extentX / res;
                float unitsPerTexelY = extentY / res;
                if (unitsPerTexelX > 0 && unitsPerTexelY > 0)
                {
                    minX = MathF.Floor(minX / unitsPerTexelX) * unitsPerTexelX;
                    maxX = minX + extentX;
                    minY = MathF.Floor(minY / unitsPerTexelY) * unitsPerTexelY;
                    maxY = minY + extentY;
                }

                float nearPlane = -maxZ - 200.0f;
                float farPlane = -minZ + 200.0f;
                nearPlane = MathF.Max(0.001f, nearPlane);
                farPlane = MathF.Max(nearPlane + 0.01f, farPlane);

                var lightProj = Matrix4.CreateOrthographicOffCenter(minX, maxX, minY, maxY, nearPlane, farPlane);
                shadowMatrices[c] = lightProj * lightView;

                GL.FramebufferTextureLayer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, shadowMapTex, 0, c);
                GL.Clear(ClearBufferMask.DepthBufferBit);

                foreach (var n in SceneGraph.Instance.GetRoot().children.OfType<Model>())
                {
                    n.DrawShadow(lightView, lightProj);
                }

                prevSplit = split;
            }

            GL.Disable(EnableCap.PolygonOffsetFill);
            GL.CullFace(CullFaceMode.Back);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        }

        private void ShadowFactorPass()
        {
            if (!RenderOptions.EnableDirectionalShadows) return;
            if (!shadowAvailable) return;
            if (shadowFactorFbo == 0 || shadowFactorTex == 0 || shadowMapTex == 0) return;
            if (gbuffer == null) return;

            var shader = ShaderPool.Instance.GetShader("ShadowFactor");
            if (shader == null) return;

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, shadowFactorFbo);
            GL.Viewport(0, 0, RenderOptions.RenderTargetWidth, RenderOptions.RenderTargetHeight);
            GL.Disable(EnableCap.DepthTest);
            GL.DepthMask(false);
            GL.Clear(ClearBufferMask.ColorBufferBit);

            shader.Bind();

            Matrix4.Invert(camera.viewMat, out var invView);
            Matrix4.Invert(camera.projMat, out var invProj);
            shader.SetMatrix4("InvView", invView);
            shader.SetMatrix4("InvProjection", invProj);
            shader.SetMatrix4("View", camera.viewMat);
            shader.SetMatrix4("Projection", camera.projMat);
            shader.SetVector3("CameraPos", camera.Transform.Position);
            shader.SetVector3("LightDirection", RenderOptions.WorldLightDirection);

            var splits = new Vector4(
                cascadeSplits[0],
                cascadeSplits[1],
                cascadeSplits[2],
                RenderOptions.ShadowMaxDistance);
            shader.SetVector4("CascadeSplits", splits);
            shader.SetMatrix4ArrayIfExists("ShadowMatrices", shadowMatrices);
            shader.SetFloat("ShadowDepthBias", RenderOptions.ShadowDepthBias);
            shader.SetFloat("ShadowNormalBias", RenderOptions.ShadowNormalBias);
            shader.SetFloat("ShadowPcfRadius", RenderOptions.ShadowPcfRadius);

            int cascades = Math.Clamp(RenderOptions.ShadowCascadeCount, 1, 4);
            int res = Math.Clamp(RenderOptions.ShadowMapResolution, 256, 8192);
            shader.SetVector2("ShadowMapTexelSize", new Vector2(1.0f / res, 1.0f / res));

            shader.SetBool("EnableScreenSpaceShadows", RenderOptions.EnableScreenSpaceShadows);
            shader.SetInt("ScreenSpaceShadowSteps", Math.Clamp(RenderOptions.ScreenSpaceShadowSteps, 1, 64));
            shader.SetFloat("ScreenSpaceShadowStepSize", RenderOptions.ScreenSpaceShadowStepSize);
            shader.SetFloat("ScreenSpaceShadowThickness", RenderOptions.ScreenSpaceShadowThickness);
            shader.SetFloat("CameraNear", camera.NearPlane);
            shader.SetFloat("CameraFar", camera.FarPlane);

            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, gbuffer.GetTexture(GBuffer.GBufferType.GBUFFER_TYPE_NORMAL));
            shader.SetInt("normalTexture", 0);

            GL.ActiveTexture(TextureUnit.Texture1);
            GL.BindTexture(TextureTarget.Texture2D, gbuffer.DepthTexture);
            shader.SetInt("depthTexture", 1);

            GL.ActiveTexture(TextureUnit.Texture2);
            GL.BindTexture(TextureTarget.Texture2DArray, shadowMapTex);
            shader.SetInt("shadowMap", 2);

            gbuffer.RenderFullscreenQuad();

            shader.Unbind();
            GL.DepthMask(true);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        }
    }
}
