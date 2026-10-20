---
description: Team_Atf_ClioMcpE2eProcessDesigner installs CrtProcessBuilder with install-process-builder from the clio.dll it just built from the branch under test, in a Prepare step that exists only in TeamCity, and sets McpE2E__RequireProcessBuilder=true so a missing or outdated package fails the run instead of ignoring the whole category on a green build
applies-to:
  - .github/workflows/teamcity-mcp-e2e.yml
  - clio.mcp.e2e/Support/Mcp/ProcessDesignerE2ESupport.cs
  - clio.mcp.e2e/Support/Configuration/McpE2ESettings.cs
  - clio/Command/InstallProcessBuilderCommand.cs
  - clio/Command/InstallBundledPackageCommand.cs
ticket: ENG-97005
date: 2026-10-20
---

**What is true** - the process-designer lane's step "Prepare site" builds `clio.mcp.e2e` from the branch
under test, like the existing lane, and then runs `dotnet clio.mcp.e2e/bin/Debug/net8.0/clio.dll
install-process-builder -e dev` (when `InstallProcessBuilder` is `true`: the lane's default, and pull-request
runs send it too). The command installs `<folder of that clio.dll>/CrtProcessBuilder/CrtProcessBuilder.gz`,
which the build copies from `clio/CrtProcessBuilder/`, waits for the restart and fails unless
`ProcessDesignService` answers. The lane also sets `env.McpE2E__RequireProcessBuilder=true`, which makes
`ProcessDesignerE2ESupport.IgnoreUnlessProcessBuilderIsRequired` FAIL a test that finds the package
missing, older than it needs, or without the capability it tests. The step exists only in the TeamCity
configuration. It is also the only live check of the install command itself, which no fixture names:
`processDesignerLane.fullRunPaths` in `mcp-e2e-selection.json` runs the whole category for a change to it.

**Why it is this way** - the bundled archive is part of what a clio pull request changes, so the stand has
to carry that archive: a rebundle is tested by its own bytes. The zip the package repository's Jenkins job
leaves on DFS (`ComposableApps\CrtProcessBuilder\CI\.non-versioned-branches`) carries a CI
`Files/app-descriptor.json` and is not what clio ships. The fixtures skip on a stand that is merely
behind, which is right on a developer stand and wrong on a lane that installed the package itself.

**What breaks if you ignore it** - drop the install, run it through a globally installed `clio` (a NuGet
release bundles an older archive of its own) or lose the switch, and the lane reports green with the
category ignored, because the fixtures read "missing or behind" as a reason to skip. Install anything but
the build output and a rebundle pull request is tested against bytes it does not ship. Lowering a
`[RequiresPackage]` floor to turn this lane green is the trap described in
`docs/knowledge/Tests/the-atf-mcp-e2e-check-goes-green-on-its-own.md`.
