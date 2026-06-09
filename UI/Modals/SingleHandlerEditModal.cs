// -----------------------------------------------------------------------
// LazyCaddy - focused editor for ONE handler of a route (the grouped Routes view
// opens this on an activated handler row). Builds that handler's editor tab(s) via
// HandlerEditorFactory and applies field edits as one batch. It's the old whole-route
// modal scoped to a single handler.
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

public sealed class SingleHandlerEditModal : ModalBase<bool>
{
    private readonly Route _route;
    private readonly HandlerDescriptor _handler;
    private readonly EditCoordinator _editor;

    private readonly List<IConfigEditor> _editors = new();
    private TabControl? _tabControl;
    private MarkupControl? _status;

    private SingleHandlerEditModal(Route route, HandlerDescriptor handler, EditCoordinator editor)
    {
        _route = route; _handler = handler; _editor = editor;
    }

    public static Task<bool> ShowAsync(ConsoleWindowSystem ws, Route route, HandlerDescriptor handler,
        EditCoordinator editor, Window? parent = null)
        => ((ModalBase<bool>)new SingleHandlerEditModal(route, handler, editor)).ShowAsync(ws, parent);

    protected override string GetTitle() => $" {_handler.Type} — {_route.HostOrMatch} ";
    protected override (int width, int height) GetSize() => (96, 32);
    protected override bool GetDefaultResult() => false;

    protected override void BuildContent()
    {
        _editors.AddRange(HandlerEditorFactory.EditorsForType(_handler.Type, _handler.ConfigPath));

        var muted = UIConstants.MutedText.ToMarkup();
        if (_editors.Count == 0)
        {
            Modal.AddControl(Controls.Markup()
                .AddLine($"[{muted}]No editor for handler type '{Escape(_handler.Type)}'.[/]")
                .WithMargin(2, 1, 2, 0).Build());
        }
        else
        {
            var tabs = Controls.TabControl().Fill().WithName("handlerEditTabs");
            foreach (var ed in _editors)
            {
                var panel = Controls.ScrollablePanel().Build();
                if (ed is ReverseProxyEditor rp)
                {
                    rp.OnError = ShowError;
                    rp.DialPrompt = (title, initial) => UpstreamDialog.ShowAsync(WindowSystem, title, initial, Modal);
                    rp.RequestRelayout = () => Modal.ForceRebuildLayout();
                    rp.ConfirmRemove = what => ConfirmDeleteDialog.ShowAsync(WindowSystem, what, Modal);
                }
                ed.Build(panel, MarkDirty);
                tabs.AddTab(ed.TabTitle, panel);
            }
            tabs.WithActiveTab(0);
            _tabControl = tabs.Build();
            Modal.AddControl(_tabControl);
        }

        _status = Controls.Markup().WithMargin(2, 0, 2, 0).StickyBottom().Build();
        Modal.AddControl(_status);

        Modal.AddControl(Controls.Toolbar()
            .AddButton(UIConstants.ActionButton("Apply", "Ctrl+S", () => RunGuarded(ApplyAllAsync, ShowError)))
            .AddButton(UIConstants.ActionButton("Close", "Esc", () => RunGuarded(CloseInteractiveAsync, ShowError)))
            .WithSpacing(2).WithMargin(2, 0, 2, 0).WithAboveLine().StickyBottom().Build());

        RunGuarded(LoadAllAsync, ShowError);
    }

    private async Task LoadAllAsync()
    {
        foreach (var e in _editors) await e.LoadAsync(_editor);
        MarkDirty();
    }

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        var ctrl = (e.KeyInfo.Modifiers & ConsoleModifiers.Control) != 0;
        if (e.KeyInfo.Key == ConsoleKey.S && ctrl) { e.Handled = true; RunGuarded(ApplyAllAsync, ShowError); return; }
        if (e.KeyInfo.Key == ConsoleKey.Escape) { e.Handled = true; RunGuarded(CloseInteractiveAsync, ShowError); return; }
        if (ActiveEditor?.HandleKey(e.KeyInfo) == true) e.Handled = true;
    }

    private async Task CloseInteractiveAsync()
    {
        if (!_editors.Any(ed => ed.IsDirty)) { CloseWithResult(false); return; }
        var choice = await UnsavedChangesDialog.ShowAsync(WindowSystem, Modal);
        if (choice == UnsavedChoice.Apply) { await ApplyAllAsync(); if (!_editors.Any(ed => ed.IsDirty)) CloseWithResult(true); }
        else if (choice == UnsavedChoice.Discard) CloseWithResult(false);
    }

    private IConfigEditor? ActiveEditor
    {
        get
        {
            if (_tabControl is null || _editors.Count == 0) return null;
            var i = _tabControl.ActiveTabIndex;
            return i >= 0 && i < _editors.Count ? _editors[i] : null;
        }
    }

    private async Task ApplyAllAsync()
    {
        var writes = new List<PendingWrite>();
        foreach (var ed in _editors) writes.AddRange(ed.BuildPatch());
        if (writes.Count == 0) { SetStatus($"[{UIConstants.MutedText.ToMarkup()}]No changes to apply.[/]"); return; }

        var (oldCombined, newCombined) = BuildCombinedDiff(writes);
        if (!await DiffConfirmDialog.ShowAsync(WindowSystem, $"Apply {_handler.Type} changes", oldCombined, newCombined, Modal))
            return;

        var res = await _editor.ApplyBatchAsync(writes, $"edit {_handler.Type} on {_route.HostOrMatch}");
        if (res.AllSucceeded)
        {
            SetStatus($"[{UIConstants.Good.ToMarkup()}]Applied {res.Applied} change(s).[/]");
            foreach (var ed in _editors) await ed.LoadAsync(_editor);
            MarkDirty();
        }
        else
        {
            SetStatus($"[{UIConstants.Bad.ToMarkup()}]Applied {res.Applied} of {res.Total}; failed on {Escape(res.FailedLabel ?? "?")}: {Escape(CaddyErrorFormatter.Format(res.Error))}[/]");
        }
    }

    private static (string oldJson, string newJson) BuildCombinedDiff(IReadOnlyList<PendingWrite> writes)
    {
        var oldObj = new Dictionary<string, JsonElement>();
        var newObj = new Dictionary<string, JsonElement>();
        var seen = new Dictionary<string, int>();
        foreach (var w in writes)
        {
            var key = w.Label;
            if (seen.TryGetValue(w.Label, out var n)) { n++; seen[w.Label] = n; key = $"{w.Label} ({n})"; }
            else seen[w.Label] = 0;
            oldObj[key] = Parse(w.OldJson);
            newObj[key] = Parse(w.Json);
        }
        var opt = new JsonSerializerOptions { WriteIndented = true };
        return (JsonSerializer.Serialize(oldObj, opt), JsonSerializer.Serialize(newObj, opt));
    }

    private static JsonElement Parse(string json)
    {
        try { using var d = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json); return d.RootElement.Clone(); }
        catch { using var d = JsonDocument.Parse("{}"); return d.RootElement.Clone(); }
    }

    private void MarkDirty()
    {
        if (_tabControl is null) return;
        for (var i = 0; i < _editors.Count && i < _tabControl.TabCount; i++)
        {
            var ed = _editors[i];
            _tabControl.SetTabTitle(i, ed.IsDirty ? ed.TabTitle + " *" : ed.TabTitle);
        }
    }

    private void ShowError(string m) => SetStatus($"[{UIConstants.Bad.ToMarkup()}]{Escape(m)}[/]");
    private void SetStatus(string markup) => _status?.SetContent(new List<string> { markup });
    private static string Escape(string s) => s.Replace("[", "[[").Replace("]", "]]");
}
