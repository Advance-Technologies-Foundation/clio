---
description: the data-binding commands refuse a symbolic link or junction anywhere below the workspace root, the packages folder included, while the nuget2dll writes let packages itself be a link; the difference is deliberate because the MCP caller chooses workspace-path and can plant that link
applies-to:
  - clio/Command/DataBindingCommand.cs
  - clio/Common/FileSystem.cs
date: 2026-11-15
---

**What is true** — `DataBindingService.EnsureConfined` and the image-content read call
`IFileSystem.HasLinkWithin(<workspace root>, path)`. The walk probes `packages` and every segment below it and
stops at the workspace root. `NugetMaterializer` and `PropsBuilder` call the same helper with `<workspace>/packages`
as the root, so for them a `packages` folder that is a junction onto another drive is fine (GH-1311).

**Why it is this way** — the data-binding commands are MCP tools, and their `workspace-path` is an argument of the
caller. An MCP host may sandbox that caller, but clio runs as the person, so the host can only check the path it was
given; whatever lies below it is clio's to keep in place. A caller that can create files can create
`<workspace>/packages` as a link to any folder, and with the nuget2dll rule every check below it passes. Telling the
person's own CLI call apart from an MCP call inside the shared service is not reliable: `Program.IsMcpServerMode` is
false in the HTTP MCP host and for direct callers.

**What breaks if you ignore it** — "aligning" data binding with the nuget2dll rule lets one link at
`<workspace>/packages` carry the binding writes, the recursive `Localization` delete and image reads to any folder
the person can reach, and lets an image value copy any file the person can read into `data.json`. The price of the
stricter rule is that a workspace whose `packages` folder is a junction cannot use the data-binding commands until
it holds a real folder.
