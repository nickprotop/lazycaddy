# LazyCaddy

<div align="center">
  <img src=".github/logo.svg" alt="LazyCaddy Logo" width="600">
</div>

<div align="center">

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-Linux-orange.svg)]()

</div>

**A terminal UI for managing a running [Caddy](https://caddyserver.com/) server through its admin API, built on [SharpConsoleUI](https://github.com/nickprotop/ConsoleEx).**

<div align="center">

### ⭐ If you find LazyCaddy useful, please consider giving it a star! ⭐

It helps others discover the project and motivates continued development.

[![GitHub stars](https://img.shields.io/github/stars/nickprotop/lazycaddy?style=for-the-badge&logo=github&color=yellow)](https://github.com/nickprotop/lazycaddy/stargazers)

</div>

LazyCaddy makes a running Caddy instance legible and editable to someone who uses Caddy without living in its JSON. It reads the running config to show your routes, their handler chains, TLS certs, and upstream health — and writes guided, reversible edits back through the admin API. Every change auto-snapshots first, so nothing you do is one-way.

**Inspect. Edit. Undo.**

## Quick Start

**Option 1: One-line install** (Linux, no .NET required)
```bash
curl -fsSL https://raw.githubusercontent.com/nickprotop/lazycaddy/master/install.sh | bash
lazycaddy
```

This installs the latest release binary to `~/.local/bin/lazycaddy`. Remove it with
`lazycaddy-uninstall.sh`, or grab a binary directly from the
[Releases](https://github.com/nickprotop/lazycaddy/releases) page.

**Option 2: Build from source** (requires .NET 10)
```bash
git clone https://github.com/nickprotop/lazycaddy.git
cd lazycaddy
./build-and-install.sh
```

## Usage

```bash
lazycaddy                                   # default: http://localhost:2019
lazycaddy --url https://caddy.host:2019     # point at a different admin API
lazycaddy --help                            # usage
```

By default it targets the Caddy admin API at `http://localhost:2019`; pass `--url <URL>`
(or a positional URL) to point elsewhere.

**Keys:** `1`–`9` jump to a view · `R` refresh now · `U` quick-undo last change ·
`Shift+S` snapshot now · `Q` / Esc quit · Tab + arrows navigate within a view.

## Views

- **Overview** — status cards (Caddy health/version/uptime, route/cert/upstream
  counts) plus a request-rate sparkline and metrics (status codes, latency
  percentiles, busiest handlers) when traffic is flowing.
- **Routes** — a grouped, expandable table: each route is a row (host/match →
  upstream); expand it to see its full handler chain. Add/edit/delete routes,
  edit matchers, and add/reorder/remove handlers — including the security
  handlers (basic auth, header manipulation, IP access, forward-auth, rate-limit).
- **TLS / Certs** — domain, issuer, expiry, days-left (color-coded by urgency),
  ACME status, with an expiry-alert banner.
- **Upstreams** — active TCP reachability probe per upstream, with latency.
- **Raw Config** — the running config as line-numbered, syntax-highlighted JSON;
  find/replace, in-place editing via `/load`, and a Caddyfile→JSON adapter.
- **Snapshots** — every write auto-snapshots first; browse, pin, and restore
  config history (restores are themselves reversible).
- **Topology** — a scrollable routing graph: host → handler chain →
  upstream, health-colored, one swim-lane per route.
- **Logs** — a live tail of Caddy's access log.
- **Server** — server-level and global settings (listeners, automatic-HTTPS,
  TLS hardening).

## How edits work

Everything that writes funnels through a single seam (`EditCoordinator`) that
**auto-snapshots the full config before every change**, then applies a granular
`PATCH`/`POST`/`DELETE` (or `/load`) to the admin API. Failures surface Caddy's
own error message, formatted for humans. `U` is a one-key undo of the last
change; the **Snapshots** view is the full history. Snapshots persist per
instance under `~/.config/lazycaddy/snapshots/` and survive restarts.

> **Note:** writes target a *real, running* Caddy. Point LazyCaddy at a test
> instance before experimenting — never at production until you trust an edit.

## Architecture

- **One seam to Caddy** — everything goes through `ICaddyAdmin` /
  `CaddyAdminClient`. Reads call pure, unit-tested parsers (`ConfigParser`,
  `UpstreamsParser`, `MetricsParser`); writes return Caddy's verbatim error on
  failure.
- **One write seam** — `EditCoordinator` snapshots-before-write for every edit,
  so all changes are reversible by construction.
- **Threading** — a single background poll loop fetches all DTOs off the UI
  thread into an immutable snapshot; every control mutation is marshalled back
  via `EnqueueOnUIThread` (opt-in `InstallSynchronizationContext` model).
- **Views** — `Views/*`, one per nav entry; each exposes `Build(panel)` and
  `Update(state)`. Pure layout/render logic (topology graph, layered layout) is
  separated from the canvas so it stays unit-testable.

## License

MIT — see [LICENSE](LICENSE).
