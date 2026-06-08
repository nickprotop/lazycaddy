using System.Text.Json;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;
using LazyCaddy.Configuration;
using LazyCaddy.Services;

namespace LazyCaddy.UI.Modals.Handlers;

public sealed class ReverseProxyForm : ModalBase<bool>
{
    private readonly string _path;
    private readonly EditCoordinator _editor;
    private PromptControl? _upstreams;
    private CheckboxControl? _stream;     // flush_interval == -1 (stream immediately)
    private TableControl? _upstreamList;
    private int _upstreamCount;
    private MarkupControl? _error;
    private string _origUpstreams = "[]";
    private int _origFlush;

    private ReverseProxyForm(string path, EditCoordinator editor) { _path = path; _editor = editor; }

    public static Task<bool> ShowAsync(ConsoleWindowSystem ws, string path, EditCoordinator editor, Window? parent = null)
        => ((ModalBase<bool>)new ReverseProxyForm(path, editor)).ShowAsync(ws, parent);

    protected override string GetTitle() => " Edit reverse_proxy ";
    protected override (int width, int height) GetSize() => (74, 19);
    protected override bool GetDefaultResult() => false;

    protected override void BuildContent()
    {
        var muted = UIConstants.MutedText.ToMarkup();
        Modal.AddControl(Controls.Markup()
            .AddLine($"[{muted}]Proxy to backends. Comma-separated host:port. Advanced (LB/health/transport) via raw edit.[/]")
            .WithMargin(2, 1, 2, 0).Build());
        _upstreams = Controls.Prompt("Upstreams: ").WithInputWidth(48).Build();
        _stream = new CheckboxControl { Label = "Stream immediately (flush_interval = -1)", Checked = false };
        Modal.AddControl(_upstreams); Modal.AddControl(_stream);
        Modal.AddControl(Controls.Markup().AddLine($"[{UIConstants.Accent.ToMarkup()}]Current upstreams[/] [{muted}](focus list, Del/- removes one)[/]").WithMargin(2, 0, 2, 0).Build());
        _upstreamList = Controls.Table()
            .AddColumn("Dial", TextJustification.Left)
            .Rounded().WithBorderColor(UIConstants.MutedText).Interactive()
            .WithVerticalScrollbar(ScrollbarVisibility.Auto).WithName("upstreamList").Build();
        Modal.AddControl(_upstreamList);
        _error = Controls.Markup().WithMargin(2, 1, 2, 0).Build(); Modal.AddControl(_error);
        Modal.AddControl(Controls.Markup().AddLine($"[{muted}]Enter: apply upstreams   l: load balancing   c: health checks   t: transport   h: headers   Del: remove upstream   Esc: cancel[/]").WithMargin(2, 0, 2, 0).StickyBottom().Build());
        RunGuarded(LoadAsync, Err);
    }

    private async Task LoadAsync()
    {
        try
        {
            _origUpstreams = await _editor.GetConfigNodeAsync($"{_path}/upstreams");
            using var d = JsonDocument.Parse(_origUpstreams);
            if (d.RootElement.ValueKind == JsonValueKind.Array)
            {
                var dials = d.RootElement.EnumerateArray()
                    .Where(u => u.TryGetProperty("dial", out _))
                    .Select(u => u.GetProperty("dial").GetString() ?? "")
                    .Where(s => s.Length > 0).ToList();
                _upstreams?.SetInput(string.Join(", ", dials));
                _upstreamCount = dials.Count;
                if (_upstreamList is not null)
                {
                    _upstreamList.ClearRows();
                    foreach (var dial in dials) _upstreamList.AddRow(new TableRow(dial));
                    _upstreamList.SelectedRowIndex = 0; // reset even when empty so no stale index survives
                }
            }
        }
        // On any failure, force _upstreamCount to 0 (fail-safe — the <=1 guard then blocks delete,
        // so a silently-stale count from a previous load can never bypass the last-upstream guard).
        catch (JsonException ex) { _upstreamCount = 0; Err($"Could not parse upstreams: {ex.Message}"); }
        catch (Exception ex) { _upstreamCount = 0; Err($"Could not load upstreams: {ex.Message}"); }

        try
        {
            var fi = await _editor.GetConfigNodeAsync($"{_path}/flush_interval");
            int.TryParse(fi.Trim(), out _origFlush);
            if (_stream is not null) _stream.Checked = _origFlush == -1;
        }
        catch { _origFlush = 0; }
    }

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        if (e.KeyInfo.Key == ConsoleKey.Escape) { CloseWithResult(false); e.Handled = true; return; }
        if (e.KeyInfo.Key == ConsoleKey.L) { e.Handled = true; _ = LoadBalancingForm.ShowAsync(WindowSystem, _path, _editor, Modal); return; }
        if (e.KeyInfo.Key == ConsoleKey.C) { e.Handled = true; _ = HealthChecksForm.ShowAsync(WindowSystem, _path, _editor, Modal); return; }
        if (e.KeyInfo.Key == ConsoleKey.T) { e.Handled = true; _ = HttpTransportForm.ShowAsync(WindowSystem, _path, _editor, Modal); return; }
        if (e.KeyInfo.Key == ConsoleKey.H) { e.Handled = true; _ = HeadersForm.ShowAsync(WindowSystem, $"{_path}/headers", _editor, Modal); return; }
        if ((e.KeyInfo.Key == ConsoleKey.Delete || e.KeyInfo.KeyChar == '-')
            && (_upstreamList?.HasFocus ?? false))
        { e.Handled = true; RunGuarded(DeleteSelectedUpstreamAsync, Err); return; }
        if (e.KeyInfo.Key == ConsoleKey.Enter) { e.Handled = true; RunGuarded(ApplyAsync, Err); }
    }

    private async Task ApplyAsync()
    {
        var dials = (_upstreams?.Input ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (dials.Length == 0) { Err("Enter at least one upstream (host:port)."); return; }
        var newUpstreams = EditPatchBuilder.UpstreamsArray(dials);
        var newFlush = (_stream?.Checked ?? false) ? -1 : 0;

        // Diff shows upstreams (the main change); apply upstreams then flush as targeted PATCHes.
        if (!await DiffConfirmDialog.ShowAsync(WindowSystem, "Apply reverse_proxy upstreams", _origUpstreams, newUpstreams, Modal)) return;

        var r1 = await _editor.ApplyAsync((a, ct) => a.UpsertConfigAsync($"{_path}/upstreams", newUpstreams, ct),
            $"reverse_proxy {string.Join(", ", dials)}");
        if (!r1.Success) { Err(r1.Error ?? "Upstream write failed."); return; }

        if (newFlush != _origFlush)
        {
            var r2 = await _editor.ApplyAsync((a, ct) => a.UpsertConfigAsync($"{_path}/flush_interval", newFlush.ToString(), ct),
                $"reverse_proxy flush_interval = {newFlush}");
            if (!r2.Success) { Err(r2.Error ?? "flush_interval write failed."); return; }
        }
        CloseWithResult(true);
    }

    private async Task DeleteSelectedUpstreamAsync()
    {
        var idx = _upstreamList?.SelectedRowIndex ?? -1;
        if (idx < 0) return;
        if (_upstreamCount <= 1) { Err("A reverse_proxy needs at least one upstream — delete the route instead."); return; }
        var r = await _editor.ApplyAsync((a, ct) => a.DeleteConfigAsync($"{_path}/upstreams/{idx}", ct),
            $"remove upstream #{idx}");
        if (!r.Success) { Err(r.Error ?? "Delete failed."); return; }
        await LoadAsync(); // refresh prompt + list from live config (reindexes after delete)
    }

    private void Err(string m) =>
        _error?.SetContent(new List<string> { $"[{UIConstants.Bad.ToMarkup()}]{m.Replace("[", "[[").Replace("]", "]]")}[/]" });
}
