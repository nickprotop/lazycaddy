using System.Text.Json;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;
using LazyCaddy.Configuration;
using LazyCaddy.Services;

namespace LazyCaddy.UI.Modals.Handlers;

public sealed class HeadersForm : ModalBase<bool>
{
    private readonly string _path;
    private readonly EditCoordinator _editor;
    private MultilineEditControl? _req, _resp;
    private MarkupControl? _error;
    private string _original = "{}";

    private HeadersForm(string path, EditCoordinator editor) { _path = path; _editor = editor; }

    public static Task<bool> ShowAsync(ConsoleWindowSystem ws, string path, EditCoordinator editor, Window? parent = null)
        => ((ModalBase<bool>)new HeadersForm(path, editor)).ShowAsync(ws, parent);

    protected override string GetTitle() => " Edit headers ";
    protected override (int width, int height) GetSize() => (80, 22);
    protected override bool GetDefaultResult() => false;

    protected override void BuildContent()
    {
        var muted = UIConstants.MutedText.ToMarkup();
        Modal.AddControl(Controls.Markup()
            .AddLine($"[{muted}]One op per line: 'set Name: value' · 'add Name: value' · 'del Name'.[/]")
            .AddLine($"[{muted}]Ctrl+S apply · Esc cancel.[/]")
            .WithMargin(2, 1, 2, 0).Build());
        Modal.AddControl(Controls.Markup().AddLine($"[{UIConstants.Accent.ToMarkup()}]Request:[/]").WithMargin(2, 0, 2, 0).Build());
        _req = Controls.MultilineEdit("").NoWrap().WithViewportHeight(5).WithVerticalScrollbar(ScrollbarVisibility.Auto).WithMargin(2, 0, 2, 0).Build();
        Modal.AddControl(_req);
        Modal.AddControl(Controls.Markup().AddLine($"[{UIConstants.Accent.ToMarkup()}]Response:[/]").WithMargin(2, 0, 2, 0).Build());
        _resp = Controls.MultilineEdit("").NoWrap().WithViewportHeight(5).WithVerticalScrollbar(ScrollbarVisibility.Auto).WithMargin(2, 0, 2, 0).Build();
        Modal.AddControl(_resp);
        _error = Controls.Markup().WithMargin(2, 0, 2, 0).StickyBottom().Build(); Modal.AddControl(_error);
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            _original = await _editor.GetConfigNodeAsync(_path);
            using var d = JsonDocument.Parse(_original); var r = d.RootElement;
            _req?.SetContent(OpsToLines(r, "request"));
            _resp?.SetContent(OpsToLines(r, "response"));
        }
        catch (JsonException ex) { Err($"Could not parse headers node: {ex.Message}"); }
        catch { }
    }

    private static string OpsToLines(JsonElement root, string dir)
    {
        if (!root.TryGetProperty(dir, out var ops) || ops.ValueKind != JsonValueKind.Object) return "";
        var lines = new List<string>();
        foreach (var verb in new[] { "set", "add" })
            if (ops.TryGetProperty(verb, out var map) && map.ValueKind == JsonValueKind.Object)
                foreach (var h in map.EnumerateObject())
                {
                    var val = h.Value.ValueKind == JsonValueKind.Array && h.Value.GetArrayLength() > 0 ? h.Value[0].GetString() : "";
                    lines.Add($"{verb} {h.Name}: {val}");
                }
        if (ops.TryGetProperty("delete", out var del) && del.ValueKind == JsonValueKind.Array)
            foreach (var h in del.EnumerateArray()) lines.Add($"del {h.GetString()}");
        return string.Join("\n", lines);
    }

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        if (e.KeyInfo.Key == ConsoleKey.Escape) { CloseWithResult(false); e.Handled = true; return; }
        if (e.KeyInfo.Key == ConsoleKey.S && (e.KeyInfo.Modifiers & ConsoleModifiers.Control) != 0) { e.Handled = true; _ = ApplyAsync(); }
    }

    private static HeaderOpsInput ParseOps(string text)
    {
        var add = new List<(string, string)>(); var set = new List<(string, string)>(); var del = new List<string>();
        foreach (var raw in (text ?? "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            var sp = line.IndexOf(' ');
            if (sp < 0) continue;
            var verb = line[..sp].ToLowerInvariant();
            var rest = line[(sp + 1)..].Trim();
            if (verb == "del") { del.Add(rest); continue; }
            var colon = rest.IndexOf(':');
            if (colon < 0) continue;
            var name = rest[..colon].Trim();
            var val = rest[(colon + 1)..].Trim();
            if (verb == "set") set.Add((name, val));
            else if (verb == "add") add.Add((name, val));
        }
        return new HeaderOpsInput(add, set, del);
    }

    private async Task ApplyAsync()
    {
        var newJson = HandlerPatch.Headers(ParseOps(_req?.GetContent() ?? ""), ParseOps(_resp?.GetContent() ?? ""));
        if (!await DiffConfirmDialog.ShowAsync(WindowSystem, "Apply headers", _original, newJson, Modal)) return;
        var result = await _editor.ApplyAsync((a, ct) => a.UpsertConfigAsync(_path, newJson, ct), $"headers {_path}");
        if (result.Success) CloseWithResult(true); else Err(result.Error ?? "Write failed.");
    }

    private void Err(string m) =>
        _error?.SetContent(new List<string> { $"[{UIConstants.Bad.ToMarkup()}]{m.Replace("[", "[[").Replace("]", "]]")}[/]" });
}
