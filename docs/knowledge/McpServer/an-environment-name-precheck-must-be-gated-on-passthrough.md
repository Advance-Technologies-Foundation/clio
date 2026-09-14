---
description: a tool that refuses a missing environment-name before resolving is unreachable under HTTP credential passthrough, because ToolCommandResolver rejects an explicit environment argument in that mode - gate the pre-check on BaseTool.IsPassthroughActive
applies-to:
  - clio/Command/McpServer/Tools/BaseTool.cs
  - clio/Command/McpServer/Tools/ToolCommandResolver.cs
  - clio/Command/McpServer/Tools/GetThemeTool.cs
ticket: ENG-93991
date: 2026-08-25
---

**What is true** — under `mcp-http` credential passthrough the target environment arrives in the
`X-Integration-Credentials` header, and `ToolCommandResolver.Resolve` **throws** when the call also
carries an explicit `uri`/`login`/`password`/`client-id`/`client-secret`/`environment` argument
(`HasExplicitCredentialArgs`). So under passthrough a caller cannot supply `environment-name` at
all. A tool that hard-requires it up front must gate that check:
`if (!IsPassthroughActive && string.IsNullOrWhiteSpace(args.EnvironmentName))`.
`BaseTool.IsPassthroughActive` is the seam (it reads the existing `ICredentialPassthroughToolGuard`),
and it is the counterpart of `RejectIfPassthroughUnsupported` for tools that ARE passthrough-capable.

**Why it is this way** — silently ignoring a named environment would let a caller believe it took
effect, so the resolver refuses the mixed input instead. The refusal is correct; the ungated
pre-check in front of it is not.

**What breaks if you ignore it** — the tool becomes unreachable in passthrough mode and fails with
`environment-name is required and cannot be empty`, which reads as a caller mistake rather than a
tool defect: the caller did supply the environment, through the only channel that mode accepts. No
unit test on the default path catches it, because stdio and default HTTP keep honouring the
argument. Pin both branches.
