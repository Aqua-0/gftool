using OpenTK.Mathematics;
using System;
using System.Collections.Generic;

namespace TrinitySceneView
{
    public partial class SceneViewerForm
    {
        private sealed class EventActorTurn
        {
            public float StartYawDeg;
            public float EndYawDeg;
            public DateTime StartUtc;
            public DateTime EndUtc;
        }

        private sealed class EventActorMove
        {
            public Vector3 StartPos;
            public Vector3 EndPos;
            public DateTime StartUtc;
            public DateTime EndUtc;
        }

        private readonly Dictionary<string, EventActorMove> eventActorMoves = new(StringComparer.Ordinal);
        private readonly Dictionary<string, EventActorTurn> eventActorTurns = new(StringComparer.Ordinal);

        private void ResetActorMoves()
        {
            eventActorMoves.Clear();
            eventActorTurns.Clear();
        }

        private void ApplyFObjMoveToPosXYZ(List<object?> args, bool snapToEnd)
        {
            // FObj_Move_To_PosXYZ(actor, x, y, z, seconds)
            if (args.Count < 5)
            {
                return;
            }

            var actor = args[0] as string;
            if (string.IsNullOrWhiteSpace(actor))
            {
                return;
            }

            if (!TryGetFloat(args[1], out var x) ||
                !TryGetFloat(args[2], out var y) ||
                !TryGetFloat(args[3], out var z))
            {
                return;
            }

            float seconds = 0.0f;
            TryGetFloat(args[4], out seconds);

            var end = new Vector3(x, y, z);
            var s = GetOrCreateActorState(actor);

            if (snapToEnd || seconds <= 0.0001f)
            {
                s.Position = end;
                eventActorMoves.Remove(actor);
                return;
            }

            var now = DateTime.UtcNow;
            eventActorMoves[actor] = new EventActorMove
            {
                StartPos = s.Position,
                EndPos = end,
                StartUtc = now,
                EndUtc = now.AddSeconds(seconds)
            };
        }

        private void ApplyFObjSetTurn(List<object?> args, bool snapToEnd)
        {
            // FObj_Set_Turn(actor, yawDeg, immediate?)
            if (args.Count < 2)
            {
                return;
            }

            var actor = args[0] as string;
            if (string.IsNullOrWhiteSpace(actor) || !TryGetFloat(args[1], out var yawDeg))
            {
                return;
            }

            bool immediate = false;
            if (args.Count > 2 && args[2] is bool b)
            {
                immediate = b;
            }

            if (snapToEnd || immediate)
            {
                var s = GetOrCreateActorState(actor);
                s.RotationDegrees = new Vector3(s.RotationDegrees.X, yawDeg, s.RotationDegrees.Z);
                eventActorTurns.Remove(actor);
                return;
            }

            StartActorTurn(actor, yawDeg);
        }

        private void ApplyFObjSetTurnToObj(List<object?> args, bool snapToEnd)
        {
            // FObj_Set_Turn_To_Obj(actor, targetActor, seconds?)
            if (args.Count < 2)
            {
                return;
            }

            var actor = args[0] as string;
            var target = args[1] as string;
            if (string.IsNullOrWhiteSpace(actor) || string.IsNullOrWhiteSpace(target))
            {
                return;
            }

            if (!eventActorStates.TryGetValue(actor, out var a) ||
                !eventActorStates.TryGetValue(target, out var t))
            {
                return;
            }

            if (!TryComputeYawToTarget(a.Position, t.Position, out var yawDeg))
            {
                return;
            }

            float seconds = -1.0f;
            if (args.Count > 2)
            {
                TryGetFloat(args[2], out seconds);
            }

            if (snapToEnd || seconds <= 0.0001f)
            {
                a.RotationDegrees = new Vector3(a.RotationDegrees.X, yawDeg, a.RotationDegrees.Z);
                eventActorTurns.Remove(actor);
                return;
            }

            StartActorTurn(actor, yawDeg, seconds);
        }

        private void ApplyFObjLookAtPos(List<object?> args, bool snapToEnd)
        {
            // FObj_LookAt_Pos(actor, pos{3})
            if (args.Count < 2)
            {
                return;
            }

            var actor = args[0] as string;
            if (string.IsNullOrWhiteSpace(actor))
            {
                return;
            }

            if (!TryGetVec3(args[1], out var pos))
            {
                return;
            }

            var s = GetOrCreateActorState(actor);
            if (!TryComputeYawToTarget(s.Position, pos, out var yawDeg))
            {
                return;
            }

            if (snapToEnd)
            {
                s.RotationDegrees = new Vector3(s.RotationDegrees.X, yawDeg, s.RotationDegrees.Z);
                eventActorTurns.Remove(actor);
                return;
            }

            StartActorTurn(actor, yawDeg);
        }

        private void StartActorTurn(string actor, float targetYawDeg, float secondsOverride = -1.0f)
        {
            const float turnSpeedDegPerSec = 180.0f;

            var s = GetOrCreateActorState(actor);
            float start = NormalizeYawDeg(s.RotationDegrees.Y);
            float end = NormalizeYawDeg(targetYawDeg);

            float delta = DeltaAngleDeg(start, end);
            float seconds = secondsOverride > 0.0001f ? secondsOverride : Math.Clamp(MathF.Abs(delta) / turnSpeedDegPerSec, 0.05f, 5.0f);

            var now = DateTime.UtcNow;
            eventActorTurns[actor] = new EventActorTurn
            {
                StartYawDeg = start,
                EndYawDeg = end,
                StartUtc = now,
                EndUtc = now.AddSeconds(seconds)
            };
        }

        private static bool TryComputeYawToTarget(Vector3 from, Vector3 to, out float yawDeg)
        {
            yawDeg = 0.0f;
            var dir = to - from;
            dir.Y = 0;
            if (dir.LengthSquared < 0.00001f)
            {
                return false;
            }

            yawDeg = MathHelper.RadiansToDegrees(MathF.Atan2(dir.Z, dir.X));
            return true;
        }

        private static float NormalizeYawDeg(float yawDeg)
        {
            yawDeg %= 360f;
            if (yawDeg > 180f) yawDeg -= 360f;
            if (yawDeg < -180f) yawDeg += 360f;
            return yawDeg;
        }

        private static float DeltaAngleDeg(float fromDeg, float toDeg)
        {
            float delta = NormalizeYawDeg(toDeg - fromDeg);
            return delta;
        }

        private static float LerpAngleDeg(float fromDeg, float toDeg, float t)
        {
            float delta = DeltaAngleDeg(fromDeg, toDeg);
            return NormalizeYawDeg(fromDeg + (delta * t));
        }

        private void UpdateActorMovesForRender(DateTime nowUtc)
        {
            if (eventActorMoves.Count == 0)
            {
                UpdateActorTurnsForRender(nowUtc);
                return;
            }

            var toRemove = new List<string>();
            foreach (var kvp in eventActorMoves)
            {
                var actorId = kvp.Key;
                var move = kvp.Value;
                if (move == null)
                {
                    toRemove.Add(actorId);
                    continue;
                }

                var s = GetOrCreateActorState(actorId);
                if (nowUtc >= move.EndUtc)
                {
                    s.Position = move.EndPos;
                    toRemove.Add(actorId);
                    continue;
                }

                double total = (move.EndUtc - move.StartUtc).TotalSeconds;
                if (total <= 0.0001)
                {
                    s.Position = move.EndPos;
                    toRemove.Add(actorId);
                    continue;
                }

                double t = (nowUtc - move.StartUtc).TotalSeconds / total;
                t = Math.Clamp(t, 0.0, 1.0);
                s.Position = Vector3.Lerp(move.StartPos, move.EndPos, (float)t);
            }

            foreach (var id in toRemove)
            {
                eventActorMoves.Remove(id);
            }

            UpdateActorTurnsForRender(nowUtc);
        }

        private void UpdateActorTurnsForRender(DateTime nowUtc)
        {
            if (eventActorTurns.Count == 0)
            {
                return;
            }

            var toRemove = new List<string>();
            foreach (var kvp in eventActorTurns)
            {
                var actorId = kvp.Key;
                var turn = kvp.Value;
                if (turn == null)
                {
                    toRemove.Add(actorId);
                    continue;
                }

                var s = GetOrCreateActorState(actorId);
                if (nowUtc >= turn.EndUtc)
                {
                    s.RotationDegrees = new Vector3(s.RotationDegrees.X, turn.EndYawDeg, s.RotationDegrees.Z);
                    toRemove.Add(actorId);
                    continue;
                }

                double total = (turn.EndUtc - turn.StartUtc).TotalSeconds;
                if (total <= 0.0001)
                {
                    s.RotationDegrees = new Vector3(s.RotationDegrees.X, turn.EndYawDeg, s.RotationDegrees.Z);
                    toRemove.Add(actorId);
                    continue;
                }

                double t = (nowUtc - turn.StartUtc).TotalSeconds / total;
                t = Math.Clamp(t, 0.0, 1.0);
                float yaw = LerpAngleDeg(turn.StartYawDeg, turn.EndYawDeg, (float)t);
                s.RotationDegrees = new Vector3(s.RotationDegrees.X, yaw, s.RotationDegrees.Z);
            }

            foreach (var id in toRemove)
            {
                eventActorTurns.Remove(id);
            }
        }

        private bool TryGetNextActorActionDoneUtc(DateTime nowUtc, out DateTime doneUtc)
        {
            doneUtc = default;
            if (eventActorMoves.Count == 0 && eventActorTurns.Count == 0)
            {
                return false;
            }

            DateTime best = DateTime.MaxValue;
            foreach (var move in eventActorMoves.Values)
            {
                if (move == null)
                {
                    continue;
                }

                if (move.EndUtc > nowUtc && move.EndUtc < best)
                {
                    best = move.EndUtc;
                }
            }

            foreach (var turn in eventActorTurns.Values)
            {
                if (turn == null)
                {
                    continue;
                }

                if (turn.EndUtc > nowUtc && turn.EndUtc < best)
                {
                    best = turn.EndUtc;
                }
            }

            if (best == DateTime.MaxValue)
            {
                return false;
            }

            doneUtc = best;
            return true;
        }
    }
}
