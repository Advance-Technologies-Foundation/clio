---
description: activating a case or process version that lives in a read-only package writes a user property (SysSchemaUserProperty), which is environment data and does not travel with any package
applies-to:
  - clio/Command/SetActiveDynamicCaseVersionCommand.cs
  - clio/Command/McpServer/Tools/ProcessDesigner/SetActiveDynamicCaseVersionTool.cs
ticket: ENG-106400
date: 2026-11-24
---

**What is true** — on the default path, `SetActiveVersionItem` re-saves a target in an editable package with
`IsActiveVersion = True`, which is schema content and ships with that package. A target in a package that does not
accept changes cannot be re-saved, so the platform pins it with a `SysSchemaUserProperty` row instead. That row is
not schema content and is bound to no package: exporting or installing the packages does not carry the switch to
another environment.

**Why it is this way** — the platform has nowhere else to put the flag for a schema it may not write; the user
property is read first by the active-version pick, so the switch takes effect here. Traced in core source
(`SysUserPropertyHelper`, `SchemaManagerItem`); on the stand only its effect was observed, because
`SysSchemaUserProperty` is not readable through DataService.

**What breaks if you ignore it** — a user who activates a vendor member (for example the family root, to go back)
sees it work on this environment and expects it after a deployment, where the previous member is still active.
The package's activation answer carries a note saying so; clio passes notes through, and guidance relays it. This
is also why the package REPORTS a failed switch (`failed-not-restored`) and never restores the previous member: a
restore onto a foreign member would pin it with exactly such a row.
