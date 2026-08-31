---
description: the remote create and delete lifecycle for an FSM desktop can leave a missing-item reference that poisons later configuration saves
applies-to:
  - clio.mcp.e2e/PageCreateToolE2ETests.cs
date: 2026-08-31
---

**What is true** — On the local Creatio 10.1 FSM stand, the remote designer create/read/delete lifecycle for a desktop based on `CentralAreaDesktopTemplate` left configuration publishing resolving that schema name as a missing item after the creating process exited. The desktop E2E therefore skips this lifecycle when `get-fsm-mode` reports `on`.

**Why it is this way** — The observed test performed both remote creation and cleanup, so the individual operation that persists the bad reference is not isolated. Restarting Creatio did not repair it, proving persisted platform state rather than only a stale in-memory schema-manager cache. The same lifecycle remains covered on an FSM-off sandbox, where the schema uses the database-backed package path.

**What breaks if you ignore it** — Every later schema or page save can fail with `Item with name "UsrE2E_Desktop_..." not found`, making unrelated E2E tests fail until the environment is redeployed.
