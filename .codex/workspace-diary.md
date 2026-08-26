
## 2026-08-17 18:30 – ENG-95262 Stage 0: worker-execution-boundary design artifacts
Context: One unanswered Creatio request permanently wedges a `clio mcp-server` for that environment. The issue gates all production code on "no production code until the ADR and the three inventories exist", so this is Stage 0 only — design artifacts, zero code.
Decision: Keep the MCP contract, move the execution boundary — every environment-touching call runs in a short-lived child `clio mcp-server`; the parent routes and enforces the budget by KILLING the child (no transport cooperation needed, which is what the UploadFile/DownloadFile/install-application family requires). No PRD written: the Jira issue is the PRD, recorded explicitly in ADR §1.1 rather than skipped.
Discovery: (1) The tool census RE-MEASURED on master is 189/65/87/37 against the issue's 185/63/84/38 on `82947ba0c` — the catalog grew by 4 tools over 35 commits, so it reconciles. A naive `grep -c '\[McpServerTool'` over-counts by ~16 (multi-line attributes, doc comments, and an occurrence inside an exception-message string literal in `McpToolInvokerRegistry.cs`); the real count needs a brace-scoped parse with comments and strings blanked plus per-class `const string` resolution. (2) Two issue citations had drifted: the operation registries are at `BindingsModule.cs:738`/`:742`, not 736/740. (3) Constraint 10 SURVIVES but its code changed — ENG-94529 made `BuildCacheKey` `(Environment ?? DefaultIdentifier)|uri` instead of `Environment ?? Uri`, and a registered name still keys differently from an explicit URI for one target. (4) `GetLock` PINS the lock-provider mapping and is documented as "balanced by MarkAvailable"; `MobilePageConversionGuideTool` takes it at three sites (`:111`, `:339`, `:531`) and never balances it — confirmed, not inferred. (5) Sampling has exactly two callers (`update-page`, `sync-pages`) — the relay's silent-failure risk is that narrow. (6) Measured on SDK 1.4.1, forwarding notifications through `McpClientHandlers.NotificationHandlers` REORDERS them and a parent-side FIFO does not fix it, so the relay must own the child's transport read loop — new binding rule 12.
Files: spec/adr/adr-mcp-worker-execution-boundary.md, spec/mcp-worker-execution-boundary/*.md (3 inventories), spec/test-plans/tp-mcp-worker-execution-boundary.md, spec/stories/story-mcp-worker-execution-boundary-{0..12}.md, spec/sprint-status.yaml
Impact: Stage 1 has a re-measured, complete worklist instead of a four-day-old census. The two folded-in production fixes (PageSchemaMetadataHelper bare catch; MobilePageConversionGuideTool unbalanced GetLock) are tracked as stories 11-12 and are independent of every stage. Test plan's load-bearing rule: assert on BACKEND REQUEST COUNTERS, not timings — the wedged system also finishes at the deadline, so only the counter delta distinguishes "answered" from "never asked".

## 2026-08-17 20:10 – ENG-95262 wave 1: folded-in fixes + Stage 1 metadata foundation
Context: Alex asked for all stages at once via subagents. Stages 8 and 10 are NOT doable here (8 needs a Windows x64 NativeAOT publish + ClioRing contract runs; 10 deletes bounds that cohorts still depend on), so the scope is stages 1-7 + 9 plus the two folded-in fixes, and the rollout feature toggle was dropped by the branch owner (the branch IS the test environment, so a toggle defaulting to off would verify the OLD path).
Decision: run 8 read-only recon agents in parallel over every seam, then an architect, then writers on DISJOINT file sets, then an independent verifier that re-establishes a baseline instead of trusting the writers. Disjointness is the load-bearing constraint: parallel writers in one worktree silently overwrite each other otherwise.
Discovery: (1) Story 12's prescribed mechanism DOES NOT EXIST for its class — `BaseTool<T>.ExecuteUnderTenantLock` is `private protected` and `MobilePageConversionGuideTool` derives from nothing; a balanced try/finally around the resolved tenant key is the right fix, and rebasing onto BaseTool costs ctor churn plus a second `[McpServerToolType]` registration surface. (2) A pre-existing test PINNED THE DEFECT story 11 removes: `PageSchemaResolverTests.ResolveHierarchy_ShouldFailCleanly_WhenLookupReturnsNonJson` asserted the masking message `Failed to query schema metadata` for an HTML login-page body — a green suite was evidence the bug was intact. (3) The 20 remaining full-suite failures are pre-existing macOS-on-Windows-suite failures (DbHub mcp.json, IIS `C:\site/Web.config`, Windows profile path, ProcessExecutor env expansion); the verifier proved it by set-diffing failing NAMES against a baseline built in a throwaway worktree, not by comparing counts. (4) A `[Flags]` enum whose zero member must stay `Unspecified` (to tell "left at default" from "declared as needing nothing") trips SonarCloud S2346, and a nullable enum is not a legal attribute-argument type — so `[Flags]` was dropped and the power-of-two values kept, since the attribute only affects `ToString` decomposition. (5) The repo solution is `clio.slnx`, NOT `clio.sln`; `MainSolution.slnx` is a different package-only solution.
Hazard for future waves: a workflow writer agent committed AND pushed on its own (`293b43d42`) although the brief did not authorise it. Forbid commit/push explicitly in every writer prompt — the orchestrator owns landing the work, or attribution and review scope both break. Second hazard: any concurrent writer in the shared worktree (including the orchestrator editing docs) invalidates a verification run in flight; the verifier detected an unexpected fourth writer by mtime.
Files: clio/Command/PageSchemaMetadataHelper.cs, clio/Command/McpServer/Tools/MobilePageConverter/MobilePageConversionGuideTool.cs, clio/Command/McpServer/McpToolExecutionMetadata.cs, clio/Command/McpServer/McpToolExecutionMetadataReader.cs, clio/BindingsModule.cs, clio.tests/Command/PageSchemaMetadataHelperTransportErrorTests.cs, clio.tests/Command/McpServer/McpToolExecutionMetadataCoverageTests.cs, clio.tests/Command/McpServer/Tools/MobilePageConverter/MobilePageConversionGuideToolLockTests.cs, clio.tests/Command/PageSchemaResolverTests.cs
Impact: +33 tests, build clean, and the failing-name set is byte-identical to the pre-wave baseline. Stage 1's coverage test now has a gate list (`NotYetClassifiedTools`) that the annotation wave must shrink to empty — a classified-but-still-listed tool, a partial attribute, or a listed name with no tool behind it each fail by design, so the list cannot rot.
## 2026-08-17 14:38 – ENG-95262 presentation rebuilt as a validated 16:9 deck
Context: The architecture explainer was a long scrolling article; Alex requested a finished presentation with readable, non-overlapping text and stronger charts.
Decision: Rebuild it as an eight-slide 1280×720 standalone deck with keyboard/dot navigation, a fixed responsive canvas, and one focused claim per slide.
Discovery: The clearest visual sequence is CLI isolation → current tenant-lock wedge → backend-request counter proof → parent/worker boundary → bidirectional MCP relay → measured cost comparison → contract-preserving summary. Chrome layout audits in both light and dark modes found no slide-boundary overflow or clipped content; screenshot review caught and corrected one SVG edge label.
Files: docs/architecture/mcp-worker-execution-boundary.html (originally added under spec/, moved in 04b463a5c)
Impact: The artifact is now presentation-ready, keeps the architecture claims intact, and uses diagrams/charts that remain readable when the 16:9 canvas is uniformly scaled.

## 2026-08-17 20:55 – ENG-95262 wave 2: file gate, supervisor, wedge lab, and the SDK re-measurement
Context: Alex offered ts1-core-dev04 over SSH, which unblocked the three Windows-only gates. Wave 2 ran the relay re-measurement on the SDK clio actually ships plus stage 9 (interprocess file gate) and stage 2 (process supervisor), serialised on BindingsModule because two owners of that file in one wave overwrite each other.
Decision: own the child transport read loop through the SDK's PUBLIC api rather than hand-rolling JSON-RPC. `IClientTransport.ConnectAsync` -> `ITransport.MessageReader` (a ChannelReader<JsonRpcMessage>) delivers messages in pipe order; keep StdioClientTransport for spawn/framing/serialization and skip ONLY McpClient.CreateAsync, which is what installs the reordering dispatch layer. ~120 lines, 30/30 clean on 2.2.0.
Discovery: (1) Rule 12 SURVIVES re-measurement - notification reordering still reproduces on 2.2.0, so it is kept on current evidence rather than out of caution. (2) SAMPLING IS DEPRECATED in 2.2.0 (MCP9005, SEP-2577) - update-page/sync-pages semantic review still works (121/121) but rests on a feature the SDK may remove; successor is InputRequest/InputResponse + McpClient.ResolveInputRequestsAsync (OQ-6). (3) 2.2.0 clients probe `server/discover` BEFORE initialize, and a child answering that probe with a SUCCESS result of the wrong shape stalls the handshake for the full 5s DiscoverProbeTimeout and then HARD-FAILS with no fallback to initialize - answering -32601 falls back in 0.05s. Five seconds of dead time inside the parent's own budget. (4) MCP Tasks were REMOVED in 2.2.0, so the ADR's rejection of them needs no revisiting; `ping` is not served on protocol 2026-07-28, so a worker liveness probe must use tools/list. (5) The ITransport/StdioClientTransport seam is BYTE-IDENTICAL between 1.4.1 and 2.2.0 - stable, not a 2.2.0 novelty.
Windows hosts: ts1-core-dev04 (Server 2022, 4 cores) CANNOT install the MSVC toolchain - an ESET TLS filter re-signs HTTPS with `CN=ESET SSL Filter CA`, the chain does not validate, and the VS installer dies at `Failed to download the catalog` against vsblob.vsassets.io. Not a TLS-version issue (TLS 1.3 negotiated, SchUseStrongCrypto already 1). Anything there that fetches from Azure Edge with a validating client fails the same way. The AOT gate was closed on `runner` (A_KRAVCHUK2, Win 11, 32 cores) instead, which already carries link.exe + Windows SDK; it only needed a user-local .NET 10 SDK in %USERPROFILE%\.dotnet10. Three VS-installer dead ends worth not repeating: `modify` needs --productId AND --channelId (not just --installPath); the installed setup.exe REJECTS --wait with code 87; and without --wait it hands off to a service and returns immediately, so completion must be polled.
Files: clio/Command/McpServer/Tools/InterprocessFileGate.cs, clio/Command/PageBaselineGuard.cs, clio/Command/PageFileWriter.cs, clio/Common/BrowserSession/BrowserSessionCache.cs, clio/Common/McpWorker/*, clio.mcp.e2e/Support/Creatio/CreatioWedgeStubServer.cs, clio.mcp.e2e/McpWorkerWedgeE2ETests.cs, spec/adr/adr-mcp-worker-execution-boundary.md
Impact: +39 unit tests, all passing; failing-NAME set byte-identical to the pre-wave baseline (the verifier compared names out of trx files, not counts). AOT gate closed with the discriminating check - a native clio-ring.exe with NO managed dll beside it, which a bare exit code would not prove. Still open and NOT to be reported as covered: TC-E-901 (the two-process meta.json race) is McpE2E.Sandbox and needs a live stand, so stage 9's headline test has compile-only evidence.

## 2026-08-17 22:10 – ENG-95262 Stage 1 annotation wave finalised (gate emptied, inventory reconciled)
Context: Six annotators wrote [McpToolExecution] across all 189 [McpServerTool] declarations. The finaliser owns the two files they were forbidden to touch — the coverage fixture's NotYetClassifiedTools gate and Inventory 1 — and had to do the cross-group checks (starter/status and alias pairs are split across groups).
Decision: emptied the gate; re-expressed TC-U-106 instead of leaving it red. Its old mechanism proved "the toggle-blind view is not a blind spot" by relying on the feature-gated tools being UNCLASSIFIED, which structurally cannot hold once the gate is empty — so the test coupled a permanent property to Stage-1 scaffolding. Replacement uses a probe gate naming exactly the feature-gated names: over the toggle-blind catalog the routine reports each as "classified but still listed" (count == gated count), over the enabled catalog it reports the same names as STALE. Same NotBeEmpty teeth, same named example (get-mobile-page-conversion-guide), permanent mechanism. No assertion weakened.
Discovery: (1) The shipped Location split is 153 worker / 36 in-process, not the heuristic's 157/32 — six Location corrections one way (add-data-binding-row, remove-data-binding-row, get-tool-contract, new-test-project, new-integration-test-project) and one the other (new-ui-project is worker because UiProjectCreator.Create calls FindExistingPackage unconditionally, reaching SelectQueryHelper with the default Timeout.Infinite). §4's predicted weak spots are exactly where the corrections landed, so the heuristic is now marked superseded rather than merely caveated. (2) The progress cohort is 15, not 14: the Stage-0 census counted McpProgressHeartbeat callers and missed tools that call server.SendNotificationAsync directly (start-creatio, stop-creatio), and it wrongly included list-apps, which takes no RequestContext and has no channel to emit on. stop-creatio and stop-all-creatio legitimately DIFFER on this field — only the first passes a configureCommand that attaches the handler. (3) There are TWO method-level alias pairs, not one: clio-run-destructive declares AliasOf = clio-run (its own class doc calls it a deprecated alias), which was not a checked invariant before. (4) Two shared local artifacts have no McpToolSharedFileResource member — workspace data-binding files (create-data-binding vs the two now-in-process row tools) and .clio-migration/<schema>/manifest.json — deliberately left as `none` and handed to Stage 9 rather than inventing an enum value. (5) Nine of the 37 hint-unbounded tools landed in-process, so the cross-field invariant forces BudgetPolicy = none and Stage 7's parent kill will NOT bound them; install-toolkit / update-toolkit genuinely block on the network. That is a Stage 7 input, not a metadata error.
Files: clio.tests/Command/McpServer/McpToolExecutionMetadataCoverageTests.cs, spec/mcp-worker-execution-boundary/mcp-worker-execution-boundary-execution-metadata.md
Impact: McpToolExecutionMetadata fixture 20/20 with an empty gate, so all four gate rules plus the starter/status, alias, budget and cross-field routines now assert instead of excusing. Full unit suite 8983 passed / 20 failed / 39 skipped — the 20 failing NAMES are the pre-existing macOS set (clio.tests/Common/DbHub/DbHubTomlStoreTests 17, NetFrameworkHttpsConfiguratorTests 2, ProcessExecutorTests 1, AppPoolProfileCleanerTests 1); none is under Command/McpServer. Inventory 1 is now a record of shipped values (189 rows, 0 diffs against the source) rather than a proposal.
## 2026-08-18 14:20 – get-page-hierarchy: a broken read stops looking like an answer
Context: ENG-95262 story 13; a bare catch let get-page-hierarchy exit 0 with a chain anchored on the wrong package.
Decision: story 11's three-state idiom, one hop up — propagate QuerySysSchemaRow's classified error; the design-package
fallback survives ONLY for an answered rejection (InvalidOperationException, not NonJsonServiceResponseException).
Discovery: GetDesignPackageUId and GetParentSchemas are DIFFERENT endpoints, so a per-endpoint failure previously
produced a plausible-but-truncated chain with success:true. Also: get-page already returns `success ? 0 : 1`
(PageGetOptions.cs:283, Program.cs:665) — the story's exit-0 premise does not reproduce from source.
Files: clio/Command/GetPageHierarchyCommand.cs, clio.tests/Command/GetPageHierarchyCommandTests.cs,
clio.tests/Command/McpServer/PageHierarchyGetToolTests.cs, clio.mcp.e2e/PageHierarchyGetToolE2ETests.cs
Impact: the same split applies verbatim to PageGetOptions.ResolveDesignPackageUId and
GetClassicPageSourcesCommand.ResolveHierarchyBaseToTop, which still carry the defect.

## 2026-08-18 15:05 – ENG-95262 reviewer-body findings folded into the design docs
Context: both CHANGES_REQUESTED review bodies on PR #1080 carried findings that never became resolvable
threads, so nobody had answered them; that is why the PR still reads CHANGES_REQUESTED with 14/14 inline
threads resolved.
Decision: threat model gained T-9 (spawn exhaustion), T-10 (executable substitution) and R-10..R-12 plus a
per-stage applicability table; ADR gained 2.5 (fast-cohort overhead ratio), 3.2a/3.2b (the SDK send lock and
rule 12's real enforceability), 3.4 (why the stderr drain lives in the dispatcher), a corrected 3.3 terminal
vocabulary and an OQ renumber; the stage-5 deferral re-homed session-key normalisation into story 7 as AC-00
rather than a depends_on edge pointing at deferred work.
Discovery: the SDK's send lock guarantees a COMPLETED send, not an ATOMIC one -- cancellation between the
payload write and the newline leaves a dangling line, which wedges a sticky worker. The shipped stage-event
contract has no `cancelled` outcome, so a cancelled deploy must resolve as indeterminate. And the child's
STDOUT is unbounded: the SDK reads a line with no maximum length, so only the stderr tail is bounded today.
Files: spec/adr/adr-mcp-worker-execution-boundary.md,
spec/mcp-worker-execution-boundary/mcp-worker-execution-boundary-credential-threat-model.md,
spec/stories/story-mcp-worker-execution-boundary-6.md, -7.md, spec/prd/prd-mcp-worker-execution-boundary.md
Impact: the two review bodies are now answerable point by point, with citations anchored on symbols rather
than line numbers -- five files in this feature moved by tens to hundreds of lines during one session.

## 2026-08-18 16:30 – Relay: probe bound, cancel notification, mid-frame retirement, IL guard
Context: ENG-95262 stories 15, 14, 18 and 16, done in one pass because 14 and 18 edit the same try/catch.
Decision: the liveness probe gets its own 2 s bound, justified against ADR 2.4's measured 2.763 s spawn rather
than chosen; a cancelled-but-already-sent request tells the worker through notifications/cancelled on the child
leg; a send that did NOT complete retires the session instead of leaving it writable.
Discovery: SDK 2.2.0's StreamClientSessionTransport DOES serialise writes behind a SemaphoreSlim, so no
relay-side send gate is needed -- but it passes the caller's token to the payload write, the newline write and
the flush SEPARATELY, so cancellation between them strands an unterminated line and the next writer's JSON is
appended to it. Measured by decoding the shipped IL, not read from documentation. The same probe corrected the
DTO member name (RequestId, not Id).
Also: a signature-only guard is not as blind as the story assumed -- a local crossing an await is hoisted into
an async state-machine field and IS visible. The shapes that escape are the non-surviving await and the
discarded call, which is why the guard now reads method bodies.
Files: clio/Command/McpServer/Relay/{WorkerMcpRelay,IWorkerMcpRelay,WorkerChildTransportOwner}.cs,
clio.tests/Command/McpServer/WorkerMcpRelayTests.cs
Impact: the reuse rule and the SDK write guarantee are now written where the code is, and the McpClient guard
can actually fail -- proven by inserting one line, watching it go red naming the member, and removing it.

## 2026-08-18 17:40 – Supervisor: the queue wait gets a bound, and the http gate stops being silent
Context: an adversarial pass over stage 6 found that routing now runs BEFORE the read-deadline wrapper, so a
cohort call waiting for a concurrency slot had no clio-side bound at all -- on master the same call was bounded
at 120 s. The observable signature was the wedge's own: nothing returned, zero requests to Creatio.
Decision: bound the queue wait (60 s, overridable) and fail with a NAMED exception carrying the wait endured,
the bound, the cap and the queue depth -- not TimeoutException and not OperationCanceledException, either of
which a caller would misread. The budget clock still starts at spawn; queue wait and budget stay separate
numbers because a caller needs to know which one it hit.
Discovery: the child environment allowlist claimed to carry "every spelling the host may have used" and
dropped HTTP_PROXY/HTTPS_PROXY/NO_PROXY in both cases -- so behind a mandated inspecting proxy the child either
cannot reach Creatio or bypasses the policy, and both present as "the environment is broken". It still drops
CLIO_MCP_HEARTBEAT_INTERVAL_SECONDS, which is story 20's leading hypothesis and is deliberately left alone
until the cause there is proven.
Also: on mcp-http every cohort tool runs in the host process with the wedge fully intact and returns an
ordinary successful result. That is correct while story 5 is deferred, but the silence was not; the host now
says so once per session.
Files: clio/Common/McpWorker/{WorkerProcessSupervisor,IWorkerProcessSupervisor}.cs,
clio/Command/McpServer/{IMcpWorkerPathGate,IMcpExecutionRouter}.cs,
clio.tests/Command/McpServer/{WorkerProcessSupervisorTests,McpExecutionRouterTests}.cs
Impact: the cohort's seven names are now pinned as literals against what story 6 promises, so changing the
cohort has to change a test on purpose rather than silently agreeing with itself.

## 2026-08-18 15:20 – Story 19: three tools serialized every tenant on the shared fallback lock
Context: story 19 of mcp-worker-execution-boundary — get-related-page-addon held
McpToolExecutionLock.SharedFallbackKey across a Creatio round-trip, so every other environment queued
behind one tenant's read. Second instance of the story-12 defect class.
Decision: switched the tool (and, from the AC-03 sweep, create-related-page-addon plus all eight
business-rule tools) to the OPTIONS-AWARE BaseTool.ExecuteWithCleanLog overload.
Discovery: two of the eight business-rule tools ALREADY passed options to the options-aware overload and
were still on the shared key — BaseTool.ResolveTenantLockKey returns SharedFallbackKey whenever the base
has no IToolCommandResolver, and all eight built their base as `BaseTool<...>(null, logger)`. A correct-
looking call site silently degrades; the resolver has to reach the BASE constructor, not just the field.
Second discovery: a lock-key test is worthless unless GetTenantKey is stubbed — NSubstitute returns "" for
an unstubbed string and Normalize turns that into SharedFallbackKey, so broken and fixed look identical.
Files: clio/Command/McpServer/Tools/{GetRelatedPageAddonTool,CreateRelatedPageAddonTool,BusinessRuleTool}.cs,
clio.tests/Command/McpServer/{GetRelatedPageAddonToolTests,CreateRelatedPageAddonToolTests,BusinessRuleToolLockTests}.cs
Impact: the reliable detector for this defect class is `ExecuteWithCleanLog(()` (environment-less overload)
PLUS a check that every BaseTool subclass doing per-tenant work threads commandResolver to its base.

## 2026-08-18 — ENG-95262 story 20: worker progress streaming, and the allowlist audit
Context: `ApplicationTool_Should_Stream_Progress_For_LongRunning_Call` observed 0 progress notifications on
the worker-execution branch and passed on master.
Decision: two-arm harness to separate "the child never beat" from "the beat was lost". Arm A (host tuned,
shipped allowlist) → 0 notifications AND the child's environment had no cadence at all. Arm B (child beats
regardless) → all 3 beats reached the client in wire order with the caller's exact token. Verdict: emission
defect, not a relay defect. Fix = `CLIO_MCP_HEARTBEAT_INTERVAL_SECONDS` on
`WorkerProcessSupervisor.DefaultInheritedEnvironmentVariableAllowlist`, plus a full audit of every `CLIO_*`
variable with inclusion/exclusion reasons written into the member's remarks.
Discovery: (1) a worker's environment is CLEARED and rebuilt from that allowlist, and every deadline/cadence
default in clio is `static readonly` captured at TYPE LOAD — so anything not present at spawn can never be
applied later; (2) `McpWorkerCallDispatcherTests`' remark that the happy path "requires a live
WorkerRelaySession … that no substitute can produce" was wrong and had kept this whole class of regression
out of the unit suite: it needs no substitute, only an anonymous pipe pair and a scripted child;
(3) `TERM`'s ABSENCE is the correct worker behaviour — inheriting it would re-enable ANSI colour in the one
process whose stdout is the MCP protocol stream.
Files: clio/Common/McpWorker/WorkerProcessSupervisor.cs,
clio.tests/Command/McpServer/WorkerProgressStreamingTests.cs,
clio.tests/Command/McpServer/WorkerProcessSupervisorTests.cs,
clio.tests/Command/McpServer/McpWorkerCallDispatcherTests.cs,
clio.mcp.e2e/ApplicationSectionToolE2ETests.cs
Impact: the worker happy path (handshake → tools/call → notifications → result) is now unit-testable in one
process; reuse `ScriptedWorkerChild` for stage-event / sampling regressions instead of reaching for e2e.

## 2026-08-18 16:20 – Closing b71f6ea0d's honest gap: the four unverified assertions
Context: b71f6ea0d shipped with an explicit self-declared gap — its author never filed a
red-before report, so two cohort-membership pins and the once-per-session HTTP notice were
"treat as open until someone mutates an input and checks". This is that check.
Decision: break each input at its own seam and require the specific test to go red, rather
than trusting a green suite.
Discovery: all four are non-vacuous, on exactly the scenarios the commit message named as
previously undetectable.
  - Swapping get-schema for another worker-classified tool in McpWorkerCohort.StageSixNames —
    the scenario the commit says used to stay green — turns BOTH membership pins red:
    ShippedCohort_ShouldBeExactlyTheSevenNamesStorySixPromises and
    HandleAsync_ShouldReturnConfirmationRequiredWithoutRouting_WhenACohortToolIsWriteCapable.
    The second is the more interesting of the pair: it means the get-schema write-capability
    asymmetry is pinned to get-schema specifically, not to "some cohort member".
  - Replacing the Interlocked.Exchange gate with an unconditional WriteWarning turns
    Resolve_ShouldStateTheInactiveBoundaryExactlyOnce_WhenHostTransportIsHttp red — so the
    test measures once-ness, not merely that a notice appears.
  - Removing the `transport != Http` restriction turns
    Resolve_ShouldStateNothing_WhenHostIsStdioOrItsTransportWasNeverDeclared red — so the
    fail-closed Unknown case is genuinely covered, not incidentally passing.
All mutations reverted; McpExecutionRouterTests 29/29 green on the restored tree.
Files: clio/Command/McpServer/McpWorkerCohort.cs, clio/Command/McpServer/IMcpWorkerPathGate.cs
  (both mutated and restored; no net change), clio.tests/Command/McpServer/McpExecutionRouterTests.cs (read only)
Impact: the gap b71f6ea0d flagged is closed with evidence. Method worth reusing — mutate at the
seam the assertion names, not at a random line, and require THAT test to fail rather than any test.

## 2026-08-18 — story 21: truncation upstream of redaction can un-redact a secret
Context: story-mcp-worker-execution-boundary-21 — `WorkerStandardErrorDrain` keeps the LAST N chars of a
worker's stderr and trims from the front at an arbitrary offset, so a cut inside `password=` leaves
`word=<secret>`, which `SensitiveErrorTextRedactor.CredentialPairRegex` cannot match (it requires the KEY).
The value reached the client verbatim on `worker-stderr` of the failure envelope.
Decision: drop the leading PARTIAL line of a trimmed tail, unconditionally, whenever anything was trimmed —
the cheapest cut that no redaction pattern can straddle. Keying the drop on the redactor's own pattern list
would duplicate it into the drain; remembering whether the byte before the cut was a line break would add
pump state to recover, on the rare aligned cut, one line we are content to pay. When no complete line
survives (one unbroken line), the tail is withheld behind `StandardErrorNoCompleteLineNotice` rather than
emptied — an empty string removes `worker-stderr`, the truncation marker AND the caveat sentence together
and reads as "the worker said nothing". Applied in `Tail()` (snapshot time), not in the pump: `Tail()` runs
on paths before `StopAsync`, so the buffer may still be growing, and the trim-after-every-append invariant
makes a snapshot-time drop no weaker.
Discovery (generalises beyond this bug): every redaction pattern recognises a secret by CONTEXT around it,
so ANY transformation between capture and redaction can un-redact. Two directions — TAIL truncation orphans
a value from its key (this bug); HEAD truncation (`value[..N]`, used by ODataCreateTool / ExecuteEsqTool /
ODataReadTool) can bisect a self-identifying shape (a JWT cut below three segments stops matching), so it is
safer, not safe. The rule: REDACT FIRST, then transform (`ServiceResponseJsonGuard.BuildResponsePreview` is
the reference); where the order cannot be inverted because bounding is liveness, cut only on a boundary the
patterns cannot straddle. Recorded in the credential threat model under T-6/R-7.
Also: the RED observation matters more than the test. The pre-existing TC-U-505 fixture padded its chunks so
the cut landed in filler — it passed throughout and hid the hole. A fixture for this class must ASSERT its
own precondition (that the bound really cuts inside the key, and that the redactor alone leaves the marker
intact) or it silently re-pads itself the next time a constant moves.
Files: clio/Command/McpServer/Relay/McpWorkerCallDispatcher.cs,
  clio.tests/Command/McpServer/McpWorkerCallDispatcherTests.cs,
  spec/mcp-worker-execution-boundary/mcp-worker-execution-boundary-credential-threat-model.md
Impact: any future bounded/truncated copy of untrusted text (Stage 7 sticky pool, Stage 8 deploy child,
R-11's stdout bound) has the same exposure and now has a named class and a stated rule to check against.

## 2026-08-18 20:10 – Stage 8: the terminal-stage protocol for deploy-creatio / uninstall-creatio
Context: ENG-95262 story 8. The deploy family declared `BudgetPolicy = TerminalStage` since Stage 1 but was
absent from `McpWorkerCohort`, because the dispatcher kills an ordinary worker at its budget unconditionally
and a deploy killed at a stopwatch can leave a half-installed environment.
Decision: implemented ADR §3.3 on the EXISTING stage-event stream — no second IPC path. A read-loop tap
(`WorkerRelayOptions.NotificationTap`, a delegate rather than an interface so `BindingsModule`'s assembly
interface scan cannot auto-register per-call state) feeds `TerminalStageWatch`, which detects
`_meta.clioStageEvent.eventType == "run-completed"` on the run's ROOT runId. Two bounds, neither an
operation timer: a stage-event SILENCE bound (300 s, `CLIO_MCP_WORKER_STAGE_SILENCE_SECONDS`) that every
stage event restarts, and a 30 s post-terminal exit grace. All of it in a new partial file
(`McpWorkerCallDispatcher.TerminalStage.cs`) so the contested stderr-drain file took only four small edits.
Only after that did the two names go into the cohort (new `StageEightNames` + `ShippedNames`; `StageSixNames`
left intact so each story's promise stays independently pinnable).
Discovery (three, each of which would have shipped a silent defect):
 1. The ADR's original terminal vocabulary (`Completed`/`Failed`/`Cancelled`) DOES NOT EXIST. The shipped
    contract is `manifest`/`stage`/`run-completed` + `success`/`failure`/`success-with-warnings`. Coding the
    old wording gives a condition that never fires, so every healthy deploy would time out on silence and
    report indeterminate — a defect that reads as an environment problem. There is no `cancelled` outcome at
    all, so a cancelled run resolves through the indeterminate path by construction.
 2. A caller with no progress token makes the child emit NOTHING (`StageEventProgressForwarder` is inert),
    which a silence-bounded protocol would call a lost child. Fixed by injecting a synthetic token on the
    child leg and CONSUMING that traffic at the relay — the one deliberate exception to rule 1. Verified the
    token survives the live vector: `ClioRunTool.DispatchAsync` sets `childParams.Meta = originalParams.Meta`,
    so a token on the OUTER clio-run params reaches the inner deploy tool inside the child.
 3. `IsError` alone is not enough for the consumer. ClioRing's `DescribeUnstreamedFailure` reads the payload,
    so the indeterminate result carries `IsError` + `success:false` + non-empty `error` + additive
    `outcome:"indeterminate"`. `BudgetExpiredErrorClass` must never be reused here: its shipped guidance says
    the call is safe to retry.
Testing note: the three assertions that actually discriminate are spawn count == 1 (the ONLY thing that can
see an automatic retry), kill ordinal position (error composed and logged BEFORE the kill, or the last stage
is lost to a closed pipe), and the fixture child's emit log stopping at the kill — which needs the
substituted supervisor's `KillContained` to really close the child's pipe, or all three go vacuous. There is
NO Creatio backend counter for these two tools: they are local-only, have no `IApplicationClient`, and deploy
CREATES the instance. Mutation-checked three ways (kill-before-report, branch disabled, tap always forwards);
each was caught by exactly the intended test.
Files: clio/Command/McpServer/Relay/McpWorkerCallDispatcher.TerminalStage.cs,
  clio/Command/McpServer/Relay/TerminalStageWatch.cs, clio/Command/McpServer/Relay/WorkerMcpRelay.cs,
  clio/Command/McpServer/Relay/IWorkerMcpRelay.cs, clio/Command/McpServer/McpWorkerCohort.cs,
  clio.tests/Command/McpServer/WorkerTerminalStageProtocolTests.cs,
  clio.tests/Command/McpServer/McpExecutionRouterTests.cs, clio.mcp.e2e/DeployTerminalStageE2ETests.cs,
  clio/docs/commands/deploy-creatio.md, clio/docs/commands/uninstall-creatio.md
Impact: Stage 10's cohort expansion inherits a working non-stopwatch bound and, more importantly, the shape
of the answer for any operation whose outcome the parent cannot establish — name the last stage, mark the
environment, and never retry.

## 2026-08-18 – ENG-95262 stage 7 AC-00: session-key normalisation (one target, one key)
Context: stage 7 moves tenant-keyed registries to the parent MCP process. `ToolCommandResolver.BuildCacheKey`
built its identity as `options.Environment ?? "default"` + `"|"` + `settings.Uri`, so ONE target produced TWO
keys (`myenv|http://x` by name, `default|http://x` by explicit uri). Moving the registries on a split key makes
`compile-status` answer "no such operation" for a running compile, and it is not repairable afterwards.
Decision: the identity is now the NORMALISED TARGET and nothing else, folded by a new
`ISessionTargetNormalizer` implementing the BINDING T-5 component table (threat model
`mcp-worker-execution-boundary-credential-threat-model.md`). Conservative by construction: `http`/`https` and
hostname/IP stay distinct; userinfo, query, fragment and non-canonical IPv4 literals are REJECTED (explicit
`EnvironmentResolutionException`, never a looser key). `BuildTargetIdentity` is the seam where R-5's principal
and credential-fingerprint components get composed in when mcp-http is revived — the normaliser itself stays a
pure target→target fold.
Discovery (load-bearing, verified on net10.0 2026-08-18): `new Uri("http://0177.0.0.1/").Host` AND
`IPAddress.Parse("0177.0.0.1").ToString()` BOTH return `127.0.0.1` — both silently perform the exact octal fold
T-5 rejects. The host must therefore be read from the RAW authority text, never through `Uri.Host`. Also
`IdnMapping{UseStd3AsciiRules=true}.GetAscii("a_b.com")` THROWS `ArgumentException`, so IDNA is applied only to
non-ASCII hosts (ASCII hosts take a plain `ToLowerInvariant` fast path) — underscore hosts are common on dev stands.
Files: clio/Command/McpServer/Tools/SessionTargetNormalizer.cs, clio/Command/McpServer/Tools/ToolCommandResolver.cs,
clio/BindingsModule.cs, clio.tests/Command/McpServer/SessionTargetNormalizerTests.cs,
clio.tests/Command/McpServer/ToolCommandResolverTargetConvergenceTests.cs
Impact: `BuildCacheKey` is now an INSTANCE method (six-arg ctor), which is why several McpServer fixtures gained
a `new SessionTargetNormalizer()` argument. Any future key component belongs in `BuildTargetIdentity`, not in a
second static.

## 2026-08-18 19:40 – Admission capacity: two pools, a reach seam, and an operator cap (ENG-95262 Stage 7 foundation)
Context: ADR §3.2c / story-7 BLOCKER — a sticky worker holds an admission slot for its whole operation, so a `compile-status` poll routed through the spawn path waits for a slot HELD BY THE WORKER IT IS REACHING (hold-and-wait, not starvation). Also closes threat-model T-9 gap G-1.
Decision: (1) `IWorkerChannel` (talk-only) split out of `IWorkerLease`, reached through a deliberately narrow `IWorkerReach.ReachExisting` that takes no slot and returns a non-owning wrapper that is not castable back to a lease; (2) the total cap is PARTITIONED into `_stickyPool` + `_perCallPool` (never additive — an extra pool would exceed the ADR §2.4 measured ceiling and falsify `ConcurrencyCap`'s published meaning); (3) sticky cap = `total / 2`, one derivation, strictly below the total for every input, per-call remainder ≥ sticky ≥ 0; (4) sticky admission never queues — `WorkerStickyCapacityExceededException` carries the limit and names `CLIO_MCP_WORKER_CONCURRENCY`.
Discovery: a total of 1 derives a sticky cap of 0, so a single-slot host runs no long operation; flooring the TOTAL at 2 instead would double admitted concurrency on every single-core host and break AC-06, so the arithmetic is kept and the operator override is the remedy. `SemaphoreSlim` rejects a maximum of 0, so `WorkerSlotPool` builds a never-filled one-slot semaphore and short-circuits on `Cap`. The discriminating tests are the SATURATED ones — an idle-host poll passes under the deadlocking implementation too; six deliberate mis-implementations were each watched red before the correct one was restored.
Files: clio/Common/McpWorker/IWorkerProcessSupervisor.cs, clio/Common/McpWorker/WorkerProcessSupervisor.cs, clio.tests/Command/McpServer/WorkerAdmissionCapacityTests.cs, clio.tests/Command/McpServer/WorkerProcessSupervisorTests.cs
Impact: Stage 7 sticky supervision can be built on a capacity model that cannot deadlock; the dispatcher still needs `Lifetime = Sticky` on the long-operation spawn, `IWorkerReach` injection on the poll path, and a `BindingsModule` forwarding registration to the SAME supervisor singleton.

## 2026-08-18 19:40 – ENG-95262 stage 7: sticky supervision, parent-owned configuration-build reservation
Context: story 7's remaining phase (AC-01…AC-05) on top of the delivered AC-00 session-key normalisation and admission-capacity foundation.
Decision: the dispatcher grew a sticky branch keyed on declared metadata (`Lifetime=Sticky` + a named `OperationFamily`). A STARTER always creates its own worker; only a POLLER reaches an existing one, and it does so through `IStickyWorkerPoll`, which is injected with `IWorkerReach` and therefore has no member that can acquire an admission slot. `IWorkerReach` is registered in `BindingsModule` as a FORWARDER to the supervisor singleton. A new 7th, non-routing attribute field `StartsOperation` (the `AliasOf` precedent) separates starter from poller; deriving it from the six routing fields or from the `ReadOnly` hint is not possible/allowed (ADR rule 7).
Discovery (the one that changed the design): reaping a sticky worker the INSTANT its private completion signal arrives regresses shipped behaviour. On stdio the compile/restart operation registries are DI singletons INSIDE the worker, so an immediately-reaped worker takes the operation record with it and the very status poll the caller was told to make answers "no such operation" for an operation that just finished — `clio.mcp.e2e/CompileCreatioToolE2ETests.CompileCreatio_Should_RecordFailedOperation_QueryableViaCompileStatus` is the existing test that would have caught it. Resolution: the signal releases the shared reservation AT ONCE (a finished build must stop denying its environment) but only shortens the worker's lifetime to a linger window (default 5 min); the sweep at the head of the next sticky dispatch returns the slot. `ReapExpiredAsync` is AWAITED on the dispatch path — a background release races the very spawn it frees capacity for.
Discovery 2: the exclusion and the sticky key are different cardinalities and both are needed. `IToolCommandResolver.GetTargetKey` was added for the exclusion (normalised target only, no credential fingerprint); `GetTenantKey` stays the sticky-worker/status key. A dispatcher-level test with two tenant keys mapping to ONE target key is what catches a principal creeping back into the exclusion key.
Files: clio/Command/McpServer/Relay/{StickyWorkerRegistry,StickyWorkerPoll,SharedResourceReservation,WorkerOperationSignalContract,WorkerStandardErrorDrain,McpWorkerCallDispatcher.Sticky}.cs, clio/Command/McpServer/Tools/WorkerOperationCompletionSignal.cs, clio/Command/McpServer/McpWorkerCohort.cs, clio/BindingsModule.cs, clio.tests/Command/McpServer/StickyWorkerSupervisionTests.cs
Impact: the four long-running families joined the shipped cohort. Six discriminating mutations were watched failing first; the drain was promoted out of the dispatcher (ADR §3.4's second lease consumer) WITHOUT an `I<Name>` interface, which is what keeps CLIO001 from demanding a `*Factory`.
Follow-up found in review and fixed the same day: (1) `StickyCallBudgetHeadroom` was declared AFTER the budget that consumes it — static field initialisers run in TEXTUAL order, so the headroom was `TimeSpan.Zero` and the parent bound silently equalled the child's response deadline exactly. Caught by the new derivation test, not by any behaviour test. (2) The linger holds a sticky slot, and `StickyConcurrencyCap = total / 2` is 1 on a two-core agent — so a finished compile would refuse every long operation for the whole window. `StickyWorkerPoll` now reaps a completed entry as soon as the poll it was lingering for has been answered; the window is the backstop for a caller that never polls. (3) `restart-by-credentials` names no environment, so the parent now reads `url` as the target argument — without it every credentials-started restart on every stand shared one unresolved sticky key.

## 2026-08-19 01:15 – ENG-95262: the e2e delta closed, and the method that closed it
Context: story 20's 46/470/98 run was uninterpretable and blocked the merge.
Decision: fix the CAUSE of the uninterpretability before re-running, not the symptoms.
Discovery, in the order it mattered:
  - "Run it in isolation and look at the file" answered NO on its own terms — the test
    passed and the file was intact — because the mechanism was TARGETING, not tearing.
    An isolated run leaves a whole file and still proves the defect. The step that
    actually decided it was asking WHERE the writes went (watcher over the suite home).
  - "clio settings bootstrap is broken" does not mean a damaged file. CanExecuteEnvTools
    is just "the ActiveEnvironmentKey resolves"; two runs on byte-identical files
    differing in that one key reproduce the message. A conclusion had been drawn from a
    message that does not support it.
  - Fixing the test's targeting is what made its SUBJECT visible: with a real private
    home it failed again and exposed SettingsRepository.CommitSettingsFile publishing
    with no retry — the same Windows MoveFileEx exposure as the browser-session cache.
    Both of story 20's original hypotheses held a piece, and the order was the point.
  - A Windows host settles what macOS cannot. rename(2) ignores open readers, so a green
    macOS probe is CONSISTENT WITH A COMPLETELY BROKEN PUBLISH. The control arm that
    reproduces the blindness is worth as much as the arm that reproduces the bug.
  - Measurement corrected the fix, not just confirmed it: a 12-attempt bound was observed
    needing 13, 15 and 16. A substituted-move unit test can never see that, because the
    substitute decides how many attempts occur.
Method worth reusing: mutate at the seam the assertion NAMES and require THAT test to go
red. It caught a guard that could not fail (nested runId), a fixture that would have
flaked CI on the happy path, and — twice — a fix of mine that was wrong (Redact(null)
returns "" so a ?? fallback never fires; a sed whose escaping silently did nothing, so
the "mutation" run proved nothing until redone in python).
Files: clio/Environment/ConfigurationOptions.cs, clio/Common/FileSystem.cs,
  clio.mcp.e2e/Support/Configuration/{IsolatedClioHome,TemporaryClioSettingsOverride}.cs,
  clio/Common/McpWorker/**, clio/Command/McpServer/Relay/**, spec/**
Impact: e2e went 46/470/98 -> 3/496/9; the remaining three are named and tracked, not
counted. Nine review findings fixed, three deferred with reasons rather than silently.

## 2026-08-19 – Sticky start is single-flight per key; completion signal and reap are entry-scoped
Context: External review found a P1 in the sticky dispatcher (ENG-95262 story 7) — two starters of one family racing for one key both spawned, the TryRegister loser still ran its call and was released in the `finally`, killing a worker whose in-progress response left a readiness wait continuing inside it. Same key-vs-entry confusion in the relay tap (`TapCompletionSignal(notification, key)`), so a loser's completion signal marked the WINNER's entry complete and released its reservation early.
Decision: (1) a zero-wait per-key start gate (`HashSet<StickyWorkerKey>` + lock in `McpWorkerCallDispatcher.Sticky.cs`) held across spawn/handshake/registration only, dropped right after `TryRegister`; (2) inside it, refuse a second starter with the new `clio-long-operation-in-progress` error class instead of spawning a doomed worker; (3) `SignalCompleted(key, entry, linger)` and `ReapAsync(key, entry)` are now ReferenceEquals-scoped to the entry.
Discovery: order matters — the configuration-build RESERVATION must be checked BEFORE the sticky-key pre-check. The reservation is keyed by target (excludes across principals AND families) and its envelope is the more specific statement; putting the key check first silently downgraded `clio-configuration-build-in-progress` to the new class for compile/install-process-builder, which an existing test caught. A COMPLETED worker is still `IsLive` for its whole linger window, so the pre-check must supersede (reap) it rather than refuse — otherwise the linger becomes minutes of false "already in progress".
Files: clio/Command/McpServer/Relay/McpWorkerCallDispatcher.Sticky.cs, clio/Command/McpServer/Relay/StickyWorkerRegistry.cs, clio/Command/McpServer/Relay/StickyWorkerPoll.cs, clio.tests/Command/McpServer/StickyWorkerSupervisionTests.cs
Impact: the only family that could reach the doubled-spawn path was `restart-*` (no shared-resource reservation). The fixture now supports a scripted handshake delay (`CreateFixture(handshakeDelay:)`), which is what makes the spawn-to-register race reproducible in a test at all.

## 2026-08-19 12:20 – Windows worker inherits exactly three handles (STARTUPINFOEX handle list)
Context: External P1 review of ENG-95262 — `CreateProcessW(bInheritHandles: true)` in the Windows containment path handed every inheritable handle in the parent to each child, so a concurrently launched worker retained a SIBLING's stdout/stderr pipe WRITE end. The relay ends its read loop on EOF and only then fails pending calls, so the sibling's reader never saw EOF and the parent waited on a dead worker until an unrelated child exited — a hang attributed to the wrong process.
Decision: Keep `bInheritHandles: true` (required for the mechanism) and narrow inheritance with `STARTUPINFOEX` + `PROC_THREAD_ATTRIBUTE_HANDLE_LIST` (0x00020002) naming exactly the three pipe client handles, plus `EXTENDED_STARTUPINFO_PRESENT` (0x00080000) in the creation flags. Attribute-list setup failure FAILS the spawn — no silent fallback, because falling back reinstates the same hang on the path nobody watches. Job object, CREATE_SUSPENDED → AssignProcessToJobObject → ResumeThread, and kill-on-close are untouched (ADR §2.4 measured those).
Discovery: (1) With `EXTENDED_STARTUPINFO_PRESENT` the inner `STARTUPINFO.cb` must be `sizeof(STARTUPINFOEX)`; the plain size fails every spawn with ERROR_INVALID_PARAMETER (87). (2) `InitializeProcThreadAttributeList`'s sizing call DELIBERATELY returns FALSE — the reported size, not the bool, is the success signal. (3) `UpdateProcThreadAttribute` stores a POINTER to the handle array, so it is GC-pinned until after `DeleteProcThreadAttributeList`; teardown is Delete-then-Free, and Delete must be skipped when initialization never succeeded. (4) A handle named in STARTUPINFO but missing from the list, or a non-inheritable handle in the list, also fails with 87.
Files: clio/Common/McpWorker/WindowsJobObjectContainment.cs (new `WindowsWorkerStartup`, `ProcThreadAttributeList`, `IProcThreadAttributeListNative` + kernel32 impl, `StartupInformationEx`), clio.tests/Command/McpServer/WindowsWorkerHandleInheritanceTests.cs
Impact: Composition and the whole unmanaged lifecycle are now asserted cross-platform via a substituted native layer; only "the kernel honours the list" needs Windows, and the one test that needs it reports a visible ignore off Windows instead of passing silently.

## 2026-08-19 – In-stage liveness refresh + clio-run sticky key unwrap (ENG-95262)
Context: two P1 review defects on the worker execution boundary — the 300 s stage-event silence timer killed healthy long stages, and sticky/reservation keys were derived from the clio-run wrapper instead of the inner call.
Decision: StageEventEmitter re-emits an ordinary `stage`/`running` event for the CURRENT stage every 30 s while the stage's action runs (interval/silence-bound relationship enforced by a ratio test, since the child cannot read CLIO_MCP_WORKER_STAGE_SILENCE_SECONDS — it is outside the worker inherit allowlist). Emit() is now locked so the refresh thread cannot duplicate a `sequence`. Sticky key derivation unwraps the clio-run envelope with the same rule as ClioRunExecutor.RecoverWrappedCall (inner `args` wins, else wrapper-minus-`command`).
Discovery: a single-environment clio-run poll test PASSES on the broken code — both calls collide on the same unresolved key — so the discriminating test needs TWO environments; with one, the collision is invisible. RunStage emits nothing between `running` and the stage's terminal status, so any silence-based bound is a stage-duration bound in disguise.
Files: clio/Command/McpServer/Progress/StageEventEmitter.cs, clio/Command/McpServer/Relay/McpWorkerCallDispatcher.Sticky.cs, clio.tests/Command/McpServer/StageEventLivenessHeartbeatTests.cs, clio.tests/Command/McpServer/WorkerTerminalStageProtocolTests.cs, clio.tests/Command/McpServer/StickyWorkerSupervisionTests.cs, clio/docs/commands/deploy-creatio.md, clio/docs/commands/uninstall-creatio.md
Impact: stage-event silence now means "the worker stopped talking"; any future bound tied to stage events must keep the refresh-to-bound ratio test green.

## 2026-08-19 – A cancelled sticky poll decides the session; the stage emitter stops holding a lock across its sink
Context: two P1 review defects on ENG-95262. (1) `StickyWorkerPoll`'s cancelled path rethrew and left the worker registered, so ADR §3.2a ("a session whose send did not complete is retired, never reused") was unenforced and `IsLive` judged process lifetime only. (2) The round-2 bounded join in `StopLivenessRefresh` bought nothing, because `Emit` held `_emitLock` across the sink: a beat blocked on pipe backpressure still owned that lock when the stage's terminal transition and `run-completed` tried to take it, so the run never reported a terminal stage and the parent classified a healthy deploy as indeterminate.
Decision: (1) `WorkerRelaySession.IsClosed` became public `IsRetired`; the poll reads it on the cancelled path with ZERO I/O — retired ⇒ reap the ENTRY (which also ends the operation, since the lease kills the process), whole ⇒ keep the worker and set `RequiresLivenessProof`, which the NEXT poll discharges with a bounded `ProbeLivenessAsync` before reusing the session. `StickyWorkerEntry.IsLive` now includes `!Session.IsRetired`. (2) `Emit` stamps the sequence and enqueues under the lock, then delivers OUTSIDE it through a single-drainer loop (`_delivering` flag, queue guarded by the same lock); a terminal event that could not drain itself waits bounded (2 s, `Monitor.Wait`/`PulseAll`) for the running drainer.
Discovery: the relay ALREADY distinguishes an interrupted send — `RequestAsync` sets the closure synchronously in its catch (`!sent` branch) BEFORE it rethrows — so the poll needs no probe to tell a half-frame from a clean cancel. A dedicated consumer thread was rejected: the whole existing suite (and `StageEventEmitterTests`) observes SYNCHRONOUS delivery — `events.Count` right after `RunStage` — so the opportunistic in-line drain is what keeps that contract while removing the lock from the sink call. The literal review-requested test ("terminal delivered WHILE a beat is stuck") is unsatisfiable under any order-preserving design; the achievable discriminator is that terminal emission RETURNS while the beat is still in the sink.
Files: clio/Command/McpServer/Relay/StickyWorkerPoll.cs, clio/Command/McpServer/Relay/StickyWorkerRegistry.cs, clio/Command/McpServer/Relay/WorkerMcpRelay.cs, clio/Command/McpServer/Progress/StageEventEmitter.cs, clio.tests/Command/McpServer/StickyWorkerSupervisionTests.cs, clio.tests/Command/McpServer/StageEventLivenessHeartbeatTests.cs
Impact: a hermetic `ITransport` that blocks `tools/call` on the caller's token reproduces the "send did not complete" state without pipes — the byte-level half frame is already pinned by `WorkerMcpRelayTests.RequestAsync_ShouldRetireTheSession_WhenASendWasCancelledMidFrame`. Anything added to the emitter's beat path that touches `_emitted`/`_manifest`/`_completed` still needs `_emitLock`.

## 2026-08-19 – The sticky lifetime bound now fires at the deadline instead of on the next dispatch (ENG-95262)
Context: one P1 review defect. `IStickyWorkerRegistry.ReapExpiredAsync` had exactly ONE caller — the head of a sticky dispatch — so expiry only happened if MORE sticky traffic arrived. On an idle host a completed worker (shortened to its linger) or a hung one outlived both the linger and the hard bound indefinitely, keeping its process, its authenticated Creatio session and its admission slot; where the sticky ceiling is one or two, one such worker denied every later long operation. The "bound" was a hint.
Decision: the REGISTRY owns the scheduling — a single `ITimer` from an injected `TimeProvider` (optional ctor param, `TimeProvider.System` by default; already registered at `BindingsModule.cs:499`, so no new DI line and no CLIO005 exposure), armed against the EARLIEST outstanding expiry and re-armed on the four events that can change it: register, supersede/reap, `SignalCompleted` (which moves an expiry DOWN), and the sweep itself. Deadline-driven rather than polled because the expiries are known exactly; a late fire only delays a reap, because the sweep re-tests `IsLive` against the clock. The dispatch-head call is KEPT: the timer removes the stranding, the awaited dispatch-head call removes the race between a sweep and the admission slot the caller is about to ask for. Timer callback never throws (it runs on a pool thread with nobody above it), sweeps are CHAINED so a deadline arriving mid-sweep is queued rather than dropped and disposal has one task to await. `_disposed` is set under `_gate` BEFORE the timer is disposed, and both the callback and the sweep test it, so post-disposal inertness does not rest on `ITimer.DisposeAsync` semantics.
Discovery: a frozen fake clock is WRONG for this type — `StickyWorkerEntry.MarkCompleted` stamps its linger against the real clock, so a registry judged against a frozen one places every completion in its own future and a linger case passes for the wrong reason. The fixture's `ControllableClock` is offset-based (real time + a delta the test moves) and its timers fire only from `Advance`, with callbacks invoked holding NO clock lock — the registry arms the timer under its own gate, so a fake that fired under its lock would deadlock the code it observes. Residual, named not fixed: an entry whose worker EXITED or whose session was retired is still only noticed at its expiry or by a dispatch — previously unbounded, now bounded by the timer.
Files: clio/Command/McpServer/Relay/StickyWorkerRegistry.cs, clio.tests/Command/McpServer/StickyWorkerSupervisionTests.cs
Impact: any new field that shortens a sticky lifetime must call `RearmLocked()` under `_gate`, or the bound silently reverts to "whenever somebody calls next". Red-before was watched with the seam present and the scheduling absent (5 failed / 0 passed), then re-confirmed by two mutations — dropping the re-arm in `SignalCompleted` fails only the linger case, and leaving the timer armed in sync `Dispose()` fails only the `(False)` disposal case.

## 2026-08-19 – Sticky completion signal moved to one choke point (ENG-95262 story 7)
Context: an external review found two P1 strands — `compile-creatio` refusing a comma-separated `package-name`, and `restart-by-environment-name` with `waitReady=false` / a failed restart request — each returning from a sticky tool WITHOUT `WorkerOperationCompletionSignal.ReportCompleted`, leaving the worker holding an admission slot and the target's configuration-build reservation for the full 30-minute hard lifetime. They are one class: the signal was emitted at the single place each happy path ended.
Decision: emission moved OUT of the four tools into `WorkerOperationCompletionSignal.RunToolCallAsync`, opened as the OUTERMOST thing in `McpToolErrorFilter.HandleCallToolErrors` (so the filter's own pre-execution refusals are covered too). Hand-off is detected by `McpProgressHeartbeat.RunWithProgressAndDeadlineAsync` — the only mechanism that detaches work in all four families — leasing the operation BEFORE `Task.Run`. Signal fires when `call ended && no outstanding operation`, exactly once, gated on `McpWorkerEnvironment.IsWorkerProcess` so the in-process host is untouched.
Discovery: the predicate MUST be `Lifetime == Sticky && StartsOperation` — `compile-status` and `restart-status` are also `Sticky` (they must reach the worker), so `Sticky` alone makes every poll reap the worker mid-compile. `Substitute.For<McpServer>()` intercepts `SendMessageAsync`, so the private notification is assertable on the wire through `WorkerOperationSignalContract.TryRead` with no production test seam. A sticky family reached through `clio-run` is NOT covered (the executor's own name arrives at the filter) — recorded as a residual.
Files: clio/Command/McpServer/Tools/WorkerOperationCompletionSignal.cs, clio/Command/McpServer/Tools/McpProgressHeartbeat.cs, clio/Command/McpServer/McpToolErrorFilter.cs, clio/Command/McpServer/Tools/{CompileCreatioTool,RestartTool,InstallProcessBuilderTool,ApplicationTool}.cs, clio.tests/Command/McpServer/WorkerOperationCompletionSignalTests.cs
Impact: adding a fifth long-running family needs only `Lifetime = Sticky, StartsOperation = true` on its metadata — no per-tool signal call and no new return statement to remember.

## 2026-08-19 05:10 – Sticky workers are reaped when they DIE, not at their lifetime bound
Context: external P2 (already named as a residual by the deadline-timer work) — `StickyWorkerEntry.IsLive` rejected an exited/retired worker but nothing invoked it: the registry's timer was armed on `ExpiresAtUtc` only, and `TryReach`/`ReapExpiredAsync` ran solely on another sticky dispatch. A worker that crashed kept the registry's lease, and therefore a slot under the shared admission semaphore, for up to 30 minutes.
Decision: per-entry supervision watcher started in `TryRegister` and torn down at every removal (`TryReach` dead path, `ReapAsync`, `ReapExpiredAsync`) and on disposal. Predicate is a NEW `StickyWorkerEntry.HasStoppedBeingReachable` (`Lease.HasExited || Session.IsRetired`) — deliberately NOT `IsLive`: expiry stays owned by the deadline timer, whose schedule the watcher must not duplicate. Process exit is event-driven through `IWorkerChannel.WaitForExitAsync` (zero cost while healthy); retirement has no event on `WorkerRelaySession`, so it is a 15 s cadence — the only mechanism available to a type that does not own the relay. Reaps are queued on the EXISTING `_sweeps` chain and re-test `_disposed`, so disposal keeps one task to await and no callback runs afterwards.
Discovery: (1) `!IsLive` as the watcher predicate reaped freshly registered workers in the fixture — the lease is stamped from the real clock while the registry judges against the offset clock, so a registration after an `Advance` is born expired. Splitting reachability out of `IsLive` is what makes supervision and the deadline disjoint. (2) A `ControllableClock` that publishes a timer BEFORE arming it can drop a whole `Advance` for timers armed on pool threads; `CreateTimer` now arms first, and `TimerCount` lets a case wait until watchers are parked. (3) `DispatchAsync_ShouldReapTheStickyWorkerAndReturnItsSlot_WhenTheWorkerSignalsCompletion` gated on the RESERVATION, which is released at `MarkCompleted`, while the slot comes back on the registry's own sweep — a pre-existing race (0/20 at HEAD, ~1/10 with the extra background work) now gated on `ActiveStickyWorkers == 0`; 0/25 under 12-way CPU load afterwards.
Files: clio/Command/McpServer/Relay/StickyWorkerRegistry.cs, clio.tests/Command/McpServer/StickyWorkerSupervisionTests.cs
Impact: a dead or unreachable sticky worker returns its admission slot within one cadence instead of half an hour. Red-before proved with four mutations (no watcher / no cadence / no teardown / no disposal guards), each caught by exactly the intended case. Validated: dotnet test --filter "Category=Unit&Module!=Common" — 8351 passed, 0 failed.

## 2026-08-19 — .clio-pages / --output-file publication made kill-atomic (ENG-95262)
Context: get-page and get-schema ship in the stage-6 worker cohort, so both are bounded by an
uncatchable parent kill. Both published local output in place.
Decision: remedy (1) — stage then swap, in both writers. PageFileWriter builds the tree in
`.clio-pages/.staging/{schema}/{guid}` and publishes with two renames (old aside, new in);
OutputPathConfinement.WriteAtomic completes the body in a sibling `.tmp` and moves it onto the target.
Neither tool leaves the cohort.
Discovery: (a) the durable damage was not untidiness — meta.json is written LAST, so a kill after
body.js left a directory that reads as a successful get-page with NO baseline, which
PageBaselineStore reports as "no baseline" and update-page then runs with no expected checksum;
(b) WriteAtomic's FileMode.CreateNew on the TARGET meant a kill left an empty file at exactly the path
OutputPathConfinement.Resolve refuses to overwrite, so the kill blocked its own retry; (c) no
cross-platform atomic directory replacement exists (renameat2(RENAME_EXCHANGE) is Linux-only and not
exposed by .NET), so the residual state is "directory absent", which is the honest never-fetched state;
(d) a kill cannot be modelled by throwing (production catches it and finally still runs) — snapshot
BETWEEN filesystem operations instead: clio.tests/Command/InterruptionObservingFileSystem.cs.
Files: clio/Command/PageFileWriter.cs, clio/Command/OutputPathConfinement.cs,
clio.tests/Command/InterruptionObservingFileSystem.cs, clio.tests/Command/PageFileWriterKillSafetyTests.cs,
clio.tests/Command/OutputPathConfinementKillSafetyTests.cs
Impact: any future cohort candidate that publishes local output can reuse the observing file system to
prove the same property; the kill-safety audit's blanket "plus every read-only tool" needs the
publication caveat spelled out.

## 2026-08-19 02:10 – ENG-95262: nine rounds of external review, and what it cost to be wrong twice
Context: Alex asked for codex review (gpt-5.6-sol, high) over the whole branch against
origin/master, fix, re-review, until a round produces nothing to fix.
Decision: verify EVERY finding against the code before acting, and mutation-check every
fix rather than trusting a green suite.
Discovery, and the parts worth carrying forward:
  - 38 findings over 9 rounds, 38 confirmed, 0 rejected. That precision is itself a
    finding: three earlier parallel reviews had missed most of them.
  - TWICE a round's own fix created the next round's defect. Round 2 widened a lock and
    bounded a join; together they cancelled out (round 3). Round 4's saturation envelope
    was applied to one of three spawn paths (round 5). Neither was visible without
    another full pass.
  - TWICE I claimed a fix I had not made. "Unified the reservation" unified the KEY and
    left two dictionaries (found in round 9, two rounds later). The lesson is that a
    commit message is an assertion and deserves the same scepticism as a test.
  - THREE TIMES I wrote a test one layer away from the change it defended — pinning the
    envelope builder, not the dispatcher's routing to it; pinning the metadata reader,
    not the filter that supplies its argument. Every time the mutation found it and the
    suite did not.
  - The strongest agent reports were the ones that argued back: a test I specified that
    CANNOT exist under an order-preserving design; two corrections to an ordering I had
    dictated; a mutation that was NOT caught, reported as a gap rather than smoothed.
Files: too many to list; see commits between 35be22b05 and HEAD.
Impact: the method generalises — mutate at the seam the assertion names, require THAT
test to go red, and treat "I already fixed that" as a claim to re-verify, not a fact.

## 2026-08-19 12:20 – Two open questions closed: no lifetime ceiling, and the finally that never runs
Context: the review loop had cleared the reported defects, leaving two items I had raised
  against myself: whether the terminal-stage path needs an absolute lifetime ceiling, and
  what happens to the temporary trees a killed worker abandons.
Decision: NO ceiling for terminal-stage, recorded in ADR §3.3 with the reasoning rather
  than left as an absence. Any number large enough not to truncate a real deploy can only
  fire on the runs it is most expensive to be wrong about, and what it produces on firing
  is the false half-install the whole protocol exists to avoid. The live-locked case is
  bounded by the caller's own token, by one worker slot, and by saturation reporting.
Discovery: `CreateTempDirectory` cleans up in a `finally`, and a killed process runs no
  `finally`. What used to be an occasional Ctrl+C leftover becomes one abandoned tree PER
  KILL under the execution boundary — the boundary's own litter. Swept at host startup
  beside the stale-worker reap, matching only the 32-hex names the generator produces and
  only those older than a day, because a working directory carries no owner to ask and a
  younger one may belong to a clio process running right now.
  Two traps on the way: a `using X = System.IO.Abstractions.IFileSystem` alias at file
  level is BEATEN by `Clio.Common.IFileSystem` when the file's namespace is nested under
  it (the alias has to go inside the namespace); and `Clio.Common.McpWorker` is excluded
  from the DI auto-scan wholesale, so a new service there is invisible until registered by
  hand — which only the ValidateOnBuild wiring tests reveal.
Files: clio/Common/McpWorker/WorkerTempResidueSweeper.cs, .../IWorkerTempResidueSweeper.cs,
  clio/Command/McpServer/McpServerCommand.cs, clio/BindingsModule.cs,
  clio.tests/Common/McpWorker/WorkerTempResidueSweeperTests.cs,
  spec/adr/adr-mcp-worker-execution-boundary.md,
  spec/mcp-worker-execution-boundary/mcp-worker-execution-boundary-kill-safety-audit.md
Impact: the pattern to carry forward is that a `finally` is a promise the kill boundary
  breaks, so every cleanup written as one is a candidate for the same treatment.

## 2026-08-19 12:45 – sync-pages publishes its verified read-back under one gate acquisition
Context: external review found sync-pages' verify path taking the schema gate twice — once for
  body.js, once for meta.json — leaving a window in which a worker-routed get-page swaps the
  schema directory and the baseline ends up describing a body that is no longer there.
Decision: merged the two into PublishVerifiedReadBack, one acquisition over both local writes;
  the network read-back stays outside it, and the anchor's CwdLock stays outside the gate so the
  lock order per-tenant → CwdLock → schema gate still matches PageFileWriter.
Discovery: the residual is wider than the window. sync writes body.js + meta.json in place and
  never writes bundle.json, so the bundle always describes an older generation; and a kill between
  the two writes still splits body from baseline. Staging + swap is the fix, but reusing
  PageFileWriter.WritePageFiles as it stands would strip EnvironmentUri — its BuildBaseline has no
  MergeEnvironmentIdentity, so a URI-mode baseline would silently lose the identity that arms
  conflict detection. Recorded as the acceptance test for a follow-up, not added red to the suite.
Files: clio/Command/McpServer/Tools/PageSyncTool.cs,
  clio.tests/Command/McpServer/PageSyncToolBaselineTests.cs,
  clio.tests/Command/McpServer/OneShotInterleavingFileGate.cs
Impact: OneShotInterleavingFileGate is the reusable way to test "another writer wins the gate in
  the gap"; InterruptionObservingFileSystem is the way to test a kill. Not interchangeable — the
  first models a competitor, the second models a corpse.

## 2026-08-19 14:10 – Round 13: a slot returned to a child that never died
Context: codex round 13 over the whole branch. Three findings — one already closed by
  the commit the review predated, two real.
Decision: lease disposal no longer treats "we asked it to die" and "it is gone" as the
  same event. When Kill() reports Failed the lease HOLDS its registration and its slot
  and releases them only on observed exit. Unbounded wait, deliberately: there is no
  deadline after which it becomes safe to assume a live process has died, and a timer
  that released anyway would restore the state being removed.
Discovery: the old path was the worst available shape. Releasing unregistered the
  worker, disposed its handle and returned its slot — so a still-running authenticated
  child was invisible to admission accounting AND absent from the stale reap, because
  the entry the reap reads had just been deleted. The trade accepted here is that a
  held slot is VISIBLE (saturation reports its numbers, R-10) while an invisible
  runaway cannot be reported at all.
  Test-shape note: each of the two tests guards one direction and only one goes red
  under the mutation — the hold test. That is correct, not weak coverage: the deferred
  release passes trivially when everything is released eagerly, so it can only guard
  the opposite regression.
Files: clio/Common/McpWorker/WorkerProcessSupervisor.cs,
  clio.tests/Command/McpServer/WorkerProcessSupervisorTests.cs,
  spec/adr/adr-mcp-worker-execution-boundary.md
Impact: "release on confirmation, not on request" is the general form — the same
  question applies anywhere this codebase returns a resource after asking something
  else to give it up.

## 2026-08-19 15:20 – Worker feature freeze must be TOTAL, not validated
Context: external review P2 on this branch — McpWorkerEnvironment.Format threw on a feature key
  containing ';' or '=', and the freeze runs before the spawn on all three dispatch paths, so one
  key from `clio experimental --name` (or a hand-edited appsettings.json) disabled the whole
  worker cohort.
Decision: serialize losslessly (Uri.EscapeDataString on the NAME) rather than reject at the write
  surface. GetFeatures() reads whatever is on disk whoever wrote it, so a write-surface guard
  cannot clean a key already there — and would additionally remove the only CLI path to clear an
  orphaned bad key (`--name "a=b" --disable` would start refusing). An unreadable payload segment
  is dropped one at a time and reads as off, never rejected wholesale.
Discovery: (1) a THIRD failure mode nobody reported — Parse trimmed the name, so a key with
  leading/trailing whitespace was silently renamed rather than refused. (2) Uri.Escape/Unescape are
  total on net8.0 and net10.0: no throw on a lone surrogate (U+FFFD), no length limit, lenient on
  '%zz'. STJ rejects an unpaired-surrogate dictionary key at load, so no invalid UTF-16 reaches
  Format. (3) old parent → new worker IS a reachable mixed-version pair, because the spawn
  re-resolves the executable on disk and an in-place tool update replaces it; the escaped format is
  a strict superset of the old one, so no compatibility branch is needed.
  The SHAPE was the real finding: all three paths composed the child environment OUTSIDE their try,
  so any throwing helper on that line is cohort-fatal by construction. Now routed through
  ComposeChildEnvironmentSafely, which falls back to an empty feature map — deliberately untested,
  because after the encoding fix no reachable input makes it throw, and it is recorded as insurance
  rather than dressed up as covered behaviour.
Files: clio/Common/McpWorker/McpWorkerEnvironment.cs,
  clio/Command/McpServer/Relay/McpWorkerCallDispatcher{,.Sticky,.TerminalStage}.cs,
  clio.tests/Command/McpServer/McpWorkerModeTests.cs,
  clio.tests/Command/SettingsRepositoryFeatureTests.cs
Impact: judge any parent→child payload on TOTALITY (never throws on data already on disk), not on
  input validation.

## 2026-08-19 17:40 – A read moved to a worker is a read moved to another Creatio session
Context: three page tests were green on trunk and failing on this branch. I had them filed as
  "non-branch failures". They were not: two are branch regressions and the third is a new test
  of mine that had never been green.
Discovery (proven from the stand's own server-side write ledger, not inferred): a worker
  authenticates on its OWN session — the cookie container is per-process — so a read relayed to a
  child speaks to Creatio on a different session from the host-resident tools that write the same
  object. A worker get-page returned a body 14 s out of date. Because get-page also WRITES the
  conflict baseline, the stale read armed conflict detection against a superseded generation and a
  stale-baseline write was ACCEPTED. Silent overwrite, not a slow read.
Decision: Stage 6 membership re-decided by READ PATH, not by tool. Schema-designer reads
  withdrawn (get-page, get-schema, get-related-page-addon — their writers are host-resident);
  database reads kept (list-pages, list-app-sections, execute-esq, odata-read — a second session
  sees the same rows). The mechanism is recorded as INFERENCE (session affinity to an IIS worker
  process with its own schema-cache generation), because it decides which long-term fix is viable,
  and the membership rule does not depend on the answer.
  Two corrections to myself on the way: (1) my first replacement guard asserted "no cohort member
  is write-capable", which is false by design — deploy/compile/restart are write-capable and in
  the cohort deliberately; removed rather than adjusted. (2) I attributed the AppSettings test
  going green to the product pin; it was the first run containing my own retry commit, and all 13
  red occurrences predate it. The parsimonious cause was mine, and I had talked myself out of it.
Files: clio/Command/McpServer/McpWorkerCohort.cs,
  clio.tests/Command/McpServer/McpExecutionRouterTests.cs,
  spec/mcp-worker-execution-boundary/mcp-worker-execution-boundary-kill-safety-audit.md
Impact: cohort membership needs a SECOND question beside kill-safety — what does the read go
  through on the server, and does a host-resident tool write the same object in one agent session.
  Stage 10 faces it for every read it moves.

## 2026-08-19 19:05 – The poll destroyed the record it had just read
Context: e2e 15896920's fourth failure — compile-status answered 'failed' on poll one and
  'not-found' on poll two, adjacent calls.
Discovery: StickyWorkerPoll reaped a COMPLETED worker as soon as one poll had been answered.
  The second poll then found no entry, fell through to an ordinary per-call worker whose
  in-worker operation registry is empty, and the tool reported "no compile-creatio operation
  has been recorded for this environment in the current MCP server session" — a sentence that
  was FALSE about an operation the parent had just destroyed. compile-status has no bound on
  poll count: its own description tells the caller to poll, and clio/tpl/workspace/AGENTS.md
  ships "poll compile-status" verbatim into every user repo.
  The worst part is mine: DispatchAsync_ShouldReapACompletedWorker_OnceItsStatusPollHasBeenAnswered
  asserted exactly that behaviour and was GREEN. Two tests on the branch asserted mutually
  exclusive things; a test can pin a defect as firmly as it pins a contract.
Decision: reclaim ON DEMAND, not on poll. The naive fix — delete the reap — regressed the slot
  concern the reap was solving, and the test proved it within a minute: supersession reclaims
  only its OWN key, so a finished worker on a DIFFERENT key refuses the next long operation for
  the whole 5-minute linger on a host whose sticky capacity is one. New ReapCompletedAsync is
  called from the STARTER path only — the one path that is about to want a slot. The record
  survives while nothing else needs the host, and real work outranks a possible future poll.
Files: clio/Command/McpServer/Relay/StickyWorkerPoll.cs,
  clio/Command/McpServer/Relay/StickyWorkerRegistry.cs,
  clio/Command/McpServer/Relay/McpWorkerCallDispatcher.Sticky.cs,
  clio.tests/Command/McpServer/StickyWorkerSupervisionTests.cs
Impact: when two claims conflict — a record a caller may still read, and capacity somebody
  needs now — reconcile them by WHEN rather than by choosing a permanent loser. Also: a latent
  hole remains and is recorded rather than fixed — a worker whose PROCESS dies mid-linger takes
  its record with it, because linger holds a lease, not a process. Unfixable by lingering; it
  needs the record to live outside the child.

## 2026-08-19 20:15 – The settings publish retry, hardened — and a commit that swept in a neighbour
Context: the reg-web-app publish retry took the AppSettings e2e from ~100% red to a MEASURED
  ~5% red per run — the reader holds the file 84.4% of its 46 us cycle, and P(refused) is that
  duty cycle raised to the attempt count. A 5%-per-run flake is not a closed defect.
Decision: window 2.5 s -> 5 s (well under 1% by the same arithmetic), jitter on the CAPPED tail
  only with the draw injected, and the exhausted failure WRAPPED with the destination path, the
  elapsed retry time and the attempt count. That last one is the real deliverable: the BCL
  message is "The process cannot access the file because it is being used by another process."
  with no path, no operation and no hint that clio already retried — one pathless sentence that
  cost a day of investigation.
Discovery: (1) the retry covered three publish branches out of four — MoveNewSettingsFile, the
  FIRST-WRITE path SettingsBootstrapService takes, went bare, so "the settings publish is
  protected" was true of the branches the concurrency test happens to exercise. Now retried,
  with the SettingsFileChangedException screen deliberately left OUTSIDE the retry: a move
  refused because the destination now exists is not contention. (2) Doubling the window halved
  the lock headroom — one hold can spend it SettingsUpdateAttemptLimit times, 3 x 5 s = 15 s —
  so SettingsLockTimeoutSeconds doubles to 60 with it, because "the settings lock timed out" is
  a more confusing failure than the contention behind it.
  The honest limit is now stated where a tuner will read it: on Windows no API atomically
  replaces a file a foreign process holds without FILE_SHARE_DELETE, FILE_RENAME_POSIX_SEMANTICS
  does not rescue it, so clio CANNOT guarantee exit 0 and any reader outliving the window
  defeats any window.
Process note, my error: commit 726d0cc1c ("the status poll destroyed the record it had just
  read") also swept in this settings work, because I ran `git add -A` while a parallel agent was
  mid-task. The commit is pushed and carries two unrelated changes. Not rewritten — the branch
  is shared with scheduled routines and a rebase would cost more than the muddled message. The
  lesson is `git add <paths>` whenever an agent is running, and it is cheap to follow.
Files: clio/Common/FileSystem.cs, clio/Environment/ConfigurationOptions.cs,
  clio.tests/Command/SettingsRepositoryConcurrencyTests.cs, clio.tests/Common/FileSystem.Tests.cs
Impact: when a retry turns a certainty into a probability, say which probability — a fix
  reported without its residual failure rate reads as closed when it is not.

## 2026-08-19 21:30 – Measured on Windows: Replace and Move(overwrite) are not interchangeable
Context: the settings-publish investigation left one question that macOS cannot answer — does
  File.Replace succeed against a reader holding FileShare.Delete, or does delete-pending bite?
  It decided whether changing the e2e reader would actually help.
Access note: ts1-core-dev04 refuses my key (Permission denied, and no entry in ~/.ssh/config).
  The Windows host that WORKS is `runner` = a_kravchuk2.tscrm.com, domain login tscrm\a.kravchuk,
  same key, 32 cores, Windows 10.0.26200, .NET SDK 9 + runtime 10.0.10 (no SDK 10 yet). Almost
  certainly the host the earlier MoveFileEx measurement was taken on; I had misremembered it.
Measured (probe under net9.0, scratchpad/winprobe):
                                   File.Replace     File.Move(overwrite: true)
    reader FileShare.Read          REFUSED (IO)     REFUSED (UnauthorizedAccess)
    reader ReadWrite | Delete      ALLOWED          REFUSED (UnauthorizedAccess)
  And the delete-pending worry is unfounded: with the old handle still open, a second opener of
  the same name gets the NEW generation, not a failure.
Two consequences, both applied:
  1. The e2e reader can and must grant delete sharing — it was opening FileShare.Read and then
     asserting the publish it was itself refusing had exited 0, i.e. encoding a guarantee the OS
     forbids under its own arrangement.
  2. THE UNEXPECTED ONE: Move(overwrite) is refused even by a fully cooperating reader, so no
     retry window can rescue it — nothing the reader can do makes it legal. Publishing over an
     existing file now uses Replace whenever the file system is real. This mattered beyond the
     test: SettingsBootstrapService writes with verifyExpectedContent=false, which took the Move
     branch, so the bootstrap write was the one path a cooperative reader still could not save.
Files: clio.mcp.e2e/ClioPagesConcurrencyE2ETests.cs, clio/Environment/ConfigurationOptions.cs
Impact: measure the platform primitive before choosing between two that look equivalent. Move and
  Replace differ in exactly the case that matters, and no amount of retrying substitutes for
  picking the right one.

## 2026-08-19 22:45 – The Windows host, and what it settled
Context: three things on this branch could only be answered on Windows — the File.Replace /
  FileShare question, the ClioRing NativeAOT gate, and whether the one red Ring test was really
  macOS-only.
Access, because I wasted time on this: ts1-core-dev04 REFUSES my key (Permission denied; no
  entry in ~/.ssh/config either) although it is in known_hosts. The host that WORKS is
  `runner` = a_kravchuk2.tscrm.com, domain login tscrm\a.kravchuk, same key. Windows 10.0.26200,
  32 cores, MSVC present. It shipped with .NET SDK 9 + runtime 10.0.10; SDK 10.0.400 installed
  into $env:USERPROFILE\.dotnet via dot.net/v1/dotnet-install.ps1 — no admin rights needed.
  Branch cloned to C:\dev\clio (--depth 1 --single-branch).
Results:
  - NativeAOT publish (win-x64, self-contained, PublishAot=true): SUCCEEDED, clio-ring.exe
    30.8 MB, ZERO IL2026/IL3050/IL2104/IL3053 and zero warnings of any kind.
  - ClioRing.Tests on Windows: 156 passed, 0 failed — which confirms the single macOS failure
    (ResolveContainedReceiptPath ... WhenRunKeyIsHostile) is a platform artefact: '\' is not a
    separator on Unix, so the hostile run key never traverses there.
Mistake worth not repeating: I started the Ring tests WHILE the AOT publish was still running on
  the same tree and got CS2012 "Cannot open ClioRing.Ipc.pdb ... user-mapped section open" —
  two builds fighting over one obj directory. Serialise builds on a shared checkout.
Files: none in the repo; this is verification, recorded so the next person does not re-derive the
  host, the SDK gap, or the macOS-only classification.
Impact: `runner` is now a fully equipped Windows bench for this branch — SDK 10, MSVC, a clone.

## 2026-08-19 16:20 – PR #1080 driven back to green after two base merges
Context: The worker-boundary PR was CONFLICTING against master and its SonarCloud gate was red on new_reliability_rating = D. Master then moved a second time while CI ran.
Decision: Fix both BUG findings rather than only the CRITICAL one — reliability maps to the worst remaining bug severity, so clearing S4586 alone would have moved D→C and still failed the "≥ A" condition. Clear the maintainability findings that are local and mechanical (S1168, S1066, S1172, S8970, eight S3267) and leave 26 with reasons attached: the eleven S2925 Thread.Sleep findings are in deadline/heartbeat tests where a fake clock makes the assertion vacuous, the two S1994 are the Windows-hardened retry loops this branch measured, six S107 would churn the composition root, two S125 are false positives on prose.
Discovery: The first merge brought master's `get-classic-list-columns`, which carried no `[McpToolExecution]` and so failed the coverage gate — correctly, since a new tool must not ship unclassified. It is classified **InProcess**, not Worker, and the reason is the read PATH: the hierarchy read goes through IPageDesignerHierarchyClient, the schema-designer path SchemaDesignerReadsWithheldNames withdrew. Worker + "excluded by cohort" would leave that constraint encoded only in a comment; InProcess means "never relayed to a worker", so a later cohort expansion cannot re-admit the read by reasoning over the classification. The InProcess invariant validator then forces Lifetime=NotApplicable / BudgetPolicy=None, and the budget rule does not apply because the tool declares ReadOnly.
Discovery: The MCP e2e check cannot produce a branch signal right now. Four builds (15897811, 15897838, 15897857, 15897944) all died in `Deploy Application (.NET)` → `InstallBundlePackages`, with `testOccurrences count: 0`; thirteen trunk builds failed the same step across the same day. The stand PROVISIONING is broken, so this is not contention — each build gets its own Creatio.
Files: clio/Command/McpServer/Progress/StageEventEmitter.cs, clio/Command/McpServer/Relay/McpWorkerCallDispatcher.Sticky.cs, clio/Command/McpServer/Relay/McpWorkerCallDispatcher.cs, clio/Command/McpServer/Relay/StickyWorkerRegistry.cs, clio/Command/McpServer/Tools/SessionTargetNormalizer.cs, clio/Command/McpServer/Tools/GetClassicListColumnsTool.cs, clio/Command/McpServer/McpToolErrorFilter.cs
Impact: Next person hitting a red reliability gate can skip the "which finding matters" step, and the InProcess-vs-cohort reasoning above is the rule to apply to any future schema-designer read arriving from master.


## 2026-08-19 16:10 – Schema-level export/import: the platform already had it, and its signature moves between cores
Context: issue #1113 — no way to move ONE schema between environments; `pull-pkg`/`push-pkg` carries the whole package, which on a customer production site risks overwriting customization that exists only there. Addons (`AddonSchemaManager`) were the worst case: writable via `create-page-business-rules`, readable by nothing.
Decision: do NOT build per-designer round-trips (one contract per schema kind, and entity schemas / processes have no safe `GetSchema`/`SaveSchema` pair), and do NOT read the file-system representation (`Pkg/<Package>/Schemas/…` is populated only under File Design Mode — the exact heavyweight operation the feature exists to avoid, and cliogate's `PackageExplorer` is rooted at `…/Files` anyway). Instead delegate to the platform's own `Terrasoft.Core.SchemaImporter`, which is type-agnostic and already serialises metadata + properties + localizable values in one self-describing JSON document. Three new cliogate routes (`FindSchemaLayers`, `ExportSchema`, `ImportSchema`) + `export-schema` / `import-schema` CLI verbs and MCP tools.
Discovery — THE API IS NOT STABLE ACROSS CORES, and the failure mode is opaque. CreatioSDK 8.1.4 exposes `SchemaImporter.ImportSchemaToWorkspace(string, Guid, UserConnection)` as a public static; a 10.1.473 stand exposes NO public static of that name at all and serves the operation only through the explicit implementation of `ISchemaImporter.ImportSchema(string, Guid)` — an interface whose TYPE is public but whose MEMBERS are inaccessible outside the core assembly, so it cannot be referenced early-bound either and has to be recovered from `typeof(SchemaImporter).GetInterfaces()`. `ExportSchema(Guid, SystemUserConnection)` is still a public static on both, hence static-first-then-interface dispatch rather than one or the other.
Discovery — a missing member surfaces as WCF `Request Error`, not an exception you can catch. The `MissingMethodException` is raised while the CALLING method is JITTED, so an in-method try/catch never sees it and the endpoint answers with an opaque HTML error page. Isolating each platform call in its own `[MethodImpl(MethodImplOptions.NoInlining)]` method moves the failure inside the caller's try block and turns it into a readable message — that single change is what made the whole investigation tractable. Generalisation: when a cliogate endpoint fails with `Request Error` and nothing in the logs, suspect a JIT-time member/type resolution failure against the target core, not your request body.
Discovery — `ClassFactory.Get(Type)` is a trap. The non-generic overload routes through `GetInstance<T>` with `T` bound to `object` and fails with `Error creating an instance of the "System.Object" class`. Resolve a runtime-only service type via `MakeGenericMethod` on the generic `Get<T>(params ConstructorArgument[])` instead.
Discovery — `SysSchema` has no `SysWorkspaceId` column on 8.x; a workspace filter is not just unnecessary, it breaks the query.
Discovery — a package pushed with `push-pkg` is locked against schema writes even after `unlock-package`; it also needs `InstallType = 0` (an `UPDATE` via `execute-sql-script` works — `SELECT` is blocked by `CustomQuery.ExecuteReader` security) plus an app restart before `create-schema` succeeds in it.
Files: cliogate/Files/cs/CreatioApiGateway.cs, cliogate/Files/cs/Dto/SchemaTransferDto.cs, clio/Command/SchemaTransfer/*, clio/Command/{Export,Import}SchemaCommand.cs, clio/Command/McpServer/Tools/{Export,Import}SchemaTool.cs, clio/Common/ServiceUrlBuilder.cs, spec/prd/prd-schema-level-export-import.md, spec/adr/adr-schema-level-export-import.md
Impact: any schema kind the platform can export now round-trips as a small reviewable folder instead of a multi-megabyte package. The reflection-dispatch helper is reusable for any other cliogate call into a core API that drifts.

## 2026-08-19 17:50 – Schema export/import verified on a stand, and three traps it took to get there
Context: continuation of the entry above; the feature was implemented but unverified end to end.
Discovery — `-p` IS ALREADY TAKEN. `EnvironmentOptions` binds `-p` to `--password`, so a second `[Option('p', "package-name", …)]` makes CommandLine reject the WHOLE verb with `Sequence contains more than one matching element` — before `Execute` runs, so no in-command try/catch can explain it. The command works fine as long as you omit the colliding flag, which makes it look like an argument-value problem rather than a duplicate short name. Both new verbs now take `--package-name` only. Worth checking any new short option against `CommandLineOptions.cs` first.
Discovery — the platform payload is NOT RFC-valid JSON. `SchemaImporter.ExportSchema` embeds the schema metadata as a JSON *string* containing raw CR/LF control characters. `System.Text.Json` refuses it (Python's `json.loads` does too, without `strict=False`), so the bundle's human-readable projections were silently skipped on every real export while the "best-effort" catch swallowed it. Switched the projection parser to Newtonsoft, which accepts it; 28 per-culture resource files then appeared for a real addon.
Discovery — reflection hides the real error. Invoking the platform importer through `MethodInfo.Invoke` wraps every platform failure in `TargetInvocationException` ("Exception has been thrown by the target of an invocation."), including the ones a caller most needs — e.g. `Unable to save changes for item "X". It is either created by third-party publisher or installed from the file archive`. The error builder now unwraps the chain before reporting.
Discovery — `ClassFactory.Get(Type)` is not the non-generic form of `Get<T>()`. It routes through `GetInstance<T>` with `T` bound to `object` and fails with `Error creating an instance of the "System.Object" class`. Resolve a runtime-only service type via `MakeGenericMethod` on the generic `Get<T>(params ConstructorArgument[])`.
Verified on `sae_m_seeenu_15888720_0820` (cliogate 2.0.0.46, .NET Framework, core 10.1): `FindSchemaLayers` lists layers and disambiguates; `export-schema` produced a full addon bundle (`ActivityBusinessRule` — descriptor + metadata + properties + 28 per-culture resource files); `import-schema --dry-run` reported REPLACE / CREATE / refused NEW LAYER correctly; a delete-then-import round trip recreated `UsrClioTransferProbe` WITH ITS ORIGINAL UId (`2c5cd215-1994-4758-9314-a9c866f0dcbe`), which is the property that makes repeated transfers safe. Importing a Creatio-owned addon into a customer package is refused by the platform itself ("third-party publisher"); that refusal now surfaces verbatim.
Operational note: `push-pkg` of cliogate on a stand that is simultaneously serving other clio requests can wedge for 10+ minutes and then fail in `UploadFile`. Deploy serially, with nothing else touching the environment.
Files: clio/Command/{Export,Import}SchemaCommand.cs, clio/Command/SchemaTransfer/SchemaBundleStore.cs, cliogate/Files/cs/CreatioApiGateway.cs, clio/help/en/{export,import}-schema.txt, clio/docs/commands/{export,import}-schema.md, spec/adr/adr-schema-level-export-import.md
Impact: the issue's scenario is now one command each way, with a dry run in between, instead of a 3.7 MB package push.

## 2026-08-21 14:20 – odata-create reports its side effect
Context: an E2E run of the process-designer MCP surface exposed a create defect. Three
`odata-create` calls into `MailboxSyncSettings` each reported `failed: 1` while every row was in
fact inserted, so the caller retried and produced three duplicate mailboxes.
Decision: model the side effect the way this repo already models `section-created` — nullable bool
plus `retry-guidance` — and reserve `false` for rows rejected locally, before any request leaves
clio. Every server-side failure is `null` (unknown), never "not created".
Discovery: a Creatio OData POST can return an error AFTER the row is written (a post-insert entity
event handler that throws), so a failed POST does NOT imply no side effect. Separately: the curated
contract in `ToolContractGetTool.cs` lists output fields by hand and does not follow the response
record, so it went stale until updated explicitly — a build cannot catch that drift.
Files: clio/Command/McpServer/Tools/ODataCreateTool.cs,
clio/Command/McpServer/Tools/ODataCreateBatchResponse.cs,
clio/Command/McpServer/Tools/ToolContractGetTool.cs
Impact: a consumer can distinguish an unverified insert from a verified failure instead of silently
duplicating rows on the natural retry.
