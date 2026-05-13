# creatio-ui Jenkins pipeline: contract for component-registry publishing

This document defines the **producer contract** that the creatio-ui Platform-UI team owns: what JSON shape to emit, where to upload it, on which trigger. This branch does not implement the Jenkins job — the implementation lives in a separate cross-repo PR in the creatio-ui repository. This file is the design reference for that PR.

Sister documents:
- [architecture.md](architecture.md) — high-level two-domain SoT.
- [clio-target-structure.md](clio-target-structure.md) — consumer side (clio).
- [extractor-analysis.md](extractor-analysis.md) — filters, invariants, and the 192/92/100 numbers.

## Contract summary

| Aspect | Value |
|---|---|
| Source repo | `Advance-Technologies-Foundation/creatio-ui` (monorepo) |
| Output artifact | A single JSON file per platform GA-tag |
| Output schema | Top-level array of `ComponentRegistryEntry` objects (same as the current in-repo file in clio) |
| Filename convention | `{Major.Minor.Patch}.json`, e.g. `8.2.1.json`. SemVer 3-part, no prefix, no build/revision component, no leading `v`. |
| Special filename | `latest.json` — copy of the highest-semver published GA file |
| Destination | `https://academy.creatio.com/api/component-registry/{filename}` (HTTPS PUT or whatever method the academy infra exposes; see § Upload mechanism) |
| Trigger | GA-tag in creatio-ui (`8.2.0`, `8.2.1`, `8.3.0`, …). Branch-cut runs the same job in **baseline mode** (artifact only; no CDN upload). |
| Cache headers (recommended) | `Cache-Control: public, max-age=86400` (matches clio's 24h TTL); `ETag` strongly recommended |
| Access | Public, no authentication |
| Failure isolation | Registry-publish failure MUST NOT block the platform GA release |

## Why this shape

clio is the consumer. The shape is dictated by what clio expects (and currently parses today from the in-repo file). The architectural decision (recorded in [README.md](README.md) and [architecture.md](architecture.md)) is to keep the schema **drop-in compatible** in v1 — top-level array, no wrapper, no per-entry `availability`. Versioning happens at the file level on the CDN, not inside the file.

If the AI side later needs `availability` ranges or wrappers, that is a coordinated change: producer (this contract) updates first, consumer (clio) reads new fields lenient until it actively uses them.

## JSON shape (v1)

A file is a top-level JSON array. Each element conforms to the `ComponentRegistryEntry` schema:

```json
[
  {
    "componentType": "crt.Button",
    "category": "interactive",
    "description": "Clickable action element.",
    "container": false,
    "parentTypes": ["crt.GridContainer", "crt.FlexContainer"],
    "properties": {
      "type": {
        "type": "string",
        "description": "Must be crt.Button.",
        "required": true
      },
      "caption": {
        "type": "string",
        "description": "Button caption."
      }
    },
    "typicalChildren": [],
    "example": { /* canonical example payload */ }
  },
  ...
]
```

Field reference (mirrors current clio POCO):

| Field | Type | Required | Notes |
|---|---|---|---|
| `componentType` | `string` | yes | Canonical type, must be unique in the file. |
| `category` | `string` | yes | One of: `containers`, `fields`, `interactive`, `display`, `filtering`. (clio v1 keeps hardcoded category order — see [clio-target-structure.md](clio-target-structure.md).) |
| `description` | `string` | yes | Plain text, ≤ 280 chars recommended. |
| `container` | `boolean` | yes | True if the component can host children. |
| `parentTypes` | `string[]` | no | Suggested parent component types. Empty array if none. |
| `properties` | `object` | yes | Map keyed by property name. Each value is a `ComponentPropertyDefinition`. |
| `typicalChildren` | `string[]` | no | Suggested child component types when `container=true`. Empty array if N/A. |
| `example` | `object` | no | Canonical JSON example as it would appear on a Freedom UI page. Free-form. |

`ComponentPropertyDefinition`:

| Field | Type | Required | Notes |
|---|---|---|---|
| `type` | `string` | yes | TS-style string: `"string"`, `"number"`, `"boolean"`, `"object"`, etc. |
| `description` | `string` | yes | Free-text description. |
| `required` | `boolean` | no | Defaults to `false` if missing. |
| `default` | any | no | Default value if known. Type matches `type`. |
| `values` | `string[]` | no | For closed string-literal unions. |

**Forbidden in v1:**

- `availability`, `since`, `until` fields on entries or properties. The schema is intentionally version-agnostic per file; per-version is encoded by the filename, not by content.
- Wrappers like `{ "schemaVersion": ..., "components": [...] }`. Top-level MUST be an array.
- Side-channel metadata files (`metadata.json`, `manifest.json`, etc.) at the same URL prefix. clio looks ONLY at `{version}.json`.

**Lenient extras allowed:** the producer MAY emit additional fields on entries or properties (e.g. JSDoc-derived `since`). clio's POCO parser ignores unknown fields. This is a deliberate forward-compat hatch.

## Filename convention

| Source event | Filename uploaded |
|---|---|
| GA-tag `8.2.0` | `8.2.0.json` + maybe `latest.json` (see § latest.json) |
| GA-tag `8.2.1` | `8.2.1.json` + maybe `latest.json` |
| GA-tag `8.3.0` | `8.3.0.json` + `latest.json` (if 8.3.0 > current latest) |
| Branch-cut `release/8.3.x` | (nothing on CDN; Jenkins artifact only) |
| Hotfix backport tag `8.2.5` after `8.3.0` is GA | `8.2.5.json` (NO `latest.json` update — 8.3.0 > 8.2.5) |
| Pre-release `8.3.0-rc1` | **Skip.** No CDN upload for pre-release tags. |

Filename validation in the Jenkins pipeline: regex `^[0-9]+\.[0-9]+\.[0-9]+$`. Pre-release suffixes (`-rc1`, `-beta`) skip the upload step (early return from the pipeline).

## `latest.json` semantics

`latest.json` is a pointer to the freshest GA. It is **updated only when** the version being published is strictly greater (semver-sorted) than the version currently pointed to by `latest.json`.

Mechanics:
1. Compute current `latest` semver: HTTP HEAD/GET `latest.json` if it exists; parse its body (a JSON array — read just enough metadata, OR rely on an academy-side convention that stores the current version in a sidecar).
2. Compare `newVersion` (just-uploaded) vs `currentLatest`.
3. If `newVersion > currentLatest`: upload `{newVersion}.json` AND replace `latest.json` with the same content (or set up a redirect; see § Upload mechanism).
4. If `newVersion <= currentLatest`: skip the `latest.json` step. The per-version file is still uploaded.

The semver comparison MUST use strict semver-2.0.0 ordering, not lexicographic. `8.10.0 > 8.9.0` despite `"8.10.0" < "8.9.0"` lexicographically.

**Bootstrap:** before the very first publication, `latest.json` does not exist. The first GA-tag upload creates both `{version}.json` and `latest.json` unconditionally.

## Trigger model

### GA-tag (primary)

Jenkins multi-branch pipeline subscribes to tag pushes matching `^\d+\.\d+\.\d+$` on the creatio-ui repo. On match:

1. Check out the tagged commit.
2. Run the extractor (see § Extractor pipeline).
3. Validate the output file (schema + duplicates).
4. Run the `latest.json` semver comparison.
5. Upload (per § Upload mechanism).
6. Post a Slack/Jira comment to the release thread with the published URL.

### Branch-cut (baseline)

When `master` forks into a `release/8.X.x` branch, the same job runs in **baseline mode** — produces the artifact, attaches it to the Jenkins build, and posts a diff vs the previous-line GA. **No CDN upload.**

This is a regression detector: if the extractor inadvertently dropped a component between two minor lines, the diff catches it before GA.

### Manual

The Jenkins job exposes a `workflow_dispatch`-style parameterized build:

| Parameter | Type | Purpose |
|---|---|---|
| `MODE` | `enum: dry-run \| publish` | `dry-run` produces the artifact, validates, prints what WOULD be uploaded. `publish` actually uploads. |
| `VERSION` | `string` | Overrides the inferred version (e.g. for re-running a missed GA publication). |

Manual `publish` mode is for incident recovery only and emits a structured audit log entry.

## Extractor pipeline (proposed Jenkinsfile sketch)

```groovy
pipeline {
    agent { label 'creatio-ui-builder' }

    parameters {
        choice(name: 'MODE', choices: ['auto', 'dry-run', 'publish'])
        string(name: 'VERSION', defaultValue: '')
    }

    stages {
        stage('Resolve version') {
            steps {
                script {
                    if (params.VERSION) {
                        env.PUBLISH_VERSION = params.VERSION
                    } else if (env.TAG_NAME?.matches(/^\d+\.\d+\.\d+$/)) {
                        env.PUBLISH_VERSION = env.TAG_NAME
                    } else {
                        env.PUBLISH_VERSION = '' // baseline mode
                    }
                }
            }
        }

        stage('Install deps') {
            steps { sh 'pnpm install --frozen-lockfile' }
        }

        stage('Extract registry') {
            steps {
                sh '''
                    nx run component-registry-extractor:build \
                        --version=${PUBLISH_VERSION:-baseline} \
                        --output=dist/component-registry/output.json
                '''
            }
        }

        stage('Validate output') {
            steps {
                sh '''
                    node tools/component-registry-extractor/validate.js \
                        dist/component-registry/output.json
                '''
            }
        }

        stage('Baseline diff (branch-cut only)') {
            when { expression { env.PUBLISH_VERSION == '' } }
            steps {
                sh '''
                    curl -sf https://academy.creatio.com/api/component-registry/latest.json \
                        -o /tmp/previous-latest.json || true
                    node tools/component-registry-extractor/diff.js \
                        dist/component-registry/output.json \
                        /tmp/previous-latest.json > diff-report.txt || true
                '''
                archiveArtifacts artifacts: 'diff-report.txt, dist/component-registry/output.json'
            }
        }

        stage('Upload') {
            when { expression { env.PUBLISH_VERSION != '' && params.MODE != 'dry-run' } }
            steps {
                sh '''
                    # Per-version
                    ./tools/cdn-upload.sh \
                        --source dist/component-registry/output.json \
                        --dest "/api/component-registry/${PUBLISH_VERSION}.json"

                    # latest.json (semver-gated)
                    CURRENT_LATEST=$(./tools/cdn-current-latest.sh)
                    if ./tools/semver-gt.sh "${PUBLISH_VERSION}" "${CURRENT_LATEST}"; then
                        ./tools/cdn-upload.sh \
                            --source dist/component-registry/output.json \
                            --dest "/api/component-registry/latest.json"
                    fi
                '''
            }
        }
    }

    post {
        always {
            archiveArtifacts artifacts: 'dist/component-registry/output.json', allowEmptyArchive: true
        }
    }
}
```

The shell wrappers (`tools/cdn-upload.sh`, `tools/cdn-current-latest.sh`, `tools/semver-gt.sh`) are placeholders — concrete implementation depends on the academy CDN's API (see § Upload mechanism, TBD).

## Upload mechanism (TBD with academy team)

This is the one open infrastructure item that the academy team owns. Options the Platform-UI team should validate:

| Option | Mechanism | Notes |
|---|---|---|
| **HTTPS PUT to S3-compatible bucket** | Pre-signed S3 PUT (AWS, Google Cloud Storage, Azure Blob). Jenkins uses an API key. | Simple, atomic. ETag for free. Needs a public-read bucket behind CDN. |
| **HTTPS POST to academy admin API** | Academy exposes `/admin/upload` accepting multipart form. | Couples to academy's admin authorization model. |
| **Git-push to a static-site repo** | A `creatio-academy-static` repo where every push is auto-deployed to academy.creatio.com. | Adds a git commit per release; clean audit trail; slower (CI rebuild). |
| **rsync over SSH** | A dedicated SSH user with write access to the static path. | Old-school but reliable; no API contract. |

Whichever option is chosen, the Jenkins job must:
1. Atomically replace the destination file (no half-uploaded states visible to clio).
2. Set `Cache-Control: public, max-age=86400` (or coordinate with academy on the exact freshness model).
3. Emit a strong `ETag` so clio's stale-while-revalidate flow can short-circuit unchanged content.
4. Log the URL of the published file for the release thread.

Open question for the academy team: **what is the supported upload API for `/api/component-registry/`?**

## Cache headers (recommended)

| Header | Value | Why |
|---|---|---|
| `Cache-Control` | `public, max-age=86400` | clio uses a 24h app-level TTL; matching CDN edge TTL avoids stale-edge issues. Longer is acceptable (clio will conditional-GET via ETag). |
| `ETag` | `"<sha256-of-payload>"` or `W/"..."` | Lets clio's stale-while-revalidate path short-circuit unchanged content with a 304. |
| `Last-Modified` | RFC 1123 timestamp | Optional, supplementary to ETag. |
| `Content-Type` | `application/json; charset=utf-8` | clio expects JSON content. |
| `Access-Control-Allow-Origin` | `*` | Only needed if a browser-based dev tool ever fetches this; clio runs as a CLI tool so CORS is not strictly required. |

If the academy infra cannot emit a strong `ETag` for these files, that is **not blocking** — clio still works on time-based TTL, just with a slightly higher network footprint.

## Validation gate in CI

Before upload, the pipeline MUST validate:

1. **JSON parse**: file is valid JSON.
2. **Top-level array**: root is `[ ... ]`.
3. **Schema check** for each element:
   - `componentType` present, string, matches `^[a-zA-Z][\w]*\.[A-Z][\w]*$` (e.g. `crt.Button`).
   - `category` present, one of the documented enum values.
   - `description` present, non-empty string.
   - `container` present, boolean.
   - `properties` present, object.
4. **Uniqueness**: `componentType` is unique across all entries (mirror of clio's fail-on-duplicate invariant).
5. **No forbidden fields**: no `availability` / `since` / `until` at the entry or property level in v1 (lenient extras are allowed elsewhere — see § JSON shape).
6. **Size sanity**: ≤ 1 MB compressed (registry today is ~190 KB; 1 MB is generous).

A validation failure is a **build failure**. The publish step is skipped.

## Bootstrap and offline-build implications for clio

The clio build (`dotnet pack`) runs an MSBuild target that GETs `latest.json` from the CDN. Until the creatio-ui Jenkins job runs for the first time, `latest.json` does not exist on the CDN. clio handles this via:

- A committed seed-snapshot at `clio/Command/McpServer/Data/ComponentRegistry.seed.json` (the file moved/renamed from the current in-repo `ComponentRegistry.json`).
- The MSBuild target falls back to the seed file when the CDN fetch fails.

This means clio releases can ship before the CDN is live, and offline builds (CI runners without internet) continue to work — they just freeze the embedded snapshot at whatever the seed file is.

Cross-team coordination: the creatio-ui team's "first upload" milestone unblocks clio's MSBuild target picking up a fresh `latest.json` automatically. Until that milestone, clio's embedded snapshot tracks the seed file.

## Failure modes (CDN producer side)

| Scenario | Behavior |
|---|---|
| Extractor fails | Build fails. Platform GA is NOT blocked (the publish job is isolated from the main creatio-ui release pipeline). Manual re-trigger after a fix. |
| Validation fails | Build fails. No upload. |
| Upload fails | Build fails. The `latest.json` update is skipped. Manual re-trigger after the academy issue is resolved. |
| Academy infra unavailable during publish | Same as upload failure. Per-version files retried in the next CI run. |
| Two GA tags published back-to-back | Each runs in its own Jenkins run; `latest.json` is updated by both, ordering reflects build completion order. If two finish out of order, the semver-gate ensures `latest.json` stays at the actual max-semver. |

## Out of scope for this contract

- Curation / overrides applied between extraction and upload. v1 emits the full extracted set.
- Per-version diffing surfaced to the AI. Each CDN file is a self-contained snapshot.
- Pre-release publication (`8.3.0-rc1`). Pre-releases skip the upload entirely.
- Backfill of historical GA tags. Only forward from the first run of the new Jenkins job.
- Multiple parallel "channels" (e.g. `stable.json` vs `beta.json`). v1 has only per-version + `latest.json`.

## Coordination checklist for the Platform-UI team

When this PR is reviewed and the implementation work begins in `creatio-ui`:

- [ ] Confirm CDN upload mechanism with the academy team (open question above).
- [ ] Implement the extractor under `tools/component-registry-extractor/` (see [extractor-analysis.md](extractor-analysis.md) for filters and invariants).
- [ ] Implement the Jenkins pipeline per the sketch above; pin it to GA-tag triggers.
- [ ] Run the pipeline in `dry-run` mode against the current `master` checkout. Compare the output to the current clio in-repo `ComponentRegistry.json` (it must be a **superset**, per [extractor-analysis.md § Regression](extractor-analysis.md)).
- [ ] After the first GA-tag run with `MODE=publish`, confirm clio's CDN client picks up the new `latest.json` on its next 24h refresh.
