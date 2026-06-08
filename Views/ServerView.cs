// -----------------------------------------------------------------------
// LazyCaddy - Server: a sectioned inline form editing one HTTP server's
// settings (listen / protocols / automatic_https / timeouts) plus the
// global apps/http ports + grace period and the top-level admin listen.
//
// Edits are batched: each field is compared to its loaded value and only
// CHANGED fields are written, in order, through EditCoordinator (one diff
// confirm for the whole batch). There is no per-control change event; the
// poll-driven Update() detects unsaved edits via HasUnsavedChanges() and
// FREEZES repopulation while dirty (so a poll mid-edit can't clobber input).
// The detection therefore lags an edit by at most one poll, which is fine —
// the worst case is one extra repopulate-attempt that the freeze swallows.
// -----------------------------------------------------------------------

using System.Text.Json;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;
using LazyCaddy.Configuration;
using LazyCaddy.Dashboard;
using LazyCaddy.Services;
using LazyCaddy.UI;
using LazyCaddy.UI.Modals;

namespace LazyCaddy.Views;

public sealed class ServerView
{
    private readonly ConsoleWindowSystem _ws;
    private readonly EditCoordinator _editor;
    private readonly Action _onRefresh;

    // ── Form controls ──
    private PromptControl? _listen, _skip, _readTo, _readHdrTo, _writeTo, _idleTo,
        _adminListen, _httpPort, _httpsPort, _grace;
    private CheckboxControl? _h1, _h2, _h3, _disable, _disableRedir, _disableCerts;
    private DropdownControl? _server;
    private TableControl? _logs;
    private MarkupControl? _status;
    private ToolbarControl? _toolbar;

    // ── State ──
    private string _serverPath = "apps/http/servers/srv0";
    private bool _dirty;
    private bool _dirtyNoticeShown;     // ensure the "unsaved edits" status is set once per dirty window
    private bool _hasServer;
    private bool _adminWasNull;          // admin null/absent at load → write whole admin object
    private string _lastSnapshotJson = ""; // last snapshot's RawConfigJson (for Revert re-parse)

    // Loaded values (what the controls were populated with) for change detection.
    private string _loadedListen = "", _loadedSkip = "", _loadedReadTo = "", _loadedReadHdrTo = "",
        _loadedWriteTo = "", _loadedIdleTo = "", _loadedAdminListen = "", _loadedHttpPort = "",
        _loadedHttpsPort = "", _loadedGrace = "";
    private bool _loadedH1, _loadedH2, _loadedH3, _loadedDisable, _loadedDisableRedir, _loadedDisableCerts;

    public ServerView(ConsoleWindowSystem ws, EditCoordinator editor, Action onRefresh)
    {
        _ws = ws;
        _editor = editor;
        _onRefresh = onRefresh;
    }

    public void Build(ScrollablePanelControl panel)
    {
        var accent = UIConstants.Accent.ToMarkup();
        var muted = UIConstants.MutedText.ToMarkup();

        panel.AddControl(Controls.Markup()
            .AddLine($"[bold {accent}]Server[/]")
            .AddLine($"[{muted}]HTTP-server settings + global/admin options. Edit a field, then Apply (s). Enter/e on a log row edits it.[/]")
            .AddEmptyLine()
            .Build());

        _toolbar = ViewToolbar.Create("serverToolbar");
        panel.AddControl(_toolbar);
        RebuildToolbar();

        // Server selector — only meaningful when more than one server exists; populated in Update.
        _server = Controls.Dropdown("server")
            .WithMargin(2, 1, 2, 0)
            .WithName("serverSelector")
            .Build();
        panel.AddControl(_server);

        // ── LISTEN & PROTOCOLS ──
        panel.AddControl(Section("LISTEN & PROTOCOLS", accent));
        _listen = Field("listen", "listen addresses (CSV)", 48);
        panel.AddControl(_listen);
        _h1 = Check("h1");
        _h2 = Check("h2");
        _h3 = Check("h3");
        panel.AddControl(_h1);
        panel.AddControl(_h2);
        panel.AddControl(_h3);

        // ── HTTPS ──
        panel.AddControl(Section("HTTPS", accent));
        _disable = Check("disable automatic HTTPS");
        _disableRedir = Check("disable HTTP→HTTPS redirects");
        _disableCerts = Check("disable certificate management");
        panel.AddControl(_disable);
        panel.AddControl(_disableRedir);
        panel.AddControl(_disableCerts);
        _skip = Field("skip", "skip hosts (CSV)", 48);
        panel.AddControl(_skip);

        // ── TIMEOUTS ──
        panel.AddControl(Section("TIMEOUTS", accent));
        _readTo = Field("read", "read timeout (e.g. 30s)", 16);
        _readHdrTo = Field("read hdr", "read header timeout", 16);
        _writeTo = Field("write", "write timeout", 16);
        _idleTo = Field("idle", "idle timeout", 16);
        panel.AddControl(_readTo);
        panel.AddControl(_readHdrTo);
        panel.AddControl(_writeTo);
        panel.AddControl(_idleTo);

        // ── GLOBAL ──
        panel.AddControl(Section("GLOBAL", accent));
        _adminListen = Field("admin listen", "admin listen (e.g. localhost:2019)", 32);
        _httpPort = Field("http port", "http port", 12);
        _httpsPort = Field("https port", "https port", 12);
        _grace = Field("grace", "grace period (e.g. 10s)", 16);
        panel.AddControl(_adminListen);
        panel.AddControl(_httpPort);
        panel.AddControl(_httpsPort);
        panel.AddControl(_grace);

        // ── LOGGING ── read-only table of logging/logs entries; Enter/e edits a log.
        panel.AddControl(Section("LOGGING", accent));
        _logs = Controls.Table()
            .AddColumn("Log", TextJustification.Left, 24)
            .AddColumn("Detail", TextJustification.Left)
            .Rounded().WithBorderColor(UIConstants.MutedText)
            .Interactive().WithVerticalScrollbar(ScrollbarVisibility.Auto)
            .WithName("serverLogs").WithMargin(2, 0, 2, 0).Build();
        panel.AddControl(_logs);

        _status = Controls.Markup().WithMargin(2, 1, 2, 0).StickyBottom().Build();
        panel.AddControl(_status);

        // NavigationView rebuilds this view's content on reopen, so reset dirty and force
        // the next Update to repopulate (empty sentinel never equals a real snapshot json).
        _dirty = false;
        _lastSnapshotJson = "";
    }

    private static MarkupControl Section(string label, string accent) =>
        Controls.Markup()
            .AddEmptyLine()
            .AddLine($"[bold {accent}]{label}[/]")
            .WithMargin(2, 0, 2, 0)
            .Build();

    private static PromptControl Field(string label, string hint, int width) =>
        Controls.Prompt($"{label}: ")
            .WithInput("")
            .WithInputWidth(width)
            .WithName($"server_{hint}")
            .WithMargin(2, 0, 2, 0)
            .Build();

    private static CheckboxControl Check(string label) =>
        new CheckboxControl { Label = label, Checked = false, Margin = new Margin(2, 0, 2, 0) };

    // ── Toolbar ──

    private void RebuildToolbar()
    {
        if (_toolbar is null) return;
        ViewToolbar.Rebuild(_toolbar, new ToolbarAction?[]
        {
            new(ViewToolbar.Caption("✓", "Apply", "s"), () => _ = ApplyAsync()),
            new(ViewToolbar.Caption("⤺", "Revert", "v"), Revert),
            new(ViewToolbar.Caption("⟳", "Reload", "r"), Reload),
        });
    }

    // ── Key handling (routed from the shell before global shortcuts) ──
    // Only act when a control in this view has focus, so global s/v/r aren't stolen.

    public bool TryHandleKey(ConsoleKeyInfo key)
    {
        // Logs table edit: only consume Enter/e when the read-only logs table has focus, so
        // these keys aren't stolen from the form fields elsewhere.
        if ((_logs?.HasFocus ?? false) && (key.Key == ConsoleKey.Enter || key.Key == ConsoleKey.E))
        {
            if (_logs!.SelectedRow?.Tag is string logName && !string.IsNullOrEmpty(logName))
                _ = OpenLogAsync(logName);
            return true;
        }

        if (!HasAnyFocus()) return false;
        switch (key.Key)
        {
            case ConsoleKey.S: _ = ApplyAsync(); return true;
            case ConsoleKey.V: Revert(); return true;
            case ConsoleKey.R: Reload(); return true;
            default: return false;
        }
    }

    private async Task OpenLogAsync(string logName)
    {
        var changed = await LogOutputDialog.ShowAsync(_ws, logName, _editor);
        if (changed) _onRefresh();
    }

    private bool HasAnyFocus() =>
        (_listen?.HasFocus ?? false) || (_skip?.HasFocus ?? false) || (_readTo?.HasFocus ?? false) ||
        (_readHdrTo?.HasFocus ?? false) || (_writeTo?.HasFocus ?? false) || (_idleTo?.HasFocus ?? false) ||
        (_adminListen?.HasFocus ?? false) || (_httpPort?.HasFocus ?? false) || (_httpsPort?.HasFocus ?? false) ||
        (_grace?.HasFocus ?? false) || (_h1?.HasFocus ?? false) || (_h2?.HasFocus ?? false) ||
        (_h3?.HasFocus ?? false) || (_disable?.HasFocus ?? false) || (_disableRedir?.HasFocus ?? false) ||
        (_disableCerts?.HasFocus ?? false) || (_server?.HasFocus ?? false) || (_logs?.HasFocus ?? false);

    // ── Update (poll tick, UI thread) ──

    public void Update(DashboardState state)
    {
        var snap = state.Snapshot;
        if (snap is null || _listen is null) return;

        // The logging table is READ-ONLY (not an editable field), so refresh it on every poll —
        // even while the form is frozen by an in-progress edit. This is the one part that updates
        // while dirty, and it's intentional/safe (it can't clobber user input).
        PopulateLogs(snap.RawConfigJson);

        // Detect an in-progress edit and freeze repopulation so a poll can't clobber input.
        if (!_dirty && HasUnsavedChanges()) _dirty = true;
        if (_dirty)
        {
            // Don't touch field controls while dirty — only the status line (set once).
            if (!_dirtyNoticeShown)
            {
                SetStatus($"[{UIConstants.MutedText.ToMarkup()}]unsaved edits — Apply (s) or Revert (v)[/]");
                _dirtyNoticeShown = true;
            }
            return;
        }
        _dirtyNoticeShown = false;

        try
        {
            using var doc = JsonDocument.Parse(snap.RawConfigJson);
            PopulateFrom(doc.RootElement);
            _lastSnapshotJson = snap.RawConfigJson;
            RebuildToolbar();
        }
        catch (Exception ex)
        {
            SetStatus($"[{UIConstants.Bad.ToMarkup()}]{Escape($"parse error: {ex.Message}")}[/]");
        }
    }

    // Parse the full config root and repopulate every control + its _loaded* sentinel.
    // Shared by Update (snapshot) and Revert (re-parse of _lastSnapshotJson).
    private void PopulateFrom(JsonElement root)
    {
        // ── Servers ── apps/http/servers object keys.
        var servers = new List<string>();
        JsonElement httpEl = default; bool hasHttp = false;
        if (root.TryGetProperty("apps", out var apps) && apps.ValueKind == JsonValueKind.Object &&
            apps.TryGetProperty("http", out httpEl) && httpEl.ValueKind == JsonValueKind.Object)
        {
            hasHttp = true;
            if (httpEl.TryGetProperty("servers", out var srvObj) && srvObj.ValueKind == JsonValueKind.Object)
                foreach (var p in srvObj.EnumerateObject()) servers.Add(p.Name);
        }

        _hasServer = servers.Count > 0;
        SyncServerDropdown(servers);

        // Pick the selected server name (default first; honour a valid dropdown selection).
        string? serverName = null;
        if (_server is { } dd && dd.SelectedValue is { } sv && servers.Contains(sv)) serverName = sv;
        if (serverName is null && servers.Count > 0) serverName = servers[0];
        if (serverName is not null) _serverPath = $"apps/http/servers/{serverName}";

        // ── The selected server object ──
        JsonElement server = default; bool hasServerObj = false;
        if (hasHttp && serverName is not null &&
            httpEl.TryGetProperty("servers", out var srvs) &&
            srvs.TryGetProperty(serverName, out server) && server.ValueKind == JsonValueKind.Object)
            hasServerObj = true;

        // listen (array → CSV)
        string listen = hasServerObj ? JoinArray(server, "listen") : "";
        SetField(_listen, listen, ref _loadedListen);

        // protocols (array of h1/h2/h3)
        var protos = hasServerObj ? ArrayValues(server, "protocols") : new List<string>();
        bool h1 = protos.Contains("h1"), h2 = protos.Contains("h2"), h3 = protos.Contains("h3");
        SetCheck(_h1, h1, ref _loadedH1);
        SetCheck(_h2, h2, ref _loadedH2);
        SetCheck(_h3, h3, ref _loadedH3);

        // automatic_https object
        bool disable = false, disableRedir = false, disableCerts = false; string skip = "";
        if (hasServerObj && server.TryGetProperty("automatic_https", out var ah) && ah.ValueKind == JsonValueKind.Object)
        {
            disable = BoolProp(ah, "disable");
            disableRedir = BoolProp(ah, "disable_redirects");
            disableCerts = BoolProp(ah, "disable_certificates");
            skip = JoinArray(ah, "skip");
        }
        SetCheck(_disable, disable, ref _loadedDisable);
        SetCheck(_disableRedir, disableRedir, ref _loadedDisableRedir);
        SetCheck(_disableCerts, disableCerts, ref _loadedDisableCerts);
        SetField(_skip, skip, ref _loadedSkip);

        // timeouts (duration strings)
        SetField(_readTo, hasServerObj ? StringProp(server, "read_timeout") : "", ref _loadedReadTo);
        SetField(_readHdrTo, hasServerObj ? StringProp(server, "read_header_timeout") : "", ref _loadedReadHdrTo);
        SetField(_writeTo, hasServerObj ? StringProp(server, "write_timeout") : "", ref _loadedWriteTo);
        SetField(_idleTo, hasServerObj ? StringProp(server, "idle_timeout") : "", ref _loadedIdleTo);

        // ── GLOBAL ── apps/http http_port / https_port / grace_period
        string httpPort = "", httpsPort = "", grace = "";
        if (hasHttp)
        {
            httpPort = IntProp(httpEl, "http_port");
            httpsPort = IntProp(httpEl, "https_port");
            grace = StringProp(httpEl, "grace_period");
        }
        SetField(_httpPort, httpPort, ref _loadedHttpPort);
        SetField(_httpsPort, httpsPort, ref _loadedHttpsPort);
        SetField(_grace, grace, ref _loadedGrace);

        // ── admin.listen ── (top-level admin null/absent → AdminObject write path)
        string adminListen = "";
        bool adminNull = true;
        if (root.TryGetProperty("admin", out var admin) && admin.ValueKind == JsonValueKind.Object)
        {
            adminNull = false;
            adminListen = StringProp(admin, "listen");
        }
        _adminWasNull = adminNull;
        SetField(_adminListen, adminListen, ref _loadedAdminListen);

        SetStatus(_hasServer
            ? ""
            : $"[{UIConstants.Warn.ToMarkup()}]No HTTP server found in config.[/]");
    }

    // Keep the dropdown items in sync with the discovered server names (no-op when unchanged).
    private void SyncServerDropdown(List<string> servers)
    {
        if (_server is null) return;
        var current = _server.StringItems;
        if (current.Count == servers.Count && current.SequenceEqual(servers)) return;
        _server.ClearItems();
        foreach (var s in servers) _server.AddItem(s);
        if (servers.Count > 0) _server.SelectedIndex = 0;
    }

    // Repopulate the read-only logging table from logging/logs. Runs every poll (even while the
    // editable form is frozen) since it's display-only. Preserves the selected row index.
    private void PopulateLogs(string rawJson)
    {
        if (_logs is null) return;
        int prev = _logs.SelectedRowIndex;
        _logs.ClearRows();
        int count = 0;
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;
            if (root.TryGetProperty("logging", out var logging) && logging.ValueKind == JsonValueKind.Object &&
                logging.TryGetProperty("logs", out var logs) && logs.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in logs.EnumerateObject())
                {
                    _logs.AddRow(new TableRow(Escape(p.Name), Escape(LogSummary(p.Value))) { Tag = p.Name });
                    count++;
                }
            }
        }
        catch { /* malformed config: leave the table empty */ }
        if (count > 0)
            _logs.SelectedRowIndex = prev >= 0 && prev < count ? prev : 0;
    }

    // A one-line summary of a log: "level · writer-output" plus include/exclude counts when present.
    private static string LogSummary(JsonElement log)
    {
        if (log.ValueKind != JsonValueKind.Object) return "";
        var parts = new List<string>();
        if (log.TryGetProperty("level", out var lv) && lv.ValueKind == JsonValueKind.String)
            parts.Add(lv.GetString() ?? "");
        if (log.TryGetProperty("writer", out var w) && w.ValueKind == JsonValueKind.Object &&
            w.TryGetProperty("output", out var ov) && ov.ValueKind == JsonValueKind.String)
            parts.Add(ov.GetString() ?? "");
        var filters = new List<string>();
        if (log.TryGetProperty("include", out var inc) && inc.ValueKind == JsonValueKind.Array && inc.GetArrayLength() > 0)
            filters.Add($"include:{inc.GetArrayLength()}");
        if (log.TryGetProperty("exclude", out var exc) && exc.ValueKind == JsonValueKind.Array && exc.GetArrayLength() > 0)
            filters.Add($"exclude:{exc.GetArrayLength()}");
        if (filters.Count > 0) parts.Add(string.Join(" ", filters));
        return parts.Count > 0 ? string.Join(" · ", parts) : "(defaults)";
    }

    // ── Change detection ──

    private bool HasUnsavedChanges()
    {
        if (_listen is null) return false;
        return Cur(_listen) != _loadedListen
            || Cur(_skip) != _loadedSkip
            || Cur(_readTo) != _loadedReadTo
            || Cur(_readHdrTo) != _loadedReadHdrTo
            || Cur(_writeTo) != _loadedWriteTo
            || Cur(_idleTo) != _loadedIdleTo
            || Cur(_adminListen) != _loadedAdminListen
            || Cur(_httpPort) != _loadedHttpPort
            || Cur(_httpsPort) != _loadedHttpsPort
            || Cur(_grace) != _loadedGrace
            || Chk(_h1) != _loadedH1
            || Chk(_h2) != _loadedH2
            || Chk(_h3) != _loadedH3
            || Chk(_disable) != _loadedDisable
            || Chk(_disableRedir) != _loadedDisableRedir
            || Chk(_disableCerts) != _loadedDisableCerts;
    }

    // ── Apply ──

    private async Task ApplyAsync()
    {
        if (_listen is null) return;
        try
        {
            var changes = new List<(string Path, string Json, string Old, string New, string Label)>();
            var skipped = new List<string>();

            // listen
            if (Cur(_listen) != _loadedListen)
                changes.Add((
                    $"{_serverPath}/listen",
                    ServerConfigPatch.StringArray(Csv(Cur(_listen))),
                    ServerConfigPatch.StringArray(Csv(_loadedListen)),
                    ServerConfigPatch.StringArray(Csv(Cur(_listen))),
                    $"server listen → {Cur(_listen)}"));

            // protocols
            if (Chk(_h1) != _loadedH1 || Chk(_h2) != _loadedH2 || Chk(_h3) != _loadedH3)
                changes.Add((
                    $"{_serverPath}/protocols",
                    ServerConfigPatch.ProtocolsArray(Chk(_h1), Chk(_h2), Chk(_h3)),
                    ServerConfigPatch.ProtocolsArray(_loadedH1, _loadedH2, _loadedH3),
                    ServerConfigPatch.ProtocolsArray(Chk(_h1), Chk(_h2), Chk(_h3)),
                    "server protocols"));

            // automatic_https
            if (Chk(_disable) != _loadedDisable || Chk(_disableRedir) != _loadedDisableRedir ||
                Chk(_disableCerts) != _loadedDisableCerts || Cur(_skip) != _loadedSkip)
                changes.Add((
                    $"{_serverPath}/automatic_https",
                    ServerConfigPatch.AutomaticHttps(Chk(_disable), Chk(_disableRedir), Chk(_disableCerts), Csv(Cur(_skip))),
                    ServerConfigPatch.AutomaticHttps(_loadedDisable, _loadedDisableRedir, _loadedDisableCerts, Csv(_loadedSkip)),
                    ServerConfigPatch.AutomaticHttps(Chk(_disable), Chk(_disableRedir), Chk(_disableCerts), Csv(Cur(_skip))),
                    "server automatic_https"));

            // timeouts — quoted duration strings; clearing is out of scope (skip if newly empty).
            AddTimeout(changes, skipped, _readTo!, _loadedReadTo, "read_timeout");
            AddTimeout(changes, skipped, _readHdrTo!, _loadedReadHdrTo, "read_header_timeout");
            AddTimeout(changes, skipped, _writeTo!, _loadedWriteTo, "write_timeout");
            AddTimeout(changes, skipped, _idleTo!, _loadedIdleTo, "idle_timeout");

            // admin listen
            if (Cur(_adminListen) != _loadedAdminListen)
            {
                var val = Cur(_adminListen);
                if (val.Length == 0) skipped.Add("admin listen (clearing not supported)");
                else if (_adminWasNull)
                    changes.Add(("admin", ServerConfigPatch.AdminObject(val),
                        "null", ServerConfigPatch.AdminObject(val), $"admin listen → {val}"));
                else
                    changes.Add(("admin/listen", JsonSerializer.Serialize(val),
                        JsonSerializer.Serialize(_loadedAdminListen), JsonSerializer.Serialize(val),
                        $"admin listen → {val}"));
            }

            // http_port / https_port — integers; only when they parse.
            AddPort(changes, skipped, _httpPort!, _loadedHttpPort, "apps/http/http_port", "http_port");
            AddPort(changes, skipped, _httpsPort!, _loadedHttpsPort, "apps/http/https_port", "https_port");

            // grace_period — quoted duration string.
            if (Cur(_grace) != _loadedGrace)
            {
                var val = Cur(_grace);
                if (val.Length == 0) skipped.Add("grace_period (clearing not supported)");
                else changes.Add(("apps/http/grace_period", JsonSerializer.Serialize(val),
                    JsonSerializer.Serialize(_loadedGrace), JsonSerializer.Serialize(val), $"grace_period → {val}"));
            }

            if (changes.Count == 0)
            {
                SetStatus(skipped.Count > 0
                    ? $"[{UIConstants.Warn.ToMarkup()}]{Escape("No applicable changes. Skipped: " + string.Join(", ", skipped))}[/]"
                    : $"[{UIConstants.MutedText.ToMarkup()}]No changes to apply.[/]");
                return;
            }

            // One combined diff: { key: old } vs { key: new } for just the changed fields.
            var oldCombined = CombinedObject(changes, useNew: false);
            var newCombined = CombinedObject(changes, useNew: true);
            if (!await DiffConfirmDialog.ShowAsync(_ws, "Apply server settings", oldCombined, newCombined, null))
                return;

            int applied = 0;
            foreach (var c in changes)
            {
                var path = c.Path; var json = c.Json;
                var r = await _editor.ApplyAsync((a, ct) => a.UpsertConfigAsync(path, json, ct), c.Label);
                if (!r.Success)
                {
                    // Commit the sentinels of the fields that DID write, so they don't stay
                    // forever-dirty; the unwritten ones remain dirty for retry/revert. Recompute
                    // _dirty against the now-partially-committed state.
                    CommitLoadedFor(changes.Take(applied));
                    _dirty = HasUnsavedChanges();
                    SetStatus($"[{UIConstants.Bad.ToMarkup()}]{Escape(r.Error ?? "write failed")}[/]");
                    return; // prior writes stay applied
                }
                applied++;
            }

            // Success: commit loaded sentinels to current, drop dirty, pull fresh state.
            CommitLoaded();
            _adminWasNull = false; // a written admin object now exists; subsequent edits patch admin/listen
            _dirty = false;
            _dirtyNoticeShown = false;
            var msg = $"Applied {applied} change(s).";
            if (skipped.Count > 0) msg += " Skipped: " + string.Join(", ", skipped) + ".";
            SetStatus($"[{UIConstants.Accent.ToMarkup()}]{Escape(msg)}[/]");
            _onRefresh();
        }
        catch (Exception ex)
        {
            SetStatus($"[{UIConstants.Bad.ToMarkup()}]{Escape(ex.Message)}[/]");
        }
    }

    // Add a timeout change if it differs and the new value is non-empty (clearing out of scope v1).
    private void AddTimeout(
        List<(string, string, string, string, string)> changes, List<string> skipped,
        PromptControl ctl, string loaded, string key)
    {
        var val = Cur(ctl);
        if (val == loaded) return;
        if (val.Length == 0) { skipped.Add($"{key} (clearing not supported)"); return; }
        changes.Add((
            $"{_serverPath}/{key}",
            JsonSerializer.Serialize(val),
            JsonSerializer.Serialize(loaded),
            JsonSerializer.Serialize(val),
            $"{key} → {val}"));
    }

    // Add a port change if it differs, parses as int, and the new differs from loaded.
    private void AddPort(
        List<(string, string, string, string, string)> changes, List<string> skipped,
        PromptControl ctl, string loaded, string path, string key)
    {
        var val = Cur(ctl);
        if (val == loaded) return;
        if (!int.TryParse(val, out var port)) { skipped.Add($"{key} (not a number)"); return; }
        changes.Add((
            path,
            port.ToString(),
            loaded.Length == 0 ? "null" : loaded,   // valid JSON for the combined-diff display
            port.ToString(),
            $"{key} → {port}"));
    }

    private static string CombinedObject(
        List<(string Path, string Json, string Old, string New, string Label)> changes, bool useNew)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("{\n");
        for (int i = 0; i < changes.Count; i++)
        {
            var c = changes[i];
            // Key the combined object by the last path segment for readability.
            var key = c.Path.Contains('/') ? c.Path[(c.Path.LastIndexOf('/') + 1)..] : c.Path;
            sb.Append("  ").Append(JsonSerializer.Serialize(key)).Append(": ")
              .Append((useNew ? c.New : c.Old).Replace("\n", "\n  "));
            if (i < changes.Count - 1) sb.Append(',');
            sb.Append('\n');
        }
        sb.Append('}');
        return sb.ToString();
    }

    // ── Revert / Reload ──

    private void Revert()
    {
        _dirty = false;
        _dirtyNoticeShown = false;
        if (_lastSnapshotJson.Length == 0) { SetStatus($"[{UIConstants.MutedText.ToMarkup()}]Nothing to revert.[/]"); return; }
        try
        {
            using var doc = JsonDocument.Parse(_lastSnapshotJson);
            PopulateFrom(doc.RootElement);
            SetStatus($"[{UIConstants.MutedText.ToMarkup()}]Reverted.[/]");
        }
        catch (Exception ex)
        {
            SetStatus($"[{UIConstants.Bad.ToMarkup()}]{Escape($"revert failed: {ex.Message}")}[/]");
        }
    }

    private void Reload()
    {
        _dirty = false;
        _dirtyNoticeShown = false;
        _onRefresh();
        SetStatus($"[{UIConstants.MutedText.ToMarkup()}]Reloading…[/]");
    }

    // ── Small helpers ──

    private void CommitLoaded()
    {
        _loadedListen = Cur(_listen); _loadedSkip = Cur(_skip);
        _loadedReadTo = Cur(_readTo); _loadedReadHdrTo = Cur(_readHdrTo);
        _loadedWriteTo = Cur(_writeTo); _loadedIdleTo = Cur(_idleTo);
        _loadedAdminListen = Cur(_adminListen); _loadedHttpPort = Cur(_httpPort);
        _loadedHttpsPort = Cur(_httpsPort); _loadedGrace = Cur(_grace);
        _loadedH1 = Chk(_h1); _loadedH2 = Chk(_h2); _loadedH3 = Chk(_h3);
        _loadedDisable = Chk(_disable); _loadedDisableRedir = Chk(_disableRedir); _loadedDisableCerts = Chk(_disableCerts);
    }

    // Commit only the loaded sentinels of the given (already-applied) changes, identified by path.
    // Used after a partial-apply failure so the successfully-written fields stop registering dirty.
    private void CommitLoadedFor(IEnumerable<(string Path, string Json, string Old, string New, string Label)> applied)
    {
        foreach (var c in applied)
        {
            if (c.Path.EndsWith("/listen", StringComparison.Ordinal) && c.Path.StartsWith("apps/http/servers", StringComparison.Ordinal)) _loadedListen = Cur(_listen);
            else if (c.Path.EndsWith("/protocols", StringComparison.Ordinal)) { _loadedH1 = Chk(_h1); _loadedH2 = Chk(_h2); _loadedH3 = Chk(_h3); }
            else if (c.Path.EndsWith("/automatic_https", StringComparison.Ordinal)) { _loadedDisable = Chk(_disable); _loadedDisableRedir = Chk(_disableRedir); _loadedDisableCerts = Chk(_disableCerts); _loadedSkip = Cur(_skip); }
            else if (c.Path.EndsWith("/read_timeout", StringComparison.Ordinal)) _loadedReadTo = Cur(_readTo);
            else if (c.Path.EndsWith("/read_header_timeout", StringComparison.Ordinal)) _loadedReadHdrTo = Cur(_readHdrTo);
            else if (c.Path.EndsWith("/write_timeout", StringComparison.Ordinal)) _loadedWriteTo = Cur(_writeTo);
            else if (c.Path.EndsWith("/idle_timeout", StringComparison.Ordinal)) _loadedIdleTo = Cur(_idleTo);
            else if (c.Path == "admin" || c.Path == "admin/listen") { _loadedAdminListen = Cur(_adminListen); if (c.Path == "admin") _adminWasNull = false; }
            else if (c.Path == "apps/http/http_port") _loadedHttpPort = Cur(_httpPort);
            else if (c.Path == "apps/http/https_port") _loadedHttpsPort = Cur(_httpsPort);
            else if (c.Path == "apps/http/grace_period") _loadedGrace = Cur(_grace);
        }
    }

    private static string Cur(PromptControl? c) => (c?.Input ?? "").Trim();
    private static bool Chk(CheckboxControl? c) => c?.Checked ?? false;

    private static void SetField(PromptControl? c, string value, ref string loaded)
    {
        c?.SetInput(value);
        loaded = value;
    }

    private static void SetCheck(CheckboxControl? c, bool value, ref bool loaded)
    {
        if (c is not null) c.Checked = value;
        loaded = value;
    }

    private static string[] Csv(string s) =>
        s.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static bool BoolProp(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static string StringProp(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static string IntProp(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetRawText() : "";

    private static string JoinArray(JsonElement obj, string name) =>
        string.Join(", ", ArrayValues(obj, name));

    private static List<string> ArrayValues(JsonElement obj, string name)
    {
        var list = new List<string>();
        if (obj.TryGetProperty(name, out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var e in arr.EnumerateArray())
                if (e.ValueKind == JsonValueKind.String) list.Add(e.GetString() ?? "");
        return list;
    }

    private void SetStatus(string markup) => _status?.SetContent(new List<string> { markup });

    private static string Escape(string s) => s.Replace("[", "[[").Replace("]", "]]");
}
