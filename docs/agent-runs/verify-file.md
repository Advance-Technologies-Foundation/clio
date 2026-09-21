# Task: verify-file

## Outcome and scope

Implement a read-only local SHA-256 verification operation through a new runtime-owned primitive and existing generic CLI/MCP adapters. Checkout: `C:/Projects/clio-10-prototype`, branch `prototype/clio-10-layering`. New functionality, not a legacy parity port: the inventory remains 21 ports. No Core/Contracts ABI, adapter machinery, live Creatio, commit or publication changes.

## Acceptance

Known digest and empty-file cases return digest, bytes and match status. Mismatch is nonaccepted. Missing files and malformed/missing digest produce stable failures; cancellation propagates. Real temporary-file primitive tests, controlled Composition tests and real CLI/MCP subprocess tests must pass. A complete runtime 10.10.0 must run on the unchanged installed preview20 host (Contracts ABI 10.4.0.0); preserve 10.8 and 10.9 artifacts. Independent review must pass.

## Frozen contract (before worker dispatch)

- Operation: `verify-file`, read-only (`Destructive=false`). Required string arguments: `path` and `sha256`. No aliases or position mapping required: generic `--path` and `--sha256` options suffice.
- Expected digest: exactly 64 hexadecimal ASCII characters, case insensitive; no whitespace trimming. Empty/whitespace path invalid; otherwise preserve path exactly. Validation happens before primitive access.
- Runtime-owned capability `file-hash`: `IFileHashPrimitive.HashAsync(string path, CancellationToken cancellationToken)` returns `Task<FileHashResult>`. `FileHashResult` is a record with `string Sha256` (lowercase hex) and `long Bytes` (actual bytes read). Define in `src/PrimitiveContracts/FileHash.cs`.
- Primitive streams file data with bounded memory, checks cancellation and owns stream disposal. Normal filesystem exceptions cross to Composition; no raw exception text in result.
- Workflow `VerifyFileWorkflow` in `src/Composition/Files/VerifyFileWorkflow.cs`, static `Descriptor` and `Requirement` using version range 10.0 inclusive to 11.0 exclusive and capability `file-hash`. TeamLead registers workflow in existing shared composition registration.
- Payload is dictionary keys `sha256`, `bytes`, `matches`, using actual digest, byte count and boolean. Match: Accepted=true/Code=completed. Mismatch: Accepted=false/Code=hash-mismatch, payload retained. Both include pinned PrimitiveVersion. Invalid input: invalid-arguments. FileNotFoundException/DirectoryNotFoundException: file-not-found. UnauthorizedAccessException: file-access-denied. Other IOException: file-read-failed. Cancellation is propagated, never translated into mismatch.
- Feature contracts travel in complete runtime; approved PrimitiveContracts separation permits this extension without resident ABI changes.

## Assignments and dependencies

TeamLead owns this record, shared `CompositionRegistration.cs`, `HttpPrimitiveBundle.cs`, `RuntimeBundle.cs`, runtime csproj manifest, package/integration scripts and feature documentation. Root owns .codex agent definitions, AGENTS.md, agent-team and task-template docs. Existing broad staged prototype replacement and untracked files are preserved.

| Task | Agent/role | Owned files | Depends on | Required checks | State |
|---|---|---|---|---|---|
| Primitive | /root/pilot_teamlead/primitive_worker / clio_primitives fallback | FileHash contract, FileHashPrimitive.cs, FileHashTests.cs | frozen contract | real temp-file primitive tests | complete; TeamLead verified |
| Composition | /root/pilot_teamlead/composition_worker / clio_composition fallback | VerifyFileWorkflow.cs, VerifyFileTests.cs | frozen contract | controlled response/failure/cancellation tests | complete; TeamLead verified |
| Adapter proof | /root/pilot_teamlead/adapter_worker / clio_adapters fallback | VerifyFileProductTests.cs | frozen contract | real CLI/MCP tests including installed runtime | complete; TeamLead verified |

One build slot: workers request it; TeamLead grants explicitly. All dotnet build/test commands use `-m:1`. Current session uses default agents reading role TOMLs because custom roles were not in the captured session inventory; automatic custom-role discovery is not claimed.

## Decisions and escalations

No architectural escalation required for this ordinary new feature capability. TeamLead/workers/reviewers do not call Claude or Collab.

## Verification

Completed; exact evidence is recorded below.

## Independent review

Independent round 1 and final nonauthor acceptance recheck passed; evidence follows below.

## Delivery

Acceptance and implementation review passed. KISS flow: generic adapter -> validation/comparison workflow -> streaming hash primitive -> structured result.

### Dispatch log

- TeamLead `/root/pilot_teamlead` read `clio_teamlead.toml` explicitly.
- `/root/pilot_teamlead/primitive_worker` dispatched with clio_primitives role instructions and the three primitive-owned files.
- `/root/pilot_teamlead/composition_worker` dispatched independently with clio_composition role instructions and the workflow/test files.
- `/root/pilot_teamlead/adapter_worker` dispatched independently with clio_adapters role instructions and only VerifyFileProductTests.cs.
- Primitive worker requested and received the first build slot for its targeted real-file tests.
- TeamLead updated RuntimeV1/V2/V3 fixture manifests to include file-hash because they inherit the current production RuntimeBundle; actual bundle capabilities must equal manifest capabilities. Existing 10.8/10.9 production artifacts remain immutable.
- Adapter investigation confirmed conventional CLI missing required input keeps the existing parser behavior (exit 2, stderr); generic --execute and MCP return structured invalid-arguments. No parser rewrite is needed.
- Preservation baseline: artifacts/verify-file-preserved-before.json hashes all installed preview20 host directory files and published runtime10.8/10.9 files. Initial Clio10.dll SHA-256: 004EE30D2396FBA21E191698F266CCD68017E6FFF3E8B2CF27F81252A18F63AA.

### Integration log

- Primitive worker's first requested targeted build compiled its contract/primitive but stopped before tests because the parallel Composition file was not yet present.
- Composition worker reported 24/24 targeted tests passing. A grant/revocation message crossed with the worker starting its brief test command; TeamLead also started integration before receiving the completion. No output corruption was observed, but this pilot does not claim flawless build-slot coordination. Subsequent verification is TeamLead-only, after every worker confirmed no further builds.
- First solution integration passed Composition 174 and Primitives 64, but exposed the V2 primitive fixture's explicit compile list missing FileHashPrimitive. TeamLead added that source and file-hash manifest metadata. Runtime fixture manifests were already aligned.
- Adapter worker completed five real-product test cases and lifecycle inspection without running a build, per TeamLead instruction.

### Review scope

Review the pilot as an explicit bounded change over the existing uncommitted prototype, not the broad staged Clio8 replacement. Source/new tests: src/PrimitiveContracts/FileHash.cs; src/Primitives/FileHashPrimitive.cs; src/Composition/Files/VerifyFileWorkflow.cs; tests/Clio10.Primitives.Tests/FileHashTests.cs; tests/Clio10.Composition.Tests/VerifyFileTests.cs; tests/Clio10.Product.Tests/VerifyFileProductTests.cs. Integration: file-hash registration in src/Primitives/HttpPrimitiveBundle.cs, src/Runtime/RuntimeBundle.cs, src/Runtime/Clio10.Runtime.csproj; VerifyFileWorkflow registration in src/Composition/CompositionRegistration.cs; tests/Fixtures/V2/PrimitiveBundle.V2.csproj source/capability and RuntimeV1/V2/V3 csproj capability additions. Docs: docs/verify-file.md, this record, README.md pilot paragraph. Root-owned agent configuration/docs are reviewed separately by root. No Core/Contracts/CLI/MCP source edits.

### Independent review round 1

Used agentic-code-review skill and its reviewer-roles reference. Three independent default-role agents explicitly loaded their project role instructions; none authored implementation or ran builds:

- `/root/pilot_teamlead/intent_agent` (clio_intent): no findings; requires remaining package/preservation acceptance evidence.
- `/root/pilot_teamlead/kiss_agent` (clio_kiss): no findings; minimal adapter -> workflow -> streaming primitive flow.
- `/root/pilot_teamlead/combined_reviewer` (clio_reviewer): no findings across correctness, security, performance, maintainability and testing; functional proof remains TeamLead's responsibility.

No source corrections requested. Review scope is explicitly bounded above because the checkout contains an earlier broad uncommitted prototype replacement.

### Confirmed regression and package commands

1. `dotnet test Clio10.slnx -c Release -m:1 --logger "console;verbosity=minimal"` — exit 0; Composition 174, Core 36, Primitives 64, Product 77 passed (351 total). Product took 4m43s. Log: artifacts/verify-file-regression.log. The opt-in pre-existing Production_runtime_adds_package_file_command was reported as skipped because its dedicated artifact environment variables were absent; the framework's summary separately reports 77 passed/0 skipped. No live Creatio was used.
2. `dotnet publish src/Runtime/Clio10.Runtime.csproj -c Release -m:1 -p:Version=10.10.0 --artifacts-path artifacts/runtime-release-build/10.10.0 -o artifacts/published-runtime-check/10.10.0` — exit 0, isolated new runtime output. Log: artifacts/verify-file-runtime-publish.log.
3. Generated the existing payload-only package project pattern at artifacts/bundle-pack/Clio10.RuntimeBundle/10.10.0/PrimitiveBundle.csproj, then `dotnet pack artifacts/bundle-pack/Clio10.RuntimeBundle/10.10.0/PrimitiveBundle.csproj -c Release -m:1 -o artifacts/runtime-packages --verbosity quiet` — exit 0. Log: artifacts/verify-file-runtime-pack.log. Actual package Clio10.RuntimeBundle.10.10.0.nupkg SHA-256: 2A535D623A2F92DCD0FDC7D196338918CF5145E6B31CB4B7840062446C1646DF.
4. Extracted that actual nupkg with ZipFile.ExtractToDirectory into artifacts/verify-file-package-extracted/10.10.0. Installed proof uses this extracted payload, not unbundled build output.

### Installed-host acceptance

Ran exactly:

```powershell
$env:CLIO10_TEST_PRODUCT = (Resolve-Path 'artifacts/tool-preview20/.store/clio/10.0.0-preview.20/clio/10.0.0-preview.20/tools/net10.0/any/Clio10.dll').Path
$env:CLIO10_RUNTIME_COMPOSITION = 'true'
$env:CLIO10_BUNDLES = (Resolve-Path 'artifacts/verify-file-package-extracted/10.10.0').Path
$env:CLIO10_PRIMITIVE_VERSION = '10.10.0.0'
dotnet test tests/Clio10.Product.Tests/Clio10.Product.Tests.csproj -c Release -m:1 --no-build --filter FullyQualifiedName~VerifyFileProductTests --logger 'console;verbosity=normal'
```

Exit 0; 5/5 passed in 15.33s. `--no-build` ensured this proof used the pre-existing installed host with the actual extracted package. CLI/MCP discovery, abc/empty success, mismatch receipt, missing file/directory, malformed/missing digest and CLI usage-error behavior all passed. Each successful/mismatched receipt asserted runtime 10.10.0.0. Log: artifacts/verify-file-installed-proof.log.

Rehashed all 108 baseline files after proof: zero changed. Both installed host and packaged runtime Contracts identities are 10.4.0.0. Receipt: artifacts/verify-file-proof.json. Exact CLI example also ran on the installed host:

```powershell
[IO.File]::WriteAllBytes((Join-Path (Get-Location) 'artifacts/verify-file-abc.txt'), [byte[]](97,98,99))
dotnet $env:CLIO10_TEST_PRODUCT verify-file --path ./artifacts/verify-file-abc.txt --sha256 ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad
```

Result saved in artifacts/verify-file-example.json: accepted/completed, runtime 10.10.0.0, sha256=ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad, bytes 3, matches=true. No host replacement, Core/Contracts source edits, generic adapter edits or legacy inventory count changes occurred. Source regression builds the current source product normally; the separate installed-host proof never uses that rebuilt product.

### Final acceptance and KISS check

- Streaming primitive: published abc/empty/million-a vectors, actual counts, file release and cancellation cases pass.
- Composition: 24 controlled policy/error/cancellation cases pass; all 174 Composition tests pass.
- Generic CLI/MCP: source checks and all 5 installed-host cases pass.
- Runtime 10.10.0 is packaged locally, extracted and executed on unchanged preview20, ABI 10.4.0.0.
- Read-only: verification input preserved; no Creatio target, commit, push, publication or host install was performed.
- KISS: exactly one capability interface/result, one streaming primitive and one validating/comparing workflow, wired through existing registration/manifest/generic adapter paths. No unnecessary production moving parts identified by the independent KISS review.
- Limitations: this proves one pilot, not automatic custom-role discovery or scalability to 100 agents. Cancellation tests cover pre-cancelled filesystem calls and controlled in-flight workflow cancellation, not a deterministic mid-read OS cancellation race. File verification observes bytes read, not publisher identity or future immutability. Build-slot message crossing is recorded above; final functional verification was run only by TeamLead.

### Final nonauthor acceptance recheck

`/root/pilot_teamlead/intent_agent` returned **Pass — no findings** after reading final regression/package/example receipts, independently rehashing all 108 baseline files (zero changes), and independently reading both Contracts assembly identities (10.4.0.0). Prior acceptance gaps are closed. No source changes were needed after independent review. All implementation workers and reviewers are complete; final process sampling showed no remaining pilot dotnet/test subprocesses (only the pre-existing host process remained).
