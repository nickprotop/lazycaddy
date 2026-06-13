// UI/Modals/ManageServersModal.cs
using LazyCaddy.Configuration;
using LazyCaddy.Services;
using LazyCaddy.UI;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;

namespace LazyCaddy.UI.Modals;

/// <summary>Manage the configured server list: Add / Edit / Remove, persisting via ServerStore.
/// Removing the ACTIVE server is blocked (switch away first). Ephemeral (cli) rows are read-only.
/// Result = true if the list changed.</summary>
public sealed class ManageServersModal : ModalBase<bool>
{
    private readonly List<ServerEntry> _servers;
    private readonly ServerEntry _active;
    private readonly ServerStore _store;
    private TableControl? _table;
    private ToolbarControl? _toolbar;
    private MarkupControl? _status;
    private bool _changed;

    private ManageServersModal(List<ServerEntry> servers, ServerEntry active, ServerStore store)
    { _servers = servers; _active = active; _store = store; }

    public static Task<bool> ShowAsync(ConsoleWindowSystem ws, List<ServerEntry> servers,
        ServerEntry active, ServerStore store, Window? parent = null)
        => ((ModalBase<bool>)new ManageServersModal(servers, active, store)).ShowAsync(ws, parent);

    protected override string GetTitle() => " Manage servers ";
    protected override (int width, int height) GetSize() => (84, 24);
    protected override bool GetDefaultResult() => _changed;

    protected override void BuildContent()
    {
        _toolbar = ViewToolbar.Create("manageServersToolbar");
        Modal.AddControl(_toolbar);

        _table = Controls.Table()
            .AddColumn("", TextJustification.Center, 3)
            .AddColumn("Name", TextJustification.Left, 20)
            .AddColumn("Endpoint", TextJustification.Left)
            .AddColumn("RO", TextJustification.Center, 4)
            .Rounded().WithBorderColor(UIConstants.MutedText)
            .Interactive().WithVerticalScrollbar(ScrollbarVisibility.Auto)
            .WithName("manageServersTable").Build();
        _table.SelectedRowChanged += (_, _) => RebuildToolbar();
        Modal.AddControl(_table);

        _status = Controls.Markup().WithMargin(2, 0, 2, 0).StickyBottom().Build();
        Modal.AddControl(_status);

        Modal.AddControl(Controls.Toolbar()
            .AddButton(UIConstants.ActionButton("Close", "Esc", () => CloseWithResult(_changed)))
            .WithSpacing(2).WithMargin(2, 0, 2, 0).WithAboveLine().StickyBottom().Build());

        Rebuild();
        RebuildToolbar();
    }

    private void Rebuild()
    {
        if (_table is null) return;
        _table.ClearRows();
        foreach (var s in _servers)
        {
            var mark = s.Identity == _active.Identity ? "●" : "";
            var name = s.IsEphemeral ? "(cli)" : s.Name;
            _table.AddRow(new TableRow(mark, name, Host(s.Url), s.ReadOnly ? "yes" : "") { Tag = s });
        }
    }

    private void RebuildToolbar()
    {
        if (_toolbar is null) return;
        var sel = _table?.SelectedRow?.Tag as ServerEntry;
        bool isEphemeral = sel?.IsEphemeral ?? false;
        bool isActive = sel is not null && sel.Identity == _active.Identity;
        var actions = new List<ToolbarAction?>
        {
            new(ViewToolbar.Caption("⊕", "Add", "a"), () => RunGuarded(AddAsync, ShowError)),
        };
        if (sel is not null && !isEphemeral)
            actions.Add(new(ViewToolbar.Caption("✎", "Edit", "e"), () => RunGuarded(EditAsync, ShowError)));
        if (sel is not null && !isEphemeral && !isActive)
            actions.Add(new(ViewToolbar.Caption("✕", "Remove", "d"), () => RunGuarded(RemoveAsync, ShowError)));
        ViewToolbar.Rebuild(_toolbar, actions);
        SetStatus(isActive ? $"[{UIConstants.MutedText.ToMarkup()}]Active server can't be removed — switch away first.[/]" : "");
    }

    private async Task AddAsync()
    {
        var entry = await EditServerModal.ShowAsync(WindowSystem, null, _servers, Modal);
        if (entry is null) return;
        _servers.Add(entry); Persist();
    }

    private async Task EditAsync()
    {
        if (_table?.SelectedRow?.Tag is not ServerEntry sel || sel.IsEphemeral) return;
        var edited = await EditServerModal.ShowAsync(WindowSystem, sel, _servers, Modal);
        if (edited is null) return;
        int i = _servers.FindIndex(s => s.Identity == sel.Identity);
        if (i >= 0) _servers[i] = edited;
        Persist();
    }

    private async Task RemoveAsync()
    {
        if (_table?.SelectedRow?.Tag is not ServerEntry sel || sel.IsEphemeral) return;
        if (sel.Identity == _active.Identity) { ShowError("Active server can't be removed — switch away first."); return; }
        if (!await ConfirmDeleteDialog.ShowAsync(WindowSystem, $"server {sel.Name}")) return;
        _servers.RemoveAll(s => s.Identity == sel.Identity); Persist();
    }

    private void Persist()
    {
        _store.Save(_servers);
        _changed = true;
        Rebuild();
        RebuildToolbar();
    }

    // Hotkeys a/e/d when the table has focus.
    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        if (_table?.HasFocus == true)
        {
            if (e.KeyInfo.Key == ConsoleKey.A) { e.Handled = true; RunGuarded(AddAsync, ShowError); return; }
            if (e.KeyInfo.Key == ConsoleKey.E) { e.Handled = true; RunGuarded(EditAsync, ShowError); return; }
            if (e.KeyInfo.Key == ConsoleKey.D) { e.Handled = true; RunGuarded(RemoveAsync, ShowError); return; }
        }
        base.OnKeyPressed(sender, e);
    }

    private static string Host(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var u) ? $"{u.Host}:{u.Port}" : url;
    private void ShowError(string m) => SetStatus($"[{UIConstants.Bad.ToMarkup()}]{Escape(m)}[/]");
    private void SetStatus(string m) => _status?.SetContent(new List<string> { m });
    private static string Escape(string s) => s.Replace("[", "[[").Replace("]", "]]");
}
