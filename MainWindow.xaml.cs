using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace InternetSeparator;

public class AppRow
{
    public string Exe { get; init; } = "";
    public string Name { get; init; } = "";
    public string Speed { get; init; } = "";
    public bool IsChecked { get; init; }
}

public record Column(string Header, List<AppRow> Apps, int Index)
{
    public bool HasLeft => Index > 0;
}

public class Settings
{
    public Dictionary<string, string> Rules { get; set; } = []; // exe name -> adapter label
    public string? Default { get; set; }
    public HashSet<string> Apps { get; set; } = []; // added via "Add app…", listed even when not running
}

public partial class MainWindow : Window
{
    readonly Mihomo m = new(AppContext.BaseDirectory);
    readonly string settingsPath;
    readonly DispatcherTimer tick = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly DispatcherTimer netDebounce = new() { Interval = TimeSpan.FromSeconds(3) };
    Settings s = new();
    List<Adapter> adapters = [];
    Dictionary<string, (long Up, long Down)> last = [];
    Dictionary<string, List<string>> connsByExe = [];
    readonly Dictionary<string, string> health = []; // adapter label -> "45 ms" / "no internet"
    readonly Dictionary<string, (long Rx, long Tx)> adapterLast = [];
    Dictionary<string, string> columnOf = []; // exe -> adapter label it's shown under
    readonly HashSet<string> ticked = []; // exe names; kept here because the rows are rebuilt every second
    readonly System.Windows.Forms.NotifyIcon tray = new()
    {
        Text = "Internet Separator", Visible = true,
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!),
    };
    bool started, busy, loadingBox, exiting, hintShown;
    int ticks;

    public MainWindow()
    {
        InitializeComponent();
        tray.DoubleClick += (_, _) => ShowFromTray();
        tray.ContextMenuStrip = new();
        tray.ContextMenuStrip.Items.Add("Open", null, (_, _) => ShowFromTray());
        tray.ContextMenuStrip.Items.Add("About", null, (_, _) => { ShowFromTray(); About_Click(this, new RoutedEventArgs()); });
        tray.ContextMenuStrip.Items.Add("Exit", null, (_, _) => { exiting = true; Close(); });
        Application.Current.SessionEnding += (_, _) => exiting = true; // don't block sign-out / shutdown
        // Routing should never die over a UI glitch: show the error instead of crashing.
        Application.Current.DispatcherUnhandledException += (_, e) => { Status.Text = $"Error: {e.Exception.Message}"; e.Handled = true; };
        settingsPath = Path.Combine(m.Dir, "settings.json");
        tick.Tick += async (_, _) => await Tick();
        netDebounce.Tick += async (_, _) => { netDebounce.Stop(); await CheckAdapters(); };
        // Plugging in the phone / joining Wi-Fi fires this; debounce because it fires in bursts.
        NetworkChange.NetworkAddressChanged += (_, _) => Dispatcher.Invoke(() => { netDebounce.Stop(); netDebounce.Start(); });
    }

    // Called by App right after construction, shown or not (it starts hidden in the tray with --minimized).
    public async void Start()
    {
        try { s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(settingsPath)) ?? new(); } catch (FileNotFoundException) { }
        AutostartBox.IsChecked = Autostart.IsEnabled;
        if (!File.Exists(m.Exe)) { Status.Text = $"mihomo.exe not found at {m.Exe}"; return; }
        await CheckAdapters();
    }

    public void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    // X hides to the tray; routing keeps running. Only tray → Exit (or sign-out) really quits.
    void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (exiting) return;
        e.Cancel = true;
        Hide();
        if (hintShown) return;
        tray.ShowBalloonTip(3000, "Internet Separator", "Still running here. Right-click → Exit to quit.", System.Windows.Forms.ToolTipIcon.Info);
        hintShown = true;
    }

    void About_Click(object sender, RoutedEventArgs e) => new AboutWindow { Owner = this }.ShowDialog();

    void Window_Closed(object? sender, EventArgs e)
    {
        tray.Dispose();
        m.Dispose();
    }

    async Task CheckAdapters()
    {
        var now = Adapters.Detect();
        if (started && now.Select(a => a.Label + a.Name).SequenceEqual(adapters.Select(a => a.Label + a.Name))) return;
        if (now.Count == 0) { Status.Text = "No internet connection found. Connect Wi-Fi, Ethernet or USB tethering."; return; }
        adapters = now;
        try
        {
            await m.Start(adapters, s.Rules);
            if (!started) { started = true; tick.Start(); }
            health.Clear();
            ticks = 0;

            var labels = adapters.Select(a => a.Label).ToList();
            var def = labels.Contains(s.Default ?? "") ? s.Default! : labels.Contains("Wi-Fi") ? "Wi-Fi" : labels[0];
            await m.SetDefault(def);
            loadingBox = true;
            DefaultBox.ItemsSource = labels;
            DefaultBox.SelectedItem = def;
            loadingBox = false;
            Status.Text = "";
        }
        catch (Exception ex) { Status.Text = ex.Message; }
    }

    async Task Tick()
    {
        if (busy) return;
        busy = true;
        try
        {
            var json = await m.Connections();
            var rows = new Dictionary<string, (string Label, long Down, long Up)>();
            var nowLast = new Dictionary<string, (long Up, long Down)>();
            var byExe = new Dictionary<string, List<string>>();
            if (json.TryGetProperty("connections", out var conns) && conns.ValueKind == JsonValueKind.Array)
                foreach (var c in conns.EnumerateArray())
                {
                    var id = c.GetProperty("id").GetString()!;
                    var exe = c.GetProperty("metadata").GetProperty("process").GetString();
                    if (string.IsNullOrEmpty(exe)) exe = "(system)";
                    var chains = c.GetProperty("chains");
                    var chain = chains.GetArrayLength() > 0 ? chains[0].GetString()! : "";
                    long up = c.GetProperty("upload").GetInt64(), down = c.GetProperty("download").GetInt64();
                    last.TryGetValue(id, out var prev);
                    nowLast[id] = (up, down);
                    rows.TryGetValue(exe, out var r);
                    rows[exe] = (chain, r.Down + down - prev.Down, r.Up + up - prev.Up);
                    (byExe.TryGetValue(exe, out var ids) ? ids : byExe[exe] = []).Add(id);
                }
            // Connections opened before mihomo started keep going straight out of an adapter, invisible to mihomo.
            var bypass = Bypassing();
            foreach (var (exe, label) in bypass) rows.TryAdd(exe, (label, 0, 0));
            // Idle apps show too, so they can be moved before they connect: pinned, added, and anything with a window.
            foreach (var exe in s.Rules.Keys.Concat(s.Apps).Concat(WindowedApps())) rows.TryAdd(exe, ("", 0, 0));
            last = nowLast;
            if (ticks++ % 10 == 0) _ = Probe();
            connsByExe = byExe;

            var labels = adapters.Select(a => a.Label).ToHashSet();
            var def = DefaultBox.SelectedItem as string ?? adapters[0].Label;
            string ColumnOf(string exe, string chain) =>
                s.Rules.TryGetValue(exe, out var l) && labels.Contains(l) ? l : labels.Contains(chain) ? chain : def;

            columnOf = rows.ToDictionary(r => r.Key, r => ColumnOf(r.Key, r.Value.Label));
            Columns.ItemsSource = adapters.Select((a, i) =>
            {
                var mine = rows.Where(r => ColumnOf(r.Key, r.Value.Label) == a.Label).OrderByDescending(r => r.Value.Down + r.Value.Up).ToList();
                var apps = mine.Select(r =>
                {
                    var pin = s.Rules.TryGetValue(r.Key, out var l) ? (labels.Contains(l) ? "📌 " : $"(waiting for {l}) ") : "";
                    var warn = bypass.ContainsKey(r.Key) ? "  ⚠ restart to route" : "";
                    return new AppRow { Exe = r.Key, Name = pin + r.Key + warn, Speed = $"↓{Fmt(r.Value.Down)} ↑{Fmt(r.Value.Up)}", IsChecked = ticked.Contains(r.Key) };
                }).ToList();
                // Totals come from the adapter's own counters, so they include traffic that bypasses mihomo.
                var st = a.Nic.GetIPStatistics();
                var (rx, tx) = adapterLast.TryGetValue(a.Label, out var p) ? (st.BytesReceived - p.Rx, st.BytesSent - p.Tx) : (0, 0);
                adapterLast[a.Label] = (st.BytesReceived, st.BytesSent);
                var header = $"{a.Label}{(a.Label == def ? "  (default)" : "")}   {health.GetValueOrDefault(a.Label, "checking…")}\n{a.Name} · {a.Ip}\n↓ {Fmt(rx)}   ↑ {Fmt(tx)}";
                return new Column(header, apps, i);
            }).ToList();
        }
        catch (Exception ex) { Status.Text = $"{ex.Message} {m.LastLog}"; }
        finally { busy = false; }
    }

    // exe -> adapter label, for processes with connections bound directly to an adapter's address.
    Dictionary<string, string> Bypassing()
    {
        var owner = new Dictionary<IPAddress, string>();
        foreach (var a in adapters)
            foreach (var u in a.Nic.GetIPProperties().UnicastAddresses) owner[u.Address] = a.Label;
        var result = new Dictionary<string, string>();
        foreach (var (local, pid) in Tcp.Established4And6())
        {
            if (pid <= 4 || !owner.TryGetValue(local, out var label)) continue; // 0/4 = Idle/System
            try
            {
                using var p = Process.GetProcessById(pid);
                var exe = p.ProcessName + ".exe";
                if (exe != "mihomo.exe") result[exe] = label; // mihomo binds to adapters by design
            }
            catch (ArgumentException) { } // exited meanwhile
        }
        return result;
    }

    static readonly string self =Environment.ProcessPath is { } p ? Path.GetFileName(p) : "";

    static IEnumerable<string> WindowedApps()
    {
        foreach (var p in Process.GetProcesses())
            using (p)
                if (p.MainWindowHandle != IntPtr.Zero && p.ProcessName + ".exe" != self)
                    yield return p.ProcessName + ".exe";
    }

    async Task Probe()
    {
        foreach (var a in adapters.ToList())
            try { health[a.Label] = await m.Delay(a.Label) is { } ms ? $"✓ {ms} ms" : "⚠ no internet"; }
            catch (HttpRequestException) { }
    }

    void Autostart_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Autostart.Set(AutostartBox.IsChecked == true);
            Status.Text = AutostartBox.IsChecked == true ? "Will start minimized when you sign in, with your saved pins." : "Won't start with Windows.";
        }
        catch (Exception ex) { Status.Text = ex.Message; }
        AutostartBox.IsChecked = Autostart.IsEnabled; // show the real state, even if it failed
    }

    async void AddApp_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Programs (*.exe)|*.exe", Title = "Pick an app to list" };
        if (dlg.ShowDialog() != true) return;
        var exe = Path.GetFileName(dlg.FileName);
        s.Apps.Add(exe);
        ticked.Add(exe);
        Save();
        Status.Text = $"{exe} added and ticked. Click ⇄ to move it.";
        await Tick();
    }

    static string Fmt(long bps) => bps switch
    {
        >= 1 << 20 => $"{bps / 1048576.0:0.0} MB/s",
        >= 1 << 10 => $"{bps / 1024.0:0} KB/s",
        _ => $"{bps} B/s",
    };

    void Check_Click(object sender, RoutedEventArgs e)
    {
        // Rows are rebuilt every second; a click landing mid-rebuild hits a detached row with no AppRow.
        if (sender is not CheckBox { DataContext: AppRow row } box) return;
        if (box.IsChecked == true) ticked.Add(row.Exe); else ticked.Remove(row.Exe);
    }

    // ⇄ between two columns: each ticked app in either of them moves to the other one.
    async void Swap_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: Column col }) return; // same mid-rebuild case as Check_Click
        var i = col.Index;
        string left = adapters[i - 1].Label, right = adapters[i].Label;
        var moving = ticked.Where(exe => columnOf.GetValueOrDefault(exe) is { } c && (c == left || c == right)).ToList();
        if (moving.Count == 0) { Status.Text = $"Tick apps under {left} or {right} first."; return; }
        foreach (var exe in moving) s.Rules[exe] = columnOf[exe] == left ? right : left;
        await ApplyRules(moving, moving.Count == 1 ? $"{moving[0]} → {s.Rules[moving[0]]}" : $"Moved {moving.Count} apps");
    }

    async void Unpin_Click(object sender, RoutedEventArgs e)
    {
        if (ticked.Count == 0) { Status.Text = "Tick apps first."; return; }
        var apps = ticked.ToList();
        foreach (var exe in apps) s.Rules.Remove(exe);
        await ApplyRules(apps, $"{apps.Count} app(s) follow the default connection");
    }

    async Task ApplyRules(List<string> exes, string msg)
    {
        Save();
        try
        {
            await m.RefreshRules(adapters, s.Rules);
            // Open connections stay on the old adapter; closing them makes the app reconnect on the new one.
            foreach (var id in exes.SelectMany(exe => connsByExe.GetValueOrDefault(exe, []))) await m.CloseConnection(id);
            Status.Text = msg;
        }
        catch (Exception ex) { Status.Text = ex.Message; }
        foreach (var exe in exes) ticked.Remove(exe);
        await Tick();
    }

    async void DefaultBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loadingBox || DefaultBox.SelectedItem is not string label) return;
        s.Default = label;
        Save();
        try
        {
            await m.SetDefault(label);
            foreach (var (exe, ids) in connsByExe)
                if (!s.Rules.ContainsKey(exe)) foreach (var id in ids) await m.CloseConnection(id);
        }
        catch (Exception ex) { Status.Text = ex.Message; }
    }

    void Save() => File.WriteAllText(settingsPath, JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true }));
}
