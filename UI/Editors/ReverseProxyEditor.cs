// -----------------------------------------------------------------------
// LazyCaddy - ReverseProxyEditor: the reverse_proxy handler's upstreams +
// flush_interval ("stream immediately") as a tab in the consolidated route
// modal. LB / health / transport / headers are sibling tabs.
//
// Upstreams are a proper CRUD list (the table is the source of truth), with
// Add / Edit / Remove BUTTONS ABOVE the table. Edit/Remove are adaptive — only
// enabled when a row is selected. Add and Edit open a small dialog to enter the
// dial string (host:port). All edits are STAGED — nothing is written until the
// modal's batched Apply, which then PATCHes the whole `{path}/upstreams` array.
//
// BuildPatch returns 0, 1, or 2 PendingWrites:
//   - `{path}/upstreams`      if the dial list changed (skipped if it would be empty —
//                              a reverse_proxy needs at least one upstream)
//   - `{path}/flush_interval` if the "stream immediately" checkbox changed
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
    private CheckboxControl? _stream;        // flush_interval == -1 (stream immediately)
    private TableControl? _upstreamList;
    private ButtonControl? _editBtn;
    private ButtonControl? _removeBtn;
    private Action? _onDirty;
    private int _origFlush;

    private readonly List<string> _dials = new();   // canonical, edited list (mirrored into the table)
    private List<string> _lDials = new();           // loaded baseline for dirty/diff
    private string _origUpstreams = "[]";           // loaded raw array JSON (for the diff "old" side)
    private bool _lStream;

    public ReverseProxyEditor(string path) => _path = path;

    public string TabTitle => "Upstreams";
    public string ConfigPath => _path;

    /// <summary>Set by the host modal so add/remove validation messages surface to the user.</summary>
    public Action<string>? OnError { get; set; }

    /// <summary>Set by the host modal: open the upstream dialog (title, initial) → dial or null.
    /// Lets the editor prompt without taking a dependency on the window system.</summary>
    public Func<string, string, Task<string?>>? DialPrompt { get; set; }

    /// <summary>Set by the host modal: force the window to rebuild its layout. Needed because adding
    /// or removing table rows is a STRUCTURAL change — the persistent layout tree won't re-measure
    /// the table's height on a plain repaint, so the table wouldn't grow/shrink without this.</summary>
    public Action? RequestRelayout { get; set; }

    /// <summary>Set by the host modal: confirm a destructive action (what → yes/no). Lets the editor
    /// ask before removing an upstream without depending on the window system.</summary>
    public Func<string, Task<bool>>? ConfirmRemove { get; set; }

    public void Build(IControlHost container, Action onDirtyChanged)
    {
        _onDirty = onDirtyChanged;
        var muted = UIConstants.MutedText.ToMarkup();
        container.AddControl(Controls.Markup()
            .AddLine($"[{muted}]Proxy targets (host:port). LB / health / transport / headers are sibling tabs.[/]")
            .WithMargin(2, 1, 2, 0).Build());

        _stream = new CheckboxControl { Label = "Stream immediately (flush_interval = -1)", Checked = false };
        container.AddControl(_stream);

        container.AddControl(Controls.Markup()
            .AddLine($"[{UIConstants.Accent.ToMarkup()}]Upstreams[/]")
            .WithMargin(2, 1, 2, 0).Build());

        // Buttons ABOVE the table; Edit/Remove are adaptive (enabled only with a selection).
        _editBtn = UIConstants.ActionButton("Edit", "", () => _ = EditSelectedAsync());
        _removeBtn = UIConstants.ActionButton("Remove", "Del", () => _ = RemoveSelectedAsync());
        container.AddControl(Controls.Toolbar()
            .AddButton(UIConstants.ActionButton("Add", "Ctrl+A", () => _ = AddAsync()))
            .AddButton(_editBtn)
            .AddButton(_removeBtn)
            .WithSpacing(2).WithMargin(2, 0, 2, 0).Build());

        _upstreamList = Controls.Table()
            .AddColumn("Dial", TextJustification.Left)
            .Rounded().WithBorderColor(UIConstants.MutedText).Interactive()
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .WithVerticalScrollbar(ScrollbarVisibility.Auto).WithName("upstreamList").Build();
        _upstreamList.SelectedRowChanged += (_, _) => UpdateButtonStates();
        // Enter / double-click on a row edits it (same as the Edit button).
        _upstreamList.RowActivated += (_, _) => _ = EditSelectedAsync();
        container.AddControl(_upstreamList);

        RefreshTable();
    }

    public async Task LoadAsync(EditCoordinator editor)
    {
        _dials.Clear();
        try
        {
            _origUpstreams = await editor.GetConfigNodeAsync($"{_path}/upstreams");
            using var d = JsonDocument.Parse(_origUpstreams);
            if (d.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var u in d.RootElement.EnumerateArray())
                    if (u.TryGetProperty("dial", out var dial) && dial.GetString() is { Length: > 0 } s)
                        _dials.Add(s);
            }
        }
        catch (JsonException) { _origUpstreams = "[]"; }
        catch { _origUpstreams = "[]"; }

        try
        {
            var fi = await editor.GetConfigNodeAsync($"{_path}/flush_interval");
            int.TryParse(fi.Trim(), out _origFlush);
            if (_stream is not null) _stream.Checked = _origFlush == -1;
        }
        catch { _origFlush = 0; }

        CaptureLoaded();
        RefreshTable();
        _onDirty?.Invoke();
    }

    private void CaptureLoaded()
    {
        _lDials = new List<string>(_dials);
        _lStream = _stream?.Checked ?? false;
    }

    private void RefreshTable()
    {
        if (_upstreamList is null) return;
        var keep = _upstreamList.SelectedRowIndex;
        _upstreamList.ClearRows();
        foreach (var dial in _dials) _upstreamList.AddRow(new TableRow(dial));
        _upstreamList.SelectedRowIndex = _dials.Count == 0 ? -1 : Math.Clamp(keep < 0 ? 0 : keep, 0, _dials.Count - 1);
        UpdateButtonStates();
        RequestRelayout?.Invoke(); // row count changed → rebuild layout so the Fill table re-measures its height
    }

    private void UpdateButtonStates()
    {
        var hasSel = (_upstreamList?.SelectedRowIndex ?? -1) >= 0 && _dials.Count > 0;
        if (_editBtn is not null) _editBtn.IsEnabled = hasSel;
        if (_removeBtn is not null) _removeBtn.IsEnabled = hasSel;
    }

    // --- CRUD (staged: edit the list, write on Apply) -----------------------------

    private async Task AddAsync()
    {
        if (DialPrompt is null) return;
        var dial = await DialPrompt("Add upstream", "");
        if (dial is null) return;
        if (_dials.Contains(dial)) { OnError?.Invoke($"Upstream {dial} is already listed."); return; }
        _dials.Add(dial);
        RefreshTable();
        if (_upstreamList is not null) _upstreamList.SelectedRowIndex = _dials.Count - 1;
        UpdateButtonStates();
        _onDirty?.Invoke();
    }

    private async Task EditSelectedAsync()
    {
        if (DialPrompt is null) return;
        var idx = _upstreamList?.SelectedRowIndex ?? -1;
        if (idx < 0 || idx >= _dials.Count) return;
        var dial = await DialPrompt("Edit upstream", _dials[idx]);
        if (dial is null) return;
        if (_dials.Contains(dial) && !string.Equals(dial, _dials[idx], StringComparison.Ordinal))
        { OnError?.Invoke($"Upstream {dial} is already listed."); return; }
        _dials[idx] = dial;
        RefreshTable();
        if (_upstreamList is not null) _upstreamList.SelectedRowIndex = idx;
        _onDirty?.Invoke();
    }

    private async Task RemoveSelectedAsync()
    {
        var idx = _upstreamList?.SelectedRowIndex ?? -1;
        if (idx < 0 || idx >= _dials.Count) return;
        if (_dials.Count <= 1) { OnError?.Invoke("A reverse_proxy needs at least one upstream."); return; }
        var dial = _dials[idx];
        if (ConfirmRemove is not null && !await ConfirmRemove($"upstream {dial}")) return;
        // Index may have shifted while the confirm dialog was open; re-resolve by value.
        var cur = _dials.IndexOf(dial);
        if (cur < 0) return;
        _dials.RemoveAt(cur);
        RefreshTable();
        _onDirty?.Invoke();
    }

    public bool IsDirty =>
        !_dials.SequenceEqual(_lDials) ||
        (_stream?.Checked ?? false) != _lStream;

    public IReadOnlyList<PendingWrite> BuildPatch()
    {
        var writes = new List<PendingWrite>();

        // Upstreams: write the whole array if the list changed. Skip an empty list (a
        // reverse_proxy needs at least one upstream) rather than failing the whole batch.
        if (!_dials.SequenceEqual(_lDials) && _dials.Count > 0)
        {
            var newUpstreams = EditPatchBuilder.UpstreamsArray(_dials);
            writes.Add(new PendingWrite($"{_path}/upstreams", newUpstreams, _origUpstreams, "upstreams"));
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
        _dials.Clear();
        _dials.AddRange(_lDials);
        if (_stream is not null) _stream.Checked = _lStream;
        RefreshTable();
    }

    public bool HandleKey(ConsoleKeyInfo key)
    {
        // Ctrl+A adds an upstream (anywhere on the Upstreams tab; Ctrl-gated so it doesn't
        // collide with typing).
        if (key.Key == ConsoleKey.A && (key.Modifiers & ConsoleModifiers.Control) != 0)
        {
            _ = AddAsync();
            return true;
        }
        // Del on the focused upstream table removes the selected row (same as the button).
        if ((key.Key == ConsoleKey.Delete || key.KeyChar == '-') && (_upstreamList?.HasFocus ?? false))
        {
            _ = RemoveSelectedAsync();
            return true;
        }
        return false;
    }
}
