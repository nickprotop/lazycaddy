// -----------------------------------------------------------------------
// LazyCaddy - edit the TLS automation issuer for a certificate's domain.
// PATCHes the first automation policy's issuers node, going through
// DiffConfirmDialog + EditCoordinator to apply.
// -----------------------------------------------------------------------

using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using LazyCaddy.Configuration;
using LazyCaddy.Models;
using LazyCaddy.Services;

namespace LazyCaddy.UI.Modals;

public sealed class EditTlsPolicyDialog : ModalBase<bool>
{
    private readonly Cert _cert;
    private readonly EditCoordinator _editor;
    private DropdownControl? _issuer;
    private MarkupControl? _error;

    private EditTlsPolicyDialog(Cert cert, EditCoordinator editor) { _cert = cert; _editor = editor; }

    public static Task<bool> ShowAsync(ConsoleWindowSystem ws, Cert cert, EditCoordinator editor, Window? parent = null)
        => ((ModalBase<bool>)new EditTlsPolicyDialog(cert, editor)).ShowAsync(ws, parent);

    protected override string GetTitle() => $" TLS policy — {_cert.Domain} ";
    protected override (int width, int height) GetSize() => (72, 12);
    protected override bool GetDefaultResult() => false;

    protected override void BuildContent()
    {
        var muted = UIConstants.MutedText.ToMarkup();
        Modal.AddControl(Controls.Markup()
            .AddLine($"[{muted}]Issuer for {_cert.Domain}. Current: {_cert.Issuer.Replace("[", "[[").Replace("]", "]]")}[/]")
            .WithMargin(2, 1, 2, 0).Build());
        _issuer = Controls.Dropdown("Issuer: ").AddItems("acme", "zerossl", "internal").Build();
        Modal.AddControl(_issuer);
        _error = Controls.Markup().WithMargin(2, 1, 2, 0).Build(); Modal.AddControl(_error);
        Modal.AddControl(Controls.Markup().AddLine($"[{muted}]Enter: apply   Esc: cancel[/]").WithMargin(2, 0, 2, 0).StickyBottom().Build());
    }

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        if (e.KeyInfo.Key == ConsoleKey.Escape) { CloseWithResult(false); e.Handled = true; return; }
        if (e.KeyInfo.Key == ConsoleKey.Enter) { e.Handled = true; _ = ApplyAsync(); }
    }

    private async Task ApplyAsync()
    {
        var issuer = _issuer?.SelectedValue ?? "acme";
        // For v2, target the first tls automation policy's issuers node. The precise
        // policy index by subject is a refinement; index 0 is correct for the common
        // single-policy setup.
        var path = "apps/tls/automation/policies/0/issuers";
        var newJson = $"[{{\"module\":\"{issuer}\"}}]";
        string oldJson;
        try { oldJson = await _editor.GetConfigNodeAsync(path); } catch { oldJson = "(unknown)"; }
        if (!await DiffConfirmDialog.ShowAsync(WindowSystem, "Apply TLS issuer", oldJson, newJson, Modal)) return;
        var result = await _editor.ApplyAsync((a, ct) => a.UpsertConfigAsync(path, newJson, ct), $"tls issuer {_cert.Domain} → {issuer}");
        if (result.Success) CloseWithResult(true);
        else _error?.SetContent(new List<string> { $"[{UIConstants.Bad.ToMarkup()}]{(result.Error ?? "").Replace("[", "[[").Replace("]", "]]")}[/]" });
    }
}
