// -----------------------------------------------------------------------
// LazyCaddy - Raw Config: read-only, line-numbered, JSON-highlighted view of
// the running config, with the control's built-in find/replace.
// -----------------------------------------------------------------------

using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Extensions;
using SharpConsoleUI.Layout;
using LazyCaddy.Configuration;
using LazyCaddy.Dashboard;
using LazyCaddy.Services;
using LazyCaddy.UI;
using LazyCaddy.UI.Modals;

namespace LazyCaddy.Views;

public sealed class RawConfigView : ICommandProvider
{
    private MultilineEditControl? _editor;
    private string _lastContent = string.Empty;

    private readonly ConsoleWindowSystem _ws;
    private readonly EditCoordinator _coordinator;
    private MarkupControl? _status;
    private ToolbarControl? _toolbar;

    public RawConfigView(ConsoleWindowSystem ws, EditCoordinator coordinator) { _ws = ws; _coordinator = coordinator; }

    public object? SelectedTag => null;

    public IEnumerable<Command> GetCommands()
    {
        const int idx = 5;
        bool onView(CommandContext c) => c.CurrentViewIndex == idx;

        yield return new Command
        {
            Id = "rawconfig.find", Label = "Find in raw config", Category = "Raw Config", Icon = "🔍",
            Keybinding = "Ctrl+F", Priority = 58,
            CanExecute = onView, DisabledReason = _ => "go to Raw Config",
            Execute = _ => OpenFind(),
        };
        yield return new Command
        {
            Id = "rawconfig.edit", Label = "Edit raw config", Category = "Raw Config", Icon = "✎",
            Keybinding = "i", Priority = 57,
            CanExecute = onView, DisabledReason = _ => "go to Raw Config",
            Execute = _ => EnterEdit(),
        };
    }

    public void Build(ScrollablePanelControl panel)
    {
        var accent = UIConstants.Accent.ToMarkup();
        var muted = UIConstants.MutedText.ToMarkup();

        panel.AddControl(Controls.Markup()
            .AddLine($"[bold {accent}]Raw Config[/]")
            .AddLine($"[{muted}]Running config (JSON).[/]")
            .AddEmptyLine()
            .Build());

        _toolbar = ViewToolbar.Create("rawConfigToolbar");
        panel.AddControl(_toolbar);
        RebuildToolbar();

        _editor = Controls.MultilineEdit(string.Empty)
            .AsReadOnly(true)
            .WithLineNumbers(true)
            .WithSyntaxHighlighter(new JsonSyntaxHighlighter())
            // Fill the available vertical space rather than capping at a fixed
            // viewport height (the control expands to its layout bounds when Fill).
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .NoWrap()
            .WithVerticalScrollbar(ScrollbarVisibility.Auto)
            .WithHorizontalScrollbar(ScrollbarVisibility.Auto)
            .WithName("rawConfigEditor")
            .Build();
        panel.AddControl(_editor);

        _status = Controls.Markup().WithMargin(2, 0, 2, 0).StickyBottom().Build();
        panel.AddControl(_status);

        // The NavigationView rebuilds this view each time it's reopened, producing a
        // fresh empty editor. Reset the change-tracking sentinel so the next Update
        // repopulates it (otherwise the unchanged-content guard leaves it blank).
        _lastContent = string.Empty;
    }

    /// <summary>Focus the config editor so find/scroll/edit keys work immediately on view entry.</summary>
    public void FocusPrimary() => _editor?.RequestFocus();

    /// <summary>Edit-mode hotkeys, active only when the config editor has focus.
    /// i: enter edit mode · Ctrl+S: apply via /load · Esc: cancel edit. Returns true if consumed.</summary>
    public bool TryHandleKey(ConsoleKeyInfo key)
    {
        if (_editor is null || !_editor.HasFocus) return false;

        // Find (read-only search).
        if (key.Key == ConsoleKey.F && (key.Modifiers & ConsoleModifiers.Control) != 0)
        {
            OpenFind();
            return true;
        }

        // Enter edit mode.
        if (key.Key == ConsoleKey.I && _editor.ReadOnly)
        {
            EnterEdit();
            return true;
        }

        // Cancel edit mode: revert to last-known config, go read-only.
        if (key.Key == ConsoleKey.Escape && !_editor.ReadOnly)
        {
            CancelEdit();
            return true;
        }

        // Apply via /load.
        if (key.Key == ConsoleKey.S && (key.Modifiers & ConsoleModifiers.Control) != 0 && !_editor.ReadOnly)
        {
            ApplyEdit();
            return true;
        }

        // Adapt a Caddyfile to JSON (read-only mode only; the editable buffer owns 'a' otherwise).
        if (key.Key == ConsoleKey.A && _editor.ReadOnly)
        {
            OpenAdapt();
            return true;
        }

        return false;
    }

    // ── Shared handlers (invoked by both keys and toolbar buttons) ──

    private void OpenFind()
    {
        if (_editor is null) return;
        _ = FindDialog.ShowAsync(_ws, _editor);
    }

    private void OpenAdapt() => _ = AdaptCaddyfileModal.ShowAsync(_ws, _coordinator);

    private void EnterEdit()
    {
        if (_editor is null || !_editor.ReadOnly) return;
        _editor.ReadOnly = false;
        SetStatus($"[{UIConstants.Accent.ToMarkup()}]editing — Ctrl+S to apply, Esc to cancel[/]");
        RebuildToolbar();
    }

    private void CancelEdit()
    {
        if (_editor is null || _editor.ReadOnly) return;
        _editor.SetContent(_lastContent);
        _editor.ReadOnly = true;
        SetStatus("");
        RebuildToolbar();
    }

    private void ApplyEdit()
    {
        if (_editor is null || _editor.ReadOnly) return;
        _ = ApplyRawAsync();
    }

    private void RebuildToolbar()
    {
        if (_toolbar is null) return;
        ViewToolbar.Rebuild(_toolbar, new ToolbarAction?[]
        {
            new(ViewToolbar.Caption("🔍", "Find", "^F"), OpenFind),
            new(ViewToolbar.Caption("✎", "Edit", "i"), EnterEdit),
            new(ViewToolbar.Caption("✔", "Apply", "^S"), ApplyEdit),
            new(ViewToolbar.Caption("↩", "Cancel", "Esc"), CancelEdit),
            new(ViewToolbar.Caption("⇄", "Adapt Caddyfile", "a"), OpenAdapt),
        });
    }

    private async Task ApplyRawAsync()
    {
        if (_editor is null) return;
        var newCfg = _editor.GetContent();
        var oldCfg = _lastContent;
        if (!await DiffConfirmDialog.ShowAsync(_ws, "Apply raw config (/load)", oldCfg, newCfg))
            return;

        var result = await _coordinator.LoadFullConfigAsync(newCfg, "raw config edit (/load)");
        _editor.ReadOnly = true;
        RebuildToolbar();
        if (result.Success)
        {
            _lastContent = newCfg;
            SetStatus($"[{UIConstants.MutedText.ToMarkup()}]applied[/]");
        }
        else
        {
            SetStatus($"[{UIConstants.Bad.ToMarkup()}]{result.FriendlyError.Replace("[", "[[").Replace("]", "]]")}[/]");
        }
    }

    private void SetStatus(string markup) => _status?.SetContent(new List<string> { markup });

    public void Update(DashboardState state)
    {
        var snap = state.Snapshot;
        if (snap is null || _editor is null) return;
        if (!_editor.ReadOnly) return; // don't overwrite an in-progress edit
        if (snap.RawConfigJson == _lastContent) return;
        _lastContent = snap.RawConfigJson;
        _editor.SetContent(snap.RawConfigJson);
    }
}
