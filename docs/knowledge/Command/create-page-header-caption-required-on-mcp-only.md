---
description: create-page refuses a BaseHomePage/BaseDashboardTemplate page without a caption (or with caption == schema-name) on the MCP path only; the CLI still defaults the caption to schema-name
applies-to:
  - clio/Command/PageCreateOptions.cs
  - clio/Command/McpServer/Tools/PageCreateTool.cs
ticket: ENG-107757
date: 2026-11-25
---

**What is true** — `PageCreateOptions.RequireHeaderCaption` is not a CLI option. Only the MCP `create-page`
tool sets it, and only then does `PageCreateCommand` refuse a home page or dashboard whose caption is missing or
equals `schema-name` (case-insensitive, trimmed). The check runs on the resolved template name, before any
package, uniqueness or save request. `clio create-page --template BaseHomePage` without `--caption` still
creates the page with the schema name as its caption.

**Why it is this way** — for these templates the caption is the title in the page header. Agents following
the home-page and dashboard guides omitted `caption` in about half of the ADAC `pixel-ninjas/create-homepage`
runs, and the page shipped with `UsrItRequestsOverviewHomePage` in its header while the agent reported success;
an explicit caption line in the prompt did not change the rate. Making it required on the CLI as well would
break existing scripts and CI calls, which name the page deliberately or do not care about the header.

**What breaks if you ignore it** — moving the check into the CLI option (`Required = true`) breaks every script
that creates a home page or dashboard without `--caption`. Dropping the "equals schema-name" half lets an agent
satisfy the requirement by copying the schema name into `caption`, which is the original defect. Changing
`SysSchema.Caption` through OData after creation does not fix the header, so the create-time check is the only
guard.
