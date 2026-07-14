# Explorer deploy local defaults implementation plan

## Decision

Mark the Explorer registry invocation with a hidden `--explorer-launch` option. Extend `DeployCreatioDefaultsResolver` after deploy-specific defaults are applied. Only for that launch context, if `DbServerName` remains empty, enumerate at most two enabled local database names and select only when exactly one exists. This keeps explicit and deploy-specific precedence and does not alter the null-to-Kubernetes contract used by MCP and ClioRing.

Update the Windows registry deploy verb to execute `pause` only after a non-zero `clio` exit code, while keeping the ZIP path as the only quoted command fragment. Suppress the command's generic exit prompt for Explorer so failures pause exactly once. Document the resolution order in the existing `deploy-creatio` help surfaces.

## Compatibility

The MCP and ClioRing callers provide explicit local database and Redis names for local deployment and null for Kubernetes. Their wire contract remains unchanged.
