# Host-side runtime MCP adapters

Expose existing runtime and operator-install CLI behavior as eight feature-gated long-tail tools. Reuse command services, validation and standard CommandExecutionResult. Add explicit stdio startup opt-in --runtime-host. Refuse Kubernetes/.NET container hosts and credential passthrough before invoking commands. The startup flag is per process, not persisted configuration; the runtime feature is the existing persisted gate.

In-process execution avoids placing filesystem attachment operations in disposable workers. Existing commands own timeouts and receipts; request interruption does not roll back operations. No additional registry, provider model, prompt or resource is introduced. MCP discovery uses existing get-tool-contract and execution uses clio-run or clio-run-destructive based on annotations.

Validate all option mappings/defaults, host opt-out, feature gating, tool metadata, and real-process contract calls. Live read-only catalogue checks are explicitly opted in with CLIO_RUNTIME_LIVE_TEST=1. Do not run destructive installation/build flows against shared clusters for wrapper tests.
