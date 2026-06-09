// -----------------------------------------------------------------------
// LazyCaddy - BasicAuthEditor: a guided editor for an `authentication` handler's
// http_basic provider, shown as a tab in the consolidated handler modal. It is a
// proper CRUD list of users (the table is the source of truth) plus an editable
// realm, with Add / Reset / Remove buttons ABOVE the table — Reset/Remove adaptive
// (enabled only with a selected row).
//
// Passwords are bcrypt-hashed the instant they're entered; the editor never holds,
// shows, or writes plaintext. The table shows ONLY the username (never the hash).
// Existing accounts keep their on-disk hash unless explicitly reset.
//
// All edits are STAGED; nothing is written until the modal's batched Apply, which
// PATCHes the whole authentication handler node. An empty user list writes nothing
// (we don't install an empty auth handler).
// -----------------------------------------------------------------------

using System.Text.Json;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;
using LazyCaddy.Configuration;
using LazyCaddy.Services;

namespace LazyCaddy.UI.Editors;

public sealed class BasicAuthEditor : IConfigEditor
{
    private readonly string _path;        // authentication handler node path

    private PromptControl? _realmPrompt;
    private TableControl? _userTable;
    private ButtonControl? _resetBtn;
    private ButtonControl? _removeBtn;
    private Action? _onDirty;

    private string _realm = "restricted";
    private readonly List<(string Username, string Hash)> _accounts = new();

    // Loaded baselines for dirty / revert.
    private string _lRealm = "restricted";
    private List<(string Username, string Hash)> _lAccounts = new();
    private string _origNodeJson = "{}";  // the raw handler node JSON (diff "old" side)

    public BasicAuthEditor(string authPath) => _path = authPath;

    public string TabTitle => "Basic auth";
    public string ConfigPath => _path;

    /// <summary>Set by the host modal: surface validation/info messages to the user.</summary>
    public Action<string>? OnError { get; set; }

    /// <summary>Set by the host modal: prompt for a new user's credentials → (Username, Password) or null.</summary>
    public Func<Task<(string Username, string Password)?>>? PromptAddUser { get; set; }

    /// <summary>Set by the host modal: prompt for a reset password for the given username → new password or null.</summary>
    public Func<string, Task<string?>>? PromptResetPassword { get; set; }

    /// <summary>Set by the host modal: confirm removing the given username → yes/no.</summary>
    public Func<string, Task<bool>>? ConfirmRemove { get; set; }

    /// <summary>Set by the host modal: force the window to rebuild its layout after a structural
    /// (row count) change so the Fill table re-measures its height.</summary>
    public Action? RequestRelayout { get; set; }

    public void Build(IControlHost container, Action onDirtyChanged)
    {
        _onDirty = onDirtyChanged;
        var muted = UIConstants.MutedText.ToMarkup();
        container.AddControl(Controls.Markup()
            .AddLine($"[{muted}]HTTP basic auth users. Passwords are bcrypt-hashed; only set/reset, never shown.[/]")
            .WithMargin(2, 1, 2, 0).Build());

        _realmPrompt = Controls.Prompt("Realm: ").WithInput(_realm).WithInputWidth(40).WithMargin(2, 1, 2, 0).Build();
        _realmPrompt.InputChanged += (_, _) => _onDirty?.Invoke();
        container.AddControl(_realmPrompt);

        container.AddControl(Controls.Markup()
            .AddLine($"[{UIConstants.Accent.ToMarkup()}]Users[/]")
            .WithMargin(2, 1, 2, 0).Build());

        // Buttons ABOVE the table; Reset/Remove are adaptive (enabled only with a selection).
        _resetBtn = UIConstants.ActionButton("Reset", "", () => _ = ResetSelectedAsync());
        _removeBtn = UIConstants.ActionButton("Remove", "Del", () => _ = RemoveSelectedAsync());
        container.AddControl(Controls.Toolbar()
            .AddButton(UIConstants.ActionButton("Add", "Ctrl+A", () => _ = AddAsync()))
            .AddButton(_resetBtn)
            .AddButton(_removeBtn)
            .WithSpacing(2).WithMargin(2, 0, 2, 0).Build());

        _userTable = Controls.Table()
            .AddColumn("User", TextJustification.Left)
            .Rounded().WithBorderColor(UIConstants.MutedText).Interactive()
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .WithVerticalScrollbar(ScrollbarVisibility.Auto).WithName("basicAuthUsers").Build();
        _userTable.SelectedRowChanged += (_, _) => UpdateButtonStates();
        // Enter / double-click on a row resets that user's password.
        _userTable.RowActivated += (_, _) => _ = ResetSelectedAsync();
        container.AddControl(_userTable);

        RefreshTable();
    }

    public async Task LoadAsync(EditCoordinator editor)
    {
        _accounts.Clear();
        _realm = "restricted";
        try
        {
            _origNodeJson = await editor.GetConfigNodeAsync(_path);
            using var doc = JsonDocument.Parse(_origNodeJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("providers", out var providers)
                && providers.ValueKind == JsonValueKind.Object
                && providers.TryGetProperty("http_basic", out var hb)
                && hb.ValueKind == JsonValueKind.Object)
            {
                if (hb.TryGetProperty("realm", out var realm) && realm.ValueKind == JsonValueKind.String
                    && realm.GetString() is { Length: > 0 } r)
                    _realm = r;

                if (hb.TryGetProperty("accounts", out var accounts) && accounts.ValueKind == JsonValueKind.Array)
                {
                    foreach (var a in accounts.EnumerateArray())
                    {
                        if (a.ValueKind != JsonValueKind.Object) continue;
                        var user = a.TryGetProperty("username", out var u) && u.ValueKind == JsonValueKind.String
                            ? u.GetString() ?? "" : "";
                        var hash = a.TryGetProperty("password", out var p) && p.ValueKind == JsonValueKind.String
                            ? p.GetString() ?? "" : "";
                        if (user.Length > 0) _accounts.Add((user, hash));
                    }
                }
            }
        }
        catch (JsonException) { _origNodeJson = "{}"; }
        catch { _origNodeJson = "{}"; }

        if (_realmPrompt is not null) _realmPrompt.Input = _realm;
        CaptureLoaded();
        RefreshTable();
        _onDirty?.Invoke();
    }

    private void CaptureLoaded()
    {
        _lRealm = CurrentRealm();
        _lAccounts = new List<(string, string)>(_accounts);
    }

    private string CurrentRealm()
    {
        var v = (_realmPrompt?.Input ?? _realm).Trim();
        return v.Length == 0 ? "restricted" : v;
    }

    private void RefreshTable()
    {
        if (_userTable is null) return;
        var keep = _userTable.SelectedRowIndex;
        _userTable.ClearRows();
        foreach (var a in _accounts) _userTable.AddRow(new TableRow(a.Username));
        _userTable.SelectedRowIndex = _accounts.Count == 0 ? -1 : Math.Clamp(keep < 0 ? 0 : keep, 0, _accounts.Count - 1);
        UpdateButtonStates();
        RequestRelayout?.Invoke(); // row count changed → rebuild layout so the Fill table re-measures
    }

    private void UpdateButtonStates()
    {
        var hasSel = (_userTable?.SelectedRowIndex ?? -1) >= 0 && _accounts.Count > 0;
        if (_resetBtn is not null) _resetBtn.IsEnabled = hasSel;
        if (_removeBtn is not null) _removeBtn.IsEnabled = hasSel;
    }

    // --- CRUD (staged: edit the list, write on Apply) -----------------------------

    private async Task AddAsync()
    {
        if (PromptAddUser is null) return;
        var cred = await PromptAddUser();
        if (cred is not { } c) return;
        if (_accounts.Any(a => string.Equals(a.Username, c.Username, StringComparison.Ordinal)))
        { OnError?.Invoke($"User {c.Username} already exists."); return; }
        _accounts.Add((c.Username, PasswordHasher.Hash(c.Password)));
        RefreshTable();
        if (_userTable is not null) _userTable.SelectedRowIndex = _accounts.Count - 1;
        UpdateButtonStates();
        _onDirty?.Invoke();
    }

    private async Task ResetSelectedAsync()
    {
        if (PromptResetPassword is null) return;
        var idx = _userTable?.SelectedRowIndex ?? -1;
        if (idx < 0 || idx >= _accounts.Count) return;
        var user = _accounts[idx].Username;
        var newPw = await PromptResetPassword(user);
        if (newPw is null) return;
        // Index may have shifted while the dialog was open; re-resolve by username.
        var cur = _accounts.FindIndex(a => string.Equals(a.Username, user, StringComparison.Ordinal));
        if (cur < 0) return;
        _accounts[cur] = (user, PasswordHasher.Hash(newPw));
        RefreshTable();
        if (_userTable is not null) _userTable.SelectedRowIndex = cur;
        _onDirty?.Invoke();
    }

    private async Task RemoveSelectedAsync()
    {
        var idx = _userTable?.SelectedRowIndex ?? -1;
        if (idx < 0 || idx >= _accounts.Count) return;
        var user = _accounts[idx].Username;
        if (ConfirmRemove is not null && !await ConfirmRemove(user)) return;
        var cur = _accounts.FindIndex(a => string.Equals(a.Username, user, StringComparison.Ordinal));
        if (cur < 0) return;
        _accounts.RemoveAt(cur);
        RefreshTable();
        _onDirty?.Invoke();
    }

    public bool IsDirty => CurrentRealm() != _lRealm || !_accounts.SequenceEqual(_lAccounts);

    public IReadOnlyList<PendingWrite> BuildPatch()
    {
        if (!IsDirty) return Array.Empty<PendingWrite>();
        if (_accounts.Count == 0)
        {
            OnError?.Invoke("At least one user is required; no basic-auth change written.");
            return Array.Empty<PendingWrite>();
        }
        var json = SecurityHandlerPatch.BasicAuth(CurrentRealm(), _accounts);
        return new[] { new PendingWrite(_path, json, _origNodeJson, "basic auth") };
    }

    public void Revert()
    {
        _realm = _lRealm;
        if (_realmPrompt is not null) _realmPrompt.Input = _lRealm;
        _accounts.Clear();
        _accounts.AddRange(_lAccounts);
        RefreshTable();
    }

    public bool HandleKey(ConsoleKeyInfo key)
    {
        // Ctrl+A adds a user (Ctrl-gated so it doesn't collide with typing in the realm field).
        if (key.Key == ConsoleKey.A && (key.Modifiers & ConsoleModifiers.Control) != 0)
        {
            _ = AddAsync();
            return true;
        }
        // Del on the focused user table removes the selected row (same as the button).
        if (key.Key == ConsoleKey.Delete && (_userTable?.HasFocus ?? false))
        {
            _ = RemoveSelectedAsync();
            return true;
        }
        return false;
    }
}
