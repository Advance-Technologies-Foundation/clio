# Explorer deploy local defaults implementation plan

## Decision

Extend `DeployCreatioDefaultsResolver` after deploy-specific defaults are applied. If `DbServerName` remains empty, enumerate at most two enabled local database names and select only when exactly one exists. This keeps explicit and deploy-specific precedence and does not alter the null-to-Kubernetes contract used by MCP and ClioRing.

Update the Windows registry deploy verb to execute `pause` only after a non-zero `clio` exit code. Document the resolution order in the existing `deploy-creatio` help surfaces.

## Compatibility

The MCP and ClioRing callers provide explicit local database and Redis names for local deployment and null for Kubernetes. Their wire contract remains unchanged.
