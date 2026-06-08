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
    private MultilineEditControl? _req, _resp, _reqHeaders;
    private PromptControl? _reqStatus;
    private MarkupControl? _error;
    private string _original = "{}";

    private HeadersForm(string path, EditCoordinator editor) { _path = path; _editor = editor; }

    public static Task<bool> ShowAsync(ConsoleWindowSystem ws, string path, EditCoordinator editor, Window? parent = null)
        => ((ModalBase<bool>)new HeadersForm(path, editor)).ShowAsync(ws, parent);

    protected override string GetTitle() => " Edit headers ";
    protected override (int width, int height) GetSize() => (80, 28);
    protected override bool GetDefaultResult() => false;

    protected override void BuildContent()
    {
        var muted = UIConstants.MutedText.ToMarkup();
        Modal.AddControl(Controls.Markup()
            .AddLine($"[{muted}]One op per line: 'set Name: value' · 'add Name: value' · 'del Name'.[/]")
            .AddLine($"[{muted}]replace Name: a => b   ·   replace ~Name: regex => b[/]")
            .AddLine($"[{muted}]Ctrl+S apply · Esc cancel.[/]")
            .WithMargin(2, 1, 2, 0).Build());
        Modal.AddControl(Controls.Markup().AddLine($"[{UIConstants.Accent.ToMarkup()}]Request:[/]").WithMargin(2, 0, 2, 0).Build());
        _req = Controls.MultilineEdit("").NoWrap().WithViewportHeight(5).WithVerticalScrollbar(ScrollbarVisibility.Auto).WithMargin(2, 0, 2, 0).Build();
        Modal.AddControl(_req);
        Modal.AddControl(Controls.Markup().AddLine($"[{UIConstants.Accent.ToMarkup()}]Response:[/]").WithMargin(2, 0, 2, 0).Build());
        _resp = Controls.MultilineEdit("").NoWrap().WithViewportHeight(5).WithVerticalScrollbar(ScrollbarVisibility.Auto).WithMargin(2, 0, 2, 0).Build();
        Modal.AddControl(_resp);
        Modal.AddControl(Controls.Markup().AddLine($"[{UIConstants.Accent.ToMarkup()}]Response require:[/]").WithMargin(2, 0, 2, 0).Build());
        _reqStatus = Controls.Prompt("Require status: ").WithInputWidth(44).WithMargin(2, 0, 2, 0).Build();
        Modal.AddControl(_reqStatus);
        _reqHeaders = Controls.MultilineEdit("").NoWrap().WithViewportHeight(2).WithVerticalScrollbar(ScrollbarVisibility.Auto).WithMargin(2, 0, 2, 0).Build();
        Modal.AddControl(_reqHeaders);
        _error = Controls.Markup().WithMargin(2, 0, 2, 0).StickyBottom().Build(); Modal.AddControl(_error);
        RunGuarded(LoadAsync, Err);
    }

    private async Task LoadAsync()
    {
        try
        {
            _original = await _editor.GetConfigNodeAsync(_path);
            using var d = JsonDocument.Parse(_original); var r = d.RootElement;
            _req?.SetContent(OpsToLines(r, "request"));
            _resp?.SetContent(OpsToLines(r, "response"));
            if (r.TryGetProperty("response", out var respNode) && respNode.ValueKind == JsonValueKind.Object
                && respNode.TryGetProperty("require", out var require) && require.ValueKind == JsonValueKind.Object)
            {
                if (require.TryGetProperty("status_code", out var sc) && sc.ValueKind == JsonValueKind.Array)
                    _reqStatus?.SetInput(string.Join(", ", sc.EnumerateArray().Select(e => e.ToString())));
                if (require.TryGetProperty("headers", out var hm) && hm.ValueKind == JsonValueKind.Object)
                    _reqHeaders?.SetContent(string.Join("\n", hm.EnumerateObject().Select(p =>
                        $"{p.Name}: {(p.Value.ValueKind == JsonValueKind.Array && p.Value.GetArrayLength() > 0 ? p.Value[0].GetString() : "")}")));
            }
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
        if (ops.TryGetProperty("replace", out var rep) && rep.ValueKind == JsonValueKind.Object)
            foreach (var h in rep.EnumerateObject())
                if (h.Value.ValueKind == JsonValueKind.Array)
                    foreach (var item in h.Value.EnumerateArray())
                    {
                        var isRegex = item.TryGetProperty("search_regexp", out var sr);
                        var search = isRegex ? sr.GetString()
                            : (item.TryGetProperty("search", out var s) ? s.GetString() : "");
                        var repl = item.TryGetProperty("replace", out var rp) ? rp.GetString() : "";
                        lines.Add($"replace {(isRegex ? "~" : "")}{h.Name}: {search} => {repl}");
                    }
        return string.Join("\n", lines);
    }

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        if (e.KeyInfo.Key == ConsoleKey.Escape) { CloseWithResult(false); e.Handled = true; return; }
        if (e.KeyInfo.Key == ConsoleKey.S && (e.KeyInfo.Modifiers & ConsoleModifiers.Control) != 0) { e.Handled = true; RunGuarded(ApplyAsync, Err); }
    }

    private static HeaderOpsInput ParseOps(string text)
    {
        var add = new List<(string, string)>(); var set = new List<(string, string)>(); var del = new List<string>();
        var replace = new List<(string, string, string, bool)>();
        foreach (var raw in (text ?? "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            var sp = line.IndexOf(' ');
            if (sp < 0) continue;
            var verb = line[..sp].ToLowerInvariant();
            var rest = line[(sp + 1)..].Trim();
            if (verb == "del") { del.Add(rest); continue; }
            if (verb == "replace")
            {
                bool isRegex = rest.StartsWith("~");
                var body = isRegex ? rest[1..] : rest;
                var colon = body.IndexOf(':');
                if (colon < 0) continue;
                var header = body[..colon].Trim();
                var rhs = body[(colon + 1)..];
                var arrow = rhs.IndexOf("=>");
                if (arrow < 0) continue;
                var search = rhs[..arrow].Trim();
                var replacement = rhs[(arrow + 2)..].Trim();
                replace.Add((header, search, replacement, isRegex));
                continue;
            }
            var colon2 = rest.IndexOf(':');
            if (colon2 < 0) continue;
            var name = rest[..colon2].Trim();
            var val = rest[(colon2 + 1)..].Trim();
            if (verb == "set") set.Add((name, val));
            else if (verb == "add") add.Add((name, val));
        }
        return new HeaderOpsInput(add, set, del, replace);
    }

    private async Task ApplyAsync()
    {
        var codes = (_reqStatus?.Input ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(s => int.TryParse(s, out var n) ? n : 0).Where(n => n > 0).ToArray();
        var reqHeaders = new List<(string, string)>();
        foreach (var raw in (_reqHeaders?.GetContent() ?? "").Split('\n'))
        { var l = raw.Trim(); var c = l.IndexOf(':'); if (c > 0) reqHeaders.Add((l[..c].Trim(), l[(c + 1)..].Trim())); }
        ResponseRequireInput? require = (codes.Length > 0 || reqHeaders.Count > 0) ? new ResponseRequireInput(codes, reqHeaders) : null;
        var newJson = HandlerPatch.Headers(ParseOps(_req?.GetContent() ?? ""), ParseOps(_resp?.GetContent() ?? ""), require);
        if (!await DiffConfirmDialog.ShowAsync(WindowSystem, "Apply headers", _original, newJson, Modal)) return;
        var result = await _editor.ApplyAsync((a, ct) => a.UpsertConfigAsync(_path, newJson, ct), $"headers {_path}");
        if (result.Success) CloseWithResult(true); else Err(result.Error ?? "Write failed.");
    }

    private void Err(string m) =>
        _error?.SetContent(new List<string> { $"[{UIConstants.Bad.ToMarkup()}]{m.Replace("[", "[[").Replace("]", "]]")}[/]" });
}
