# Design convergence — 2026-09-05

User intent: finish a reusable Clio 10 blueprint and obtain an independent Claude review before user review. Implementation is in the isolated prototype branch; nothing is published or committed.

## Changes agreed with Claude

Design challenge rev_4245b943f9424cce approved the direction with corrections. These implement the P2 concerns from rev_4d992065cdb34335:

- Discovery is metadata only. Keyed scoped DI activates just the selected handler; one broken constructor cannot disable unrelated operations. Plain singleton metadata records avoid TryAddEnumerable deduplication. Duplicate operation names fail explicitly.
- Core.RunAsync owns the callback lifetime and rejects nested roots on that Core. Composition creates one execution scope shared by sequential children. It rejects overlapping children and drains unfinished work before releasing the session.
- Each workflow can access only declared capabilities, and child requirements must fit the root's union and pinned version.
- Pre-execution activation errors, unexpected execution errors and transport uncertainty have distinct codes. Cleanup diagnostics cannot replace a known result or primary exception. Serialization failures preserve known receipts and do not turn accepted MCP operations into execution errors.
- CLI and MCP have logging services, explicit scope validation and consistent override ordering. Missing bundle directories have visible error codes. Manifests explicitly identify implementations without vendor names in Core.

## Verification

The complete Release solution run passes 103 tests: 41 Composition, 28 Core, 9 Primitives and 25 Product. An earlier run exposed omitted entry-point fields in fixture manifests; corrected manifests and bounded test waits now pass the complete run.

Preview 7 and the reusable libraries pack successfully. The tool installs into artifacts/tool-preview7; all five shipping CLI/MCP tests pass against the installed managed DLL. The installed product was tested through both CLI and MCP. Preview 6 executed flush-redis and restart on the dedicated Clio10Architecture lab; both returned Accepted=true, http-accepted and success:true while selecting the 10.0.0.0 bundle using the short 10.0 pin. This proves endpoint acceptance on that stand, not completed process replacement or readiness.

Claude's implementation review rev_ca80b804cffe4430 **approved**, with no P1/P2 findings. It verified that the lifetime and extensibility tests exercise real ownership boundaries. Claude did not run the tests; the results above are our execution evidence.

Four advisory topics were addressed after approval: short numeric versions normalize to four components; output-only adapter conversion omits environment passwords; the missing directory code has a regression test; running-child cancellation and failing workflow-scope cleanup have additional coverage. The focused follow-up rev_4bec444df0744b9f **retained APPROVE**, with no P1/P2 defects. It also accepted the explicit module-once registration restriction. Its optional suggestion to normalize Context.PrimitiveVersion was applied exactly and verified by a new injected-short-version test. This last one-line change was proposed in Claude's review; no further broad review was needed.

Our conclusion: approve this scoped blueprint for user review and incremental feature work. This is not a claim of Clio 8 feature parity or unrestricted plugin compatibility.

One advisory proposal was declined deliberately: silently skipping an existing vendor operation during registration can hide a partner/vendor collision. Modules must be registered once; duplicate names remain an explicit error. CONTRIBUTING documents that adapter callbacks register modules/overrides and must not call AddClioComposition a second time. This is a usage constraint, not automatic idempotency.

```powershell
dotnet test Clio10.slnx -c Release --verbosity quiet
dotnet pack Clio10.slnx -c Release --no-build -o artifacts/packages -p:Version=10.0.0-preview.7
dotnet tool install clio --version 10.0.0-preview.7 --tool-path artifacts/tool-preview7 --configfile scripts/local-packages.config --add-source artifacts/packages
# Set CLIO10_TEST_PRODUCT to the installed Clio10.dll, not the executable shim:
dotnet test tests/Clio10.Product.Tests/Clio10.Product.Tests.csproj -c Release --no-build --filter FullyQualifiedName~Clio10.Product.Tests.ProductTests
```

New coverage includes scoped lifetime across children, undeclared capabilities, root requirement union, duplicate catalogs, nested same/different-target roots, concurrent child rejection and draining, cleanup failure precedence, broken constructors through real CLI/MCP, safe unexpected failures and serialization receipts. Existing tests exercise real loopback Creatio SDK calls and actual same-name bundle DLLs in separate load contexts.

Full read-only Claude reports are retained locally under artifacts/reviews/rev_ca80b804cffe4430.md and artifacts/reviews/rev_4bec444df0744b9f.md.

## Deliberate limits

One complete primitive bundle per single-target root. No mixed capability-version solver, no nested multi-target roots, no cross-process coordination. Partner assemblies and bundles are trusted. A child ignoring cancellation can delay draining. There is no automatic download, hot replacement or plugin-folder discovery. CI redesign remains deferred. Primitive modules can have separate owners but release as one compatible bundle. Managed embedding is supported; NativeAOT dynamic managed loading is not claimed.

## KISS assessment

Application selects an adapter; the adapter calls a workflow; Core supplies one compatible session; primitives perform I/O; data returns to the caller. Metadata separates discovery from activation, keyed DI owns handlers, and Core owns the gate/session. These additions prevent specific observed lifetime and extensibility failures; they do not introduce a workflow engine or another protocol.
