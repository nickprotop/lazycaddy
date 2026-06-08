using System.Text.Json;

namespace LazyCaddy.Services;

/// <summary>Pure builders for the structured server-level config fields the Server view edits.</summary>
public static class ServerConfigPatch
{
    private static readonly JsonSerializerOptions Opt = new() { WriteIndented = true };

    /// <summary>The `protocols` array — the checked subset, in h1,h2,h3 order.</summary>
    public static string ProtocolsArray(bool h1, bool h2, bool h3)
    {
        var list = new List<string>();
        if (h1) list.Add("h1");
        if (h2) list.Add("h2");
        if (h3) list.Add("h3");
        return JsonSerializer.Serialize(list, Opt);
    }

    /// <summary>The `automatic_https` object — only the true flags + non-empty skip.</summary>
    public static string AutomaticHttps(bool disable, bool disableRedirects, bool disableCerts, IReadOnlyList<string> skip)
    {
        var o = new Dictionary<string, object>();
        if (disable) o["disable"] = true;
        if (disableRedirects) o["disable_redirects"] = true;
        if (disableCerts) o["disable_certificates"] = true;
        var sk = skip.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
        if (sk.Length > 0) o["skip"] = sk;
        return JsonSerializer.Serialize(o, Opt);
    }

    /// <summary>The whole `admin` object — used when admin is null/absent (writing admin/listen would 404).</summary>
    public static string AdminObject(string listen) =>
        JsonSerializer.Serialize(new Dictionary<string, object> { ["listen"] = listen }, Opt);

    /// <summary>A JSON string array from inputs, filtering empty/whitespace entries.</summary>
    public static string StringArray(IEnumerable<string> items) =>
        JsonSerializer.Serialize(items.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray(), Opt);

    /// <summary>A log `writer` object — file → output+filename; stdout/stderr/discard → output only.</summary>
    public static string LogWriter(string output, string filename)
    {
        var o = new Dictionary<string, object> { ["output"] = output };
        if (output == "file" && !string.IsNullOrWhiteSpace(filename)) o["filename"] = filename;
        return JsonSerializer.Serialize(o, Opt);
    }

    /// <summary>A logging.logs entry: level/include/exclude/writer, omitting empties. writerJson "" → no writer.</summary>
    public static string LogNode(string level, IReadOnlyList<string> include, IReadOnlyList<string> exclude, string writerJson)
    {
        var o = new Dictionary<string, object>();
        if (!string.IsNullOrWhiteSpace(level)) o["level"] = level;
        var inc = include.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
        if (inc.Length > 0) o["include"] = inc;
        var exc = exclude.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
        if (exc.Length > 0) o["exclude"] = exc;
        if (!string.IsNullOrWhiteSpace(writerJson))
        {
            using var w = JsonDocument.Parse(writerJson);
            o["writer"] = w.RootElement.Clone();
        }
        return JsonSerializer.Serialize(o, Opt);
    }
}
