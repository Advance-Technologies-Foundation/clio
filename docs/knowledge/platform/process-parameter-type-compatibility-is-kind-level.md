---
description: Creatio accepts a process parameter mapping across different DataValueType UIds as long as the kind matches (ShortText to Text), so compatibility must be compared with CreatioDataValueType.GetKind, never by UId equality
applies-to:
  - clio/Common/CreatioDataValueType.cs
  - clio/Command/ProcessModel/Schema.cs
ticket: ENG-92127
date: 2026-08-19
---

**What is true** — "the parameter types must be the same" is a kind-level rule on the platform, not
an identity rule on `DataValueType`. A real working process maps a process parameter typed
`ShortText` (`{325A73B8-…}`, Text50) onto an element parameter typed `Text` (`{8B3F29BB-…}`) and the
server accepts it; the designer's source picker only promises the types "correspond to" each other.
The comparison a clio-side guard or a documented rule must make is
`CreatioDataValueType.GetKind(a) == CreatioDataValueType.GetKind(b)`
(`clio/Common/CreatioDataValueType.cs`, the `CreatioDataValueKind` table), not UId or type-name
equality. The `DataValueType` UId constants in `clio/Command/ProcessModel/Schema.cs` are for
resolution, not for compatibility checks.

**Why it is this way** — Creatio's own binding validation is expressed over .NET value types and
implicit casts, which is *looser* still (it accepts Email to Phone, Date to Time, Integer to Money).
Kind equality sits between that and exact identity: strict enough to be meaningful to a caller,
never stricter than what the server will accept.

**What breaks if you ignore it** — a UId-equality guard rejects mappings the platform performs
happily, including processes that already exist and work on the stand. The error blames the caller's
parameter types, so it reads as a modelling mistake rather than as an over-strict clio check, and the
only way out is to bypass the guard.
