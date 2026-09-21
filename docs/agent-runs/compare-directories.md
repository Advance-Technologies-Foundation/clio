# Task: compare-directories

## Outcome and scope
Implement a read-only local comparison of two directory trees in C:\Projects\clio-10-prototype, preserving all prior dirty work. No Creatio, synchronization, deletion, commits, releases, or changes to older packaged runtimes/hosts. Root owns artifacts/agent-lab telemetry and baseline; TeamLead owns source integration and this record.

## Acceptance and frozen contract
Operation `compare-directories`; required non-whitespace string arguments `left` and `right`. Successful comparison always Accepted=true, Code=completed, even with differences. Payload contains `added`, `removed`, `changed` string arrays: right-only, left-only, and common relative paths with unequal SHA-256 respectively. Matching and ordering use ordinal case-sensitive semantics on every OS. Paths use actual directory separators normalized to `/` (Unix literal backslashes remain literal). Empty directories/metadata excluded. Same/overlapping trees allowed. Trees must remain stable; no atomic snapshot or concurrent mutation sandbox guarantee.

New runtime-owned capability `directory-snapshot`: IDirectorySnapshotPrimitive.SnapshotAsync(string path, CancellationToken) -> Task<IReadOnlyList<DirectoryFileSnapshot>>; record DirectoryFileSnapshot(string RelativePath, string Sha256). Primitive reuses injected IFileHashPrimitive. It rejects reparse points on roots, ancestors and encountered entries, without traversing them; throws DirectoryLinkException : IOException. Missing directory or regular-file root: directory-not-found. UnauthorizedAccessException: directory-access-denied; IOException: directory-read-failed; invalid path argument exceptions: invalid-arguments; DirectoryLinkException: symbolic-link-not-supported. Entire operation fails with no payload; cancellation propagates and cannot produce success. No new resident ABI/Core/adapter behavior.

## Early QA
/root/lab_teamlead/early_qa, fresh default-role fallback, explicit medium effort; read clio_qa TOML. Approved with clarifications: successful differences remain completed; file roots map directory-not-found; only reparse ancestors rejected; do not rewrite Unix literal backslashes. All incorporated before dispatch. Required tests: real tree SHA256/read-only/link checks where permitted, controlled error/cancellation, ordinal diff semantics, CLI and MCP discovery/execution, full regression because feature contracts/wiring change.

## Assignments and dependencies
TeamLead /root/lab_teamlead (medium, fallback reading TOML) owns contracts, shared registrations/manifests, docs and integration. Worker assignments and gates appended below. One explicit build slot, dotnet -m:1 only after source dependencies ready. No build currently granted.

## Decisions
Use one reusable snapshot capability for filesystem traversal and streaming hashes, then Composition compares dictionaries. Architectural principles permit runtime-owned feature API extension; stable Contracts untouched. No Architect or Claude needed.

## Timeline and gates

- 2026-09-06T10:22:17.9093121Z Early QA completed; contract frozen; implementation dispatch starts. Exact earlier stage timestamps are available in tool/session events, not inferred here.

- 2026-09-06T10:24:19.0693270Z Workers dispatched: /root/lab_teamlead/primitives, /root/lab_teamlead/composition, /root/lab_teamlead/adapters; each fresh default fallback, explicit low effort, read own TOML; no build slot granted. Shared registration and manifest integration updated by TeamLead.

- 2026-09-06T10:26:28.8459878Z All three workers ready for review; tests not run by workers. /root/lab_teamlead/early_qa reused at medium for independent per-worker reviews (read reviewer TOML); shared integration source review passed. TeamLead grants itself exclusive build slot after all referenced source exists; starts solution build and regression.

- 2026-09-06T10:27:29.7580202Z Initial solution build passed (0 warnings/errors). Regression running session 55823. Per-worker reviewer /root/lab_teamlead/early_qa (medium) passes Composition and shared integration; primitive/adapters receive Medium fixture portability finding (linked temp ancestors). Correction round 1 assigned to original authors; no delivery accepted yet.

- 2026-09-06T10:30:53.3678890Z Correction round 1 for primitive/product fixture portability independently closed by medium reviewer (source only). Final review dispatched fresh high fallback agents: /root/lab_teamlead/intent_agent (intent), /root/lab_teamlead/kiss_agent (KISS), /root/lab_teamlead/technical_review (maintainability/correctness/security/performance/testing), /root/lab_teamlead/final_qa (behavioral QA). Agentic-code-review skill and reviewer references read. Intent and KISS report no findings. Technical review and independent execution ongoing.

- 2026-09-06T10:31:59.7164938Z Final technical review identified Medium Unix special-file limitation: FIFO open can block before cancellation. Acceptance clarified during final review (not original frozen wording): only stable trees of ordinary files/directories supported; Unix special entries are not detected or safely rejected and may block before cancellation is observed. Root agreed bounded scope; high nonauthor /root/lab_teamlead/technical_review independently approved limitation disposition, not a fix. Avoid platform interop expansion. XML and user docs updated; no executable code changed.

- 2026-09-06T10:32:33.9243801Z Initial full regression completed exit0: Composition200, Core36, Primitives76 passed/1 Unix-only skipped, Product82 passed (log prints optional production-runtime explicit test skipped but summary0 skips). Total394 passed. Runtime code unchanged since this run; rebuild for corrected fixture tests and XML begins under same exclusive TeamLead slot.

- 2026-09-06T10:34:18.5529698Z High final QA /root/lab_teamlead/final_qa independently passed 11 compiled CLI filesystem scenarios including locked-file IO failure and junction root/ancestor/entry rejection, plus unchanged bytes/mtime. Granted exclusive test slot after correction rebuild; focused runs passed: Primitives13/1 explicit Unix-only skip, Composition26, Product5 CLI/MCP. QA released slot. Medium worker review+independent correction closure+corrected tests now permit TeamLead acceptance of all three worker deliveries. High technical five-lens review approves with documented Medium special-file residual; no other findings. TeamLead reacquires slot for isolated new runtime10.11.0 package proof.

## Verification and delivery
- `dotnet build Clio10.slnx -c Release -m:1 --nologo`: passed, 0 warnings/errors; `build.log`. Correction rebuild same command passed; `build-correction1.log`.
- `dotnet test Clio10.slnx -c Release --no-build -m:1 --nologo`: exit0, 394 passed (Composition200/Core36/Primitives76/Product82), one Unix-only skip. `regression.log`. Full regression preceded fixture portability correction; executable feature logic unchanged. Corrected tests were subsequently rebuilt and independently executed.
- Final QA sequential `dotnet test <project> -c Release --no-build -m:1 --filter FullyQualifiedName~<fixture>`: DirectorySnapshotTests13 passed/1 Windows skip; CompareDirectoriesTests26 passed; CompareDirectoriesProductTests5 passed. `qa-primitives.log`, `qa-composition.log`, `qa-product.log`.
- Final QA independently executed 14 real CLI cases plus discovery: ordinal case/order, reverse/same/overlapping trees, equal metadata with differing bytes, invalid inputs, actual sharing violation, junction root/ancestor/entry rejection; SHA256 and mtime unchanged.
- `dotnet publish src/Runtime/Clio10.Runtime.csproj -c Release -m:1 -p:Version=10.11.0 -o artifacts/compare-directories/runtime-10.11.0 --nologo`: passed. Next unused runtime version was confirmed; older 10.10.0 package was preserved.
- `dotnet pack artifacts/compare-directories/RuntimePackage.csproj -c Release -m:1 -o artifacts/runtime-packages --nologo`: passed. Artifact-only package project uses the same complete `bundle/` layout as existing `pack-primitive-bundle.ps1`, with explicit bounded MSBuild execution. No shipping packaging logic changed.
- Extracted `Clio10.RuntimeBundle.10.11.0.nupkg`; executed all5 CompareDirectoriesProductTests with `CLIO10_TEST_PRODUCT` pointing at unchanged installed preview20 Clio10.dll, `CLIO10_RUNTIME_COMPOSITION=true`, `CLIO10_BUNDLES` pointing at package-extracted, `CLIO10_PRIMITIVE_VERSION=10.11.0`: passed; `package-proof.log`. Old host SHA256 before/after equal (`old-host-sha256.txt`). This proves complete package execution through existing CLI/MCP adapters, not hot activation over an already-running connection.

All logs above are under `artifacts/compare-directories/` unless explicitly named. Root owns measured usage artifacts under `artifacts/agent-lab/`; this record makes no unobserved token/cost or role-discovery claims. Every subagent used explicit default-role fallback and read assigned TOML; canonical IDs appear above. No Claude/Architect required because no architectural exception arose.

Docs and MCP reviewed: generic discovery/execute exposes the new workflow without adapter source or resident MCP changes. New runtime-owned capability and manifests align. No Core or stable Contracts source changed. Older work and packages/hosts preserved; no commit, push, release, Creatio access or global tool install.

KISS check: compare ordinary local directory files by content; one primitive enumerates paths and reuses streaming SHA256, one workflow compares two snapshots, existing adapters serialize results. No synchronization, deletion, custom coordination, or native interop added. Unsupported special-file/cancellation limitation is explicit above. Windows behavior is verified; macOS/Linux execution and actual ACL denial remain unverified (controlled access exception is tested).

- 2026-09-06T10:36:28.4018877Z Package proof session47706 completed exit0 with5 tests. TeamLead releases build slot. All final review lenses approve; high intent independently rechecked special-file clarification with no findings. High QA inspecting package evidence for final closure.

- 2026-09-06T10:37:06.3956991Z Final independent packaging QA closed: /root/lab_teamlead/final_qa verified all27 bundle files SHA256-identical across actual nupkg, publish output and extracted runtime; nuspec/manifest/assembly identities match10.11.0, stable Contracts10.4.0.0. Installed host hash matches baseline. Package SHA256: 979B0981F139A156EFF84E88AA5335A4CEFF0CF2C5FBC80A1D6642DDBFD9D98C. Acceptance and review complete with explicit limitations; no further work pending in TeamLead scope.
