---
description: the MCP e2e ATF lane deploys a prebuilt Creatio core build that changes with no VCS revision changing, so an internal platform contract change (here ISchemaImporter.ImportSchema gaining an ExistingSchemaPackageAction parameter in 10.3.1146, ENG-106408) breaks import-schema on every pull request at once with no clio commit to blame
applies-to:
  - cliogate/Files/cs/CreatioApiGateway.cs
  - clio.mcp.e2e/SchemaTransferToolE2ETests.cs
ticket: ENG-106408
date: 2026-11-24
---

**What is true** - the "CLIO MCP e2e tests (ATF)" lane installs whatever core build is current. Its
`Core` VCS revision can stay the same while the deployed build moves: the build log names it as
`Version=10.3.NNNN`. The platform schema importer cliogate calls is the internal
`ISchemaImporter` in engineering/core (`TSBpm/Src/Lib/Terrasoft.Core/ISchemaImporter.cs`), with no
compatibility promise. Before ENG-106408 it moved a schema that already existed in another package into
the target package without asking.

**Why it is this way** - the lane tests clio against the platform as it ships next, not against a pinned
release.

**What breaks if you ignore it** - a platform-side change looks like a clio regression that appears
everywhere at once. When `SchemaTransfer_Should_RoundTrip_Export_And_Import_Against_A_Live_Environment`
(or any cliogate reflection path) starts failing with a binding message on every branch, compare
`Version=` in the last green and first red build logs, then read the current signature in engineering/core.
Searching clio history for the cause finds nothing: the 10.3.1125 -> 10.3.1146 move kept the lane red
for a week before that comparison was made.
