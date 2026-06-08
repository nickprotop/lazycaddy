// -----------------------------------------------------------------------
// LazyCaddy - pure builders for individual handler JSON objects.
// Each returns the full handler node (including "handler": "<type>").
// -----------------------------------------------------------------------

using System.Text.Json;

namespace LazyCaddy.Services;

public static class HandlerPatch
{
    private static readonly JsonSerializerOptions Opt = new() { WriteIndented = true };

    public static string FileServer(string root, IEnumerable<string> indexNames,
        IEnumerable<string> hide, bool browse, bool passThru)
    {
        var o = new Dictionary<string, object> { ["handler"] = "file_server" };
        if (!string.IsNullOrWhiteSpace(root)) o["root"] = root;
        var idx = indexNames.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
        if (idx.Length > 0) o["index_names"] = idx;
        var hd = hide.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
        if (hd.Length > 0) o["hide"] = hd;
        if (browse) o["browse"] = new Dictionary<string, object>();
        if (passThru) o["pass_thru"] = true;
        return JsonSerializer.Serialize(o, Opt);
    }

    public static string StaticResponse(int statusCode, string body, bool close)
    {
        var o = new Dictionary<string, object> { ["handler"] = "static_response" };
        if (statusCode > 0) o["status_code"] = statusCode;
        if (!string.IsNullOrEmpty(body)) o["body"] = body;
        if (close) o["close"] = true;
        return JsonSerializer.Serialize(o, Opt);
    }

    public static string Error(string message, int statusCode)
    {
        var o = new Dictionary<string, object> { ["handler"] = "error" };
        if (statusCode > 0) o["status_code"] = statusCode;
        if (!string.IsNullOrEmpty(message)) o["error"] = message;
        return JsonSerializer.Serialize(o, Opt);
    }
}
