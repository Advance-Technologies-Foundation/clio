# Package dependency explorer

Status: superseded working proposal, 2026-10-02. See ../package-dependency-explorer/package-dependency-explorer-contract-review.md for the runtime-backed agent contract review and Fable disposition.

Use the existing authenticated Creatio client and four registered PackageService routes. One DI service owns HTTP validation and graph queries; commands and environment-aware MCP adapters stay thin. Preserve endpoint payloads and enrich schema rows with dependency reachability and shortest paths. BFS is cycle-safe and deterministic. Reasons and drop checks remain separate: a used target may be reachable through an alternative edge.

No graph cache in Clio: each invocation reads the environment. No fallback to undocumented graph formats. Paths and reverse dependencies need no new server algorithms. Client-side exact filtering cannot correct a silently capped or contextless search. Improve the Creatio contract before implementing Clio wrappers; preserve the runtime distinction between reasons, alternative paths and removal blockers.
