# ADR: Isolated, GitHub-delivered clio-ring companion

**Status:** Accepted
**Date:** 2026-07-12

## Decision

Import the proven Ring source at commit `f2bf1eb` into `clio-ring/`, while retaining an independent project graph and release version. clio receives only a local bootstrap command and lifecycle service. The service consumes a small GitHub release manifest, verifies SHA-256, installs side-by-side, and records `current.json` atomically.

The `ring` command is protected by `[FeatureToggle("ring")]`, is Windows-only, and has no MCP primitive. It never installs automatically. Ring has no project reference from clio and clio has no dependency on Avalonia.

## Rationale

This gives internal employees a `dotnet tool install clio` entry point while keeping the POC deletable. Independent ZIP releases avoid inflating the clio tool and let Ring iterate or disappear without coupling clio versions to a UI experiment.

## Distribution contract

The manifest contains `schemaVersion`, `version`, `channel`, `rid`, `assetUrl`, `sha256`, and `entryPoint`. URLs must use HTTPS. The bootstrap rejects unsupported schemas/RIDs, invalid hashes, and ZIP entries escaping the destination.

GitHub Releases for `Advance-Technologies-Foundation/clio` is the explicit publisher trust root for this internal preview. SHA-256 protects transport/artifact consistency; it does not claim to protect against compromise of the repository's release authority. A separate signing identity is deferred unless the POC advances beyond internal preview.

## Removal plan

Delete `clio-ring/`, `.github/workflows/clio-ring-release.yml`, the Ring command/service/tests/docs, and their solution/DI/dispatch entries. No migration of clio settings or environments is required.

## Consequences

- Ring availability depends on GitHub Releases.
- Windows is the only initial RID.
- The repo contains a second independently released product, but it remains structurally isolated.
- There is intentionally no telemetry for the internal preview.
