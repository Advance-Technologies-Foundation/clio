---
description: the MCP boundary surfaces the INNER-MOST exception message (SurfacedExceptionMessage.Resolve), so a curated caller-facing error is replaced by the raw inner text unless its exception implements Clio.Common.IAuthoritativeErrorMessage
applies-to:
  - clio/Common/SurfacedExceptionMessage.cs
  - clio/Common/IAuthoritativeErrorMessage.cs
  - clio/Command/McpServer/McpToolErrorFilter.cs
  - clio/Command/McpServer/Tools/ClioRunTool.cs
ticket: ENG-93365
date: 2026-08-19
---

**What is true** — both MCP error paths (`McpToolErrorFilter` for a direct tool call, `ClioRunTool` for
a nested `clio-run` dispatch, the entry point ClioRing uses) surface the message through
`SurfacedExceptionMessage.Resolve`, which walks `InnerException` to the bottom. If you build a curated
message and keep the underlying failure as the inner exception for diagnostics, the agent sees the
inner exception's text, not yours. The walk stops only at an exception implementing
`Clio.Common.IAuthoritativeErrorMessage` (`NonJsonServiceResponseException` is the reference
implementation). The two paths deliberately share one resolver rather than each owning a copy of the
walk.

**Why it is this way** — unwrapping is right by default: a `TargetInvocationException` or another
dispatch wrapper would otherwise hide the real cause from the agent. The marker is the narrow opt-out
for the cases where the outer message is the product, not the wrapper.

**What breaks if you ignore it** — the failure is silent in the worst way: the CLI prints your curated
guidance and the MCP caller gets the raw text the guidance existed to replace, so a passing CLI check
proves nothing about the agent's experience. This latently defeated the package-dependency recovery
guidance in `RemoteEntitySchemaDesignerClient` until that path threw a marked exception. Any new
exception whose message is written for the caller must implement the marker, and it must be covered on
BOTH dispatch paths — a test on one path stays green while the other reverts.
