---
description: DataService BatchQuery stop-on-error may omit all queryResults and rollback depends on server transaction configuration
applies-to:
  - clio/Command/DataServiceBatchCommand.cs
ticket: "#1577"
date: 2026-09-17
---

**What is true** — A native batch with continuation enabled returned successful, failed, successful item acknowledgements for a three-write probe with an invalid middle column. Independent readback confirmed both successful writes persisted. With continuation disabled, the same failure produced a batch-level error without item outcomes; the earlier write rolled back in the tested environment. That rollback is not a portable atomicity guarantee because native transaction behavior depends on server configuration.

**Why it is this way** — Native continuation controls whether individual exceptions become query results or escape the batch. The endpoint does not provide a reliable per-item execution history when the batch fails as a whole.

**What breaks if you ignore it** — Treating missing results as not attempted, or assuming every batch is atomic, can cause retries of already applied writes. Preserve unknown outcomes and independently inspect records before resubmission.
