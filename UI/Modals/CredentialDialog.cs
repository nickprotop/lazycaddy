// -----------------------------------------------------------------------
// LazyCaddy - prompt for HTTP basic-auth credentials. Two modes:
//   - Add:   prompts for username + password → (Username, Password) or null.
//   - Reset: username shown read-only in the title → new password → string or null.
// The password is masked in the field and NEVER logged or echoed anywhere; the
// caller bcrypt-hashes it immediately and discards the plaintext.
// -----------------------------------------------------------------------

using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;
using LazyCaddy.Configuration;

namespace LazyCaddy.UI.Modals;

public sealed class CredentialDialog : ModalBase<CredentialDialog.Credentials?>
{
    /// <summary>The entered credentials. For a reset, <see cref="Credentials.Username"/> echoes the supplied user.</summary>
    public readonly record struct Credentials(string Username, string Password);

    private readonly bool _addMode;       // true: prompt username + password; false: password only
    private readonly string _username;    // pre-set (reset) or empty (add)
    private PromptControl? _user;
    private PromptControl? _pass;

    private CredentialDialog(bool addMode, string username)
    {
        _addMode = addMode;
        _username = username;
    }

    /// <summary>Add a user: prompt for username + password. Returns null if cancelled or either is empty.</summary>
    public static async Task<(string Username, string Password)?> ShowAddAsync(ConsoleWindowSystem ws, Window? parent = null)
    {
        var r = await ((ModalBase<Credentials?>)new CredentialDialog(addMode: true, username: "")).ShowAsync(ws, parent);
        return r is { } v ? (v.Username, v.Password) : null;
    }

    /// <summary>Reset a user's password (username shown read-only). Returns the new password, or null if cancelled/empty.</summary>
    public static async Task<string?> ShowResetAsync(ConsoleWindowSystem ws, string username, Window? parent = null)
    {
        var r = await ((ModalBase<Credentials?>)new CredentialDialog(addMode: false, username: username)).ShowAsync(ws, parent);
        return r is { } v ? v.Password : null;
    }

    protected override string GetTitle() => _addMode ? " Add user " : $" Reset password — {_username} ";
    protected override (int width, int height) GetSize() => (62, _addMode ? 11 : 10);
    protected override Credentials? GetDefaultResult() => null;

    protected override void BuildContent()
    {
        var muted = UIConstants.MutedText.ToMarkup();
        Modal.AddControl(Controls.Markup()
            .AddLine($"[{muted}]Password is bcrypt-hashed on save; it is never stored or shown in plaintext.[/]")
            .WithMargin(2, 1, 2, 0).Build());

        if (_addMode)
        {
            _user = Controls.Prompt("Username: ").WithInput("").WithInputWidth(44).Build();
            Modal.AddControl(_user);
        }

        _pass = Controls.Prompt("Password: ").WithInput("").WithInputWidth(44).WithMaskCharacter('*').Build();
        Modal.AddControl(_pass);

        var ok = Controls.Button(" OK (Enter) ").Build(); ok.Click += (_, _) => Submit();
        var cancel = Controls.Button(" Cancel (Esc) ").Build(); cancel.Click += (_, _) => CloseWithResult(null);
        Modal.AddControl(Controls.HorizontalGrid().WithAlignment(HorizontalAlignment.Center).StickyBottom()
            .Column(c => c.Add(ok)).Column(c => c.Width(2)).Column(c => c.Add(cancel)).Build());
    }

    private void Submit()
    {
        var pass = _pass?.Input ?? "";
        if (pass.Length == 0) { CloseWithResult(null); return; }
        var user = _addMode ? (_user?.Input ?? "").Trim() : _username;
        if (user.Length == 0) { CloseWithResult(null); return; }
        CloseWithResult(new Credentials(user, pass));
    }

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        if (e.KeyInfo.Key == ConsoleKey.Enter) { Submit(); e.Handled = true; }
        else if (e.KeyInfo.Key == ConsoleKey.Escape) { CloseWithResult(null); e.Handled = true; }
    }
}
