# Read-only package dependency CLI and MCP

status: review

Implement get-pkg-dependencies, pkg-dependency-path, pkg-dependency-why, find-pkg-by-schema export-pkg-graph and check-pkg-dependency. Validate unsupported endpoints, invalid responses, package ambiguity, cycles, reverse/transitive traversal, exact/partial matching, optional drop impact and result completeness. Preserve raw graph fields in JSON export and escape DOT labels. Update CLI help and MCP contracts. Validate with live feature and stock runtimes. Keep issue open while any acceptance requirement remains outstanding.

Implementation and live evidence: [validation](../package-dependency-explorer/package-dependency-explorer-validation.md). Backend remains local by user request; no merge/release claim.
