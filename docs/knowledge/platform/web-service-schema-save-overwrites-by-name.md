---
description: ServiceDataContract save validates nothing and replaces an existing web service schema of the same name
applies-to:
  - clio/Command/WebServiceDesigner/WebServiceDesigner.cs
  - clio/Command/WebServiceDesigner/WebServiceDesignerClient.cs
ticket: ENG-105475
date: 2026-11-11
---

**What is true** — the platform save behind Studio > Web services (`DataService/json/SyncReply/ServiceDataContract`,
`ServiceManagerService`) looks the schema up by name, replaces what it finds, and runs none of Studio's checks:
prefix on method and parameter codes, Object only as an array, no GET body, no XML body. It also has no
CanManageSolution gate of its own. Those rules live only in the Studio page scripts.

**Why it is this way** — Studio validates in the browser before it sends the contract; the service trusts its
caller.

**What breaks if you ignore it** — a register call with a code another service already uses silently overwrites
that service, and a schema that breaks a Studio rule saves fine but cannot be saved again in Studio (the method
dialog refuses OK) and may not bind in a Call web service element. clio therefore checks the name with
`CheckUniqueSchemaName`, re-implements the Studio rules in `WebServiceDefinitionBuilder`, and checks
CanManageSolution itself.
