---
description: CLIO_HOME relocates appsettings.json but NOT the knowledge cache when knowledge.root-path is an absolute path in that file - copying a real settings file into a sandbox home keeps writing into the developer's live knowledge root
applies-to:
  - clio/Command/McpServer/Knowledge/KnowledgeRootPathProvider.cs
  - clio.mcp.e2e/McpSharedHomeSetUpFixture.cs
date: 2026-08-30
---

**What is true** — `KnowledgeRootPathProvider.GetOrCreateRoot` reads `knowledge.root-path` from settings
and, when it is non-empty, uses it verbatim. `CLIO_HOME` only decides *which* `appsettings.json` is read.
So the settings-directory default is a fallback, not a rule: the moment a settings file names an absolute
root, that root wins no matter where `CLIO_HOME` points.

The consequence is that the obvious way to build an isolated sandbox — copy the developer's real
`appsettings.json` into a temp `CLIO_HOME` so the environments and credentials come along — produces a home
that *looks* isolated and writes its knowledge cache straight into the developer's live
`…/creatio/clio/knowledge`. `install-knowledge` in that "sandbox" materializes a real source directory,
with its ownership marker, in the real cache.

`clio.mcp.e2e/McpSharedHomeSetUpFixture.cs` gets this right and is the model to copy: it seeds from the
real settings file and then **overwrites the whole `knowledge` node** with a `root-path` under its own
fixture directory and an empty `sources` map. Seeding without that rewrite is the bug.

**Why it is this way** — an absolute `root-path` is a deliberate feature: the cache can be large and an
operator may want it off the profile volume. Making `CLIO_HOME` silently override it would break that, and
there is no separate "knowledge home" variable.

**What breaks if you ignore it** — nothing fails and nothing warns. Two sessions on this machine hit it
within a day of each other: one registered a source into what it believed was a sandbox and found the cache
in the live root afterwards; the other ran E2E suites for hours against a `CLIO_HOME` whose copied settings
still named the real root. The residue is the worst kind — a source directory carrying an ownership marker
whose registration exists in no settings file anyone still has, so no `clio` command lists it, no command
deletes it, and the next `add-knowledge-source` for that alias is refused with "not owned by Clio". When
building a sandbox home, override `knowledge.root-path` in the same breath as copying the file, and verify
it afterwards rather than assuming — `(Get-Content <sandbox>/appsettings.json | ConvertFrom-Json).knowledge.'root-path'`
must not point outside the sandbox.
