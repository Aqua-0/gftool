using BnTxx;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Titan.Resource;
using Trinity.Core.Utils;

namespace TrinityModelViewer.UI.Dialogs
{
    public sealed class PokemonLoaderForm : Form
    {
        public enum GameKind
        {
            ZA,
            SV
        }

        private sealed class PokemonRow
        {
            public ushort Species;
            public ushort Form;
            public byte Gender;
            public string Name = string.Empty;
            public string ModelRel = string.Empty;
            public string IconRel = string.Empty;
            public string IconKey = string.Empty;
            public string IconFullPath = string.Empty;
            public bool ModelExists;

            public string DisplayName =>
                !string.IsNullOrWhiteSpace(Name) ? Name : $"(species {Species})";
        }

        private readonly string gameRoot;
        private readonly Func<string, Task> loadModelAsync;
        private readonly GameKind gameKind;

        private readonly TextBox searchBox = new TextBox();
        private readonly Button refreshButton = new Button();
        private readonly CheckBox showIconsCheckBox = new CheckBox();
        private readonly Label statusLabel = new Label();
        private readonly ListView listView = new ListView();
        private readonly Button loadButton = new Button();
        private readonly Button closeButton = new Button();
        private readonly ImageList smallIcons = new ImageList();

        private readonly object rowsGate = new object();
        private PokemonRow[] allRows = Array.Empty<PokemonRow>();
        private PokemonRow[] filteredRows = Array.Empty<PokemonRow>();
        private CancellationTokenSource? iconLoadCts;
        private readonly Dictionary<string, string> normalizedPathCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> iconIndexByRelPath = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<int>> listItemIndicesByIconKey = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        private int uniqueIconsQueued;
        private int iconsLoaded;
        private int iconsFailed;
        private int iconsMissing;
        private int iconsCached;
        private int iconsDecoded;
        private string lastIconError = string.Empty;
        private readonly string iconCacheRoot;
        private readonly int iconSizePx;

        public PokemonLoaderForm(GameKind gameKind, string gameRoot, Func<string, Task> loadModelAsync)
        {
            this.gameKind = gameKind;
            this.gameRoot = gameRoot ?? string.Empty;
            this.loadModelAsync = loadModelAsync ?? throw new ArgumentNullException(nameof(loadModelAsync));
            iconSizePx = gameKind == GameKind.SV ? 32 : 24;
            iconCacheRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TrinityModelViewer",
                "Cache",
                "PokemonIcons",
                gameKind.ToString());

            Text = $"Pokemon Loader ({gameKind})";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(820, 520);

            smallIcons.ColorDepth = ColorDepth.Depth32Bit;
            smallIcons.ImageSize = new Size(iconSizePx, iconSizePx);
            using (var placeholder = new Bitmap(iconSizePx, iconSizePx))
            using (var g = Graphics.FromImage(placeholder))
            using (var pen = new Pen(Color.FromArgb(110, 110, 110)))
            {
                g.Clear(Color.FromArgb(50, 50, 50));
                g.DrawRectangle(pen, 0, 0, Math.Max(0, iconSizePx - 1), Math.Max(0, iconSizePx - 1));
                smallIcons.Images.Add("placeholder", (Bitmap)placeholder.Clone());
            }

            listView.View = View.Details;
            listView.FullRowSelect = true;
            listView.HideSelection = false;
            listView.MultiSelect = false;
            listView.UseCompatibleStateImageBehavior = false;
            listView.SmallImageList = smallIcons;
            listView.Columns.Add("Name", gameKind == GameKind.SV ? 240 : 220);
            listView.Columns.Add("Species", 70);
            listView.Columns.Add("Form", 50);
            listView.Columns.Add("Gender", 60);
            listView.Columns.Add("Model", 340);
            listView.DoubleClick += async (s, e) => await LoadSelectedAsync();

            searchBox.PlaceholderText = "Search by name / species id / pm#### ...";
            searchBox.Dock = DockStyle.Fill;
            searchBox.TextChanged += (s, e) => ApplyFilterAndRefresh();

            refreshButton.Text = "Refresh";
            refreshButton.AutoSize = true;
            refreshButton.Click += async (s, e) => await LoadCatalogAsync();

            showIconsCheckBox.Text = "Icons";
            showIconsCheckBox.Checked = true;
            showIconsCheckBox.AutoSize = true;
            showIconsCheckBox.CheckedChanged += (s, e) => ApplyFilterAndRefresh();

            statusLabel.Text = "Loading...";
            statusLabel.AutoSize = true;
            statusLabel.Dock = DockStyle.Fill;
            statusLabel.Click += (s, e) =>
            {
                if (statusLabel.Tag is string err && !string.IsNullOrWhiteSpace(err))
                {
                    MessageBox.Show(this, err, "Pokemon Loader (Icons)", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            };

            loadButton.Text = "Load";
            loadButton.AutoSize = true;
            loadButton.Enabled = false;
            loadButton.Click += async (s, e) => await LoadSelectedAsync();

            closeButton.Text = "Close";
            closeButton.AutoSize = true;
            closeButton.Click += (s, e) => Close();

            listView.SelectedIndexChanged += (s, e) =>
            {
                loadButton.Enabled = listView.SelectedItems.Count > 0;
            };

            var top = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 5,
                RowCount = 2,
                Padding = new Padding(8),
            };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var rootLabel = new Label { Text = "Active Root:", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill };
            var rootValue = new TextBox
            {
                ReadOnly = true,
                Text = this.gameRoot,
                Dock = DockStyle.Fill
            };

            top.Controls.Add(rootLabel, 0, 0);
            top.Controls.Add(rootValue, 1, 0);
            top.Controls.Add(refreshButton, 2, 0);
            top.Controls.Add(showIconsCheckBox, 3, 0);

            var searchLabel = new Label { Text = "Search:", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill };
            top.Controls.Add(searchLabel, 0, 1);
            top.Controls.Add(searchBox, 1, 1);
            top.Controls.Add(statusLabel, 2, 1);
            top.SetColumnSpan(statusLabel, 3);

            var bottom = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(8),
                WrapContents = false,
            };
            bottom.Controls.Add(closeButton);
            bottom.Controls.Add(loadButton);

            var center = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
            listView.Dock = DockStyle.Fill;
            center.Controls.Add(listView);

            Controls.Add(center);
            Controls.Add(bottom);
            Controls.Add(top);

            Shown += async (s, e) => await LoadCatalogAsync();
            FormClosed += (s, e) => CancelIconLoads();
        }

        private async Task LoadCatalogAsync()
        {
            refreshButton.Enabled = false;
            loadButton.Enabled = false;
            statusLabel.Text = "Loading...";
            statusLabel.Tag = null;
            statusLabel.Cursor = Cursors.Default;

            try
            {
                CancelIconLoads();
                iconsLoaded = 0;
                iconsFailed = 0;
                iconsMissing = 0;
                iconsCached = 0;
                iconsDecoded = 0;
                lastIconError = string.Empty;
                listView.BeginUpdate();
                listView.Items.Clear();
                listView.EndUpdate();

                var rows = await Task.Run(() => BuildRows(gameRoot));
                lock (rowsGate)
                {
                    allRows = rows;
                }

                ApplyFilterAndRefresh();
            }
            catch (Exception ex)
            {
                statusLabel.Text = "Failed";
                MessageBox.Show(this, $"Failed to load Pokemon list:\n{ex.Message}", "Pokemon Loader", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                refreshButton.Enabled = true;
            }
        }

        private void ApplyFilterAndRefresh()
        {
            PokemonRow[] snapshot;
            lock (rowsGate)
            {
                snapshot = allRows;
            }

            string q = (searchBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(q))
            {
                filteredRows = snapshot;
            }
            else
            {
                filteredRows = snapshot.Where(r => MatchesQuery(r, q)).ToArray();
            }

            listView.BeginUpdate();
            try
            {
                listView.Items.Clear();
                listItemIndicesByIconKey.Clear();
                for (int i = 0; i < filteredRows.Length; i++)
                {
                    var r = filteredRows[i];
                    var item = new ListViewItem(r.DisplayName)
                    {
                        Tag = r,
                        ImageKey = "placeholder"
                    };
                    item.SubItems.Add(r.Species.ToString());
                    item.SubItems.Add(r.Form.ToString());
                    item.SubItems.Add(r.Gender.ToString());
                    item.SubItems.Add(r.ModelRel);

                    if (!r.ModelExists)
                    {
                        item.ForeColor = Color.Gray;
                    }

                    // If we don't have an icon for this entry, clear ImageKey so the placeholder doesn't render.
                    if (string.IsNullOrWhiteSpace(r.IconKey))
                    {
                        item.ImageKey = string.Empty;
                    }
                    else
                    {
                        if (!listItemIndicesByIconKey.TryGetValue(r.IconKey, out var indices))
                        {
                            indices = new List<int>();
                            listItemIndicesByIconKey[r.IconKey] = indices;
                        }
                        indices.Add(i);
                    }

                    listView.Items.Add(item);
                }
            }
            finally
            {
                listView.EndUpdate();
            }

            statusLabel.Text = $"{filteredRows.Length} shown / {snapshot.Length} total";
            loadButton.Enabled = listView.SelectedItems.Count > 0;
            UpdateIconStatus();

            if (showIconsCheckBox.Checked)
            {
                QueueIconLoadsForVisibleRows();
            }
            else
            {
                statusLabel.Tag = null;
                statusLabel.Cursor = Cursors.Default;
            }
        }

        private void UpdateIconStatus()
        {
            string baseStatus = $"{filteredRows.Length} shown / {allRows.Length} total";
            if (!showIconsCheckBox.Checked)
            {
                statusLabel.Text = baseStatus;
                return;
            }

            int loaded = iconsLoaded;
            int missing = iconsMissing;
            int failed = iconsFailed;
            int cached = iconsCached;
            int decoded = iconsDecoded;
            if (loaded == 0 && missing == 0 && failed == 0)
            {
                statusLabel.Text = baseStatus;
            }
            else
            {
                int queued = uniqueIconsQueued;
                string extra = queued > 0 ? $"icons: {loaded}/{queued} ok" : $"icons: {loaded} ok";
                if (cached > 0 || decoded > 0)
                {
                    extra += $" ({cached} cached, {decoded} decoded)";
                }
                if (missing > 0) extra += $", {missing} missing";
                if (failed > 0) extra += $", {failed} failed";
                statusLabel.Text = $"{baseStatus}  ({extra})";
            }

            if (!string.IsNullOrWhiteSpace(lastIconError))
            {
                statusLabel.Tag = lastIconError;
                statusLabel.Cursor = Cursors.Hand;
            }
            else
            {
                statusLabel.Tag = null;
                statusLabel.Cursor = Cursors.Default;
            }
        }

        private static bool MatchesQuery(PokemonRow row, string q)
        {
            if (q.Length == 0)
            {
                return true;
            }

            if (ushort.TryParse(q, out var n))
            {
                if (row.Species == n)
                {
                    return true;
                }
            }

            return row.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                   row.ModelRel.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                   row.IconRel.Contains(q, StringComparison.OrdinalIgnoreCase);
        }

        private async Task LoadSelectedAsync()
        {
            if (listView.SelectedItems.Count == 0)
            {
                return;
            }

            if (listView.SelectedItems[0].Tag is not PokemonRow row)
            {
                return;
            }

            string full = ResolveRootRelative(row.ModelRel);
            if (!File.Exists(full))
            {
                MessageBox.Show(this, $"Missing model file:\n{full}", "Pokemon Loader", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            loadButton.Enabled = false;
            refreshButton.Enabled = false;
            statusLabel.Text = "Loading model...";
            try
            {
                await loadModelAsync(full);
                Close();
            }
            finally
            {
                loadButton.Enabled = true;
                refreshButton.Enabled = true;
            }
        }

        private void QueueIconLoadsForVisibleRows()
        {
            CancelIconLoads();
            iconLoadCts = new CancellationTokenSource();
            var token = iconLoadCts.Token;
            uniqueIconsQueued = 0;
            iconsLoaded = 0;
            iconsFailed = 0;
            iconsMissing = 0;
            iconsCached = 0;
            iconsDecoded = 0;
            lastIconError = string.Empty;
            UpdateIconStatus();

            var rels = new List<string>();
            var relSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < filteredRows.Length; i++)
            {
                var key = filteredRows[i].IconKey;
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                if (iconIndexByRelPath.ContainsKey(key))
                {
                    continue;
                }

                if (relSet.Add(key))
                {
                    rels.Add(key);
                }
            }

            if (rels.Count == 0)
            {
                return;
            }

            uniqueIconsQueued = rels.Count;
            UpdateIconStatus();

            _ = Task.Run(async () =>
            {
                var sem = new SemaphoreSlim(Math.Clamp(Environment.ProcessorCount, 4, 12));
                var tasks = new List<Task>();
                try
                {
                    foreach (var iconKey in rels)
                    {
                        if (token.IsCancellationRequested)
                        {
                            break;
                        }

                        try
                        {
                            await sem.WaitAsync(token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }

                        tasks.Add(Task.Run(() =>
                        {
                            try
                            {
                                if (token.IsCancellationRequested)
                                {
                                    return;
                                }

                                string full = iconKey;
                                if (!File.Exists(full))
                                {
                                    Interlocked.Increment(ref iconsMissing);
                                    try { BeginInvoke((Action)UpdateIconStatus); } catch { }
                                    return;
                                }

            if (TryLoadCachedIcon(full, out var cachedBmp))
            {
                                BeginInvoke((Action)(() =>
                                {
                                    try
                                    {
                                        if (token.IsCancellationRequested)
                                        {
                                            cachedBmp.Dispose();
                                            return;
                                        }

                                        if (iconIndexByRelPath.ContainsKey(iconKey))
                                        {
                                            cachedBmp.Dispose();
                                            return;
                                        }

                                        smallIcons.Images.Add(iconKey, cachedBmp);
                                        iconIndexByRelPath[iconKey] = smallIcons.Images.IndexOfKey(iconKey);
                                        ApplyIconToListItems(iconKey);
                                        Interlocked.Increment(ref iconsLoaded);
                                        Interlocked.Increment(ref iconsCached);
                                        UpdateIconStatus();
                                    }
                                    catch
                                    {
                                        cachedBmp.Dispose();
                                    }
                                }));
                                return;
                            }

                            if (!BNTX.TryLoadFromFile(full, preferredName: string.Empty, out var bm, out var error))
                            {
                                Interlocked.Increment(ref iconsFailed);
                                if (!string.IsNullOrWhiteSpace(error))
                                {
                                    lastIconError = $"{Path.GetFileName(full)}: {error}";
                                }
                                try { BeginInvoke((Action)UpdateIconStatus); } catch { }
                                return;
                            }

                            using (bm)
                            {
                                var scaled = ScaleToIcon(bm, iconSizePx, iconSizePx);
                                TryWriteCachedIcon(full, scaled);

                                if (token.IsCancellationRequested)
                                {
                                    scaled.Dispose();
                                    return;
                                }

                                BeginInvoke((Action)(() =>
                                {
                                    try
                                    {
                                        if (token.IsCancellationRequested)
                                        {
                                            scaled.Dispose();
                                            return;
                                        }

                                        if (iconIndexByRelPath.ContainsKey(iconKey))
                                        {
                                            scaled.Dispose();
                                            return;
                                        }

                                        smallIcons.Images.Add(iconKey, scaled);
                                        iconIndexByRelPath[iconKey] = smallIcons.Images.IndexOfKey(iconKey);
                                        ApplyIconToListItems(iconKey);
                                        Interlocked.Increment(ref iconsLoaded);
                                        Interlocked.Increment(ref iconsDecoded);
                                        UpdateIconStatus();
                                    }
                                    catch
                                    {
                                        scaled.Dispose();
                                    }
                                }));
                            }
                        }
                        catch
                        {
                            // Ignore decode failures.
                            Interlocked.Increment(ref iconsFailed);
                            try { BeginInvoke((Action)UpdateIconStatus); } catch { }
                        }
                        finally
                        {
                            try { sem.Release(); } catch (ObjectDisposedException) { }
                        }
                    }, token));
                    }
                }
                finally
                {
                    try
                    {
                        await Task.WhenAll(tasks).ConfigureAwait(false);
                    }
                    catch
                    {
                        // Ignore cancellation/aggregate errors.
                    }

                    try
                    {
                        sem.Dispose();
                    }
                    catch
                    {
                        // Ignore.
                    }
                }
            }, token);
        }

        private void ApplyIconToListItems(string iconKey)
        {
            if (!showIconsCheckBox.Checked ||
                string.IsNullOrWhiteSpace(iconKey) ||
                listView.Items.Count == 0)
            {
                return;
            }

            if (!listItemIndicesByIconKey.TryGetValue(iconKey, out var indices) || indices == null || indices.Count == 0)
            {
                return;
            }

            if (!iconIndexByRelPath.TryGetValue(iconKey, out var idx) || idx < 0)
            {
                return;
            }

            for (int j = 0; j < indices.Count; j++)
            {
                int i = indices[j];
                if (i < 0 || i >= listView.Items.Count)
                {
                    continue;
                }

                listView.Items[i].ImageKey = iconKey;
            }
        }

        private void CancelIconLoads()
        {
            try
            {
                iconLoadCts?.Cancel();
            }
            catch
            {
                // Ignore.
            }

            try
            {
                iconLoadCts?.Dispose();
            }
            catch
            {
                // Ignore.
            }

            iconLoadCts = null;
        }

        private Bitmap ScaleToIcon(Bitmap src, int w, int h)
        {
            var scaled = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            using var g = Graphics.FromImage(scaled);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
            g.Clear(Color.Transparent);
            g.DrawImage(src, new Rectangle(0, 0, w, h));
            return scaled;
        }

        private bool TryLoadCachedIcon(string sourcePath, out Bitmap bitmap)
        {
            bitmap = null!;
            try
            {
                string cachePath = GetCachePathForSource(sourcePath);
                if (string.IsNullOrWhiteSpace(cachePath) || !File.Exists(cachePath))
                {
                    return false;
                }

                using var img = Image.FromFile(cachePath);
                bitmap = new Bitmap(img);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void TryWriteCachedIcon(string sourcePath, Bitmap scaled)
        {
            try
            {
                string cachePath = GetCachePathForSource(sourcePath);
                if (string.IsNullOrWhiteSpace(cachePath))
                {
                    return;
                }

                string? dir = Path.GetDirectoryName(cachePath);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                if (File.Exists(cachePath))
                {
                    return;
                }

                scaled.Save(cachePath, ImageFormat.Png);
            }
            catch
            {
                // Ignore cache write failures.
            }
        }

        private string GetCachePathForSource(string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                return string.Empty;
            }

            try
            {
                var fi = new FileInfo(sourcePath);
                if (!fi.Exists)
                {
                    return string.Empty;
                }

                string fingerprint = $"{fi.LastWriteTimeUtc.Ticks:x16}_{fi.Length:x16}";
                string key = sourcePath.Replace('\\', '/').ToLowerInvariant();
                string sha1 = Convert.ToHexString(SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
                return Path.Combine(iconCacheRoot, $"{sha1}_{fingerprint}_{iconSizePx}px.png");
            }
            catch
            {
                return string.Empty;
            }
        }

        private string ResolveRootRelative(string rel)
        {
            if (string.IsNullOrWhiteSpace(rel))
            {
                return string.Empty;
            }

            if (Path.IsPathRooted(rel))
            {
                return rel;
            }

            lock (normalizedPathCache)
            {
                if (normalizedPathCache.TryGetValue(rel, out var cached))
                {
                    return cached;
                }
            }

            string trimmed = rel.TrimStart('\\', '/');
            string normalized = trimmed.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            string full = ResolveCatalogPath(gameKind, gameRoot, normalized);

            lock (normalizedPathCache)
            {
                normalizedPathCache[rel] = full;
            }

            return full;
        }

        private static string ResolveCatalogPath(GameKind kind, string root, string normalizedRelPath)
        {
            // Both ZA and SV typically store paths like:
            //   pm####/pm####_vv_vv/pm####_vv_vv.trmdl
            // but the on-disk base differs:
            //   ZA: <root>/ik_pokemon/data/<...>
            //   SV: <root>/pokemon/data/<...>
            //
            if (string.IsNullOrWhiteSpace(root))
            {
                return normalizedRelPath;
            }

            string candidate0 = Path.Combine(root, normalizedRelPath);
            if (File.Exists(candidate0) || Directory.Exists(candidate0))
            {
                return candidate0;
            }

            string top = kind == GameKind.ZA ? "ik_pokemon" : "pokemon";
            string candidate1 = Path.Combine(root, top, normalizedRelPath);
            if (File.Exists(candidate1) || Directory.Exists(candidate1))
            {
                return candidate1;
            }

            string candidate2 = Path.Combine(root, top, "data", normalizedRelPath);
            if (File.Exists(candidate2) || Directory.Exists(candidate2))
            {
                return candidate2;
            }

            // Default to the most common layout so error messages are helpful.
            return candidate2;
        }

        private PokemonRow[] BuildRows(string root)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                return Array.Empty<PokemonRow>();
            }

            string catalogPath = FindCatalogPath(root);
            if (string.IsNullOrWhiteSpace(catalogPath) || !File.Exists(catalogPath))
            {
                throw new FileNotFoundException($"poke_resource_table.trpmcatalog not found under active root for {gameKind}.", catalogPath);
            }

            var catalog = FlatBufferConverter.DeserializeFrom<Catalog>(catalogPath);
            if (catalog?.Table == null || catalog.Table.Length == 0)
            {
                return Array.Empty<PokemonRow>();
            }

            var nameMap = LoadMonsnameMap(root, "English");

            var rows = new List<PokemonRow>(catalog.Table.Length);
            foreach (var entry in catalog.Table)
            {
                var info = entry?.SpeciesInfo;
                if (info == null)
                {
                    continue;
                }

                string modelRel = entry.ModelPath ?? string.Empty;
                if (string.IsNullOrWhiteSpace(modelRel))
                {
                    continue;
                }

                var row = new PokemonRow
                {
                    Species = info.Species,
                    Form = info.Form,
                    Gender = info.Gender,
                    Name = nameMap.TryGetValue(info.Species, out var n) ? n : string.Empty,
                    ModelRel = modelRel,
                    IconRel = entry.IconPath ?? string.Empty,
                };

                string normalizedModel = modelRel.TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
                string fullModel = ResolveCatalogPath(gameKind, root, normalizedModel);
                row.ModelExists = File.Exists(fullModel);

                row.IconFullPath = ResolveIconFullPath(root, row.ModelRel, row.IconRel);
                row.IconKey = row.IconFullPath;

                rows.Add(row);
            }

            return rows
                .OrderBy(r => r.Species)
                .ThenBy(r => r.Form)
                .ThenBy(r => r.Gender)
                .ThenBy(r => r.ModelRel, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private string FindCatalogPath(string root)
        {
            if (gameKind == GameKind.ZA)
            {
                return Path.Combine(root, "ik_pokemon", "catalog", "catalog", "poke_resource_table.trpmcatalog");
            }

            // SV common layouts (varies by dump tooling).
            var candidates = new[]
            {
                Path.Combine(root, "pokemon", "catalog", "catalog", "poke_resource_table.trpmcatalog"),
                Path.Combine(root, "catalog", "catalog", "poke_resource_table.trpmcatalog"),
            };
            for (int i = 0; i < candidates.Length; i++)
            {
                if (File.Exists(candidates[i]))
                {
                    return candidates[i];
                }
            }
            return candidates[0];
        }

        private string ResolveIconFullPath(string root, string modelRel, string iconRel)
        {
            return gameKind == GameKind.ZA
                ? ResolveZaIconFullPath(root, modelRel, iconRel)
                : ResolveSvIconFullPath(root, modelRel, iconRel);
        }

        private Dictionary<ushort, string> LoadMonsnameMap(string root, string language)
        {
            var result = new Dictionary<ushort, string>();
            if (string.IsNullOrWhiteSpace(root))
            {
                return result;
            }

            string tbl;
            string dat;
            if (gameKind == GameKind.ZA)
            {
                tbl = Path.Combine(root, "ik_message", "dat", language, "common", "monsname.tbl");
                dat = Path.Combine(root, "ik_message", "dat", language, "common", "monsname.dat");
            }
            else
            {
                tbl = Path.Combine(root, "message", "dat", language, "common", "monsname.tbl");
                dat = Path.Combine(root, "message", "dat", language, "common", "monsname.dat");
            }
            if (!File.Exists(tbl) || !File.Exists(dat))
            {
                return result;
            }

            var keys = ReadAhtbKeys(tbl);
            var strings = DecodeMsgDatStrings(dat);
            for (int i = 0; i < keys.Count && i < strings.Count; i++)
            {
                var k = keys[i];
                if (string.Equals(k, "msg_monsname_max", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!k.StartsWith("MONSNAME_", StringComparison.Ordinal))
                {
                    continue;
                }

                var suffix = k.AsSpan("MONSNAME_".Length);
                if (!ushort.TryParse(suffix, out var sid))
                {
                    continue;
                }

                result[sid] = strings[i];
            }

            return result;
        }

        private static List<string> ReadAhtbKeys(string tblPath)
        {
            var b = File.ReadAllBytes(tblPath);
            if (b.Length < 8 || b[0] != (byte)'A' || b[1] != (byte)'H' || b[2] != (byte)'T' || b[3] != (byte)'B')
            {
                throw new InvalidDataException($"Not an AHTB table: {tblPath}");
            }

            int count = (int)BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(4, 4));
            int off = 8;
            var keys = new List<string>(Math.Max(0, count));
            for (int i = 0; i < count; i++)
            {
                off += 8; // u64 hash
                if (off + 2 > b.Length)
                {
                    break;
                }
                int slen = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(off, 2));
                off += 2;
                if (off + slen > b.Length)
                {
                    break;
                }
                var raw = b.AsSpan(off, slen);
                off += slen;

                if (raw.Length == 0)
                {
                    keys.Add(string.Empty);
                    continue;
                }

                if (raw[^1] == 0)
                {
                    raw = raw[..^1];
                }

                keys.Add(System.Text.Encoding.UTF8.GetString(raw));
            }
            return keys;
        }

        private static List<string> DecodeMsgDatStrings(string datPath)
        {
            var b = File.ReadAllBytes(datPath);
            if (b.Length < 16)
            {
                return new List<string>();
            }

            ushort numLangs = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(0, 2));
            ushort numStrings = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(2, 2));
            uint reserved = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(8, 4));

            // Per-language offsets start at 0x0C.
            var langOffsets = new uint[numLangs];
            for (int i = 0; i < numLangs; i++)
            {
                langOffsets[i] = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(12 + i * 4, 4));
            }

            int lang0 = (int)langOffsets[0];
            int paramsOff = lang0 + 4;

            var outStrings = new List<string>(numStrings);
            for (int strId = 0; strId < numStrings; strId++)
            {
                int p = paramsOff + strId * 8;
                if (p + 8 > b.Length)
                {
                    outStrings.Add(string.Empty);
                    continue;
                }

                int ofs = (int)BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(p, 4));
                int len = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(p + 4, 2));

                int start = lang0 + ofs;
                int byteLen = len * 2;
                if (start < 0 || start + byteLen > b.Length)
                {
                    outStrings.Add(string.Empty);
                    continue;
                }

                Span<ushort> codes = len <= 2048 ? stackalloc ushort[len] : new ushort[len];
                for (int i = 0; i < len; i++)
                {
                    codes[i] = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(start + i * 2, 2));
                }

                if (reserved == 0)
                {
                    DecryptInPlace(codes, strId);
                }

                int end = 0;
                for (; end < codes.Length; end++)
                {
                    if (codes[end] == 0)
                    {
                        break;
                    }
                }

                var u16 = new byte[end * 2];
                for (int i = 0; i < end; i++)
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(u16.AsSpan(i * 2, 2), codes[i]);
                }
                outStrings.Add(System.Text.Encoding.Unicode.GetString(u16));
            }

            return outStrings;
        }

        private static void DecryptInPlace(Span<ushort> codes, int strId)
        {
            ushort mask = (ushort)((0x2983 * ((strId & 0xFFFF) + 3)) & 0xFFFF);
            for (int i = 0; i < codes.Length; i++)
            {
                codes[i] = (ushort)(codes[i] ^ mask);
                mask = (ushort)((((mask & 0xE000) >> 13) | ((mask & 0x1FFF) << 3)) & 0xFFFF);
            }
        }

        private static string ResolveZaIconFullPath(string root, string modelRel, string iconRel)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                return string.Empty;
            }

            // Common ZA layout: icons live under the model folder's `icon/` subdir:
            //   <root>/ik_pokemon/data/pm####/pm####_vv_vv/icon/pm####_vv_vv_00_0.bntx
            // where `pm####_vv_vv` is the variant folder name (same as the TRMDL basename).
            if (string.IsNullOrWhiteSpace(modelRel))
            {
                // No model path; fall back to the catalog-provided path (if any).
                return ResolveZaIconFullPathFromCatalog(root, iconRel);
            }

            string normalizedModelRel = modelRel.TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            string pmDir = normalizedModelRel.Split(Path.DirectorySeparatorChar).FirstOrDefault() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(pmDir))
            {
                return string.Empty;
            }

            string variant = Path.GetFileNameWithoutExtension(normalizedModelRel);
            if (string.IsNullOrWhiteSpace(variant))
            {
                return string.Empty;
            }

            // Try the most common filename first.
            string rel1 = Path.Combine(pmDir, variant, "icon", $"{variant}_00_0.bntx");
            string full1 = ResolveCatalogPath(GameKind.ZA, root, rel1);
            if (File.Exists(full1))
            {
                return full1;
            }

            // Some dumps use a slightly different suffix; try a couple cheap variants.
            string rel2 = Path.Combine(pmDir, variant, "icon", $"{variant}_00.bntx");
            string full2 = ResolveCatalogPath(GameKind.ZA, root, rel2);
            if (File.Exists(full2))
            {
                return full2;
            }

            string rel3 = Path.Combine(pmDir, variant, "icon", $"{variant}.bntx");
            string full3 = ResolveCatalogPath(GameKind.ZA, root, rel3);
            if (File.Exists(full3))
            {
                return full3;
            }

            // Last resort: if the icon dir exists, pick any file that looks like an icon for this variant.
            // (Covers suffixes other than _00_0, e.g. ..._00_1, ..._01_0, etc.)
            try
            {
                string iconDir = ResolveCatalogPath(GameKind.ZA, root, Path.Combine(pmDir, variant, "icon"));
                if (Directory.Exists(iconDir))
                {
                    var candidates = Directory.EnumerateFiles(iconDir, "*.bntx", SearchOption.TopDirectoryOnly)
                        .Where(p => Path.GetFileName(p).StartsWith(variant, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    if (candidates.Length > 0)
                    {
                        return candidates[0];
                    }
                }
            }
            catch
            {
                // Ignore directory enumeration failures.
            }

            // Default to the most likely location so error stats are meaningful.
            // If the derived path didn't exist, try the catalog-provided path (if any).
            var fromCatalog = ResolveZaIconFullPathFromCatalog(root, iconRel);
            return string.IsNullOrWhiteSpace(fromCatalog) ? full1 : fromCatalog;
        }

        private static string ResolveSvIconFullPath(string root, string modelRel, string iconRel)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                return string.Empty;
            }

            // Try derived path next to model first (common in dumps).
            if (!string.IsNullOrWhiteSpace(modelRel))
            {
                string normalizedModelRel = modelRel.TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
                string pmDir = normalizedModelRel.Split(Path.DirectorySeparatorChar).FirstOrDefault() ?? string.Empty;
                string variant = Path.GetFileNameWithoutExtension(normalizedModelRel) ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(pmDir) && !string.IsNullOrWhiteSpace(variant))
                {
                    string rel1 = Path.Combine(pmDir, variant, "icon", $"{variant}_00_0.bntx");
                    string full1 = ResolveCatalogPath(GameKind.SV, root, rel1);
                    if (File.Exists(full1))
                    {
                        return full1;
                    }

                    try
                    {
                        string iconDir = ResolveCatalogPath(GameKind.SV, root, Path.Combine(pmDir, variant, "icon"));
                        if (Directory.Exists(iconDir))
                        {
                            var candidates = Directory.EnumerateFiles(iconDir, "*.bntx", SearchOption.TopDirectoryOnly)
                                .Where(p => Path.GetFileName(p).StartsWith(variant, StringComparison.OrdinalIgnoreCase))
                                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                                .ToArray();
                            if (candidates.Length > 0)
                            {
                                return candidates[0];
                            }
                        }
                    }
                    catch
                    {
                    }
                }
            }

            // Fallback to catalog path if it exists and resolves.
            if (!string.IsNullOrWhiteSpace(iconRel))
            {
                string normalized = iconRel.TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
                string full = ResolveCatalogPath(GameKind.SV, root, normalized);
                if (File.Exists(full))
                {
                    return full;
                }
            }

            return string.Empty;
        }

        private static string ResolveZaIconFullPathFromCatalog(string root, string iconRel)
        {
            if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(iconRel))
            {
                return string.Empty;
            }

            string normalized = iconRel.TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            string full = ResolveCatalogPath(GameKind.ZA, root, normalized);
            return File.Exists(full) ? full : string.Empty;
        }
    }
}
