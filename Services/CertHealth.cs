// -----------------------------------------------------------------------
// LazyCaddy - pure cert-expiry bucketing. No HTTP/UI: takes the parsed cert
// list + "now" and returns counts by urgency, plus the certs sorted soonest-
// expiry-first. Drives the Certs view banner/sort and the Overview alert.
// -----------------------------------------------------------------------

using LazyCaddy.Models;

namespace LazyCaddy.Services;

/// <summary>Cert counts bucketed by days-until-expiry urgency. Buckets are disjoint:
/// Expired (&lt;0); Critical = 0..&lt;14; Warning = 14..&lt;30; Ok = ≥30; Unknown = expiry not readable.</summary>
public readonly record struct CertHealth(int Total, int Expired, int Critical, int Warning, int Ok, int Unknown)
{
    /// <summary>True if anything needs attention (expired, or expiring within 30 days).</summary>
    public bool HasAlert => Expired > 0 || Critical > 0 || Warning > 0;
    /// <summary>True if anything is expired or critically close (&lt;14 days).</summary>
    public bool HasCritical => Expired > 0 || Critical > 0;
}

public static class CertExpiry
{
    public const int CriticalDays = 14;
    public const int WarningDays = 30;

    /// <summary>Bucket certs by urgency relative to <paramref name="now"/>. Certs whose expiry
    /// couldn't be read go to Unknown (never counted as healthy or alerting).</summary>
    public static CertHealth Summarize(IReadOnlyList<Cert> certs, DateTimeOffset now)
    {
        int expired = 0, critical = 0, warning = 0, ok = 0, unknown = 0;
        foreach (var c in certs)
        {
            if (!c.ExpiryKnown) { unknown++; continue; }
            var d = c.DaysLeft(now);
            if (d < 0) expired++;
            else if (d < CriticalDays) critical++;
            else if (d < WarningDays) warning++;
            else ok++;
        }
        return new CertHealth(certs.Count, expired, critical, warning, ok, unknown);
    }

    /// <summary>Certs ordered most-urgent first: known expiries soonest-first, then unknown-expiry
    /// certs last (ties by domain).</summary>
    public static IReadOnlyList<Cert> SortByUrgency(IReadOnlyList<Cert> certs)
        => certs
            .OrderBy(c => c.ExpiryKnown ? 0 : 1)
            .ThenBy(c => c.ExpiryKnown ? c.Expires : DateTimeOffset.MaxValue)
            .ThenBy(c => c.Domain, StringComparer.Ordinal)
            .ToList();
}
