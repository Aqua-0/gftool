using System;
using GFTool.Core.Flatbuffers.TR.Scene.Components;
using OpenTK.Mathematics;

namespace TrinitySceneView
{
    public partial class SceneViewerForm
    {
        private static Matrix4 BuildSrtMatrix(trinity_Transform? srt)
        {
            if (srt == null)
            {
                return Matrix4.Identity;
            }

            var scale = srt.Scale != null
                ? new Vector3(srt.Scale.X, srt.Scale.Y, srt.Scale.Z)
                : Vector3.One;

            Quaternion rot = Quaternion.Identity;
            if (srt.Rotate != null)
            {
                rot = new Quaternion(srt.Rotate.X, srt.Rotate.Y, srt.Rotate.Z, srt.Rotate.W);
                rot.Normalize();
            }

            var trans = srt.Translate != null
                ? new Vector3(srt.Translate.X, srt.Translate.Y, srt.Translate.Z)
                : Vector3.Zero;

            return
                Matrix4.CreateTranslation(trans) *
                Matrix4.CreateFromQuaternion(rot) *
                Matrix4.CreateScale(scale);
        }

        private static Matrix4 BuildSpawnerTransformMatrix(SpawnerTransform t)
        {
            var pos = new Vector3(t.X, t.Y, t.Z);

            float rx = MathHelper.DegreesToRadians(t.RotX);
            float ry = MathHelper.DegreesToRadians(t.RotY);
            float rz = MathHelper.DegreesToRadians(t.RotZ);

            var q = Quaternion.FromEulerAngles(rx, ry, rz);
            q.Normalize();

            return Matrix4.CreateTranslation(pos) * Matrix4.CreateFromQuaternion(q);
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
