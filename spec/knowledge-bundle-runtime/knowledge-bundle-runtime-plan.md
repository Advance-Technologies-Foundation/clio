# Knowledge bundle runtime plan

- **Status:** Accepted for prototype
- **Date:** 2026-07-18
- **Feature:** `knowledge-bundle-runtime`

## Context

Guidance currently lives as C# string content in Clio. `clio-knowledge` now produces a signed v0
bundle and a frozen ESQ oracle captured from compiled Clio commit
`baa34546589413aa898429051d1702442bbd2dd2`. Distribution may use NuGet, but transport does not
decide whether downloaded content is safe or compatible to serve.

## Decision

Introduce a transport-neutral runtime boundary:

- `IKnowledgeBundleRuntime` owns candidate verification, forward-only activation, and article
  lookup.
- A candidate is fully materialized into immutable memory before one atomic active-reference swap.
- The detached `ECDSA-P256-SHA256` signature covers the exact `manifest.json` bytes.
- The runtime resolves the manifest `keyId` through `IKnowledgeBundleTrustStore`; an unknown or
  wrong key rejects the candidate.
- Exact inclusive `MAJOR.MINOR.PATCH` ranges define Clio and MCP-tool-contract compatibility.
- ZIP entry paths are normalized and constrained; duplicate, unexpected, missing, or traversal
  entries reject the entire candidate.
- Every declared resource length and SHA-256 digest is verified before activation.
- Sequence is monotonic and forward-only. Equal or lower sequences reject without mutating active
  state.
- Cold lookup returns `unavailable`; active lookup returns `active` or `not-found`.

The runtime does not download packages. A future NuGet or other transport supplies candidate
streams through the same interface.

## Consequences

The safety-critical serving behavior can be proven before choosing transport. Clio temporarily
mirrors the v0 DTO shape; conformance fixtures from `clio-knowledge` are the cross-repository
contract oracle until a shared contract package is selected.
