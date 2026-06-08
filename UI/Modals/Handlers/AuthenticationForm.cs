using SharpConsoleUI;
using LazyCaddy.Services;
using LazyCaddy.UI.Modals;

namespace LazyCaddy.UI.Modals.Handlers;

/// <summary>Authentication providers are polymorphic and credential config is security-
/// sensitive (password hashing is provider-specific); edit the providers node as raw JSON.</summary>
public static class AuthenticationForm
{
    public static Task<bool> ShowAsync(ConsoleWindowSystem ws, string path, EditCoordinator editor, Window? parent = null)
        => RawNodeEditDialog.ShowAsync(ws, "Edit authentication (providers)", $"{path}/providers", editor, parent);
}
