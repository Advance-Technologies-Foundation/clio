# Knowledge capabilities and installed version reporting

- Problem: the knowledge installer advertises four tools instead of the enabled MCP catalog, rejecting supported administration requirements (#1485).
- Outcome: signed and Git knowledge validation uses the actual enabled tool registry, including long-tail tools. Unknown and disabled requirements remain rejected.
- Reporting: `clio info` / `clio ver` default and `--all` output reports local activation-marker versions by source, including disabled installed sources. No readable records means `not installed or unavailable`; no publisher check is introduced. Component-specific flags retain their output.
- Scope: production DI capability registration, a small reader over the existing installation store, command output, documentation and regression coverage. Signature/version/sequence checks and release fallback behavior remain unchanged.
- Validation: production CLI/MCP registration tests, retained missing-tool rejection tests, signed administration-requirement E2E, rejected-update retention, local version reporting, and isolated installation of the current signed public bundle followed by guidance reads.
