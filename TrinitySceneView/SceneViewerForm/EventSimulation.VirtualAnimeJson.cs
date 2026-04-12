using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace TrinitySceneView
{
    public partial class SceneViewerForm
    {
        private static void BuildVirtualToRealMap(JsonDocument doc, Dictionary<int, int> outMap)
        {
            if (!doc.RootElement.TryGetProperty("values", out var values) || values.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var group in values.EnumerateArray())
            {
                if (!group.TryGetProperty("root", out var root) || root.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var db in root.EnumerateArray())
                {
                    if (!db.TryGetProperty("VirtualAnimes", out var list) || list.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    foreach (var entry in list.EnumerateArray())
                    {
                        if (!entry.TryGetProperty("virtualAnimeType", out var vtEl) || vtEl.ValueKind != JsonValueKind.Number)
                        {
                            continue;
                        }

                        int vt = vtEl.GetInt32();
                        int? rt = null;

                        if (entry.TryGetProperty("realAnimParam", out var rp) && rp.ValueKind == JsonValueKind.Object)
                        {
                            if (rp.TryGetProperty("realAnimeTypes", out var rts) && rts.ValueKind == JsonValueKind.Array && rts.GetArrayLength() > 0)
                            {
                                var first = rts.EnumerateArray().FirstOrDefault();
                                if (first.ValueKind == JsonValueKind.Number)
                                {
                                    rt = first.GetInt32();
                                }
                            }
                        }

                        if (rt.HasValue && !outMap.ContainsKey(vt))
                        {
                            outMap[vt] = rt.Value;
                        }
                    }
                }
            }
        }

        private static void BuildRealParamEdits(JsonDocument doc, Dictionary<int, Dictionary<string, object>> outMap)
        {
            if (!doc.RootElement.TryGetProperty("values", out var values) || values.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var group in values.EnumerateArray())
            {
                if (!group.TryGetProperty("root", out var root) || root.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var el in root.EnumerateArray())
                {
                    if (!el.TryGetProperty("realAnimaeType", out var rtEl) || rtEl.ValueKind != JsonValueKind.Number)
                    {
                        continue;
                    }
                    int rt = rtEl.GetInt32();

                    var edits = new Dictionary<string, object>(StringComparer.Ordinal);
                    if (el.TryGetProperty("Param", out var p) && p.ValueKind == JsonValueKind.Object &&
                        p.TryGetProperty("multiParam", out var mp) && mp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var unit in mp.EnumerateArray())
                        {
                            AddParamEditsFromUnit(unit, "Bool", edits);
                            AddParamEditsFromUnit(unit, "Int", edits);
                            AddParamEditsFromUnit(unit, "Float", edits);
                            AddParamEditsFromUnit(unit, "Trigger", edits);
                        }
                    }

                    if (!outMap.ContainsKey(rt))
                    {
                        outMap[rt] = edits;
                    }
                }
            }
        }

        private static void AddParamEditsFromUnit(JsonElement unit, string key, Dictionary<string, object> edits)
        {
            if (!unit.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var el in arr.EnumerateArray())
            {
                if (!el.TryGetProperty("keyName", out var nameEl) || nameEl.ValueKind != JsonValueKind.String)
                {
                    continue;
                }
                string name = nameEl.GetString() ?? "";
                if (name.Length == 0)
                {
                    continue;
                }

                if (!el.TryGetProperty("value", out var v))
                {
                    continue;
                }

                object? boxed = v.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Number => v.TryGetInt32(out var i) ? i : v.GetDouble(),
                    _ => null
                };

                if (boxed != null)
                {
                    edits[name] = boxed;
                }
            }
        }

        private static void ParseTracpDefaults(JsonDocument doc, Dictionary<string, object> defaults)
        {
            if (!TryGetFirstRootElement(doc, out var root))
            {
                return;
            }

            if (!root.TryGetProperty("parameterConfig", out var pc) || pc.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            if (pc.TryGetProperty("intParameters", out var ints) && ints.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in ints.EnumerateArray())
                {
                    if (p.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String &&
                        p.TryGetProperty("defaultValue", out var dv) && dv.ValueKind == JsonValueKind.Number)
                    {
                        defaults[n.GetString() ?? ""] = dv.GetInt32();
                    }
                }
            }

            if (pc.TryGetProperty("floatParameters", out var floats) && floats.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in floats.EnumerateArray())
                {
                    if (p.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String &&
                        p.TryGetProperty("defaultValue", out var dv) && dv.ValueKind == JsonValueKind.Number)
                    {
                        defaults[n.GetString() ?? ""] = dv.GetDouble();
                    }
                }
            }

            if (pc.TryGetProperty("boolParameters", out var bools) && bools.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in bools.EnumerateArray())
                {
                    if (p.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String &&
                        p.TryGetProperty("defaultValue", out var dv) && (dv.ValueKind == JsonValueKind.True || dv.ValueKind == JsonValueKind.False))
                    {
                        defaults[n.GetString() ?? ""] = dv.ValueKind == JsonValueKind.True;
                    }
                }
            }
        }

        private static void ParseTracrResources(JsonDocument doc, Dictionary<string, string> map)
        {
            if (!TryGetFirstRootElement(doc, out var root))
            {
                return;
            }

            if (!root.TryGetProperty("resourceConfig", out var rc) || rc.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            if (!rc.TryGetProperty("fileResources", out var fr) || fr.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var el in fr.EnumerateArray())
            {
                if (!el.TryGetProperty("name", out var nameEl) || nameEl.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                string name = nameEl.GetString() ?? "";
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                string? tranm = null;
                if (el.TryGetProperty("clipData", out var cd) && cd.ValueKind == JsonValueKind.Object &&
                    cd.TryGetProperty("jointAnimeFilePath", out var jp) && jp.ValueKind == JsonValueKind.Object &&
                    jp.TryGetProperty("path", out var pathEl) && pathEl.ValueKind == JsonValueKind.String)
                {
                    tranm = pathEl.GetString();
                }

                if (!string.IsNullOrWhiteSpace(tranm) && !map.ContainsKey(name))
                {
                    map[name] = tranm!.Replace('\\', '/');
                }
            }
        }

        private static void ParseTracsStates(JsonDocument doc, Dictionary<string, TracsState> outStates, out string? entryStateName)
        {
            entryStateName = null;
            if (!TryGetFirstRootElement(doc, out var root))
            {
                return;
            }

            if (!root.TryGetProperty("stateConfig", out var sc) || sc.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            if (!sc.TryGetProperty("topStates", out var ts) || ts.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var layer in ts.EnumerateArray())
            {
                if (!layer.TryGetProperty("layerName", out var ln) || ln.ValueKind != JsonValueKind.String)
                {
                    continue;
                }
                if (!string.Equals(ln.GetString(), "Base", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!layer.TryGetProperty("state", out var state) || state.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                WalkState(state, outStates, ref entryStateName);
                break;
            }
        }

        private static void WalkState(JsonElement state, Dictionary<string, TracsState> outStates, ref string? entryStateName)
        {
            string name = state.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? (n.GetString() ?? "") : "";
            string type = state.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? (t.GetString() ?? "") : "";

            string? resName = null;
            string? resType = null;
            if (state.TryGetProperty("source", out var src) && src.ValueKind == JsonValueKind.Object &&
                src.TryGetProperty("resourceLabel", out var rl) && rl.ValueKind == JsonValueKind.Object)
            {
                if (rl.TryGetProperty("name", out var rn) && rn.ValueKind == JsonValueKind.String)
                {
                    resName = rn.GetString();
                }
                if (rl.TryGetProperty("type", out var rt) && rt.ValueKind == JsonValueKind.String)
                {
                    resType = rt.GetString();
                }
            }

            var transitions = new List<TracsTransition>();
            if (state.TryGetProperty("transitions", out var trans) && trans.ValueKind == JsonValueKind.Array)
            {
                foreach (var tr in trans.EnumerateArray())
                {
                    if (!tr.TryGetProperty("stateName", out var sn) || sn.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }
                    string to = sn.GetString() ?? "";
                    if (string.IsNullOrWhiteSpace(to))
                    {
                        continue;
                    }

                    string lop = tr.TryGetProperty("logicalOperator", out var lo) && lo.ValueKind == JsonValueKind.String
                        ? (lo.GetString() ?? "And")
                        : "And";

                    int priority = tr.TryGetProperty("priority", out var pr) && pr.ValueKind == JsonValueKind.Number ? pr.GetInt32() : 0;
                    bool hasExitTime = tr.TryGetProperty("hasExitTime", out var het) && het.ValueKind == JsonValueKind.True;
                    float exitTime = tr.TryGetProperty("exitTime", out var et) && et.ValueKind == JsonValueKind.Number ? (float)et.GetDouble() : 0.0f;
                    string? exitTimeParamName = tr.TryGetProperty("exitTimeParameterName", out var etp) && etp.ValueKind == JsonValueKind.String
                        ? (etp.GetString() ?? "")
                        : null;
                    if (string.IsNullOrWhiteSpace(exitTimeParamName))
                    {
                        exitTimeParamName = null;
                    }
                    string? requiredLastState = tr.TryGetProperty("requiredLastState", out var rls) && rls.ValueKind == JsonValueKind.String
                        ? (rls.GetString() ?? "")
                        : null;
                    if (string.IsNullOrWhiteSpace(requiredLastState))
                    {
                        requiredLastState = null;
                    }

                    var conds = new List<TracsCondition>();
                    if (tr.TryGetProperty("conditions", out var c) && c.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var ce in c.EnumerateArray())
                        {
                            if (!ce.TryGetProperty("parameterName", out var pn) || pn.ValueKind != JsonValueKind.String)
                            {
                                continue;
                            }
                            var paramName = pn.GetString() ?? "";
                            if (string.IsNullOrWhiteSpace(paramName))
                            {
                                continue;
                            }

                            var func = ce.TryGetProperty("func", out var fn) && fn.ValueKind == JsonValueKind.String ? (fn.GetString() ?? "") : "";

                            if (!TryReadConditionValue(ce, out var valType, out var value))
                            {
                                continue;
                            }

                            string cop = ce.TryGetProperty("logicalOperator", out var co) && co.ValueKind == JsonValueKind.String
                                ? (co.GetString() ?? "And")
                                : "And";

                            conds.Add(new TracsCondition(paramName, func, valType, value, cop));
                        }
                    }

                    transitions.Add(new TracsTransition(to, lop, priority, hasExitTime, exitTime, exitTimeParamName, requiredLastState, conds));
                }
            }

            if (!string.IsNullOrWhiteSpace(name) && !outStates.ContainsKey(name))
            {
                outStates[name] = new TracsState(name, type, resName, resType, transitions);
            }

            if (entryStateName == null && string.Equals(type, "Entry", StringComparison.OrdinalIgnoreCase) && name.EndsWith("/Entry", StringComparison.OrdinalIgnoreCase))
            {
                entryStateName = name;
            }

            if (state.TryGetProperty("subStates", out var subs) && subs.ValueKind == JsonValueKind.Array)
            {
                foreach (var sub in subs.EnumerateArray())
                {
                    if (sub.ValueKind == JsonValueKind.Object)
                    {
                        WalkState(sub, outStates, ref entryStateName);
                    }
                }
            }
        }

        private static bool TryReadConditionValue(JsonElement condition, out string valueType, out object value)
        {
            valueType = string.Empty;
            value = 0;

            if (condition.TryGetProperty("value_type", out var vt) && vt.ValueKind == JsonValueKind.String)
            {
                valueType = vt.GetString() ?? "";
            }

            if (!condition.TryGetProperty("value", out var v) || v.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!v.TryGetProperty("value", out var vv))
            {
                return false;
            }

            switch (vv.ValueKind)
            {
                case JsonValueKind.Number:
                    if (vv.TryGetInt32(out var i))
                    {
                        value = i;
                    }
                    else
                    {
                        value = vv.GetDouble();
                    }
                    return true;
                case JsonValueKind.True:
                    value = true;
                    return true;
                case JsonValueKind.False:
                    value = false;
                    return true;
                default:
                    return false;
            }
        }

        private static bool TrySelectResourceLabel(MotionAnimDb db, Dictionary<string, object> parameters, out string resourceName)
        {
            resourceName = string.Empty;
            if (db.States.Count == 0)
            {
                return false;
            }

            var candidates = new List<string>();
            var visited = new HashSet<string>(StringComparer.Ordinal);
            string? current = db.EntryStateName;
            if (current == null || !db.States.ContainsKey(current))
            {
                current = db.States.Values.FirstOrDefault(s => string.Equals(s.Type, "Entry", StringComparison.OrdinalIgnoreCase))?.Name;
            }

            string? last = null;
            for (int step = 0; step < 64 && current != null; step++)
            {
                if (!visited.Add(current))
                {
                    break;
                }

                if (!db.States.TryGetValue(current, out var st))
                {
                    break;
                }

                // For hierarchical state machines, enter substates via Entry before evaluating parent transitions.
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

                if (!string.IsNullOrWhiteSpace(st.ResourceName) && string.Equals(st.ResourceType, "File", StringComparison.OrdinalIgnoreCase))
                {
                    candidates.Add(st.ResourceName!);
                }

                var next = ChooseTransition(db, current, last, st.Transitions, parameters);
                if (next != null)
                {
                    last = current;
                    current = next.ToStateName;
                    continue;
                }

                break;
            }

            if (candidates.Count > 0)
            {
                for (int i = candidates.Count - 1; i >= 0; i--)
                {
                    if (candidates[i].Contains("_loop", StringComparison.OrdinalIgnoreCase))
                    {
                        resourceName = candidates[i];
                        return true;
                    }
                }

                resourceName = candidates[candidates.Count - 1];
                return true;
            }

            if (candidates.Count == 0)
            {
                foreach (var st in db.States.Values)
                {
                    foreach (var tr in st.Transitions)
                    {
                        if (!IsTransitionSatisfied(tr, lastStateName: null, parameters))
                        {
                            continue;
                        }
                        CollectFileResourcesUnderPrefix(db, tr.ToStateName, candidates);
                    }
                }
            }

            if (candidates.Count == 0)
            {
                return false;
            }

            candidates = candidates.Distinct(StringComparer.Ordinal).ToList();
            candidates.Sort(StringComparer.OrdinalIgnoreCase);

            var loop = candidates.FirstOrDefault(c => c.Contains("_loop", StringComparison.OrdinalIgnoreCase));
            resourceName = loop ?? candidates.Last();
            return true;
        }

        private static void CollectFileResourcesUnderPrefix(MotionAnimDb db, string statePrefix, List<string> candidates)
        {
            if (string.IsNullOrWhiteSpace(statePrefix))
            {
                return;
            }

            if (db.States.TryGetValue(statePrefix, out var exact) &&
                !string.IsNullOrWhiteSpace(exact.ResourceName) &&
                string.Equals(exact.ResourceType, "File", StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add(exact.ResourceName!);
                return;
            }

            string prefix = statePrefix.EndsWith("/", StringComparison.Ordinal) ? statePrefix : statePrefix + "/";
            foreach (var st in db.States.Values)
            {
                if (!st.Name.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(st.ResourceName) && string.Equals(st.ResourceType, "File", StringComparison.OrdinalIgnoreCase))
                {
                    candidates.Add(st.ResourceName!);
                }
            }
        }

	        private static TracsTransition? ChooseTransition(MotionAnimDb db, string currentStateName, string? lastStateName, List<TracsTransition> transitions, Dictionary<string, object> parameters)
	        {
	            TracsTransition? best = null;
	            int bestPriority = int.MinValue, bestHasConds = -1, bestIsExit = 1, bestOrder = -1, order = 0;
	            bool preferConditional = db.States.TryGetValue(currentStateName, out var curState) && string.Equals(curState.Type, "Entry", StringComparison.OrdinalIgnoreCase);

            foreach (var stateName in EnumerateStateAndParents(currentStateName))
            {
                if (db.States.TryGetValue(stateName, out var st))
                {
                    Consider(st.Transitions);
                }

                foreach (var any in EnumerateAnyStatesInScope(db, stateName))
                {
                    Consider(any.Transitions);
                }
            }

            return best;

            void Consider(List<TracsTransition> list)
            {
                if (list == null || list.Count == 0)
                {
                    return;
                }

	                for (int i = 0; i < list.Count; i++)
	                {
	                    int curOrder = order++;
	                    var tr = list[i];
	                    if (!IsTransitionSatisfied(tr, lastStateName, parameters))
	                    {
	                        continue;
	                    }

	                    int pri = tr.Priority;
	                    int hasConds = tr.Conditions != null && tr.Conditions.Count > 0 ? 1 : 0;
	                    int isExit = tr.ToStateName.EndsWith("/Exit", StringComparison.OrdinalIgnoreCase) || (db.States.TryGetValue(tr.ToStateName, out var toState) && string.Equals(toState.Type, "Exit", StringComparison.OrdinalIgnoreCase)) ? 1 : 0;
	                    if (best == null ||
	                        (preferConditional && isExit < bestIsExit) ||
	                        (preferConditional && hasConds > bestHasConds) ||
	                        (preferConditional && hasConds == bestHasConds && pri > bestPriority) ||
	                        (!preferConditional && pri > bestPriority) ||
	                        (!preferConditional && pri == bestPriority && hasConds > bestHasConds) ||
	                        (pri == bestPriority && hasConds == bestHasConds && curOrder > bestOrder))
	                    {
	                        best = tr;
	                        bestPriority = pri;
	                        bestHasConds = hasConds;
	                        bestIsExit = isExit;
	                        bestOrder = curOrder;
	                    }
	                }
	            }
	        }

        private static IEnumerable<string> EnumerateStateAndParents(string stateName)
        {
            string? cur = stateName;
            while (!string.IsNullOrWhiteSpace(cur))
            {
                yield return cur;
                cur = GetParentPath(cur);
            }
        }

        private static IEnumerable<TracsState> EnumerateAnyStatesInScope(MotionAnimDb db, string stateOrScopeName)
        {
            var scope = GetParentPath(stateOrScopeName);
            if (string.IsNullOrWhiteSpace(scope))
            {
                scope = stateOrScopeName;
            }

            foreach (var st in db.States.Values)
            {
                if (!string.Equals(st.Type, "Any", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var anyScope = GetParentPath(st.Name);
                if (string.Equals(anyScope, scope, StringComparison.OrdinalIgnoreCase))
                {
                    yield return st;
                }
            }
        }

        private static string? GetParentPath(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            int idx = name.LastIndexOf('/');
            if (idx <= 0)
            {
                return null;
            }
            return name.Substring(0, idx);
        }

        private static bool IsTransitionSatisfied(TracsTransition tr, string? lastStateName, Dictionary<string, object> parameters)
        {
            // We don't model time yet; ignore exit-time gated transitions during resource selection.
            if (tr.HasExitTime)
            {
                return false;
            }
            if (!string.IsNullOrWhiteSpace(tr.ExitTimeParameterName))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(tr.RequiredLastState) &&
                !string.Equals(tr.RequiredLastState, lastStateName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (tr.Conditions == null || tr.Conditions.Count == 0)
            {
                return true;
            }

            bool acc = EvaluateCondition(tr.Conditions[0], parameters);
            for (int i = 1; i < tr.Conditions.Count; i++)
            {
                string op = tr.Conditions[i].LogicalOperator;
                if (string.IsNullOrWhiteSpace(op))
                {
                    op = tr.LogicalOperator;
                }

                bool next = EvaluateCondition(tr.Conditions[i], parameters);
                if (string.Equals(op, "Or", StringComparison.OrdinalIgnoreCase))
                {
                    acc |= next;
                }
                else
                {
                    acc &= next;
                }
            }

            return acc;
        }

        private static bool EvaluateCondition(TracsCondition c, Dictionary<string, object> parameters)
        {
            parameters.TryGetValue(c.ParameterName, out var paramValue);

            if (c.Value is bool b)
            {
                bool pv = paramValue is bool pb ? pb : false;
                return EvaluateBool(c.Func, pv, b);
            }

            if (c.Value is int i)
            {
                int pv = paramValue is int pi ? pi : paramValue is double pd ? (int)pd : 0;
                return EvaluateNumber(c.Func, pv, i);
            }

            if (c.Value is double f)
            {
                double pv = paramValue is double pf ? pf : paramValue is int pi ? pi : 0.0;
                return EvaluateNumber(c.Func, pv, f);
            }

            return false;
        }

        private static bool EvaluateBool(string func, bool left, bool right)
        {
            return func switch
            {
                "Equal" => left == right,
                "NotEqual" => left != right,
                _ => left == right
            };
        }

        private static bool EvaluateNumber<T>(string func, T left, T right) where T : IComparable<T>
        {
            int cmp = left.CompareTo(right);
            return func switch
            {
                "Less" => cmp < 0,
                "LEqual" => cmp <= 0,
                "Greater" => cmp > 0,
                "GEqual" => cmp >= 0,
                "Equal" => cmp == 0,
                "NotEqual" => cmp != 0,
                _ => cmp == 0
            };
        }

        private static bool TryGetFirstRootElement(JsonDocument doc, out JsonElement root)
        {
            root = default;
            if (!doc.RootElement.TryGetProperty("values", out var values) || values.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var group in values.EnumerateArray())
            {
                if (!group.TryGetProperty("root", out var r) || r.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var el in r.EnumerateArray())
                {
                    if (el.ValueKind == JsonValueKind.Object)
                    {
                        root = el;
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
