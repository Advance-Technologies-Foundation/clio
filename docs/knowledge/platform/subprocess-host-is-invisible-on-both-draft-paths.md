---
description: The process designer never offers the host as a sub-process callee (ExcludedSchemas carries parentSchema.uId), while on create and on save-as-new-version the host is a draft built with CreateSchema(..., addToDesignItems false) that no schema-manager lookup can see - so a guard that asks the manager about the host answers "not found" on create and makes a new version its own family
applies-to:
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
  - clio.mcp.e2e/SubProcessElementToolE2ETests.cs
ticket: ENG-100192
date: 2026-09-23
---

**What is true** — two platform facts, and they pull against each other.

The classic designer never lists the host process in the Sub-process card's callee picker.
`SubProcessPropertiesPage.getSchemaListFilter` puts `parentSchema.uId` into `ExcludedSchemas`
(CrtProcessDesigner 7.8.0, `SubProcessPropertiesPage.js:169-187`), and the server lists only
`VwProcessLib` rows with `IsActiveVersion = true` and `UId NotEqual` the excluded ones
(`ProcessSchemaManagerService.svc.cs:182-203`). The designer shows no error. The host is simply never
a candidate, whether it is saved or new. The filter excludes the exact UId only, so an inactive version being
edited still lists its family's active version: the designer does not stop a family self-reference.

CrtProcessBuilder builds the host as a draft on two paths: `create-business-process`
(`ProcessBuildHandler.CreateSchemaDraft`) and `modify-business-process-as-new-version`
(`ProcessVersionCloneFactory.CreateVersionClone`). Both use `CreateSchema(..., addToDesignItems: false)`,
which registers the item nowhere (`SchemaManager.cs:4193-4202`, TSBpm core checkout `8f6745caa`; other core
versions shift the line numbers). While the edit runs, `FindItemByName` and `FindItemByUId` both miss the host.
The version clone's `ParentSchemaUId` already names the family root before the edit pipeline runs, and it
keeps the source's NAME until it is renamed after the edit.

**Why it is this way** — the draft stays unregistered on purpose: a failed build or version save must
leave nothing behind that any lookup could reach. There is no list to leave the host out of, because a
caller types a name. So the package has to recognise the host from the schema it is editing, not from
the manager.

**What breaks if you ignore it** — a self-reference guard that resolves the callee by name and compares
families by UId misses both draft paths. On create, the host's own name resolves to nothing and the
caller is told the process "was not found on this environment". That sends them off to create it
first, and they meet the real refusal one request later. `CreateBusinessProcess_Should_RefuseASelfReferencingSubProcess`
failed that way from the day it was written: the draft predates the sub-process element. On a new
version it is worse. The clone looked like a family of its own, so selecting the root passed. The
platform's exact-UId `GetCanSynchronizeParameters` then synchronizes normally, and the saved version
calls itself once it is activated. A unit test that registers the host stays green through both: the create
path never has a registered host (modify's addElement does run Create mode against one, so the arrange has to
match the path under test).
