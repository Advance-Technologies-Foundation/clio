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
- Prove that the active ESQ payloads are byte-identical to the frozen Clio oracle.

## Out of scope

- NuGet versus npm transport selection, feed authentication, polling, and publication.
- Production key management or revocation.
- Rollback, age expiry, stale state, or advisory safety floors.
- Removing every existing embedded guidance article during this prototype.

## Acceptance

Focused unit tests cover the valid and adversarial state transitions. The existing MCP
`get-guidance` contract remains stable when the runtime is wired into the tool in a later story.
