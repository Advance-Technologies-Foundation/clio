---
description: web-vs-mobile for a Freedom UI page (schemaType 9 vs 10) cannot be selected from SysSchema - it lives in the schema metadata and only the designer hierarchy service returns it, so a SysSchema SelectQuery can only classify a page by its parent template
applies-to:
  - clio/Command/PageListOptions.cs
  - clio/Command/PageSchemaType.cs
  - clio/Command/McpServer/Tools/MobilePageConverter/MobileActionTargetProbe.cs
ticket: ENG-94839
date: 2026-09-07
---

**What is true** — there is no `SysSchema` column carrying a client-unit schema's numeric type
(`9` = Freedom UI web, `10` = Freedom UI mobile). Both web and mobile pages are rows with
`ManagerName = 'ClientUnitSchemaManager'` and nothing in the row distinguishes them. The number
lives inside the schema's serialized metadata, and the only read that surfaces it is
`ClientUnitSchemaDesignerService.svc/GetParentSchemas`
(`IPageDesignerHierarchyClient.GetParentSchemas` -> `PageDesignerHierarchySchema.SchemaType`), which
needs a schema UId and returns one schema per call. `get-page` exposes it as its `schema-type`
label for exactly one page; `list-pages` selects `Name`, `UId`, `SysPackage.Name` and
`[SysSchema:Id:Parent].Name` and therefore cannot filter or report it at all.

**Why it is this way** — `SysSchema` is the generic schema table shared by every manager; the
client-unit subtype is a property of the client-unit schema payload, not of the row. Adding a
column would be a platform change, so a consumer that needs the type for MANY schemas has to
choose between N designer round trips and an indirect signal (the PARENT: a mobile page descends
from one of the mobile template roots in `CrtUIPlatform`, and `[SysSchema:Id:Parent].Name` IS
selectable).

**What breaks if you ignore it** — a `SelectQuery` that filters or selects a schema-type column
returns a DataService failure envelope, and `DataServiceSelectResponse` turns that into an
exception. So "add a `schema-type` filter to `list-pages`" is not a small change: it needs either a
designer round trip per row or the parent-template signal, and the parent signal is only sound with
an escalation tier — a page under a custom base (`UsrMyMobileBase` extending
`BaseMobilePageTemplate`) is not placed by the root allowlist and must resolve to UNKNOWN rather
than to "not a mobile page".

> ENG-94839 built exactly that two-tier classifier and then deleted it: a WEB page's
> `crt.OpenPageRequest` names a web page by construction, so the question it answered had a fixed
> answer. Kept here because the platform fact outlives that consumer and the next person to want a
> bulk web-vs-mobile filter will hit it again.
