# LazyCaddy

A read-only TUI dashboard for a running [Caddy](https://caddyserver.com/) server,
built on [SharpConsoleUI](https://github.com/nickprotop/ConsoleEx).

It makes a running Caddy instance legible at a glance: what it's routing
(public host → internal upstream), whether TLS certs are healthy, and whether
upstreams are reachable. It only **reads** the admin API — no write/PATCH
operations.

## Views

- **Overview** — status cards (Caddy health/version/uptime, route count, cert
  valid/expiring, upstream up/down) plus an optional request-rate sparkline.
- **Routes** — one row per route (host/match → upstream, TLS, status). Press
  **Enter** on a row to open a detail overlay with the pretty-printed config.
- **TLS / Certs** — domain, issuer, expiry, days-left (color-coded by urgency),
  ACME status.
- **Upstreams** — active TCP reachability probe per upstream, with latency.
- **Raw Config** — the running config as read-only, line-numbered,
  syntax-highlighted JSON (Ctrl+F to find).

## Run

```bash
dotnet run                                  # default: http://localhost:2019
dotnet run -- http://localhost:2019         # positional admin URL
dotnet run -- --url https://caddy.host:2019 # or via --url / -u
dotnet run -- --help                        # usage
```

Requires .NET 10. By default it targets the Caddy admin API at
`http://localhost:2019`; pass a positional URL or `--url <URL>` to point elsewhere.

Keys: **R** refresh now · **Q** / Esc quit · arrows/Tab/Enter navigate.

### Try the disconnected state

```bash
LAZYCADDY_SIMULATE_DOWN=1 dotnet run
```

The status bar goes red/disconnected and the loop keeps retrying.

## Wiring the real Caddy admin API

The UI runs end-to-end on **representative dummy data** out of the box. The only
file you need to edit to talk to a real Caddy is:

```
Services/CaddyAdminClient.cs
```

Each method has a clearly-marked `// TODO: parse real Caddy admin API JSON here`
stub returning dummy DTOs. The `HttpClient` plumbing (base address, timeout, the
`GetStringAsync` helper) is already wired — replace each stub body with a real
GET + `System.Text.Json` parse into the DTOs in `Models/CaddyModels.cs`. Delete
`Services/DummyData.cs` once real data flows.

Useful endpoints: `GET /config/`, `GET /reverse_proxy/upstreams`, `GET /metrics`.

## Architecture

- `Program.cs` — bootstrap: window system, admin client, prober, shell.
- `Dashboard/DashboardShell.cs` — NavigationView shell + pinned status bar +
  the single background poll loop (`WithAsyncWindowThread`). All HTTP and probes
  run off the UI thread; control mutations are marshalled back via
  `EnqueueOnUIThread`. Uses the opt-in `InstallSynchronizationContext` async
  model (see ConsoleEx `docs/THREADING_AND_ASYNC.md`).
- `Dashboard/DashboardState.cs` — thread-safe holder of the latest snapshot +
  connection state.
- `Views/*` — one file per view; each exposes `Build(panel)` (content factory)
  and `Update(state)` (called on the UI thread each poll).
- `Services/ICaddyAdmin.cs` + `CaddyAdminClient.cs` — the admin-API abstraction.
- `UI/Modals/*` — reusable modal base + the route-detail overlay.

A Canvas-based live topology diagram (host → upstream edges) is a planned v2
feature; a marker comment sits in `Views/OverviewView.cs` where it would slot in.
