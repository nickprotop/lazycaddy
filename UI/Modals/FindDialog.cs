// -----------------------------------------------------------------------
// LazyCaddy - find dialog for the read-only Raw Config view. Drives the
// MultilineEditControl's built-in search (Find/FindNext/FindPrevious). Find-only
// (Raw Config is read-only) — no replace. Modelled on lazydotide's
// FindReplaceDialog but trimmed to find. Clears the editor's search on close.
// -----------------------------------------------------------------------

using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;
using LazyCaddy.Configuration;

namespace LazyCaddy.UI.Modals;

public sealed class FindDialog : ModalBase<bool>
{
    private readonly MultilineEditControl _editor;
    private PromptControl? _find;
    private CheckboxControl? _caseBox;
    private MarkupControl? _status;

    private FindDialog(MultilineEditControl editor) { _editor = editor; }

    public static Task<bool> ShowAsync(ConsoleWindowSystem ws, MultilineEditControl editor, Window? parent = null)
        => ((ModalBase<bool>)new FindDialog(editor)).ShowAsync(ws, parent);

    protected override string GetTitle() => " Find ";
    protected override (int width, int height) GetSize() => (60, 12);
    protected override bool GetDefaultResult() => false;

    protected override void BuildContent()
    {
        var muted = UIConstants.MutedText.ToMarkup();

        _find = Controls.Prompt("Find: ").WithInputWidth(44).Build();
        _caseBox = new CheckboxControl { Label = "Case-sensitive", Checked = false };
        _status = Controls.Markup().WithMargin(2, 1, 2, 0).Build();

        Modal.AddControl(_find);
        Modal.AddControl(_caseBox);
        Modal.AddControl(_status);

        var findNext = Controls.Button().WithText("Find Next").OnClick((_, _) => DoFindNext()).Build();
        var findPrev = Controls.Button().WithText("Find Prev").OnClick((_, _) => DoFindPrev()).Build();
        var close = Controls.Button().WithText("Close").OnClick((_, _) => CloseWithResult(false)).Build();

        var row = Controls.HorizontalGrid()
            .WithAlignment(HorizontalAlignment.Center)
            .StickyBottom()
            .Column(col => col.Add(findNext))
            .Column(col => col.Width(2))
            .Column(col => col.Add(findPrev))
            .Column(col => col.Width(2))
            .Column(col => col.Add(close))
            .Build();
        Modal.AddControl(row);

        Modal.AddControl(Controls.Markup()
            .AddLine($"[{muted}]Enter: find next   Esc: close[/]")
            .WithMargin(2, 0, 2, 0).StickyBottom().Build());
    }

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        if (e.KeyInfo.Key == ConsoleKey.Escape) { CloseWithResult(false); e.Handled = true; return; }
        if (e.KeyInfo.Key == ConsoleKey.Enter) { DoFindNext(); e.Handled = true; }
    }

    private void DoFindNext()
    {
        var term = _find?.Input ?? "";
        if (string.IsNullOrEmpty(term)) { SetStatus(""); return; }
        if (_editor.SearchTerm != term) _editor.Find(term, _caseBox?.Checked ?? false);
        else _editor.FindNext();
        ShowMatchStatus();
    }

    private void DoFindPrev()
    {
        var term = _find?.Input ?? "";
        if (string.IsNullOrEmpty(term)) { SetStatus(""); return; }
        if (_editor.SearchTerm != term) _editor.Find(term, _caseBox?.Checked ?? false);
        else _editor.FindPrevious();
        ShowMatchStatus();
    }

    private void ShowMatchStatus()
        => SetStatus(_editor.HasActiveSearch
            ? $"Match {_editor.CurrentMatchIndex + 1} of {_editor.MatchCount}"
            : $"[{UIConstants.Bad.ToMarkup()}]Not found[/]");

    private void SetStatus(string markup) => _status?.SetContent(new List<string> { markup });

    protected override void OnCleanup() => _editor.ClearFind();
}
