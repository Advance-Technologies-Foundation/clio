---
description: the ENG-95885 flat-argument classifier refuses unknown keys only for a RESIDENT tool and only in a FLAT tools/call payload; a long-tail tool is never classified at all and an already-wrapped {"args":{...}} call is passed through untouched, so the per-tool overflow-bag check is still required
applies-to:
  - clio/Command/McpServer/McpToolErrorFilter.cs
  - clio/Command/McpServer/Tools/McpFlatArgumentContract.cs
  - clio/Command/McpServer/Tools/ProcessDesigner/
ticket: ENG-98566
date: 2026-09-16
---

**What is true** — the normalizer has TWO independent exclusions, and naming only one of them gets the
next reader to the wrong conclusion.

**Exclusion 1 — residency.** `TryRefuseCallArgumentsCore` bails at `TryGetToolMethod`, because
`MatchedPrimitive` is null for a tool that is not advertised in `tools/list`. Only the ~20 types in
`McpCoreToolProfile.CoreToolTypes` are resident; the whole long tail is therefore never classified, in
ANY payload shape. This is deliberate and documented in the filter itself: a long-tail tool is normally
reached through `clio-run`, which owns its own wrapped/flat recovery.

**Exclusion 2 — payload shape.** For a tool that IS resident, `McpToolErrorFilter`'s normalizer classifies a `tools/call` payload and
refuses one carrying an unknown top-level key. That classification applies to the **flat** shape only. A
payload that is *already wrapped* — `{"args":{...}}`, the single wrapper key — is documented as "untouched"
and is handed straight to the binder, which drops any key inside the wrapper that matches no
`[JsonPropertyName]`. The published schema keeps `required: ["args"]`, so the wrapped shape is the one the
tool contract asks an agent for: the shape the classifier does **not** inspect is the normal one.

**Why it is this way** — the normalizer exists to rewrite a flat payload into the wrapper, and rewriting is
the only point at which it has to decide whether the keys are real. A correctly wrapped call needs no
rewrite, so there is nothing for the classifier to decide and no reason for it to walk the inner object.
Making it walk the wrapper would duplicate, at the filter layer, a per-tool judgement about which fields are
tolerated — the thing `McpRecoversUnknownArguments` and the per-tool alias tables exist to keep local.

**What breaks if you ignore it** — you read `McpFlatArgumentContract` or the AGENTS.md section it documents,
conclude that unknown arguments are now refused globally, and skip the `[JsonExtensionData]` bag plus its
`BuildLegacyAliasError` check on a new args record. The tool then answers a mis-keyed call with a plausible
success. Measured on `validate-process-graph` (ENG-98566) — note that BOTH exclusions applied to it at once, since it is
long-tail; a RESIDENT tool is exposed by the wrapped-shape exclusion alone, which is why the residency half has
to be stated rather than left implicit: `{environment-name, process-name:"UsrOrder_Handle"}`
and `{environment-name}` returned **byte-identical** responses — `success:true, has-errors:true,
findings:[R3 "Process has no start event."]` — about a process the tool had never read and which does have a
start event. Two consecutive sessions hit it and one acted on the finding. See
[[mcp-arg-records-swallow-unbound-fields]] for the remedy itself; this record exists because the normalizer
makes that remedy look obsolete when it is not.
