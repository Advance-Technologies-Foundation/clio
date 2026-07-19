# Persistent knowledge installation and hot-reload specification

- **Status:** Approved for implementation
- **Date:** 2026-07-19
- **Feature:** `knowledge-bundle-runtime`

## Goal

Move external knowledge from an ephemeral MCP-process download into an explicit, inspectable Clio
installation. Guidance and future reference-example source must live under a user-visible root on
disk. Every MCP process must load the verified local installation, observe an atomic update without
restarting. MCP is a disk-only consumer; only explicit lifecycle commands contact NuGet.

## User-visible configuration

Clio persists one top-level setting in `appsettings.json`:

```json
"knowledge-root-path": "C:\\Users\\user\\AppData\\Local\\creatio\\clio\\knowledge"
```

When the setting is absent, Clio selects `<clio-home>/knowledge`, creates the directory, and writes
the resolved absolute path through `ISettingsRepository`. `CLIO_HOME` therefore relocates the
default consistently with the rest of Clio. A failed first download does not hide the selected
location: the setting remains visible and `info-knowledge` reports that nothing is installed.

## Commands

### `install-knowledge`

- Creates and persists the default root when needed.
- Acquires the cross-process knowledge lock.
- Downloads the highest stable compatible package when no active installation exists.
- Verifies the bounded NuGet package, inner signed bundle, compatibility, catalog completeness,
  signature, resource paths, lengths, and digests before publishing any files.
- Extracts the verified bundle into a versioned staging directory and atomically publishes it.
- Refuses to replace an existing active installation; callers use `update-knowledge` instead.
- Is idempotent when the same verified version is already active.

### `update-knowledge`

- Uses the same source, trust, staging, validation, and locking pipeline as installation.
- Activates only a package version greater than the durable active-version floor.
- Returns success without rewriting files when no newer compatible package exists.
- Keeps the previous installed version available for last-known-good fallback.

### `info-knowledge`

- Works whether or not knowledge is installed.
- Reports the settings file, configured root, active and previous versions, active content path,
  source, package ID, installation timestamp, validation status, and current-operation diagnostics.
- Checks the feed within the bounded transport deadline and reports `available`, `up-to-date`, or
  `unknown`; a transport failure is not reported as "no update".
- Supports structured output suitable for MCP and automation as well as human-readable CLI output.

### `delete-knowledge`

- Is destructive and requires explicit confirmation in non-interactive/MCP use.
- Acquires the knowledge lock and removes only Clio-managed installation children beneath the
  resolved root.
- Never recursively deletes the configured root itself or any path outside it.
- Leaves `knowledge-root-path` in `appsettings.json` so the location remains visible and can be
  reused by a later installation.

## On-disk contract

```text
<knowledge-root-path>/
  .clio-knowledge-root
  current.json
  knowledge.lock
  versions/
    <package-version>/
      bundle.zip
      install.json
      manifest.json
      manifest.sig
      resources/...
  staging/
  examples/
```

- `.clio-knowledge-root` is the ownership sentinel. Clio initializes only an empty root and refuses
  to adopt or recursively clean a non-empty unowned directory.
- `bundle.zip` preserves the exact signed candidate used for runtime verification.
- Extracted signed files make guidance and future example content directly readable by coding
  agents without going through MCP.
- `install.json` contains non-secret provenance: package ID/version, source, verified bundle digest,
  signed sequence, and UTC installation time.
- `current.json` contains a schema version, active version/path/digest, optional previous
  version/path/digest, and the activation timestamp. Every stored path is relative to the root.
- `examples/` is reserved for later materialization of pinned reference repositories; this feature
  does not yet download Kafka, Google Pub/Sub, or other leaf repositories.

The installer writes only to a unique staging directory. It publishes the completed immutable
version directory first and atomically replaces `current.json` last. A crash before the marker
swap cannot expose a partial version. Stale staging directories are safe to remove while holding
the lock.

Deletion atomically withdraws `current.json` before removing owned version, staging, and example
directories. A retry completes cleanup after interruption, while a running MCP process fails closed
when it observes the missing marker.

## MCP lifecycle

1. MCP resolves and persists `knowledge-root-path` during knowledge-service initialization.
2. MCP performs no NuGet request, whether the cache is valid, missing, or invalid.
3. With no active installation, MCP stays available and external guidance returns typed
   `guidance-unavailable` until `install-knowledge` publishes a verified marker.
4. Every guidance/resource lookup cheaply compares the active marker identity with the loaded
   identity.
5. On change, MCP verifies and materializes the complete new snapshot before one atomic in-memory
   swap. In-flight readers finish against the old immutable snapshot; subsequent readers use the
   new snapshot.
6. If a changed marker fails to reload, the running server retains its previous snapshot and
   records a safe diagnostic; if the marker is deleted or unreadable, serving fails closed and the
   in-memory snapshot is deactivated.
7. A newly started server that cannot load the active entry tries the recorded previous entry
   before returning unavailable.

`FileSystemWatcher` may be used only as a latency optimization. Correctness comes from checking the
small atomic marker at every knowledge access, so missed watcher events cannot leave the process
permanently stale.

## Concurrency and security

- Settings updates use the existing Clio cross-process `appsettings.json` lock and compare/write
  behavior.
- Installation mutations use a separate cross-process `knowledge.lock` plus an in-process lock.
- Readers never take the installation lock; the immutable version directory and atomic marker are
  the synchronization boundary.
- Root, staging, version, and extracted entry paths are canonicalized and constrained beneath the
  configured root before writes or deletion.
- Roots and managed descendants containing symbolic links or junctions are rejected before access.
- Update publication compares the active marker identity observed before download with the identity
  under the mutation lock, so a concurrent delete or update wins instead of being overwritten.
- Package count/size, ZIP central directory, response body, version catalog, extraction, and
  transport deadlines remain bounded.
- Redirects and cross-origin NuGet resources remain rejected.
- Feed URIs containing user information, query strings, or fragments are rejected so credentials
  cannot be persisted in installation metadata or command output.
- Package version remains cryptographically bound to the signed bundle version.
- Trust keys and secrets are never copied into the knowledge directory or emitted by command
  output.

## Out of scope

- Publishing Kafka or Google Pub/Sub reference repositories.
- Downloading or updating leaf example repositories.
- Scheduled or silent background update policies.
- Authenticated feed credential storage and production key rotation/revocation.
- A graphical knowledge manager.

## Acceptance

- A first installation visibly persists `knowledge-root-path` and a verified version on disk.
- A second MCP process reuses the installation without a NuGet request.
- Updating through another CLI process makes new synthetic content visible on the next request in
  the already-running MCP process without restart.
- A failed update, invalid marker, malformed version, concurrent installer, or interrupted staging
  operation never replaces the last verified active snapshot.
- Clio tests assert only delivery mechanics with synthetic content. Canonical article wording and
  reference-example correctness remain owned by `clio-knowledge` and leaf repositories.
