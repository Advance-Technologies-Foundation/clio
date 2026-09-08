---
description: web-vs-mobile for a Freedom UI page (schemaType 9 vs 10) cannot be selected from SysSchema - it lives in the schema metadata and only the designer hierarchy service returns it, so a SysSchema SelectQuery can only classify a page by its parent template
applies-to:
  - clio/Command/McpServer/Tools/MobilePageConverter/MobileActionTargetProbe.cs
  - clio/Command/PageListOptions.cs
  - clio/Command/PageSchemaType.cs
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
choose between N designer round trips and an indirect signal. The indirect signal that works is
the PARENT — a mobile page descends from one of the mobile template roots in `CrtUIPlatform`, and
`[SysSchema:Id:Parent].Name` IS selectable — which is why `MobileActionTargetProbe` classifies in
two tiers rather than one query.

**What breaks if you ignore it** — a `SelectQuery` that filters or selects a schema-type column
returns a DataService failure envelope, and `DataServiceSelectResponse` turns that into an
exception; that failure is loud. The silent one is worse: classifying a page by its parent WITHOUT
an escalation tier reports every page under a custom base (`UsrMyMobileBase` extending
`BaseMobilePageTemplate`) as "not a mobile page". In `MobileActionTargetProbe` that verdict reaches
the caller as `state: "missing"`, which the guide reports as a verified broken navigation — so the
developer is sent to fix a button that already works, on the strength of a lookup that never had the
answer. Any consumer needing web-vs-mobile in bulk must keep the rule: a page the cheap tier cannot
place is `Unknown`, never `Missing`.
