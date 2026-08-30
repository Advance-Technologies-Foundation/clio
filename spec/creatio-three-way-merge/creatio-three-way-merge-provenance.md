# Creatio conflict resolver transfer provenance

## Source identity

The authorized source candidate is the clean checkout at `F:\Projects\crt-git-integration-app`:

| Item | Git identity |
|---|---|
| Repository commit | `e65852f9521b2c1d288883428b3dd7ebb6fc73be` |
| Resolver source subtree | `4fcf348b4d3ededbaf1388719bdef63dd188f921` |
| Resolver test/fixture subtree | `6235d306d8e9733a83b10f50fadce51b75bb214c` |

The transfer includes only:

- `ConflictResolver/Creatio.ConflictResolver/`;
- `ConflictResolver/Creatio.ConflictResolver.Tests/`, excluding tests that exercise the standalone
  resolver CLI.

The standalone CLI project, batch scripts, solution, and build-output directories are not imported.

## Rights gate

On 2026-08-22 the rights holder confirmed that they have the necessary rights and authorized the
resolver source at commit `e65852f9521b2c1d288883428b3dd7ebb6fc73be` for public modification and
redistribution under the MIT License in the clio repository. The authorization is recorded in the
GitHub issue and covers this source transfer.

The rights holder explicitly did not authorize changes to `crt-git-integration-app`. clio and
`crt-git-integration-app` are independent products. The source repository is read-only provenance
and inspiration for this feature; the clio change must not modify, package, or migrate it.

The import commit must preserve contributor attribution and add the resolver package's license and
repository metadata. This document records provenance; it is not itself the license grant.

## Verified baseline

On 2026-08-22 the clean source commit was validated on Windows with:

```text
dotnet test ConflictResolver\Creatio.ConflictResolver.Tests\Creatio.ConflictResolver.Tests.csproj -c Release --no-restore
Passed: 170, Failed: 0, Skipped: 0
```

`dotnet list ... package --vulnerable --include-transitive` reported no known vulnerable packages
from the configured sources.

The current test project exercises the `net8.0` resolver output while the Creatio app ships the
`netstandard2.0` output. The transfer removes that gap by targeting the canonical resolver library
only at `netstandard2.0`, then running its fixture suite through clio's supported .NET test hosts.

After excluding the three standalone-CLI integration tests, the transferred suite contains 167
tests. All 167 pass against the clio-owned `netstandard2.0` resolver project. A current
`dotnet list ... package --vulnerable --include-transitive` check reports no known vulnerable
packages from NuGet.org.

Before enabling cross-platform CI, normalize fixture identifiers containing Windows path separators.
The semantic assertions and 232 fixture files remain the behavioral source of truth.

## Import verification

The source-transfer commit must prove:

1. every imported file is traceable to one of the two recorded subtrees;
2. intentional adaptations are isolated and reviewed separately from the mechanical import;
3. all retained semantic tests pass on Windows, Linux, and macOS;
4. the built resolver assembly identity remains compatible with the existing Creatio descriptor;
5. the imported project builds as an independent clio-owned `netstandard2.0` library;
6. no file in `crt-git-integration-app` is modified by the implementation or verification work.
