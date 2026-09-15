---
description: IPersistedResourceKeyReader caches in a STATIC AsyncLocal opened at the tool entry point; it never caches a failed read, drops the entry after a save, and sync-pages additionally defers the pre-pass content verdict for a schema an earlier page in the batch also writes
applies-to:
  - clio/Command/PersistedResourceKeyReader.cs
  - clio/Command/PageUpdateOptions.cs
  - clio/Command/McpServer/Tools/McpPersistedResourceKeyGate.cs
  - clio/Command/McpServer/Tools/PageUpdateTool.cs
  - clio/Command/McpServer/Tools/PageSyncTool.cs
ticket: GH-1464
date: 2026-09-12
---

**What is true** — four rules hold together, and each one exists because dropping it produces a wrong
verdict rather than a slow one.

- **The store is `static`, not per instance.** The page tools are resolved from the MCP bootstrap
  container while `PageUpdateCommand` comes out of a SEPARATE per-tenant container built by its own
  `new BindingsModule().Register`, which does not share the first container's singletons. Two
  instances of `PersistedResourceKeyReader` therefore exist inside one logical save whatever DI
  lifetime is registered; a static store is the only thing that makes them agree.
- **The scope is opened at the top of the entry point.** An `AsyncLocal` value flows DOWN to awaited
  callees and never back UP to the caller, so a scope opened inside a nested async helper covers that
  helper and nothing else. `Dispose` restores the enclosing value rather than clearing it — on the
  synchronous CLI path the assignment IS visible to the caller's `ExecutionContext`, so a bare clear
  would leave a later `TryUpdatePage` on that thread inheriting a closed scope's cache.
- **A FAILED read is never cached; only its reason is.** One transient 401 or timeout in the
  out-of-lock pre-pass would otherwise stand in for the rest of the call and reject a page whose key
  IS persisted — even though the in-lock command gate, running moments later with a resolved schema
  context, would have read it successfully. The reason is recorded separately so it still reaches the
  caller's warning channel.
- **The entry is dropped after a successful save of that schema, and `sync-pages` also defers.** A
  save REGISTERS keys, so every cached read of that schema is stale the moment it lands. That alone
  is not enough for a batch: `sync-pages` materialises its deterministic content verdict for EVERY
  page in a pre-pass that runs before ANY page is saved, so a second page on the same schema would be
  rejected there for a key the first page's save is about to create. For a schema an earlier page in
  the batch also writes, the pre-pass therefore skips the CONTENT half and lets the in-lock gate —
  which re-runs the identical chain after that save — decide. The lint half still runs in the
  pre-pass, because the in-lock path materialises lint warnings only.

With NO scope open the reader simply reads, uncached. That is the plain-CLI and unit-test shape and
it is correct, only more expensive.

**Why it is this way** — the cache key has to describe what is actually read: environment, login,
both redirect UIds (they decide which schema `TryResolveContext` resolves to) and the schema name.
The memo this replaced lived on `PageUpdateOptions`, which `sync-pages` cannot use at all: it builds
a fresh options instance per page, and its first validation gate runs before any options exist.

**What breaks if you ignore it** — the cost rules fail silently and the correctness rules fail
loudly-but-misleadingly. Without the static store or the entry-point scope nothing fails and nothing
logs; the save just resolves the schema hierarchy again at each gate — about three extra Creatio
round trips per gate, per page, on what the rescue defines as the NORMAL path. Without the
failure-not-cached rule, the not-caching rule's absence turns one flaky network moment into a hard
rejection of a valid page. Without the invalidation and the pre-pass deferral, a two-page batch on
one schema reports `success: false` for page 2 with "resource 'X' is neither auto-provided ... nor
registered" naming a key that demonstrably exists on the schema by then.

The in-tree checks are `SyncPages_ShouldResolveTheHierarchyOnceForTheRescue_OnTopOfTheSavesOwnResolution`
(measures the rescued save against a clean one rather than a hard-coded number),
`SyncPages_ShouldRevalidateAgainstThePostSaveKeys_WhenTwoPagesTargetTheSameSchema` (the pre-pass
deferral) and `SyncPages_ShouldDropTheCachedRead_WhenAnEarlierPageSavedTheSameSchema` (the
invalidation; it fails if the `Invalidate` call is removed).
