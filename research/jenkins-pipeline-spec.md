# creatio-ui Jenkins pipeline: contract for component-registry publishing

This document defines the **producer contract** that the creatio-ui Platform-UI team owns: what JSON shape to emit, where to publish it, on which trigger. This branch does not implement the Jenkins job — the implementation lives in a separate cross-repo PR in the creatio-ui repository. This file is the design reference for that PR.

## Storage and delivery topology

There are two systems on the producer side, owned by different teams:

| Layer | Where | Who owns | Role |
|---|---|---|---|
| Storage | `gitdigital.creatio.com/academy/static-files-mcp` (GitLab) | Academy team (write access shared with the Jenkins job) | Authoritative store of the per-version JSON files; full git history of every published payload |
| Edge | `https://academy.creatio.com/api/mcp/` | Academy infrastructure | Public read-only mirror of the GitLab repository, refreshed every 5 minutes |

The producer (Jenkins, planned) **only writes to the GitLab repository.** It never talks to the academy edge directly. The academy mirror job is what makes a payload visible to clio.

**Current state (interim).** Until the Jenkins automation in `creatio-ui` lands, the `static-files-mcp` repository is updated manually. The mirror is already live — anything committed to the repository appears at `https://academy.creatio.com/api/mcp/...` within ≤5 minutes.

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
| Path convention | `{Major.Minor.Patch}/ComponentRegistry.json`, e.g. `8.3.4/ComponentRegistry.json`. SemVer 3-part directory, no prefix, no build/revision component, no leading `v`; the filename is always `ComponentRegistry.json`. |
| Special path | `latest/ComponentRegistry.json` — copy of the highest-semver published GA payload |
| Destination | `gitdigital.creatio.com/academy/static-files-mcp` (GitLab). Commit the payload under the path above on the `master` branch. The academy mirror copies it to `https://academy.creatio.com/api/mcp/{path}` on its next 5-minute tick. |
| Trigger | GA-tag in creatio-ui (`8.2.0`, `8.2.1`, `8.3.0`, …). Branch-cut runs the same job in **baseline mode** (artifact only; no git push). |
| Cache headers (recommended) | `Cache-Control: public, max-age=300` (matches clio's 5min TTL and the 5-minute mirror cadence); `ETag` strongly recommended. Headers are emitted by the academy edge — the producer does not set them. |
| Access | Public read on the CDN edge, no authentication. GitLab write access is restricted to the academy team and the Jenkins service account. |
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
| GA-tag `8.2.0` | `8.2.0/ComponentRegistry.json` + maybe `latest/ComponentRegistry.json` (see § latest alias) |
| GA-tag `8.2.1` | `8.2.1/ComponentRegistry.json` + maybe `latest/ComponentRegistry.json` |
| GA-tag `8.3.0` | `8.3.0/ComponentRegistry.json` + `latest/ComponentRegistry.json` (if 8.3.0 > current latest) |
| Branch-cut `release/8.3.x` | (no git push to `static-files-mcp`; Jenkins artifact only) |
| Hotfix backport tag `8.2.5` after `8.3.0` is GA | `8.2.5/ComponentRegistry.json` (NO `latest/ComponentRegistry.json` update — 8.3.0 > 8.2.5) |
| Pre-release `8.3.0-rc1` | **Skip.** No git push for pre-release tags. |

Filename validation in the Jenkins pipeline: regex `^[0-9]+\.[0-9]+\.[0-9]+$`. Pre-release suffixes (`-rc1`, `-beta`) skip the upload step (early return from the pipeline).

## `latest/ComponentRegistry.json` semantics

`latest/ComponentRegistry.json` is a pointer to the freshest GA. It is **updated only when** the version being published is strictly greater (semver-sorted) than the version currently pointed to by `latest/ComponentRegistry.json`.

Mechanics:
1. Compute the current `latest` semver. Two options the Jenkins job can pick from: (a) `git ls-tree` the `static-files-mcp` repo and parse the directory names; (b) walk back through the git history of `latest/ComponentRegistry.json` and read the commit message tag set by previous runs. Option (a) is the simpler one and is what the sketch below uses.
2. Compare `newVersion` (just-extracted) vs `currentLatest`.
3. If `newVersion > currentLatest`: in the same commit, write both `{newVersion}/ComponentRegistry.json` and `latest/ComponentRegistry.json` with identical content.
4. If `newVersion <= currentLatest`: write only `{newVersion}/ComponentRegistry.json`. Skip the `latest/...` update.

The semver comparison MUST use strict semver-2.0.0 ordering, not lexicographic. `8.10.0 > 8.9.0` despite `"8.10.0" < "8.9.0"` lexicographically.

**Bootstrap:** before the very first publication, `latest/ComponentRegistry.json` does not exist. The first GA-tag commit creates both `{version}/ComponentRegistry.json` and `latest/ComponentRegistry.json` unconditionally.

## Trigger model

### GA-tag (primary)

Jenkins multi-branch pipeline subscribes to tag pushes matching `^\d+\.\d+\.\d+$` on the creatio-ui repo. On match:

1. Check out the tagged commit.
2. Run the extractor (see § Extractor pipeline).
3. Validate the output file (schema + duplicates).
4. Clone the `static-files-mcp` repository (shallow clone is fine).
5. Compute the `latest/ComponentRegistry.json` semver gate against the existing directory tree.
6. Write `{version}/ComponentRegistry.json` and — if the semver gate passes — `latest/ComponentRegistry.json` in the same commit.
7. Git push to `master` (per § Push mechanism).
8. Post a Slack/Jira comment to the release thread with the resulting `https://academy.creatio.com/api/mcp/...` URLs.

### Branch-cut (baseline)

When `master` forks into a `release/8.X.x` branch, the same job runs in **baseline mode** — produces the artifact, attaches it to the Jenkins build, and posts a diff vs the previous-line GA. **No git push to `static-files-mcp`.**

This is a regression detector: if the extractor inadvertently dropped a component between two minor lines, the diff catches it before GA.

### Manual

The Jenkins job exposes a `workflow_dispatch`-style parameterized build:

| Parameter | Type | Purpose |
|---|---|---|
| `MODE` | `enum: dry-run \| publish` | `dry-run` produces the artifact, validates, prints what WOULD be pushed. `publish` actually pushes to `static-files-mcp`. |
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
                    curl -sf https://academy.creatio.com/api/mcp/latest/ComponentRegistry.json \
                        -o /tmp/previous-latest.json || true
                    node tools/component-registry-extractor/diff.js \
                        dist/component-registry/output.json \
                        /tmp/previous-latest.json > diff-report.txt || true
                '''
                archiveArtifacts artifacts: 'diff-report.txt, dist/component-registry/output.json'
            }
        }

        stage('Publish to static-files-mcp') {
            when { expression { env.PUBLISH_VERSION != '' && params.MODE != 'dry-run' } }
            steps {
                sshagent(credentials: ['static-files-mcp-deploy-key']) {
                    sh '''
                        set -eu
                        REPO_DIR=$(mktemp -d)
                        git clone --depth 1 \
                            git@gitdigital.creatio.com:academy/static-files-mcp.git \
                            "$REPO_DIR"

                        cd "$REPO_DIR"
                        mkdir -p "${PUBLISH_VERSION}"
                        cp "$WORKSPACE/dist/component-registry/output.json" \
                            "${PUBLISH_VERSION}/ComponentRegistry.json"

                        # latest alias (semver-gated against the existing tree)
                        CURRENT_LATEST=$(ls -1 | grep -E '^[0-9]+\\.[0-9]+\\.[0-9]+$' \
                            | sort -V | tail -1 || echo "0.0.0")
                        if [ "$(printf '%s\\n%s\\n' "$CURRENT_LATEST" "$PUBLISH_VERSION" \
                                | sort -V | tail -1)" = "$PUBLISH_VERSION" ] \
                           && [ "$CURRENT_LATEST" != "$PUBLISH_VERSION" ]; then
                            mkdir -p latest
                            cp "${PUBLISH_VERSION}/ComponentRegistry.json" \
                                latest/ComponentRegistry.json
                        fi

                        git add -A
                        git -c user.name='creatio-ui Jenkins' \
                            -c user.email='ci@creatio.com' \
                            commit -m "Publish ${PUBLISH_VERSION}/ComponentRegistry.json"
                        git push origin master
                    '''
                }
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

The `static-files-mcp-deploy-key` Jenkins credential carries the SSH key with write access granted by the academy team. Concrete shell flags may vary — what matters is the contract: a single commit per GA tag, on `master`, containing one or two files under the version directory.

## Push mechanism (decided)

The publishing surface is the `static-files-mcp` GitLab repository, **not** the academy edge. The academy team owns the mirror that serves the CDN tree under `/api/mcp/`; the producer never talks to that endpoint directly.

Concrete contract:

| Aspect | Value |
|---|---|
| Protocol | git (SSH or HTTPS with a Personal Access Token) |
| Repository | `gitdigital.creatio.com/academy/static-files-mcp` |
| Branch | `master` |
| Commit shape | One commit per GA tag; touches only files under `{version}/` and optionally `latest/` |
| Commit message | `Publish {version}/ComponentRegistry.json` (extended with the GA tag and a Jenkins build URL is recommended) |
| Atomicity | A single git commit ensures clio never sees a half-written payload |
| Audit trail | Free — every push is `git log` on the repository |

The Jenkins job MUST:
1. Use a dedicated service-account credential, not a personal token.
2. Clone shallow (`--depth 1`) and operate inside a fresh temp dir per build.
3. Compute the semver gate against the existing repo tree, not against a sidecar metadata file.
4. Commit both `{version}/...` and `latest/...` in the same commit when the gate passes, so the mirror sees a consistent pair.
5. Log the resulting `https://academy.creatio.com/api/mcp/{version}/ComponentRegistry.json` URL in the build output (with a note that the academy mirror lag may add up to 5 minutes).

Cache headers, TLS, and the public read surface are emitted by the academy edge and are not the producer's responsibility — they apply uniformly to whatever the mirror copies into the CDN tree.

## Cache headers (academy edge, recommended)

These headers are emitted by the academy mirror, not by the producer. They are recorded here so producers and consumers agree on the contract.

| Header | Value | Why |
|---|---|---|
| `Cache-Control` | `public, max-age=300` | clio uses a 5min app-level TTL aligned with the 5-minute mirror cadence; matching CDN edge TTL avoids stale-edge issues. Longer would force AI to wait extra ticks for a freshly mirrored payload. |
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

The clio build (`dotnet pack`) runs an MSBuild target that GETs `latest/ComponentRegistry.json` from the CDN. While the producer pipeline is being built up, the CDN may serve an empty `latest/` (404) or only a manually-primed payload. clio handles this via:

- A committed seed-snapshot at `clio/Command/McpServer/Data/ComponentRegistry.seed.json` (the file moved/renamed from the current in-repo `ComponentRegistry.json`).
- The MSBuild target falls back to the seed file when the CDN fetch fails.

This means clio releases can ship before the Jenkins automation is live, and offline builds (CI runners without internet) continue to work — they just freeze the embedded snapshot at whatever the seed file is.

Cross-team coordination: the creatio-ui team's "first push to `static-files-mcp`" milestone unblocks clio's MSBuild target picking up a fresh `latest/ComponentRegistry.json` automatically. Until that milestone, the `static-files-mcp` repository is primed manually for the GA versions that AI needs today (`8.3.4/...` and `latest/...` are already live), and clio's embedded snapshot tracks the seed file.

## Failure modes (producer side)

| Scenario | Behavior |
|---|---|
| Extractor fails | Build fails. Platform GA is NOT blocked (the publish job is isolated from the main creatio-ui release pipeline). Manual re-trigger after a fix. |
| Validation fails | Build fails. No git push. |
| Git push fails (auth, network, conflict) | Build fails. Nothing changes on `static-files-mcp`; nothing changes on the academy edge after the next mirror tick. Manual re-trigger after the issue is resolved. |
| Mid-air collision (two CI runs pushing concurrently) | The second push is rejected by GitLab (`non-fast-forward`). The Jenkins job retries `git fetch` + replay, or fails for manual intervention — whichever the team prefers. The semver gate must be re-evaluated after the fetch. |
| Academy mirror unavailable | The git push still succeeds (it only touches GitLab). The CDN edge serves the prior payload until the mirror recovers; once it does, the new files appear within ≤5 minutes. |
| Two GA tags published back-to-back | Each runs in its own Jenkins run; `latest/ComponentRegistry.json` is updated by both, ordering reflects build completion order. If two finish out of order, the semver-gate ensures `latest/ComponentRegistry.json` stays at the actual max-semver. |

## Out of scope for this contract

- Curation / overrides applied between extraction and upload. v1 emits the full extracted set.
- Per-version diffing surfaced to the AI. Each CDN file is a self-contained snapshot.
- Pre-release publication (`8.3.0-rc1`). Pre-releases skip the upload entirely.
- Backfill of historical GA tags. Only forward from the first run of the new Jenkins job.
- Multiple parallel "channels" (e.g. `stable.json` vs `beta.json`). v1 has only per-version + `latest.json`.

## Coordination checklist for the Platform-UI team

When this PR is reviewed and the implementation work begins in `creatio-ui`:

- [ ] Coordinate with the academy team to provision a Jenkins service-account SSH key with write access to `gitdigital.creatio.com/academy/static-files-mcp`.
- [ ] Implement the extractor under `tools/component-registry-extractor/` (see [extractor-analysis.md](extractor-analysis.md) for filters and invariants).
- [ ] Implement the Jenkins pipeline per the sketch above; pin it to GA-tag triggers.
- [ ] Run the pipeline in `dry-run` mode against the current `master` checkout. Compare the output to the current clio in-repo `ComponentRegistry.json` (it must be a **superset**, per [extractor-analysis.md § Regression](extractor-analysis.md)).
- [ ] After the first GA-tag run with `MODE=publish`, confirm the new files appear under `https://academy.creatio.com/api/mcp/{version}/ComponentRegistry.json` within ≤5 minutes, then confirm clio's CDN client picks up the new `latest/ComponentRegistry.json` on its next 5min refresh.
