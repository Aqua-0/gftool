using GFTool.Renderer.Core;
using GFTool.Renderer.Scene.GraphicsObjects;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Forms;

namespace TrinitySceneView
{
	    public partial class SceneViewerForm
	    {
	        private sealed record EventActorModel(Model Model, Matrix4 LocalMatrix);

	        private sealed class EventActorState
	        {
	            public Vector3 Position;
	            public Vector3 RotationDegrees;
	            public bool Visible = true;
	        }

	        private readonly Dictionary<string, EventActorState> eventActorStates = new(StringComparer.Ordinal);
	        private readonly Dictionary<string, List<EventActorModel>> eventActorModels = new(StringComparer.Ordinal);
	        private readonly Dictionary<string, List<string>> eventActorSourceModelRels = new(StringComparer.Ordinal);
	        private readonly Dictionary<string, string> eventActorMotionDirRels = new(StringComparer.Ordinal);
	        private readonly Dictionary<string, GFTool.Renderer.Scene.GraphicsObjects.Animation> eventAnimationCacheByAbsPath = new(StringComparer.OrdinalIgnoreCase);
	        private readonly Dictionary<string, string> eventDynamicActorToNpcAssetId = new(StringComparer.Ordinal);
	        private readonly Dictionary<string, int> eventDynamicActorToPokemonNo = new(StringComparer.Ordinal);
	        private readonly Dictionary<string, (int Form, int Gender)> eventDynamicActorToPokemonVariant = new(StringComparer.Ordinal);

        private Vector3 eventCameraPos;
        private Vector3 eventCameraRotDeg;
        private float eventCameraFovDeg = 45.0f;
        private CameraGizmo? eventCameraGizmo;
        private DateTime? eventPlaybackBlockedUntilUtc;
        private DateTime? eventCameraMoveDoneUtc;
        private int? eventScrubAppliedIndex;

        private bool npcTabActorsMode;

        private void UpdateNpcActorsUiEnabled(bool enabled)
        {
            if (!npcTabActorsMode)
            {
                return;
            }

            btnListSceneSpawners.Enabled = enabled;
            btnSpawnCandidate.Enabled = enabled;
            btnLookupSpawner.Enabled = enabled;
        }

        private void OnEventCameraTogglesChanged()
        {
            if (renderCtrl?.renderer == null)
            {
                return;
            }

            bool show = eventShowCameraCheckBox?.Checked == true;
            bool use = eventUseCameraCheckBox?.Checked == true;

            renderCtrl.renderer.AllowUserInput = !use;

            if (show && eventCameraGizmo == null)
            {
                eventCameraGizmo = new CameraGizmo
                {
                    IsVisible = true
                };
                eventCameraGizmo.Transform.Scale = new Vector3(5.0f);
                renderCtrl.renderer.AddSceneObject(eventCameraGizmo);
            }

            if (eventCameraGizmo != null)
            {
                eventCameraGizmo.IsVisible = show;
                if (eventCameraGizmo.Transform.Scale == Vector3.One)
                {
                    eventCameraGizmo.Transform.Scale = new Vector3(5.0f);
                }
            }

            if (eventTimelineEntries.Count > 0)
            {
                ScrubEventSimulationToIndex(eventTimelineIndex);
            }
            else
            {
                ApplyEventSimulationToRenderer();
            }
        }

        private void RefreshNpcActorsFromEventTimeline()
        {
            if (!npcTabActorsMode)
            {
                return;
            }

            var actorIds = CollectEventActorIdsFromTimeline();
            actorIds.Sort(StringComparer.Ordinal);

            spawnerCandidatesListView.BeginUpdate();
            try
            {
                spawnerCandidatesListView.Items.Clear();
                foreach (var id in actorIds)
                {
                    var item = new ListViewItem(id);
                    item.SubItems.Add(ClassifyEventActorKind(id));
                    item.SubItems.Add(eventActorModels.ContainsKey(id) ? "Yes" : "No");
                    item.Tag = id;
                    spawnerCandidatesListView.Items.Add(item);
                }
            }
            finally
            {
                spawnerCandidatesListView.EndUpdate();
            }
        }

        private List<string> CollectEventActorIdsFromTimeline()
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var list = new List<string>();

            void Add(string? id)
            {
                if (string.IsNullOrWhiteSpace(id))
                {
                    return;
                }
                if (seen.Add(id))
                {
                    list.Add(id);
                }
            }

            foreach (var e in eventTimelineEntries)
            {
                switch (e.Method)
                {
                    case "Create_Npc_Dynamic":
                    case "Create_NpcPokemon":
                    case "SetPosRot":
                    case "SetPlayerPosRot":
                    case "SetMotion":
                    case "SetFace":
                    case "FObj_Set_Visibility":
                    case "FObj_Set_Animation_Immediately":
                    case "FObj_Move_To_PosXYZ":
                    case "FObj_Set_Turn_To_Obj":
                        Add(e.Args.Count > 0 ? e.Args[0] as string : null);
                        break;
                    case "LookAt":
                        Add(e.Args.Count > 0 ? e.Args[0] as string : null);
                        Add(e.Args.Count > 1 ? e.Args[1] as string : null);
                        break;
                }
            }

            return list;
        }

        private static string ClassifyEventActorKind(string actorId)
        {
            if (string.Equals(actorId, "Player", StringComparison.Ordinal))
            {
                return "Player";
            }

            if (actorId.StartsWith("npc_", StringComparison.Ordinal) || actorId.StartsWith("dyn_", StringComparison.Ordinal))
            {
                return "NPC";
            }

            return "Other";
        }

        private void OnEventTimelineIndexChanged()
        {
            if (eventIsPlaying)
            {
                return;
            }

            ScrubEventSimulationToIndex(eventTimelineIndex);
        }

        private void ScrubEventSimulationToIndex(int index)
        {
            if (eventTimelineEntries.Count == 0)
            {
                return;
            }

            index = Math.Clamp(index, 0, eventTimelineEntries.Count - 1);

            if (eventScrubAppliedIndex.HasValue && eventScrubAppliedIndex.Value == index)
            {
                ApplyEventSimulationToRenderer();
                return;
            }

            if (!eventScrubAppliedIndex.HasValue || index < eventScrubAppliedIndex.Value)
            {
                ResetEventSimulationState();
                for (int i = 0; i <= index; i++)
                {
                    ApplyEventCallImmediate(eventTimelineEntries[i]);
                }
            }
            else
            {
                for (int i = eventScrubAppliedIndex.Value + 1; i <= index; i++)
                {
                    ApplyEventCallImmediate(eventTimelineEntries[i]);
                }
            }

            eventScrubAppliedIndex = index;
            ApplyEventSimulationToRenderer();
        }

	        private void ResetEventSimulationState()
	        {
	            eventActorStates.Clear();
	            eventDynamicActorToNpcAssetId.Clear();
	            eventDynamicActorToPokemonNo.Clear();
	            eventDynamicActorToPokemonVariant.Clear();
	            ResetActorMoves();
	            eventActorSourceModelRels.Clear();
	            eventActorMotionDirRels.Clear();
	            eventAnimationCacheByAbsPath.Clear();
                eventActorCurrentTranmAbsByActor.Clear();
	            eventCameraPos = Vector3.Zero;
	            eventCameraRotDeg = Vector3.Zero;
	            eventCameraFovDeg = 45.0f;
	            eventPlaybackBlockedUntilUtc = null;
	            eventCameraMoveDoneUtc = null;
                eventScrubAppliedIndex = null;

	            renderCtrl?.renderer?.StopAnimation();
	            renderCtrl?.renderer?.SetAnimationTargets(null);
	        }

	        private void ApplyEventCallImmediate(EventTimelineEntry e)
	        {
	            switch (e.Method)
	            {
	                case "Create_Npc_Dynamic":
	                    ApplyCreateNpcDynamic(e.Args);
	                    break;
	                case "Create_NpcPokemon":
	                    ApplyCreateNpcPokemon(e.Args);
	                    break;
	                case "SetPosRot":
	                case "SetPlayerPosRot":
	                    ApplySetPosRot(e.Args);
	                    break;
	                case "SetMotion":
	                    ApplySetMotion(e.Args);
	                    break;
	                case "FObj_Reset_Animation":
	                    ApplyResetAnimation(e.Args);
	                    break;
	                case "FObj_Set_Animation_Immediately":
	                    ApplySetAnimationImmediately(e.Args);
	                    break;
	                case "FObj_Move_To_PosXYZ":
	                    ApplyFObjMoveToPosXYZ(e.Args, snapToEnd: true);
	                    break;
	                case "FObj_Set_Turn":
                    ApplyFObjSetTurn(e.Args, snapToEnd: true);
                    break;
                case "FObj_Set_Turn_To_Obj":
                    ApplyFObjSetTurnToObj(e.Args, snapToEnd: true);
                    break;
                case "FObj_LookAt_Pos":
                    ApplyFObjLookAtPos(e.Args, snapToEnd: true);
                    break;
                case "FObj_Set_Visibility":
                    ApplyVisibility(e.Args);
                    break;
                case "LookAt":
                    ApplyLookAt(e.Args);
                    break;
	                case "Camera":
	                    ApplyCamera(e.Args);
	                    break;
	            }
	        }

        private void ApplyCreateNpcDynamic(List<object?> args)
        {
            // Create_Npc_Dynamic(dynName, pos{3}, rotY, npcAssetId)
            if (args.Count < 4)
            {
                return;
            }

            var dynName = args[0] as string;
            if (string.IsNullOrWhiteSpace(dynName))
            {
                return;
            }

            if (!TryGetVec3(args[1], out var pos) || !TryGetFloat(args[2], out var rotY))
            {
                return;
            }

            if (args[3] is string npcAssetId && !string.IsNullOrWhiteSpace(npcAssetId))
            {
                eventDynamicActorToNpcAssetId[dynName] = npcAssetId;
            }

            var s = GetOrCreateActorState(dynName);
            s.Position = pos;
            s.RotationDegrees = new Vector3(0, rotY, 0);
            s.Visible = true;
        }

        private void ApplyCreateNpcPokemon(List<object?> args)
        {
            // Create_NpcPokemon(dynName, pos{3}, rotY, pokemonNo, ...)
            if (args.Count < 4)
            {
                return;
            }

            var dynName = args[0] as string;
            if (string.IsNullOrWhiteSpace(dynName))
            {
                return;
            }

            if (!TryGetVec3(args[1], out var pos) || !TryGetFloat(args[2], out var rotY))
            {
                return;
            }

            if (TryGetFloat(args[3], out var pokemonNo))
            {
                eventDynamicActorToPokemonNo[dynName] = (int)pokemonNo;
            }

            // Empirically in sub_201: Create_NpcPokemon(name, pos, rotY, pokemonNo, form?, gender?, ...)
            if (args.Count > 5 &&
                TryGetFloat(args[4], out var form) &&
                TryGetFloat(args[5], out var gender))
            {
                eventDynamicActorToPokemonVariant[dynName] = ((int)form, (int)gender);
            }

            var s = GetOrCreateActorState(dynName);
            s.Position = pos;
            s.RotationDegrees = new Vector3(0, rotY, 0);
            s.Visible = true;
        }

        private void TickEventPlayback()
        {
            if (!eventIsPlaying)
            {
                return;
            }

            if (eventTimelineEntries.Count == 0)
            {
                SetEventPlaying(false);
                return;
            }

            var now = DateTime.UtcNow;
            if (eventPlaybackBlockedUntilUtc.HasValue && now < eventPlaybackBlockedUntilUtc.Value)
            {
                ApplyEventSimulationToRenderer();
                return;
            }

            if (eventTimelineIndex < 0 || eventTimelineIndex >= eventTimelineEntries.Count)
            {
                SetEventPlaying(false);
                return;
            }

            var e = eventTimelineEntries[eventTimelineIndex];
            if (string.Equals(e.Method, "FObj_Move_To_PosXYZ", StringComparison.Ordinal))
            {
                ApplyFObjMoveToPosXYZ(e.Args, snapToEnd: false);
            }
            else if (string.Equals(e.Method, "FObj_Set_Turn", StringComparison.Ordinal))
            {
                ApplyFObjSetTurn(e.Args, snapToEnd: false);
            }
            else if (string.Equals(e.Method, "FObj_Set_Turn_To_Obj", StringComparison.Ordinal))
            {
                ApplyFObjSetTurnToObj(e.Args, snapToEnd: false);
            }
            else if (string.Equals(e.Method, "FObj_LookAt_Pos", StringComparison.Ordinal))
            {
                ApplyFObjLookAtPos(e.Args, snapToEnd: false);
            }
            else
            {
                ApplyEventCallImmediate(e);
            }

            if (string.Equals(e.Method, "WaitSeconds", StringComparison.Ordinal) &&
                e.Args.Count > 0 &&
                TryGetFloat(e.Args[0], out var seconds) &&
                seconds > 0.0f)
            {
                eventPlaybackBlockedUntilUtc = now.AddSeconds(seconds);
            }
            else if (string.Equals(e.Method, "Wait", StringComparison.Ordinal))
            {
                SetEventPlaying(false);
                MessageHandler.Instance.AddMessage(MessageType.LOG, "[Event] Wait() hit (paused playback).");
                ApplyEventSimulationToRenderer();
                return;
            }
            else if (string.Equals(e.Method, "Camera", StringComparison.Ordinal) &&
                     e.Args.Count > 0 &&
                     TryGetFloat(e.Args[0], out var dur) &&
                     dur > 0.0f)
            {
                eventCameraMoveDoneUtc = now.AddSeconds(dur);
            }
            else if (string.Equals(e.Method, "Wait_Camera", StringComparison.Ordinal) &&
                     eventCameraMoveDoneUtc.HasValue &&
                     now < eventCameraMoveDoneUtc.Value)
            {
                eventPlaybackBlockedUntilUtc = eventCameraMoveDoneUtc.Value;
            }
            else if (string.Equals(e.Method, "Wait_FObj_Action", StringComparison.Ordinal))
            {
                if (TryGetNextActorActionDoneUtc(now, out var doneUtc))
                {
                    eventPlaybackBlockedUntilUtc = doneUtc;
                }
            }

            ApplyEventSimulationToRenderer();

            if (!eventIsPlaying)
            {
                return;
            }

            if (eventTimelineIndex >= eventTimelineEntries.Count - 1)
            {
                SetEventPlaying(false);
                return;
            }

            StepEvent(+1);
        }

        private void ApplySetPosRot(List<object?> args)
        {
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
                !TryGetFloat(args[3], out var z) ||
                !TryGetFloat(args[4], out var rotY))
            {
                return;
            }

            var s = GetOrCreateActorState(actor);
            s.Position = new Vector3(x, y, z);
            s.RotationDegrees = new Vector3(0, rotY, 0);
        }

        private void ApplyVisibility(List<object?> args)
        {
            if (args.Count < 2)
            {
                return;
            }

            var actor = args[0] as string;
            if (string.IsNullOrWhiteSpace(actor))
            {
                return;
            }

            if (args[1] is not bool b)
            {
                return;
            }

            GetOrCreateActorState(actor).Visible = b;
        }

        private void ApplyLookAt(List<object?> args)
        {
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

            var dir = t.Position - a.Position;
            dir.Y = 0;
            if (dir.LengthSquared < 0.00001f)
            {
                return;
            }

            // Match the renderer camera's yaw convention (yaw=atan2(front.Z, front.X)).
            float yawDeg = MathHelper.RadiansToDegrees(MathF.Atan2(dir.Z, dir.X));
            a.RotationDegrees = new Vector3(a.RotationDegrees.X, yawDeg, a.RotationDegrees.Z);
        }

        private void ApplyCamera(List<object?> args)
        {
            // Camera(duration, pos{3}, rot{3}, fov, ...)
            if (args.Count < 4)
            {
                return;
            }

            if (!TryGetVec3(args[1], out var pos) || !TryGetVec3(args[2], out var rot))
            {
                return;
            }

            if (!TryGetFloat(args[3], out var fov))
            {
                fov = 45.0f;
            }

            eventCameraPos = pos;
            eventCameraRotDeg = rot;
            eventCameraFovDeg = fov;
        }

        private EventActorState GetOrCreateActorState(string actorId)
        {
            if (!eventActorStates.TryGetValue(actorId, out var s))
            {
                s = new EventActorState();
                eventActorStates[actorId] = s;
            }
            return s;
        }

        private bool TryResolveEventActorToNpcAssetId(string actorId, out string npcAssetId)
        {
            npcAssetId = string.Empty;

            if (string.IsNullOrWhiteSpace(actorId))
            {
                return false;
            }

            if (actorId.StartsWith("npc_", StringComparison.Ordinal))
            {
                npcAssetId = actorId;
                return true;
            }

            if (eventDynamicActorToNpcAssetId.TryGetValue(actorId, out var mapped) && !string.IsNullOrWhiteSpace(mapped))
            {
                npcAssetId = mapped;
                return true;
            }

            foreach (var e in eventTimelineEntries)
            {
                if (!string.Equals(e.Method, "Create_Npc_Dynamic", StringComparison.Ordinal))
                {
                    continue;
                }

                if (e.Args.Count < 4)
                {
                    continue;
                }

                if (e.Args[0] is string dyn &&
                    string.Equals(dyn, actorId, StringComparison.Ordinal) &&
                    e.Args[3] is string npc &&
                    !string.IsNullOrWhiteSpace(npc))
                {
                    eventDynamicActorToNpcAssetId[actorId] = npc;
                    npcAssetId = npc;
                    return true;
                }
            }

            return false;
        }

        private bool TryGetEventActorPokemonNo(string actorId, out int pokemonNo)
        {
            pokemonNo = 0;
            if (eventDynamicActorToPokemonNo.TryGetValue(actorId, out pokemonNo))
            {
                return true;
            }

            foreach (var e in eventTimelineEntries)
            {
                if (!string.Equals(e.Method, "Create_NpcPokemon", StringComparison.Ordinal))
                {
                    continue;
                }

                if (e.Args.Count < 4)
                {
                    continue;
                }

                if (e.Args[0] is string dyn &&
                    string.Equals(dyn, actorId, StringComparison.Ordinal) &&
                    TryGetFloat(e.Args[3], out var no))
                {
                    pokemonNo = (int)no;
                    eventDynamicActorToPokemonNo[actorId] = pokemonNo;
                    return true;
                }
            }

            return false;
        }

        private bool TryGetEventActorPokemonSpawnInfo(string actorId, out int pokemonNo, out int form, out int gender, out Vector3 pos, out float rotY)
        {
            pokemonNo = 0;
            form = 0;
            gender = 0;
            pos = Vector3.Zero;
            rotY = 0.0f;

            foreach (var e in eventTimelineEntries)
            {
                if (!string.Equals(e.Method, "Create_NpcPokemon", StringComparison.Ordinal))
                {
                    continue;
                }

                if (e.Args.Count < 4)
                {
                    continue;
                }

                if (e.Args[0] is not string dyn || !string.Equals(dyn, actorId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!TryGetVec3(e.Args[1], out pos) || !TryGetFloat(e.Args[2], out rotY))
                {
                    return false;
                }

                if (!TryGetFloat(e.Args[3], out var no))
                {
                    return false;
                }

                pokemonNo = (int)no;
                eventDynamicActorToPokemonNo[actorId] = pokemonNo;
                if (e.Args.Count > 5 &&
                    TryGetFloat(e.Args[4], out var f) &&
                    TryGetFloat(e.Args[5], out var g))
                {
                    form = (int)f;
                    gender = (int)g;
                    eventDynamicActorToPokemonVariant[actorId] = (form, gender);
                }
                else if (eventDynamicActorToPokemonVariant.TryGetValue(actorId, out var v))
                {
                    form = v.Form;
                    gender = v.Gender;
                }
                return true;
            }

            return false;
        }

        private static Matrix4 BuildTransformMatrix(Vector3 pos, Vector3 rotDeg)
        {
            return Matrix4.CreateTranslation(pos) * Matrix4.CreateFromQuaternion(RotationFromDegrees(rotDeg));
        }

        private static Quaternion RotationFromDegrees(Vector3 rotDeg)
        {
            float rx = MathHelper.DegreesToRadians(rotDeg.X);
            float ry = MathHelper.DegreesToRadians(rotDeg.Y);
            float rz = MathHelper.DegreesToRadians(rotDeg.Z);
            var q = Quaternion.FromEulerAngles(rx, ry, rz);
            q.Normalize();
            return q;
        }

        private static bool TryGetVec3(object? v, out Vector3 result)
        {
            result = Vector3.Zero;
            if (v is not List<object?> list || list.Count < 3)
            {
                return false;
            }

            if (!TryGetFloat(list[0], out var x) ||
                !TryGetFloat(list[1], out var y) ||
                !TryGetFloat(list[2], out var z))
            {
                return false;
            }

            result = new Vector3(x, y, z);
            return true;
        }

        private static bool TryGetFloat(object? v, out float result)
        {
            result = 0;
            switch (v)
            {
                case float f:
                    result = f;
                    return true;
                case double d:
                    result = (float)d;
                    return true;
                case int i:
                    result = i;
                    return true;
                case long l:
                    result = l;
                    return true;
                case string s:
                    return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
                default:
                    return false;
            }
        }
    }
}
