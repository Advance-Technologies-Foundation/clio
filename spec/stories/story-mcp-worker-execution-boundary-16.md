# Story 16: The `McpClient` guard cannot see the regression it exists to prevent

**Feature**: mcp-worker-execution-boundary
**Jira**: [ENG-95262](https://creatio.atlassian.net/browse/ENG-95262)
**ADR**: [adr-mcp-worker-execution-boundary.md](../adr/adr-mcp-worker-execution-boundary.md)
**Test plan**: [tp-mcp-worker-execution-boundary.md](../test-plans/tp-mcp-worker-execution-boundary.md)
**Stage**: 4 (hardens the Stage 4a structural assertion TC-U-401)
**Status**: ready-for-dev
**Size**: M

## As a
reviewer relying on a structural test instead of on everyone remembering ADR rule 12

## I want
the guard to fail when a relay type uses `McpClient` anywhere, including inside a method body

## So that
the exact regression the rule was written for is caught by the build rather than by a reader

## Design
- **What the guard inspects today.** `RelayTypes_ShouldNotReferenceTheSdkClient_WhenInspectedStructurally`
  (`clio.tests/Command/McpServer/WorkerMcpRelayTests.cs:43-69`) enumerates every type in the relay namespace
  and feeds it to `SignatureTypes` (`:698-717`), which yields field types, property types, method return types
  and parameter types. It then fails if any of those is `ModelContextProtocol.Client.McpClient` or
  `McpClientHandlers`. That is a SIGNATURE scan and nothing more.
- **So the regression it names in its own failure message passes it.** A future implementer who writes
  `var child = await McpClient.CreateAsync(transport, …);` as a local inside `OpenAsync`, uses it, and never
  exposes it on a field, property, parameter or return type, produces exactly the reordering dispatch layer
  rule 12 forbids — and the guard is green. Nothing the signature-level reflection in this test looks at can
  see it: a local variable's type is compiler-emitted debug information, and the call itself is a member
  reference inside the method's IL body.
- **That is why this is a story and not a two-line fix.** Catching it means reading the body, and there are
  two workable mechanisms: `System.Reflection.Metadata` over the compiled `clio` assembly (walk each relay
  type's `MethodDefinition`, read its `MethodBody`, resolve `MemberReference` / `TypeReference` handles and
  fail on the forbidden declaring types), or runtime reflection over the IL
  (`MethodBase.GetMethodBody().GetILAsByteArray()` plus `Module.ResolveMethod(token)` on the call operands).
  Both are real work and both need the assembly on disk, so pick
  one deliberately rather than bolting a string search onto the existing test — a source-text grep would be
  defeated by a `using` alias or a fully-qualified name split across lines, and would flag the comments and
  the `forbidden` array in the test itself.
- **Keep the existing signature assertion.** It is cheap, it is not wrong, and it catches the sloppier shape
  (a stored client) directly. The IL scan is an addition, not a replacement.
- Scope the forbidden set the way the current test does — `McpClient` and `McpClientHandlers` — and scope the
  inspected set to the relay namespace, so the guard keeps saying something specific instead of becoming a
  repository-wide ban that someone will suppress.
- Prove it fails first, the way TC-U-102 proves the metadata coverage gate is not vacuous: a planted
  method-body use must turn the test red. A guard nobody has ever seen fail is a guard nobody knows the shape
  of.

## Acceptance Criteria
- [ ] AC-01 — A relay type with a method-body-only `McpClient.CreateAsync` call fails the strengthened guard.
- [ ] AC-02 — The fail-first evidence is part of the change: the planted-offender case is demonstrated (a
      throwaway fixture type, or an explicitly negative-tested probe), not asserted in prose.
- [ ] AC-03 — The existing signature assertion still runs and still fails on a stored `McpClient` field,
      property, parameter or return type.
- [ ] AC-04 — The scan is limited to the relay namespace and to the two forbidden SDK client types; it does not
      become a repository-wide reference ban.
- [ ] AC-05 — The test runs on macOS, Linux and Windows with no path or toolchain assumption beyond the built
      assembly's own location.

## Tests
Unit TC-U-404 (`WorkerMcpRelayTests`, `Module=McpServer`), alongside the existing TC-U-401 structural case.

## Notes
The failure message the current test already carries is the right one and should survive verbatim — the
reordering it names (`0..5` arriving as `[5,4,2,3,0,1]`, unfixed by a parent-side FIFO) is the whole reason the
rule exists, and a reader who trips this guard needs that sentence, not a stack trace.
