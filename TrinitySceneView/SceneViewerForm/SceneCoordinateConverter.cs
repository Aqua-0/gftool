using OpenTK.Mathematics;
using System;

namespace TrinitySceneView
{
    internal static class SceneCoordinateConverter
    {
        public static Vector3 ApplyRotate180X(Vector3 v)
        {
            return new Vector3(v.X, -v.Y, -v.Z);
        }

        public static Vector3 ApplyRotate180Y(Vector3 v)
        {
            return new Vector3(-v.X, v.Y, -v.Z);
        }

        public static Vector3 ConvertEventCameraEulerDegreesForRotate180X(Vector3 eulerDegrees)
        {
            // TSV camera uses pitch=X and yaw=Y (roll ignored).
            float pitchRad = MathHelper.DegreesToRadians(eulerDegrees.X);
            float yawRad = MathHelper.DegreesToRadians(eulerDegrees.Y);

            var front = FrontFromAngles(yawRad, pitchRad);
            front = ApplyRotate180X(front);

            float yaw2 = MathF.Atan2(front.Z, front.X);
            float pitch2 = MathF.Asin(Math.Clamp(front.Y, -1f, 1f));

            return new Vector3(MathHelper.RadiansToDegrees(pitch2), MathHelper.RadiansToDegrees(yaw2), 0f);
        }

        public static Vector3 ConvertEventCameraEulerDegreesForRotate180Y(Vector3 eulerDegrees)
        {
            float pitchRad = MathHelper.DegreesToRadians(eulerDegrees.X);
            float yawRad = MathHelper.DegreesToRadians(eulerDegrees.Y);

            var front = FrontFromAngles(yawRad, pitchRad);
            front = ApplyRotate180Y(front);

            float yaw2 = MathF.Atan2(front.Z, front.X);
            float pitch2 = MathF.Asin(Math.Clamp(front.Y, -1f, 1f));

            return new Vector3(MathHelper.RadiansToDegrees(pitch2), MathHelper.RadiansToDegrees(yaw2), 0f);
        }

        private static Vector3 FrontFromAngles(float yawRad, float pitchRad)
        {
            Vector3 front;
            front.X = (float)(Math.Cos(yawRad) * Math.Cos(pitchRad));
            front.Y = (float)(Math.Sin(pitchRad));
            front.Z = (float)(Math.Sin(yawRad) * Math.Cos(pitchRad));
            return Vector3.Normalize(front);
        }
    }
}
