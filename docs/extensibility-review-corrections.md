# Extensibility review corrections — 5 September 2026

This records the response to Claude review rev_13802082333f4870. The target is the new Clio 10 working solution, not the removed Clio 8 architecture.

| Accepted concern | Correction | Concrete proof |
|---|---|---|
| HTTP-only session contract limits reuse | ABI 2 capability provider with typed interfaces; Core remains capability-neutral | HTTP-only V1 and HTTP+filesystem V2 select independently; real filesystem workflow |
| String-only arguments and no useful result payload | Portable nested BCL input snapshots, descriptors and data-only result payload | Typed TextSummary to managed caller; decimal/object inputs and JSON output through both adapters |
| Nested workflows cannot choose their own inputs | InvokeAsync accepts replacement arguments, omitted means empty | Same child called twice with distinct file/label values; caller mutation and parent isolation tests |
| Central CLI/MCP maps block partner exposure | Generic discovery and execution adapters | Separately compiled PartnerHost registers workflows using existing adapter callbacks |
| Partner contracts live in implementation libraries | IClioWorkflow, context and requirements reside in Contracts | Partner assembly has only Contracts + DI dependencies |
| Non-HTTP operations still need fake HTTP setup | Explicit --local adapter startup; optional BaseUri | CLI/MCP filesystem tests use no target/user/platform |
| Nested operations repeat initial login | Successful initial authentication cached in the session | Loopback asserts one login for two LoginAsync calls plus execution |
| Cleanup can release ownership incorrectly | Await session disposal before releasing gate; initialization failures dispose partial sessions | Delayed/failing cleanup and invalid capability tests |

No additional architecture layer was introduced. Core still chooses one complete bundle per root workflow. Composition authors declare the required capabilities, sequence operations and interpret outcomes. Clients choose presentation. Partner code is trusted and explicit registration remains the extension mechanism.

The new public contracts intentionally break the earlier experimental ABI; contract version 2 is explicit. MCP resident names changed to the generic execute bridge. Clio 8 and its public tool contracts were not changed.

Deliberate boundaries remain: same-target root serialization, sequential children, no cross-process coordination, no hot replacement, no automatic downloads, no plugin folder discovery, no CI redesign. Bundle fixtures do not prove conflicting private dependency versions. Result payloads must be data records compatible with the consuming adapter's serializer.

See [architecture](architecture.md) for the design and [validation](validation.md) for current test, package, live-environment and review evidence.

## Claude follow-up assessment

Claude review `rev_a1e0b75001b54cf4` completed with no Blocker/P1/P2 and confirmed all four architectural concerns closed. This is a source review, not independent execution evidence.

Accepted and corrected observations:

- The local-only missing-target documentation was too specific about the error code; it now describes prerequisite validation. With an empty identity, maintenance returns missing-credentials first.
- Encoded/absolute traversal and invalid child-input coverage were missing. Regression tests now exercise both without network I/O.
- Invalid capability cleanup was not covered in the snapshot Claude read; SessionLifetimeTests now exercises partial-session cleanup and subsequent ownership, plus delayed/failing async disposal.

Known P3 follow-ups, retained explicitly rather than introducing another framework in this slice:

- MCP's Dictionary parameter binding can collapse duplicate top-level argument keys before our conversion sees them. Nested duplicate keys are rejected; CLI rejects duplicates at every object level. This is inferred from binding behavior, not a raw-wire duplicate-key test.
- Bundle diagnostic codes are available on the concrete catalog but are not included in adapter discovery/results; an unavailable configured bundle directory currently becomes no-compatible-bundle. Operators must verify the configured directory. Failed-bundle fallback itself is tested.
- Duplicate workflow names fail all execution with duplicate-operation, while discovery lists the first descriptor. Registration diagnostics should make this configuration problem visible during discovery.
- Unexpected exceptions from trusted workflow code propagate to managed callers and can terminate the CLI outside its documented handled exit codes. Workflows must translate expected domain failures; arbitrary plugin failures are not contained by this prototype. A host error boundary is a future robustness improvement, not an isolation guarantee.
- CLI rejects malformed CLIO10_PRIMITIVE_VERSION with exit 2 but lacks the explanatory stderr text already present in MCP.
- The composition capability-unavailable mapping itself lacks a dedicated missing-target test, although Core's invalid-capability-provider initialization and cleanup path is tested.

These are concrete usability/diagnostic and defensive-coverage limits. They do not reintroduce HTTP-only contexts, string-only composition, hardcoded command adapters or vendor implementation dependencies in partner libraries. No second broad Claude review was requested for test/documentation corrections.
