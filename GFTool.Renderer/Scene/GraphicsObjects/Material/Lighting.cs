using GFTool.Core.Utils;
using GFTool.Renderer.Core;
using GFTool.Renderer.Core.Graphics;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System;
using System.Globalization;
using System.Drawing;
using System.Linq;
using Trinity.Core.Flatbuffers.TR.Model;
using Trinity.Core.Flatbuffers.Utils;
using Trinity.Core.Assets;

namespace GFTool.Renderer.Scene.GraphicsObjects
{
    public partial class Material : IDisposable
    {
        private void SetLightingUniforms(Shader activeShader, Matrix4 view)
        {
            Matrix4.Invert(view, out var inverseView);
            var cameraPos = inverseView.ExtractTranslation();
            activeShader.SetVector3IfExists("CameraPos", cameraPos);

            var lightDirection = RenderOptions.WorldLightDirection;
            activeShader.SetVector3IfExists("LightDirection", lightDirection);
            activeShader.SetVector3IfExists("LightColor", new Vector3(0.95f, 0.95f, 0.95f));
            activeShader.SetVector3IfExists("AmbientColor", new Vector3(0.18f, 0.18f, 0.18f));
            activeShader.SetBoolIfExists("TwoSidedDiffuse", true);
            activeShader.SetFloatIfExists("LightWrap", RenderOptions.LightWrap);
            activeShader.SetFloatIfExists("SpecularScale", RenderOptions.SpecularScale);
            activeShader.SetFloatIfExists("LensOpacity", RenderOptions.LensOpacity);
            activeShader.SetFloatIfExists("AlphaTestThreshold", 0.5f);
            activeShader.SetBoolIfExists("LegacyMode", RenderOptions.LegacyMode);
            activeShader.SetIntIfExists("DebugShaderMode", RenderOptions.ShaderDebugMode);

            activeShader.SetFloatIfExists("CameraNear", RenderOptions.CameraNear);
            activeShader.SetFloatIfExists("CameraFar", RenderOptions.CameraFar);
            activeShader.SetVector2IfExists("ScreenSize", new Vector2(Math.Max(1, RenderOptions.RenderTargetWidth), Math.Max(1, RenderOptions.RenderTargetHeight)));
            activeShader.SetFloatIfExists("EnvMaxLod", RenderOptions.EnvMaxLod);
            activeShader.SetFloatIfExists("EnvIntensity", RenderOptions.EnvIntensity);

            float seconds = (float)(Environment.TickCount64 / 1000.0);
            activeShader.SetVector4IfExists("time_params", new Vector4(seconds, 0.0f, 0.0f, 0.0f));

            activeShader.SetBoolIfExists("EnableTeraEffect", RenderOptions.EnableTeraEffect);
            activeShader.SetVector3IfExists("TeraColor", RenderOptions.TeraColor);
            activeShader.SetFloatIfExists("TeraStrength", RenderOptions.TeraStrength);
        }
    }
}
