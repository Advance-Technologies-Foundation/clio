# Story: Verify and retain an active external knowledge bundle

As the Clio MCP guidance runtime, I want to verify an external candidate completely before
activation so a broken or hostile update cannot replace working guidance.

## Acceptance criteria

1. A valid compatible candidate activates and its articles are readable byte-for-byte.
2. A malformed, tampered, truncated, incompatible, unknown-key, equal-sequence, or lower-sequence
   candidate is rejected and the prior active articles remain unchanged.
3. A cold runtime reports typed `unavailable` rather than throwing or falling back silently.
4. An active runtime distinguishes `not-found` from `unavailable`.
5. The verifier depends on no network transport; the NuGet adapter is isolated and cannot bypass
   verification or last-known-good retention.
6. Real-process delivery tests use a loopback fake NuGet v3 feed and synthetic guide bytes only.
