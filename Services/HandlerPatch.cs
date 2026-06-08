// -----------------------------------------------------------------------
// LazyCaddy - pure builders for individual handler JSON objects.
// Each returns the full handler node (including "handler": "<type>").
// -----------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Nodes;

namespace LazyCaddy.Services;

/// <summary>Header operations for one direction (request or response).</summary>
public sealed record HeaderOpsInput(
    IReadOnlyList<(string Name, string Value)> Add,
    IReadOnlyList<(string Name, string Value)> Set,
    IReadOnlyList<string> Delete);

public sealed record ActiveHealthCheckInput(string Uri, int Port, string Method, string Interval,
    string Timeout, int Passes, int Fails, int ExpectStatus, string ExpectBody);

public sealed record PassiveHealthCheckInput(string FailDuration, int MaxFails,
    int UnhealthyRequestCount, IReadOnlyList<int> UnhealthyStatus, string UnhealthyLatency);

public sealed record HttpTransportInput(
    bool Compression, int MaxConnsPerHost, string DialTimeout, string DialFallbackDelay,
    string ResponseHeaderTimeout, string ExpectContinueTimeout, string ReadTimeout, string WriteTimeout,
    int MaxResponseHeaderSize, int ReadBufferSize, int WriteBufferSize,
    IReadOnlyList<string> Versions, string LocalAddress, string ProxyProtocol,
    IReadOnlyList<string> ResolverAddresses);

public sealed record TlsConfigInput(
    bool InsecureSkipVerify, string ServerName, string Renegotiation, string HandshakeTimeout,
    IReadOnlyList<string> Curves, IReadOnlyList<string> ExceptPorts);

public sealed record KeepAliveInput(
    bool EnabledSet, bool Enabled, string IdleTimeout, string ProbeInterval,
    int MaxIdleConns, int MaxIdleConnsPerHost);

public sealed record BrowseInput(
    string TemplateFile, bool RevealSymlinks, IReadOnlyList<string> Sort, int FileLimit);

public sealed record FileServerInput(
    string Root, IReadOnlyList<string> IndexNames, IReadOnlyList<string> Hide, bool PassThru,
    IReadOnlyList<string> PrecompressedOrder, string StatusCode, bool CanonicalUrisSet, bool CanonicalUris);

public static class HandlerPatch
{
    private static readonly JsonSerializerOptions Opt = new() { WriteIndented = true };

    public static readonly IReadOnlySet<string> ManagedFileServerKeys = new HashSet<string>
    {
        "handler", "root", "index_names", "hide", "pass_thru",
        "precompressed_order", "status_code", "canonical_uris"
    };

    public static string FileServer(FileServerInput x)
    {
        var o = new Dictionary<string, object> { ["handler"] = "file_server" };
        if (!string.IsNullOrWhiteSpace(x.Root)) o["root"] = x.Root;
        var idx = x.IndexNames.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
        if (idx.Length > 0) o["index_names"] = idx;
        var hd = x.Hide.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
        if (hd.Length > 0) o["hide"] = hd;
        if (x.PassThru) o["pass_thru"] = true;
        var pre = x.PrecompressedOrder.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
        if (pre.Length > 0) o["precompressed_order"] = pre;
        if (!string.IsNullOrWhiteSpace(x.StatusCode)) o["status_code"] = x.StatusCode;
        if (x.CanonicalUrisSet) o["canonical_uris"] = x.CanonicalUris;
        return JsonSerializer.Serialize(o, Opt);
    }

    public static string Browse(BrowseInput x)
    {
        var o = new Dictionary<string, object>();
        if (!string.IsNullOrWhiteSpace(x.TemplateFile)) o["template_file"] = x.TemplateFile;
        if (x.RevealSymlinks) o["reveal_symlinks"] = true;
        var sort = x.Sort.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
        if (sort.Length > 0) o["sort"] = sort;
        if (x.FileLimit > 0) o["file_limit"] = x.FileLimit;
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

    public static string Templates(string fileRoot, IEnumerable<string> mimeTypes)
    {
        var o = new Dictionary<string, object> { ["handler"] = "templates" };
        if (!string.IsNullOrWhiteSpace(fileRoot)) o["file_root"] = fileRoot;
        var mt = mimeTypes.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
        if (mt.Length > 0) o["mime_types"] = mt;
        return JsonSerializer.Serialize(o, Opt);
    }

    public static string ReverseProxy(IEnumerable<string> dials, int flushInterval)
    {
        var o = new Dictionary<string, object> { ["handler"] = "reverse_proxy" };
        var ups = dials.Where(s => !string.IsNullOrWhiteSpace(s)).Select(d => new { dial = d }).ToArray();
        if (ups.Length > 0) o["upstreams"] = ups;
        if (flushInterval != 0) o["flush_interval"] = flushInterval;
        return JsonSerializer.Serialize(o, Opt);
    }

    public static string LoadBalancing(string policy, string policyParam, int retries,
        string tryDuration, string tryInterval)
    {
        var o = new Dictionary<string, object>();
        if (!string.IsNullOrWhiteSpace(policy))
        {
            var sp = new Dictionary<string, object> { ["policy"] = policy };
            // Single-param policies: map the param to the right key.
            switch (policy)
            {
                case "header": if (!string.IsNullOrWhiteSpace(policyParam)) sp["field"] = policyParam; break;
                case "cookie": if (!string.IsNullOrWhiteSpace(policyParam)) sp["name"] = policyParam; break;
                case "query":  if (!string.IsNullOrWhiteSpace(policyParam)) sp["key"] = policyParam; break;
                case "random_choose":
                    if (int.TryParse(policyParam, out var n) && n > 0) sp["choose"] = n; break;
                case "weighted_round_robin":
                    var weights = policyParam.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => int.TryParse(s, out var w) ? w : 0).Where(w => w > 0).ToArray();
                    if (weights.Length > 0) sp["weights"] = weights; break;
            }
            o["selection_policy"] = sp;
        }
        if (retries > 0) o["retries"] = retries;
        if (!string.IsNullOrWhiteSpace(tryDuration)) o["try_duration"] = tryDuration;
        if (!string.IsNullOrWhiteSpace(tryInterval)) o["try_interval"] = tryInterval;
        return JsonSerializer.Serialize(o, Opt);
    }

    public static string HealthChecks(ActiveHealthCheckInput active, PassiveHealthCheckInput passive)
    {
        var o = new Dictionary<string, object>();
        var a = BuildActive(active);
        var p = BuildPassive(passive);
        if (a is not null) o["active"] = a;
        if (p is not null) o["passive"] = p;
        return JsonSerializer.Serialize(o, Opt);
    }

    private static Dictionary<string, object>? BuildActive(ActiveHealthCheckInput x)
    {
        var a = new Dictionary<string, object>();
        if (!string.IsNullOrWhiteSpace(x.Uri)) a["uri"] = x.Uri;
        if (x.Port > 0) a["port"] = x.Port;
        if (!string.IsNullOrWhiteSpace(x.Method)) a["method"] = x.Method;
        if (!string.IsNullOrWhiteSpace(x.Interval)) a["interval"] = x.Interval;
        if (!string.IsNullOrWhiteSpace(x.Timeout)) a["timeout"] = x.Timeout;
        if (x.Passes > 0) a["passes"] = x.Passes;
        if (x.Fails > 0) a["fails"] = x.Fails;
        if (x.ExpectStatus > 0) a["expect_status"] = x.ExpectStatus;
        if (!string.IsNullOrWhiteSpace(x.ExpectBody)) a["expect_body"] = x.ExpectBody;
        return a.Count > 0 ? a : null;
    }

    private static Dictionary<string, object>? BuildPassive(PassiveHealthCheckInput x)
    {
        var p = new Dictionary<string, object>();
        if (!string.IsNullOrWhiteSpace(x.FailDuration)) p["fail_duration"] = x.FailDuration;
        if (x.MaxFails > 0) p["max_fails"] = x.MaxFails;
        if (x.UnhealthyRequestCount > 0) p["unhealthy_request_count"] = x.UnhealthyRequestCount;
        if (x.UnhealthyStatus.Count > 0) p["unhealthy_status"] = x.UnhealthyStatus.ToArray();
        if (!string.IsNullOrWhiteSpace(x.UnhealthyLatency)) p["unhealthy_latency"] = x.UnhealthyLatency;
        return p.Count > 0 ? p : null;
    }

    public static string HttpTransport(HttpTransportInput x)
    {
        // protocol is fixed to "http" — these fields belong only to the HTTP transport.
        var o = new Dictionary<string, object> { ["protocol"] = "http" };
        if (x.Compression) o["compression"] = true;
        if (x.MaxConnsPerHost > 0) o["max_conns_per_host"] = x.MaxConnsPerHost;
        if (!string.IsNullOrWhiteSpace(x.DialTimeout)) o["dial_timeout"] = x.DialTimeout;
        if (!string.IsNullOrWhiteSpace(x.DialFallbackDelay)) o["dial_fallback_delay"] = x.DialFallbackDelay;
        if (!string.IsNullOrWhiteSpace(x.ResponseHeaderTimeout)) o["response_header_timeout"] = x.ResponseHeaderTimeout;
        if (!string.IsNullOrWhiteSpace(x.ExpectContinueTimeout)) o["expect_continue_timeout"] = x.ExpectContinueTimeout;
        if (!string.IsNullOrWhiteSpace(x.ReadTimeout)) o["read_timeout"] = x.ReadTimeout;
        if (!string.IsNullOrWhiteSpace(x.WriteTimeout)) o["write_timeout"] = x.WriteTimeout;
        if (x.MaxResponseHeaderSize > 0) o["max_response_header_size"] = x.MaxResponseHeaderSize;
        if (x.ReadBufferSize > 0) o["read_buffer_size"] = x.ReadBufferSize;
        if (x.WriteBufferSize > 0) o["write_buffer_size"] = x.WriteBufferSize;
        var versions = x.Versions.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
        if (versions.Length > 0) o["versions"] = versions;
        if (!string.IsNullOrWhiteSpace(x.LocalAddress)) o["local_address"] = x.LocalAddress;
        if (!string.IsNullOrWhiteSpace(x.ProxyProtocol)) o["proxy_protocol"] = x.ProxyProtocol;
        var addrs = x.ResolverAddresses.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
        if (addrs.Length > 0) o["resolver"] = new Dictionary<string, object> { ["addresses"] = addrs };
        return JsonSerializer.Serialize(o, Opt);
    }

    /// <summary>Top-level keys the HttpTransport builder can emit; everything else is preserved on merge.</summary>
    private static readonly HashSet<string> ManagedTransportKeys = new(StringComparer.Ordinal)
    {
        "protocol", "compression", "max_conns_per_host", "dial_timeout", "dial_fallback_delay",
        "response_header_timeout", "expect_continue_timeout", "read_timeout", "write_timeout",
        "max_response_header_size", "read_buffer_size", "write_buffer_size", "versions",
        "local_address", "proxy_protocol", "resolver",
    };

    /// <summary>Top-level keys the TlsConfig builder can emit; everything else is preserved on merge.</summary>
    private static readonly HashSet<string> ManagedTlsKeys = new(StringComparer.Ordinal)
    {
        "insecure_skip_verify", "server_name", "renegotiation", "handshake_timeout", "curves", "except_ports",
    };

    /// <summary>
    /// Merge freshly-built managed JSON over the original node, preserving any top-level key the
    /// builder does not manage. Caddy PATCH replaces an object node wholesale, so unmanaged keys
    /// (polymorphic blocks, advanced fields left to raw edit) must be carried forward explicitly.
    /// Managed keys take their value from <paramref name="managedJson"/> (so a field cleared in the
    /// form is dropped, not resurrected). Falls back to managedJson when original is empty/absent/
    /// non-object/unparseable.
    /// </summary>
    public static string MergeUnmanaged(string originalJson, string managedJson, IReadOnlySet<string> managedKeys)
    {
        if (string.IsNullOrWhiteSpace(originalJson)) return managedJson;
        JsonNode? originalNode;
        try { originalNode = JsonNode.Parse(originalJson); }
        catch (JsonException) { return managedJson; }
        if (originalNode is not JsonObject original) return managedJson;
        if (JsonNode.Parse(managedJson) is not JsonObject managed) return managedJson;

        foreach (var kvp in original)
        {
            if (managedKeys.Contains(kvp.Key)) continue;
            managed[kvp.Key] = kvp.Value?.DeepClone();
        }
        return JsonSerializer.Serialize(managed, Opt);
    }

    /// <summary>
    /// Merge freshly-built managed transport JSON over the original transport node, preserving
    /// any keys the builder does not manage (tls, keep_alive, network_proxy, etc). Caddy PATCH
    /// replaces an object node wholesale, so we must carry unmanaged sub-nodes forward ourselves.
    /// Managed keys reflect the user's current form state (cleared fields are intentionally omitted);
    /// only keys absent from the managed key set are copied verbatim from the original.
    /// </summary>
    public static string MergeTransport(string originalJson, string managedJson)
        => MergeUnmanaged(originalJson, managedJson, ManagedTransportKeys);

    /// <summary>
    /// Merge freshly-built managed tls JSON over the original tls node, preserving unmanaged keys
    /// (the polymorphic `ca` block, client-certificate fields left to raw edit, etc).
    /// </summary>
    public static string MergeTlsConfig(string originalJson, string managedJson)
        => MergeUnmanaged(originalJson, managedJson, ManagedTlsKeys);

    public static string TlsConfig(TlsConfigInput x)
    {
        var o = new Dictionary<string, object>();
        if (x.InsecureSkipVerify) o["insecure_skip_verify"] = true;
        if (!string.IsNullOrWhiteSpace(x.ServerName)) o["server_name"] = x.ServerName;
        if (!string.IsNullOrWhiteSpace(x.Renegotiation)) o["renegotiation"] = x.Renegotiation;
        if (!string.IsNullOrWhiteSpace(x.HandshakeTimeout)) o["handshake_timeout"] = x.HandshakeTimeout;
        var curves = x.Curves.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
        if (curves.Length > 0) o["curves"] = curves;
        var ports = x.ExceptPorts.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
        if (ports.Length > 0) o["except_ports"] = ports;
        return JsonSerializer.Serialize(o, Opt);
    }

    public static string KeepAlive(KeepAliveInput x)
    {
        var o = new Dictionary<string, object>();
        if (x.EnabledSet) o["enabled"] = x.Enabled;   // false is meaningful → emit when explicitly set
        if (!string.IsNullOrWhiteSpace(x.IdleTimeout)) o["idle_timeout"] = x.IdleTimeout;
        if (!string.IsNullOrWhiteSpace(x.ProbeInterval)) o["probe_interval"] = x.ProbeInterval;
        if (x.MaxIdleConns > 0) o["max_idle_conns"] = x.MaxIdleConns;
        if (x.MaxIdleConnsPerHost > 0) o["max_idle_conns_per_host"] = x.MaxIdleConnsPerHost;
        return JsonSerializer.Serialize(o, Opt);
    }
}
