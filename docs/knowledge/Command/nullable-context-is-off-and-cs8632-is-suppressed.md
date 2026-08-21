---
description: nullable reference types are disabled project-wide (no Nullable property anywhere, no #nullable directive in clio) and CS8632 is in clio.csproj NoWarn, so string? annotations compile silently with no compiler enforcement and the null-forgiving ! operator is a pure no-op
applies-to:
  - clio/clio.csproj
date: 2026-08-19
---

**What is true** — clio has no `Directory.Build.props`, no `<Nullable>` property in `clio/clio.csproj`
(or any other project file), and not a single `#nullable` directive in `clio/**/*.cs`. The nullable
context is therefore off for the whole assembly. The annotations the code is full of - `string?`,
`EnvironmentSettings?`, `int?` on reference-typed members - are documentation only: the compiler runs
no flow analysis, emits no CS8600/CS8602/CS8618, and a `null` assigned to an unannotated reference is
legal everywhere. The `!` null-forgiving operator suppresses a warning that is never produced, so it
is a no-op with no effect on generated code.

**Why it is this way** — `CS8632` ("nullable annotation in code without a `#nullable` context") is
listed in `<NoWarn>` in `clio/clio.csproj`, next to CS0108, CS0659, CS0661 and CS0169. That
suppression is what lets the annotations exist without a build full of warnings, and it also removes
the only signal that would have told anyone the context is off.

**What breaks if you ignore it** — you read `string?` versus `string` on a signature as a checked
contract and skip a null guard the compiler was never going to demand; the NullReferenceException
shows up at runtime in a caller that "cannot" pass null. In the other direction, removing `!`
operators (Sonar S8970 flags them) is safe here precisely because they are inert - but that is only
true while the nullable context stays off. Turning `<Nullable>` on, even for one file with a
`#nullable enable` directive, immediately makes hundreds of previously silent sites warn and changes
what those `!` operators mean.
