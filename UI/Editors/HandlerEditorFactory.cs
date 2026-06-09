// -----------------------------------------------------------------------
// LazyCaddy - the single home for "handler type → its editor tab(s)". A simple
// handler maps to one IConfigEditor; reverse_proxy/file_server expand into peer
// tabs. Used by the single-handler editor modal. Pure mapping, no I/O.
// -----------------------------------------------------------------------

namespace LazyCaddy.UI.Editors;

internal static class HandlerEditorFactory
{
    /// <summary>The editor tab(s) for a handler of `type` at node path `p`.</summary>
    public static IEnumerable<IConfigEditor> EditorsForType(string type, string p)
    {
        switch (type)
        {
            case "reverse_proxy":
                yield return new ReverseProxyEditor(p);
                yield return new LoadBalancingEditor(p);
                yield return new HealthChecksEditor(p);
                yield return new HttpTransportEditor(p);
                yield return new TlsConfigEditor($"{p}/transport/tls");
                yield return new KeepAliveEditor($"{p}/transport");
                yield return new HeadersEditor($"{p}/headers");
                break;
            case "file_server":
                yield return new FileServerEditor(p);
                yield return new BrowseEditor(p);
                break;
            case "static_response":
                yield return new StaticResponseEditor(p);
                break;
            case "error":
                yield return new ErrorEditor(p);
                break;
            case "rewrite":
                yield return new RewriteEditor(p);
                break;
            case "headers":
                yield return new HeadersEditor(p);
                break;
            case "encode":
                yield return new EncodeEditor(p);
                break;
            case "vars":
                yield return new VarsEditor(p);
                break;
            case "request_body":
                yield return new RequestBodyEditor(p);
                break;
            case "templates":
                yield return new TemplatesEditor(p);
                break;
            case "authentication":
                yield return new AuthenticationEditor(p);
                break;
            default:
                break;
        }
    }
}
