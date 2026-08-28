---
description: CommandLineParser applies [Option(Default = true)] only when it parses a command line, so an options object built with new() or Activator.CreateInstance (MCP tools, YAML scenario runner) silently gets false unless the property also carries a C# initializer
applies-to:
  - clio/Command/CompressAppCommand.cs
  - clio/Command/RestoreDb.cs
  - clio/Command/McpServer/Tools/InstallerCommandTool.cs
  - clio/YAML/Step.cs
date: 2026-08-19
---

**What is true** — the `Default` value in `[Option(..., Default = true)]` is applied by
`CommandLineParser` while it binds a real command line. Nothing applies it when an options object is
constructed in code, and clio constructs options objects in code on two live paths: every MCP tool
that builds a command's options with an object initializer (for example
`InstallerCommandTool.DeployCreatio` building `PfInstallerOptions`), and the YAML scenario runner
(`clio/YAML/Step.cs`, `Activator.CreateInstance`). On those paths a `bool` whose only default is the
attribute arrives as `false`.

`RestoreDb.cs` shows the defensive shape - `Default = true` on the attribute **and** `= true` as a
property initializer, so both paths agree. `CompressAppCommand.SkipPdb` shows the exposed shape:
`Default = true` with no initializer.

**Why it is this way** — the attribute is parser metadata, not a language construct; a property
initializer is the only default the CLR itself honours. clio grew the in-code construction paths long
after the options classes were written for the parser alone.

**What breaks if you ignore it** — the flag is simply off, on the MCP and scenario surfaces only, and
the command still exits 0. There is no error and no warning: the caller reads the CLI help, sees the
documented default, and gets the opposite behaviour with no signal that a different construction path
was taken. When you add or review an `[Option(Default = ...)]`, put the same value in a property
initializer next to it. See also
`docs/knowledge/Command/yaml-scenario-runner-bypasses-argument-normalization.md`, the same class of
bypass for argument normalization.
