// -----------------------------------------------------------------------
// LazyCaddy - the Ctrl+K command portal: a search box + filtered command list
// overlaid on the shell. Type to fuzzy-filter, ↑↓ to move, Enter to run, Esc to
// close. Context-aware commands whose context isn't met render dimmed and can't
// be run (the status line shows why).
//
// Mirrors the author's LazyDotIde CommandPalettePortal (same PortalContentContainer
// + Prompt + ListControl pattern), adapted to LazyCaddy's Command/CommandContext
// and with disabled-row handling.
// -----------------------------------------------------------------------

using LazyCaddy.Configuration;
using LazyCaddy.Services;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Core;
using SharpConsoleUI.Drawing;
using SharpConsoleUI.Extensions;
using SharpConsoleUI.Layout;
using Rectangle = System.Drawing.Rectangle;

namespace LazyCaddy.UI;

public sealed class CommandPortal : PortalContentContainer
{
    private const int MaxWidth = 88;
    private const int MaxHeight = 22;

    private readonly CommandRegistry _registry;
    private readonly CommandContext _context;
    private readonly PromptControl _search;
    private readonly ListControl _list;
    private readonly MarkupControl _status;

    /// <summary>Raised with the chosen command (already validated enabled), or null on cancel.</summary>
    public event EventHandler<Command?>? CommandSelected;

    public CommandPortal(CommandRegistry registry, CommandContext context, int windowWidth, int windowHeight)
    {
        _registry = registry;
        _context = context;

        DismissOnOutsideClick = true;
        BorderStyle = BoxChars.Rounded;
        BorderColor = UIConstants.Accent;
        BackgroundColor = UIConstants.ContentBg;
        ForegroundColor = Color.Grey93;

        _search = Controls.Prompt()
            .WithPrompt("> ")
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithMargin(1, 0, 1, 0)
            .Build();
        AddChild(_search);

        AddChild(Controls.RuleBuilder().WithColor(UIConstants.MutedText).Build());

        _list = Controls.List()
            .WithTitle("")
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .WithColors(Color.Grey93, UIConstants.ContentBg)
            .WithFocusedColors(Color.Grey93, UIConstants.ContentBg)
            .WithHighlightColors(Color.White, UIConstants.SelectedBg)
            .WithDoubleClickActivation(true)
            .Build();
        AddChild(_list);

        AddChild(Controls.RuleBuilder().WithColor(UIConstants.MutedText).StickyBottom().Build());

        _status = Controls.Markup()
            .AddLine($"[{UIConstants.MutedText.ToMarkup()}]{_registry.All.Count} commands[/]")
            .WithAlignment(HorizontalAlignment.Left)
            .WithMargin(1, 0, 1, 0)
            .StickyBottom()
            .Build();
        AddChild(_status);

        AddChild(Controls.Markup()
            .AddLine($"[{UIConstants.MutedText.ToMarkup()}]Enter: Run  •  Esc: Close  •  ↑↓: Navigate[/]")
            .WithAlignment(HorizontalAlignment.Center)
            .StickyBottom()
            .Build());

        int w = Math.Min(MaxWidth, windowWidth - 4);
        int h = Math.Min(MaxHeight, windowHeight - 2);
        int x = (windowWidth - w) / 2;
        PortalBounds = new Rectangle(x, 1, w, h);

        // Cap visible rows to the list's actual portion: total minus border(2) minus the
        // four fixed-height children (prompt, rule, rule, status, hint = 5).
        _list.MaxVisibleItems = Math.Max(1, h - 2 - 5);

        UpdateList("");

        _search.InputChanged += (_, text) => UpdateList(text);
        _list.SelectedItemChanged += (_, _) => RefreshStatus();
        _list.ItemActivated += (_, item) => Activate(item);

        SetFocusOnFirstChild();
    }

    /// <summary>Move the window's keyboard focus to the search box (call after CreatePortal,
    /// since creating a portal doesn't reassign the window's FocusManager focus).</summary>
    public void FocusSearch()
        => _search.GetParentWindow()?.FocusManager.SetFocus(_search, FocusReason.Programmatic);

    public new bool ProcessKey(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.Escape:
                CommandSelected?.Invoke(this, null);
                return true;

            case ConsoleKey.Enter:
                // Run the highlighted command directly, whether the search box or the list has focus.
                Activate(_list.SelectedItem ?? (_list.Items.Count > 0 ? _list.Items[0] : null));
                return true;

            case ConsoleKey.DownArrow:
                MoveSelection(+1);
                return true;

            case ConsoleKey.UpArrow:
                MoveSelection(-1);
                return true;

            case ConsoleKey.PageDown:
                MoveSelection(+PageSize);
                return true;

            case ConsoleKey.PageUp:
                MoveSelection(-PageSize);
                return true;

            case ConsoleKey.Home:
                SelectIndex(0);
                return true;

            case ConsoleKey.End:
                SelectIndex(_list.Items.Count - 1);
                return true;
        }

        // Everything else (typing, backspace) goes to the focused search box.
        base.ProcessKey(key);
        return true; // swallow all keys while the portal is open
    }

    // A page = the number of rows visible in the list (falls back to a sane default).
    private int PageSize => Math.Max(1, _list.MaxVisibleItems ?? 8);

    private void MoveSelection(int delta) => SelectIndex(_list.SelectedIndex + delta);

    private void SelectIndex(int index)
    {
        int n = _list.Items.Count;
        if (n == 0) return;
        _list.SelectedIndex = Math.Clamp(index, 0, n - 1);
    }

    private void Activate(ListItem? item)
    {
        if (item?.Tag is not Command cmd) return;
        if (!cmd.IsEnabled(_context))
        {
            // Disabled: keep the portal open, surface why.
            _status.SetContent(new List<string>
            {
                $"[{UIConstants.Warn.ToMarkup()}]⚠ {Escape(cmd.GetDisabledReason(_context) ?? "unavailable here")}[/]",
            });
            return;
        }
        CommandSelected?.Invoke(this, cmd);
    }

    private void UpdateList(string query)
    {
        _list.ClearItems();
        var results = _registry.Search(query);
        foreach (var c in results)
        {
            bool enabled = c.IsEnabled(_context);
            var label = FormatRow(c, enabled);
            _list.AddItem(new ListItem(label) { Tag = c });
        }
        RefreshStatus(results.Count, query);
    }

    private string FormatRow(Command c, bool enabled)
    {
        var dim = UIConstants.MutedText.ToMarkup();
        var keys = string.IsNullOrEmpty(c.Keybinding) ? "" : c.Keybinding;
        if (enabled)
            return $"{c.Icon} [{dim}]{c.Category,-10}[/] {Escape(c.Label),-34} [{dim}]{Escape(keys)}[/]";
        // Disabled: whole row dimmed + a marker.
        return $"[{dim}]{c.Icon} {c.Category,-10} {Escape(c.Label),-34} ⃠[/]";
    }

    private void RefreshStatus() => RefreshStatus(_list.Items.Count, _search.Input);

    private void RefreshStatus(int count, string query)
    {
        var dim = UIConstants.MutedText.ToMarkup();
        // If the selected row is disabled, show its reason instead of the count.
        if (_list.SelectedItem?.Tag is Command sel && !sel.IsEnabled(_context))
        {
            _status.SetContent(new List<string>
            {
                $"[{UIConstants.Warn.ToMarkup()}]⚠ {Escape(sel.GetDisabledReason(_context) ?? "unavailable here")}[/]",
            });
            return;
        }
        var line = string.IsNullOrWhiteSpace(query)
            ? $"[{dim}]{count} commands[/]"
            : $"[{dim}]{count} of {_registry.All.Count} commands[/]";
        _status.SetContent(new List<string> { line });
    }

    private static string Escape(string s) => s.Replace("[", "[[").Replace("]", "]]");
}
