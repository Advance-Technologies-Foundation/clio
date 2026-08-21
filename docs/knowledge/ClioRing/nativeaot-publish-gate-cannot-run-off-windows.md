---
description: the mandatory ClioRing gate "dotnet publish -r win-x64 -p:PublishAot=true" fails on macOS and Linux with "Cross-OS native compilation is not supported", so a non-Windows session cannot satisfy it
applies-to:
  - clio-ring/ClioRing.Desktop/ClioRing.Desktop.csproj
  - AGENTS.md
date: 2026-08-19
---

**What is true** — `AGENTS.md` requires the Windows x64 NativeAOT publish as one of two mandatory Ring
validation commands. On a macOS or Linux host it stops before compiling anything:
`microsoft.dotnet.ilcompiler/<ver>/build/Microsoft.NETCore.Native.Publish.targets(63,5): error :
Cross-OS native compilation is not supported.` (reproduced on macOS with the current tree). The other
gate command, `dotnet test clio-ring/ClioRing.Tests -c Release`, runs everywhere.

**Why it is this way** — ILCompiler emits a native Windows image and needs the matching host toolchain
and linker; the .NET SDK refuses cross-OS AOT rather than producing something unusable. Nothing about it
is specific to Ring's code.

**What breaks if you ignore it** — the gate exists because contract or DTO changes can alter
source-generated serialization paths that JIT never exercises, so a green `dotnet test` is not a
substitute. Off Windows you cannot obtain that evidence: say so explicitly in the change summary and get
the publish run on a Windows host (or in CI) before the change is considered validated. Do not record
the gate as passed, and do not "work around" the error by dropping `-r win-x64` or `PublishAot` — that
publishes a different artifact and proves nothing about the AOT path.
