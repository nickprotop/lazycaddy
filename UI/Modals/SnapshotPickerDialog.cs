// -----------------------------------------------------------------------
// LazyCaddy - Snapshot preview/restore dialog. Shows the captured config
// JSON read-only; 'r' restores it to the live Caddy.
// -----------------------------------------------------------------------

using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;
using LazyCaddy.Configuration;
using LazyCaddy.Models;
using LazyCaddy.Services;

namespace LazyCaddy.UI.Modals;

public sealed class SnapshotPickerDialog : ModalBase<bool>
{
    private readonly Snapshot _snap;
    private readonly EditCoordinator _editor;
    private MarkupControl? _error;

    private SnapshotPickerDialog(Snapshot snap, EditCoordinator editor) { _snap = snap; _editor = editor; }

    public static Task<bool> ShowAsync(ConsoleWindowSystem ws, Snapshot snap, EditCoordinator editor, Window? parent = null)
        => ((ModalBase<bool>)new SnapshotPickerDialog(snap, editor)).ShowAsync(ws, parent);

    protected override string GetTitle() => $" Snapshot {_snap.TimestampUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss} ";
    protected override (int width, int height) GetSize() => (90, 30);
    protected override bool GetDefaultResult() => false;

    protected override void BuildContent()
    {
        var viewer = Controls.MultilineEdit(_snap.ConfigJson)
            .AsReadOnly(true).WithLineNumbers(true)
            .WithSyntaxHighlighter(new JsonSyntaxHighlighter())
            .WithVerticalAlignment(VerticalAlignment.Fill).NoWrap()
            .WithVerticalScrollbar(ScrollbarVisibility.Auto).WithMargin(2, 1, 2, 0).Build();
        Modal.AddControl(viewer);
        _error = Controls.Markup().WithMargin(2, 0, 2, 0).Build(); Modal.AddControl(_error);

        var restore = Controls.Button(" Restore (r) ").Build(); restore.Click += (_, _) => _ = DoRestore();
        var cancel = Controls.Button(" Cancel (Esc) ").Build(); cancel.Click += (_, _) => CloseWithResult(false);
        Modal.AddControl(Controls.HorizontalGrid().WithAlignment(HorizontalAlignment.Center).StickyBottom()
            .Column(c => c.Add(restore)).Column(c => c.Width(2)).Column(c => c.Add(cancel)).Build());
    }

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        if (e.KeyInfo.Key == ConsoleKey.Escape) { CloseWithResult(false); e.Handled = true; return; }
        if (e.KeyInfo.Key == ConsoleKey.R) { e.Handled = true; _ = DoRestore(); }
    }

    private async Task DoRestore()
    {
        var result = await _editor.RestoreAsync(_snap);
        if (result.Success) CloseWithResult(true);
        else _error?.SetContent(new List<string> { $"[{UIConstants.Bad.ToMarkup()}]{(result.Error ?? "").Replace("[", "[[").Replace("]", "]]")}[/]" });
    }
}
