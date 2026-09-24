# Issue 1640: unresolved Freedom UI parents

Intent: catch a parentName that resolves to no element before saving an orphan.
Reproduced in a disposable Creatio environment: validate-page accepted a nonexistent parent.

Standalone validate-page accepts optional known-containers. Unresolved references
warn without inherited context and fail with it. Saves resolve the actual target
hierarchy and use the existing diff interpreter to reject effective unresolved
parents, including with validate:false. Append checks the merged body. Mobile
rules and required baseline comments are preserved.

Acceptance: diagnostics identify the child and parent and suggest a known name.
Inherited, new, nested, and root elements remain supported. Ignored moves and
alias-excluded operations retain platform behavior. Replace, append, explicit
schema targets, and dry runs use the save guard.

Validation: Command and MCP unit suites passed (10,523 passed, 16 skipped).
Four real-process MCP tests passed. In a disposable Creatio 10.2.212 environment,
the invalid save was rejected with an unchanged stored checksum. A corrected
parent saved successfully and readback confirmed the intended nesting.

Reviewed command docs, help, MCP contract, and container-selection guidance.
No Ring-consumed contract changed. Parallel review findings were corrected.
