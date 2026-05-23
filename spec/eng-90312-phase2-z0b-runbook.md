# ENG-90312 Phase 2 — Z0b host smoke-test runbook (Claude Code)

Status: prerequisite for Z1.
Spec: [eng-90312-mcp-tools-consolidation-phase2.md](eng-90312-mcp-tools-consolidation-phase2.md) §3.6 Gate A.
Scope: Claude Code only. The two other hosts (Claude Desktop, Cursor) are deferred as follow-up after Phase 2 lands.

## Goal

Verify that Claude Code can:
1. Parse the 52-branch `anyOf` schema published by clio's MCP server without truncating or rejecting it.
2. Display every `anyOf` branch with its per-command fields when the user inspects the tool.
3. Pick the correct branch when invoking the consolidated tool with a representative payload.

The check is on the SDK-published 52-branch probe — *not* the real `clio-run` (Z1 doesn't exist yet). If Claude Code handles the probe correctly, the real `clio-run` will publish a similar-shape schema.

## Preconditions

- On branch `phase2-pivot` (already created in this session).
- `dotnet build clio.tests/clio.tests.csproj` is green.
- `dotnet test --filter "FullyQualifiedName~ClioRunSchemaProbeTests"` reports `Z0-PROBE-SCHEMA-BYTES=77595`, `Z0-PROBE-ANYOF-BRANCHES=52`, test passes. (In-process measurement; this runbook adds the cross-process verification.)

## Step 1 — promote the probe to a real MCP tool

The probe currently lives in `clio.tests` so the production server doesn't expose it. To smoke-test it through `clio mcp-server`, move it into `clio` temporarily.

```bash
# 1. Move the probe args + a wrapper tool into clio production source.
git mv clio.tests/Command/McpServer/Z0ProbeArgs.cs clio/Command/McpServer/Tools/Z0ProbeArgs.cs

# 2. Edit the namespace in Z0ProbeArgs.cs:
#    from: namespace Clio.Tests.Command.McpServer.Z0Probe;
#    to:   namespace Clio.Command.McpServer.Tools;
```

Then create `clio/Command/McpServer/Tools/Z0ProbeTool.cs`:

```csharp
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

[McpServerToolType]
public sealed class Z0ProbeTool {
    [McpServerTool(Name = "clio-run-probe", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("Z0 probe — 52-branch anyOf. Throwaway: remove before Z1.")]
    public static object Probe(
        [Description("Probe args — 52-branch anyOf union.")] [Required] ClioRunArgsProbe args)
        => new { ok = true, command = args.GetType().Name };
}
```

Register in `clio/BindingsModule.cs` next to other tool registrations (search for `AddTransient<DataForgeTool>` and add a sibling line `services.AddTransient<Z0ProbeTool>();`).

Then:

```bash
dotnet build clio/clio.csproj
dotnet test clio.tests/clio.tests.csproj --filter "FullyQualifiedName~McpToolBudgetTests"
```

The ratchet test will now report 76 (75 + 1 probe). **Do not commit this state** — it's a throwaway. Confirm the build is green and the ratchet message lists `clio-run-probe`.

## Step 2 — wire the local clio into Claude Code

Find the absolute path to the built dll:

```bash
ABS=$(cd clio/bin/Debug/net10.0 && pwd)/clio.dll
echo $ABS
```

Register the server with Claude Code (the `claude mcp` CLI manages local MCP server registrations):

```bash
claude mcp add clio-probe --scope local -- dotnet "$ABS" mcp-server
claude mcp list                              # confirm clio-probe is registered
```

Restart Claude Code (or open a fresh conversation) so the new server is picked up.

## Step 3 — confirm tools/list exposes clio-run-probe

In a Claude Code conversation, run:

```
/mcp
```

The output should show the `clio-probe` server with status `connected`. Expand its tool list — `clio-run-probe` must appear.

Then ask Claude (in the same conversation):

> List every command available in the clio-run-probe tool and report the count.

PASS criterion: Claude correctly enumerates 52 commands (`probe-cmd-01` through `probe-cmd-52`).

If Claude reports a lower count, schema truncation has happened — record the number and stop here.

## Step 4 — invoke one branch

Ask Claude:

> Call clio-run-probe with command=probe-cmd-17, environment-name=dev, mode=environment, target-path=/tmp/foo, dry-run=true.

PASS criterion: Claude assembles the call with the discriminator and per-branch fields visible in the published schema, the call returns `{ ok: true, command: "ProbeArgs17" }`, and no schema-validation error is raised by the host.

If Claude either refuses to call the tool or invents a different argument shape (e.g., omits `command`, picks fields not in the schema), record the symptom.

## Step 5 — baseline sanity (BusinessRuleAction parity)

This isolates "host broke our 52-branch payload" from "host is misbehaving today on every polymorphic schema". Ask Claude:

> What are the possible action types in the `rule` parameter of `create-entity-business-rule`?

PASS criterion: Claude lists 6 types — `make-required`, `make-not-required`, `make-editable`, `make-read-only`, `set-values`, `apply-filter`.

If the 6-branch precedent also fails, the host is broken today and Gate A measurement is contaminated — pause Z0b and investigate the host before judging clio.

## Step 6 — cleanup

```bash
claude mcp remove clio-probe --scope local
git restore clio.tests/Command/McpServer/Z0ProbeArgs.cs
git rm -f clio/Command/McpServer/Tools/Z0ProbeArgs.cs clio/Command/McpServer/Tools/Z0ProbeTool.cs
# revert the BindingsModule.cs edit
git diff clio/BindingsModule.cs                       # confirm only the AddTransient<Z0ProbeTool> line was added
git checkout clio/BindingsModule.cs
dotnet build                                          # final green build with no probe
```

## Reporting back

After Steps 3–5 paste back:

| Step | Result | Notes |
|---|---|---|
| 3 — tools/list shows clio-run-probe with 52 commands | PASS / FAIL — count: __ | … |
| 4 — one-branch invocation succeeds | PASS / FAIL | … |
| 5 — BusinessRuleAction 6-branch baseline | PASS / FAIL | … |

Gate A verdict:
- All three PASS → proceed to Z1.
- Step 5 FAIL → pause Z0b, investigate the host, do not infer about clio.
- Step 3 or 4 FAIL but Step 5 PASS → host parser does not handle our 52-branch schema; fall back to domain split (re-open architecture review per §3.2).
