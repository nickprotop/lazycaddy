// -----------------------------------------------------------------------
// LazyCaddy - the application shell: NavigationView sidebar + content views,
// a pinned status bar, the maximized main window, and the single background
// polling thread that drives all data.
// -----------------------------------------------------------------------

using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Helpers;
using SharpConsoleUI.Layout;
using SharpConsoleUI.Rendering;
using LazyCaddy.Configuration;
using LazyCaddy.Models;
using LazyCaddy.Services;
using LazyCaddy.UI;
using LazyCaddy.UI.Modals;
using LazyCaddy.Views;

namespace LazyCaddy.Dashboard;

public sealed class DashboardShell
{
    private readonly ConsoleWindowSystem _ws;

    // The active per-server dependency bundle. Switching servers swaps this; the accessors below
    // keep every existing _admin/_prober/_editor/_config use valid by resolving through _active.
    private ConnectionContext _active;
    private readonly ServerStore _serverStore;
    private List<ServerEntry> _servers;
    private int _generation;

    private ICaddyAdmin _admin => _active.Admin;
    private UpstreamProber _prober => _active.Prober;
    private EditCoordinator _editor => _active.Editor;
    private LazyCaddyConfig _config => _active.Config;

    private readonly DashboardState _state = new();

    // Views (each owns its content factory + Update).
    private readonly OverviewView _overview;
    private readonly RoutesView _routes;
    private readonly CertsView _certs;
    private readonly UpstreamsView _upstreams;
    private readonly RawConfigView _rawConfig;
    private readonly SnapshotsView _snapshots;
    private readonly TopologyView _topology = new();
    private readonly LogState _logState = new();
    private readonly LogsView _logs;
    private readonly ServerView _server;

    private Window? _window;
    private NavigationView? _nav;

    // Command portal (Ctrl+K). Registry built once in Create(); portal overlay is transient.
    private readonly CommandRegistry _commands = new();
    private CommandPortal? _portal;
    private SharpConsoleUI.Layout.LayoutNode? _portalNode;

    // Server picker portal (Ctrl+L) + the nav top-right button that toggles it.
    private ButtonControl? _serverButton;
    private ServerPortal? _serverPortal;
    private SharpConsoleUI.Layout.LayoutNode? _serverPortalNode;

    // Status bar item handles, mutated in place each tick (Label supports markup).
    private StatusBarItem? _connItem;
    private StatusBarItem? _refreshItem;

    // Lets the R key wake the poll loop immediately instead of waiting for the tick.
    private readonly SemaphoreSlim _refreshSignal = new(0, 1);

    // The dedicated access-log tail loop (separate from the 5s poll loop).
    private readonly CancellationTokenSource _tailCts = new();
    private LogTailer? _tailer;
    private LogSource _tailSource = LogSource.NotConfigured;
    private const int TailIntervalMs = 1000;

    // Spinner frames for the "polling in flight" indicator in the status bar.
    private static readonly string[] SpinnerFrames = { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };

    public DashboardShell(ConsoleWindowSystem ws, ConnectionContext initial,
        ServerStore serverStore, IReadOnlyList<ServerEntry> servers)
    {
        _ws = ws;
        _active = initial;
        _serverStore = serverStore;
        _servers = servers.ToList();
        _generation = initial.Generation;

        _overview = new OverviewView(_config);
        _routes = new RoutesView(ws, () => _active.Editor);
        _certs = new CertsView(ws, () => _active.Editor);
        _upstreams = new UpstreamsView();
        _rawConfig = new RawConfigView(ws, () => _active.Editor);
        _snapshots = new SnapshotsView(ws, () => _active.Editor);
        _logs = new LogsView(_logState);
        _server = new ServerView(ws, () => _active.Editor, RequestRefresh);
    }

    public void Create()
    {
        _nav = BuildNavigation();
        var statusBar = BuildStatusBar();

        var gradient = ColorGradient.FromColors(UIConstants.BaseBg, UIConstants.BaseEnd);

        Window? window = null;
        window = new WindowBuilder(_ws)
            .WithTitle("LazyCaddy")
            .Maximized()
            .WithBackgroundGradient(gradient, GradientDirection.DiagonalDown)
            .WithColors(UIConstants.PrimaryText, UIConstants.BaseBg)
            // Subtle rounded frame in a dark-neutral grey (matching cxpost/LazyNuGet),
            // same color whether focused or not so there's no bright active-state flash.
            .WithBorderStyle(BorderStyle.Rounded)
            .WithBorderColor(UIConstants.BorderSubtle)
            .WithActiveBorderColor(UIConstants.BorderSubtle)
            .HideTitle()
            .HideTitleButtons()
            .Movable(false)
            .Resizable(false)
            .OnKeyPressed(OnKeyPressed)
            .AddControl(_nav)
            .AddControl(statusBar)
            .WithAsyncWindowThread(PollLoopAsync)
            .BuildAndShow();

        _window = window;
        // Route keys to the command portal before normal focus dispatch (same mechanism as
        // LazyDotIde): while the portal is open it handles Esc/Enter/↑↓/typing via ProcessKey.
        _window.PreviewKeyPressed += OnPreviewKey;
        RegisterCommands();

        // Server picker button in the nav content toolbar (top-right). Clicking it (or Ctrl+L)
        // opens the ServerPortal anchored under it.
        _serverButton = _nav.AddContentToolbarButton(ServerButtonLabel(), (_, _) => ToggleServerPortal());

        _ = Task.Run(TailLoopAsync);

        // Reflow the Overview cards live on terminal resize. ScreenResized may fire
        // off the UI thread, so marshal the relayout back onto it (same event
        // ServerHub uses for its dynamic dashboard layout).
        _ws.ConsoleDriver.ScreenResized += (_, size) =>
            _ws.EnqueueOnUIThread(() =>
            {
                _overview.HandleResize(size.Width);
                _topology.HandleResize();
                // Full-window repaint clears stale regions (e.g. Topology canvas
                // ghosting the Overview cards at the top on resize).
                _window?.Invalidate(true);
            }, "view:reflow");
    }

    private NavigationView BuildNavigation()
    {
        return Controls.NavigationView()
            .WithNavWidth(26)
            .WithPaneHeader($"[bold {UIConstants.Accent.ToMarkup()}]  ◆  LazyCaddy[/]")
            .WithSelectedColors(Color.White, UIConstants.SelectedBg)
            .WithContentBorder(BorderStyle.Rounded)
            .WithContentBorderColor(UIConstants.MutedText)
            .WithContentBackground(UIConstants.ContentBg)
            .WithContentPadding(1, 0, 1, 0)
            .WithPaneDisplayMode(NavigationViewDisplayMode.Auto)
            .WithExpandedThreshold(80)
            .WithCompactThreshold(50)
            .AddHeader("Caddy", UIConstants.Accent, h =>
            {
                // WithInitialData wraps each factory so the view shows the latest snapshot the
                // instant it's first opened, rather than waiting for the next poll tick. The digit
                // shortcut (1..9) matches each item's position; pressing it jumps here and focuses
                // the view's primary control (see FocusPrimaryControl).
                h.AddItem("Overview", icon: "◈", subtitle: "Status at a glance",
                    content: WithInitialData(_overview.Build, _overview.Update));
                h.AddItem("Routes", icon: "↦", subtitle: "Host → upstream",
                    content: WithInitialData(_routes.Build, _routes.Update));
                h.AddItem("TLS / Certs", icon: "🔒", subtitle: "Certificate health",
                    content: WithInitialData(_certs.Build, _certs.Update));
                h.AddItem("Upstreams", icon: "⇡", subtitle: "Reachability probes",
                    content: WithInitialData(_upstreams.Build, _upstreams.Update));
                h.AddItem("Raw Config", icon: "{}", subtitle: "Running config JSON",
                    content: WithInitialData(_rawConfig.Build, _rawConfig.Update));
                h.AddItem("Snapshots", icon: "⟲", subtitle: "Config history",
                    content: WithInitialData(_snapshots.Build, _snapshots.Update));
                h.AddItem("Topology", icon: "⌗", subtitle: "Routing graph",
                    content: WithInitialData(_topology.Build, _topology.Update));
                h.AddItem("Logs", icon: "📜", subtitle: "Live access log",
                    content: WithInitialData(_logs.Build, _logs.Update));
                h.AddItem("Server", icon: "⚙", subtitle: "Server & global settings",
                    content: WithInitialData(_server.Build, _server.Update));
            })
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .Build();
    }

    // After jumping to a view, focus its primary control so the user can act immediately
    // (arrows/Enter/edit keys/scroll) without first pressing Tab to cross into the content. Each
    // view focuses its own control ref (FindControl can't reach into the nav's content panel).
    // Overview is read-only cards — nothing meaningful to focus, so it's omitted.
    private void FocusPrimaryControl(int view)
    {
        switch (view)
        {
            case 2: _routes.FocusPrimary(); break;
            case 3: _certs.FocusPrimary(); break;
            case 4: _upstreams.FocusPrimary(); break;
            case 5: _rawConfig.FocusPrimary(); break;
            case 6: _snapshots.FocusPrimary(); break;
            case 7: _topology.FocusPrimary(); break;
            case 8: _logs.FocusPrimary(); break;
            case 9: _server.FocusPrimary(); break;
        }
    }

    /// <summary>Combine a view's Build factory with an immediate Update so it isn't
    /// blank until the next poll. Runs on the UI thread (content factories do).</summary>
    private Action<ScrollablePanelControl> WithInitialData(
        Action<ScrollablePanelControl> build, Action<DashboardState> update)
        => panel =>
        {
            build(panel);
            if (_state.Snapshot is not null)
                update(_state);
        };

    private StatusBarControl BuildStatusBar()
    {
        var bar = Controls.StatusBar()
            .WithBackgroundColor(UIConstants.HeaderBg)
            .WithForegroundColor(UIConstants.PrimaryText)
            .WithShortcutForegroundColor(UIConstants.Accent)
            .WithStickyPosition(StickyPosition.Bottom)
            .Build();

        // Left zone: endpoint + connection state, then the last-refresh timestamp
        // (moved here from the center). Right zone keeps the shortcut hints.
        _connItem = bar.AddLeftText($"{UIConstants.ConnectionDot(false)} {Escape(_config.AdminApiUrl)}");
        bar.AddLeftSeparator();
        _refreshItem = bar.AddLeftText($"[{UIConstants.MutedText.ToMarkup()}]starting…[/]");

        // The shortcut hints are also clickable — same actions as the keys.
        // Right cluster renders left→right in add order, so Help sits left of Refresh.
        bar.AddRight("F1", "Help", ShowHelp);
        bar.AddRight("R", "Refresh", RequestRefresh);
        bar.AddRight("Q", "Quit", Quit);
        return bar;
    }

    /// <summary>Wake the poll loop now (no-op if a refresh is already pending).</summary>
    private void RequestRefresh()
    {
        if (_refreshSignal.CurrentCount == 0)
        {
            try { _refreshSignal.Release(); } catch (SemaphoreFullException) { }
        }
    }

    private void Quit()
    {
        _tailCts.Cancel();
        _ws.Shutdown(0);
    }

    // Number keys 1..9 jump to the matching view. The nav item list has a single
    // Header at index 0 ("Caddy"), then the 9 views at indices 1..9 in order:
    // 1 Overview · 2 Routes · 3 TLS/Certs · 4 Upstreams · 5 Raw Config · 6 Snapshots · 7 Topology ·
    // 8 Logs · 9 Server. So SelectedIndex == digit (header offset of 1). This handler only
    // fires for the main window; modal prompts capture their own keys, so digits aren't hijacked.
    private const int ViewCount = 9;

    // 1-based nav index of the Logs entry (header is index 0; Overview=1 ... Topology=7, Logs=8).
    private const int LogsNavIndex = 8;

    private void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        // Ctrl+K toggles the command portal, ahead of every other binding so it works from any view.
        if (e.KeyInfo.Key == ConsoleKey.K && (e.KeyInfo.Modifiers & ConsoleModifiers.Control) != 0)
        {
            ToggleCommandPortal();
            e.Handled = true;
            return;
        }

        // Ctrl+L toggles the server picker portal, from any view.
        if (e.KeyInfo.Key == ConsoleKey.L && (e.KeyInfo.Modifiers & ConsoleModifiers.Control) != 0)
        {
            ToggleServerPortal();
            e.Handled = true;
            return;
        }

        if (_nav is not null && TryDigit(e.KeyInfo, out int view))
        {
            _nav.SelectedIndex = view; // header is index 0, first view is index 1 == digit 1
            FocusPrimaryControl(view); // land on the view's primary control, ready to act
            e.Handled = true;
            return;
        }

        if (_routes.TryHandleKey(e.KeyInfo)) { e.Handled = true; return; }
        if (_certs.TryHandleKey(e.KeyInfo)) { e.Handled = true; return; }
        if (_snapshots.TryHandleKey(e.KeyInfo)) { e.Handled = true; return; }
        if (_rawConfig.TryHandleKey(e.KeyInfo)) { e.Handled = true; return; }
        if (_logs.TryHandleKey(e.KeyInfo)) { e.Handled = true; return; }
        if (_server.TryHandleKey(e.KeyInfo)) { e.Handled = true; return; }

        // Help: F1 only.
        if (e.KeyInfo.Key == ConsoleKey.F1)
        {
            ShowHelp();
            e.Handled = true;
            return;
        }

        switch (e.KeyInfo.Key)
        {
            case ConsoleKey.Q:
                Quit();
                e.Handled = true;
                break;
            case ConsoleKey.R:
                RequestRefresh();
                e.Handled = true;
                break;
            case ConsoleKey.U:
                _ = QuickUndoAsync();
                e.Handled = true;
                break;
            case ConsoleKey.S when (e.KeyInfo.Modifiers & ConsoleModifiers.Shift) != 0:
                _ = SnapshotNowAsync();
                e.Handled = true;
                break;
        }
    }

    /// <summary>Map D1..D9 / NumPad1..NumPad9 to a 1-based view index. False otherwise.</summary>
    private static bool TryDigit(ConsoleKeyInfo key, out int view)
    {
        view = key.Key switch
        {
            >= ConsoleKey.D1 and <= ConsoleKey.D9 => key.Key - ConsoleKey.D1 + 1,
            >= ConsoleKey.NumPad1 and <= ConsoleKey.NumPad9 => key.Key - ConsoleKey.NumPad1 + 1,
            _ => 0,
        };
        return view is >= 1 and <= ViewCount;
    }

    // ── Command portal ──────────────────────────────────────────────────

    private static readonly (int Index, string Label, string Icon)[] ViewDescriptors =
    {
        (1, "Overview", "◈"), (2, "Routes", "↦"), (3, "TLS / Certs", "🔒"),
        (4, "Upstreams", "⇡"), (5, "Raw Config", "{}"), (6, "Snapshots", "⟲"),
        (7, "Topology", "⌗"), (8, "Logs", "📜"), (9, "Server", "⚙"),
    };

    private void RegisterCommands()
    {
        var views = ViewDescriptors.Select(v => new ViewDescriptor(v.Index, v.Label, v.Icon)).ToList();
        var actions = new CommandCatalog.Actions(
            GoToView: GoToView,
            Refresh: RequestRefresh,
            QuickUndo: () => _ = QuickUndoAsync(),
            SnapshotNow: () => _ = SnapshotNowAsync(),
            AdaptCaddyfile: () => _ = AdaptCaddyfileModal.ShowAsync(_ws, _editor),
            ShowHelp: ShowHelp,
            Quit: Quit);

        foreach (var c in CommandCatalog.Build(views, actions))
            _commands.Register(c);

        // Views contribute their own (context-aware) commands.
        foreach (var provider in new ICommandProvider[] { _routes, _certs, _snapshots, _rawConfig, _server })
            foreach (var c in provider.GetCommands())
                _commands.Register(c);
    }

    private void ShowHelp() => _ = HelpModal.ShowAsync(_ws, _commands);

    private void GoToView(int view)
    {
        if (_nav is null) return;
        _nav.SelectedIndex = view;
        FocusPrimaryControl(view);
    }

    private CommandContext BuildContext()
        => new(
            CurrentViewIndex: _nav?.SelectedIndex ?? 0,
            SelectedTag: CurrentSelectedTag(),
            HasSnapshots: _editor.Snapshots.MostRecent() is not null,
            Editor: _editor);

    // The selected row tag of whichever view is open (for context-aware command predicates).
    private object? CurrentSelectedTag() => (_nav?.SelectedIndex) switch
    {
        2 => _routes.SelectedTag,
        3 => _certs.SelectedTag,
        6 => _snapshots.SelectedTag,
        _ => null,
    };

    // While the portal is open, forward every key to it before normal focus dispatch.
    private void OnPreviewKey(object? sender, KeyPressedEventArgs e)
    {
        if (_serverPortal is not null && _serverPortal.ProcessKey(e.KeyInfo)) { e.Handled = true; return; }
        if (_portal is null) return;
        if (_portal.ProcessKey(e.KeyInfo))
            e.Handled = true;
    }

    private void ToggleCommandPortal()
    {
        if (_portal is not null) { DismissPortal(); return; }
        if (_window is null || _nav is null) return;

        var portal = new CommandPortal(_commands, BuildContext(), _window.Width, _window.Height);
        portal.CommandSelected += (_, cmd) =>
        {
            var ctx = BuildContext();
            DismissPortal();
            if (cmd is not null && cmd.IsEnabled(ctx))
            {
                try { cmd.Execute(ctx); }
                catch (Exception ex) { _ws.EnqueueOnUIThread(() => ShowTransientError(ex.Message)); }
            }
        };
        portal.DismissRequested += (_, _) => DismissPortal();

        _portal = portal;
        _portalNode = _window.CreatePortal(_nav, portal);
        portal.FocusSearch(); // move window focus into the portal so it receives keystrokes
    }

    private void DismissPortal()
    {
        if (_portal is null) return;
        if (_window is not null && _nav is not null && _portalNode is not null)
            _window.RemovePortal(_nav, _portalNode);
        _portal = null;
        _portalNode = null;
    }

    // ── Server picker (Ctrl+L) ──────────────────────────────────────────

    private string ServerButtonLabel()
    {
        var s = _active.Server;
        var tag = s.IsEphemeral ? "(cli)" : s.Name;
        return $"⚐ {tag} ▾";
    }

    private void UpdateServerButtonLabel()
    {
        if (_serverButton is not null) _serverButton.Text = ServerButtonLabel();
    }

    /// <summary>Switch the active server in-process: build a fresh context off the UI thread, then
    /// swap it, clear state, and force a repaint. The poll loop's generation guard drops any in-flight
    /// result from the previous server.</summary>
    private async Task SwitchToAsync(ServerEntry entry)
    {
        if (entry.Identity == _active.Server.Identity) return;   // already active

        var snapshotRoot = LazyCaddyConfig.Default.SnapshotDir;
        int nextGen = ++_generation;
        var next = await Task.Run(() => ConnectionContext.Create(entry, snapshotRoot, nextGen)).ConfigureAwait(true);

        var old = _active;
        _active = next;
        _state.Reset();
        UpdateServerButtonLabel();
        ApplyAll();
        RequestRefresh();
        old.Dispose();
    }

    private void ToggleServerPortal()
    {
        if (_serverPortal is not null) { DismissServerPortal(); return; }
        if (_window is null || _serverButton is null) return;
        var portal = new ServerPortal(_servers, _active.Server, _window.Width, _window.Height);
        portal.ServerSelected += (_, entry) => { DismissServerPortal(); _ = SwitchToAsync(entry); };
        portal.AddRequested += (_, _) => { DismissServerPortal(); _ = AddServerAsync(); };
        portal.ManageRequested += (_, _) => { DismissServerPortal(); _ = ManageServersAsync(); };
        portal.DismissRequested += (_, _) => DismissServerPortal();
        portal.CancelRequested += (_, _) => DismissServerPortal();
        _serverPortal = portal;
        _serverPortalNode = _window.CreatePortal(_serverButton, portal);
        portal.FocusList(); // move window focus into the portal so it receives keystrokes
    }

    private void DismissServerPortal()
    {
        if (_serverPortal is null) return;
        if (_window is not null && _serverButton is not null && _serverPortalNode is not null)
            _window.RemovePortal(_serverButton, _serverPortalNode);
        _serverPortal = null; _serverPortalNode = null;
    }

    // CRUD entrypoints.
    private async Task AddServerAsync()
    {
        var entry = await EditServerModal.ShowAsync(_ws, null, _servers);
        if (entry is null) return;
        _servers.Add(entry);
        _serverStore.Save(_servers);
        await SwitchToAsync(entry);
    }

    private async Task ManageServersAsync()
    {
        var changed = await ManageServersModal.ShowAsync(_ws, _servers, _active.Server, _serverStore);
        if (!changed) return;
        var current = _servers.FirstOrDefault(s => s.Identity == _active.Server.Identity);
        if (current is null) UpdateServerButtonLabel();
        else if (current != _active.Server) await SwitchToAsync(current);
        else UpdateServerButtonLabel();
    }

    private void ShowTransientError(string message)
    {
        if (_connItem is not null)
            _connItem.Label = $"[{UIConstants.Bad.ToMarkup()}]{message.Replace("[", "[[").Replace("]", "]]")}[/]";
    }

    private async Task QuickUndoAsync()
    {
        var snap = _editor.Snapshots.MostRecent();
        if (snap is null) return;
        await _editor.RestoreAsync(snap);
        RequestRefresh();
    }

    private async Task SnapshotNowAsync()
    {
        var label = await UI.Modals.SnapshotNowDialog.ShowAsync(_ws);
        if (label is null) return;
        try
        {
            var cfg = await _admin.GetRawConfigAsync();
            _editor.Snapshots.Capture(cfg, label);
        }
        catch { /* ignore */ }
        RequestRefresh();
    }

    // ── The single background poll loop ──────────────────────────────────
    // Runs on a background Task (off the UI thread). All fetching is awaited
    // async I/O; every control mutation is marshalled back via EnqueueOnUIThread.
    private async Task PollLoopAsync(Window window, CancellationToken ct)
    {
        int spinnerTick = 0;

        while (!ct.IsCancellationRequested)
        {
            // Show the in-flight spinner on the status bar.
            _state.SetConnecting();
            var frame = SpinnerFrames[spinnerTick++ % SpinnerFrames.Length];
            _ws.EnqueueOnUIThread(() => ApplyStatusBar(frame), "poll:spinner");

            try
            {
                var gen = _active.Generation;
                var snapshot = await FetchSnapshotAsync(ct).ConfigureAwait(false);
                if (gen != _active.Generation) continue;   // switched mid-fetch → drop stale result
                _state.SetConnected(snapshot);
                _ws.EnqueueOnUIThread(ApplyAll, "poll:apply");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _state.SetDisconnected(ex.Message);
                _ws.EnqueueOnUIThread(() => ApplyStatusBar(spinner: null), "poll:apply-error");
            }

            // Wait for the next tick OR an explicit R refresh, whichever comes first.
            try
            {
                await _refreshSignal.WaitAsync(_config.RefreshIntervalMs, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
        }
    }

    // Dedicated ~1s loop that tails the access-log file while the Logs view is active,
    // separate from the 5s poll loop so the feed feels live. Off-UI-thread; the only UI
    // touch is marshalled via EnqueueOnUIThread. Cancelled on Quit.
    private async Task TailLoopAsync()
    {
        var ct = _tailCts.Token;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_logState.IsActive)
                {
                    EnsureTailer();
                    if (_tailer is not null)
                    {
                        var r = _tailer.ReadNewLines();
                        _logState.LastTail = r.Kind;
                        if (r.Kind == TailKind.Lines && r.Lines.Count > 0)
                        {
                            var parsed = r.Lines
                                .Select(AccessLogParser.Parse)
                                .Where(e => e is not null)
                                .Select(e => e!)
                                .ToList();
                            if (parsed.Count > 0) _logState.Append(parsed);
                        }
                    }
                    _ws.EnqueueOnUIThread(_logs.ApplyNew, "tail:apply");
                }
            }
            catch { /* never let the tail loop die; next tick retries */ }

            try { await Task.Delay(TailIntervalMs, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    // Resolve the log source from the latest snapshot's config; (re)create the tailer when the
    // resolved path changes. Runs on the tail thread; reads the thread-safe DashboardState snapshot.
    private void EnsureTailer()
    {
        var snap = _state.Snapshot;
        var configJson = snap?.RawConfigJson ?? "";
        var source = AccessLogLocator.Resolve(configJson, _config.AccessLogPath,
            AccessLogLocator.UrlIsLocal(_config.AdminApiUrl));
        _logState.Source = source;

        if (source.Kind != LogSourceKind.File)
        {
            _tailer = null;
            _tailSource = source;
            return;
        }
        if (_tailSource.Kind != LogSourceKind.File || _tailSource.Path != source.Path)
        {
            _tailer = new LogTailer(source.Path!);
            _tailSource = source;
        }
    }

    private async Task<CaddySnapshot> FetchSnapshotAsync(CancellationToken ct)
    {
        // All awaited off-thread; no UI access here.
        var status = await _admin.GetStatusAsync(ct).ConfigureAwait(false);
        var routes = await _admin.GetRoutesAsync(ct).ConfigureAwait(false);
        var certs = await _admin.GetCertsAsync(ct).ConfigureAwait(false);
        var upstreams = await _admin.GetUpstreamsAsync(ct).ConfigureAwait(false);
        var metrics = _config.EnableRequestRateSparkline
            ? await _admin.GetMetricsAsync(ct).ConfigureAwait(false)
            : MetricsSnapshot.Unavailable;
        var rawConfig = await _admin.GetRawConfigAsync(ct).ConfigureAwait(false);

        // Active reachability probes (TCP), concurrently.
        var probed = await _prober.ProbeAllAsync(upstreams, ct).ConfigureAwait(false);

        return new CaddySnapshot(status, routes, certs, probed, metrics, rawConfig, DateTimeOffset.Now);
    }

    // ── UI-thread appliers (only ever called inside EnqueueOnUIThread) ──

    private void ApplyAll()
    {
        // Gate the Logs tail loop to when the Logs view is actually shown.
        _logState.IsActive = _nav is not null && _nav.SelectedIndex == LogsNavIndex;
        ApplyStatusBar(spinner: null);
        _overview.Update(_state);
        _routes.Update(_state);
        _certs.Update(_state);
        _upstreams.Update(_state);
        _rawConfig.Update(_state);
        _snapshots.Update(_state);
        _topology.Update(_state);
        _logs.Update(_state);
        _server.Update(_state);
    }

    private void ApplyStatusBar(string? spinner)
    {
        if (_connItem is null || _refreshItem is null) return;

        var endpoint = Escape(_config.AdminApiUrl);
        var connected = _state.Connection == ConnectionState.Connected;
        var spin = spinner is null ? string.Empty : $" [{UIConstants.Accent.ToMarkup()}]{spinner}[/]";

        _connItem.Label = _state.Connection switch
        {
            ConnectionState.Connected => $"{UIConstants.ConnectionDot(true)} {endpoint}",
            ConnectionState.Connecting => $"{UIConstants.ConnectionDot(connected)} {endpoint}{spin}",
            _ => $"{UIConstants.ConnectionDot(false)} {endpoint} [{UIConstants.Bad.ToMarkup()}]disconnected[/]",
        };

        var snap = _state.Snapshot;
        var muted = UIConstants.MutedText.ToMarkup();
        _refreshItem.Label = snap is not null
            ? $"[{muted}]last refresh {snap.Timestamp:HH:mm:ss}[/]"
            : $"[{muted}]waiting…[/]";
    }

    private static string Escape(string s) => s.Replace("[", "[[").Replace("]", "]]");
}
