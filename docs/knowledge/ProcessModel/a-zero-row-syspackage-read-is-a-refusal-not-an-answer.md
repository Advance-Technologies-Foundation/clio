---
description: a SysPackage read that comes back with zero rows is a REFUSAL, not an empty table - a DataService fault envelope parses cleanly and yields no rows under Success=true, so the version reader treats no-rows as not-read and warns, while one unresolved name stays silent
applies-to:
  - clio/Command/ProcessModel/IProcessVersionLibReader.cs
ticket: ENG-94374
date: 2026-09-14
---

**What is true** — an empty `SysPackage` result means the read was refused. A Creatio environment
always carries packages, so "no rows" is never a legitimate answer, and the version reader maps it to
`null` — which raises `versionReadWarning` and leaves every `packageName` absent, while the version
facts beside it stay established.

**Why it is this way, and why the obvious counter-argument is wrong.** `ClassifyingDataProvider` wraps
the registered `IDataProvider` and turns ATF's swallow-and-report failure into an exception, which
invites the conclusion that a refused read always arrives as a failure and this arm is unreachable.
That over-reads it. Per
[remotedataprovider-swallows-every-failure-into-success-false](../Common/remotedataprovider-swallows-every-failure-into-success-false.md),
a DataService **fault envelope** — the shape a server-side rejection of a restricted-NUI object takes —
parses without error, its `success` field is ignored, and it yields zero rows under `Success = true`.
Only a transport or parse fault becomes `Success = false`. `AGENTS.md` names `SysPackage` as exactly
the restricted-NUI case, so both shapes are reachable and only one of them throws.

**One unresolved name is a different answer and stays silent.** A member whose `PackageUId` has no row
loses its name with no warning: that is a gap in one field, not an unestablished standing, and routing
it through the warning channel would teach a caller to report the version standing as unknown over a
cosmetic miss. Only the whole-table case is announced, because there every member loses its name at
once and silence would render raw GUIDs.

**What breaks if you ignore it** — deleting the zero-row arm as unreachable publishes every member
with no package name and **no warning**, which is the one combination the contract on
`ProcessVersionFamilyMember.PackageName` forbids: the caller cannot tell "the names are complete" from
"the names could not be read", and answers a builder in GUIDs while believing it answered fully.
