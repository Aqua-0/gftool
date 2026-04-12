using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using GFTool.Renderer.Core;
using Timer = System.Windows.Forms.Timer;

namespace TrinitySceneView
{
    public partial class SceneViewerForm
    {
        private TabPage? tabEvent;
        private ListView? eventTimelineListView;
        private TextBox? eventDetailsTextBox;
        private Label? eventScriptPathLabel;
        private Button? eventPlayPauseButton;
        private Button? eventStepButton;
        private Button? eventBackButton;
        private Button? eventResetButton;
        private CheckBox? eventShowCameraCheckBox;
        private CheckBox? eventUseCameraCheckBox;
        private Timer? eventPlaybackTimer;
        private EventMessageResolver? eventMessageResolver;

        private string? loadedEventScriptPath;
        private string? lastOpenedEventScriptPath;
        private List<EventTimelineEntry> eventTimelineEntries = new();
        private int eventTimelineIndex;
        private bool eventIsPlaying;

        private void InitializeEventViewerTab()
        {
            if (rightTabs == null)
            {
                return;
            }

            tabEvent = new TabPage("Event");
            tabEvent.Padding = new Padding(6);

            var outer = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterDistance = 520
            };

            var topPanel = new Panel { Dock = DockStyle.Fill };
            var controlsRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 32,
                AutoSize = false,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false
            };

            var openButton = new Button { Text = "Open Script...", Width = 110, Height = 24 };
            openButton.Click += (_, _) => OpenEventScript();

            eventPlayPauseButton = new Button { Text = "Play", Width = 60, Height = 24, Enabled = false };
            eventPlayPauseButton.Click += (_, _) => ToggleEventPlayPause();

            eventBackButton = new Button { Text = "<", Width = 36, Height = 24, Enabled = false };
            eventBackButton.Click += (_, _) => StepEvent(-1);

            eventStepButton = new Button { Text = ">", Width = 36, Height = 24, Enabled = false };
            eventStepButton.Click += (_, _) => StepEvent(+1);

            eventResetButton = new Button { Text = "Reset", Width = 60, Height = 24, Enabled = false };
            eventResetButton.Click += (_, _) => ResetEventPlayback();

            eventShowCameraCheckBox = new CheckBox { Text = "Show event cam", AutoSize = true, Checked = false };
            eventShowCameraCheckBox.CheckedChanged += (_, _) => OnEventCameraTogglesChanged();

            eventUseCameraCheckBox = new CheckBox { Text = "Use event cam", AutoSize = true, Checked = false };
            eventUseCameraCheckBox.CheckedChanged += (_, _) => OnEventCameraTogglesChanged();

            eventScriptPathLabel = new Label
            {
                AutoSize = false,
                Text = "(no script loaded)",
                TextAlign = ContentAlignment.MiddleLeft,
                Width = 1000,
                Height = 24
            };

            controlsRow.Controls.Add(openButton);
            controlsRow.Controls.Add(eventPlayPauseButton);
            controlsRow.Controls.Add(eventBackButton);
            controlsRow.Controls.Add(eventStepButton);
            controlsRow.Controls.Add(eventResetButton);
            controlsRow.Controls.Add(eventShowCameraCheckBox);
            controlsRow.Controls.Add(eventUseCameraCheckBox);
            controlsRow.Controls.Add(eventScriptPathLabel);

            eventTimelineListView = new ListView
            {
                Dock = DockStyle.Fill,
                FullRowSelect = true,
                GridLines = true,
                View = View.Details
            };
            eventTimelineListView.Columns.Add("#", 50);
            eventTimelineListView.Columns.Add("Call", 150);
            eventTimelineListView.Columns.Add("Summary", 600);
            eventTimelineListView.Columns.Add("Line", 70);
            eventTimelineListView.SelectedIndexChanged += (_, _) => UpdateEventDetailsFromSelection();

            topPanel.Controls.Add(eventTimelineListView);
            topPanel.Controls.Add(controlsRow);

            eventDetailsTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false
            };

            outer.Panel1.Controls.Add(topPanel);
            outer.Panel2.Controls.Add(eventDetailsTextBox);

            tabEvent.Controls.Add(outer);
            rightTabs.TabPages.Add(tabEvent);

            eventPlaybackTimer = new Timer { Interval = 200, Enabled = false };
            eventPlaybackTimer.Tick += (_, _) =>
            {
                TickEventPlayback();
            };
            ApplyTheme(tabEvent);
            EventViewerOnAssetRootChanged();
        }

        private void OpenEventScript()
        {
            using var ofd = new OpenFileDialog
            {
                Filter = "Symbolized Lua (*.lua.sym.lua)|*.lua.sym.lua|Lua (*.lua)|*.lua|All files (*.*)|*.*",
                Multiselect = false,
                RestoreDirectory = true
            };
            if (!string.IsNullOrWhiteSpace(lastOpenedEventScriptPath))
            {
                try
                {
                    var dir = Path.GetDirectoryName(lastOpenedEventScriptPath);
                    if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                    {
                        ofd.InitialDirectory = dir;
                    }
                }
                catch { }
            }
            if (ofd.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            lastOpenedEventScriptPath = ofd.FileName;
            TryLoadEventScript(ofd.FileName);
        }

        private void TryLoadEventScript(string path)
        {
            try
            {
                string text = File.ReadAllText(path, Encoding.UTF8);
                loadedEventScriptPath = path;
                eventTimelineEntries = EventSymLuaParser.Parse(text);
                RecomputeEventTimelineText();
                eventTimelineIndex = 0;
                PopulateEventTimelineList();
                UpdateEventPlaybackUiEnabled(eventTimelineEntries.Count > 0);
                UpdateNpcActorsUiEnabled(eventTimelineEntries.Count > 0);
                SetEventPlaying(false);

                if (eventScriptPathLabel != null)
                {
                    eventScriptPathLabel.Text = Path.GetFileName(path);
                }

                if (eventTimelineEntries.Count > 0)
                {
                    SelectEventTimelineIndex(0);
                    RefreshNpcActorsFromEventTimeline();
                }
                else
                {
                    MessageHandler.Instance.AddMessage(MessageType.WARNING, $"[Event] Loaded 0 timeline entries from '{Path.GetFileName(path)}'.");
                }
            }
            catch (Exception ex)
            {
                MessageHandler.Instance.AddMessage(MessageType.WARNING, $"[Event] Failed to load script '{path}': {ex.GetType().Name}: {ex.Message}");
                loadedEventScriptPath = null;
                eventTimelineEntries = new List<EventTimelineEntry>();
                PopulateEventTimelineList();
                UpdateEventPlaybackUiEnabled(false);
                UpdateNpcActorsUiEnabled(false);
            }
        }

        private void PopulateEventTimelineList()
        {
            if (eventTimelineListView == null)
            {
                return;
            }

            eventTimelineListView.BeginUpdate();
            try
            {
                eventTimelineListView.Items.Clear();
                for (int i = 0; i < eventTimelineEntries.Count; i++)
                {
                    var e = eventTimelineEntries[i];
                    var item = new ListViewItem((i + 1).ToString(CultureInfo.InvariantCulture));
                    item.SubItems.Add(e.Method);
                    item.SubItems.Add(e.Summary ?? string.Empty);
                    item.SubItems.Add(e.SourceLine > 0 ? e.SourceLine.ToString(CultureInfo.InvariantCulture) : string.Empty);
                    item.Tag = i;
                    eventTimelineListView.Items.Add(item);
                }
            }
            finally
            {
                eventTimelineListView.EndUpdate();
            }
        }

        private void UpdateEventPlaybackUiEnabled(bool enabled)
        {
            if (eventPlayPauseButton != null) eventPlayPauseButton.Enabled = enabled;
            if (eventStepButton != null) eventStepButton.Enabled = enabled;
            if (eventBackButton != null) eventBackButton.Enabled = enabled;
            if (eventResetButton != null) eventResetButton.Enabled = enabled;
        }

        private void ToggleEventPlayPause()
        {
            SetEventPlaying(!eventIsPlaying);
        }

        private void SetEventPlaying(bool playing)
        {
            eventIsPlaying = playing;
            if (eventPlayPauseButton != null)
            {
                eventPlayPauseButton.Text = playing ? "Pause" : "Play";
            }
            if (eventPlaybackTimer != null)
            {
                eventPlaybackTimer.Enabled = playing;
            }
        }

        private void ResetEventPlayback()
        {
            SetEventPlaying(false);
            eventTimelineIndex = 0;
            SelectEventTimelineIndex(0);
        }

        private void StepEvent(int delta)
        {
            if (eventTimelineEntries.Count == 0)
            {
                return;
            }

            int next = Math.Clamp(eventTimelineIndex + delta, 0, eventTimelineEntries.Count - 1);
            eventTimelineIndex = next;
            SelectEventTimelineIndex(next);
        }

        private void SelectEventTimelineIndex(int index)
        {
            if (eventTimelineListView == null)
            {
                return;
            }

            if (index < 0 || index >= eventTimelineListView.Items.Count)
            {
                return;
            }

            eventTimelineListView.SelectedIndices.Clear();
            eventTimelineListView.Items[index].Selected = true;
            eventTimelineListView.Items[index].Focused = true;
            eventTimelineListView.EnsureVisible(index);
            UpdateEventDetails(index);
            OnEventTimelineIndexChanged();
        }

        private void UpdateEventDetailsFromSelection()
        {
            if (eventTimelineListView == null)
            {
                return;
            }

            if (eventTimelineListView.SelectedItems.Count == 0)
            {
                return;
            }

            if (eventTimelineListView.SelectedItems[0].Tag is int index)
            {
                eventTimelineIndex = index;
                UpdateEventDetails(index);
                OnEventTimelineIndexChanged();
            }
        }

        private void UpdateEventDetails(int index)
        {
            if (eventDetailsTextBox == null)
            {
                return;
            }

            if (index < 0 || index >= eventTimelineEntries.Count)
            {
                eventDetailsTextBox.Text = string.Empty;
                return;
            }

            var e = eventTimelineEntries[index];
            var sb = new StringBuilder();
            sb.AppendLine($"#{index + 1}: {e.Method}");
            if (!string.IsNullOrWhiteSpace(e.Summary))
            {
                sb.AppendLine(e.Summary);
            }
            if (e.SourceLine > 0)
            {
                sb.AppendLine($"Source line: {e.SourceLine}");
            }
            if (!string.IsNullOrWhiteSpace(e.Details))
            {
                sb.AppendLine();
                sb.AppendLine(e.Details);
            }
            eventDetailsTextBox.Text = sb.ToString();
        }

        private sealed class EventTimelineEntry
        {
            public int SourceLine { get; }
            public string Method { get; }
            public List<string> RawArgs { get; }
            public List<object?> Args { get; }
            public string? Summary { get; set; }
            public string? Details { get; set; }

            public EventTimelineEntry(int sourceLine, string method, List<string> rawArgs, List<object?> args)
            {
                SourceLine = sourceLine;
                Method = method;
                RawArgs = rawArgs;
                Args = args;
            }
        }

        private static class EventSymLuaParser
        {
            private static readonly Regex AssignString = new(@"^\s*(L\d+_\d+)\s*=\s*""([^""]*)""\s*$", RegexOptions.Compiled);
            private static readonly Regex AssignNumber = new(@"^\s*(L\d+_\d+)\s*=\s*(-?\d+(?:\.\d+)?)\s*$", RegexOptions.Compiled);
            private static readonly Regex AssignBool = new(@"^\s*(L\d+_\d+)\s*=\s*(true|false)\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            private static readonly Regex AssignEmptyTable = new(@"^\s*(L\d+_\d+)\s*=\s*\{\}\s*$", RegexOptions.Compiled);
            private static readonly Regex AssignHxTabArray = new(@"^\s*(L\d+_\d+)\s*=\s*_hx_tab_array\s*$", RegexOptions.Compiled);
            private static readonly Regex TableIndexAssign = new(@"^\s*(L\d+_\d+)\[(\d+)\]\s*=\s*(L\d+_\d+|""[^""]*""|-?\d+(?:\.\d+)?|true|false)\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            private static readonly Regex AssignFromBase = new(@"^\s*(L\d+_\d+)\s*=\s*(C[0-9A-F]{16})\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            private static readonly Regex FetchMethod = new(@"^\s*(L\d+_\d+)\s*=\s*\1\.(\w+)\s*$", RegexOptions.Compiled);
            private static readonly Regex CallReg = new(@"^\s*(L\d+_\d+)\((.*)\)\s*$", RegexOptions.Compiled);
            private static readonly Regex HxTabArrayCall = new(@"^\s*(L\d+_\d+)\s*=\s*\1\((L\d+_\d+)\s*,\s*(L\d+_\d+)\)\s*$", RegexOptions.Compiled);

            private sealed class TableLiteral
            {
                public Dictionary<int, object?> Values { get; } = new();
            }

            public static List<EventTimelineEntry> Parse(string text)
            {
                var entries = new List<EventTimelineEntry>();
                var literal = new Dictionary<string, object?>(StringComparer.Ordinal);
                var tables = new Dictionary<string, TableLiteral>(StringComparer.Ordinal);

                var baseForReg = new Dictionary<string, string>(StringComparer.Ordinal);
                string? pendingReg = null;
                string? pendingBase = null;
                string? pendingMethod = null;
                int pendingFetchLine = 0;

                var lines = text.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].TrimEnd('\r');
                    if (line.StartsWith("--", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var mStr = AssignString.Match(line);
                    if (mStr.Success)
                    {
                        literal[mStr.Groups[1].Value] = mStr.Groups[2].Value;
                        continue;
                    }

                    var mNum = AssignNumber.Match(line);
                    if (mNum.Success)
                    {
                        if (double.TryParse(mNum.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                        {
                            literal[mNum.Groups[1].Value] = v;
                        }
                        continue;
                    }

                    var mBool = AssignBool.Match(line);
                    if (mBool.Success)
                    {
                        literal[mBool.Groups[1].Value] = string.Equals(mBool.Groups[2].Value, "true", StringComparison.OrdinalIgnoreCase);
                        continue;
                    }

                    var mTbl = AssignEmptyTable.Match(line);
                    if (mTbl.Success)
                    {
                        var reg = mTbl.Groups[1].Value;
                        var t = new TableLiteral();
                        tables[reg] = t;
                        // Many event scripts build vec3 tables via `{}` + `[1]=...` assignments.
                        // Keep a reference in the literal map so calls can resolve arguments deterministically.
                        literal[reg] = t;
                        continue;
                    }

                    var mHx = AssignHxTabArray.Match(line);
                    if (mHx.Success)
                    {
                        literal[mHx.Groups[1].Value] = "__hx_tab_array";
                        continue;
                    }

                    var mIdx = TableIndexAssign.Match(line);
                    if (mIdx.Success)
                    {
                        string tbl = mIdx.Groups[1].Value;
                        if (!tables.TryGetValue(tbl, out var t))
                        {
                            t = new TableLiteral();
                            tables[tbl] = t;
                            literal[tbl] = t;
                        }

                        int idx = int.Parse(mIdx.Groups[2].Value, CultureInfo.InvariantCulture);
                        string rhs = mIdx.Groups[3].Value;
                        t.Values[idx] = ResolveToken(rhs, literal);
                        continue;
                    }

                    var mHxCall = HxTabArrayCall.Match(line);
                    if (mHxCall.Success)
                    {
                        string reg = mHxCall.Groups[1].Value;
                        if (literal.TryGetValue(reg, out var tag) && tag is string s && s == "__hx_tab_array")
                        {
                            string tableReg = mHxCall.Groups[2].Value;
                            if (tables.TryGetValue(tableReg, out var t))
                            {
                                var list = t.Values
                                    .OrderBy(kv => kv.Key)
                                    .Select(kv => kv.Value)
                                    .ToList();
                                literal[reg] = list;
                            }
                        }
                        continue;
                    }

                    var mBase = AssignFromBase.Match(line);
                    if (mBase.Success)
                    {
                        baseForReg[mBase.Groups[1].Value] = mBase.Groups[2].Value.ToUpperInvariant();
                        continue;
                    }

                    var mFetch = FetchMethod.Match(line);
                    if (mFetch.Success)
                    {
                        string reg = mFetch.Groups[1].Value;
                        if (baseForReg.TryGetValue(reg, out var b))
                        {
                            pendingReg = reg;
                            pendingBase = b;
                            pendingMethod = mFetch.Groups[2].Value;
                            pendingFetchLine = i + 1;
                        }
                        continue;
                    }

                    var mCall = CallReg.Match(line);
                    if (mCall.Success && pendingReg != null && pendingMethod != null && pendingBase != null)
                    {
                        string callReg = mCall.Groups[1].Value;
                        if (!string.Equals(callReg, pendingReg, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        var args = SplitArgs(mCall.Groups[2].Value);
                        var resolved = args.Select(a => ResolveToken(a, literal)).ToList();

                        entries.Add(new EventTimelineEntry(
                            sourceLine: pendingFetchLine > 0 ? pendingFetchLine : i + 1,
                            method: pendingMethod,
                            rawArgs: args,
                            args: resolved));

                        pendingReg = pendingBase = pendingMethod = null;
                        pendingFetchLine = 0;
                        continue;
                    }
                }

                return entries;
            }

            private static List<string> SplitArgs(string args)
            {
                if (string.IsNullOrWhiteSpace(args))
                {
                    return new List<string>();
                }

                return args.Split(',')
                    .Select(s => s.Trim())
                    .Where(s => s.Length > 0)
                    .ToList();
            }

            private static object? ResolveToken(string token, Dictionary<string, object?> literal)
            {
                if (string.IsNullOrWhiteSpace(token))
                {
                    return null;
                }

                token = token.Trim();
                if (literal.TryGetValue(token, out var v))
                {
                    if (v is TableLiteral t)
                    {
                        return t.Values
                            .OrderBy(kv => kv.Key)
                            .Select(kv => kv.Value)
                            .ToList();
                    }
                    return v;
                }

                if (token.StartsWith("\"", StringComparison.Ordinal) && token.EndsWith("\"", StringComparison.Ordinal) && token.Length >= 2)
                {
                    return token.Substring(1, token.Length - 2);
                }

                if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var num))
                {
                    return num;
                }

                if (string.Equals(token, "true", StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(token, "false", StringComparison.OrdinalIgnoreCase)) return false;

                return token;
            }

            public static string BuildSummary(string method, List<object?> args)
            {
                switch (method)
                {
                    case "Talk":
                        // Talk(windowType, msgId, quest, speaker?)
                        return $"Talk {FormatArg(args, 1)}";
                    case "Choice":
                        // Choice([msgId...], defaultIndex, allowCancel?, ???)
                        return $"Choice {FormatArg(args, 0)}";
                    case "Wait":
                        return "Wait";
                    case "WaitSeconds":
                        return $"WaitSeconds {FormatArg(args, 0)}";
                    case "Fade_In":
                    case "Fade_Out":
                    case "Wait_Fade":
                        return $"{method} {FormatArg(args, 0)}";
                    case "Camera":
                    case "Camera_SetEasy_AnyAngle_Template":
                    case "Camera_Shake":
                    case "Wait_Camera":
                        return $"{method}";
                    case "SetMotion":
                        return $"SetMotion {FormatArg(args, 0)} {FormatArg(args, 1)}";
                    case "SetFace":
                        return $"SetFace {FormatArg(args, 0)} {FormatArg(args, 1)}";
                    case "SetPosRot":
                    case "SetPlayerPosRot":
                        return $"{method} {FormatArg(args, 0)}";
                    case "LookAt":
                        return $"LookAt {FormatArg(args, 0)} -> {FormatArg(args, 1)}";
                    case "Wait_FObj_Action":
                        return args.Count > 0 ? $"Wait_FObj_Action({FormatArg(args, 0)})" : "Wait_FObj_Action()";
                    default:
                        return $"{method}";
                }
            }

            private static string BuildDetails(List<string> rawArgs, List<object?> resolved)
            {
                var sb = new StringBuilder();
                if (rawArgs.Count > 0)
                {
                    sb.AppendLine("Args:");
                    for (int i = 0; i < rawArgs.Count; i++)
                    {
                        sb.Append("  [");
                        sb.Append(i);
                        sb.Append("] ");
                        sb.Append(rawArgs[i]);
                        sb.Append(" => ");
                        sb.AppendLine(FormatValue(i < resolved.Count ? resolved[i] : null));
                    }
                }

                return sb.ToString().TrimEnd();
            }

            public static string FormatArg(List<object?> args, int idx)
            {
                if (idx < 0 || idx >= args.Count)
                {
                    return "(missing)";
                }

                return FormatValue(args[idx]);
            }

            public static string FormatValue(object? value)
            {
                if (value == null) return "(null)";

                if (value is double d)
                {
                    return d.ToString("0.###", CultureInfo.InvariantCulture);
                }

                if (value is bool b)
                {
                    return b ? "true" : "false";
                }

                if (value is List<object?> list)
                {
                    var inner = string.Join(", ", list.Select(FormatValue));
                    return $"[{inner}]";
                }

                return value.ToString() ?? string.Empty;
            }
        }
    }
}
