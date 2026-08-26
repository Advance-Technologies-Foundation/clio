---
description: dbHub names its SQL tool execute_sql (no suffix) when only one source is configured, and normalizes every non-alphanumeric character of a source id to underscore, so two distinct clio environments can collide on one tool name
applies-to:
  - clio/Common/DbHub/
date: 2026-08-19
---

**What is true** — dbHub, the external MCP process clio configures through `dbhub.toml`, derives its
SQL tool name from the source id and it does two non-obvious things with it. When exactly one source
is configured the tool is called plain `execute_sql`, with no `_<sourceId>` suffix; clio mirrors that
in `DbHubHttpClient.ContainsSourceTool` (`singleSource ? "execute_sql" : $"execute_sql_{sourceId}"`).
And every character that is not `[a-zA-Z0-9]` in the source id becomes `_` in the tool name, which
`DbHubTomlStore.NormalizeDbHubToolSuffix` reproduces so `DbHubTomlStore.UpsertCore` can refuse a
source whose normalized suffix is already taken by another source.

**Why it is this way** — clio does not own the naming; dbHub does. The only way to verify that a
source is really live is to read the tool inventory dbHub publishes and match the name dbHub would
have generated, which means clio has to model both rules exactly, including the single-source case.
`DbHubConnectionSourceFactory.NormalizeSourceId` already collapses non-alphanumeric runs when it
builds the id, so two clio environment names that differ only in punctuation map to the same id.

**What breaks if you ignore it** — assuming a suffix is always present makes lifecycle verification
report a healthy single-source install as missing, and skipping the collision check lets a sync
silently rewrite another environment's dbHub source: both environments then answer as one, and the
`execute_sql` call runs against the wrong database with no error anywhere.
