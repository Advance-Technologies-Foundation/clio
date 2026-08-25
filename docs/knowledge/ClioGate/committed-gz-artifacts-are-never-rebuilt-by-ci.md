---
description: clio/cliogate/*.gz are hand-committed binaries that no workflow regenerates, so a change under cliogate/Files/cs/** ships stale unless the archives are rebuilt in the same pull request - and nothing in the diff, the tests or the version files signals the drift
applies-to:
  - cliogate/Files/cs/
  - clio/cliogate/cliogate.gz
  - clio/cliogate/cliogate_netcore.gz
  - build.ps1
date: 2026-08-23
---

**What is true** — `clio/cliogate/cliogate.gz` and `cliogate_netcore.gz` are the artifacts a user
actually installs: `InstallGateCommand.GetPackagePath()` reads
`<ExecutingDirectory>/cliogate/<name>.gz`, and `clio.csproj` copies `cliogate\**` to output. They are
committed binaries produced by `build.ps1` (or the manual macOS steps in AGENTS.md). No workflow
rebuilds them: `.github/workflows/build.yml` compiles and tests `cliogate/**` but never runs the
`compress` step, and the nuget release workflow does not either.

**Why it is this way** — the gate targets `net472` against a specific CreatioSDK, so the archive is
produced once on a developer machine rather than per CI run.

**What breaks if you ignore it** — every edit under `cliogate/Files/cs/**` that does not also
rebuild both archives ships source-only. The whole feedback loop reads green while it happens:
`cliogate.tests` passes against source the deployed binary does not contain, `clio/docs/commands/**`
quotes messages the installed gate never emits, and `descriptor.json` / `version.txt` are unchanged
by definition, so a site already on that `PackageVersion` reads as current. Observed on PR #1123,
where three merged fixes (an S1168 pair, the `DescribeAmbiguity` refusal rewrite, and an S3011
`BindingFlags` change) were all absent from both committed archives.

**How to check** — decompress both archives (`clio extract-pkg-zip <gz> -d <dir>`) and grep the
payload for a symbol the change introduced, using an untouched sibling symbol as the sensitivity
control. Rebuilding needs no version bump: `version.txt` and `descriptor.json` stay as they are
unless the gate is being released.
