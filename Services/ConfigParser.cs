// -----------------------------------------------------------------------
// LazyCaddy - pure parser for the Caddy running config (GET /config/).
// Walks apps.http.servers.*.routes[] and apps.tls.automation.policies[].
// -----------------------------------------------------------------------

using System.Text.Json;
using LazyCaddy.Models;

namespace LazyCaddy.Services;

public static class ConfigParser
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>Flatten every server's routes into host -> upstream DTOs.</summary>
    public static IReadOnlyList<Route> ParseRoutes(string configJson)
    {
        var routes = new List<Route>();
        using var doc = JsonDocument.Parse(configJson);
        var root = doc.RootElement;

        if (!TryGetPath(root, out var servers, "apps", "http", "servers"))
            return routes;

        var tlsHosts = TlsHostSet(root);

        foreach (var server in servers.EnumerateObject())
        {
            if (!server.Value.TryGetProperty("routes", out var routeArr) ||
                routeArr.ValueKind != JsonValueKind.Array)
                continue;

            int idx = 0;
            foreach (var route in routeArr.EnumerateArray())
            {
                var path = $"apps/http/servers/{server.Name}/routes/{idx}";
                var host = ExtractMatchSummary(route);
                var upstream = ExtractUpstream(route);
                bool tls = host.Split(", ").Any(h => tlsHosts.Contains(h));
                string listen = JoinArray(server.Value, "listen");
                routes.Add(new Route(
                    HostOrMatch: host,
                    Upstream: upstream,
                    TlsEnabled: tls,
                    Status: "active",
                    RawConfigJson: Pretty(route),
                    ConfigPath: path,
                    ServerName: server.Name,
                    Listen: listen));
                idx++;
            }
        }
        return routes;
    }

    private static string ExtractMatchSummary(JsonElement route)
    {
        if (!route.TryGetProperty("match", out var matchArr) || matchArr.ValueKind != JsonValueKind.Array)
            return "(any)";

        var hosts = new List<string>();
        var paths = new List<string>();
        foreach (var m in matchArr.EnumerateArray())
        {
            if (m.TryGetProperty("host", out var h) && h.ValueKind == JsonValueKind.Array)
                hosts.AddRange(h.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!));
            if (m.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.Array)
                paths.AddRange(p.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!));
        }
        var parts = new List<string>();
        if (hosts.Count > 0) parts.Add(string.Join(", ", hosts));
        if (paths.Count > 0) parts.Add(string.Join(", ", paths));
        return parts.Count > 0 ? string.Join(" ", parts) : "(any)";
    }

    /// <summary>Find the first reverse_proxy dial, recursing through subroute handlers.</summary>
    private static string ExtractUpstream(JsonElement node)
    {
        if (node.TryGetProperty("handle", out var handlers) && handlers.ValueKind == JsonValueKind.Array)
        {
            foreach (var h in handlers.EnumerateArray())
            {
                var handler = h.TryGetProperty("handler", out var hn) && hn.ValueKind == JsonValueKind.String
                    ? hn.GetString() : null;

                if (handler == "reverse_proxy" &&
                    h.TryGetProperty("upstreams", out var ups) && ups.ValueKind == JsonValueKind.Array)
                {
                    var dials = ups.EnumerateArray()
                        .Where(u => u.TryGetProperty("dial", out _))
                        .Select(u => u.GetProperty("dial").GetString())
                        .Where(d => d is not null)
                        .ToList();
                    if (dials.Count > 0) return string.Join(", ", dials);
                }

                if (handler == "file_server") return "file_server";
                if (handler == "static_response") return "static_response";

                if (handler == "subroute" &&
                    h.TryGetProperty("routes", out var subRoutes) && subRoutes.ValueKind == JsonValueKind.Array)
                {
                    foreach (var sr in subRoutes.EnumerateArray())
                    {
                        var inner = ExtractUpstream(sr);
                        if (inner != "(none)") return inner;
                    }
                }
            }
        }
        return "(none)";
    }

    /// <summary>
    /// Extract managed certificates from tls.automation.policies. Expiry is not in the
    /// config; default to a far date and let a later pass enrich from real cert metadata.
    /// </summary>
    /// <summary>
    /// Every http server in the config, with its listen addresses. Used where the user has to
    /// pick a target server (adding a route): the bare name "srv0" means nothing to them, but
    /// "srv0 — :8443" does, and several servers can share a hostname on different ports.
    /// </summary>
    public static IReadOnlyList<ServerInfo> ParseServers(string configJson)
    {
        var result = new List<ServerInfo>();
        using var doc = JsonDocument.Parse(configJson);
        if (!TryGetPath(doc.RootElement, out var servers, "apps", "http", "servers"))
            return result;

        foreach (var server in servers.EnumerateObject())
            result.Add(new ServerInfo(server.Name, JoinArray(server.Value, "listen")));

        return result;
    }

    public static IReadOnlyList<Cert> ParseCerts(string configJson)
    {
        var certs = new List<Cert>();
        using var doc = JsonDocument.Parse(configJson);
        if (!TryGetPath(doc.RootElement, out var policies, "apps", "tls", "automation", "policies") ||
            policies.ValueKind != JsonValueKind.Array)
            return certs;

        foreach (var pol in policies.EnumerateArray())
        {
            var issuer = IssuerLabel(pol);
            if (!pol.TryGetProperty("subjects", out var subs) || subs.ValueKind != JsonValueKind.Array)
                continue;
            foreach (var s in subs.EnumerateArray())
            {
                if (s.ValueKind != JsonValueKind.String) continue;
                // Expiry is not in the admin API; CaddyAdminClient enriches it from the on-disk
                // cert file afterward. Until/unless that succeeds, mark expiry unknown so the UI
                // never shows a fake "90 days" or a false "all healthy".
                certs.Add(new Cert(
                    Domain: s.GetString()!,
                    Issuer: issuer,
                    Expires: default,
                    AcmeStatus: "managed",
                    ExpiryKnown: false));
            }
        }
        return certs;
    }

    private static string IssuerLabel(JsonElement policy)
    {
        if (policy.TryGetProperty("issuers", out var issuers) && issuers.ValueKind == JsonValueKind.Array)
        {
            foreach (var iss in issuers.EnumerateArray())
            {
                var module = iss.TryGetProperty("module", out var m) && m.ValueKind == JsonValueKind.String
                    ? m.GetString() : null;
                if (module == "acme")
                {
                    if (iss.TryGetProperty("challenges", out var ch) &&
                        ch.TryGetProperty("dns", out var dns) &&
                        dns.TryGetProperty("provider", out var prov) &&
                        prov.TryGetProperty("name", out var pn) && pn.ValueKind == JsonValueKind.String)
                        return $"ACME ({pn.GetString()})";
                    return "ACME";
                }
                if (module is not null) return module;
            }
        }
        return "unknown";
    }

    private static HashSet<string> TlsHostSet(JsonElement root)
    {
        var set = new HashSet<string>();
        if (TryGetPath(root, out var policies, "apps", "tls", "automation", "policies") &&
            policies.ValueKind == JsonValueKind.Array)
        {
            foreach (var pol in policies.EnumerateArray())
                if (pol.TryGetProperty("subjects", out var subs) && subs.ValueKind == JsonValueKind.Array)
                    foreach (var s in subs.EnumerateArray())
                        if (s.ValueKind == JsonValueKind.String) set.Add(s.GetString()!);
        }
        return set;
    }

    private static bool TryGetPath(JsonElement root, out JsonElement found, params string[] path)
    {
        var cur = root;
        foreach (var key in path)
        {
            if (cur.ValueKind != JsonValueKind.Object || !cur.TryGetProperty(key, out cur))
            {
                found = default;
                return false;
            }
        }
        found = cur;
        return true;
    }

    private static string Pretty(JsonElement el) => JsonSerializer.Serialize(el, Indented);

    private static string JoinArray(JsonElement obj, string name) =>
        string.Join(", ", ArrayValues(obj, name));

    private static List<string> ArrayValues(JsonElement obj, string name)
    {
        var list = new List<string>();
        if (obj.TryGetProperty(name, out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var e in arr.EnumerateArray())
                if (e.ValueKind == JsonValueKind.String) list.Add(e.GetString()!);
        return list;
    }
}
