// -----------------------------------------------------------------------
// LazyCaddy - the Ctrl+L server portal: a favorites-style picker of configured
// servers (active one marked), plus Add / Manage actions. Anchored under the nav
// server button. ↑↓ to move, Enter to pick, Esc to close.
//
// Uses the same ListControl-in-a-PortalContentContainer pattern as CommandPortal
// (which is the proven, working approach for key+mouse activation in a portal).
// -----------------------------------------------------------------------

using LazyCaddy.Configuration;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Core;
using SharpConsoleUI.Drawing;
using SharpConsoleUI.Extensions;
using SharpConsoleUI.Layout;
using Rectangle = System.Drawing.Rectangle;

namespace LazyCaddy.UI;

public sealed class ServerPortal : PortalContentContainer
{
    // Row tags: a ServerEntry for a server row, or one of these sentinels for the actions.
    private const string AddTag = "__add__";
    private const string ManageTag = "__manage__";

    private readonly ListControl _list;

    public event EventHandler<ServerEntry>? ServerSelected;
    public event EventHandler? AddRequested;
    public event EventHandler? ManageRequested;
    /// <summary>Raised on Esc (the base DismissRequested is internal-only to raise).</summary>
    public event EventHandler? CancelRequested;

    public ServerPortal(IReadOnlyList<ServerEntry> servers, ServerEntry active, int windowWidth, int windowHeight)
    {
        DismissOnOutsideClick = true;
        BorderStyle = BoxChars.Rounded;
        BorderColor = UIConstants.Accent;
        BackgroundColor = UIConstants.ContentBg;
        ForegroundColor = UIConstants.PrimaryText;

        _list = Controls.List()
            .WithTitle("")
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .WithColors(UIConstants.PrimaryText, UIConstants.ContentBg)
            .WithFocusedColors(UIConstants.PrimaryText, UIConstants.ContentBg)
            .WithHighlightColors(Color.White, UIConstants.SelectedBg)
            .WithDoubleClickActivation(true)
            .Build();

        var muted = UIConstants.MutedText.ToMarkup();
        foreach (var s in servers)
        {
            var marker = s.Identity == active.Identity ? "●" : " ";
            var name = s.IsEphemeral ? "(cli)" : s.Name;
            _list.AddItem(new ListItem($"{marker} {name}  [{muted}]{Host(s.Url)}[/]") { Tag = s });
        }
        _list.AddItem(new ListItem($"[{muted}]──────────────[/]") { Tag = "__sep__" });
        _list.AddItem(new ListItem("+ Add server…") { Tag = AddTag });
        _list.AddItem(new ListItem("✎ Manage servers…") { Tag = ManageTag });
        AddChild(_list);

        AddChild(Controls.RuleBuilder().WithColor(UIConstants.MutedText).StickyBottom().Build());
        AddChild(Controls.Markup()
            .AddLine($"[{muted}]Enter: select  •  Esc: close  •  ↑↓: navigate[/]")
            .WithAlignment(HorizontalAlignment.Center)
            .StickyBottom()
            .Build());

        // List rows = servers + separator + Add + Manage. Plus the rule + hint footer (2) and the
        // box border (2). Size the portal to fit all of it (capped to the window), and let the list
        // show every row so nothing is clipped or needs scrolling.
        int listRows = servers.Count + 3;
        int w = Math.Min(52, windowWidth - 4);
        int h = Math.Min(listRows + 2 + 2, windowHeight - 2);
        PortalBounds = new Rectangle(Math.Max(1, windowWidth - w - 2), 1, w, h);
        _list.MaxVisibleItems = Math.Max(1, h - 2 - 2);

        _list.ItemActivated += (_, item) => Activate(item);
        PortalFocusedControl = _list;
        SetFocusOnFirstChild();
    }

    /// <summary>Move the window's keyboard focus to the list (call after CreatePortal).</summary>
    public void FocusList()
        => _list.GetParentWindow()?.FocusManager.SetFocus(_list, FocusReason.Programmatic);

    public new bool ProcessKey(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.Escape:
                CancelRequested?.Invoke(this, EventArgs.Empty);
                return true;
            case ConsoleKey.Enter:
                Activate(_list.SelectedItem);
                return true;
            case ConsoleKey.DownArrow:
                Move(+1);
                return true;
            case ConsoleKey.UpArrow:
                Move(-1);
                return true;
            case ConsoleKey.Home:
                Select(0);
                return true;
            case ConsoleKey.End:
                Select(_list.Items.Count - 1);
                return true;
        }
        base.ProcessKey(key);
        return true; // swallow keys while open
    }

    // Skip the non-selectable separator row when moving.
    private void Move(int delta)
    {
        int n = _list.Items.Count;
        if (n == 0) return;
        int i = _list.SelectedIndex;
        for (int step = 0; step < n; step++)
        {
            i = Math.Clamp(i + delta, 0, n - 1);
            if (_list.Items[i].Tag is not "__sep__") { _list.SelectedIndex = i; return; }
            if (i == 0 || i == n - 1) break; // hit an edge on a separator → stop
        }
    }

    private void Select(int index)
    {
        int n = _list.Items.Count;
        if (n == 0) return;
        _list.SelectedIndex = Math.Clamp(index, 0, n - 1);
    }

    private void Activate(ListItem? item)
    {
        switch (item?.Tag)
        {
            case ServerEntry s: ServerSelected?.Invoke(this, s); break;
            case AddTag: AddRequested?.Invoke(this, EventArgs.Empty); break;
            case ManageTag: ManageRequested?.Invoke(this, EventArgs.Empty); break;
            // separator / null → ignore
        }
    }

    private static string Host(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var u) ? $"{u.Host}:{u.Port}" : url;
}
