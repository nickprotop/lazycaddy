// -----------------------------------------------------------------------
// LazyCaddy - SecurityHeadersEditor: a presets form for the `headers` handler,
// shown as a SECOND tab alongside the raw-ops HeadersEditor. It edits the SAME
// headers handler node, but only the well-known security response headers
// (HSTS, nosniff, X-Frame-Options, Referrer-Policy, CSP), MERGING them into the
// existing response.set so non-security headers the raw editor set survive.
// -----------------------------------------------------------------------

using System.Text.Json;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using LazyCaddy.Configuration;
using LazyCaddy.Services;

namespace LazyCaddy.UI.Editors;

public sealed class SecurityHeadersEditor : IConfigEditor
{
    private const string DefaultHsts = "max-age=31536000; includeSubDomains";

    private static readonly string[] FrameOptions = { "Off", "DENY", "SAMEORIGIN" };
    private static readonly string[] ReferrerOptions =
        { "Off", "strict-origin-when-cross-origin", "no-referrer", "same-origin" };

    private readonly string _path;
    private CheckboxControl? _hsts, _noSniff;
    private PromptControl? _hstsValue, _csp;
    private DropdownControl? _frame, _referrer;
    private Action? _onDirty;
    private string _origNodeJson = "{}";

    // Loaded-snapshot mirror of every control's value, for IsDirty + Revert.
    private bool _lHsts, _lNoSniff;
    private string _lHstsValue = "", _lCsp = "";
    private string? _lFrame, _lReferrer;

    public SecurityHeadersEditor(string headersPath) => _path = headersPath;

    public string TabTitle => "Security headers";
    public string ConfigPath => _path;

    public void Build(IControlHost container, Action onDirtyChanged)
    {
        _onDirty = onDirtyChanged;
        var muted = UIConstants.MutedText.ToMarkup();

        container.AddControl(Controls.Markup()
            .AddLine($"[{muted}]Common response security headers (merged with the raw Headers tab).[/]")
            .WithMargin(2, 1, 2, 0).Build());

        _hsts = new CheckboxControl { Label = "Strict-Transport-Security (HSTS)", Checked = false };
        _hsts.CheckedChanged += (_, _) => _onDirty?.Invoke();
        container.AddControl(_hsts);
        _hstsValue = Controls.Prompt("HSTS value:    ").WithInput(DefaultHsts).WithInputWidth(44).Build();
        container.AddControl(_hstsValue);

        _noSniff = new CheckboxControl { Label = "X-Content-Type-Options: nosniff", Checked = false };
        _noSniff.CheckedChanged += (_, _) => _onDirty?.Invoke();
        container.AddControl(_noSniff);

        _frame = Controls.Dropdown("X-Frame-Options:  ").AddItems(FrameOptions).Build();
        container.AddControl(_frame);

        _referrer = Controls.Dropdown("Referrer-Policy:  ").AddItems(ReferrerOptions).Build();
        container.AddControl(_referrer);

        _csp = Controls.Prompt("Content-Security-Policy: ").WithInputWidth(44).Build();
        container.AddControl(_csp);

        container.AddControl(Controls.Toolbar()
            .AddButton(UIConstants.ActionButton("Recommended baseline", "", ApplyBaseline))
            .WithSpacing(2).WithMargin(2, 1, 2, 0).Build());
    }

    private void ApplyBaseline()
    {
        if (_hsts is not null) _hsts.Checked = true;
        _hstsValue?.SetInput(DefaultHsts);
        if (_noSniff is not null) _noSniff.Checked = true;
        SetDropdown(_frame, "DENY");
        SetDropdown(_referrer, "strict-origin-when-cross-origin");
        _onDirty?.Invoke();
    }

    public async Task LoadAsync(EditCoordinator editor)
    {
        try
        {
            _origNodeJson = await editor.GetConfigNodeAsync(_path);
            using var d = JsonDocument.Parse(_origNodeJson); var r = d.RootElement;
            if (r.TryGetProperty("response", out var resp) && resp.ValueKind == JsonValueKind.Object
                && resp.TryGetProperty("set", out var set) && set.ValueKind == JsonValueKind.Object)
            {
                var hsts = First(set, "Strict-Transport-Security");
                if (hsts is not null) { if (_hsts is not null) _hsts.Checked = true; _hstsValue?.SetInput(hsts); }
                if (First(set, "X-Content-Type-Options") == "nosniff" && _noSniff is not null) _noSniff.Checked = true;
                SetDropdown(_frame, First(set, "X-Frame-Options"));
                SetDropdown(_referrer, First(set, "Referrer-Policy"));
                var csp = First(set, "Content-Security-Policy");
                if (csp is not null) _csp?.SetInput(csp);
            }
        }
        catch (JsonException) { /* leave defaults */ }
        catch { /* node absent (404)/network → leave defaults */ }

        CaptureLoaded();
        _onDirty?.Invoke();
    }

    // The header value in Caddy is a JSON string ARRAY — take element [0].
    private static string? First(JsonElement set, string key)
        => set.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Array && v.GetArrayLength() > 0
            ? v[0].GetString()
            : null;

    private static void SetDropdown(DropdownControl? d, string? value)
    {
        if (d is null || string.IsNullOrEmpty(value)) return;
        d.SelectedValue = value;
    }

    private void CaptureLoaded()
    {
        _lHsts = _hsts?.Checked ?? false; _lHstsValue = T(_hstsValue);
        _lNoSniff = _noSniff?.Checked ?? false;
        _lFrame = _frame?.SelectedValue; _lReferrer = _referrer?.SelectedValue;
        _lCsp = T(_csp);
    }

    public bool IsDirty =>
        (_hsts?.Checked ?? false) != _lHsts || T(_hstsValue) != _lHstsValue ||
        (_noSniff?.Checked ?? false) != _lNoSniff ||
        (_frame?.SelectedValue) != _lFrame || (_referrer?.SelectedValue) != _lReferrer ||
        T(_csp) != _lCsp;

    public IReadOnlyList<PendingWrite> BuildPatch()
    {
        if (!IsDirty) return Array.Empty<PendingWrite>();

        // Start from the existing response.set so non-security headers survive.
        var set = ExistingSet(_origNodeJson);

        Overlay(set, "Strict-Transport-Security",
            (_hsts?.Checked ?? false) ? Nz(T(_hstsValue), DefaultHsts) : null);
        Overlay(set, "X-Content-Type-Options", (_noSniff?.Checked ?? false) ? "nosniff" : null);
        Overlay(set, "X-Frame-Options", DropdownOrNull(_frame));
        Overlay(set, "Referrer-Policy", DropdownOrNull(_referrer));
        Overlay(set, "Content-Security-Policy", string.IsNullOrEmpty(T(_csp)) ? null : T(_csp));

        var node = new Dictionary<string, object>
        {
            ["handler"] = "headers",
            ["response"] = new Dictionary<string, object> { ["set"] = set },
        };
        var newJson = JsonSerializer.Serialize(node, new JsonSerializerOptions { WriteIndented = true });
        return new[] { new PendingWrite(_path, newJson, _origNodeJson, "security headers") };
    }

    // Deserialize old node's response.set into a string→string[] map (preserves
    // any non-security headers); empty map if absent/unparseable.
    private static Dictionary<string, string[]> ExistingSet(string nodeJson)
    {
        var map = new Dictionary<string, string[]>();
        try
        {
            using var d = JsonDocument.Parse(nodeJson); var r = d.RootElement;
            if (r.TryGetProperty("response", out var resp) && resp.ValueKind == JsonValueKind.Object
                && resp.TryGetProperty("set", out var set) && set.ValueKind == JsonValueKind.Object)
                foreach (var p in set.EnumerateObject())
                    if (p.Value.ValueKind == JsonValueKind.Array)
                        map[p.Name] = p.Value.EnumerateArray().Select(e => e.GetString() ?? "").ToArray();
        }
        catch (JsonException) { /* empty map */ }
        return map;
    }

    // enabled → set/overwrite the header; disabled (null) → remove that key only.
    private static void Overlay(Dictionary<string, string[]> set, string key, string? value)
    {
        if (value is null) set.Remove(key);
        else set[key] = new[] { value };
    }

    private static string? DropdownOrNull(DropdownControl? d)
    {
        var v = d?.SelectedValue;
        return string.IsNullOrEmpty(v) || v == "Off" ? null : v;
    }

    public void Revert()
    {
        if (_hsts is not null) _hsts.Checked = _lHsts;
        _hstsValue?.SetInput(_lHstsValue);
        if (_noSniff is not null) _noSniff.Checked = _lNoSniff;
        if (_frame is not null) _frame.SelectedValue = _lFrame;
        if (_referrer is not null) _referrer.SelectedValue = _lReferrer;
        _csp?.SetInput(_lCsp);
    }

    public bool HandleKey(ConsoleKeyInfo key) => false;

    private static string T(PromptControl? c) => (c?.Input ?? "").Trim();
    private static string Nz(string v, string fallback) => string.IsNullOrEmpty(v) ? fallback : v;
}
