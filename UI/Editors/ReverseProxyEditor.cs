// -----------------------------------------------------------------------
// LazyCaddy - ReverseProxyEditor: the reverse_proxy handler's upstreams +
// flush_interval ("stream immediately") as a tab in the consolidated route
// modal. Ported from ReverseProxyForm; the modal-wrapper (DiffConfirm/self-apply)
// is dropped and the LB/health/transport/headers sub-launchers are GONE — those
// are sibling tabs now.
//
// This editor edits TWO nodes, so BuildPatch can return 0, 1, or 2 PendingWrites:
//   - `{path}/upstreams`      if the upstreams prompt changed (skipped if empty —
//                              the modal has no per-field validation; see below)
//   - `{path}/flush_interval` if the "stream immediately" checkbox changed
//
// Single-upstream delete (Del/- on the focused list) is the ONE write this editor
// does OUTSIDE BuildPatch: it's a discrete, targeted DELETE of one upstream index
// (then a re-load to reindex), not a pending field edit — so it goes straight
// through the EditCoordinator captured in LoadAsync. The last-upstream guard
// (_upstreamCount <= 1) is preserved.
// -----------------------------------------------------------------------

using System.Text.Json;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;
using LazyCaddy.Configuration;
using LazyCaddy.Services;

namespace LazyCaddy.UI.Editors;

public sealed class ReverseProxyEditor : IConfigEditor
{
    private readonly string _path;          // reverse_proxy handler node path
    private EditCoordinator? _editor;       // captured in LoadAsync, used by the targeted delete in HandleKey
    private PromptControl? _upstreams;
    private CheckboxControl? _stream;        // flush_interval == -1 (stream immediately)
    private TableControl? _upstreamList;
    private int _upstreamCount;
    private Action? _onDirty;
    private string _origUpstreams = "[]";
    private int _origFlush;

    private string _lUpstreams = "";        // loaded comma-joined dials (prompt value at load)
    private bool _lStream;

    public ReverseProxyEditor(string path) => _path = path;

    public string TabTitle => "Upstreams";
    public string ConfigPath => _path;

    public void Build(ScrollablePanelControl container, Action onDirtyChanged)
    {
        _onDirty = onDirtyChanged;
        var muted = UIConstants.MutedText.ToMarkup();
        container.AddControl(Controls.Markup()
            .AddLine($"[{muted}]Proxy to backends. Comma-separated host:port. LB / health / transport / headers are sibling tabs.[/]")
            .WithMargin(2, 1, 2, 0).Build());
        _upstreams = Controls.Prompt("Upstreams: ").WithInputWidth(48).Build();
        _stream = new CheckboxControl { Label = "Stream immediately (flush_interval = -1)", Checked = false };
        container.AddControl(_upstreams); container.AddControl(_stream);
        container.AddControl(Controls.Markup()
            .AddLine($"[{UIConstants.Accent.ToMarkup()}]Current upstreams[/] [{muted}](focus list, Del/- removes one)[/]")
            .WithMargin(2, 0, 2, 0).Build());
        _upstreamList = Controls.Table()
            .AddColumn("Dial", TextJustification.Left)
            .Rounded().WithBorderColor(UIConstants.MutedText).Interactive()
            .WithVerticalScrollbar(ScrollbarVisibility.Auto).WithName("upstreamList").Build();
        container.AddControl(_upstreamList);
    }

    public async Task LoadAsync(EditCoordinator editor)
    {
        _editor = editor;
        try
        {
            _origUpstreams = await editor.GetConfigNodeAsync($"{_path}/upstreams");
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
        catch (JsonException) { _upstreamCount = 0; }
        catch { _upstreamCount = 0; }

        try
        {
            var fi = await editor.GetConfigNodeAsync($"{_path}/flush_interval");
            int.TryParse(fi.Trim(), out _origFlush);
            if (_stream is not null) _stream.Checked = _origFlush == -1;
        }
        catch { _origFlush = 0; }

        CaptureLoaded();
        _onDirty?.Invoke();
    }

    private void CaptureLoaded()
    {
        _lUpstreams = _upstreams?.Input ?? "";
        _lStream = _stream?.Checked ?? false;
    }

    public bool IsDirty =>
        (_upstreams?.Input ?? "") != _lUpstreams ||
        (_stream?.Checked ?? false) != _lStream;

    public IReadOnlyList<PendingWrite> BuildPatch()
    {
        var writes = new List<PendingWrite>();

        // Upstreams: only if the prompt changed. If empty, SKIP — the form refused empty,
        // but the modal has no per-field validation, so we just don't add the write rather
        // than fail the whole batch.
        if ((_upstreams?.Input ?? "") != _lUpstreams)
        {
            var dials = (_upstreams?.Input ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (dials.Length > 0)
            {
                var newUpstreams = EditPatchBuilder.UpstreamsArray(dials);
                writes.Add(new PendingWrite($"{_path}/upstreams", newUpstreams, _origUpstreams, "upstreams"));
            }
        }

        // flush_interval: only if the checkbox changed.
        var stream = _stream?.Checked ?? false;
        if (stream != _lStream)
        {
            var newFlush = stream ? -1 : 0;
            writes.Add(new PendingWrite($"{_path}/flush_interval", newFlush.ToString(), _origFlush.ToString(), "flush_interval"));
        }

        return writes;
    }

    public void Revert()
    {
        _upstreams?.SetInput(_lUpstreams);
        if (_stream is not null) _stream.Checked = _lStream;
    }

    public bool HandleKey(ConsoleKeyInfo key)
    {
        // Single-upstream delete: a discrete targeted DELETE (not a pending field edit), so it
        // writes immediately through the captured EditCoordinator instead of via BuildPatch.
        if ((key.Key == ConsoleKey.Delete || key.KeyChar == '-') && (_upstreamList?.HasFocus ?? false))
        {
            _ = DeleteSelectedUpstreamAsync();
            return true;
        }
        return false;
    }

    /// <summary>Set by the host modal so a single-upstream delete failure surfaces to the user
    /// (this is the one editor action that writes outside BuildPatch). Null → errors are swallowed
    /// to the debug log only.</summary>
    public Action<string>? OnError { get; set; }

    private async Task DeleteSelectedUpstreamAsync()
    {
        // Fully guarded: this is a fire-and-forget launch from HandleKey, so an unhandled throw
        // (e.g. the post-delete reload failing) would otherwise be silently lost.
        try
        {
            if (_editor is null) return;
            var idx = _upstreamList?.SelectedRowIndex ?? -1;
            if (idx < 0) return;
            if (_upstreamCount <= 1) { OnError?.Invoke("A reverse_proxy needs at least one upstream."); return; }
            var r = await _editor.ApplyAsync((a, ct) => a.DeleteConfigAsync($"{_path}/upstreams/{idx}", ct),
                $"remove upstream #{idx}");
            if (!r.Success) { OnError?.Invoke(r.Error ?? "Upstream delete failed."); return; }
            await LoadAsync(_editor); // refresh prompt + list from live config (reindexes after delete)
        }
        catch (Exception ex) { OnError?.Invoke($"Upstream delete failed: {ex.Message}"); }
    }
}
