---
description: the mandatory Windows x64 NativeAOT publish of ClioRing.Desktop fails at the native LINK step with "vswhere.exe is not recognized" unless the Visual Studio Installer directory is on PATH - and a vcvars64 developer shell does NOT put it there
applies-to:
  - clio-ring/ClioRing.Desktop/
ticket: ENG-99856
date: 2026-09-22
---

**What is true** — the NativeAOT publish that `AGENTS.md` makes mandatory for every Ring change,

```
dotnet publish clio-ring/ClioRing.Desktop/ClioRing.Desktop.csproj -c Release -r win-x64 \
  --self-contained true -p:PublishAot=true
```

fails on a developer machine with

```
error MSB3073: The command ""'vswhere.exe' is not recognized as an internal or external command,
;operable program or batch file.;...\Hostx64\x64\link.exe" @"...\native\link.rsp"" exited with code 123
```

`vswhere.exe` lives at `C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe` and that
directory is **not** added by `vcvars64.bat` — running the publish inside a Visual Studio developer shell
changes nothing. Put the Installer directory on `PATH` for the publish and it succeeds:

```powershell
$env:PATH = "C:\Program Files (x86)\Microsoft Visual Studio\Installer;" + $env:PATH
dotnet publish clio-ring/ClioRing.Desktop/ClioRing.Desktop.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishAot=true
```

**Why it is this way** — the ILCompiler targets shell out to the MSVC linker and resolve the toolchain
through `vswhere`, which ships with the VS *Installer* rather than with the build tools. A Build Tools-only
install has the linker and the vcvars script but leaves the Installer directory off every environment
`vcvars64.bat` composes.

**What breaks if you ignore it** — you read the failure as a Ring defect, or as the AOT gate genuinely
failing, and either chase a phantom or skip the gate. The message is misleading twice over: it names
`link.exe` (which is present and fine) and reports exit code 123 from a shell that never ran, so nothing in
it points at `vswhere`.

Read the failure POINT, because it tells you what was already proved. The run reaches the native link only
**after** the IL compilation and the trim/AOT analysis have completed, so a failure here means the part the
gate actually cares about — no new `IL2026` / `IL3050` — already passed. That is worth stating when you
report the gate, but it is not a substitute for the link: a successful publish is what the policy asks for,
and it is one `PATH` entry away.
