---
description: a compile-creatio call whose process-name or package-name is not bound runs a FULL configuration compile; clio builds older than process-name drop it silently, and knowledge cannot require an argument
applies-to:
  - clio/Command/McpServer/Tools/CompileCreatioTool.cs
ticket: ENG-92711
date: 2026-09-28
---

**What is true** — every scope argument of `compile-creatio` is optional, so a call whose `process-name` or
`package-name` did not bind is a request to compile everything. From ENG-92711 the tool refuses any key its
record cannot bind. Every clio build before that (all releases that predate `process-name`) drops the key
without a word — see [mcp-arg-records-swallow-unbound-fields.md](mcp-arg-records-swallow-unbound-fields.md) —
and runs `clio cc --all`. The knowledge library cannot stop an old clio from receiving guidance that says
`process-name`: a bundle declares `compatibility.clio` as a range (`8.1.0`–`8.1.999`) and `requirements.tools`
by tool NAME, and clio advertises `mcpToolContract 1.1.0` for both old and new builds, so nothing in the
contract says whether `compile-creatio` knows `process-name`.

**Why it is this way** — knowledge and clio ship separately (`update-knowledge` pulls guidance without
updating clio), and the MCP binder is loose on purpose. The refusal fixes only the builds that carry it.

**What breaks if you ignore it** — measured on a .NET 8 stand on 2026-09-28: an agent session whose MCP server
was clio 8.1.0.121 sent `compile-creatio process-name=...` nine times and got nine full compiles
(`Build.log`: `force=True; packagesNamesToCompile=[<none>]`, `compiled: 25`, 2.5–3.7 min each, the runtime
reloaded for every user each time) instead of ~1.5-minute `CompileProcess` runs. The answers looked like
compile output, so nothing flagged it. Guidance that tells an agent to use `process-name` must tell it to
confirm the argument first — `get-tool-contract` for `compile-creatio` lists it only on a build that has it.
