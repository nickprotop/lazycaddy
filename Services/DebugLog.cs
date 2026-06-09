// -----------------------------------------------------------------------
// LazyCaddy - opt-in stderr debug logging. Off unless LAZYCADDY_DEBUG=1.
//
// A TUI owns the terminal, so we must NOT write to stderr while it's on the
// screen. But when run as `dotnet run ... 2>/tmp/lc.err`, stderr is redirected
// to a file and never touches the rendered UI — so these lines land in that log
// for inspection (and for driving/verifying flows that are hard to read off a
// screen capture). No-op when the env var isn't set, so it costs nothing in
// normal use.
// -----------------------------------------------------------------------

namespace LazyCaddy.Services;

public static class DebugLog
{
    public static readonly bool Enabled =
        Environment.GetEnvironmentVariable("LAZYCADDY_DEBUG") == "1";

    /// <summary>Write a timestamped line to stderr when LAZYCADDY_DEBUG=1; otherwise no-op.</summary>
    public static void Line(string message)
    {
        if (!Enabled) return;
        try { Console.Error.WriteLine($"[dbg {DateTime.Now:HH:mm:ss.fff}] {message}"); }
        catch { /* never let logging throw */ }
    }
}
