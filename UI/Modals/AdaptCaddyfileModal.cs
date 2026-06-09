// -----------------------------------------------------------------------
// LazyCaddy - Caddyfile → JSON modal: paste/edit a Caddyfile, adapt it to JSON via
// Caddy's /adapt endpoint (no change to the running config), preview the result,
// and optionally load it (POST /load, snapshotted first).
//
// /adapt is a pure converter — adapting never touches the running server. Only
// "Load adapted config" mutates, and it goes through EditCoordinator (snapshot →
// /load) with a confirmation. Launched from the Raw Config view.
// -----------------------------------------------------------------------

using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;
using LazyCaddy.Configuration;
using LazyCaddy.Services;

namespace LazyCaddy.UI.Modals;

public sealed class AdaptCaddyfileModal : ModalBase<bool>
{
    private readonly EditCoordinator _editor;

    private MultilineEditControl? _input;
    private MultilineEditControl? _output;
    private ToolbarControl? _toolbar;
    private MarkupControl? _status;

    // The most recent successful adaptation's JSON, eligible to load. Null until a clean adapt.
    private string? _adaptedJson;

    // Whether a Load succeeded — surfaced as the modal result so the caller can refresh.
    private bool _loaded;

    private AdaptCaddyfileModal(EditCoordinator editor) { _editor = editor; }

    public static Task<bool> ShowAsync(ConsoleWindowSystem ws, EditCoordinator editor, Window? parent = null)
        => ((ModalBase<bool>)new AdaptCaddyfileModal(editor)).ShowAsync(ws, parent);

    protected override string GetTitle() => " Caddyfile → JSON ";
    protected override (int width, int height) GetSize() => (110, 36);
    protected override bool GetDefaultResult() => false;

    protected override void BuildContent()
    {
        var accent = UIConstants.Accent.ToMarkup();
        var muted = UIConstants.MutedText.ToMarkup();

        Modal.AddControl(Controls.Markup()
            .AddLine($"[bold {accent}]Caddyfile → JSON[/]")
            .AddLine($"[{muted}]Edit a Caddyfile, adapt it to JSON (Ctrl+Enter). Adapting never changes the running config.[/]")
            .WithMargin(2, 1, 2, 0)
            .Build());

        // Top toolbar: Adapt always; Load shown only after a successful adapt (RebuildToolbar).
        _toolbar = ViewToolbar.Create("adaptToolbar");
        Modal.AddControl(_toolbar);

        Modal.AddControl(Controls.RuleBuilder().WithTitle("Caddyfile").WithColor(UIConstants.MutedText).Build());
        _input = Controls.MultilineEdit(SampleCaddyfile)
            .WithLineNumbers(true)
            .NoWrap()
            .WithVerticalScrollbar(ScrollbarVisibility.Auto)
            .WithHorizontalScrollbar(ScrollbarVisibility.Auto)
            .WithName("adaptInput")
            .Build();
        _input.Height = 10;
        Modal.AddControl(_input);

        Modal.AddControl(Controls.RuleBuilder().WithTitle("Adapted JSON").WithColor(UIConstants.MutedText).Build());
        _output = Controls.MultilineEdit(string.Empty)
            .AsReadOnly(true)
            .WithLineNumbers(true)
            .WithSyntaxHighlighter(new JsonSyntaxHighlighter())
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .NoWrap()
            .WithVerticalScrollbar(ScrollbarVisibility.Auto)
            .WithHorizontalScrollbar(ScrollbarVisibility.Auto)
            .WithName("adaptOutput")
            .Build();
        Modal.AddControl(_output);

        _status = Controls.Markup().WithMargin(2, 0, 2, 0).StickyBottom().Build();
        Modal.AddControl(_status);

        // Sticky bottom: Close. (Adapt/Load live on the top toolbar.)
        Modal.AddControl(Controls.Toolbar()
            .AddButton(UIConstants.ActionButton("Close", "Esc", () => CloseWithResult(_loaded)))
            .WithSpacing(2).WithMargin(2, 0, 2, 0).WithAboveLine().StickyBottom().Build());

        RebuildToolbar();
    }

    private void RebuildToolbar()
    {
        if (_toolbar is null) return;
        var actions = new List<ToolbarAction?>
        {
            new(ViewToolbar.Caption("⇄", "Adapt", "Ctrl+Enter"), () => RunGuarded(AdaptAsync, ShowError)),
        };
        if (_adaptedJson is not null)
            actions.Add(new(ViewToolbar.Caption("⬆", "Load adapted config", "l"), () => RunGuarded(LoadAsync, ShowError)));
        ViewToolbar.Rebuild(_toolbar, actions);
    }

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        var ctrl = (e.KeyInfo.Modifiers & ConsoleModifiers.Control) != 0;
        if (e.KeyInfo.Key == ConsoleKey.Enter && ctrl) { e.Handled = true; RunGuarded(AdaptAsync, ShowError); return; }
        if (e.KeyInfo.Key == ConsoleKey.Escape) { e.Handled = true; CloseWithResult(_loaded); return; }
        // 'l' loads — but only when not typing in the editable input.
        if (e.KeyInfo.Key == ConsoleKey.L && !ctrl && !(_input?.HasFocus ?? false) && _adaptedJson is not null)
        {
            e.Handled = true; RunGuarded(LoadAsync, ShowError); return;
        }
    }

    private async Task AdaptAsync()
    {
        var caddyfile = _input?.Content ?? "";
        if (string.IsNullOrWhiteSpace(caddyfile)) { SetStatus($"[{UIConstants.MutedText.ToMarkup()}]Nothing to adapt.[/]"); return; }

        AdaptResult r;
        try { r = await _editor.AdaptCaddyfileAsync(caddyfile); }
        catch (Exception ex) { ShowError(ex.Message); return; }

        if (!r.Success)
        {
            _adaptedJson = null;
            _output?.SetContent("");
            ShowError(r.Error ?? "Adapt failed.");
            RebuildToolbar();
            return;
        }

        _adaptedJson = r.ResultJson;
        _output?.SetContent(r.ResultJson ?? "");
        var muted = UIConstants.MutedText.ToMarkup();
        if (r.Warnings.Count > 0)
        {
            var w = r.Warnings[0];
            var more = r.Warnings.Count > 1 ? $" (+{r.Warnings.Count - 1} more)" : "";
            SetStatus($"[{UIConstants.Warn.ToMarkup()}]⚠[/] [{muted}]{Escape(w.Message)} ({Escape(w.File)}:{w.Line}){Escape(more)}[/]");
        }
        else
        {
            SetStatus($"[{UIConstants.Good.ToMarkup()}]✓[/] [{muted}]Adapted OK. Review the JSON, then 'Load adapted config' to apply.[/]");
        }
        RebuildToolbar();
    }

    private async Task LoadAsync()
    {
        if (_adaptedJson is null) return;
        if (_editor.ReadOnly) { ShowError("Editing is disabled (read-only mode)."); return; }

        // Show the diff of current-vs-adapted before replacing the whole config.
        string current;
        try { current = await _editor.GetRawConfigAsync(); }
        catch { current = "{}"; }
        if (!await DiffConfirmDialog.ShowAsync(WindowSystem, "Load adapted config (replaces entire config)", current, _adaptedJson, Modal))
            return;

        var res = await _editor.LoadFullConfigAsync(_adaptedJson, "load adapted Caddyfile");
        if (res.Success)
        {
            _loaded = true;
            SetStatus($"[{UIConstants.Good.ToMarkup()}]✓ Loaded adapted config (snapshot taken first).[/]");
        }
        else
            ShowError(res.FriendlyError);
    }

    private void ShowError(string m) => SetStatus($"[{UIConstants.Bad.ToMarkup()}]{Escape(m)}[/]");
    private void SetStatus(string markup) => _status?.SetContent(new List<string> { markup });
    private static string Escape(string s) => s.Replace("[", "[[").Replace("]", "]]");

    private const string SampleCaddyfile =
        "example.com {\n\treverse_proxy localhost:8080\n}\n";
}
