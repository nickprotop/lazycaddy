// -----------------------------------------------------------------------
// LazyCaddy - a TUI for managing a running Caddy server via its admin API.
// Built on SharpConsoleUI (ConsoleEx). Polls Caddy's admin API to display
// routes, handler chains, TLS certs, and upstream health, and writes guided,
// snapshot-backed edits (PATCH/POST/DELETE and /load) back through it.
// -----------------------------------------------------------------------

using SharpConsoleUI;
using SharpConsoleUI.Configuration;
using SharpConsoleUI.Drivers;
using SharpConsoleUI.Helpers;
using LazyCaddy.Configuration;
using LazyCaddy.Dashboard;
using LazyCaddy.Services;

namespace LazyCaddy;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        // Support the PTY shim used by SharpConsoleUI's screenshot/testing harness.
        if (PtyShim.RunIfShim(args)) return 127;

        if (!TryParseArgs(args, out var adminUrl, out var certDir, out var accessLog, out var exitCode))
            return exitCode;

        try
        {
            var config = LazyCaddyConfig.Default with { AdminApiUrl = adminUrl };
            if (certDir is not null) config = config with { CaddyDataDir = certDir };
            if (accessLog is not null) config = config with { AccessLogPath = accessLog };

            // Flip simulateDisconnected via an env var to exercise the red status path.
            var simulateDown = Environment.GetEnvironmentVariable("LAZYCADDY_SIMULATE_DOWN") == "1";
            var admin = new CaddyAdminClient(config, simulateDisconnected: simulateDown);
            var prober = new UpstreamProber(config);
            var snapshots = new SnapshotStore(config.InstanceSnapshotDir, config.MaxAutoSnapshots);
            var editor = new EditCoordinator(admin, snapshots, config);

            // Use the new opt-in async model: with InstallSynchronizationContext on,
            // await inside UI-thread handlers (e.g. row activation) resumes on the UI
            // thread. The background poll thread does not start on the UI thread, so
            // its HTTP/probe awaits still resume on the thread pool — fetching stays
            // off the UI thread. (See ConsoleEx docs/THREADING_AND_ASYNC.md.)
            var windowSystem = new ConsoleWindowSystem(
                new NetConsoleDriver(RenderMode.Buffer),
                options: new ConsoleWindowSystemOptions(
                    ShowTopPanel: false,
                    ShowBottomPanel: false,
                    InstallSynchronizationContext: true));

            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                windowSystem.Shutdown(0);
            };

            var shell = new DashboardShell(windowSystem, config, admin, prober, editor);
            shell.Create();

            await Task.Run(() => windowSystem.Run());

            admin.Dispose();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Clear();
            ExceptionFormatter.WriteException(ex);
            return 1;
        }
    }

    /// <summary>
    /// Parse CLI args for the Caddy admin API URL. Accepts a positional URL or
    /// <c>--url/-u &lt;URL&gt;</c>, defaulting to the config default (http://localhost:2019).
    /// Returns false when the app should exit without launching (help or bad input),
    /// with <paramref name="exitCode"/> set accordingly.
    /// </summary>
    private static bool TryParseArgs(string[] args, out string adminUrl, out string? certDir, out string? accessLog, out int exitCode)
    {
        adminUrl = LazyCaddyConfig.Default.AdminApiUrl;
        certDir = null;
        accessLog = null;
        exitCode = 0;

        string? url = null;
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            switch (a)
            {
                case "-h" or "--help":
                    PrintUsage();
                    exitCode = 0;
                    return false;

                case "-u" or "--url":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("lazycaddy: --url requires a value.");
                        exitCode = 2;
                        return false;
                    }
                    url = args[++i];
                    break;

                case "--cert-dir":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("lazycaddy: --cert-dir requires a value.");
                        exitCode = 2;
                        return false;
                    }
                    certDir = args[++i];
                    break;

                case "--access-log":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("lazycaddy: --access-log requires a value.");
                        exitCode = 2;
                        return false;
                    }
                    accessLog = args[++i];
                    break;

                default:
                    if (a.StartsWith('-'))
                    {
                        Console.Error.WriteLine($"lazycaddy: unknown option '{a}'. Try --help.");
                        exitCode = 2;
                        return false;
                    }
                    if (url is not null)
                    {
                        Console.Error.WriteLine("lazycaddy: admin URL specified more than once.");
                        exitCode = 2;
                        return false;
                    }
                    url = a; // positional URL
                    break;
            }
        }

        if (url is not null)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                Console.Error.WriteLine($"lazycaddy: invalid admin URL '{url}' (expected http(s)://host:port).");
                exitCode = 2;
                return false;
            }
            adminUrl = url;
        }

        return true;
    }

    private static void PrintUsage()
    {
        Console.WriteLine(
            """
            lazycaddy — a TUI for managing a running Caddy server via its admin API.

            Usage:
              lazycaddy [URL]
              lazycaddy --url <URL>

            Arguments:
              URL                Caddy admin API base URL (default: http://localhost:2019)

            Options:
              -u, --url <URL>    Caddy admin API base URL
              --cert-dir <DIR>   Caddy data dir for reading real cert expiry from disk
                                 (default: $XDG_DATA_HOME/caddy or ~/.local/share/caddy;
                                 only useful when run on the same host as Caddy)
              --access-log <PATH>  Access-log file to tail in the Logs view
                                   (default: auto-discovered from the running config;
                                   only works for a local Caddy with a file log writer)
              -h, --help         Show this help and exit

            Examples:
              lazycaddy
              lazycaddy http://localhost:2019
              lazycaddy --url https://caddy.internal:2019
              lazycaddy --cert-dir /var/lib/caddy/.local/share/caddy
              lazycaddy --access-log /var/log/caddy/access.log
            """);
    }
}
