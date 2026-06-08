using System.Text.Json;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using LazyCaddy.Configuration;
using LazyCaddy.Services;

namespace LazyCaddy.UI.Modals;

/// <summary>
/// Edits one log under <c>logging/logs/{name}</c>: level, include/exclude filters, and writer.
/// Known writer outputs (file/stdout/stderr/discard) are formed; an unknown-module writer is
/// preserved verbatim via <see cref="HandlerPatch.MergeUnmanaged"/> when the output is left "(unchanged)".
/// </summary>
public sealed class LogOutputDialog : ModalBase<bool>
{
    private readonly string _logName;
    private readonly string _path;          // = "logging/logs/{name}"
    private readonly EditCoordinator _editor;
    private PromptControl? _level, _include, _exclude, _filename;
    private DropdownControl? _output;
    private MarkupControl? _error;
    private string _original = "{}";

    private static readonly string[] Outputs = { "(unchanged)", "stdout", "stderr", "discard", "file" };
    private static readonly HashSet<string> KnownOutputs = new(StringComparer.Ordinal) { "stdout", "stderr", "discard", "file" };

    // Managed keys for the log node. When a known output is chosen, `writer` is managed (formed);
    // when "(unchanged)" is chosen, `writer` is dropped from the set so MergeUnmanaged preserves
    // the original writer verbatim (including unknown-module writers).
    private static readonly IReadOnlySet<string> ManagedLogKeysWithWriter =
        new HashSet<string>(StringComparer.Ordinal) { "level", "include", "exclude", "writer" };
    private static readonly IReadOnlySet<string> ManagedLogKeysNoWriter =
        new HashSet<string>(StringComparer.Ordinal) { "level", "include", "exclude" };

    private LogOutputDialog(string logName, EditCoordinator editor)
    {
        _logName = logName;
        _path = $"logging/logs/{logName}";
        _editor = editor;
    }

    public static Task<bool> ShowAsync(ConsoleWindowSystem ws, string logName, EditCoordinator editor, Window? parent = null)
        => ((ModalBase<bool>)new LogOutputDialog(logName, editor)).ShowAsync(ws, parent);

    protected override string GetTitle() => $" Edit log {_logName} ";
    protected override (int width, int height) GetSize() => (78, 16);
    protected override bool GetDefaultResult() => false;

    protected override void BuildContent()
    {
        var muted = UIConstants.MutedText.ToMarkup();
        Modal.AddControl(Controls.Markup()
            .AddLine($"[{muted}]Level: INFO/DEBUG/WARN/ERROR. Include/exclude: comma-separated logger names. Filename used only for output=file.[/]")
            .WithMargin(2, 1, 2, 0).Build());
        _level   = Controls.Prompt("Level:    ").WithInputWidth(20).Build();
        _include = Controls.Prompt("Include:  ").WithInputWidth(54).Build();
        _exclude = Controls.Prompt("Exclude:  ").WithInputWidth(54).Build();
        _output  = Controls.Dropdown("Writer:   ").AddItems(Outputs).Build();
        _filename = Controls.Prompt("Filename: ").WithInputWidth(54).Build();
        Modal.AddControl(_level); Modal.AddControl(_include); Modal.AddControl(_exclude);
        Modal.AddControl(_output); Modal.AddControl(_filename);
        _error = Controls.Markup().WithMargin(2, 1, 2, 0).Build(); Modal.AddControl(_error);
        Modal.AddControl(Controls.Markup().AddLine($"[{muted}]Enter: apply   Esc: cancel[/]").WithMargin(2, 0, 2, 0).StickyBottom().Build());
        RunGuarded(LoadAsync, Err);
    }

    private async Task LoadAsync()
    {
        try
        {
            _original = await _editor.GetConfigNodeAsync(_path);
            using var d = JsonDocument.Parse(_original); var r = d.RootElement;
            if (r.ValueKind != JsonValueKind.Object) return;
            if (r.TryGetProperty("level", out var lv) && lv.ValueKind == JsonValueKind.String) _level!.SetInput(lv.GetString());
            Arr(_include, r, "include");
            Arr(_exclude, r, "exclude");
            if (r.TryGetProperty("writer", out var w) && w.ValueKind == JsonValueKind.Object
                && w.TryGetProperty("output", out var ov) && ov.ValueKind == JsonValueKind.String)
            {
                var output = ov.GetString() ?? "";
                if (KnownOutputs.Contains(output))
                {
                    var idx = System.Array.IndexOf(Outputs, output);
                    if (idx > 0 && _output is not null) _output.SelectedIndex = idx;
                    if (output == "file" && w.TryGetProperty("filename", out var fn) && fn.ValueKind == JsonValueKind.String)
                        _filename!.SetInput(fn.GetString());
                }
                else
                {
                    // Unknown polymorphic writer module: leave dropdown at "(unchanged)" so applying
                    // preserves it verbatim through the merge.
                    Err($"writer is a '{output}' module — leave output at (unchanged) and the existing writer is preserved.");
                }
            }
        }
        catch (JsonException ex) { Err($"Could not parse log node: {ex.Message}"); }
        catch { }
    }

    private static void Arr(PromptControl? c, JsonElement r, string k)
    {
        if (c is not null && r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Array)
            c.SetInput(string.Join(", ", v.EnumerateArray().Select(e => e.ToString())));
    }

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        if (e.KeyInfo.Key == ConsoleKey.Escape) { CloseWithResult(false); e.Handled = true; return; }
        if (e.KeyInfo.Key == ConsoleKey.Enter) { e.Handled = true; RunGuarded(ApplyAsync, Err); }
    }

    private static string[] Csv(PromptControl? c) =>
        (c?.Input ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private async Task ApplyAsync()
    {
        var level = (_level?.Input ?? "").Trim();
        var include = Csv(_include);
        var exclude = Csv(_exclude);
        var output = _output?.SelectedValue ?? "(unchanged)";

        // When a known output is chosen, form the writer and manage the `writer` key.
        // When "(unchanged)", emit no writer and exclude `writer` from the managed set so the
        // original writer (known or unknown-module) is carried forward verbatim by the merge.
        string writerJson;
        IReadOnlySet<string> managedKeys;
        if (KnownOutputs.Contains(output))
        {
            writerJson = ServerConfigPatch.LogWriter(output, (_filename?.Input ?? "").Trim());
            managedKeys = ManagedLogKeysWithWriter;
        }
        else
        {
            writerJson = "";
            managedKeys = ManagedLogKeysNoWriter;
        }

        var managed = ServerConfigPatch.LogNode(level, include, exclude, writerJson);
        var merged = HandlerPatch.MergeUnmanaged(_original, managed, managedKeys);

        if (!await DiffConfirmDialog.ShowAsync(WindowSystem, $"Apply log {_logName}", _original, merged, Modal)) return;
        var result = await _editor.ApplyAsync((a, ct) => a.UpsertConfigAsync(_path, merged, ct), $"log {_logName}");
        if (result.Success) CloseWithResult(true); else Err(result.Error ?? "Write failed.");
    }

    private void Err(string m) =>
        _error?.SetContent(new List<string> { $"[{UIConstants.Bad.ToMarkup()}]{m.Replace("[", "[[").Replace("]", "]]")}[/]" });
}
