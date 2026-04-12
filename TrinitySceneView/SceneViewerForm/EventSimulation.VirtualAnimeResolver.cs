using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using GFTool.Renderer.Core;
using Trinity.Core.Flatbuffers.Reflections;
using Trinity.Core.Utils;

namespace TrinitySceneView
{
    public partial class SceneViewerForm
    {
        private sealed class VirtualAnimDb
        {
            public readonly Dictionary<int, int> VirtualToReal = new();
            public readonly Dictionary<int, Dictionary<string, object>> RealToParamEdits = new();
        }

        private sealed class MotionAnimDb
        {
            public readonly Dictionary<string, object> DefaultParams = new(StringComparer.Ordinal);
            public readonly Dictionary<string, string> ResourceToTranmRel = new(StringComparer.Ordinal);
            public readonly Dictionary<string, TracsState> States = new(StringComparer.Ordinal);
            public string? EntryStateName;
        }

        private sealed record TracsState(
            string Name,
            string Type,
            string? ResourceName,
            string? ResourceType,
            List<TracsTransition> Transitions);

        private sealed record TracsTransition(
            string ToStateName,
            string LogicalOperator,
            int Priority,
            bool HasExitTime,
            float ExitTime,
            string? ExitTimeParameterName,
            string? RequiredLastState,
            List<TracsCondition> Conditions);

        private sealed record TracsCondition(
            string ParameterName,
            string Func,
            string ValueType,
            object Value,
            string LogicalOperator);

        private VirtualAnimDb? cachedVirtualAnimDb;
        private string? cachedVirtualAnimDbAssetRoot;
        private readonly Dictionary<string, MotionAnimDb> motionAnimDbByDir = new(StringComparer.OrdinalIgnoreCase);

        private bool TryResolveVirtualAnimationTranmAbs(string actorId, int virtualAnimeType, string motionDirAbs, out string tranmAbs, out string? reason)
        {
            tranmAbs = string.Empty;
            reason = null;

            if (!TryLoadVirtualAnimDb(out var db, out reason))
            {
                return false;
            }

            if (!db.VirtualToReal.TryGetValue(virtualAnimeType, out var realAnimeType))
            {
                reason = $"No mapping for virtualAnimeType={virtualAnimeType}.";
                return false;
            }

            if (!db.RealToParamEdits.TryGetValue(realAnimeType, out var edits))
            {
                edits = new Dictionary<string, object>(StringComparer.Ordinal);
            }

            if (!TryLoadMotionAnimDb(motionDirAbs, out var motionDb, out reason))
            {
                return false;
            }

            var paramSnapshot = new Dictionary<string, object>(motionDb.DefaultParams, StringComparer.Ordinal);
            foreach (var (k, v) in edits)
            {
                paramSnapshot[k] = v;
            }

            if (MessageHandler.Instance.DebugLogsEnabled && virtualAnimeType == 28)
            {
                TraceTracsTraversal(motionDb, paramSnapshot);
            }

            if (!TrySelectResourceLabel(motionDb, paramSnapshot, out var resourceName))
            {
                reason = $"Failed selecting resource label for virtualAnimeType={virtualAnimeType} (realAnimeType={realAnimeType}).";
                return false;
            }

            if (MessageHandler.Instance.DebugLogsEnabled && virtualAnimeType == 28)
            {
                LogLoopActionTransitions(motionDb, paramSnapshot);
            }

            if (!motionDb.ResourceToTranmRel.TryGetValue(resourceName, out var tranmRel) || string.IsNullOrWhiteSpace(tranmRel))
            {
                reason = $"Resource '{resourceName}' not found in TRACR.";
                return false;
            }

            var abs = Path.Combine(motionDirAbs, tranmRel.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(abs))
            {
                reason = $"Missing TRANM: '{tranmRel}'.";
                return false;
            }

            tranmAbs = abs;

            if (MessageHandler.Instance.DebugLogsEnabled)
            {
                string editSummary = edits.Count == 0
                    ? "<none>"
                    : string.Join(", ", edits.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}={kv.Value}"));
                if (editSummary.Length > 220)
                {
                    editSummary = editSummary.Substring(0, 220) + "...";
                }

                MessageHandler.Instance.AddMessage(
                    MessageType.LOG,
                    $"[Event] Anim resolve '{actorId}' virtual={virtualAnimeType} real={realAnimeType} edits=[{editSummary}] resource='{resourceName}' tranm='{tranmRel}'");
            }

            return true;
        }

        private static void TraceTracsTraversal(MotionAnimDb db, Dictionary<string, object> parameters)
        {
            try
            {
                string? current = db.EntryStateName;
                if (current == null || !db.States.ContainsKey(current))
                {
                    current = db.States.Values.FirstOrDefault(s => string.Equals(s.Type, "Entry", StringComparison.OrdinalIgnoreCase))?.Name;
                }

                var visited = new HashSet<string>(StringComparer.Ordinal);
                string? last = null;
                for (int step = 0; step < 24 && current != null; step++)
                {
                    if (!visited.Add(current))
                    {
                        MessageHandler.Instance.AddMessage(MessageType.LOG, $"[Event] TRACS trace: loop at '{current}'.");
                        break;
                    }

                    if (!db.States.TryGetValue(current, out var st))
                    {
                        MessageHandler.Instance.AddMessage(MessageType.LOG, $"[Event] TRACS trace: missing state '{current}'.");
                        break;
                    }

                    var res = (!string.IsNullOrWhiteSpace(st.ResourceName) && string.Equals(st.ResourceType, "File", StringComparison.OrdinalIgnoreCase))
                        ? st.ResourceName
                        : null;
                    MessageHandler.Instance.AddMessage(
                        MessageType.LOG,
                        $"[Event] TRACS trace[{step + 1}] state='{current}' type='{st.Type}' res='{res ?? "<none>"}'");

                    if (!string.Equals(st.Type, "Entry", StringComparison.OrdinalIgnoreCase))
                    {
                        var preChildEntry = current + "/Entry";
                        if (db.States.ContainsKey(preChildEntry) && !visited.Contains(preChildEntry))
                        {
                            current = preChildEntry;
                            continue;
                        }

                        var preDeepEntry = FindClosestEntryDescendant(db, current, visited);
                        if (preDeepEntry != null)
                        {
                            current = preDeepEntry;
                            continue;
                        }
                    }

                    var next = ChooseTransition(db, current, last, st.Transitions, parameters);
                    if (next != null)
                    {
                        string exit = $"{next.ExitTime.ToString(CultureInfo.InvariantCulture)}";
                        if (!string.IsNullOrWhiteSpace(next.ExitTimeParameterName))
                        {
                            exit += $"({next.ExitTimeParameterName})";
                        }

                        MessageHandler.Instance.AddMessage(
                            MessageType.LOG,
                            $"[Event] TRACS trace[{step + 1}] -> '{next.ToStateName}' pri={next.Priority} hasExitTime={next.HasExitTime} exit={exit} req='{next.RequiredLastState ?? ""}' conds={(next.Conditions?.Count ?? 0)}");
                        last = current;
                        current = next.ToStateName;
                        continue;
                    }

                    break;
                }
            }
            catch
            {
                // Debug only.
            }
        }

        private static void LogLoopActionTransitions(MotionAnimDb db, Dictionary<string, object> parameters)
        {
            try
            {
                var rows = new List<(string From, TracsTransition Tr, bool CondsOk)>();
                foreach (var st in db.States.Values)
                {
                    if (st.Transitions == null || st.Transitions.Count == 0)
                    {
                        continue;
                    }

                    foreach (var tr in st.Transitions)
                    {
                        bool mentions = tr.Conditions != null && tr.Conditions.Any(c =>
                            c.ParameterName.StartsWith("loop_action", StringComparison.OrdinalIgnoreCase));
                        if (!mentions)
                        {
                            continue;
                        }

                        // Debug-only: ignore requiredLastState so we can see condition behavior.
                        bool ok = tr.Conditions == null || tr.Conditions.Count == 0 || tr.Conditions.All(c => EvaluateCondition(c, parameters));
                        rows.Add((st.Name, tr, ok));
                    }
                }

                if (rows.Count == 0)
                {
                    MessageHandler.Instance.AddMessage(MessageType.LOG, "[Event] TRACS loop_action transitions: <none found>");
                    return;
                }

                rows = rows
                    .OrderByDescending(r => r.CondsOk)
                    .ThenByDescending(r => r.Tr.Priority)
                    .ThenByDescending(r => r.Tr.Conditions?.Count ?? 0)
                    .ThenBy(r => r.From, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                int n = Math.Min(12, rows.Count);
                for (int i = 0; i < n; i++)
                {
                    var r = rows[i];
                    string req = r.Tr.RequiredLastState ?? "";
                    if (req.Length > 0) req = $" req='{req}'";
                    string conds = r.Tr.Conditions == null ? "" : string.Join(" ", r.Tr.Conditions.Select(c => $"{c.ParameterName}{c.Func}{c.Value}"));
                    if (conds.Length > 180) conds = conds.Substring(0, 180) + "...";
                    MessageHandler.Instance.AddMessage(
                        MessageType.LOG,
                        $"[Event] TRACS loop_action tr[{i + 1}/{rows.Count}] ok={r.CondsOk} pri={r.Tr.Priority} from='{r.From}' -> '{r.Tr.ToStateName}'{req} conds='{conds}'");
                }
            }
            catch
            {
                // Debug only.
            }
        }

        private bool TryLoadVirtualAnimDb(out VirtualAnimDb db, out string? reason)
        {
            reason = null;
            if (!string.Equals(cachedVirtualAnimDbAssetRoot, assetRoot, StringComparison.OrdinalIgnoreCase))
            {
                cachedVirtualAnimDb = null;
                cachedVirtualAnimDbAssetRoot = assetRoot;
            }

            if (cachedVirtualAnimDb != null)
            {
                db = cachedVirtualAnimDb;
                return true;
            }

            var virtualBfbsAbs = ResolveAssetFileAbs("param_chr/data/character/anime/chara_virtual_anime/chara_virtual_anime_array.bfbs");
            var virtualBinAbs = ResolveAssetFileAbs("param_chr/data/character/anime/chara_virtual_anime/chara_virtual_anime_array.bin");
            var realBfbsAbs = ResolveAssetFileAbs("param_chr/data/character/anime/chara_real_anime/chara_real_anime_array.bfbs");
            var realBinAbs = ResolveAssetFileAbs("param_chr/data/character/anime/chara_real_anime/chara_real_anime_array.bin");

            if (virtualBfbsAbs == null || virtualBinAbs == null || realBfbsAbs == null || realBinAbs == null)
            {
                reason = "Missing virtual/real anime param tables under the current asset root.";
                db = new VirtualAnimDb();
                cachedVirtualAnimDb = db;
                return false;
            }

            JsonDocument virtualDoc;
            JsonDocument realDoc;
            try
            {
                virtualDoc = LoadReflectionDocFromFiles(virtualBfbsAbs, virtualBinAbs);
                realDoc = LoadReflectionDocFromFiles(realBfbsAbs, realBinAbs);
            }
            catch (Exception ex)
            {
                reason = $"Failed reading virtual/real anime param tables: {ex.GetType().Name}: {ex.Message}";
                db = new VirtualAnimDb();
                cachedVirtualAnimDb = db;
                return false;
            }

            using (virtualDoc)
            using (realDoc)
            {
                var built = new VirtualAnimDb();
                BuildVirtualToRealMap(virtualDoc, built.VirtualToReal);
                BuildRealParamEdits(realDoc, built.RealToParamEdits);
                cachedVirtualAnimDb = built;
                db = built;

                if (MessageHandler.Instance.DebugLogsEnabled)
                {
                    string vt28 = built.VirtualToReal.TryGetValue(28, out var rt28)
                        ? rt28.ToString(CultureInfo.InvariantCulture)
                        : "<missing>";
                    MessageHandler.Instance.AddMessage(
                        MessageType.LOG,
                        $"[Event] VirtualAnim DB loaded: virtual={built.VirtualToReal.Count} real={built.RealToParamEdits.Count} vt28={vt28}");
                }

                return built.VirtualToReal.Count > 0;
            }
        }

        private bool TryLoadMotionAnimDb(string motionDirAbs, out MotionAnimDb db, out string? reason)
        {
            reason = null;
            if (motionAnimDbByDir.TryGetValue(motionDirAbs, out db!))
            {
                return true;
            }

            if (!TryResolveBaseAnimFiles(motionDirAbs, out var tracpAbs, out var tracsAbs, out var tracrAbs))
            {
                reason = $"Missing base animation config files under '{motionDirAbs}'.";
                return false;
            }

            try
            {
                var built = new MotionAnimDb();
                ParseTracpDefaultsFromBytes(File.ReadAllBytes(tracpAbs), built.DefaultParams);
                ParseTracrResourcesFromBytes(File.ReadAllBytes(tracrAbs), built.ResourceToTranmRel);
                ParseTracsStatesFromBytes(File.ReadAllBytes(tracsAbs), built.States, out built.EntryStateName);

                if (built.States.Count == 0 || built.ResourceToTranmRel.Count == 0)
                {
                    reason = $"Parsed TRAC configs but got states={built.States.Count} resources={built.ResourceToTranmRel.Count}.";
                    db = new MotionAnimDb();
                    return false;
                }

                if (MessageHandler.Instance.DebugLogsEnabled)
                {
                    var condNames = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var st in built.States.Values)
                    {
                        foreach (var tr in st.Transitions)
                        {
                            foreach (var c in tr.Conditions)
                            {
                                if (!string.IsNullOrWhiteSpace(c.ParameterName))
                                {
                                    condNames.Add(c.ParameterName);
                                }
                            }
                        }
                    }

                    bool hasLoopActionIntDefault = built.DefaultParams.ContainsKey("loop_action_int");
                    bool hasLoopActionBoolDefault = built.DefaultParams.ContainsKey("loop_action_bool");
                    bool hasLoopActionDefault = built.DefaultParams.ContainsKey("loop_action");
                    int condLoopActionInt = condNames.Contains("loop_action_int") ? 1 : 0;
                    int condLoopActionBool = condNames.Contains("loop_action_bool") ? 1 : 0;
                    int condLoopAction = condNames.Contains("loop_action") ? 1 : 0;

                    MessageHandler.Instance.AddMessage(
                        MessageType.LOG,
                        $"[Event] TRAC loaded dir='{Path.GetFileName(motionDirAbs)}' params={built.DefaultParams.Count} states={built.States.Count} resources={built.ResourceToTranmRel.Count} entry='{built.EntryStateName ?? "<none>"}' default(loop_action_int)={(hasLoopActionIntDefault ? "Y" : "N")} default(loop_action_bool)={(hasLoopActionBoolDefault ? "Y" : "N")} default(loop_action)={(hasLoopActionDefault ? "Y" : "N")} cond(loop_action_int)={condLoopActionInt} cond(loop_action_bool)={condLoopActionBool} cond(loop_action)={condLoopAction}");
                }

                motionAnimDbByDir[motionDirAbs] = built;
                db = built;
                return true;
            }
            catch (Exception ex)
            {
                reason = $"Failed parsing TRAC configs: {ex.GetType().Name}: {ex.Message}";
                db = new MotionAnimDb();
                return false;
            }
        }

        private static bool TryResolveBaseAnimFiles(string motionDirAbs, out string tracpAbs, out string tracsAbs, out string tracrAbs)
        {
            tracpAbs = string.Empty;
            tracsAbs = string.Empty;
            tracrAbs = string.Empty;

            try
            {
                tracpAbs = Directory.EnumerateFiles(motionDirAbs, "*_base.tracp", SearchOption.TopDirectoryOnly)
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault() ?? "";
                tracsAbs = Directory.EnumerateFiles(motionDirAbs, "*_base.tracs", SearchOption.TopDirectoryOnly)
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault() ?? "";
                tracrAbs = Directory.EnumerateFiles(motionDirAbs, "*_base.tracr", SearchOption.TopDirectoryOnly)
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault() ?? "";
            }
            catch
            {
            }

            return File.Exists(tracpAbs) && File.Exists(tracsAbs) && File.Exists(tracrAbs);
        }

        private static JsonDocument LoadReflectionDocFromFiles(string bfbsAbs, string binAbs)
        {
            byte[] bfbsBytes = File.ReadAllBytes(bfbsAbs);
            byte[] binBytes = File.ReadAllBytes(binAbs);
            var reflectionSchema = FlatBufferConverter.DeserializeFrom<ReflectionSchema>(bfbsBytes);
            var ctx = ReflectionSchemaContext.Create(reflectionSchema);
            var json = FlatbufferReflectionJsonDumper.Dump(binBytes, ctx);
            return JsonDocument.Parse(json);
        }

        private string? ResolveAssetFileAbs(string rel)
        {
            var direct = ResolveModelPath(rel);
            if (direct != null && File.Exists(direct))
            {
                return direct;
            }

            if (!string.IsNullOrWhiteSpace(assetRoot))
            {
                try
                {
                    var abs = Path.GetFullPath(Path.Combine(assetRoot, rel.Replace('/', Path.DirectorySeparatorChar)));
                    if (File.Exists(abs))
                    {
                        return abs;
                    }
                }
                catch
                {
                }
            }

            return null;
        }
    }
}
