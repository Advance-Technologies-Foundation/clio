# Explorer deploy local defaults specification

Issue: #874

## Problem

Explorer starts `deploy-creatio` with only the selected ZIP. When no deploy-specific database default exists, an empty `DbServerName` selects Kubernetes even when exactly one enabled local database configuration is available. The Explorer launcher also closes immediately after failures.

## Requirements

- Explicit command options retain highest precedence.
- A deploy-specific database default retains precedence over general local configurations.
- For Explorer launches, exactly one enabled local database configuration becomes the fallback local preference.
- MCP, ClioRing, and ordinary CLI calls preserve omitted `DbServerName` as Kubernetes intent.
- Zero or multiple enabled local database configurations preserve the existing Kubernetes fallback.
- Existing local Redis resolution continues to honor explicit selection, `defaultRedis`, and a sole enabled server.
- The Explorer deploy terminal pauses on non-zero exit without an additional command-level exit prompt.

## Exclusions

- No MCP argument or result-contract changes.
- No new database-default setting is introduced.
- No automatic choice among multiple enabled local database servers.
