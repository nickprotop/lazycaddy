using SharpConsoleUI;
using LazyCaddy.Services;
using LazyCaddy.UI.Modals.Handlers;

namespace LazyCaddy.UI.Modals;

/// <summary>
/// Maps a handler `type` to its structured edit form, opened against a handler config path.
/// Shared by the route editor (edit existing) and the new-route / add-handler flows (edit a
/// just-created handler). Unknown types fall back to the raw node editor.
/// </summary>
public static class HandlerFormDispatch
{
    public static Task<bool> OpenAsync(ConsoleWindowSystem ws, string type, string handlerPath,
        EditCoordinator editor, Window parent) => type switch
    {
        "file_server"     => FileServerForm.ShowAsync(ws, handlerPath, editor, parent),
        "static_response" => StaticResponseForm.ShowAsync(ws, handlerPath, editor, parent),
        "error"           => ErrorForm.ShowAsync(ws, handlerPath, editor, parent),
        "rewrite"         => RewriteForm.ShowAsync(ws, handlerPath, editor, parent),
        "headers"         => HeadersForm.ShowAsync(ws, handlerPath, editor, parent),
        "encode"          => EncodeForm.ShowAsync(ws, handlerPath, editor, parent),
        "vars"            => VarsForm.ShowAsync(ws, handlerPath, editor, parent),
        "request_body"    => RequestBodyForm.ShowAsync(ws, handlerPath, editor, parent),
        "reverse_proxy"   => ReverseProxyForm.ShowAsync(ws, handlerPath, editor, parent),
        "templates"       => TemplatesForm.ShowAsync(ws, handlerPath, editor, parent),
        "authentication"  => AuthenticationForm.ShowAsync(ws, handlerPath, editor, parent),
        _                 => RawNodeEditDialog.ShowAsync(ws, $"Edit {type}", handlerPath, editor, parent),
    };
}
