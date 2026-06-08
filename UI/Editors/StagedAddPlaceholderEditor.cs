// -----------------------------------------------------------------------
// LazyCaddy - placeholder tab for a STAGED-ADD handler in the route edit modal.
//
// A handler added via Ctrl+N doesn't exist as a config node yet, so it can't be
// edited like a real handler (its sub-node editors would write fragments to a
// non-existent node). Instead its tab is read-only: on Apply the modal POSTs a
// minimal valid handler (NewRouteSkeleton.MinimalHandler), then re-parses — the
// real handler and its full sub-setting tabs appear, and configuration happens
// there. This keeps the staged add to a single, valid whole-handler POST.
// -----------------------------------------------------------------------

using System;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using LazyCaddy.Configuration;
using LazyCaddy.Services;

namespace LazyCaddy.UI.Editors;

public sealed class StagedAddPlaceholderEditor : IConfigEditor
{
    private readonly string _type;

    public StagedAddPlaceholderEditor(string type) => _type = type;

    public string TabTitle => $"+ {_type}";
    public string ConfigPath => $"(staged:{_type})"; // never used for a write — the modal POSTs MinimalHandler

    public void Build(ScrollablePanelControl container, Action onDirtyChanged)
    {
        var muted = UIConstants.MutedText.ToMarkup();
        var accent = UIConstants.Accent.ToMarkup();
        container.AddControl(Controls.Markup()
            .AddLine($"[{accent}]New {_type} handler (staged)[/]")
            .AddEmptyLine()
            .AddLine($"[{muted}]Apply (Ctrl+S) creates this handler with default settings.[/]")
            .AddLine($"[{muted}]Its configuration tabs appear after applying — edit it there.[/]")
            .AddLine($"[{muted}]Ctrl+D removes this staged handler before applying.[/]")
            .WithMargin(2, 1, 2, 0).Build());
    }

    public Task LoadAsync(EditCoordinator editor) => Task.CompletedTask; // no node to load
    public bool IsDirty => false;                                        // dirtiness is tracked by the modal's staged-add list
    public IReadOnlyList<PendingWrite> BuildPatch() => System.Array.Empty<PendingWrite>();
    public void Revert() { }
    public bool HandleKey(ConsoleKeyInfo key) => false;
}
