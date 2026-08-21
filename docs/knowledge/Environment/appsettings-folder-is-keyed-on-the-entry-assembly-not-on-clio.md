---
description: SettingsRepository.AppSettingsFolderPath derives its path from Assembly.GetEntryAssembly()'s AssemblyCompany/AssemblyProduct, so any host other than clio.exe reads a different appsettings.json unless CLIO_HOME is set
applies-to:
  - clio/Environment/ConfigurationOptions.cs
  - clio/Environment/ClioRuntimePaths.cs
date: 2026-08-19
---

**What is true** — `SettingsRepository.AppSettingsFolderPath` builds
`<LOCALAPPDATA|HOME>/<AssemblyCompany>/<AssemblyProduct>` from **`Assembly.GetEntryAssembly()`** —
the process entry point, not the clio assembly the code lives in. A throwaway console, a separate
tool, or any other host that references `clio.csproj` therefore resolves its **own** company/product
folder, finds no `appsettings.json` there, and gets a freshly bootstrapped stub. `CLIO_HOME`, when
set, overrides the whole root verbatim and is the only supported way to point such a host at the real
clio home.

**Why it is this way** — the path predates any notion of clio being consumed as a library, and the
entry-assembly attributes were the cheapest per-user location available. `CLIO_HOME` was added as the
single relocation switch (see `clio/Environment/ClioRuntimePaths.cs` and
`docs/architecture/clio-home-consolidation.md`) rather than by hard-coding clio's identity here.

**What breaks if you ignore it** — `settingsRepository.GetEnvironment("<name>")` returns `null` for
an environment you can see with `clio list-environments`, and every downstream call fails as
"environment not registered". Nothing reports that a different settings file was read, so the
symptom reads as a corrupted or lost registration rather than a path-resolution difference. Set
`CLIO_HOME` to the real clio home before the first settings read in any out-of-process host,
including one-off verification runners.
