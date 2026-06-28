using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using RimeKit.Windows.Core.Utilities;
using RimeKit.Windows.Gui;

namespace RimeKit.Windows.Tests.Tools.GuiProbeRunner;

internal sealed class ProbeStep
{
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = "";

    [JsonPropertyName("page")]
    public string? Page { get; init; }

    [JsonPropertyName("subpage")]
    public string? Subpage { get; init; }

    [JsonPropertyName("control")]
    public string? Control { get; init; }

    [JsonPropertyName("control_name")]
    public string? ControlName { get; init; }

    [JsonPropertyName("list_control_name")]
    public string? ListControlName { get; init; }

    [JsonPropertyName("combo_control_name")]
    public string? ComboControlName { get; init; }

    [JsonPropertyName("select_text")]
    public string? SelectText { get; init; }

    [JsonPropertyName("select_index")]
    public int? SelectIndex { get; init; }
}

internal sealed class WaitForSpec
{
    [JsonPropertyName("kind")] public string Kind { get; init; } = "";
    [JsonPropertyName("path")] public string? Path { get; init; }
    [JsonPropertyName("property")] public string? Property { get; init; }
    [JsonPropertyName("value")] public string? Value { get; init; }
    [JsonPropertyName("timeout_ms")] public int TimeoutMs { get; init; } = 15000;
    [JsonPropertyName("source_control_name")] public string? SourceControlName { get; init; }
}

internal sealed class AssertSpec
{
    [JsonPropertyName("kind")] public string Kind { get; init; } = "";
    [JsonPropertyName("path")] public string? Path { get; init; }
    [JsonPropertyName("property")] public string? Property { get; init; }
    [JsonPropertyName("value")] public string? Value { get; init; }
    [JsonPropertyName("source_control_name")] public string? SourceControlName { get; init; }
}

internal sealed class ProbeManifest
{
    [JsonPropertyName("action_id")] public string ActionId { get; init; } = "";
    [JsonPropertyName("page")] public string Page { get; init; } = "";
    [JsonPropertyName("subpage")] public string? Subpage { get; init; }
    [JsonPropertyName("control")] public string Control { get; init; } = "";
    [JsonPropertyName("control_name")] public string? ControlName { get; init; }
    [JsonPropertyName("trigger_kind")] public string TriggerKind { get; init; } = "cli_command";
    [JsonPropertyName("command")] public string? Command { get; init; }
    [JsonPropertyName("arguments")] public List<string>? Arguments { get; init; }
    [JsonPropertyName("target")] public string? Target { get; init; }
    [JsonPropertyName("steps")] public List<ProbeStep>? Steps { get; init; }
    [JsonPropertyName("wait_for")] public WaitForSpec? WaitFor { get; init; }
    [JsonPropertyName("assert")] public AssertSpec? Assert { get; init; }
}

internal sealed record ProbeResult(
    string ActionId, string Page, string Control, string TriggerKind,
    string Status, string ObservedStatusText, string? ObservedStatusTextBefore,
    string? ObservedStatusTextAfter, List<string> ObservedFiles, List<string> DiagnosticFiles,
    List<string> ObservedPathsChecked, List<string> ObservedJsonChecks,
    string? Exception, long ElapsedMs, string? WaitForKind, string? AssertKind,
    bool TriggerPerformed, bool EvidenceSatisfied, bool StepsSucceeded
);

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };

    [STAThread]
    internal static int Main(string[] args)
    {
        if (args.Length < 3) { Console.Error.WriteLine("Usage: GuiProbeRunner.exe <manifest_path> <repo_root> <report_path>"); return 1; }

        string manifestPath = Path.GetFullPath(args[0]);
        string repoRoot = Path.GetFullPath(args[1]);
        string reportPath = Path.GetFullPath(args[2]);

        if (!File.Exists(manifestPath)) { Console.Error.WriteLine($"Manifest not found: {manifestPath}"); return 1; }

        List<ProbeManifest>? manifest = JsonSerializer.Deserialize<List<ProbeManifest>>(File.ReadAllText(manifestPath));
        if (manifest is null || manifest.Count == 0) { Console.Error.WriteLine("Empty or invalid manifest."); return 1; }

        string stateRoot = Path.Combine(repoRoot, "workspace", "windows", "state");
        string cliPath = Path.Combine(repoRoot, "apps", "windows", "RimeKit.Windows.Cli", "bin", "Debug", "net10.0-windows", "RimeKit.Windows.Cli.exe");
        var results = new List<ProbeResult>();

        foreach (ProbeManifest action in manifest)
        {
            Stopwatch sw = Stopwatch.StartNew();
            ProbeResult result;
            try
            {
                result = action.TriggerKind switch
                {
                    "cli_command" => ExecuteCliCommand(action, cliPath, stateRoot, repoRoot, sw),
                    "check_file" => ExecuteCheckFile(action, repoRoot, sw),
                    "validate_json" => ExecuteValidateJson(action, repoRoot, sw),
                    "gui_click" => ExecuteGuiAction(action, repoRoot, stateRoot, sw),
                    _ => Mk(action, "blocked", $"Unknown trigger_kind: {action.TriggerKind}", sw)
                };
            }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException) { sw.Stop(); result = Mk(action, "failed", "", sw, ex); }
            results.Add(result);
        }

        string reportJson = JsonSerializer.Serialize(results, JsonOptions);
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        FileHelper.WriteTextWithVerification(reportPath, reportJson);

        bool allPassed = results.All(r =>
        {
            if (string.Equals(r.TriggerKind, "gui_click", StringComparison.OrdinalIgnoreCase))
                return string.Equals(r.Status, "completed", StringComparison.OrdinalIgnoreCase)
                    && r.TriggerPerformed && r.StepsSucceeded && r.EvidenceSatisfied;
            return string.Equals(r.Status, "completed", StringComparison.OrdinalIgnoreCase);
        });
        return allPassed ? 0 : 1;
    }

    private static ProbeResult Mk(ProbeManifest a, string s, string o, Stopwatch sw, Exception? ex = null) =>
        new(a.ActionId, a.Page, a.Control, a.TriggerKind, s, o, null, null, [], [], [], [], ex?.ToString(), sw.ElapsedMilliseconds, null, null, false, false, false);

    private static ProbeResult ExecuteCliCommand(ProbeManifest action, string cliPath, string stateRoot, string repoRoot, Stopwatch sw)
    {
        string cmd = action.Command ?? throw new InvalidOperationException("Missing command.");
        var si = new ProcessStartInfo { FileName = cliPath, WorkingDirectory = repoRoot, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, UseShellExecute = false, CreateNoWindow = true };
        var al = new List<string> { cmd }; if (action.Arguments is not null) al.AddRange(action.Arguments);
        foreach (string a in al) si.ArgumentList.Add(a);
        using Process? p = Process.Start(si); p?.WaitForExit(30000);
        string stdout = p?.StandardOutput.ReadToEnd() ?? "";
        sw.Stop();
        return new ProbeResult(action.ActionId, action.Page, action.Control, action.TriggerKind,
            p?.ExitCode == 0 ? "completed" : "failed", stdout.Split('\n')[0].Trim(), null, null, [],
            [Path.Combine(stateRoot, "last_diagnostic.json"), Path.Combine(stateRoot, "last_recheck_summary.json")],
            [], [],
            p?.ExitCode != 0 ? $"CLI exit code: {p?.ExitCode}" : null, sw.ElapsedMilliseconds, null, null, true, p?.ExitCode == 0, true);
    }

    private static ProbeResult ExecuteCheckFile(ProbeManifest action, string repoRoot, Stopwatch sw)
    {
        string fp = Resolve(repoRoot, action.Target ?? throw new InvalidOperationException("Missing target."));
        bool exists = File.Exists(fp); sw.Stop();
        return new ProbeResult(action.ActionId, action.Page, action.Control, action.TriggerKind,
            exists ? "completed" : "failed", $"File {fp}: {(exists ? "exists" : "missing")}",
            null, null, exists ? [fp] : [], [], exists ? [fp] : [], [],
            exists ? null : "File not found", sw.ElapsedMilliseconds,
            "file_exists", null, false, exists, false);
    }

    private static ProbeResult ExecuteValidateJson(ProbeManifest action, string repoRoot, Stopwatch sw)
    {
        string fp = Resolve(repoRoot, action.Target ?? throw new InvalidOperationException("Missing target."));
        if (!File.Exists(fp)) { sw.Stop(); return MkFail(action, $"File not found: {fp}", sw); }
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(fp));
        string status = "completed"; string observed = $"Valid JSON with {doc.RootElement.EnumerateObject().Count()} properties";
        string? exc = null; bool ev = true;
        if (action.Assert?.Property is not null && action.Assert.Value is not null)
        {
            if (doc.RootElement.TryGetProperty(action.Assert.Property, out JsonElement ep))
            {
                string act = ep.GetString() ?? ep.GetRawText();
                if (!string.Equals(act, action.Assert.Value, StringComparison.Ordinal)) { status = "failed"; exc = $"Expected '{action.Assert.Value}', got '{act}'"; ev = false; }
            }
            else { status = "failed"; exc = $"Property '{action.Assert.Property}' not found"; ev = false; }
        }
        sw.Stop();
        List<string> opc = [fp];
        List<string> ojc = [];
        if (action.Assert?.Property is not null)
        {
            ojc.Add($"{fp}#{action.Assert.Property}");
        }
        return new ProbeResult(action.ActionId, action.Page, action.Control, action.TriggerKind, status, observed, null, null, [fp], [], opc, ojc, exc, sw.ElapsedMilliseconds, null, "json_property_equals", false, ev, false);
    }

    private static ProbeResult MkFail(ProbeManifest a, string msg, Stopwatch sw) =>
        new(a.ActionId, a.Page, a.Control, a.TriggerKind, "failed", msg, null, null, [], [], [], [], msg, sw.ElapsedMilliseconds, null, null, false, false, false);

    // ──────────────────── gui_click with steps ────────────────────

    private sealed class GcCtx
    {
        public string Status = "pending", StatusText = "", Detail = "", StatusTextBefore = "", StatusTextAfter = "";
        public string? WfKind, AsKind, Exc; public bool Tp, Ev, Ss;
        public List<string> Of = [], Df = [], Opc = [], Ojc = [];
    }

    private static ProbeResult ExecuteGuiAction(ProbeManifest action, string repoRoot, string stateRoot, Stopwatch sw)
    {
        GcCtx ctx = new(); Exception? top = null;
        var t = new Thread(() =>
        {
            try
            {
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
                using MainForm form = new(repoRoot);
                form.UnsupportedActionObserver = _ => { };
                form.WorkflowErrorObserver = _ => { };
                form.Shown += (_, _) =>
                {
                    try
                    {
                        Application.DoEvents();
                        Thread.Sleep(1200);
                        ctx.StatusTextBefore = ReadLabel(form, "_statusLabel");
                        bool stepsOk = true;
                        int stepIndex = 0;
                        foreach (ProbeStep step in (action.Steps ?? []))
                        {
                            if (!DoStep(form, step, repoRoot)) { stepsOk = false; ctx.Exc = $"Step {stepIndex} '{step.Kind}' failed."; break; }
                            stepIndex++;
                            Application.DoEvents(); Thread.Sleep(250);
                        }

                        if (!stepsOk) { ctx.Status = "failed"; /* ctx.Exc already set by the step loop */ return; }
                        ctx.Ss = true; ctx.Tp = true;
                        ctx.Detail = $"Executed {action.Steps?.Count ?? 0} steps for {action.ActionId}";

                        if (action.WaitFor is not null)
                        {
                            ctx.WfKind = action.WaitFor.Kind;
                            if (!string.IsNullOrWhiteSpace(action.WaitFor.Path))
                            {
                                string resolved = Resolve(repoRoot, action.WaitFor.Path);
                                if (!ctx.Opc.Contains(resolved)) ctx.Opc.Add(resolved);
                            }
                            string? wf = WaitFor(action.WaitFor, repoRoot, form);
                            if (wf is not null) { ctx.Status = "failed"; ctx.Exc = wf; return; }
                        }

                        WaitForProgressBarIdle(form);
                        ctx.StatusTextAfter = ReadLabel(form, "_statusLabel");

                        if (action.Assert is not null)
                        {
                            ctx.AsKind = action.Assert.Kind;
                            if (action.Assert.Kind == "json_property_equals" && !string.IsNullOrWhiteSpace(action.Assert.Path) && !string.IsNullOrWhiteSpace(action.Assert.Property))
                            {
                                ctx.Ojc.Add($"{Resolve(repoRoot, action.Assert.Path)}#{action.Assert.Property}");
                            }
                            string? af = Assert(action.Assert, repoRoot, form);
                            if (af is not null) { ctx.Status = "failed"; ctx.Exc = af; return; }
                        }

                        ctx.Ev = true; ctx.Status = "completed";
                    }
                    catch (Exception ex) when (ex is InvalidOperationException) { top = ex; ctx.Status = "failed"; ctx.Exc = ex.ToString(); }
                    finally { form.Close(); }
                };
                Application.Run(form);
            }
            catch (Exception ex) when (ex is InvalidOperationException) { top = ex; }
        });
        t.SetApartmentState(ApartmentState.STA); t.Start(); t.Join();
        sw.Stop();
        if (top is not null && ctx.Status != "failed") { ctx.Status = "failed"; ctx.Exc = top.ToString(); }
        var df = new List<string>(ctx.Df);
        string lr = Path.Combine(stateRoot, "last_recheck_summary.json");
        string ld = Path.Combine(stateRoot, "last_diagnostic.json");
        if (File.Exists(lr)) df.Add(lr); if (File.Exists(ld)) df.Add(ld);

        return new ProbeResult(action.ActionId, action.Page, action.Control, action.TriggerKind,
            ctx.Status, !string.IsNullOrWhiteSpace(ctx.StatusText) ? ctx.StatusText : ctx.Detail,
            ctx.StatusTextBefore, ctx.StatusTextAfter, ctx.Of, df, ctx.Opc, ctx.Ojc,
            ctx.Exc, sw.ElapsedMilliseconds,
            ctx.WfKind, ctx.AsKind, ctx.Tp, ctx.Ev, ctx.Ss);
    }

    private static bool DoStep(MainForm form, ProbeStep step, string repoRoot)
    {
        switch (step.Kind)
        {
            case "select_top_tab":
            {
                TabControl? found = null;
                foreach (TabControl tabCtrl in FindControls<TabControl>(form))
                {
                    TabPage? tp = tabCtrl.TabPages.Cast<TabPage>().FirstOrDefault(p => p.Text == step.Page);
                    if (tp is not null) { found = tabCtrl; found.SelectedTab = tp; break; }
                }
                if (found is null) return false;
                Application.DoEvents();
                break;
            }

            case "select_sub_tab":
            {
                TabControl? found = null;
                foreach (TabControl inner in FindControls<TabControl>(form))
                {
                    TabPage? tp = inner.TabPages.Cast<TabPage>().FirstOrDefault(p => p.Text == step.Page);
                    if (tp is not null) { found = inner; found.SelectedTab = tp; break; }
                }
                if (found is null) return false;
                Application.DoEvents();
                break;
            }

            case "click_button":
                Button? btn = null;
                if (step.ControlName is not null) btn = FindControls<Button>(form).FirstOrDefault(b => string.Equals(b.Name, step.ControlName, StringComparison.Ordinal));
                if (btn is null && step.Control is not null) btn = FindControls<Button>(form).Where(b => string.Equals(b.Text, step.Control, StringComparison.Ordinal) && b.Visible && b.Enabled).OrderBy(b => b.Top).ThenBy(b => b.Left).FirstOrDefault();
                if (btn is null) return false;
                btn.PerformClick(); Application.DoEvents();
                break;

            case "select_list_item":
                if (step.ListControlName is null) return false;
                ListBox? lb = FindControls<ListBox>(form).FirstOrDefault(c => string.Equals(c.Name, step.ListControlName, StringComparison.Ordinal));
                if (lb is null) return false;
                if (step.SelectIndex is not null) { if (step.SelectIndex.Value < lb.Items.Count) lb.SelectedIndex = step.SelectIndex.Value; }
                else if (step.SelectText is not null)
                {
                    int idx = -1;
                    for (int i = 0; i < lb.Items.Count; i++) { if (string.Equals(lb.Items[i]?.ToString(), step.SelectText, StringComparison.Ordinal)) { idx = i; break; } }
                    if (idx >= 0) lb.SelectedIndex = idx; else return false;
                }
                Application.DoEvents();
                break;

            case "select_combo_item":
                if (step.ComboControlName is null) return false;
                ComboBox? cb = FindControls<ComboBox>(form).FirstOrDefault(c => string.Equals(c.Name, step.ComboControlName, StringComparison.Ordinal));
                if (cb is null) return false;
                if (step.SelectIndex is not null) { if (step.SelectIndex.Value < cb.Items.Count) cb.SelectedIndex = step.SelectIndex.Value; }
                else if (step.SelectText is not null)
                {
                    int idx = -1;
                    for (int i = 0; i < cb.Items.Count; i++) { if (string.Equals(cb.Items[i]?.ToString(), step.SelectText, StringComparison.Ordinal)) { idx = i; break; } }
                    if (idx >= 0) cb.SelectedIndex = idx;
                }
                Application.DoEvents();
                break;
        }
        return true;
    }

    private static string? WaitFor(WaitForSpec? spec, string repoRoot, MainForm form)
    {
        if (spec is null) return null;
        int timeout = spec.TimeoutMs > 0 ? spec.TimeoutMs : 15000;
        Stopwatch t = Stopwatch.StartNew();
        while (t.ElapsedMilliseconds < timeout)
        {
            bool ok = spec.Kind switch
            {
                "file_exists" => !string.IsNullOrWhiteSpace(spec.Path) && File.Exists(Resolve(repoRoot, spec.Path)),
                "status_text_contains" => CheckText(spec, form),
                "json_property_equals" => CheckJson(spec, repoRoot),
                "diagnostic_exists" => !string.IsNullOrWhiteSpace(spec.Path) && File.Exists(Resolve(repoRoot, spec.Path)),
                _ => false
            };
            if (ok) { Application.DoEvents(); return null; }
            Application.DoEvents();
            Thread.Sleep(50);
        }
        return $"Timeout waiting for {spec.Kind}";
    }

    private static string? Assert(AssertSpec? spec, string repoRoot, MainForm form)
    {
        if (spec is null) return null;
        return spec.Kind switch
        {
            "json_property_equals" => AssertJson(spec, repoRoot),
            "status_text_contains" => AssertText(spec, form),
            "file_contains" => AssertFile(spec, repoRoot),
            _ => $"Unknown assert: {spec.Kind}"
        };
    }

    private static bool CheckText(WaitForSpec s, MainForm f)
    {
        if (string.IsNullOrWhiteSpace(s.Value)) return false;
        Control? c = FindSource(f, s.SourceControlName) ?? FindControls<Label>(f).FirstOrDefault(l => l.Name == "_statusLabel");
        return c is not null && ReadTree(c).Contains(s.Value, StringComparison.Ordinal);
    }

    private static bool CheckJson(WaitForSpec s, string repo)
    {
        if (string.IsNullOrWhiteSpace(s.Path) || string.IsNullOrWhiteSpace(s.Property)) return false;
        string fp = Resolve(repo, s.Path); if (!File.Exists(fp)) return false;
        try { using JsonDocument d = JsonDocument.Parse(File.ReadAllText(fp)); return d.RootElement.TryGetProperty(s.Property, out JsonElement e) && string.Equals(e.GetString() ?? e.GetRawText(), s.Value, StringComparison.Ordinal); } catch (JsonException) { return false; }
    }

    private static string? AssertJson(AssertSpec s, string repo)
    {
        if (string.IsNullOrWhiteSpace(s.Path) || string.IsNullOrWhiteSpace(s.Property)) return "path/property missing";
        string fp = Resolve(repo, s.Path); if (!File.Exists(fp)) return $"file not found: {fp}";
        using JsonDocument d = JsonDocument.Parse(File.ReadAllText(fp));
        if (!d.RootElement.TryGetProperty(s.Property, out JsonElement e)) return $"Property '{s.Property}' not found";
        string a = e.GetString() ?? e.GetRawText();
        return string.Equals(a, s.Value, StringComparison.Ordinal) ? null : $"Expected '{s.Value}', got '{a}'";
    }

    private static string? AssertText(AssertSpec s, MainForm f)
    {
        if (string.IsNullOrWhiteSpace(s.Value)) return "value missing";
        Control? c = FindSource(f, s.SourceControlName) ?? FindControls<Label>(f).FirstOrDefault(l => l.Name == "_statusLabel");
        if (c is null) return "source control not found";
        string t = ReadTree(c);
        return t.Contains(s.Value, StringComparison.Ordinal) ? null : $"Text does not contain '{s.Value}'. Sample: '{t[..Math.Min(t.Length, 120)]}'";
    }

    private static string? AssertFile(AssertSpec s, string repo)
    {
        if (string.IsNullOrWhiteSpace(s.Path) || string.IsNullOrWhiteSpace(s.Value)) return "path/value missing";
        string fp = Resolve(repo, s.Path); if (!File.Exists(fp)) return $"file not found: {fp}";
        return File.ReadAllText(fp).Contains(s.Value, StringComparison.Ordinal) ? null : $"'{fp}' does not contain '{s.Value}'";
    }

    private static Control? FindSource(MainForm f, string? name) =>
        name is null ? null : FindControls<Control>(f).FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.Ordinal));

    private static string ReadTree(Control root)
    {
        var p = new List<string>();
        void V(Control c) { if (!string.IsNullOrWhiteSpace(c.Text)) p.Add(c.Text); foreach (Control ch in c.Controls) V(ch); }
        V(root); return string.Join("\n", p);
    }

    private static string ReadLabel(MainForm f, string name) =>
        FindControls<Label>(f).FirstOrDefault(l => l.Name == name)?.Text ?? "";

    private static string Resolve(string root, string rel) =>
        Path.IsPathRooted(rel) ? rel : Path.GetFullPath(Path.Combine(root, rel));

    private static IEnumerable<T> FindControls<T>(Control root) where T : Control
    {
        foreach (Control c in root.Controls) { if (c is T m) yield return m; foreach (T d in FindControls<T>(c)) yield return d; }
    }

    private static void WaitForProgressBarIdle(MainForm form, int timeoutMs = 15000)
    {
        Label? label = FindControls<Label>(form).FirstOrDefault(p => p.Name == "_statusLabel");
        if (label is null) return;
        Stopwatch sw = Stopwatch.StartNew(); int stable = 0; string? lastText = label.Text;
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            Application.DoEvents();
            string? current = label.Text;
            if (string.IsNullOrEmpty(current) || (current == lastText && stable >= 4)) return;
            if (current == lastText) { stable++; } else { stable = 0; lastText = current; }
            Thread.Sleep(100);
        }
    }
}
