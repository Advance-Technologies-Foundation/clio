---
description: describe-business-process resolves a process by schema Name, and every saved process version is a SEPARATE schema with its own name, so on a versioned process it describes version 0 while the runtime executes the active version - and the output has no version field to reveal it
applies-to:
  - clio/Command/DescribeProcessCommand.cs
  - clio/Command/McpServer/Tools/ProcessDesigner/DescribeProcessTool.cs
  - clio/Command/ProcessModel/IProcessDescriber.cs
ticket: ENG-94374
date: 2026-08-19
---

**What is true** — a Creatio process version is not a revision of one schema; each version is a
distinct schema whose name is `<parentSchemaName><PackageName><version>` (the trailing `Custom1` in
`UsrProcess_0370312Custom1` is the package name, not a literal). `describe-business-process` accepts
`--process-name` / `--process-uid` / `--process-caption` and the server resolves the name against the
schema instance, so asking for `UsrProcess_0370312` on a process that has versions returns the graph
of version 0 - while the runtime redirects execution to whichever schema is flagged as the active
version. Neither `DescribeProcessOptions` nor any DTO in `clio/Command/ProcessModel/` carries a
`version` or `isActiveVersion` field, so nothing in the response says which schema was read.

**Why it is this way** — versioning was added on the platform side as a family of sibling schemas
(the family is flat: every version points at the ROOT as its parent, not at the previous version).
clio's describe path predates that and models a process as one schema identified by name.

**What breaks if you ignore it** — an agent asked "what does this process do?" reads and confidently
explains a graph that is not the one running in production, with no warning and no field to check.
Any fix has to surface the version and the active-version flag in the describe output before the
answer can be trusted; do not treat a name match as identity on a versioned process.
