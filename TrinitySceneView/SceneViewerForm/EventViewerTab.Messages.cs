using System.Globalization;
using System.Text;

namespace TrinitySceneView
{
    public partial class SceneViewerForm
    {
        private void EventViewerOnAssetRootChanged()
        {
            if (!string.IsNullOrWhiteSpace(assetRoot) &&
                Directory.Exists(assetRoot) &&
                Directory.Exists(Path.Combine(assetRoot, "ik_message")))
            {
                eventMessageResolver = new EventMessageResolver(assetRoot, "English");
            }
            else
            {
                eventMessageResolver = null;
            }

            if (eventTimelineEntries.Count > 0)
            {
                RecomputeEventTimelineText();
                UpdateEventTimelineListSummaries();
                UpdateEventDetailsFromSelection();
            }
        }

        private void UpdateEventTimelineListSummaries()
        {
            if (eventTimelineListView == null) return;
            if (eventTimelineListView.Items.Count != eventTimelineEntries.Count) return;

            for (var i = 0; i < eventTimelineEntries.Count; i++)
            {
                var item = eventTimelineListView.Items[i];
                if (item.SubItems.Count >= 3)
                {
                    item.SubItems[2].Text = eventTimelineEntries[i].Summary ?? string.Empty;
                }
            }
        }

        private void RecomputeEventTimelineText()
        {
            foreach (var e in eventTimelineEntries)
            {
                e.Summary = FormatEventSummary(e.Method, e.Args);
                e.Details = BuildEventDetails(e.RawArgs, e.Args);
            }
        }

        private string FormatEventSummary(string method, List<object?> args)
        {
            switch (method)
            {
                case "Talk":
                {
                    var msgKey = args.Count > 1 ? args[1] as string : null;
                    if (!string.IsNullOrWhiteSpace(msgKey) && TryResolveMessage(msgKey!, out var text))
                    {
                        return $"Talk \"{MakeSnippet(text)}\"";
                    }
                    return $"Talk {EventSymLuaParser.FormatArg(args, 1)}";
                }
                case "Choice":
                {
                    if (args.Count > 0 && args[0] is List<object?> list)
                    {
                        var parts = new List<string>();
                        foreach (var v in list)
                        {
                            if (v is string s && TryResolveMessage(s, out var t))
                            {
                                parts.Add($"\"{MakeSnippet(t)}\"");
                            }
                            else
                            {
                                parts.Add(EventSymLuaParser.FormatValue(v));
                            }
                        }
                        return $"Choice {string.Join(" / ", parts)}";
                    }
                    return $"Choice {EventSymLuaParser.FormatArg(args, 0)}";
                }
                default:
                    return EventSymLuaParser.BuildSummary(method, args);
            }
        }

        private string BuildEventDetails(List<string> rawArgs, List<object?> resolved)
        {
            var sb = new StringBuilder();
            if (rawArgs.Count > 0)
            {
                sb.AppendLine("Args:");
                for (int i = 0; i < rawArgs.Count; i++)
                {
                    sb.Append("  [");
                    sb.Append(i.ToString(CultureInfo.InvariantCulture));
                    sb.Append("] ");
                    sb.Append(rawArgs[i]);
                    sb.Append(" => ");
                    sb.AppendLine(EventSymLuaParser.FormatValue(i < resolved.Count ? resolved[i] : null));
                }
            }

            var msgKeys = FindMsgKeys(resolved);
            if (msgKeys.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Text:");
                foreach (var key in msgKeys)
                {
                    if (!TryResolveMessage(key, out var text)) continue;
                    sb.Append("  ");
                    sb.Append(key);
                    sb.AppendLine(":");
                    foreach (var line in text.Replace("\r", "").Split('\n'))
                    {
                        sb.Append("    ");
                        sb.AppendLine(line);
                    }
                }
            }

            return sb.ToString().TrimEnd();
        }

        private static List<string> FindMsgKeys(List<object?> args)
        {
            var keys = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            void Add(string s)
            {
                if (!s.StartsWith("msg_", StringComparison.Ordinal)) return;
                if (seen.Add(s)) keys.Add(s);
            }

            foreach (var a in args)
            {
                if (a is string s) Add(s);
                else if (a is List<object?> list)
                {
                    foreach (var v in list)
                    {
                        if (v is string ss) Add(ss);
                    }
                }
            }

            return keys;
        }

        private bool TryResolveMessage(string msgKey, out string text)
        {
            text = string.Empty;
            if (eventMessageResolver == null) return false;
            return eventMessageResolver.TryResolve(msgKey, out text);
        }

        private static string MakeSnippet(string text)
        {
            var flat = FlattenWhitespace(text);
            const int max = 110;
            if (flat.Length <= max) return flat;
            return flat.Substring(0, max - 1) + "…";
        }

        private static string FlattenWhitespace(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new StringBuilder(s.Length);
            var prevSpace = false;
            foreach (var ch in s)
            {
                var isWs = ch == '\n' || ch == '\t' || ch == ' ' || ch == '\u00A0' || ch == '\u202F';
                if (isWs)
                {
                    if (!prevSpace) sb.Append(' ');
                    prevSpace = true;
                }
                else
                {
                    sb.Append(ch);
                    prevSpace = false;
                }
            }
            return sb.ToString().Trim();
        }
    }
}
