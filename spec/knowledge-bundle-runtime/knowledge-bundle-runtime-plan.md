# Persistent knowledge store architecture decision

- **Status:** Accepted
- **Date:** 2026-07-19
- **Feature:** `knowledge-bundle-runtime`

> Superseded in part by
> [the multi-source knowledge contract](knowledge-bundle-runtime-multi-source-contract.md). The
> durable local-cache decision remains; NuGet is now one implementation of `IKnowledgeTransport`,
> Git is another, and activation/resolution are library-scoped.

## Context

The original prototype downloaded a NuGet package during MCP guidance access and retained the
verified articles only in process memory. Stdio MCP processes are frequently recreated by coding
agents, so that design repeats network work, loses last-known-good state at restart, hides content
from filesystem tools, and cannot support inspectable reference implementations.

Clio already has an atomic, cross-process `appsettings.json` update path and a relocatable
`SettingsRepository.AppSettingsFolderPath`. The knowledge runtime already validates signed,
bounded candidates and atomically swaps an immutable in-memory article snapshot.

## Decision

Introduce a local, versioned knowledge store rooted at the explicit top-level
`knowledge-root-path` setting.

- Default the missing setting to `<clio-home>/knowledge` and persist the absolute path.
- Make NuGet an installation/update transport, not the serving path.
- Extract verified signed content into immutable `versions/<package-version>` directories so
  coding agents can read the same content Clio serves.
- Preserve the exact verified `bundle.zip` in each version directory.
- Publish `current.json` atomically after the complete version directory exists.
- Store both active and previous identities in the marker to retain last-known-good startup
  behavior across process restarts.
- Make runtime access compare the marker on every lookup and atomically swap only after the new
  disk snapshot verifies completely.
- Use a dedicated cross-process knowledge lock for install/update/delete; keep ordinary reads
  lock-free against immutable directories.
- Expose install, update, info, and delete as ordinary DI-backed local Clio commands. Keep them out
  of the MCP tool catalog: MCP is the disk-only consumer, while lifecycle/network operations are
  explicit terminal actions and do not require duplicate tool contracts.
- Keep Clio tests synthetic and mechanics-only. `clio-knowledge` owns real content assertions.

The durable marker is the authoritative activation record. A filesystem watcher is optional and
cannot be the only freshness mechanism.

## Command semantics

- `install-knowledge` bootstraps only an absent installation and is idempotent for the active
  version.
- `update-knowledge` advances the durable package floor and never downgrades.
- `info-knowledge` reports local state and a bounded remote update status.
- `delete-knowledge` removes only managed children after explicit confirmation and retains the
  configured root pointer.

## Consequences

- Normal MCP startup and guidance reads become local and deterministic after the first install.
- A CLI update is visible to every already-running MCP process on its next knowledge request.
- Installed guidance and future example source are available to ordinary filesystem tools.
- Disk usage includes at least active and previous versions; older versions can be pruned while
  holding the install lock after the marker swap.
- The store and marker become a security boundary and require adversarial path, archive, lock,
  crash, and tamper tests.
- The existing NuGet renewal-on-read implementation must be refactored rather than layered under
  the new runtime.
