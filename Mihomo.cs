using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace InternetSeparator;

// Runs mihomo in TUN mode: it captures all traffic, matches it by process name, and sends it out
// through a "direct" outbound pinned to one adapter. Per-adapter app lists live in rule-provider
// files, so moving an app is a file rewrite + provider refresh, not a full restart.
public sealed class Mihomo : IDisposable
{
    const int Port = 9097; // ponytail: fixed port, pick a free one if it ever clashes
    public readonly string Exe, Dir;
    readonly string secret = Guid.NewGuid().ToString("N");
    readonly HttpClient http;
    Process? proc;
    public string LastLog { get; private set; } = "";

    public Mihomo(string baseDir)
    {
        Exe = Path.Combine(baseDir, "mihomo", "mihomo.exe");
        Dir = Path.Combine(baseDir, "data");
        Directory.CreateDirectory(Path.Combine(Dir, "rules"));
        http = new HttpClient { BaseAddress = new($"http://127.0.0.1:{Port}/") };
        http.DefaultRequestHeaders.Authorization = new("Bearer", secret);
    }

    string ConfigPath => Path.Combine(Dir, "config.yaml");

    public static string Config(IReadOnlyList<Adapter> adapters, string secret)
    {
        var sb = new StringBuilder($"""
            external-controller: 127.0.0.1:{Port}
            secret: "{secret}"
            log-level: warning
            find-process-mode: always
            tun:
              enable: true
              device: {Adapters.TunName}
              stack: mixed
              auto-route: true
              auto-detect-interface: true
              dns-hijack: ["any:53"]
            dns:
              enable: true
              enhanced-mode: redir-host
              nameserver: [1.1.1.1, 8.8.8.8]
            proxies:

            """);
        foreach (var a in adapters)
            sb.Append($"  - {{name: \"{a.Label}\", type: direct, interface-name: \"{a.Name}\"}}\n");
        sb.Append("proxy-groups:\n  - name: Default\n    type: select\n    proxies: [")
          .Append(string.Join(", ", adapters.Select(a => $"\"{a.Label}\""))).Append("]\nrule-providers:\n");
        for (var i = 0; i < adapters.Count; i++)
            sb.Append($"  r{i}: {{type: file, behavior: classical, format: text, path: ./rules/r{i}.txt}}\n");
        sb.Append("rules:\n");
        for (var i = 0; i < adapters.Count; i++)
            sb.Append($"  - \"RULE-SET,r{i},{adapters[i].Label}\"\n");
        sb.Append("  - MATCH,Default\n");
        return sb.ToString();
    }

    public void WriteRules(IReadOnlyList<Adapter> adapters, IReadOnlyDictionary<string, string> rules)
    {
        for (var i = 0; i < adapters.Count; i++)
        {
            // Placeholder keeps the file non-empty, which mihomo requires.
            var lines = rules.Where(r => r.Value == adapters[i].Label).Select(r => $"PROCESS-NAME,{r.Key}").Prepend("PROCESS-NAME,__none__.exe");
            File.WriteAllLines(Path.Combine(Dir, "rules", $"r{i}.txt"), lines);
        }
    }

    void WriteAll(IReadOnlyList<Adapter> adapters, IReadOnlyDictionary<string, string> rules)
    {
        WriteRules(adapters, rules);
        File.WriteAllText(ConfigPath, Config(adapters, secret));
    }

    // Also used when adapters change: a full restart drops every connection still bound to an
    // adapter that went away, and avoids hot-reloading the TUN device.
    public async Task Start(IReadOnlyList<Adapter> adapters, IReadOnlyDictionary<string, string> rules)
    {
        // Our previous instance, or a leftover from a crash, would hold the TUN adapter and the port.
        foreach (var p in Process.GetProcessesByName("mihomo"))
            try { if (string.Equals(p.MainModule?.FileName, Exe, StringComparison.OrdinalIgnoreCase)) { p.Kill(); p.WaitForExit(3000); } } catch { }

        WriteAll(adapters, rules);
        proc = Process.Start(new ProcessStartInfo(Exe, ["-d", Dir, "-f", ConfigPath])
        {
            CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
        })!;
        var log = Path.Combine(Dir, "mihomo.log");
        DataReceivedEventHandler onLine = (_, e) =>
        {
            if (e.Data == null) return;
            LastLog = e.Data;
            lock (Exe) File.AppendAllText(log, e.Data + Environment.NewLine);
        };
        proc.OutputDataReceived += onLine;
        proc.ErrorDataReceived += onLine;
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        for (var i = 0; i < 50; i++)
        {
            if (proc.HasExited) throw new Exception($"mihomo exited: {LastLog}");
            try { (await http.GetAsync("version")).EnsureSuccessStatusCode(); return; } catch (HttpRequestException) { }
            await Task.Delay(200);
        }
        throw new Exception($"mihomo API did not come up: {LastLog}");
    }

    // Real request out through one adapter; null = that adapter has no working internet.
    public async Task<int?> Delay(string label)
    {
        var r = await http.GetAsync($"proxies/{Uri.EscapeDataString(label)}/delay?timeout=3000&url=http%3A%2F%2Fwww.gstatic.com%2Fgenerate_204");
        return r.IsSuccessStatusCode ? (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("delay").GetInt32() : null;
    }

    public async Task RefreshRules(IReadOnlyList<Adapter> adapters, IReadOnlyDictionary<string, string> rules)
    {
        WriteRules(adapters, rules);
        for (var i = 0; i < adapters.Count; i++)
            (await http.PutAsync($"providers/rules/r{i}", null)).EnsureSuccessStatusCode();
    }

    public async Task SetDefault(string label) =>
        (await http.PutAsJsonAsync("proxies/Default", new { name = label })).EnsureSuccessStatusCode();

    public async Task<JsonElement> Connections() => await http.GetFromJsonAsync<JsonElement>("connections");

    public Task CloseConnection(string id) => http.DeleteAsync($"connections/{id}");

    public void Dispose()
    {
        try { proc?.Kill(); } catch { }
        http.Dispose();
    }
}
