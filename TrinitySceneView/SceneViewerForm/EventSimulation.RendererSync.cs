using OpenTK.Mathematics;
using System;

namespace TrinitySceneView
{
    public partial class SceneViewerForm
    {
        private void ApplyEventSimulationToRenderer()
        {
            if (renderCtrl?.renderer == null)
            {
                return;
            }

            UpdateActorMovesForRender(DateTime.UtcNow);

            foreach (var kvp in eventActorStates)
            {
                if (!eventActorModels.TryGetValue(kvp.Key, out var models) || models.Count == 0)
                {
                    continue;
                }

                var s = kvp.Value;
                var actorMat = BuildTransformMatrix(s.Position, s.RotationDegrees);
                foreach (var m in models)
                {
                    try
                    {
                        m.Model.SetVisible(s.Visible);
                        var mat = ApplyViewerMatrixOptions(
                            m.LocalMatrix * actorMat,
                            config.SpawnModelsAtOrigin,
                            config.ApplySceneRotationToActors && config.RotateModels180X,
                            config.ApplySceneRotationToActors && config.RotateModels180Y,
                            out _,
                            out _);

                        if (config.RotateActors180X)
                        {
                            mat = mat * Matrix4.CreateRotationX(MathHelper.Pi);
                        }
                        if (config.RotateActors180Y)
                        {
                            mat = mat * Matrix4.CreateRotationY(MathHelper.Pi);
                        }

                        m.Model.SetModelMatrix(mat);
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }

            if (eventCameraGizmo != null)
            {
                var camPos = eventCameraPos;
                var camRot = eventCameraRotDeg;
                if (config.ApplySceneRotationToEventCamera && config.RotateModels180X)
                {
                    camPos = SceneCoordinateConverter.ApplyRotate180X(camPos);
                    camRot = SceneCoordinateConverter.ConvertEventCameraEulerDegreesForRotate180X(camRot);
                }
                if (config.ApplySceneRotationToEventCamera && config.RotateModels180Y)
                {
                    camPos = SceneCoordinateConverter.ApplyRotate180Y(camPos);
                    camRot = SceneCoordinateConverter.ConvertEventCameraEulerDegreesForRotate180Y(camRot);
                }

                eventCameraGizmo.Transform.Position = camPos;
                eventCameraGizmo.Transform.Rotation = Quaternion.FromEulerAngles(
                    MathHelper.DegreesToRadians(camRot.X),
                    MathHelper.DegreesToRadians(camRot.Y),
                    0f);
                eventCameraGizmo.SetFrustum(eventCameraFovDeg, 16.0f / 9.0f);
            }

            if (eventUseCameraCheckBox?.Checked == true)
            {
                var camPos = eventCameraPos;
                var camRot = eventCameraRotDeg;
                if (config.ApplySceneRotationToEventCamera && config.RotateModels180X)
                {
                    camPos = SceneCoordinateConverter.ApplyRotate180X(camPos);
                    camRot = SceneCoordinateConverter.ConvertEventCameraEulerDegreesForRotate180X(camRot);
                }
                if (config.ApplySceneRotationToEventCamera && config.RotateModels180Y)
                {
                    camPos = SceneCoordinateConverter.ApplyRotate180Y(camPos);
                    camRot = SceneCoordinateConverter.ConvertEventCameraEulerDegreesForRotate180Y(camRot);
                }
                renderCtrl.renderer.SetCameraPoseFromEulerDegrees(camPos, camRot, eventCameraFovDeg);
            }

            renderCtrl.Invalidate();
        }
    }
}
