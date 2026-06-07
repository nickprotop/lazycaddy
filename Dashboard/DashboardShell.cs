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
using LazyCaddy.Views;

namespace LazyCaddy.Dashboard;

public sealed class DashboardShell
{
    private readonly ConsoleWindowSystem _ws;
    private readonly LazyCaddyConfig _config;
    private readonly ICaddyAdmin _admin;
    private readonly UpstreamProber _prober;
    private readonly EditCoordinator _editor;
    private readonly DashboardState _state = new();

    // Views (each owns its content factory + Update).
    private readonly OverviewView _overview;
    private readonly RoutesView _routes;
    private readonly CertsView _certs;
    private readonly UpstreamsView _upstreams;
    private readonly RawConfigView _rawConfig;
    private readonly SnapshotsView _snapshots;
    private readonly TopologyView _topology = new();

    private Window? _window;
    private NavigationView? _nav;

    // Status bar item handles, mutated in place each tick (Label supports markup).
    private StatusBarItem? _connItem;
    private StatusBarItem? _refreshItem;

    // Lets the R key wake the poll loop immediately instead of waiting for the tick.
    private readonly SemaphoreSlim _refreshSignal = new(0, 1);

    // Spinner frames for the "polling in flight" indicator in the status bar.
    private static readonly string[] SpinnerFrames = { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };

    public DashboardShell(ConsoleWindowSystem ws, LazyCaddyConfig config, ICaddyAdmin admin, UpstreamProber prober, EditCoordinator editor)
    {
        _ws = ws;
        _config = config;
        _admin = admin;
        _prober = prober;
        _editor = editor;

        _overview = new OverviewView(config);
        _routes = new RoutesView(ws, editor);
        _certs = new CertsView(ws, editor);
        _upstreams = new UpstreamsView();
        _rawConfig = new RawConfigView(ws, editor);
        _snapshots = new SnapshotsView(ws, editor);
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

        // Reflow the Overview cards live on terminal resize. ScreenResized may fire
        // off the UI thread, so marshal the relayout back onto it (same event
        // ServerHub uses for its dynamic dashboard layout).
        _ws.ConsoleDriver.ScreenResized += (_, size) =>
            _ws.EnqueueOnUIThread(() =>
            {
                _overview.HandleResize(size.Width);
                _topology.HandleResize();
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
                // Wrap each factory so the view shows the latest snapshot the instant
                // it's first opened, rather than waiting for the next poll tick.
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
            })
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .Build();
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

        // The shortcut hints are also clickable — same actions as the R / Q keys.
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

    private void Quit() => _ws.Shutdown(0);

    // Number keys 1..7 jump to the matching view. The nav item list has a single
    // Header at index 0 ("Caddy"), then the 7 views at indices 1..7 in order:
    // 1 Overview · 2 Routes · 3 TLS/Certs · 4 Upstreams · 5 Raw Config · 6 Snapshots · 7 Topology.
    // So SelectedIndex == digit (header offset of 1). This handler only fires for the
    // main window; modal prompts capture their own keys, so digits aren't hijacked.
    private const int ViewCount = 7;

    private void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        if (_nav is not null && TryDigit(e.KeyInfo, out int view))
        {
            _nav.SelectedIndex = view; // header is index 0, first view is index 1 == digit 1
            e.Handled = true;
            return;
        }

        if (_routes.TryHandleKey(e.KeyInfo)) { e.Handled = true; return; }
        if (_certs.TryHandleKey(e.KeyInfo)) { e.Handled = true; return; }
        if (_snapshots.TryHandleKey(e.KeyInfo)) { e.Handled = true; return; }
        if (_rawConfig.TryHandleKey(e.KeyInfo)) { e.Handled = true; return; }

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

    /// <summary>Map D1..D7 / NumPad1..NumPad7 to a 1-based view index. False otherwise.</summary>
    private static bool TryDigit(ConsoleKeyInfo key, out int view)
    {
        view = key.Key switch
        {
            >= ConsoleKey.D1 and <= ConsoleKey.D7 => key.Key - ConsoleKey.D1 + 1,
            >= ConsoleKey.NumPad1 and <= ConsoleKey.NumPad7 => key.Key - ConsoleKey.NumPad1 + 1,
            _ => 0,
        };
        return view is >= 1 and <= ViewCount;
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
                var snapshot = await FetchSnapshotAsync(ct).ConfigureAwait(false);
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
        ApplyStatusBar(spinner: null);
        _overview.Update(_state);
        _routes.Update(_state);
        _certs.Update(_state);
        _upstreams.Update(_state);
        _rawConfig.Update(_state);
        _snapshots.Update(_state);
        _topology.Update(_state);
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
