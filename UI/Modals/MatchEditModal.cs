// -----------------------------------------------------------------------
// LazyCaddy - focused editor for ONE route's host/path matcher (the grouped
// Routes view opens this with 'e' on a route row). Structurally a single-editor
// twin of SingleHandlerEditModal, hosting just a MatchEditor.
// -----------------------------------------------------------------------

using System.Text.Json;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using LazyCaddy.Configuration;
using LazyCaddy.Models;
using LazyCaddy.Services;
using LazyCaddy.UI.Editors;

namespace LazyCaddy.UI.Modals;

public sealed class MatchEditModal : ModalBase<bool>
{
    private readonly Route _route;
    private readonly EditCoordinator _editor;

    private readonly MatchEditor _matchEditor;
    private MarkupControl? _status;

    private MatchEditModal(Route route, EditCoordinator editor)
    {
        _route = route; _editor = editor;
        _matchEditor = new MatchEditor(route.ConfigPath);
    }

    public static Task<bool> ShowAsync(ConsoleWindowSystem ws, Route route, EditCoordinator editor, Window? parent = null)
        => ((ModalBase<bool>)new MatchEditModal(route, editor)).ShowAsync(ws, parent);

    protected override string GetTitle() => $" Match — {_route.HostOrMatch} ";
    protected override (int width, int height) GetSize() => (80, 16);
    protected override bool GetDefaultResult() => false;

    protected override void BuildContent()
    {
        var panel = Controls.ScrollablePanel().Build();
        _matchEditor.Build(panel, MarkDirty);
        Modal.AddControl(panel);

        _status = Controls.Markup().WithMargin(2, 0, 2, 0).StickyBottom().Build();
        Modal.AddControl(_status);

        Modal.AddControl(Controls.Toolbar()
            .AddButton(UIConstants.ActionButton("Apply", "Ctrl+S", () => RunGuarded(ApplyAsync, ShowError)))
            .AddButton(UIConstants.ActionButton("Close", "Esc", () => RunGuarded(CloseInteractiveAsync, ShowError)))
            .WithSpacing(2).WithMargin(2, 0, 2, 0).WithAboveLine().StickyBottom().Build());

        RunGuarded(LoadAsync, ShowError);
    }

    private async Task LoadAsync()
    {
        await _matchEditor.LoadAsync(_editor);
    }

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        var ctrl = (e.KeyInfo.Modifiers & ConsoleModifiers.Control) != 0;
        if (e.KeyInfo.Key == ConsoleKey.S && ctrl) { e.Handled = true; RunGuarded(ApplyAsync, ShowError); return; }
        if (e.KeyInfo.Key == ConsoleKey.Escape) { e.Handled = true; RunGuarded(CloseInteractiveAsync, ShowError); return; }
        if (_matchEditor.HandleKey(e.KeyInfo)) e.Handled = true;
    }

    private async Task CloseInteractiveAsync()
    {
        if (!_matchEditor.IsDirty) { CloseWithResult(false); return; }
        var choice = await UnsavedChangesDialog.ShowAsync(WindowSystem, Modal);
        if (choice == UnsavedChoice.Apply) { await ApplyAsync(); if (!_matchEditor.IsDirty) CloseWithResult(true); }
        else if (choice == UnsavedChoice.Discard) CloseWithResult(false);
    }

    private async Task ApplyAsync()
    {
        var writes = _matchEditor.BuildPatch();
        if (writes.Count == 0) { SetStatus($"[{UIConstants.MutedText.ToMarkup()}]No changes to apply.[/]"); return; }

        var w = writes[0];
        if (!await DiffConfirmDialog.ShowAsync(WindowSystem, $"Apply match changes for {_route.HostOrMatch}",
                Pretty(w.OldJson), Pretty(w.Json), Modal))
            return;

        var res = await _editor.ApplyBatchAsync(writes, $"edit match on {_route.HostOrMatch}");
        if (res.AllSucceeded)
        {
            SetStatus($"[{UIConstants.Good.ToMarkup()}]Applied {res.Applied} change(s).[/]");
            await _matchEditor.LoadAsync(_editor);
        }
        else
        {
            SetStatus($"[{UIConstants.Bad.ToMarkup()}]Failed: {Escape(CaddyErrorFormatter.Format(res.Error))}[/]");
        }
    }

    private static string Pretty(string json)
    {
        try { using var d = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "[]" : json); return JsonSerializer.Serialize(d.RootElement, new JsonSerializerOptions { WriteIndented = true }); }
        catch { return json; }
    }

    private void MarkDirty() { /* single editor — no tab title to update */ }

    private void ShowError(string m) => SetStatus($"[{UIConstants.Bad.ToMarkup()}]{Escape(m)}[/]");
    private void SetStatus(string markup) => _status?.SetContent(new List<string> { markup });
    private static string Escape(string s) => s.Replace("[", "[[").Replace("]", "]]");
}
