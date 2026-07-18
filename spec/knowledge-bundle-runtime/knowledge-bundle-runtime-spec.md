# Knowledge bundle runtime prototype

## Goal

Prove that Clio can consume externally produced guidance without embedding the guidance text in
its serving runtime. The prototype must preserve the last verified active bundle when a newer
candidate is rejected and must report a typed unavailable state before any bundle is active.

## Scope

- Accept a transport-neutral bundle stream plus a configured trusted public key.
- Verify the detached manifest signature, v0 contract, compatibility, resource paths, lengths,
  and SHA-256 digests before activation.
- Activate only a compatible candidate whose sequence is greater than the active sequence.
- Publish the complete candidate atomically; readers never observe a partially verified bundle.
- Keep the active bundle unchanged after malformed, tampered, incompatible, wrong-key, truncated,
  equal-sequence, or lower-sequence candidates.
- Return typed `active`, `not-found`, and `unavailable` article lookup outcomes.
- Discover the highest stable package version from a configured NuGet v3 flat container, extract
  `content/knowledge-bundle.zip`, and memoize rejected immutable versions in a bounded recent-version
  window while allowing later higher versions to recover.
- Exercise the real MCP process with generated synthetic package content only.

## Out of scope

- Authenticated feed credentials, background polling, and package publication.
- Production key management or revocation.
- Rollback, age expiry, stale state, or advisory safety floors.
- Removing every existing embedded guidance article during this prototype.

## Acceptance

Focused unit tests cover the valid and adversarial state transitions. A hermetic NuGet v3 feed E2E
proves discovery, download, inner extraction, activation, renewal, and last-known-good retention
through the unchanged MCP `get-guidance` contract without asserting canonical guidance content.
