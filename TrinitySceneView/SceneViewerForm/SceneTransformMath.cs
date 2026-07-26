using GFTool.Core.Flatbuffers.TR.Scene.Components;
using OpenTK.Mathematics;
using Trinity.Core.Utils;

namespace TrinitySceneView
{
    public static class SceneTransformMath
    {
        public static Matrix4 BuildSrtMatrix(trinity_Transform? srt)
        {
            if (srt == null)
            {
                return Matrix4.Identity;
            }

            var scale = srt.Scale != null
                ? new Vector3(srt.Scale.X, srt.Scale.Y, srt.Scale.Z)
                : Vector3.One;

            var rotateRadians = srt.Rotate != null
                ? new Vector3(srt.Rotate.X, srt.Rotate.Y, srt.Rotate.Z)
                : Vector3.Zero;

            var translate = srt.Translate != null
                ? new Vector3(srt.Translate.X, srt.Translate.Y, srt.Translate.Z)
                : Vector3.Zero;

            return BuildSrtMatrixRadians(scale, rotateRadians, translate);
        }

        public static bool TryBuildObjectTemplateInstanceMatrix(
            trinity_ObjectTemplate? objectTemplate,
            Matrix4 parentMatrix,
            out Matrix4 instanceMatrix,
            out string? sceneObjectName)
        {
            instanceMatrix = parentMatrix;
            sceneObjectName = null;

            if (!TryGetObjectTemplateSceneObject(objectTemplate, out var sceneObject) || sceneObject == null)
            {
                return false;
            }

            var localMatrix = BuildSrtMatrix(sceneObject.Srt);
            instanceMatrix =
                sceneObject.AttachTransform && sceneObject.KeepWorldSrt
                    ? localMatrix
                    : localMatrix * parentMatrix;
            sceneObjectName = sceneObject.Name;
            return true;
        }

        public static bool TryGetObjectTemplateSceneObject(
            trinity_ObjectTemplate? objectTemplate,
            out trinity_SceneObject? sceneObject)
        {
            sceneObject = null;

            if (objectTemplate?.EntityData == null ||
                objectTemplate.EntityData.Length == 0 ||
                !string.Equals(objectTemplate.EntityType, nameof(trinity_SceneObject), StringComparison.Ordinal))
            {
                return false;
            }

            try
            {
                sceneObject = FlatBufferConverter.DeserializeFrom<trinity_SceneObject>(objectTemplate.EntityData);
            }
            catch
            {
                return false;
            }

            return sceneObject != null;
        }

        public static bool ShouldLoadObjectTemplateFile(trinity_ObjectTemplate? objectTemplate)
        {
            return objectTemplate != null &&
                !objectTemplate.IsExpanded &&
                !string.IsNullOrWhiteSpace(objectTemplate.FilePath);
        }

        public static Matrix4 ApplyTrinsInstanceDefaultTransform(Matrix4 instanceMatrix)
        {
            return instanceMatrix;
        }

        public static Matrix4 ApplyZaWorldBakedFieldRootTransform(Matrix4 modelMatrix, string? modelPath)
        {
            if (!IsIdentityLike(modelMatrix) || !IsZaWorldBakedFieldRootModelPath(modelPath))
            {
                return modelMatrix;
            }

            return Matrix4.CreateRotationY(MathHelper.Pi) * modelMatrix;
        }

        public static Matrix4 GetSubSceneRootMatrix(Matrix4 callerMatrix)
        {
            return Matrix4.Identity;
        }

        public static Matrix4 BuildCombineLodEntityMatrix(trinity_CombineLODEntity combineLod)
        {
            var rotation = combineLod.Rotation != null
                ? new Vector3(combineLod.Rotation.X, combineLod.Rotation.Y, combineLod.Rotation.Z)
                : Vector3.Zero;
            var position = combineLod.Position != null
                ? new Vector3(combineLod.Position.X, combineLod.Position.Y, combineLod.Position.Z)
                : Vector3.Zero;

            return BuildSrtMatrix(Vector3.One, rotation, position);
        }

        public static Matrix4 BuildSrtMatrix(Vector3 scale, Vector3 rotateDegrees, Vector3 translate)
        {
            return
                Matrix4.CreateScale(scale) *
                Matrix4.CreateFromQuaternion(CreateTrinityEulerQuaternionDegrees(rotateDegrees)) *
                Matrix4.CreateTranslation(translate);
        }

        public static Matrix4 BuildSrtMatrixRadians(Vector3 scale, Vector3 rotateRadians, Vector3 translate)
        {
            return
                Matrix4.CreateScale(scale) *
                Matrix4.CreateFromQuaternion(CreateTrinityEulerQuaternionRadians(rotateRadians)) *
                Matrix4.CreateTranslation(translate);
        }

        public static Quaternion CreateTrinityEulerQuaternionDegrees(Vector3 rotateDegrees)
        {
            return CreateYawPitchRollQuaternion(
                MathHelper.DegreesToRadians(rotateDegrees.Y),
                MathHelper.DegreesToRadians(rotateDegrees.X),
                MathHelper.DegreesToRadians(rotateDegrees.Z));
        }

        public static Quaternion CreateTrinityEulerQuaternionRadians(Vector3 rotateRadians)
        {
            return CreateYawPitchRollQuaternion(
                rotateRadians.Y,
                rotateRadians.X,
                rotateRadians.Z);
        }

        private static Quaternion CreateYawPitchRollQuaternion(float yaw, float pitch, float roll)
        {
            float sy = MathF.Sin(yaw * 0.5f);
            float cy = MathF.Cos(yaw * 0.5f);
            float sp = MathF.Sin(pitch * 0.5f);
            float cp = MathF.Cos(pitch * 0.5f);
            float sr = MathF.Sin(roll * 0.5f);
            float cr = MathF.Cos(roll * 0.5f);

            var q = new Quaternion(
                cy * sp * cr + sy * cp * sr,
                sy * cp * cr - cy * sp * sr,
                cy * cp * sr - sy * sp * cr,
                cy * cp * cr + sy * sp * sr);
            q.Normalize();
            return q;
        }

        private static bool IsZaWorldBakedFieldRootModelPath(string? modelPath)
        {
            if (string.IsNullOrWhiteSpace(modelPath))
            {
                return false;
            }

            var segments = modelPath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 2 || !HasFieldModelPrefix(segments))
            {
                return false;
            }

            var parentName = segments[^2];
            var fileName = RemoveExtension(segments[^1]);
            if (!string.Equals(fileName, parentName, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(fileName, parentName + "_plants", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return
                (IsZaBlockRootName(parentName) && HasBlockCategorySegment(segments, parentName)) ||
                (IsZaGroundRootName(parentName) && HasGroundCategorySegment(segments, parentName)) ||
                (IsZaClodRootName(parentName) && HasClodCategorySegment(segments, parentName));
        }

        private static bool HasFieldModelPrefix(string[] segments)
        {
            for (var i = 0; i < segments.Length - 1; i++)
            {
                if (string.Equals(segments[i], "field", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(segments[i + 1], "model", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasBlockCategorySegment(string[] segments, string blockRootName)
        {
            var firstSeparator = blockRootName.IndexOf('_');
            if (firstSeparator <= 0)
            {
                return false;
            }

            var expectedCategory = blockRootName[..firstSeparator] + "_b";
            foreach (var segment in segments)
            {
                if (string.Equals(segment, expectedCategory, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsZaBlockRootName(string name)
        {
            var parts = name.Split('_');
            return parts.Length == 3 &&
                HasPrefixAndDigits(parts[0], 't') &&
                HasPrefixAndDigits(parts[1], 'b') &&
                IsAllDigits(parts[2]);
        }

        private static bool HasGroundCategorySegment(string[] segments, string groundRootName)
        {
            var firstSeparator = groundRootName.IndexOf('_');
            if (firstSeparator <= 0)
            {
                return false;
            }

            var expectedCategory = groundRootName[..firstSeparator] + "_g";
            foreach (var segment in segments)
            {
                if (string.Equals(segment, expectedCategory, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsZaGroundRootName(string name)
        {
            var parts = name.Split('_');
            return parts.Length == 3 &&
                HasPrefixAndDigits(parts[0], 't') &&
                HasPrefixAndDigits(parts[1], 'g') &&
                IsAllDigits(parts[2]);
        }

        private static bool HasClodCategorySegment(string[] segments, string clodRootName)
        {
            var firstSeparator = clodRootName.IndexOf('_');
            if (firstSeparator <= 0)
            {
                return false;
            }

            var expectedCategory = clodRootName[..firstSeparator] + "_clod";
            foreach (var segment in segments)
            {
                if (string.Equals(segment, expectedCategory, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsZaClodRootName(string name)
        {
            var parts = name.Split('_');
            return parts.Length == 3 &&
                HasPrefixAndDigits(parts[0], 't') &&
                HasPrefixAndDigits(parts[1], "clod") &&
                IsAllDigits(parts[2]);
        }

        private static bool HasPrefixAndDigits(string value, char prefix)
        {
            return value.Length > 1 &&
                char.ToLowerInvariant(value[0]) == prefix &&
                IsAllDigits(value[1..]);
        }

        private static bool HasPrefixAndDigits(string value, string prefix)
        {
            return value.Length > prefix.Length &&
                value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                IsAllDigits(value[prefix.Length..]);
        }

        private static bool IsAllDigits(string value)
        {
            if (value.Length == 0)
            {
                return false;
            }

            foreach (var ch in value)
            {
                if (!char.IsDigit(ch))
                {
                    return false;
                }
            }

            return true;
        }

        private static string RemoveExtension(string fileName)
        {
            var extensionStart = fileName.LastIndexOf('.');
            return extensionStart >= 0 ? fileName[..extensionStart] : fileName;
        }

        private static bool IsIdentityLike(Matrix4 matrix)
        {
            return
                IsNear(matrix.M11, 1.0f) &&
                IsNear(matrix.M12, 0.0f) &&
                IsNear(matrix.M13, 0.0f) &&
                IsNear(matrix.M14, 0.0f) &&
                IsNear(matrix.M21, 0.0f) &&
                IsNear(matrix.M22, 1.0f) &&
                IsNear(matrix.M23, 0.0f) &&
                IsNear(matrix.M24, 0.0f) &&
                IsNear(matrix.M31, 0.0f) &&
                IsNear(matrix.M32, 0.0f) &&
                IsNear(matrix.M33, 1.0f) &&
                IsNear(matrix.M34, 0.0f) &&
                IsNear(matrix.M41, 0.0f) &&
                IsNear(matrix.M42, 0.0f) &&
                IsNear(matrix.M43, 0.0f) &&
                IsNear(matrix.M44, 1.0f);
        }

        private static bool IsNear(float actual, float expected)
        {
            return MathF.Abs(actual - expected) <= 0.0001f;
        }
    }
}
