# GitHub Release delivery for built-in knowledge

- **Status:** Accepted; implemented
- **Date:** 2026-08-02
- **Feature:** `knowledge-bundle-runtime`
- **Amends:** [`knowledge-bundle-runtime-multi-source-contract.md`](knowledge-bundle-runtime-multi-source-contract.md)

## Context

The built-in `creatio-curated` source shipped as a `git` transport: a clone of
`clio-knowledge` on `master`, refreshed at runtime. That has three defects that no amount of
hardening in the Git transport can fix.

1. **It requires a Git CLI.** Clio cannot guarantee one exists, is on `PATH`, or is a version whose
   flags behave as expected. A missing Git means no guidance at all.
2. **It reads a mutable branch.** What a user activates depends on when they ran it. There is no
   generation a support engineer can name, reproduce, or roll back to.
3. **It cannot be signed.** A checkout carries no publisher signature, so trust rests entirely on
   transport security and repository access control.

NuGet solves 2 and 3 but introduces a package publication pipeline for content that is not a
library, and a feed that must be reachable and configured.

## Decision

Deliver the built-in library as a **signed GitHub Release asset**, through a new
`github-release` artifact transport. Keep `git` and `nuget` unchanged for partner, customer, and
private sources.

The transport is deliberately not called `git`: it never touches a checkout.

### Source contract

```json
"creatio-curated": {
  "library-id": "com.creatio.clio",
  "type": "github-release",
  "location": "https://api.github.com/",
  "repository-owner": "Advance-Technologies-Foundation",
  "repository-name": "clio-knowledge",
  "asset-name": "clio-knowledge-bundle.zip",
  "enabled": true,
  "priority": 100,
  "participation": "authoritative"
}
```

`location` carries only the API origin. The repository and asset are named structurally so the
built-in source cannot be pointed at an arbitrary URL by editing one field. A third-party publisher
uses the same three fields plus its own `trusted-key-id` and `trusted-public-key-path`.

### Retrieval order

1. `GET {location}repos/{owner}/{repo}/releases/latest`, conditional on the stored ETag.
2. Refuse a draft or prerelease; require a tag that is an exact `MAJOR.MINOR.PATCH` library version.
3. Require exactly one asset named `asset-name`, in state `uploaded`, with an archive content type,
   a size within bounds, and a well-formed `sha256:` digest. Record whether the release is immutable.
4. Stop with no candidate when the tag is the active revision or one the search already refused.
5. Download the asset URL, resolving redirects manually against a host and scheme allowlist.
6. Compare SHA-256 with the digest GitHub published; a mismatch rejects the revision.
7. Hand the bytes to the existing bundle runtime, which verifies the publisher signature, the
   contract, the resources, and the sequence — unchanged from the NuGet path.

`releases/latest` is **discovery only**. Generation ordering is the bundle's monotonic `sequence`;
a newer tag never authorizes a lower sequence. The GitHub digest proves transport integrity and is
not a substitute for the publisher signature, since both come from the same origin.

A repair asks for one exact previously installed revision and uses `releases/tags/{tag}` instead, so
recovering a damaged generation cannot silently jump to whatever is current.

### Revision semantics

The transport revision is the release tag, and the producer keeps it identical to the signed
bundle's `libraryVersion`. The runtime already refuses a candidate whose declared version differs
from the revision, so this binds a tag to the content it ships for free. Ordering tags is transport
bookkeeping for the candidate walk only.

### Trust

The built-in library has no operator-supplied key file: its public key is pinned in Clio's binary
(`BuiltInKnowledgeBundleTrustStore`). The composed trust store consults the pinned key **first** and
refuses to fall through to configured material for `com.creatio.clio`, so a settings entry naming
that library cannot substitute its own signing key. Rotation is additive and consumer-first: ship
the successor key in a Clio release, then start signing with it, then retire the predecessor.

### Startup

`CuratedKnowledgeBootstrapService` gets an artifact-shaped local probe
(`IsBundleGenerationInstalled`) beside the existing Git one. A warm start therefore activates the
verified cache with no lock, no parse, and — decisively — no HTTP request, staying inside the
five-second pre-serve budget. Without it, every warm start would fall through to an install and hit
`api.github.com`, which no unit test would have caught.

`get-guidance`, `resources/list`, and `resources/read` never contact GitHub. Only
`install-knowledge` and `update-knowledge` do, within their operation deadline.

### Migration

`EnsureKnowledgeSource` rewrites the persisted `creatio-curated` entry from the canonical
definition and carries `Enabled` across, so an upgrade migrates a Git entry to the release contract
while preserving an operator's `enabled: false` kill switch. The alias, library identity, priority,
and participation are unchanged. An existing Git checkout under the alias is left on disk: it is
simply no longer read. Unrelated user-configured Git and NuGet sources are untouched.

## Consequences

- The built-in source no longer needs Git. Generic Git sources still do, and the documentation says
  so explicitly rather than leaving it implied.
- Content ships only when a release is published, not when `master` moves. That is a deliberate
  trade: slower propagation in exchange for a named, signed, reproducible generation.
- Reusing a `sequence` with different content is now a publishing error the producer pipeline can
  and does refuse before a release leaves draft.

## Rejected alternatives

- **Harden the Git transport.** Cannot produce a signature or an immutable generation; the Git CLI
  dependency remains.
- **Move the built-in source to NuGet.** Solves trust and immutability but adds a package pipeline
  for non-package content and a feed dependency. NuGet stays for publishers who already have one.
- **Download `master` as a tarball.** Removes the Git CLI but keeps a mutable, unsigned source.
- **Accept an arbitrary release URL for the built-in source.** One edited settings field would
  redirect the trusted library to a foreign host.
