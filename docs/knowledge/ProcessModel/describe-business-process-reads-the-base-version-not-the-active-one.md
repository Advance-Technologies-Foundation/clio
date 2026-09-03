---
description: describe-business-process resolves a process by schema Name, and every saved process version is a SEPARATE schema with its own name, so on a versioned process it describes version 0 while the runtime executes the active version - the response now reports version/isActiveVersion/activeVersionName, but the resolution itself is unchanged and a caller that ignores those fields still explains the wrong graph
applies-to:
  - clio/Command/DescribeProcessCommand.cs
  - clio/Command/McpServer/Tools/ProcessDesigner/DescribeProcessTool.cs
  - clio/Command/ProcessModel/IProcessDescriber.cs
ticket: ENG-94374
date: 2026-09-03
---

**What is true** — a Creatio process version is not a revision of one schema; each version is a
distinct schema whose name is `<parentSchemaName><PackageName><version>` (the trailing `Custom1` in
`UsrProcess_0370312Custom1` is the package name, not a literal). `describe-business-process` accepts
`--process-name` / `--process-uid` / `--process-caption` and the server resolves the name against the
schema instance, so asking for `UsrProcess_0370312` on a process that has versions returns the graph
of version 0 — while the runtime redirects execution to whichever schema is flagged as the active
version. The response now carries `version`, `isActiveVersion`, `activeVersionName`,
`activeVersionSchemaUId`, `versionRootSchemaUId` and the `versions[]` family, so the graph's standing
is stated; the resolution is unchanged, and a caller that does not read those fields is in exactly
the position this record described before they existed.

**Why it is this way** — versioning was added on the platform side as a family of sibling schemas
(the family is flat: every version points at the ROOT as its parent, not at the previous version).
clio's describe path predates that and models a process as one schema identified by name. The fields
are read from the platform's own process-library view rather than from the server's describe
response, which is why `activeVersionSource` names the authority: the runtime consults the schema
manager instead, and the two can rank a tied family differently.

**What breaks if you ignore it** — an agent asked "what does this process do?" reads and confidently
explains a graph that is not the one running in production. Absence is not reassurance either:
`version` is omitted rather than zeroed when the facts could not be established, and
`versionReadWarning` then says why, so treating a missing `version` as "unversioned" reproduces the
original defect with the fields in place. Do not treat a name match as identity on a versioned
process.
