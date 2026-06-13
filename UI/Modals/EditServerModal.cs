// UI/Modals/EditServerModal.cs
using LazyCaddy.Configuration;
using LazyCaddy.Services;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;

namespace LazyCaddy.UI.Modals;

/// <summary>Add or edit a server. Full ServerEntry fields + a Test-connection probe. Returns the
/// validated ServerEntry on Save, or null on cancel. Validation reuses ServerStore.Validate against
/// the existing list minus the entry being edited.</summary>
public sealed class EditServerModal : ModalBase<ServerEntry?>
{
    private readonly ServerEntry? _editing;
    private readonly IReadOnlyList<ServerEntry> _existing;
    private PromptControl? _name, _url, _certDir, _accessLog;
    private CheckboxControl? _readOnly;
    private MarkupControl? _status;

    private EditServerModal(ServerEntry? editing, IReadOnlyList<ServerEntry> existing)
    { _editing = editing; _existing = existing; }

    public static Task<ServerEntry?> ShowAsync(ConsoleWindowSystem ws, ServerEntry? editing,
        IReadOnlyList<ServerEntry> existing, Window? parent = null)
        => ((ModalBase<ServerEntry?>)new EditServerModal(editing, existing)).ShowAsync(ws, parent);

    protected override string GetTitle() => _editing is null ? " Add server " : " Edit server ";
    protected override (int width, int height) GetSize() => (74, 18);
    protected override ServerEntry? GetDefaultResult() => null;

    protected override void BuildContent()
    {
        var muted = UIConstants.MutedText.ToMarkup();
        Modal.AddControl(Controls.Markup()
            .AddLine($"[{muted}]Name and admin URL are required. Cert dir / access log are optional overrides.[/]")
            .WithMargin(2, 1, 2, 0).Build());

        _name = Controls.Prompt("Name:       ").WithInput(_editing?.Name ?? "").WithInputWidth(50).Build();
        _url = Controls.Prompt("URL:        ").WithInput(_editing?.Url ?? "http://localhost:2019").WithInputWidth(50).Build();
        _certDir = Controls.Prompt("Cert dir:   ").WithInput(_editing?.CertDir ?? "").WithInputWidth(50).Build();
        _accessLog = Controls.Prompt("Access log: ").WithInput(_editing?.AccessLog ?? "").WithInputWidth(50).Build();
        _readOnly = new CheckboxControl { Label = "Read-only", Checked = _editing?.ReadOnly ?? false };

        Modal.AddControl(_name);
        Modal.AddControl(_url);
        Modal.AddControl(_certDir);
        Modal.AddControl(_accessLog);
        Modal.AddControl(_readOnly);

        _status = Controls.Markup().WithMargin(2, 1, 2, 0).StickyBottom().Build();
        Modal.AddControl(_status);

        Modal.AddControl(Controls.Toolbar()
            .AddButton(UIConstants.ActionButton("Save", "Ctrl+S", () => RunGuarded(SaveAsync, ShowError)))
            .AddButton(UIConstants.ActionButton("Test", "Ctrl+T", () => RunGuarded(TestAsync, ShowError)))
            .AddButton(UIConstants.ActionButton("Cancel", "Esc", () => CloseWithResult(null)))
            .WithSpacing(2).WithMargin(2, 0, 2, 0).WithAboveLine().StickyBottom().Build());
    }

    private ServerEntry Candidate() => new((_name?.Input ?? "").Trim(), (_url?.Input ?? "").Trim())
    {
        CertDir = Empty(_certDir?.Input),
        AccessLog = Empty(_accessLog?.Input),
        ReadOnly = _readOnly?.Checked ?? false,
    };
    private static string? Empty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s!.Trim();

    private Task SaveAsync()
    {
        var candidate = Candidate();
        var others = _existing.Where(e => _editing is null || e.Identity != _editing.Identity).ToList();
        var err = ServerStore.Validate(candidate, others);
        if (err is not null) { ShowError(err); return Task.CompletedTask; }
        CloseWithResult(candidate);
        return Task.CompletedTask;
    }

    private async Task TestAsync()
    {
        var url = (_url?.Input ?? "").Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
        { ShowError("URL must be an absolute http(s) URL."); return; }
        SetStatus($"[{UIConstants.MutedText.ToMarkup()}]Testing {Escape(url)}…[/]");
        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            using var resp = await http.GetAsync(url.TrimEnd('/') + "/config/");
            if (resp.IsSuccessStatusCode)
                SetStatus($"[{UIConstants.Good.ToMarkup()}]✓ Reachable (HTTP {(int)resp.StatusCode})[/]");
            else
                SetStatus($"[{UIConstants.Warn.ToMarkup()}]⚠ HTTP {(int)resp.StatusCode}[/]");
        }
        catch (Exception ex) { ShowError($"Unreachable: {ex.Message}"); }
    }

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        var ctrl = (e.KeyInfo.Modifiers & ConsoleModifiers.Control) != 0;
        if (ctrl && e.KeyInfo.Key == ConsoleKey.S) { e.Handled = true; RunGuarded(SaveAsync, ShowError); return; }
        if (ctrl && e.KeyInfo.Key == ConsoleKey.T) { e.Handled = true; RunGuarded(TestAsync, ShowError); return; }
        base.OnKeyPressed(sender, e);
    }

    private void ShowError(string m) => SetStatus($"[{UIConstants.Bad.ToMarkup()}]{Escape(m)}[/]");
    private void SetStatus(string markup) => _status?.SetContent(new List<string> { markup });
    private static string Escape(string s) => s.Replace("[", "[[").Replace("]", "]]");
}
