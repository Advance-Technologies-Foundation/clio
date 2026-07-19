# Multi-source knowledge identity and resolution contract

- **Status:** Accepted for proof-of-concept implementation
- **Date:** 2026-07-19
- **Feature:** `knowledge-bundle-runtime`

## Context

The initial proof of concept treats one NuGet package as the entire knowledge installation. That is
not sufficient for a knowledge ecosystem in which Creatio, partners, and customers publish trusted
libraries independently. Git is also a first-class authoring and delivery path: a branch can deliver
validated knowledge immediately, while a tag or commit can provide immutable reproduction.

Clio keeps retrieval concerns separate from content resolution. NuGet obtains one signed candidate
generation and publishes it through the verified generation store. Git synchronizes one explicitly
trusted repository into a Clio-owned checkout and reads its declarative source manifest without
building or executing repository code. Neither path decides which guidance wins. The operator owns
source trust, enablement, priority, and optional topic pins.

## Configuration contract

Move the root path beneath a visible `knowledge` section in `appsettings.json`:

```json
"knowledge": {
  "root-path": "C:\\Users\\user\\AppData\\Local\\creatio\\clio\\knowledge",
  "sources": {
    "creatio": {
      "library-id": "com.creatio.clio",
      "type": "git",
      "location": "https://github.com/Advance-Technologies-Foundation/clio-knowledge.git",
      "branch": "master",
      "enabled": true,
      "priority": 100,
      "participation": "authoritative"
    },
    "partner": {
      "library-id": "com.example.partner",
      "type": "nuget",
      "location": "https://packages.example.test/v3/index.json",
      "package-id": "Example.Partner.Knowledge",
      "trusted-key-id": "partner-2026",
      "trusted-public-key-path": "C:\\Keys\\partner-public.pem",
      "enabled": true,
      "priority": 50,
      "participation": "supplement"
    }
  },
  "topic-pins": {
    "creatio.esq.filters": "com.creatio.clio"
  }
}
```

- The source map key is an operator-friendly alias. `library-id` is the stable publisher identity
  and must be unique among configured sources.
- `enabled` is a serving and update kill switch; disabling retains the installed cache.
- `priority` is an operator-controlled integer. Higher values win only where a source is eligible.
- `participation` is `isolated`, `supplement`, or `authoritative`.
- NuGet sources require `package-id`, `trusted-key-id`, and an absolute
  `trusted-public-key-path`. The key ID authorizes the signed bundle manifest for exactly that
  library. Public keys are not secrets; private signing keys are never stored in source
  configuration.
- Git sources use none of the NuGet package or signing-key fields. Adding a Git source explicitly
  trusts its credential-free repository location and selected branch, tag, or commit as the content
  authority.
- Topic pins select one enabled eligible library by stable library ID and override numeric priority.
- Clio migrates the former top-level `knowledge-root-path` into `knowledge.root-path` once and does
  not maintain two writable sources of truth.
- Secrets are not stored in this section. Public verification-key paths are safe configuration.
  The proof of concept accepts credential-free HTTPS Git repositories and NuGet feeds, plus
  loopback HTTP for local testing; private-source and credential-manager authentication are
  explicitly out of scope.

## Identity contract

Each signed NuGet bundle declares:

- `libraryId`: reverse-DNS stable publisher/library identity;
- `libraryVersion`: publisher-facing generation label;
- `sequence`: positive, monotonically increasing unsigned integer scoped to `libraryId`;
- `source`: non-secret provenance including repository and exact commit when applicable;
- resources with stable `itemId`, `topicId`, relative path, media type, digest, and role.

Each Git repository carries a bounded `bundle-source.json` manifest with contract version `1.0.0`,
the configured `libraryId`, a publisher-facing `libraryVersion`, a positive `sequence`, and bounded
resource declarations. Each resource supplies an `itemId`, `topicId`, role, exact namespaced URI,
repository-relative `sourcePath`, and optional legacy URIs. Clio computes the content digest from
the manifest and declared resource bytes and records the complete resolved Git commit separately as
transport provenance. Configured commits must be complete 40-character SHA-1 or 64-character
SHA-256 hexadecimal object IDs; abbreviated commit IDs are rejected.

For signed NuGet content, the immutable generation identity is
`(libraryId, sequence, bundleDigest)`. A package version is transport state, not knowledge identity.
Reusing a sequence with different bytes is rejected, and a lower or equal sequence cannot replace
the active generation unless the bytes are identical and the operation is an explicit repair.

For direct Git content, the configured repository plus resolved commit identifies the retrieved
revision, while `(libraryId, sequence, contentDigest)` identifies the activated in-memory snapshot.
Branch and tag names remain mutable retrieval selectors. Git does not reuse the signed NuGet replay
ledger or generation store.

Every item has an exact namespaced route:

```text
docs://knowledge/<library-id>/<item-id>
```

The logical `topicId` supports cross-library discovery and selection. Exact namespaced lookup never
falls through to another library. A resource may additionally declare `legacyUris`; they are signed
manifest fields for NuGet and repository-manifest fields for Git. The resolver serves those aliases
as exact references to that same item. Alias collisions are rejected or reported as ambiguity
rather than being resolved by configuration order.

## Resolution contract

Resolution is deterministic and does not merge Markdown at runtime:

1. Exclude disabled, invalid, incompatible, or unavailable libraries.
2. Exact namespaced requests select that library and item only.
3. Logical topic requests select eligible items for the requested role.
4. Apply a configured topic pin when present; a missing or ineligible pin is a visible error.
5. Otherwise choose the highest operator priority.
6. Equal highest priority from different eligible libraries is an ambiguity error, never
   configuration-order tie-breaking.

Participation modes mean:

- `isolated`: available only through its namespaced identity; never competes for logical topics.
- `supplement`: may provide additional topics but cannot replace a topic already supplied by an
  eligible authoritative source.
- `authoritative`: participates in logical-topic selection and can win by pin or priority.

Clio returns provenance with a resolved result: library ID, item ID, topic ID, generation sequence,
bundle digest, source alias, and exact local path. This makes resolution inspectable by both humans
and agents.

## Transport contract

The two proof-of-concept retrieval paths intentionally have different publication mechanics:

- `nuget` discovers stable package versions, extracts a bounded signed bundle, verifies its
  configured public-key trust, and publishes immutable current/previous generations;
- `git` clones or updates a configured branch, tag, or commit in a bounded Clio-owned checkout,
  validates `bundle-source.json` plus its declared files, and activates an immutable in-memory
  snapshot of those bytes. It does not create or consume a bundle ZIP.

Both paths feed the same resolver and provenance surface after validation. A future transport must
define its trust, storage, and rollback behavior explicitly instead of assuming that the NuGet
generation model or direct Git checkout model applies automatically.

Git follows container-image-style reference semantics:

- an explicit commit is immutable and always wins over tag/branch configuration;
- an explicit tag resolves to a commit and records both;
- an explicit branch follows that branch and records the resolved commit on every install/update;
- when no reference is supplied, install/update discovers the remote default branch and persists
  it only after the checkout is successfully validated and activated; read-only information and
  update-availability checks never mutate source configuration;
- each activation snapshots the declared manifest and resource bytes from the managed checkout;
  serving never executes repository code.

Git retrieval disables hooks, rejects submodules, isolates inherited Git process configuration, and
bounds captured output and checkout size. It uses no repository credentials in persisted metadata
or output and never executes build scripts. The proof of concept supports credential-free HTTPS
repositories, plus loopback HTTP for local testing. A compatible repository publishes
`bundle-source.json` and every declared resource directly in the checkout.

## On-disk model

The root contains one independently managed directory per configured source. NuGet uses immutable
generation directories and activation markers; Git uses a managed repository checkout:

```text
<knowledge.root-path>/
  .clio-knowledge-root
  sources/
    .locks/...
    <filesystem-safe-source-key>/
      .clio-knowledge-source
      repository/...                         # Git
      current.json                           # NuGet
      generations/<sequence>-<digest-prefix>/...  # NuGet
      staging/...                            # NuGet publication only
```

One source can fail or update without withdrawing other libraries. Runtime lookup re-reads NuGet
activation markers or the Git source manifest and atomically replaces one immutable snapshot at a
time. Disabling a source removes it from the resolution snapshot on the next lookup without
deleting its files.

## Command surface

Lifecycle commands accept an optional source alias and default to all enabled sources when that is
safe and unambiguous:

- `install-knowledge`, `update-knowledge`, `info-knowledge`, `delete-knowledge`;
- `add-knowledge-source`, `remove-knowledge-source`;
- `enable-knowledge-source`, `disable-knowledge-source`;
- `list-knowledge-sources`, `list-knowledge-examples`.

Source management commands validate and persist configuration atomically. Destructive removal and
deletion require explicit confirmation. These ordinary CLI commands are available to agents through
`clio-run`; no duplicate resident MCP tools are required for the proof of concept.

`info-knowledge` reads configuration and installed caches without contacting transports by default.
Its explicit `--check-updates` option performs bounded read-only transport checks.

## Compatibility and failure behavior

- NuGet lifecycle commands accept signed version 1 bundles only. Git lifecycle commands accept the
  direct repository source-manifest contract version `1.0.0`. The unreleased version 0 single-source
  prototype is not registered as an implicit compatibility source; an existing prototype cache
  must be replaced through source configuration and reinstall.
- A NuGet transport failure, invalid signature, incompatible bundle, or rollback attempt never
  replaces its last-known-good generation. An invalid Git checkout is reported and is not activated.
  Ambiguous topics and broken pins remain visible errors on either path.
- A disabled library is never served, even by exact route, until re-enabled.
- `info-knowledge` exposes source configuration, installed generation, resolved transport revision,
  validation state, and update availability without emitting secrets.
- Source removal deactivates and removes configuration before best-effort deletion of its managed
  cache. A cleanup failure leaves only an explicitly reported orphan cache, not a serving source.
- Clio content tests remain synthetic and assert delivery/resolution mechanics only.
  `clio-knowledge` validates its real manifest, identifiers, routes, and article inventory.

## Consequences

Knowledge publishers can ship independently through supported transports while sharing one
resolution contract. Operators can prefer or disable trusted publishers deterministically. Agents
can use MCP routes or inspect the same managed content on disk. Signed NuGet generations provide
forward-only verification and rollback retention; direct Git checkouts provide fast branch-following
updates with inspectable resolved-commit and content-digest provenance.
