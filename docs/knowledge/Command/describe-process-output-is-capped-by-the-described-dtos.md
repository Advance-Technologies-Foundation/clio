---
description: describe-process re-serializes the typed DescribedProcess/DescribedParameter DTOs, so any field a newer CrtProcessBuilder server adds is silently stripped from the output
applies-to:
  - clio/Command/ProcessModel/IProcessDescriber.cs
  - clio/Command/DescribeProcessCommand.cs
ticket: ENG-92127
date: 2026-08-19
---

**What is true** — `describe-process` is not a passthrough. The server response is deserialized into
the closed DTO graph in `clio/Command/ProcessModel/IProcessDescriber.cs`
(`JsonSerializer.Deserialize<DescribeProcessResultEnvelope>`, line ~74) and the command then
re-serializes that object graph to stdout (`DescribeProcessCommand.cs`, line ~70). Whatever the
server reports that has no matching property on `DescribedElement` / `DescribedParameter` /
`DescribedFlow` is dropped without a warning. Adding a server field therefore always requires a
matching clio DTO property in the same change.

**Why it is this way** — the typed graph is what gives the command its stable, documented JSON
shape and its `WhenWritingNull` omission of fields older servers do not report. The cost of that
stability is that the DTO, not the server, is the ceiling on what a caller can see.

**What breaks if you ignore it** — the failure is a false negative, and it misdirects. If you pick a
newly added server field as your indicator that a rebuilt `CrtProcessBuilder` package is live on a
stand, the field is invisible no matter what is deployed, so a successful deploy reads as a failed
one; ENG-92127 burned roughly eight deploy attempts on exactly this. Probe with a field the DTO
already carries (`source` / `value` survived where `direction` / `isResult` did not) or add the DTO
property first.
