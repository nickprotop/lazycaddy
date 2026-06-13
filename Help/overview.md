# LazyCaddy

LazyCaddy makes a running [Caddy](https://caddyserver.com/) server legible and
editable without hand-writing JSON. It talks to Caddy's **admin API** (default
`http://localhost:2019`): reading the live config to show routes, certs, and
upstreams, and writing guided edits back.

## Getting around

- The left sidebar lists the views. Press a **digit (1–9)** to jump to one, or
  open the **command portal** with **Ctrl+K** and type a view name.
- Each view focuses its primary control on entry — arrows move the selection,
  **Tab** crosses between panes.

## Editing is safe

Every edit is **atomic**: LazyCaddy computes the full candidate config and
applies it in one transaction. If Caddy rejects it, the whole change rolls back
and nothing is left half-applied. Each edit is also **snapshotted first**, so
**Undo** (`u`) restores the previous state.

See the other topics for per-view help, and **Keyboard shortcuts** for the full
key list.
