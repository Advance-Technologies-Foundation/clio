---
description: the nuget2dll path confinement walks reparse points only down to the workspace root, because DbHubPathSafety's walk to the filesystem root refuses every workspace under a linked path
applies-to:
  - clio/Common/NugetMaterializer.cs
  - clio/Common/PropsBuilder.cs
  - clio/Common/FileSystem.cs
ticket: GH-1311
date: 2026-09-12
---

**What is true** — `FileSystem.HasLinkWithin` walks from a leaf path up to the confinement root
(`<workspace>/packages` or `<workspace>/.nuget`) and stops there. It deliberately does NOT walk on to
the filesystem root, which is what the existing `DbHubPathSafety.RefuseUnsafeTarget`
(`clio/Common/DbHub/DbHubTomlStore.cs`) does. The root itself is exempt: it is the developer's own
workspace layout, and a `packages/` folder that is a junction onto another drive is an ordinary
Windows setup that worked before.

**Why it is this way** — a workspace legitimately sits under a linked path. On macOS the temp
directory itself is reached through `/var` -> `/private/var`, and `Path.GetFullPath` does not
resolve that, so an ancestry walk to `/` reports a reparse point for every workspace created under
`Path.GetTempPath()`. Only the segments the workspace owns can be required to be real directories.

**What breaks if you ignore it** — replacing this walk with `DbHubPathSafety.RefuseUnsafeTarget`
makes `switch-nuget-to-dll-reference` refuse to run at all on macOS temp workspaces, and on any
developer machine whose checkout lives under a symlinked home or volume path. The command exits 1
with "path is a symbolic link or a junction" and converts nothing, with no way for the user to tell
that the link it objects to is not theirs.
