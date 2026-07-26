using System;
using GFTool.Core.Flatbuffers.TR.Scene.Components;
using OpenTK.Mathematics;

namespace TrinitySceneView
{
    public partial class SceneViewerForm
    {
        private static Matrix4 BuildSrtMatrix(trinity_Transform? srt)
        {
            return SceneTransformMath.BuildSrtMatrix(srt);
        }

        private static Matrix4 BuildSpawnerTransformMatrix(SpawnerTransform t)
        {
            return SceneTransformMath.BuildSrtMatrix(
                Vector3.One,
                new Vector3(t.RotX, t.RotY, t.RotZ),
                new Vector3(t.X, t.Y, t.Z));
        }

        private static string NormalizeSpawnerId(string id)
        {
            if (id.StartsWith("id_", StringComparison.Ordinal))
            {
                return id[3..];
            }
            return id;
        }
    }
}
