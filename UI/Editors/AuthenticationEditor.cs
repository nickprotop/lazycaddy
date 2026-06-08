// -----------------------------------------------------------------------
// LazyCaddy - AuthenticationEditor: the authentication handler's `providers`
// node as a tab in the consolidated route modal. Authentication providers are
// polymorphic and credential config is security-sensitive (password hashing is
// provider-specific), so — like AuthenticationForm (which delegated to
// RawNodeEditDialog) — the providers node is edited as raw JSON.
// -----------------------------------------------------------------------

using System.Text.Json;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;
using LazyCaddy.Configuration;
using LazyCaddy.Services;

namespace LazyCaddy.UI.Editors;

public sealed class AuthenticationEditor : IConfigEditor
{
    private readonly string _path;   // = "{authPath}/providers"
    private MultilineEditControl? _edit;
    private Action? _onDirty;
    private string _original = "";

    private string _lContent = "";

    // AuthenticationForm edits "{path}/providers"; mirror that here.
    public AuthenticationEditor(string authPath) => _path = $"{authPath}/providers";

    public string TabTitle => "Authentication";
    public string ConfigPath => _path;

    public void Build(ScrollablePanelControl container, Action onDirtyChanged)
    {
        _onDirty = onDirtyChanged;
        var muted = UIConstants.MutedText.ToMarkup();
        container.AddControl(Controls.Markup()
            .AddLine($"[{muted}]Raw JSON for {_path}. Providers are polymorphic — edit as raw JSON.[/]")
            .WithMargin(2, 1, 2, 0).Build());
        _edit = Controls.MultilineEdit("")
            .WithLineNumbers(true)
            .WithSyntaxHighlighter(new JsonSyntaxHighlighter())
            .WithVerticalAlignment(VerticalAlignment.Fill).NoWrap()
            .WithVerticalScrollbar(ScrollbarVisibility.Auto).WithMargin(2, 1, 2, 0).Build();
        container.AddControl(_edit);
    }

    public async Task LoadAsync(EditCoordinator editor)
    {
        try
        {
            _original = await editor.GetConfigNodeAsync(_path);
            using var doc = JsonDocument.Parse(_original);
            _original = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
            _edit?.SetContent(_original);
        }
        catch (JsonException) { /* leave defaults */ }
        catch { /* node absent (404)/network → leave defaults */ }

        CaptureLoaded();
        _onDirty?.Invoke();
    }

    private void CaptureLoaded() => _lContent = Content();

    public bool IsDirty => Content() != _lContent;

    public IReadOnlyList<PendingWrite> BuildPatch()
    {
        if (!IsDirty) return Array.Empty<PendingWrite>();
        var newJson = _edit?.GetContent() ?? "";
        return new[] { new PendingWrite(_path, newJson, _original, "authentication providers") };
    }

    public void Revert() => _edit?.SetContent(_lContent);

    public bool HandleKey(ConsoleKeyInfo key) => false;

    private string Content() => (_edit?.GetContent() ?? "").Trim();
}
