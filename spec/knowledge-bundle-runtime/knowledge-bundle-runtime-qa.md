# Knowledge bundle runtime QA

## Focused unit matrix

- Valid signed bundle activates from cold state.
- Generated synthetic articles preserve their expected stable URI and byte digest.
- Tampered manifest signature rejects and retains active.
- Unknown/wrong key rejects and retains active.
- Missing, duplicate, unexpected, truncated, traversal, length-mismatched, and digest-mismatched
  entries reject and retain active.
- Incompatible Clio or MCP-tool-contract range rejects and retains active.
- Equal/lower sequence rejects and retains active; higher sequence activates.
- Cold lookup is `unavailable`; active missing article is `not-found`.
- NuGet v3 service-index and flat-container discovery select the highest stable package version.
- The fixed inner bundle is extracted byte-for-byte from a bounded `.nupkg`.
- A newer valid synthetic package renews the real MCP process; a newer invalid signed bundle leaves
  the prior synthetic digest active.

## MCP integration matrix

Use generated synthetic fixtures only. Assert stable identity, digests, package versions, sequence
transitions, typed unavailable state, and transport requests through the real stdio MCP process;
never assert canonical guidance wording or snapshots in Clio.
