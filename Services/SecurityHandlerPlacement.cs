// -----------------------------------------------------------------------
// LazyCaddy - pure: where to insert a security handler (auth/headers) in a route's
// handle[] so it runs BEFORE the terminal handler (reverse_proxy/file_server/
// static_response/subroute) and therefore actually protects the route. Returns the
// index to insert at; appends (Count) when there's no terminal handler. Non-array
// or empty → 0. No I/O.
// -----------------------------------------------------------------------

using System.Text.Json;

namespace LazyCaddy.Services;

public static class SecurityHandlerPlacement
{
    private static readonly HashSet<string> Terminal = new()
    { "reverse_proxy", "file_server", "static_response", "subroute" };

    public static int InsertIndex(string handleArrayJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(handleArrayJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return 0;
            int i = 0;
            foreach (var h in doc.RootElement.EnumerateArray())
            {
                var type = h.ValueKind == JsonValueKind.Object &&
                           h.TryGetProperty("handler", out var t) && t.ValueKind == JsonValueKind.String
                    ? t.GetString() : null;
                if (type is not null && Terminal.Contains(type)) return i;
                i++;
            }
            return i;
        }
        catch { return 0; }
    }
}
