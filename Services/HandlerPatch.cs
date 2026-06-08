// -----------------------------------------------------------------------
// LazyCaddy - pure builders for individual handler JSON objects.
// Each returns the full handler node (including "handler": "<type>").
// -----------------------------------------------------------------------

using System.Text.Json;

namespace LazyCaddy.Services;

/// <summary>Header operations for one direction (request or response).</summary>
public sealed record HeaderOpsInput(
    IReadOnlyList<(string Name, string Value)> Add,
    IReadOnlyList<(string Name, string Value)> Set,
    IReadOnlyList<string> Delete);

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

    public static string Rewrite(string method, string uri, string stripPrefix, string stripSuffix)
    {
        var o = new Dictionary<string, object> { ["handler"] = "rewrite" };
        if (!string.IsNullOrWhiteSpace(method)) o["method"] = method;
        if (!string.IsNullOrWhiteSpace(uri)) o["uri"] = uri;
        if (!string.IsNullOrWhiteSpace(stripPrefix)) o["strip_path_prefix"] = stripPrefix;
        if (!string.IsNullOrWhiteSpace(stripSuffix)) o["strip_path_suffix"] = stripSuffix;
        return JsonSerializer.Serialize(o, Opt);
    }

    public static string Headers(HeaderOpsInput request, HeaderOpsInput response)
    {
        var o = new Dictionary<string, object> { ["handler"] = "headers" };
        var req = BuildHeaderOps(request);
        var resp = BuildHeaderOps(response);
        if (req is not null) o["request"] = req;
        if (resp is not null) o["response"] = resp;
        return JsonSerializer.Serialize(o, Opt);
    }

    private static Dictionary<string, object>? BuildHeaderOps(HeaderOpsInput ops)
    {
        var result = new Dictionary<string, object>();
        var add = ToHeaderMap(ops.Add);
        var set = ToHeaderMap(ops.Set);
        var del = ops.Delete.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
        if (add.Count > 0) result["add"] = add;
        if (set.Count > 0) result["set"] = set;
        if (del.Length > 0) result["delete"] = del;
        return result.Count > 0 ? result : null;
    }

    private static Dictionary<string, string[]> ToHeaderMap(IReadOnlyList<(string Name, string Value)> pairs)
    {
        var m = new Dictionary<string, string[]>();
        foreach (var (n, v) in pairs)
            if (!string.IsNullOrWhiteSpace(n)) m[n] = new[] { v };
        return m;
    }

    public static string Encode(bool gzip, bool zstd, int minimumLength)
    {
        var encodings = new Dictionary<string, object>();
        if (gzip) encodings["gzip"] = new Dictionary<string, object>();
        if (zstd) encodings["zstd"] = new Dictionary<string, object>();
        var o = new Dictionary<string, object> { ["handler"] = "encode" };
        if (encodings.Count > 0) o["encodings"] = encodings;
        if (minimumLength > 0) o["minimum_length"] = minimumLength;
        return JsonSerializer.Serialize(o, Opt);
    }

    public static string Vars(IEnumerable<(string Key, string Value)> entries)
    {
        var o = new Dictionary<string, object> { ["handler"] = "vars" };
        foreach (var (k, v) in entries)
            if (!string.IsNullOrWhiteSpace(k)) o[k] = v;
        return JsonSerializer.Serialize(o, Opt);
    }

    public static string RequestBody(long maxSize)
    {
        var o = new Dictionary<string, object> { ["handler"] = "request_body" };
        if (maxSize > 0) o["max_size"] = maxSize;
        return JsonSerializer.Serialize(o, Opt);
    }
}
