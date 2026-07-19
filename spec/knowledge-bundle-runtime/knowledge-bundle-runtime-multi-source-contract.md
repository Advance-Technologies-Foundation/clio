# Multi-source knowledge identity and resolution contract

- **Status:** Accepted for proof-of-concept implementation
- **Date:** 2026-07-19
- **Feature:** `knowledge-bundle-runtime`

## Context

The initial proof of concept treats one NuGet package as the entire knowledge installation. That is
not sufficient for a knowledge ecosystem in which Creatio, partners, and customers publish trusted
libraries independently. Git is also a first-class authoring and delivery path: a branch can deliver
validated knowledge immediately, while a tag or commit can provide immutable reproduction.

Clio must keep transport concerns separate from trust, installation, and content resolution. A
transport obtains one candidate generation; it does not decide which guidance wins. A library owns
stable knowledge identities and a monotonic signed sequence. The operator owns trust, enablement,
priority, and optional topic pins.

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
      "trusted-key-id": "creatio-2026",
      "trusted-public-key-path": "C:\\Keys\\creatio-public.pem",
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
- `trusted-key-id` authorizes the manifest signing-key identity for exactly that source.
- `trusted-public-key-path` is a required absolute local path to public verification-key material.
  Public keys are not secrets; private signing keys are never stored in source configuration.
- Topic pins select one enabled eligible library by stable library ID and override numeric priority.
- Clio migrates the former top-level `knowledge-root-path` into `knowledge.root-path` once and does
  not maintain two writable sources of truth.
- Secrets are not stored in this section. Public verification-key paths are safe configuration.
  The proof of concept accepts credential-free public HTTPS Git repositories and NuGet feeds only;
  private-source and credential-manager authentication are explicitly out of scope.

## Identity contract

Each signed bundle declares:

- `libraryId`: reverse-DNS stable publisher/library identity;
- `libraryVersion`: publisher-facing generation label;
- `sequence`: positive, monotonically increasing unsigned integer scoped to `libraryId`;
- `source`: non-secret provenance including repository and exact commit when applicable;
- resources with stable `itemId`, `topicId`, relative path, media type, digest, and role.

For Git provenance, `source.commit` is the complete 40-character SHA-1 or 64-character SHA-256
hexadecimal object ID. Abbreviated commit IDs are rejected.

The immutable generation identity is `(libraryId, sequence, bundleDigest)`. A transport version,
Git branch, tag, or commit is provenance and retrieval state; it is not the knowledge identity.
Reusing a sequence with different bytes is rejected. Installing a lower or equal sequence cannot
replace the active generation unless the bytes are identical and the operation is an explicit
repair.

Every item has an exact namespaced route:

```text
docs://knowledge/<library-id>/<item-id>
```

The logical `topicId` supports cross-library discovery and selection. Exact namespaced lookup never
falls through to another library. A resource may additionally declare signed `legacyUris`; the
resolver serves those aliases as exact references to that same item. Alias collisions are rejected
or reported as ambiguity rather than being resolved by configuration order.

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

`IKnowledgeTransport` retrieves candidate generations for one configured source. It reports the
resolved transport revision and candidate bytes/path; common validation, trust, rollback,
installation, and activation remain outside the adapter.

The initial adapters are:

- `nuget`: discovers stable package versions and extracts the signed bundle from a bounded package;
- `git`: fetches a configured branch, tag, or commit into a bounded staging area and reads the
  repository's declared ready bundle artifact without executing repository code.

Future `npm`, filesystem/share, or SVN adapters implement the same contract without changing
resolution or the on-disk activation model.

Git follows container-image-style reference semantics:

- an explicit commit is immutable and always wins over tag/branch configuration;
- an explicit tag resolves to a commit and records both;
- an explicit branch follows that branch and records the resolved commit on every install/update;
- when no reference is supplied, install/update discovers the remote default branch and persists
  it only after the candidate is successfully verified and installed; read-only information and
  update-availability checks never mutate source configuration;
- serving always uses the installed resolved commit, never live files from the remote checkout.

Git retrieval disables hooks, rejects submodules and symbolic-link escapes, uses no repository
credentials in persisted metadata or output, and never executes build scripts. The proof of
concept supports credential-free public HTTPS repositories only. A compatible repository therefore
publishes a ready bundle at its declared artifact path.

## On-disk model

The root contains one independently activated store per library:

```text
<knowledge.root-path>/
  .clio-knowledge-root
  sources/
    <filesystem-safe-library-key>/
      current.json
      knowledge.lock
      generations/<sequence>-<digest-prefix>/...
      staging/...
  examples/...
```

One source can fail or update without withdrawing other libraries. Runtime lookup re-reads cheap
per-library activation markers and atomically replaces one immutable snapshot at a time. Disabling
a source removes it from the resolution snapshot on the next lookup without deleting its files.

## Command surface

Lifecycle commands accept an optional source alias and default to all enabled sources when that is
safe and unambiguous:

- `install-knowledge`, `update-knowledge`, `info-knowledge`, `delete-knowledge`;
- `add-knowledge-source`, `remove-knowledge-source`;
- `enable-knowledge-source`, `disable-knowledge-source`;
- `list-knowledge-sources`.

Source management commands validate and persist configuration atomically. Destructive removal and
deletion require explicit confirmation. These ordinary CLI commands are available to agents through
`clio-run`; no duplicate resident MCP tools are required for the proof of concept.

`info-knowledge` reads configuration and installed caches without contacting transports by default.
Its explicit `--check-updates` option performs bounded read-only transport checks.

## Compatibility and failure behavior

- Configured multi-source lifecycle commands accept signed version 1 bundles only. The unreleased
  version 0 single-source prototype is not registered as an implicit compatibility source; an
  existing prototype cache must be replaced through source configuration and reinstall.
- A transport failure, invalid signature, incompatible bundle, rollback attempt, ambiguous topic,
  or broken pin never replaces a last-known-good generation.
- A disabled library is never served, even by exact route, until re-enabled.
- `info-knowledge` exposes source configuration, installed generation, resolved transport revision,
  validation state, and update availability without emitting secrets.
- Source removal deactivates and removes configuration before best-effort deletion of its managed
  cache. A cleanup failure leaves only an explicitly reported orphan cache, not a serving source.
- Clio content tests remain synthetic and assert delivery/resolution mechanics only.
  `clio-knowledge` validates its real manifest, identifiers, routes, and article inventory.

## Consequences

Knowledge publishers can ship independently through supported transports, while Clio retains one
verification, storage, and resolution implementation. Operators can prefer or disable trusted
publishers deterministically. Agents can use MCP routes or inspect the same installed content on
disk. Git enables fast branch-following updates without weakening the signed generation or recorded
commit boundary.
