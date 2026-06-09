// -----------------------------------------------------------------------
// LazyCaddy - pure JSON fragment builders for the security mechanisms. No I/O,
// no UI; editors call these to produce the node/handler/route JSON they write.
// Shapes verified against Caddy v2 docs/source (see the design spec).
// -----------------------------------------------------------------------

using System.Text.Json;

namespace LazyCaddy.Services;

public enum ForwardAuthProvider { Authelia, Authentik, Custom }

public static class SecurityHandlerPatch
{
    private static readonly JsonSerializerOptions Opt = new() { WriteIndented = true };
    private static string Ser(object o) => JsonSerializer.Serialize(o, Opt);

    public static string BasicAuth(string realm, IEnumerable<(string Username, string PasswordHash)> accounts)
    {
        var accs = accounts.Select(a => new Dictionary<string, object>
        { ["username"] = a.Username, ["password"] = a.PasswordHash }).ToList();
        return Ser(new Dictionary<string, object>
        {
            ["handler"] = "authentication",
            ["providers"] = new Dictionary<string, object>
            {
                ["http_basic"] = new Dictionary<string, object>
                {
                    ["hash"] = new Dictionary<string, object> { ["algorithm"] = "bcrypt" },
                    ["realm"] = realm,
                    ["accounts"] = accs,
                },
            },
        });
    }

    public static string SecurityHeaders(string? hsts, bool noSniff, string? frameOptions, string? referrerPolicy, string? csp)
    {
        var set = new Dictionary<string, object>();
        if (!string.IsNullOrEmpty(hsts)) set["Strict-Transport-Security"] = new[] { hsts };
        if (noSniff) set["X-Content-Type-Options"] = new[] { "nosniff" };
        if (!string.IsNullOrEmpty(frameOptions)) set["X-Frame-Options"] = new[] { frameOptions };
        if (!string.IsNullOrEmpty(referrerPolicy)) set["Referrer-Policy"] = new[] { referrerPolicy };
        if (!string.IsNullOrEmpty(csp)) set["Content-Security-Policy"] = new[] { csp };
        return Ser(new Dictionary<string, object>
        {
            ["handler"] = "headers",
            ["response"] = new Dictionary<string, object> { ["set"] = set },
        });
    }

    public static string RateLimit(string zone, string key, string window, int maxEvents)
        => Ser(new Dictionary<string, object>
        {
            ["handler"] = "rate_limit",
            ["rate_limits"] = new Dictionary<string, object>
            { [zone] = new Dictionary<string, object> { ["key"] = key, ["window"] = window, ["max_events"] = maxEvents } },
        });

    public static string ForwardAuth(ForwardAuthProvider provider, string upstream)
    {
        var (uri, copied) = provider switch
        {
            ForwardAuthProvider.Authelia => ("/api/authz/forward-auth",
                new[] { "Remote-User", "Remote-Groups", "Remote-Email", "Remote-Name" }),
            ForwardAuthProvider.Authentik => ("/outpost.goauthentik.io/auth/caddy",
                new[] { "X-authentik-username", "X-authentik-groups", "X-authentik-email", "X-authentik-uid" }),
            _ => ("/", System.Array.Empty<string>()),
        };
        var copyHeaders = new Dictionary<string, object>();
        foreach (var h in copied) copyHeaders[h] = new[] { $"{{http.reverse_proxy.header.{h}}}" };

        return Ser(new Dictionary<string, object>
        {
            ["handler"] = "reverse_proxy",
            ["upstreams"] = new object[] { new Dictionary<string, object> { ["dial"] = upstream } },
            ["rewrite"] = new Dictionary<string, object> { ["method"] = "GET", ["uri"] = uri },
            ["headers"] = new Dictionary<string, object>
            {
                ["request"] = new Dictionary<string, object>
                {
                    ["set"] = new Dictionary<string, object>
                    {
                        ["X-Forwarded-Method"] = new[] { "{http.request.method}" },
                        ["X-Forwarded-Uri"] = new[] { "{http.request.uri}" },
                    },
                },
            },
            ["handle_response"] = new object[]
            {
                new Dictionary<string, object>
                {
                    ["match"] = new Dictionary<string, object> { ["status_code"] = new[] { 2 } },
                    ["routes"] = new object[]
                    {
                        new Dictionary<string, object>
                        {
                            ["handle"] = new object[]
                            {
                                new Dictionary<string, object>
                                {
                                    ["handler"] = "headers",
                                    ["request"] = new Dictionary<string, object> { ["set"] = copyHeaders },
                                },
                            },
                        },
                    },
                },
            },
        });
    }

    public static string IpMatcher(bool clientIp, IEnumerable<string> ranges)
        => Ser(new Dictionary<string, object>
        { [clientIp ? "client_ip" : "remote_ip"] = new Dictionary<string, object> { ["ranges"] = ranges.ToArray() } });

    public static string DenyRoute(bool clientIp, IEnumerable<string> ranges)
        => Ser(new Dictionary<string, object>
        {
            ["match"] = new object[]
            { new Dictionary<string, object> { [clientIp ? "client_ip" : "remote_ip"] = new Dictionary<string, object> { ["ranges"] = ranges.ToArray() } } },
            ["handle"] = new object[]
            { new Dictionary<string, object> { ["handler"] = "static_response", ["status_code"] = 403, ["body"] = "Forbidden" } },
            ["terminal"] = true,
        });

    public static string TlsPolicy(string min, string max)
        => Ser(new Dictionary<string, object> { ["protocol_min"] = min, ["protocol_max"] = max });
}
