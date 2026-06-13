# Editing & snapshots

## How edits apply

All edits funnel through one path: LazyCaddy reads the current config, builds the
full candidate with your change applied in-memory, then sends it to Caddy with a
single atomic `POST /load`. Caddy provisions the whole config or rolls the whole
thing back — there is never a partially-applied state.

If Caddy rejects the candidate, you see its verbatim error and the running config
is unchanged.

## Snapshots

- Before every edit, LazyCaddy captures a **snapshot** of the known-good config.
- **Undo** (`u`) restores the most recent snapshot. Restoring is itself
  snapshotted, so it's reversible.
- **Snapshot now** (`Shift+S`) captures the current config with a label you type.
- The **Snapshots** view lists history; pin (`p`) protects a snapshot from the
  50-snapshot cap.

## Read-only mode

When started read-only, all edit commands are disabled (the command portal shows
them dimmed with the reason).
