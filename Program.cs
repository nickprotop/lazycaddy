// -----------------------------------------------------------------------
// LazyCaddy - a read-only TUI dashboard for a running Caddy server.
// Built on SharpConsoleUI (ConsoleEx). Polls Caddy's admin API and displays
// routes, TLS certs, and upstream reachability. No write operations.
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

        if (!TryParseArgs(args, out var adminUrl, out var exitCode))
            return exitCode;

        try
        {
            var config = LazyCaddyConfig.Default with { AdminApiUrl = adminUrl };

            // Flip simulateDisconnected via an env var to exercise the red status path.
            var simulateDown = Environment.GetEnvironmentVariable("LAZYCADDY_SIMULATE_DOWN") == "1";
            var admin = new CaddyAdminClient(config, simulateDisconnected: simulateDown);
            var prober = new UpstreamProber(config);
            var snapshots = new SnapshotStore(config.SnapshotDir, config.MaxAutoSnapshots);
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
    private static bool TryParseArgs(string[] args, out string adminUrl, out int exitCode)
    {
        adminUrl = LazyCaddyConfig.Default.AdminApiUrl;
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
            lazycaddy — a read-only TUI dashboard for a running Caddy server.

            Usage:
              lazycaddy [URL]
              lazycaddy --url <URL>

            Arguments:
              URL                Caddy admin API base URL (default: http://localhost:2019)

            Options:
              -u, --url <URL>    Caddy admin API base URL
              -h, --help         Show this help and exit

            Examples:
              lazycaddy
              lazycaddy http://localhost:2019
              lazycaddy --url https://caddy.internal:2019
            """);
    }
}
