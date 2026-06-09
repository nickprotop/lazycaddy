// -----------------------------------------------------------------------
// LazyCaddy - Caddyfile → JSON: paste/edit a Caddyfile, adapt it to JSON via
// Caddy's /adapt endpoint (no change to the running config), preview the result,
// and optionally load it (POST /load, snapshotted first).
//
// /adapt is a pure converter — adapting never touches the running server. Only
// "Load adapted config" mutates, and it goes through EditCoordinator (snapshot →
// /load) with a confirmation.
// -----------------------------------------------------------------------

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

public sealed class AdaptView
{
    private readonly ConsoleWindowSystem _ws;
    private readonly EditCoordinator _editor;

    private MultilineEditControl? _input;
    private MultilineEditControl? _output;
    private ToolbarControl? _toolbar;
    private MarkupControl? _status;

    // The most recent successful adaptation's JSON, eligible to load. Null until a clean adapt.
    private string? _adaptedJson;

    public AdaptView(ConsoleWindowSystem ws, EditCoordinator editor) { _ws = ws; _editor = editor; }

    public bool TryHandleKey(ConsoleKeyInfo key)
    {
        // Ctrl+Enter adapts (works while typing in the input). Other keys fall through to the editor.
        if (key.Key == ConsoleKey.Enter && (key.Modifiers & ConsoleModifiers.Control) != 0
            && (_input?.HasFocus ?? false))
        {
            _ = AdaptAsync();
            return true;
        }
        return false;
    }

    public void Build(ScrollablePanelControl panel)
    {
        var accent = UIConstants.Accent.ToMarkup();
        var muted = UIConstants.MutedText.ToMarkup();

        panel.AddControl(Controls.Markup()
            .AddLine($"[bold {accent}]Caddyfile → JSON[/]")
            .AddLine($"[{muted}]Edit a Caddyfile, adapt it to JSON (Ctrl+Enter). Adapting never changes the running config.[/]")
            .AddEmptyLine()
            .Build());

        _toolbar = ViewToolbar.Create("adaptToolbar");
        panel.AddControl(_toolbar);

        panel.AddControl(Controls.RuleBuilder().WithTitle("Caddyfile").WithColor(UIConstants.MutedText).Build());
        _input = Controls.MultilineEdit(SampleCaddyfile)
            .WithLineNumbers(true)
            .NoWrap()
            .WithVerticalScrollbar(ScrollbarVisibility.Auto)
            .WithHorizontalScrollbar(ScrollbarVisibility.Auto)
            .WithName("adaptInput")
            .Build();
        _input.Height = 10;
        panel.AddControl(_input);

        panel.AddControl(Controls.RuleBuilder().WithTitle("Adapted JSON").WithColor(UIConstants.MutedText).Build());
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
        panel.AddControl(_output);

        _status = Controls.Markup().WithMargin(2, 0, 2, 0).StickyBottom().Build();
        panel.AddControl(_status);

        RebuildToolbar();
    }

    private void RebuildToolbar()
    {
        if (_toolbar is null) return;
        var actions = new List<ToolbarAction?>
        {
            new(ViewToolbar.Caption("⇄", "Adapt", "Ctrl+Enter"), () => _ = AdaptAsync()),
        };
        if (_adaptedJson is not null)
            actions.Add(new(ViewToolbar.Caption("⬆", "Load adapted config", "l"), () => _ = LoadAsync()));
        ViewToolbar.Rebuild(_toolbar, actions);
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
        if (!await DiffConfirmDialog.ShowAsync(_ws, "Load adapted config (replaces entire config)", current, _adaptedJson))
            return;

        var res = await _editor.LoadFullConfigAsync(_adaptedJson, "load adapted Caddyfile");
        if (res.Success)
            SetStatus($"[{UIConstants.Good.ToMarkup()}]✓ Loaded adapted config (snapshot taken first).[/]");
        else
            ShowError(res.FriendlyError);
    }

    public void Update(DashboardState state)
    {
        // Stateless view: nothing to refresh per poll. The user drives it.
    }

    private void ShowError(string m) => SetStatus($"[{UIConstants.Bad.ToMarkup()}]{Escape(m)}[/]");
    private void SetStatus(string markup) => _status?.SetContent(new List<string> { markup });
    private static string Escape(string s) => s.Replace("[", "[[").Replace("]", "]]");

    private const string SampleCaddyfile =
        "example.com {\n\treverse_proxy localhost:8080\n}\n";
}
