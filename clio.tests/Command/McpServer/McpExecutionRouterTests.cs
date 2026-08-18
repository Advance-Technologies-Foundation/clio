using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// The single execution-routing authority (ENG-95262 Stage 4b, ADR §9) and its THREE dispatch sites: the
/// matched filter (<see cref="McpToolErrorFilter"/>), the unmatched durable handler
/// (<see cref="McpDurableCallToolHandler"/>) and the <c>clio-run</c> inner dispatch
/// (<see cref="ClioRunExecutor"/>).
/// </summary>
/// <remarks>
/// <para>
/// There is no feature flag (ADR §5): a tool routes to a worker when its <c>Location</c> metadata says so
/// AND it is in the Stage 6 cohort AND this process may spawn workers at all. The load-bearing distinction
/// these tests draw is therefore between what the router REPORTS (<c>Worker</c> for all 153
/// worker-classified tools) and where the call actually EXECUTES (a worker for the cohort, the host process
/// for everything else). A test that could not tell those apart would pass on a router that decides
/// nothing.
/// </para>
/// </remarks>
[TestFixture]
[Property("Module", "McpServer")]
public sealed class McpExecutionRouterTests {

	// A real worker-classified tool with a real deprecated alias in the production compatibility seed —
	// the one subject that can be reached by all three routes, which is what TC-U-402 needs.
	private const string WorkerToolCanonicalName = "get-classic-page-sources";
	private const string WorkerToolDeprecatedAlias = "get-classic-migration-bundle";

	// A real Stage 6 COHORT member: worker-classified AND in the shipped cohort, so the production-shaped
	// router relays it. get-classic-page-sources above is deliberately NOT a member — the pair is what makes
	// "declared worker" and "actually relayed" separately assertable.
	private const string CohortToolName = Clio.Command.McpServer.Tools.PageListTool.ToolName;

	// clio-run itself is classified in-process: the wrapper resolves and dispatches, the INNER tool decides.
	private const string ExecutorToolName = "clio-run";

	private IMcpToolExecutionMetadataReader _reader;
	private McpExecutionRouter _sut;

	[SetUp]
	public void SetUp() {
		// The real reader over the real clio tool catalog and the real (production seed) compatibility
		// catalog: routing is only meaningful against the metadata that actually ships.
		_reader = new McpToolExecutionMetadataReader(new McpToolCompatibilityCatalog());
		// PRODUCTION-SHAPED: the shipped cohort and a gate that reports an ordinary stdio host. Anything a
		// test wants to vary it states explicitly rather than inheriting.
		_sut = ProductionShapedRouter();
	}

	/// <summary>The router production resolves: shipped cohort, stdio host, worker path wired.</summary>
	private McpExecutionRouter ProductionShapedRouter() =>
		new(_reader, new McpWorkerCohort(), StdioHostGate(), workerPathWired: true);

	/// <summary>A gate reporting an ordinary stdio host that is not itself a worker.</summary>
	private static IMcpWorkerPathGate StdioHostGate() =>
		new McpWorkerPathGate(() => McpHostTransportKind.Stdio, () => false);

	/// <summary>A gate reporting a host on a transport that has no credential channel to a child.</summary>
	private static IMcpWorkerPathGate NonStdioHostGate() =>
		new McpWorkerPathGate(() => McpHostTransportKind.Http, () => false);

	// ---------------------------------------------------------------------------------------------
	// The authority itself
	// ---------------------------------------------------------------------------------------------

	[Test]
	[Category("Unit")]
	[Description("The metadata reader unwraps and canonicalises: the same tool named canonically, by deprecated alias, and as a clio-run inner command produces the SAME routing key and declared location. Argument shapes only — the SITE-level agreement is TC-U-402 below.")]
	public void Resolve_ShouldCanonicaliseAcrossArgumentShapes_WhenOneToolIsNamedThreeWays() {
		// Arrange — the three ARGUMENT SHAPES a dispatch site can hand the authority. This does not touch a
		// dispatch site; it pins the name resolution the sites depend on.
		// Act
		McpExecutionRoute matched = _sut.Resolve(WorkerToolCanonicalName, innerCommand: null);
		McpExecutionRoute viaAlias = _sut.Resolve(WorkerToolDeprecatedAlias, innerCommand: null);
		McpExecutionRoute viaExecutor = _sut.Resolve(ExecutorToolName, WorkerToolCanonicalName);

		// Assert
		matched.RoutingKey.Should().Be(WorkerToolCanonicalName,
			because: "a canonical name routes under itself");
		viaAlias.RoutingKey.Should().Be(WorkerToolCanonicalName,
			because: "a deprecated alias must be canonicalised BEFORE routing — routing on the alias would key on a name no tool declares");
		viaExecutor.RoutingKey.Should().Be(WorkerToolCanonicalName,
			because: "clio-run must be unwrapped before routing (ADR rule 7) — the long tail is reached through it");
		viaAlias.DeclaredLocation.Should().Be(matched.DeclaredLocation,
			because: "the same tool reached through an alias must execute in the same place, or the two seams disagree");
		viaExecutor.DeclaredLocation.Should().Be(matched.DeclaredLocation,
			because: "the same tool reached through clio-run must execute in the same place as when named directly");
		matched.DeclaredLocation.Should().Be(McpToolExecutionLocation.Worker,
			because: $"'{WorkerToolCanonicalName}' is classified worker, which is what makes this agreement check non-vacuous");
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-402: one canonical tool driven through all THREE real dispatch sites against ONE real router — matched name, deprecated alias arriving unmatched, and clio-run — reaches the SAME execution location.")]
	public async Task DispatchSites_ShouldAgreeOnExecutionLocation_WhenOneToolIsReachedThroughAllThree() {
		// Arrange — ONE real router shared by the three sites, and the only setting that makes the property
		// observable: with the production-shaped router every route is in-process, so "all three continued"
		// could not tell a site that consulted the authority from a site that never asked. Wired to a worker
		// path, a site that consults the shared rule refuses and names the canonical tool; a site that does
		// not, runs the tool. The three sites therefore have to DISAGREE for this test to pass wrongly.
		// The cohort is stated rather than inherited: this subject is the only tool reachable through all
		// three vectors (it has a real deprecated alias), and the shipped cohort does not contain it — so a
		// production-shaped router would answer InProcessOutsideCohort at every site and the agreement below
		// would be vacuous. Substituting membership is exactly the seam ADR §5 requires for this.
		IMcpExecutionRouter sharedRouter = new McpExecutionRouter(
			_reader, new McpWorkerCohort([WorkerToolCanonicalName]), StdioHostGate(), workerPathWired: true);
		IMcpToolCompatibilityCatalog realCatalog = new McpToolCompatibilityCatalog();
		// A real SDK tool published under the CANONICAL protocol name, so the matched site keys on exactly
		// the name production would match.
		McpServerTool canonicalTool = BuildEchoTool(WorkerToolCanonicalName);

		// Act — site (a): the matched filter, reached under the canonical name.
		bool matchedPipelineContinued = false;
		RequestContext<CallToolRequestParams> matchedContext = CallContext(WorkerToolCanonicalName);
		matchedContext.MatchedPrimitive = canonicalTool;
		matchedContext.Services = ProviderWith(sharedRouter);
		CallToolResult viaMatchedName = await McpToolErrorFilter.HandleCallToolErrors(
			(_, _) => { matchedPipelineContinued = true; return ValueTask.FromResult(new CallToolResult()); })(
			matchedContext, CancellationToken.None);

		// Act — site (b): the unmatched durable handler, reached under the DEPRECATED ALIAS (MatchedPrimitive
		// stays null, which is what the SDK does for a name absent from tools/list).
		IMcpToolInvokerRegistry durableRegistry = Substitute.For<IMcpToolInvokerRegistry>();
		durableRegistry.TryGetTool(WorkerToolCanonicalName, out Arg.Any<McpServerTool>())
			.Returns(callInfo => { callInfo[1] = canonicalTool; return true; });
		durableRegistry.IsReadOnly(WorkerToolCanonicalName).Returns(true);
		IClioRunExecutor durableExecutor = Substitute.For<IClioRunExecutor>();
		McpDurableCallToolHandler durableHandler = new(
			durableRegistry, realCatalog, durableExecutor, sharedRouter);
		CallToolResult viaAlias = await durableHandler.HandleAsync(
			CallContext(WorkerToolDeprecatedAlias), CancellationToken.None);

		// Act — site (c): the clio-run inner dispatch, also reached under the deprecated alias so the
		// unwrap AND the canonicalisation both have to happen before the routing question.
		IMcpToolInvokerRegistry clioRunRegistry = Substitute.For<IMcpToolInvokerRegistry>();
		clioRunRegistry.TryGetTool(WorkerToolDeprecatedAlias, out Arg.Any<McpServerTool>()).Returns(false);
		clioRunRegistry.TryGetTool(WorkerToolCanonicalName, out Arg.Any<McpServerTool>())
			.Returns(callInfo => { callInfo[1] = canonicalTool; return true; });
		ClioRunExecutor clioRun = new(clioRunRegistry, realCatalog, sharedRouter);
		CallToolResult viaClioRun = await clioRun.RunAsync(
			WorkerToolDeprecatedAlias,
			EchoArgs(),
			destructiveSurface: false,
			CallContext(ExecutorToolName),
			CancellationToken.None);

		// Assert — the execution location the three sites reached, compared as the refusal each site emits.
		// The refusal embeds the ROUTING KEY the decision was made under, so identical text across the three
		// is the site-level statement ADR §9 needs: one tool, three vectors, one execution location.
		string matchedText = TextOf(viaMatchedName);
		matchedText.Should().Contain($"Tool '{WorkerToolCanonicalName}'",
			because: "the matched site must consult the shared authority and refuse under the canonical name");
		TextOf(viaAlias).Should().Be(matchedText,
			because: "a deprecated alias arriving UNMATCHED must land on the same execution location as the canonical name — the hole ADR §9 names, and it is invisible unless the two SITES are compared");
		TextOf(viaClioRun).Should().Be(matchedText,
			because: "the same tool reached through clio-run must land there too, or the long tail executes somewhere its directly-named sibling does not");
		matchedText.Should().NotContain(WorkerToolDeprecatedAlias,
			because: "each site must canonicalise BEFORE routing; routing on the alias would key on a name no tool declares");

		// Assert — and none of the three actually ran the tool, which is what makes the agreement above an
		// agreement about EXECUTION rather than about error strings.
		matchedPipelineContinued.Should().BeFalse(
			because: "continuing the matched pipeline would execute a worker-routed tool in the host process");
		await durableExecutor.DidNotReceiveWithAnyArgs()
			.InvokeResolvedAsync(default, default, default, default);
		TextOf(viaClioRun).Should().NotContain("echo:",
			because: "the clio-run site must refuse before dispatching, not after");
	}

	[Test]
	[Category("Unit")]
	[Description("AC-05: a worker-classified tool OUTSIDE the Stage 6 cohort is REPORTED as worker while still EXECUTING in-process — the two facts stay separable, and the disposition names which of them applies.")]
	public void Resolve_ShouldReportWorkerButExecuteInProcess_WhenToolIsOutsideTheCohort() {
		// Arrange — the production-shaped router, i.e. exactly what DI resolves.
		// Act
		McpExecutionRoute route = _sut.Resolve(WorkerToolCanonicalName, innerCommand: null);

		// Assert
		route.DeclaredLocation.Should().Be(McpToolExecutionLocation.Worker,
			because: "the router must report the declared location even for a tool that has not moved yet — otherwise the decision is unobservable");
		route.Disposition.Should().Be(McpExecutionDisposition.InProcessOutsideCohort,
			because: "the disposition names WHY the call runs in the host: this tool's supervision is a later stage, not a missing destination");
		route.ExecutesInProcess.Should().BeTrue(
			because: "nothing outside the cohort may change route — that is AC-05, and it is what keeps Stage 6 a cohort rather than a catalog move");
	}

	[Test]
	[Category("Unit")]
	[Description("A worker-classified tool that is a Stage 6 cohort member routes to a worker on the production-shaped router — proving the router relays rather than always answering in-process.")]
	public void Resolve_ShouldDisposeToWorker_WhenToolIsInTheCohort() {
		// Arrange — nothing substituted: the shipped cohort and an ordinary stdio host.
		// Act
		McpExecutionRoute route = _sut.Resolve(CohortToolName, innerCommand: null);

		// Assert
		route.DeclaredLocation.Should().Be(McpToolExecutionLocation.Worker,
			because: "a cohort member must be worker-classified in the shipped metadata, or the cohort names a tool the metadata does not support");
		route.Disposition.Should().Be(McpExecutionDisposition.Worker,
			because: "a router that answered in-process for a cohort member would leave Stage 6 unwired while every other assertion passed vacuously");
		route.ExecutesInProcess.Should().BeFalse(
			because: "a Worker disposition is the one case a dispatch site must NOT run in the host process");
	}

	[Test]
	[Category("Unit")]
	[Description("THE STDIO-ONLY GATE: a cohort tool on a non-stdio host stays in-process with a named transport-gated disposition, because no credential channel to a child exists while Stage 5 is deferred.")]
	public void Resolve_ShouldRefuseToRelay_WhenHostTransportIsNotStdio() {
		// Arrange — identical in every respect to the production router except the declared transport.
		McpExecutionRouter httpHostRouter =
			new(_reader, new McpWorkerCohort(), NonStdioHostGate(), workerPathWired: true);

		// Act
		McpExecutionRoute route = httpHostRouter.Resolve(CohortToolName, innerCommand: null);

		// Assert
		route.DeclaredLocation.Should().Be(McpToolExecutionLocation.Worker,
			because: "the declaration does not change with the transport — only what the host may do about it does");
		route.Disposition.Should().Be(McpExecutionDisposition.InProcessTransportGated,
			because: "on mcp-http the caller's credentials live in the parent's HttpContext and the channel to hand them to a child does not exist, so relaying would either fail or run the call under a DIFFERENT identity");
		route.ExecutesInProcess.Should().BeTrue(
			because: "the call must still be served, in the host process, exactly as it was before the worker path existed");
	}

	[Test]
	[Category("Unit")]
	[Description("THE RECURSION GUARD: a cohort tool resolved INSIDE a worker process stays in-process, because the child receives the very call the parent relayed and would otherwise spawn a worker of its own without end.")]
	public void Resolve_ShouldRefuseToRelay_WhenThisProcessIsItselfAWorker() {
		// Arrange — a worker serves stdio too, so a transport-only check would have passed here.
		IMcpWorkerPathGate workerProcessGate =
			new McpWorkerPathGate(() => McpHostTransportKind.Stdio, () => true);
		McpExecutionRouter routerInsideAWorker =
			new(_reader, new McpWorkerCohort(), workerProcessGate, workerPathWired: true);

		// Act
		McpExecutionRoute route = routerInsideAWorker.Resolve(CohortToolName, innerCommand: null);

		// Assert
		route.Disposition.Should().Be(McpExecutionDisposition.InProcessWorkerRecursionGuard,
			because: "a worker that relayed would hand its child the same call and the chain would never terminate — and the reason is named so it cannot be mistaken for the transport gate");
		route.ExecutesInProcess.Should().BeTrue(
			because: "the worker's whole purpose is to EXECUTE the call it was given");
	}

	[Test]
	[Category("Unit")]
	[Description("With no worker path wired at all, a cohort tool reports Worker yet still executes in-process — the shape a router with no destination answers, kept distinct from the cohort and gate reasons.")]
	public void Resolve_ShouldReportPendingWorkerPath_WhenNoWorkerPathIsWired() {
		// Arrange
		McpExecutionRouter unwiredRouter =
			new(_reader, new McpWorkerCohort(), StdioHostGate(), workerPathWired: false);

		// Act
		McpExecutionRoute route = unwiredRouter.Resolve(CohortToolName, innerCommand: null);

		// Assert
		route.Disposition.Should().Be(McpExecutionDisposition.InProcessPendingWorkerPath,
			because: "'there is nowhere to route to' is a different statement from 'this tool has not moved yet' and from 'this host may not spawn workers' — collapsing them would make a wiring regression read as a deliberate scoping decision");
		route.ExecutesInProcess.Should().BeTrue(
			because: "with no destination the call must still be served in the host process");
	}

	[Test]
	[Category("Unit")]
	[Description("Cohort membership, not the wiring, is what moves a tool: the SAME non-cohort worker-classified tool disposes to Worker once membership is stated, so the in-process answer above is a scoping decision rather than a constant.")]
	public void Resolve_ShouldDisposeToWorker_WhenMembershipIsStatedForANonCohortTool() {
		// Arrange — the only thing that changes is the stated membership.
		McpExecutionRouter routerWithWiderCohort = new(
			_reader, new McpWorkerCohort([WorkerToolCanonicalName]), StdioHostGate(), workerPathWired: true);

		// Act
		McpExecutionRoute route = routerWithWiderCohort.Resolve(WorkerToolCanonicalName, innerCommand: null);

		// Assert
		route.Disposition.Should().Be(McpExecutionDisposition.Worker,
			because: "expanding the cohort is the whole rollout mechanism (ADR §5 / Stage 10) and it must be the ONLY thing this change requires");
		route.ExecutesInProcess.Should().BeFalse(
			because: "a Worker disposition is the one case a dispatch site must NOT run in the host process");
	}

	[Test]
	[Category("Unit")]
	[Description("An in-process-classified tool stays in-process even when a worker path is wired — the metadata, not the wiring, decides the cohort.")]
	public void Resolve_ShouldStayInProcess_WhenToolIsClassifiedInProcessAndWorkerPathIsWired() {
		// Arrange
		McpExecutionRouter routerWithWorkerPath = ProductionShapedRouter();

		// Act
		McpExecutionRoute route = routerWithWorkerPath.Resolve(ExecutorToolName, innerCommand: null);

		// Assert
		route.DeclaredLocation.Should().Be(McpToolExecutionLocation.InProcess,
			because: "clio-run itself resolves and dispatches in the host; only its inner tool can be relayed");
		route.Disposition.Should().Be(McpExecutionDisposition.InProcessByClassification,
			because: "an in-process classification is a decision, not a pending one — it must not be confused with the worker-path-missing case");
	}

	[Test]
	[Category("Unit")]
	[Description("AC-05 over the WHOLE shipped catalog: the set of tools the production router actually relays equals the Stage 6 cohort EXACTLY — no tool outside it changes route, and every member of it does.")]
	public void Resolve_ShouldRelayExactlyTheCohort_OverTheWholeShippedCatalog() {
		// Arrange — every name the metadata reader knows, not a hand-picked sample: the assertion has to be
		// set EQUALITY, because "no unintended route change" is a statement about the complement of the
		// cohort, and a containment check would pass while quietly relaying a deploy tool.
		IReadOnlyList<string> allToolNames = [.. _reader.DeclaredMetadataByToolName.Keys];
		IReadOnlyList<string> workerClassifiedNames = [.. _reader.DeclaredMetadataByToolName
			.Where(pair => pair.Value.Location == McpToolExecutionLocation.Worker)
			.Select(pair => pair.Key)];

		// Act
		IReadOnlyList<McpExecutionRoute> routes =
			[.. allToolNames.Select(name => _sut.Resolve(name, innerCommand: null))];
		IReadOnlySet<string> relayedNames = routes
			.Where(route => !route.ExecutesInProcess)
			.Select(route => route.RoutingKey)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);

		// Assert
		workerClassifiedNames.Should().HaveCountGreaterThan(100,
			because: "the worker CLASSIFICATION is the majority of the 189 tools; if it collapsed, the complement this test guards would be empty and the assertion would prove nothing");
		relayedNames.Should().BeEquivalentTo(new McpWorkerCohort().Names,
			because: "exactly the Stage 6 cohort may relay — a name missing here means the cohort silently did not move, and an extra name means a family whose supervision is story 7 or 8 is already being relayed");
		routes.Where(route => route.DeclaredLocation == McpToolExecutionLocation.Worker)
			.Should().OnlyContain(route => route.Disposition == McpExecutionDisposition.Worker
				|| route.Disposition == McpExecutionDisposition.InProcessOutsideCohort,
			because: "a worker-classified tool either relays or is explicitly named as not-yet-in-cohort; any other disposition would mean the process gate fired on an ordinary stdio host");
		routes.Where(route => route.DeclaredLocation == McpToolExecutionLocation.InProcess)
			.Should().OnlyContain(route => route.Disposition == McpExecutionDisposition.InProcessByClassification,
			because: "an in-process classification is a decision, and it must never be reported through one of the not-yet / gated reasons");
	}

	[Test]
	[Category("Unit")]
	[Description("Every name in the shipped Stage 6 cohort is a real tool whose declared metadata actually supports being relayed today: worker-classified, per-call, bounded by the parent kill.")]
	public void ShippedCohort_ShouldNameOnlyToolsWhoseDeclaredMetadataSupportsRelayingToday() {
		// Arrange
		IReadOnlySet<string> cohortNames = new McpWorkerCohort().Names;

		// Act
		IReadOnlyList<McpToolExecutionMetadata> declared = [.. cohortNames.Select(name => {
			_reader.TryGetMetadata(name, innerCommand: null, out McpToolExecutionMetadata metadata);
			return metadata;
		})];

		// Assert
		cohortNames.Should().HaveCount(7,
			because: "the Stage 6 cohort is the seven retry-safe stdio reads story 6 names; a changed count is a rollout decision and must be made deliberately, not drift in");
		declared.Should().NotContainNulls(
			because: "a cohort name with no declared metadata would be routed on a guess — or, worse, silently fall through the reader's fail-closed unclassified branch and never relay at all");
		declared.Should().OnlyContain(metadata => metadata.Location == McpToolExecutionLocation.Worker,
			because: "the cohort may only name tools the metadata already classifies as worker-bound; naming an in-process tool would make the cohort override the classification instead of scoping it");
		declared.Should().OnlyContain(metadata => metadata.Lifetime == McpToolExecutionLifetime.PerCall,
			because: "a sticky worker is story 7 and the parent has no private completion signal yet, so a sticky member would leak the child");
		declared.Should().OnlyContain(metadata => metadata.BudgetPolicy == McpToolBudgetPolicy.ParentKillDefault,
			because: "the parent kill is the only bound implemented today; a terminal-stage member would be killed mid-deploy, which is exactly what ADR rule 4 forbids");
	}

	[Test]
	[Category("Unit")]
	[Description("An unknown tool name is fail-closed to an explicitly unclassified in-process route rather than throwing or being relayed on a guess.")]
	public void Resolve_ShouldReturnUnclassifiedInProcessRoute_WhenToolIsUnknown() {
		// Arrange
		const string unknownName = "definitely-not-a-clio-tool";

		// Act
		McpExecutionRoute route = _sut.Resolve(unknownName, innerCommand: null);

		// Assert
		route.RoutingKey.Should().Be(unknownName,
			because: "the key the decision was made under must still be reported, so a refusal can name it");
		route.DeclaredLocation.Should().Be(McpToolExecutionLocation.Unspecified,
			because: "an unknown name declares nothing — inventing a location would be routing on a guess");
		route.Disposition.Should().Be(McpExecutionDisposition.InProcessUnclassified,
			because: "fail-closed: an unclassified call is never relayed, and the reason is named rather than folded into the classified case");
		route.Metadata.Should().BeNull(
			because: "there is no metadata row to report for a name no tool declares");
	}

	[Test]
	[Category("Unit")]
	[Description("A null or blank tool name resolves to a null routing key without throwing, so a routing question can never break a call the rest of the pipeline would have rejected with its own error.")]
	public void Resolve_ShouldReturnNullRoutingKey_WhenToolNameIsBlank() {
		// Arrange & Act
		McpExecutionRoute route = _sut.Resolve("   ", innerCommand: null);

		// Assert
		route.RoutingKey.Should().BeNull(
			because: "there is no name to key on, and the reader reports that rather than inventing one");
		route.ExecutesInProcess.Should().BeTrue(
			because: "a blank name must never be relayed — the caller's own missing-name error is the right outcome");
	}

	// ---------------------------------------------------------------------------------------------
	// TC-U-109 — the router must never key on the outer executor name
	// ---------------------------------------------------------------------------------------------

	[Test]
	[Category("Unit")]
	[Description("TC-U-109 (router half): routing clio-run WITH an inner command yields the INNER tool's location; without one it yields the executor's own in-process row, never the inner cohort's.")]
	public void Resolve_ShouldKeyOnInnerCommand_WhenToolIsAnExecutor() {
		// Arrange & Act
		McpExecutionRoute withInner = _sut.Resolve(ExecutorToolName, WorkerToolCanonicalName);
		McpExecutionRoute withoutInner = _sut.Resolve(ExecutorToolName, innerCommand: null);
		McpExecutionRoute destructiveAliasWithInner =
			_sut.Resolve("clio-run-destructive", WorkerToolCanonicalName);

		// Assert
		withInner.RoutingKey.Should().Be(WorkerToolCanonicalName,
			because: "keying on the outer executor name would give the ENTIRE long tail clio-run's in-process row — the exact unbounded wedge this work removes");
		withInner.DeclaredLocation.Should().Be(McpToolExecutionLocation.Worker,
			because: "the inner tool's own cohort decides, not the wrapper's");
		withoutInner.RoutingKey.Should().Be(ExecutorToolName,
			because: "with nothing to unwrap there is nothing else to key on, and the executor itself does run in the host");
		withoutInner.DeclaredLocation.Should().Be(McpToolExecutionLocation.InProcess,
			because: "clio-run is classified in-process, so a command-less executor call is correctly an in-process route");
		destructiveAliasWithInner.RoutingKey.Should().Be(WorkerToolCanonicalName,
			because: "clio-run-destructive runs the same body and must unwrap identically");
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-109 (executor half): a clio-run call with no inner command returns an immediate in-process error and never routes or dispatches anything — never a silent in-process execution.")]
	public async Task RunAsync_ShouldFailImmediatelyWithoutRouting_WhenInnerCommandIsMissing() {
		// Arrange
		IMcpToolInvokerRegistry registry = Substitute.For<IMcpToolInvokerRegistry>();
		IMcpExecutionRouter router = Substitute.For<IMcpExecutionRouter>();
		ClioRunExecutor executor = new(registry, Substitute.For<IMcpToolCompatibilityCatalog>(), router);

		// Act
		CallToolResult result = await executor.RunAsync(
			command: null,
			args: null,
			destructiveSurface: false,
			CallContext(ExecutorToolName),
			CancellationToken.None);

		// Assert
		result.IsError.Should().BeTrue(
			because: "a clio-run call with no target is a caller error and must be answered as one");
		TextOf(result).Should().Contain("'command' is required",
			because: "the caller must be told what to supply, immediately, rather than having something run on its behalf");
		router.DidNotReceiveWithAnyArgs().Resolve(default, default);
		registry.DidNotReceiveWithAnyArgs().TryGetTool(default, out McpServerTool _);
	}

	// ---------------------------------------------------------------------------------------------
	// Dispatch site (c) — clio-run inner
	// ---------------------------------------------------------------------------------------------

	[Test]
	[Category("Unit")]
	[Description("The clio-run dispatch site consults the authority with the UNWRAPPED, alias-canonicalised inner name — not the wrapper's name and not the caller's alias.")]
	public async Task RunAsync_ShouldRouteOnCanonicalInnerName_WhenCallerUsedADeprecatedAlias() {
		// Arrange — the registry misses the alias and hits the canonical, exactly as production does.
		McpServerTool tool = BuildEchoTool();
		IMcpToolInvokerRegistry registry = Substitute.For<IMcpToolInvokerRegistry>();
		registry.TryGetTool(WorkerToolDeprecatedAlias, out Arg.Any<McpServerTool>()).Returns(false);
		registry.TryGetTool(WorkerToolCanonicalName, out Arg.Any<McpServerTool>())
			.Returns(callInfo => { callInfo[1] = tool; return true; });
		IMcpToolCompatibilityCatalog catalog = Substitute.For<IMcpToolCompatibilityCatalog>();
		catalog.TryResolveAlias(WorkerToolDeprecatedAlias, out Arg.Any<string>(), out Arg.Any<McpToolCompatibilityEntry>())
			.Returns(callInfo => { callInfo[1] = WorkerToolCanonicalName; return true; });
		IMcpExecutionRouter router = InProcessRouterStub();
		ClioRunExecutor executor = new(registry, catalog, router);

		// Act
		await executor.RunAsync(
			WorkerToolDeprecatedAlias,
			EchoArgs(),
			destructiveSurface: false,
			CallContext(ExecutorToolName),
			CancellationToken.None);

		// Assert
		router.Received(1).Resolve(WorkerToolCanonicalName, null);
		router.DidNotReceive().Resolve(WorkerToolDeprecatedAlias, Arg.Any<string>());
		router.DidNotReceive().Resolve(ExecutorToolName, Arg.Any<string>());
	}

	[Test]
	[Category("Unit")]
	[Description("When the authority routes a clio-run inner call to a worker and no worker path exists, the site refuses instead of dispatching in-process.")]
	public async Task RunAsync_ShouldRefuseWithoutDispatching_WhenRouteIsWorker() {
		// Arrange
		McpServerTool tool = BuildEchoTool();
		IMcpToolInvokerRegistry registry = Substitute.For<IMcpToolInvokerRegistry>();
		registry.TryGetTool(EchoToolName, out Arg.Any<McpServerTool>())
			.Returns(callInfo => { callInfo[1] = tool; return true; });
		ClioRunExecutor executor = new(registry, Substitute.For<IMcpToolCompatibilityCatalog>(), WorkerRouterStub());

		// Act
		CallToolResult result = await executor.RunAsync(
			EchoToolName,
			EchoArgs(),
			destructiveSurface: false,
			CallContext(ExecutorToolName),
			CancellationToken.None);

		// Assert
		result.IsError.Should().BeTrue(
			because: "a call routed to a destination that does not exist must fail loudly, not fall back into the host process");
		TextOf(result).Should().Contain("no worker path wired",
			because: "the refusal names why, so the seam Stage 6 replaces is identifiable from the transcript alone");
		TextOf(result).Should().NotContain("echo:",
			because: "the tool must NOT have run — a silent in-process execution is exactly the failure this branch prevents");
		registry.DidNotReceive().IsRetrySafe(Arg.Any<string>());
	}

	// ---------------------------------------------------------------------------------------------
	// Dispatch site (b) — the unmatched durable handler
	// ---------------------------------------------------------------------------------------------

	[Test]
	[Category("Unit")]
	[Description("TC-U-403: an unmatched WRITE-capable tool hits the write-capability confirmation gate FIRST, and a refused confirmation prevents routing and dispatch entirely.")]
	public async Task HandleAsync_ShouldNotRouteOrDispatch_WhenWriteCapableToolIsRefusedByConfirmationGate() {
		// Arrange — a write-capable (not read-only) tool: the gate keys on readOnlyHint, so this covers
		// additive writes too (issue #953).
		DurableHandlerHarness harness = new();
		harness.RegisterTool(EchoToolName, readOnly: false);

		// Act
		CallToolResult result = await harness.Handler.HandleAsync(
			CallContext(EchoToolName), CancellationToken.None);

		// Assert
		result.IsError.Should().BeTrue(
			because: "an unadvertised write cannot raise the host's own confirmation prompt, so it is refused with a retry shape");
		TextOf(result).Should().Contain(ClioRunTool.ToolName,
			because: "the refusal must point at the host-gated executor, which is the confirmation seam itself");
		harness.Router.DidNotReceiveWithAnyArgs().Resolve(default, default);
		await harness.Executor.DidNotReceiveWithAnyArgs()
			.InvokeResolvedAsync(default, default, default, default);
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-403 (ordering): on the admitted read path the confirmation gate is consulted BEFORE the router, so routing can never precede host gating.")]
	public async Task HandleAsync_ShouldConsultConfirmationGateBeforeRouter_WhenToolIsReadOnly() {
		// Arrange
		DurableHandlerHarness harness = new();
		harness.RegisterTool(EchoToolName, readOnly: true);

		// Act
		await harness.Handler.HandleAsync(CallContext(EchoToolName), CancellationToken.None);

		// Assert — asserted on CALL ORDER, not by reading the source: routing a write before the gate would
		// execute it in a worker and bypass host gating (ADR rule 9).
		Received.InOrder(() => {
			harness.Registry.IsReadOnly(EchoToolName);
			harness.Router.Resolve(EchoToolName, null);
		});
	}

	[Test]
	[Category("Unit")]
	[Description("When the authority routes an unmatched call to a worker and no worker path exists, the durable handler refuses instead of dispatching in-process.")]
	public async Task HandleAsync_ShouldRefuseWithoutDispatching_WhenRouteIsWorker() {
		// Arrange
		DurableHandlerHarness harness = new(WorkerRouterStub());
		harness.RegisterTool(EchoToolName, readOnly: true);

		// Act
		CallToolResult result = await harness.Handler.HandleAsync(
			CallContext(EchoToolName), CancellationToken.None);

		// Assert
		TextOf(result).Should().Contain("no worker path wired",
			because: "the unmatched seam must refuse a worker-routed call for the same reason the other two do");
		await harness.Executor.DidNotReceiveWithAnyArgs()
			.InvokeResolvedAsync(default, default, default, default);
	}

	[Test]
	[Category("Unit")]
	[Description("The durable handler asserts its 'unmatched only' precondition: a MATCHED primitive is an SDK-contract violation and fails fast rather than being routed a second time.")]
	public async Task HandleAsync_ShouldThrow_WhenPrimitiveIsAlreadyMatched() {
		// Arrange — the invariant the router depends on was prose-only until now; a matched call here would
		// mean dispatch site (a) already decided this call.
		DurableHandlerHarness harness = new();
		RequestContext<CallToolRequestParams> context = CallContext(EchoToolName);
		context.MatchedPrimitive = BuildEchoTool();

		// Act
		Func<Task> act = async () =>
			await harness.Handler.HandleAsync(context, CancellationToken.None);

		// Assert
		await act.Should().ThrowAsync<InvalidOperationException>(
			because: "a broken precondition is a programming error, not a tool outcome — it must not be returned as a structured result");
	}

	// ---------------------------------------------------------------------------------------------
	// Dispatch site (a) — the matched filter
	// ---------------------------------------------------------------------------------------------

	[Test]
	[Category("Unit")]
	[Description("The matched filter consults the authority with the matched tool's own protocol name and continues the pipeline when the route executes in-process.")]
	public async Task Filter_ShouldRouteOnMatchedToolNameAndContinue_WhenRouteIsInProcess() {
		// Arrange
		IMcpExecutionRouter router = InProcessRouterStub();
		bool nextCalled = false;
		RequestContext<CallToolRequestParams> context = MatchedContext(router);

		// Act
		CallToolResult result = await McpToolErrorFilter.HandleCallToolErrors(
			(_, _) => { nextCalled = true; return ValueTask.FromResult(new CallToolResult()); })(
			context, CancellationToken.None);

		// Assert
		router.Received(1).Resolve(EchoToolName, null);
		nextCalled.Should().BeTrue(
			because: "an in-process route must leave the matched pipeline byte-identical to before the router existed");
		result.IsError.Should().NotBe(true,
			because: "nothing about an in-process decision turns a healthy call into an error");
	}

	[Test]
	[Category("Unit")]
	[Description("The matched filter refuses a worker-routed call instead of continuing the in-process pipeline.")]
	public async Task Filter_ShouldRefuseWithoutContinuing_WhenRouteIsWorker() {
		// Arrange
		bool nextCalled = false;
		RequestContext<CallToolRequestParams> context = MatchedContext(WorkerRouterStub());

		// Act
		CallToolResult result = await McpToolErrorFilter.HandleCallToolErrors(
			(_, _) => { nextCalled = true; return ValueTask.FromResult(new CallToolResult()); })(
			context, CancellationToken.None);

		// Assert
		nextCalled.Should().BeFalse(
			because: "continuing would execute a worker-routed tool in the host process, silently");
		TextOf(result).Should().Contain("no worker path wired",
			because: "all three dispatch sites must produce ONE refusal shape — three hand-rolled copies drift");
	}

	[Test]
	[Category("Unit")]
	[Description("An UNMATCHED name is deliberately left alone by the matched filter: it has no canonical yet and its confirmation gate has not run, so dispatch site (b) routes it instead.")]
	public async Task Filter_ShouldNotRoute_WhenPrimitiveIsUnmatched() {
		// Arrange — MatchedPrimitive stays null, as it is for every unmatched name.
		IMcpExecutionRouter router = WorkerRouterStub();
		bool nextCalled = false;
		RequestContext<CallToolRequestParams> context = CallContext("some-unmatched-name");
		context.Services = ProviderWith(router);

		// Act
		await McpToolErrorFilter.HandleCallToolErrors(
			(_, _) => { nextCalled = true; return ValueTask.FromResult(new CallToolResult()); })(
			context, CancellationToken.None);

		// Assert
		router.DidNotReceiveWithAnyArgs().Resolve(default, default);
		nextCalled.Should().BeTrue(
			because: "routing an unresolved alias here would key on the wrong name and miss — the durable handler routes it after resolution and after the gate");
	}

	[Test]
	[Category("Unit")]
	[Description("The matched filter is FAIL-CLOSED when the routing authority cannot be resolved: it refuses and names the wiring defect instead of silently continuing in-process, so it cannot drift back into the asymmetry its two constructor-injected siblings do not have.")]
	public async Task Filter_ShouldRefuseAndNameTheDefect_WhenNoRouterIsReachable() {
		// Arrange — the matched site is a static delegate, so it SERVICE-LOCATES the router; its two
		// siblings take it by constructor injection and cannot be built without it. A silent fallthrough
		// here would mean "run everything in the host process" the moment the relay is wired, and nothing
		// would say so. No real host reaches this branch: the router is registered on the transport-neutral
		// RegisterInto path, so stdio, mcp-http and the per-request tenant containers all resolve it.
		bool nextCalled = false;
		RequestContext<CallToolRequestParams> context = CallContext(EchoToolName);
		context.MatchedPrimitive = BuildEchoTool();

		// Act
		CallToolResult result = await McpToolErrorFilter.HandleCallToolErrors(
			(_, _) => { nextCalled = true; return ValueTask.FromResult(new CallToolResult()); })(
			context, CancellationToken.None);

		// Assert
		nextCalled.Should().BeFalse(
			because: "continuing here is the silent in-process fallthrough this feature exists to remove — if this flips back to `return false`, this assertion is what fails");
		result.IsError.Should().BeTrue(
			because: "an unroutable call must be answered as a failure rather than executed on a guess about where it belongs");
		TextOf(result).Should().Contain(nameof(IMcpExecutionRouter),
			because: "the refusal must name the missing authority, so a wiring defect is diagnosable from the transcript alone rather than showing up as tools that quietly stopped being relayed");
		TextOf(result).Should().Contain(EchoToolName,
			because: "the refusal must name the tool that was not executed");
	}

	// ---------------------------------------------------------------------------------------------
	// Fixtures
	// ---------------------------------------------------------------------------------------------

	private const string EchoToolName = "echo-tool";

	// A real SDK-built tool over a static echo method, so a resolved McpServerTool exists (and can be
	// invoked) without a live server.
	[McpServerToolType]
	private static class EchoToolType {
		[McpServerTool(Name = EchoToolName, Destructive = false)]
		[System.ComponentModel.Description("Echoes its input back.")]
		public static string Echo([System.ComponentModel.Description("payload")] string value) => $"echo:{value}";
	}

	private static McpServerTool BuildEchoTool() => BuildEchoTool(EchoToolName);

	// The same echo body published under an arbitrary PROTOCOL NAME, so the matched dispatch site — which
	// keys on tool.ProtocolTool.Name — can be driven with a real production tool name.
	private static McpServerTool BuildEchoTool(string protocolName) =>
		McpServerTool.Create(
			typeof(EchoToolType).GetMethod(nameof(EchoToolType.Echo))!,
			target: null,
			new McpServerToolCreateOptions {
				Name = protocolName,
				SerializerOptions = JsonSerializerOptions.Default
			});

	private static JsonElement EchoArgs() =>
		JsonSerializer.SerializeToElement(new Dictionary<string, string> { ["value"] = "x" });

	private static RequestContext<CallToolRequestParams> CallContext(
		string toolName, Dictionary<string, JsonElement> arguments = null) =>
		McpRequestContextTestFactory.CreateCallToolContext(toolName, arguments);

	// A matched-path context: the echo tool as MatchedPrimitive plus a provider carrying the router, which
	// is how the static filter reaches the authority at runtime.
	private static RequestContext<CallToolRequestParams> MatchedContext(IMcpExecutionRouter router) {
		RequestContext<CallToolRequestParams> context = CallContext(EchoToolName);
		context.MatchedPrimitive = BuildEchoTool();
		context.Services = ProviderWith(router);
		return context;
	}

	private static IServiceProvider ProviderWith(IMcpExecutionRouter router) =>
		new ServiceCollection().AddSingleton(router).BuildServiceProvider();

	// A router that answers "runs in the host process" for every name, mirroring the production decision
	// while no worker path is wired.
	private static IMcpExecutionRouter InProcessRouterStub() => RouterStub(McpExecutionDisposition.InProcessUnclassified);

	// A router that routes everything to a worker — the decision production cannot currently produce, and
	// the only way to exercise each site's refusal branch.
	private static IMcpExecutionRouter WorkerRouterStub() => RouterStub(McpExecutionDisposition.Worker);

	private static IMcpExecutionRouter RouterStub(McpExecutionDisposition disposition) {
		IMcpExecutionRouter router = Substitute.For<IMcpExecutionRouter>();
		router.Resolve(Arg.Any<string>(), Arg.Any<string>()).Returns(callInfo => new McpExecutionRoute(
			callInfo.ArgAt<string>(0),
			disposition == McpExecutionDisposition.Worker
				? McpToolExecutionLocation.Worker
				: McpToolExecutionLocation.Unspecified,
			disposition,
			Metadata: null));
		return router;
	}

	private static string TextOf(CallToolResult result) =>
		string.Join(" ", (result.Content ?? []).OfType<TextContentBlock>().Select(block => block.Text));

	// The unmatched-path system under test with its three collaborators plus the routing authority, so the
	// ordering assertions can be written against real substitutes rather than a hand-built graph per test.
	private sealed class DurableHandlerHarness {

		internal DurableHandlerHarness(IMcpExecutionRouter router = null) {
			Registry = Substitute.For<IMcpToolInvokerRegistry>();
			Catalog = Substitute.For<IMcpToolCompatibilityCatalog>();
			Executor = Substitute.For<IClioRunExecutor>();
			Router = router ?? InProcessRouterStub();
			Executor.InvokeResolvedAsync(
					Arg.Any<McpServerTool>(),
					Arg.Any<string>(),
					Arg.Any<RequestContext<CallToolRequestParams>>(),
					Arg.Any<CancellationToken>())
				.Returns(new ValueTask<CallToolResult>(new CallToolResult()));
			Handler = new McpDurableCallToolHandler(Registry, Catalog, Executor, Router);
		}

		internal IMcpToolInvokerRegistry Registry { get; }

		internal IMcpToolCompatibilityCatalog Catalog { get; }

		internal IClioRunExecutor Executor { get; }

		internal IMcpExecutionRouter Router { get; }

		internal McpDurableCallToolHandler Handler { get; }

		internal void RegisterTool(string toolName, bool readOnly) {
			McpServerTool tool = BuildEchoTool();
			Registry.TryGetTool(toolName, out Arg.Any<McpServerTool>())
				.Returns(callInfo => { callInfo[1] = tool; return true; });
			Registry.IsReadOnly(toolName).Returns(readOnly);
		}
	}
}
