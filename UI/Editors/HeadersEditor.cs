// -----------------------------------------------------------------------
// LazyCaddy - HeadersEditor: a headers node (a standalone headers handler OR a
// reverse_proxy's `/headers`) as a tab in the consolidated route modal. Ported
// from HeadersForm's load (OpsToLines) + patch-build (ParseOps → HandlerPatch.
// Headers) — modal-wrapper dropped, the modal owns a single batched apply.
//
// The ctor takes the FULL node path. No merge — the form patched the whole node.
// Ctrl+S apply is now the modal's job, so HandleKey returns false.
// -----------------------------------------------------------------------

using System.Text.Json;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;
using LazyCaddy.Configuration;
using LazyCaddy.Services;

namespace LazyCaddy.UI.Editors;

public sealed class HeadersEditor : IConfigEditor
{
    private readonly string _path;
    private MultilineEditControl? _req, _resp, _reqHeaders;
    private PromptControl? _reqStatus;
    private Action? _onDirty;
    private string _original = "{}";

    private string _lReq = "", _lResp = "", _lReqStatus = "", _lReqHeaders = "";

    public HeadersEditor(string path) => _path = path;

    public string TabTitle => "Headers";
    public string ConfigPath => _path;

    public void Build(ScrollablePanelControl container, Action onDirtyChanged)
    {
        _onDirty = onDirtyChanged;
        var muted = UIConstants.MutedText.ToMarkup();
        container.AddControl(Controls.Markup()
            .AddLine($"[{muted}]One op per line: 'set Name: value' · 'add Name: value' · 'del Name'.[/]")
            .AddLine($"[{muted}]replace Name: a => b   ·   replace ~Name: regex => b[/]")
            .WithMargin(2, 1, 2, 0).Build());
        container.AddControl(Controls.Markup().AddLine($"[{UIConstants.Accent.ToMarkup()}]Request:[/]").WithMargin(2, 0, 2, 0).Build());
        _req = Controls.MultilineEdit("").NoWrap().WithViewportHeight(5).WithVerticalScrollbar(ScrollbarVisibility.Auto).WithMargin(2, 0, 2, 0).Build();
        container.AddControl(_req);
        container.AddControl(Controls.Markup().AddLine($"[{UIConstants.Accent.ToMarkup()}]Response:[/]").WithMargin(2, 0, 2, 0).Build());
        _resp = Controls.MultilineEdit("").NoWrap().WithViewportHeight(5).WithVerticalScrollbar(ScrollbarVisibility.Auto).WithMargin(2, 0, 2, 0).Build();
        container.AddControl(_resp);
        container.AddControl(Controls.Markup().AddLine($"[{UIConstants.Accent.ToMarkup()}]Response require:[/]").WithMargin(2, 0, 2, 0).Build());
        _reqStatus = Controls.Prompt("Require status: ").WithInputWidth(44).WithMargin(2, 0, 2, 0).Build();
        container.AddControl(_reqStatus);
        _reqHeaders = Controls.MultilineEdit("").NoWrap().WithViewportHeight(2).WithVerticalScrollbar(ScrollbarVisibility.Auto).WithMargin(2, 0, 2, 0).Build();
        container.AddControl(_reqHeaders);
    }

    public async Task LoadAsync(EditCoordinator editor)
    {
        try
        {
            _original = await editor.GetConfigNodeAsync(_path);
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
        catch (JsonException) { /* leave defaults */ }
        catch { /* node absent (404)/network → leave defaults */ }

        CaptureLoaded();
        _onDirty?.Invoke();
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

    private void CaptureLoaded()
    {
        _lReq = _req?.GetContent() ?? ""; _lResp = _resp?.GetContent() ?? "";
        _lReqStatus = _reqStatus?.Input ?? ""; _lReqHeaders = _reqHeaders?.GetContent() ?? "";
    }

    public bool IsDirty =>
        (_req?.GetContent() ?? "") != _lReq || (_resp?.GetContent() ?? "") != _lResp ||
        (_reqStatus?.Input ?? "") != _lReqStatus || (_reqHeaders?.GetContent() ?? "") != _lReqHeaders;

    public IReadOnlyList<PendingWrite> BuildPatch()
    {
        if (!IsDirty) return Array.Empty<PendingWrite>();
        var codes = (_reqStatus?.Input ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(s => int.TryParse(s, out var n) ? n : 0).Where(n => n > 0).ToArray();
        var reqHeaders = new List<(string, string)>();
        foreach (var raw in (_reqHeaders?.GetContent() ?? "").Split('\n'))
        { var l = raw.Trim(); var c = l.IndexOf(':'); if (c > 0) reqHeaders.Add((l[..c].Trim(), l[(c + 1)..].Trim())); }
        ResponseRequireInput? require = (codes.Length > 0 || reqHeaders.Count > 0) ? new ResponseRequireInput(codes, reqHeaders) : null;
        // No merge — the form patched the whole headers node.
        var newJson = HandlerPatch.Headers(ParseOps(_req?.GetContent() ?? ""), ParseOps(_resp?.GetContent() ?? ""), require);
        return new[] { new PendingWrite(ConfigPath, newJson, _original, "headers") };
    }

    public void Revert()
    {
        _req?.SetContent(_lReq); _resp?.SetContent(_lResp);
        _reqStatus?.SetInput(_lReqStatus); _reqHeaders?.SetContent(_lReqHeaders);
    }

    public bool HandleKey(ConsoleKeyInfo key) => false;
}
