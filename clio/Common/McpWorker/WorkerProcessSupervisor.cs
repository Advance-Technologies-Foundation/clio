using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Clio.Common.McpWorker;

/// <inheritdoc />
public sealed class WorkerProcessSupervisor : IWorkerProcessSupervisor, IWorkerProcessInspector {

	/// <summary>
	/// Ambient variables copied into a worker's cleared environment. Everything else is dropped, so a
	/// stray variable in the parent's environment cannot contradict the frozen payload the worker is
	/// launched with (ADR rule 11).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The governing principle: a worker must behave the way the PARENT was configured to behave.</b>
	/// An operator who tunes a variable is tuning clio, not one process of it, so a knob that lands in the
	/// parent only produces two clios that disagree — and the disagreement is invisible, because the parent
	/// is the one the operator can observe. The list is nevertheless an allowlist and not "inherit
	/// everything": three classes are deliberately kept OUT, and they are enumerated below so an omission
	/// reads as a decision rather than as an oversight.
	/// </para>
	/// <para>
	/// <b>Scope of the audit.</b> A variable earns a place here when it changes the behaviour of code a
	/// worker can actually reach — <c>mcp-server --worker</c> serving <c>tools/call</c> for a
	/// <see cref="Command.McpServer.McpToolExecutionLocation.Worker"/> tool, plus the in-process
	/// <c>clio-run</c> dispatch a worker performs. Variables read only by CLI-only commands that no worker
	/// path reaches are recorded as out of scope, to be revisited when the cohort expands, rather than
	/// added speculatively.
	/// </para>
	/// <para>
	/// <b>Runtime location — <c>DOTNET_ROOT*</c>.</b> Not decoration. It is how a .NET apphost — which is
	/// what a published clio and the test fixture both are — finds the shared runtime when that runtime is
	/// not at the machine's default location, and the variable that carries it is ARCHITECTURE-SPECIFIC:
	/// measured on an arm64 macOS host, only <c>DOTNET_ROOT_ARM64</c> was set, and dropping it made a
	/// spawned worker fail at startup with "You must install or update .NET" before executing a line of
	/// clio. A frozen environment that omits these turns a working host into one where every worker dies
	/// instantly, so this family carries every spelling the host may have used.
	/// </para>
	/// <para>
	/// <b>Egress — the proxy family, and the flag that decides whether it is honoured.</b> The proxy
	/// variables are here under the same "every spelling" rule: where egress goes through a mandated
	/// inspecting proxy, the parent honours <c>HTTPS_PROXY</c> and a child that does not inherit it either
	/// cannot reach Creatio at all or reaches it around the policy — and both present to the user as "the
	/// environment is broken". The lowercase spellings are NOT duplicates of the uppercase ones: on Unix
	/// they are the conventional spelling, plenty of stacks read them case-sensitively, and a host may have
	/// set only those. <c>CLIO_MCP_RESPECT_AMBIENT_PROXY</c> belongs with them because it decides whether an
	/// MCP process honours those variables at all (<c>Program.cs</c>, MCP mode clears
	/// <c>HttpClient.DefaultProxy</c> unless the flag is set): inheriting the proxy ADDRESS without the flag
	/// is the worst of both worlds — the operator's parent goes through the mandated proxy and the worker
	/// goes straight around it, which is precisely the policy bypass the paragraph above warns about.
	/// </para>
	/// <para>
	/// <b>Host behaviour a worker must match.</b> <c>CLIO_MCP_HEARTBEAT_INTERVAL_SECONDS</c> sets the
	/// cadence at which a tool streams <c>notifications/progress</c> while a synchronous backend call runs.
	/// It is captured at TYPE LOAD (<c>McpProgressHeartbeat.DefaultInterval</c> is <c>static readonly</c>),
	/// so a worker that does not receive it at spawn can never be told afterwards — and a host tuned for a
	/// fast beat then gets a parent that beats and a worker that does not.
	/// <c>CLIO_WORKING_DIRECTORY</c> relocates clio's scratch root
	/// (<c>WorkingDirectoriesProvider.BaseTempDirectory</c>), so a worker without it writes temporary trees
	/// under the machine default while the parent writes them where the operator put them — and
	/// <c>TEMP</c> / <c>TMP</c> / <c>TMPDIR</c> are already inherited, so withholding only the clio-specific
	/// one is the inconsistent half of a pair. Checked rather than assumed: sharing the root is safe because
	/// every allocation is a fresh <c>Guid</c> subdirectory, and
	/// <c>WarmUpPackageDownloader.CreateOwnerPrivateDirectory</c> opts OUT of this variable on purpose — that
	/// opt-out is a privacy guarantee that holds in parent and worker alike, so it is not an argument against
	/// inheriting the variable for everything else.
	/// <c>CLIO_CREATE_SECTION_TIMEOUT_SECONDS</c> bounds the <c>create-app-section</c> insert, and that tool
	/// is <see cref="Command.McpServer.McpToolExecutionLocation.Worker"/>, so an operator's tuned timeout
	/// would otherwise apply only while the tool still runs in-process.
	/// The component / request registry overrides pin where component documentation is read from;
	/// <c>get-component-info</c> is a worker-located tool, so a worker that ignores the pin reaches the
	/// public CDN the operator redirected away from and answers out of different data.
	/// <c>PATHEXT</c> is the other half of <c>PATH</c>: on Windows an executable is resolved as PATH ×
	/// PATHEXT, so inheriting one without the other resolves a different program, or none.
	/// </para>
	/// <para>
	/// <b>Exclusion class 1 — parent-side orchestration and hazards (ADR rule 11).</b>
	/// <c>CLIO_MCP_READ_DEADLINE_SECONDS</c> must never reach a worker: the parent bounds a worker by
	/// KILLING it, and a second in-child deadline would abandon the work while keeping the per-tenant
	/// monitor, which is the wedge this feature exists to remove.
	/// <c>CLIO_MCP_WORKER_FROZEN_FEATURES</c> is composed per call by
	/// <see cref="McpWorkerEnvironment.ComposeChildEnvironment"/>; an ambient copy could contradict the
	/// frozen payload.
	/// <c>CLIO_MCP_WORKER_BUDGET_SECONDS</c>, <c>CLIO_MCP_WORKER_QUEUE_WAIT_SECONDS</c> and
	/// <c>CLIO_MCP_WORKER_CONCURRENCY</c> configure the SUPERVISOR, which only the parent runs; a worker
	/// spawns no workers, and carrying them down would arm a second supervisor the day one ever did. The
	/// concurrency cap joins its two siblings here for exactly that reason and for no other: it is an
	/// admission knob, admission happens once, and it happens in the parent.
	/// </para>
	/// <para>
	/// <b>NOT an exclusion, however it looks — <c>CLIO_MCP_RESPONSE_DEADLINE_SECONDS</c> is DELEGATED.</b>
	/// It is absent from this list because it must reach SOME workers and not others, and an allowlist can
	/// only say "always". <see cref="McpWorkerEnvironment.ComposeChildEnvironment"/> owns the decision and
	/// makes it per lifetime: a STICKY worker <b>keeps the parent's value verbatim</b> — its in-progress
	/// envelope is what returns the call, and stripping it turned a 25 s backend call into a 77 s block in
	/// the prototype (ADR rule 11) — while a per-call worker gets no deadline override at all, because the
	/// parent bounds it by killing. So the absence here is what ENABLES that asymmetry; adding the variable
	/// to this list would silently promote "sometimes" to "always" and is not the way to fix a sticky worker
	/// that is missing its deadline.
	/// </para>
	/// <para>
	/// <b>Exclusion class 2 — secrets and transport-only configuration.</b> The credential threat model's
	/// stdio property is that NO secret crosses the parent→worker channel: the child reads
	/// <c>appsettings.json</c> itself and is handed only an environment NAME. So
	/// <c>CLIO_OIDC_CLIENT_SECRET</c>, <c>CLIO_MCP_HTTP_PLATFORM_API_KEY</c>, <c>CLIO_TELEMETRY_INGEST_KEY</c>
	/// and the <c>CLIO_KNOWLEDGE_TRUSTED_*</c> trust anchors stay out. The whole <c>CLIO_MCP_HTTP_AUTH*</c>
	/// family configures the parent's HTTP transport, which no worker serves — the worker path is
	/// stdio-only until Stage 5 is revived (ADR §5, OQ-9).
	/// </para>
	/// <para>
	/// <b>Exclusion class 3 — variables whose ABSENCE is the correct worker behaviour.</b> <c>TERM</c> and
	/// <c>NO_COLOR</c> are the clearest case and the decision is a positive one, not an omission: clio
	/// enables ANSI colour only when <c>TERM</c> is present, a worker's standard output IS the MCP protocol
	/// stream, and inheriting <c>TERM</c> would re-enable escape sequences in the one process that must
	/// never emit them on stdout. The cleared environment already gives the right answer, so the right move
	/// is to leave it cleared. <c>CLIO_NO_UPDATE_CHECK</c> is a second case: a worker is <c>mcp-server</c>,
	/// and <c>Program.ShouldSkipUpdateCheck</c> returns true for MCP server mode before it ever reads the
	/// variable, so parent and worker already agree. (Revisit if a worker ever spawns a grandchild clio in
	/// ordinary CLI mode.)
	/// </para>
	/// <para>
	/// <b>Out of scope while the cohort is Stage 6, revisit on expansion.</b> The telemetry family
	/// (<c>CLIO_TELEMETRY_ENABLED</c> / <c>_ENDPOINT</c> / <c>_HOME</c>) — a worker runs no host bootstrap
	/// and therefore no flush or drain (ADR rule 11), and <c>send-telemetry</c> is
	/// <see cref="Command.McpServer.McpToolExecutionLocation.InProcess"/>, so nothing in a worker writes the
	/// outbox. If that tool is ever worker-routed, <c>_HOME</c> and <c>_ENABLED</c> move onto this list
	/// immediately: a mismatched home silently loses events and a missing opt-out violates it.
	/// The curated-knowledge overrides (<c>CLIO_KNOWLEDGE_CURATED_API_BASE_URL</c>,
	/// <c>CLIO_KNOWLEDGE_NUGET_*</c>) — the bootstrap they configure is host bootstrap, which a worker does
	/// not run. And the infrastructure / container family (<c>CLIO_DEBUG_IIS</c>,
	/// <c>KUBERNETES_SERVICE_HOST</c>, <c>XDG_RUNTIME_DIR</c>, the BuildKit host) — read only by
	/// install / deploy / docker commands, none of them reachable from a worker-located tool today.
	/// </para>
	/// </remarks>
	public static readonly IReadOnlyCollection<string> DefaultInheritedEnvironmentVariableAllowlist = [
		"PATH",
		"PATHEXT",
		"HOME",
		"USERPROFILE",
		"LOCALAPPDATA",
		"APPDATA",
		"SystemRoot",
		"SystemDrive",
		"windir",
		"COMSPEC",
		"TEMP",
		"TMP",
		"TMPDIR",
		"DOTNET_ROOT",
		"DOTNET_ROOT_ARM64",
		"DOTNET_ROOT_X64",
		"DOTNET_ROOT_X86",
		"DOTNET_ROOT(x86)",
		"DOTNET_HOST_PATH",
		"CLIO_HOME",
		"CLIO_WORKING_DIRECTORY",
		"LANG",
		"LC_ALL",
		"HTTP_PROXY",
		"HTTPS_PROXY",
		"NO_PROXY",
		"http_proxy",
		"https_proxy",
		"no_proxy",
		// TLS trust roots. Same argument the proxy spellings won, one layer down: an installation that
		// trusts a private Creatio CA through SSL_CERT_FILE / SSL_CERT_DIR (the OpenSSL convention .NET
		// honours on Linux) configures the PARENT that way. Clearing the child's environment and copying
		// only this list removes that trust, so the parent connects and every worker-routed call fails
		// certificate validation instead — a failure that reads as "the environment is unreachable" while
		// the CLI against the same stand works. Neither variable carries a secret: they are paths to
		// public certificates.
		"SSL_CERT_FILE",
		"SSL_CERT_DIR",
		// WHICH CLUSTER a deployment targets. `deploy-creatio` became worker-routed at stage 8, and
		// InstallerCommand — the command it runs — takes IKubernetes as a constructor dependency, built
		// through KubernetesClientConfiguration.BuildConfigFromConfigFile(), which honours KUBECONFIG. An
		// operator who selects a non-default context therefore gets it in the parent and NOT in the worker,
		// so the worker silently falls back to the default kubeconfig: a destructive operation aimed at a
		// cluster nobody chose, or a valid deployment rejected because the default context cannot see it.
		// It is a path to a config file, not a secret.
		"KUBECONFIG",
		"CLIO_MCP_RESPECT_AMBIENT_PROXY",
		"CLIO_MCP_HEARTBEAT_INTERVAL_SECONDS",
		"CLIO_CREATE_SECTION_TIMEOUT_SECONDS",
		"CLIO_COMPONENT_REGISTRY_CDN_BASE_URL",
		"CLIO_COMPONENT_REGISTRY_LOCAL_FILE",
		"CLIO_MOBILE_COMPONENT_REGISTRY_LOCAL_FILE",
		"CLIO_REQUEST_REGISTRY_LOCAL_FILE",
		"CLIO_MOBILE_REQUEST_REGISTRY_LOCAL_FILE",
		"CLIO_WEB_TO_MOBILE_PAGE_CONVERSION_RULES_LOCAL_FILE"
	];

	/// <summary>
	/// Environment variable overriding <see cref="DefaultQueueWaitBound"/>, in seconds (invariant
	/// culture, accepted range 0 &lt; n ≤ 3600).
	/// </summary>
	/// <remarks>
	/// Separate from <c>CLIO_MCP_WORKER_BUDGET_SECONDS</c>, which bounds a worker that is RUNNING. The
	/// two answer different questions and a caller has to be able to tell which one it hit, so they are
	/// configured separately as well as reported separately.
	/// </remarks>
	internal const string QueueWaitOverrideEnvVar = "CLIO_MCP_WORKER_QUEUE_WAIT_SECONDS";

	/// <summary>
	/// Environment variable overriding the total worker concurrency cap (whole workers, accepted range
	/// 0 &lt; n ≤ <see cref="MaximumConfigurableConcurrencyCap"/>).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The cap has to be configurable, and Stage 7 is the point at which it stops being optional</b>
	/// (threat model T-9, gap G-1). While every worker was per-call, the default derived from the core
	/// count was right on every host and an operator only ever needed to change how long callers WAIT.
	/// Sticky workers hold a slot for a whole operation, so the number of slots becomes the number of
	/// operations a host can run — an over-subscribed host needs to lower it, a large host wants to raise
	/// it, and a single-slot host must raise it to run any long operation at all
	/// (<see cref="StickyConcurrencyCap"/> is zero there by derivation).
	/// </para>
	/// <para>
	/// <b>One variable, not two.</b> The sticky cap is DERIVED from this
	/// (<see cref="DeriveStickyConcurrencyCap"/>) rather than configured beside it: two independent knobs
	/// would let an operator set a sticky cap equal to or above the total and reintroduce precisely the
	/// exhaustion the split removes, and the relationship — not just each range — would then need
	/// clamping.
	/// </para>
	/// <para>
	/// Deliberately absent from
	/// <see cref="DefaultInheritedEnvironmentVariableAllowlist"/>, with its two supervisor siblings and
	/// for their reason: a worker spawns no workers, so an admission knob has nothing to configure inside
	/// one.
	/// </para>
	/// </remarks>
	internal const string ConcurrencyCapOverrideEnvVar = "CLIO_MCP_WORKER_CONCURRENCY";

	/// <summary>
	/// Largest cap <see cref="ConcurrencyCapOverrideEnvVar"/> accepts; anything above falls back to the
	/// core-count default.
	/// </summary>
	/// <remarks>
	/// Derived from ADR §2.4 rather than picked as a round number. On the four-core Windows stand nothing
	/// was gained above ~4 — wall time grows linearly past the core count — and width 16 already produced
	/// a 1073 MB working set and a 16.9 s queue wait on a HEALTHY backend. 64 is therefore far past any
	/// measured benefit even on a very large host, while still being low enough that a mistyped value
	/// cannot fork hundreds of clio processes on a machine that has no cores to run them.
	/// </remarks>
	internal const int MaximumConfigurableConcurrencyCap = 64;

	/// <summary>
	/// How long a call may wait for a concurrency slot before it is refused with
	/// <see cref="WorkerQueueWaitExpiredException"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>60 s, from the measurements in ADR §2.4 rather than from taste.</b> The bound has to clear the
	/// worst HEALTHY queue wait ever measured: at concurrency width 16 on the four-core Windows stand a
	/// perfectly healthy call waited <b>16.9 s</b> just to reach <c>initialize</c> — four times
	/// oversubscribed, with a responsive backend. A bound anywhere near that would refuse calls for
	/// being busy, which is the failure mode the spawn-anchored budget already exists to avoid. 60 s is
	/// roughly 3.5× it, so an ordinarily busy host queues and succeeds.
	/// </para>
	/// <para>
	/// The upper end is set by the client, not by us: 60 s of queueing plus the 120 s default response
	/// budget is 180 s, which is about the hard ceiling an MCP client gives a single call before it
	/// abandons it. Anything larger and clio's own answer arrives after the client has stopped
	/// listening — the caller learns nothing, which is the condition this bound exists to end.
	/// </para>
	/// <para>
	/// <b>Read this together with <see cref="ConcurrencyCap"/>.</b> The cap is a shared, HELD resource:
	/// a slot is taken at spawn and returned at lease dispose, so any worker that lives longer than the
	/// answer it produced occupies capacity for its whole life. With a four-slot cap, four such holders
	/// are enough to send every other call into this queue — bounded, named and reported here rather
	/// than silently waiting, but still queued.
	/// </para>
	/// </remarks>
	public static readonly TimeSpan DefaultQueueWaitBound = TimeSpan.FromSeconds(60);

	private static readonly TimeSpan TerminationConfirmationTimeout = TimeSpan.FromSeconds(5);

	private readonly ILogger _logger;
	private readonly IProcessExecutor _processExecutor;
	private readonly IProcessContainment _containment;
	private readonly IClioExecutablePathProvider _executablePathProvider;
	private readonly IStaleWorkerRegistry _registry;
	// ONE pool for the whole host, plus a ceiling on how much of it sticky work may hold. See the
	// construction site for why this is a ceiling and not a partition.
	private readonly WorkerSlotPool _pool;
	private int _activeStickyWorkers;
	private readonly ProcessIdentitySnapshot _ownerIdentity;

	private int _activeWorkers;
	private int _peakActiveWorkers;
	private long _totalSpawned;
	private long _totalTerminated;
	private long _totalStaleReaped;

	/// <summary>
	/// Initializes a new instance of the <see cref="WorkerProcessSupervisor"/> class with a concurrency
	/// cap derived from the machine's processor count.
	/// </summary>
	/// <param name="logger">Logger for containment and cleanup diagnostics.</param>
	/// <param name="processExecutor">
	/// The ordinary process executor, which serves the four inherited <see cref="IProcessExecutor"/>
	/// members. Worker spawning never goes through it — see the interface remarks for why it cannot.
	/// </param>
	/// <param name="containment">Platform containment for spawned workers.</param>
	/// <param name="executablePathProvider">Resolves how to re-launch this clio build.</param>
	/// <param name="registry">On-disk record of live workers, used for stale cleanup.</param>
	public WorkerProcessSupervisor(ILogger logger, IProcessExecutor processExecutor,
		IProcessContainment containment, IClioExecutablePathProvider executablePathProvider,
		IStaleWorkerRegistry registry)
		: this(logger, processExecutor, containment, executablePathProvider, registry,
			ResolveConcurrencyCap(System.Environment.GetEnvironmentVariable(ConcurrencyCapOverrideEnvVar)),
			ResolveQueueWaitBound(System.Environment.GetEnvironmentVariable(QueueWaitOverrideEnvVar))) {
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="WorkerProcessSupervisor"/> class with an explicit
	/// concurrency cap. Used by tests, which must observe queueing without spawning one worker per core.
	/// </summary>
	/// <param name="logger">Logger for containment and cleanup diagnostics.</param>
	/// <param name="processExecutor">Executor serving the inherited members.</param>
	/// <param name="containment">Platform containment for spawned workers.</param>
	/// <param name="executablePathProvider">Resolves how to re-launch this clio build.</param>
	/// <param name="registry">On-disk record of live workers.</param>
	/// <param name="concurrencyCap">Explicit cap; processor count when null.</param>
	/// <param name="queueWaitBound">
	/// Explicit queue-wait bound; <see cref="DefaultQueueWaitBound"/> when null. Stated rather than read
	/// from the environment so a test can bound a queued call without mutating process-wide state.
	/// </param>
	internal WorkerProcessSupervisor(ILogger logger, IProcessExecutor processExecutor,
		IProcessContainment containment, IClioExecutablePathProvider executablePathProvider,
		IStaleWorkerRegistry registry, int? concurrencyCap, TimeSpan? queueWaitBound = null) {
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
		_processExecutor = processExecutor ?? throw new ArgumentNullException(nameof(processExecutor));
		_containment = containment ?? throw new ArgumentNullException(nameof(containment));
		_executablePathProvider = executablePathProvider
			?? throw new ArgumentNullException(nameof(executablePathProvider));
		_registry = registry ?? throw new ArgumentNullException(nameof(registry));
		// The cap is core-count derived because wall time grows linearly past the core count: a wider
		// cap buys no throughput and only inflates per-call latency (ADR section 2.4). Memory is not the
		// binding constraint — CPU is.
		ConcurrencyCap = Math.Max(1, concurrencyCap ?? System.Environment.ProcessorCount);
		QueueWaitBound = queueWaitBound ?? DefaultQueueWaitBound;
		// ONE pool of `ConcurrencyCap` slots, and a CEILING on how many of them sticky work may hold —
		// deliberately not two partitioned pools.
		//
		// A partition reserves capacity from per-call work whether or not any sticky worker exists, so on
		// a four-core host ordinary reads would drop from four concurrent to two on the day this shipped,
		// for a benefit nothing consumes yet; on a two-core build agent they would drop to one and
		// serialise the end-to-end suite. A ceiling costs nothing while sticky work is absent — per-call
		// may use every slot — and still guarantees the floor the moment sticky work appears, because
		// sticky can never occupy more than `StickyConcurrencyCap` of them.
		//
		// The floor is what AC-06 asserts, and it survives either way: with sticky at its ceiling,
		// `ConcurrencyCap - StickyConcurrencyCap` slots remain reachable by per-call work.
		StickyConcurrencyCap = DeriveStickyConcurrencyCap(ConcurrencyCap);
		PerCallConcurrencyCap = ConcurrencyCap - StickyConcurrencyCap;
		_pool = new WorkerSlotPool(ConcurrencyCap);
		_ownerIdentity = CaptureCurrentProcessIdentity();
	}

	/// <inheritdoc />
	public int ConcurrencyCap { get; }

	/// <inheritdoc />
	public int StickyConcurrencyCap { get; }

	/// <inheritdoc />
	public int PerCallConcurrencyCap { get; }

	/// <summary>
	/// Gets how long a call may wait for a slot before it is refused with
	/// <see cref="WorkerQueueWaitExpiredException"/>. See <see cref="DefaultQueueWaitBound"/> for the
	/// measurements behind the default and for why the wait is bounded at all.
	/// </summary>
	public TimeSpan QueueWaitBound { get; }

	/// <summary>
	/// Parses a raw seconds override into a queue-wait bound, falling back to
	/// <see cref="DefaultQueueWaitBound"/> for null / empty / non-numeric / out-of-range values. Pure, so
	/// the parse rules are testable without touching process state.
	/// </summary>
	/// <param name="rawValue">The raw override value.</param>
	/// <returns>The resolved bound.</returns>
	internal static TimeSpan ResolveQueueWaitBound(string rawValue) {
		if (!string.IsNullOrWhiteSpace(rawValue)
			&& double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds)
			&& seconds > 0 && seconds <= 3600) {
			return TimeSpan.FromSeconds(seconds);
		}
		return DefaultQueueWaitBound;
	}

	/// <summary>
	/// Parses a raw whole-worker override into a total concurrency cap, falling back to the core-count
	/// default for null / empty / non-numeric / out-of-range values. Pure, so the parse rules are testable
	/// without touching process state.
	/// </summary>
	/// <param name="rawValue">The raw override value.</param>
	/// <returns>The resolved total cap, always at least one.</returns>
	/// <remarks>
	/// Same shape as <see cref="ResolveQueueWaitBound"/> deliberately — one parse convention for the
	/// supervisor's knobs — but WHOLE workers rather than seconds: half a slot does not exist, and
	/// accepting "2.5" would silently mean something the operator did not write.
	/// </remarks>
	internal static int ResolveConcurrencyCap(string rawValue) {
		if (!string.IsNullOrWhiteSpace(rawValue)
			&& int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int cap)
			&& cap > 0 && cap <= MaximumConfigurableConcurrencyCap) {
			return cap;
		}
		return Math.Max(1, System.Environment.ProcessorCount);
	}

	/// <summary>
	/// Derives the sticky cap — the number of concurrent long operations a host supports — from the total
	/// admission capacity.
	/// </summary>
	/// <param name="totalConcurrencyCap">The total cap the two pools partition.</param>
	/// <returns>The sticky cap, always strictly less than the total.</returns>
	/// <remarks>
	/// <para>
	/// <b>Halving, and the two properties it is chosen for.</b> Integer division makes the result
	/// STRICTLY LESS than the total for every input, so the per-call remainder
	/// (<c>total − sticky</c>) is a floor that sticky work can never take — and it makes that remainder
	/// greater than or equal to the sticky share for every input too (5 → 2 sticky, 3 per-call), so the
	/// side of the split that answers ordinary reads is never the smaller one. Two pools whose caps left
	/// per-call work at zero would have relabelled the exhaustion rather than removed it.
	/// </para>
	/// <para>
	/// <b>A total of one derives a sticky cap of ZERO, and that is arithmetic rather than an oversight.</b>
	/// A single slot cannot both carry an operation that holds it for an hour and leave ordinary calls a
	/// floor; a host in that state supports no long operations, is told so by name
	/// (<see cref="WorkerStickyCapacityExceededException"/>), and the operator's remedy is to raise
	/// <see cref="ConcurrencyCapOverrideEnvVar"/>. The alternative — quietly flooring the TOTAL at two —
	/// would double admitted concurrency on every single-core host to paper over one message.
	/// </para>
	/// <para>
	/// <b>Partition rather than an additional pool.</b> An extra pool on top of the total would let a host
	/// run more workers than the measured ceiling the total encodes (ADR §2.4: CPU, not memory, is the
	/// binding constraint), and would make <see cref="ConcurrencyCap"/>'s published meaning — the most
	/// workers that may run at once — false.
	/// </para>
	/// </remarks>
	internal static int DeriveStickyConcurrencyCap(int totalConcurrencyCap) => totalConcurrencyCap / 2;

	#region Methods: worker lifecycle

	/// <inheritdoc />
	public async Task<IWorkerLease> SpawnContainedAsync(WorkerSpawnRequest request,
		CancellationToken cancellationToken) {
		ArgumentNullException.ThrowIfNull(request);
		// This method CREATES. Reaching a worker that already exists is ReachExisting, which takes no slot
		// — routing that here would make a caller wait for capacity held by the worker it is looking for
		// (ADR §3.2c).
		//
		// Per-call: queued, never dropped (AC-01) — but BOUNDED. A call is admitted the moment a slot
		// frees, and only a wait that outlasts the bound is refused, with a named exception carrying the
		// numbers a caller needs. An unbounded wait here would return nothing, issue zero requests to
		// Creatio and do so for an arbitrarily long time, which is the wedge this feature removes wearing
		// a different hat.
		//
		// Sticky: NOT queued. Its cap is the number of concurrent long operations the host supports and
		// each holder keeps its slot for minutes to an hour, so a queue could only spend the caller's
		// patience on its way to the same refusal. Which POOL the slot came from is recorded on the lease,
		// so the release path is unchanged by there being two of them.
		bool sticky = request.Lifetime == WorkerLifetime.Sticky;
		WorkerSlotPool pool = _pool;
		if (sticky) {
			// The ceiling is checked BEFORE the slot, so a refusal never disturbs the pool. Both are then
			// released together on every failure path below — a counter that drifts out of step with the
			// semaphore either leaks capacity or refuses forever, and neither shows up in a green suite.
			//
			// The two rejections are DIFFERENT conditions and must not share an exception. The ceiling being
			// full means every long operation this host supports is already running: that clears in minutes
			// to an hour, so queueing could only spend the caller's patience on the way to the same answer,
			// and WorkerStickyCapacityExceededException says so with a number the caller can act on. The
			// shared pool being full means ORDINARY per-call reads are momentarily using every slot, which
			// clears in seconds and has nothing to do with the sticky limit — reporting it as "all N long
			// operations are in use" would assert a condition a snapshot taken at that instant flatly
			// contradicts, and tell the operator to wait for something that does not exist. So it queues
			// under the ordinary bound instead, and only a wait that outlasts the bound is refused, with the
			// exception whose text is actually true.
			if (!TryReserveStickySlot()) {
				throw new WorkerStickyCapacityExceededException(StickyConcurrencyCap, ConcurrencyCap);
			}
			try {
				await pool.AcquireAsync(QueueWaitBound, cancellationToken).ConfigureAwait(false);
			} catch {
				ReleaseStickySlot();
				throw;
			}
		} else {
			await pool.AcquireAsync(QueueWaitBound, cancellationToken).ConfigureAwait(false);
		}

		bool slotHandedOver = false;
		try {
			WorkerLaunchRequest launchRequest = BuildLaunchRequest(request);
			IContainedWorker worker = _containment.OwnsProcessCreation
				? _containment.Launch(launchRequest)
				: _containment.Adopt(StartRedirectedProcess(launchRequest));
			// The budget clock starts HERE — after the slot was granted and the process exists — never at
			// admission. See IWorkerLease.BudgetExpiresAtUtc for the measurement behind that.
			DateTimeOffset spawnedAtUtc = DateTimeOffset.UtcNow;
			RegisterWorker(worker);
			int active = Interlocked.Increment(ref _activeWorkers);
			UpdatePeak(active);
			Interlocked.Increment(ref _totalSpawned);
			slotHandedOver = true;
			return new SupervisedWorkerLease(this, worker, pool, sticky, spawnedAtUtc, request.Budget);
		} finally {
			if (!slotHandedOver) {
				pool.Release();
				if (sticky) {
					ReleaseStickySlot();
				}
			}
		}
	}

	/// <inheritdoc />
	public IWorkerChannel ReachExisting(IWorkerLease lease) {
		ArgumentNullException.ThrowIfNull(lease);
		if (lease is not SupervisedWorkerLease ownLease || !ownLease.IssuedBy(this)) {
			// The instance check is the point, not the type check. Comparing only the TYPE would accept a
			// lease minted by ANOTHER supervisor while the message claims otherwise — and each supervisor
			// carries its own caps and its own registry, so this guard is exactly what the multi-consumer
			// wiring leans on. Not exploitable while SupervisedWorkerLease is private and nested, which is
			// why it is worth closing now, as one comparison, rather than after something depends on it.
			throw new ArgumentException("The lease was not issued by this supervisor.", nameof(lease));
		}
		// No pool is touched, on either branch, and there is nothing to await. That is the whole point:
		// the slot a poll would wait for is held by the worker the poll is trying to reach (ADR §3.2c).
		return new NonOwningWorkerChannel(ownLease);
	}

	/// <inheritdoc />
	public async Task<WorkerRunResult> WaitWithinBudgetAsync(IWorkerLease lease,
		CancellationToken cancellationToken) {
		ArgumentNullException.ThrowIfNull(lease);
		TimeSpan remaining = lease.BudgetExpiresAtUtc - DateTimeOffset.UtcNow;
		if (remaining <= TimeSpan.Zero) {
			return await TerminateForBudgetAsync(lease, WorkerRunStatus.BudgetExpired).ConfigureAwait(false);
		}

		using CancellationTokenSource budgetSource = new(remaining);
		using CancellationTokenSource linkedSource =
			CancellationTokenSource.CreateLinkedTokenSource(budgetSource.Token, cancellationToken);
		try {
			await lease.WaitForExitAsync(linkedSource.Token).ConfigureAwait(false);
			return new WorkerRunResult(WorkerRunStatus.Completed, lease.ExitCode,
				DateTimeOffset.UtcNow - lease.SpawnedAtUtc, null);
		} catch (OperationCanceledException) {
			// Kill regardless of which token fired: an abandoned child is exactly the wedge this feature
			// removes. Which of the two fired only decides how the outcome is reported.
			WorkerRunStatus status = cancellationToken.IsCancellationRequested
				? WorkerRunStatus.Canceled
				: WorkerRunStatus.BudgetExpired;
			return await TerminateForBudgetAsync(lease, status).ConfigureAwait(false);
		}
	}

	/// <inheritdoc />
	public WorkerTerminationOutcome KillContained(IWorkerLease lease) {
		ArgumentNullException.ThrowIfNull(lease);
		if (lease is not SupervisedWorkerLease ownLease || !ownLease.IssuedBy(this)) {
			// The instance check is the point, not the type check. Comparing only the TYPE would accept a
			// lease minted by ANOTHER supervisor while the message claims otherwise — and each supervisor
			// carries its own caps and its own registry, so this guard is exactly what the multi-consumer
			// wiring leans on. Not exploitable while SupervisedWorkerLease is private and nested, which is
			// why it is worth closing now, as one comparison, rather than after something depends on it.
			throw new ArgumentException("The lease was not issued by this supervisor.", nameof(lease));
		}
		return ownLease.Terminate();
	}

	/// <inheritdoc />
	public StaleWorkerReapReport ReapStaleWorkers() {
		StaleWorkerReapReport report = _registry.Reap(this);
		Interlocked.Add(ref _totalStaleReaped, report.Terminated);
		foreach (string warning in report.Warnings) {
			_logger.WriteWarning(warning);
		}
		if (report.Terminated > 0) {
			_logger.WriteInfo(
				$"Terminated {report.Terminated} MCP worker process(es) left behind by a previous clio process.");
		}
		return report;
	}

	/// <inheritdoc />
	public WorkerSupervisorSnapshot GetSnapshot() {
		return new WorkerSupervisorSnapshot(
			ConcurrencyCap,
			Volatile.Read(ref _activeWorkers),
			_pool.QueuedRequests,
			Volatile.Read(ref _peakActiveWorkers),
			Interlocked.Read(ref _totalSpawned),
			Interlocked.Read(ref _totalTerminated),
			Interlocked.Read(ref _totalStaleReaped),
			StickyConcurrencyCap,
			PerCallConcurrencyCap,
			// Read from the pool rather than from a counter of its own: the semaphore IS the occupancy, and
			// a parallel counter is one more thing that can disagree with the resource it describes.
			Volatile.Read(ref _activeStickyWorkers));
	}

	#endregion

	#region Methods: IWorkerProcessInspector

	/// <inheritdoc />
	public ProcessIdentitySnapshot TryCaptureIdentity(int processId) {
		if (processId <= 0) {
			return null;
		}
		Process process = null;
		try {
			process = Process.GetProcessById(processId);
			if (process.HasExited) {
				return null;
			}
			return new ProcessIdentitySnapshot(process.Id, process.StartTime.ToUniversalTime().Ticks,
				ReadExecutablePath(process));
		} catch (Exception exception) when (IsProcessInspectionFailure(exception)) {
			return null;
		} finally {
			process?.Dispose();
		}
	}

	/// <inheritdoc />
	public WorkerTerminationOutcome TerminateStaleWorker(WorkerRegistrationEntry entry) {
		ArgumentNullException.ThrowIfNull(entry);
		Process process = null;
		try {
			process = Process.GetProcessById(entry.ProcessId);
			// RE-VALIDATED ON THIS HANDLE, not trusted from the caller's earlier check. The registry does
			// compare the full pid / start-time / executable-path triple before deciding an entry is stale
			// — but that was a DIFFERENT Process object at an earlier instant. Between that decision and
			// this GetProcessById the recorded worker can exit and its pid be handed to something else, and
			// TerminateOrphan kills a process TREE. Killing a stranger and its children is the one outcome
			// this registry's identity checks exist to make impossible, so the check is repeated against the
			// handle actually about to be terminated. A pid alone is not an identity.
			// The FULL triple, not two thirds of it. An earlier version of this guard compared only the
			// start ticks — which is better than the pid alone and still not an identity: start timestamps
			// have finite resolution and two processes can share one, and on a machine that recycles pids
			// quickly the pair is exactly what collides. The registry's own comparison uses pid, start time
			// AND executable path, and a last-mile check that stops short of it is a weaker promise made in
			// the same words. An unreadable path counts as NO match, following the registry's rule: the
			// cost of refusing is one surviving orphan, the cost of guessing is a stranger's process tree.
			DateTime actualStartTimeUtc = ReadStartTimeUtc(process);
			string actualExecutablePath = ReadExecutablePath(process);
			if (actualStartTimeUtc.Ticks != entry.StartTimeUtcTicks
				|| !ExecutablePathsEqual(actualExecutablePath, entry.ExecutablePath)) {
				return WorkerTerminationOutcome.AlreadyExited;
			}
			using IWorkerProcessHandle handle = CreateHandle(process, entry.ExecutablePath, null, null, null);
			return _containment.TerminateOrphan(handle);
		} catch (Exception exception) when (IsProcessInspectionFailure(exception)) {
			return WorkerTerminationOutcome.AlreadyExited;
		} finally {
			process?.Dispose();
		}
	}

	#endregion

	#region Methods: inherited IProcessExecutor surface

	/// <inheritdoc />
	public string Execute(string program, string arguments, bool waitForExit, string workingDirectory = null,
		bool showOutput = false, bool suppressErrors = false) =>
		_processExecutor.Execute(program, arguments, waitForExit, workingDirectory, showOutput, suppressErrors);

	/// <inheritdoc />
	public Task<ProcessLaunchResult> FireAndForgetAsync(ProcessExecutionOptions options) =>
		_processExecutor.FireAndForgetAsync(options);

	/// <inheritdoc />
	public Task<ProcessExecutionResult> ExecuteAndCaptureAsync(ProcessExecutionOptions options) =>
		_processExecutor.ExecuteAndCaptureAsync(options);

	/// <inheritdoc />
	public Task<ProcessExecutionResult> ExecuteWithRealtimeOutputAsync(ProcessExecutionOptions options) =>
		_processExecutor.ExecuteWithRealtimeOutputAsync(options);

	#endregion

	#region Methods: Private

	private async Task<WorkerRunResult> TerminateForBudgetAsync(IWorkerLease lease, WorkerRunStatus status) {
		WorkerTerminationOutcome outcome = KillContained(lease);
		await WaitForTerminationConfirmationAsync(lease).ConfigureAwait(false);
		return new WorkerRunResult(status, lease.ExitCode, DateTimeOffset.UtcNow - lease.SpawnedAtUtc,
			outcome);
	}

	// Waited for, rather than assumed: a caller that is told the worker was killed must not have to
	// discover later that it is still holding a file or a socket.
	private async Task WaitForTerminationConfirmationAsync(IWorkerLease lease) {
		try {
			using CancellationTokenSource confirmation = new(TerminationConfirmationTimeout);
			await lease.WaitForExitAsync(confirmation.Token).ConfigureAwait(false);
		} catch (OperationCanceledException) {
			_logger.WriteWarning(
				$"MCP worker {lease.ProcessId} did not exit within {TerminationConfirmationTimeout.TotalSeconds:0} s of being terminated.");
		} catch (Exception exception) when (IsProcessInspectionFailure(exception)) {
			// The process is gone; nothing left to confirm.
		}
	}

	private WorkerLaunchRequest BuildLaunchRequest(WorkerSpawnRequest request) {
		ClioWorkerLaunchDescriptor descriptor = request.LaunchOverride
			?? _executablePathProvider.Resolve([.. request.Arguments]);
		string executable = ProcessExecutor.ResolveExecutablePath(descriptor.Executable);
		return new WorkerLaunchRequest(
			executable,
			descriptor.Arguments,
			request.WorkingDirectory ?? descriptor.WorkingDirectory ?? System.Environment.CurrentDirectory,
			ComposeEffectiveEnvironment(request),
			request.ClearInheritedEnvironment);
	}

	/// <summary>
	/// Composes the environment a worker is actually launched with: the allowlisted ambient variables read
	/// from the parent, then the caller's explicit delta on top.
	/// </summary>
	/// <param name="request">The spawn request, which states the allowlist and the delta.</param>
	/// <param name="parentEnvironmentReader">
	/// Reads one variable from the parent process; defaults to
	/// <see cref="System.Environment.GetEnvironmentVariable(string)"/>. Injected for the same reason
	/// <see cref="McpWorkerEnvironment.ComposeChildEnvironment"/> injects one — the composition rule is what
	/// decides whether a host-level knob reaches the worker at all, and asserting it must not require
	/// mutating process-wide state that neighbouring fixtures run in parallel with.
	/// </param>
	/// <returns>The variables the worker sees.</returns>
	/// <remarks>
	/// The explicit delta is applied LAST and therefore wins over the allowlist: a caller that states a
	/// variable is stating the worker's value for it, not a default the ambient environment may override.
	/// </remarks>
	internal static IReadOnlyDictionary<string, string> ComposeEffectiveEnvironment(WorkerSpawnRequest request,
		Func<string, string> parentEnvironmentReader = null) {
		Func<string, string> readParent = parentEnvironmentReader ?? System.Environment.GetEnvironmentVariable;
		Dictionary<string, string> environment = new(StringComparer.Ordinal);
		if (request.ClearInheritedEnvironment) {
			IReadOnlyCollection<string> allowlist = request.InheritedEnvironmentVariableAllowlist
				?? DefaultInheritedEnvironmentVariableAllowlist;
			foreach (string name in allowlist) {
				string value = readParent(name);
				if (value is not null) {
					environment[name] = value;
				}
			}
		}
		if (request.EnvironmentVariables is not null) {
			foreach (KeyValuePair<string, string> pair in request.EnvironmentVariables) {
				environment[pair.Key] = pair.Value;
			}
		}
		return environment;
	}

	private IWorkerProcessHandle StartRedirectedProcess(WorkerLaunchRequest request) {
		ProcessStartInfo startInfo = new() {
			FileName = request.Executable,
			WorkingDirectory = request.WorkingDirectory,
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardInput = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true
		};
		foreach (string argument in request.Arguments) {
			startInfo.ArgumentList.Add(argument);
		}
		if (request.ClearInheritedEnvironment) {
			startInfo.Environment.Clear();
		}
		foreach (KeyValuePair<string, string> pair in request.Environment) {
			startInfo.Environment[pair.Key] = pair.Value;
		}

		Process process = new() { StartInfo = startInfo };
		try {
			if (!process.Start()) {
				throw new InvalidOperationException(
					$"The MCP worker process '{request.Executable}' did not start.");
			}
			return CreateHandle(process, request.Executable,
				process.StandardInput.BaseStream,
				process.StandardOutput.BaseStream,
				process.StandardError.BaseStream);
		} catch {
			process.Dispose();
			throw;
		}
	}

	// Every operation on the System.Diagnostics.Process object is captured here, inside the one class
	// this feature allows to name that type, and handed out as delegates. The containment
	// implementations and the registry therefore work with a plain interface and stay free of it.
	private static IWorkerProcessHandle CreateHandle(Process process, string fallbackExecutablePath,
		Stream standardInput, Stream standardOutput, Stream standardError) {
		int processId = process.Id;
		DateTime startTimeUtc = ReadStartTimeUtc(process);
		string executablePath = ReadExecutablePath(process) ?? fallbackExecutablePath;
		return new DelegatedWorkerProcessHandle(
			processId,
			startTimeUtc,
			executablePath,
			standardInput,
			standardOutput,
			standardError,
			hasExited: () => {
				try {
					return process.HasExited;
				} catch (Exception exception) when (IsProcessInspectionFailure(exception)) {
					return true;
				}
			},
			exitCode: () => {
				try {
					return process.HasExited ? process.ExitCode : null;
				} catch (Exception exception) when (IsProcessInspectionFailure(exception)) {
					return null;
				}
			},
			waitForExitAsync: token => {
				// Guarded like HasExited and ExitCode beside it, and for the same stated reason: a reach that
				// threw on a dead worker would only move the race somewhere less convenient to handle. This
				// one was NOT guarded, and it is the member a status poll is most likely to await — the
				// owner disposing its lease mid-poll would surface ObjectDisposedException (an
				// InvalidOperationException) from a channel documented never to throw on a dead worker.
				try {
					return process.WaitForExitAsync(token);
				} catch (Exception exception) when (IsProcessInspectionFailure(exception)) {
					return Task.CompletedTask;
				}
			},
			killProcessTree: () => process.Kill(entireProcessTree: true),
			dispose: process.Dispose);
	}

	private static DateTime ReadStartTimeUtc(Process process) {
		try {
			return process.StartTime.ToUniversalTime();
		} catch (Exception exception) when (IsProcessInspectionFailure(exception)) {
			return DateTime.UtcNow;
		}
	}

	// Mirrors StaleWorkerRegistry.PathsEqual deliberately — same refusal on a blank path, and the same
	// raw string comparison. Both sides of this comparison were produced by ReadExecutablePath, so there
	// is nothing to normalise; normalising anyway would mean calling Path.GetFullPath on a string read
	// out of another process, which can throw, and an identity check that can throw is not one.
	private static bool ExecutablePathsEqual(string left, string right) {
		if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right)) {
			// Half an identity is not an identity: the cost of refusing is one surviving orphan, the cost
			// of guessing is a stranger's process tree.
			return false;
		}
		return string.Equals(left, right,
			OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
	}

	private static string ReadExecutablePath(Process process) {
		try {
			return process.MainModule?.FileName;
		} catch (Exception exception) when (IsProcessInspectionFailure(exception)) {
			return null;
		}
	}

	private static ProcessIdentitySnapshot CaptureCurrentProcessIdentity() {
		using Process current = Process.GetCurrentProcess();
		return new ProcessIdentitySnapshot(current.Id, ReadStartTimeUtc(current).Ticks,
			ReadExecutablePath(current) ?? System.Environment.ProcessPath);
	}

	private static bool IsProcessInspectionFailure(Exception exception) {
		return exception is ArgumentException
			or InvalidOperationException
			or NotSupportedException
			or System.ComponentModel.Win32Exception
			or IOException
			or UnauthorizedAccessException;
	}

	private void RegisterWorker(IContainedWorker worker) {
		try {
			_registry.Record(new WorkerRegistrationEntry(
				worker.ProcessId,
				worker.StartTimeUtc.Ticks,
				worker.ExecutablePath,
				_ownerIdentity.ProcessId,
				_ownerIdentity.StartTimeUtcTicks,
				_ownerIdentity.ExecutablePath,
				DateTimeOffset.UtcNow));
		} catch (Exception exception) when (exception is TimeoutException or IOException
				or UnauthorizedAccessException) {
			// Failing to record a worker costs a possible orphan after an abrupt parent death; failing the
			// tool call costs the user their answer. The containment layers are the primary guarantee, so
			// the warning is surfaced and the call proceeds.
			_logger.WriteWarning(
				$"Unable to record MCP worker {worker.ProcessId} for stale cleanup: {exception.Message}");
		}
	}

	private void UnregisterWorker(IContainedWorker worker) {
		try {
			_registry.Remove(worker.ProcessId, worker.StartTimeUtc.Ticks);
		} catch (Exception exception) when (exception is TimeoutException or IOException
				or UnauthorizedAccessException) {
			_logger.WriteWarning(
				$"Unable to remove MCP worker {worker.ProcessId} from the stale-cleanup registry: {exception.Message}");
		}
	}

	private void UpdatePeak(int active) {
		int observed = Volatile.Read(ref _peakActiveWorkers);
		while (active > observed) {
			int previous = Interlocked.CompareExchange(ref _peakActiveWorkers, active, observed);
			if (previous == observed) {
				return;
			}
			observed = previous;
		}
	}

	// The slot goes back to the pool it came from, named on the lease — not to "the" pool. A caller that
	// waits on a different pool therefore releases into that one without this method changing.
	private void ReleaseLease(IContainedWorker worker, WorkerSlotPool pool, bool sticky) {
		Interlocked.Decrement(ref _activeWorkers);
		UnregisterWorker(worker);
		worker.Dispose();
		pool.Release();
		if (sticky) {
			ReleaseStickySlot();
		}
	}

	// Reserves one place under the sticky ceiling, or reports that the ceiling is full. A compare-exchange
	// loop rather than a plain increment-then-test: the latter would momentarily exceed the ceiling and two
	// racing callers could both observe their own over-count and both back out, refusing a slot that was
	// free.
	private bool TryReserveStickySlot() {
		while (true) {
			int current = Volatile.Read(ref _activeStickyWorkers);
			if (current >= StickyConcurrencyCap) {
				return false;
			}
			if (Interlocked.CompareExchange(ref _activeStickyWorkers, current + 1, current) == current) {
				return true;
			}
		}
	}

	private void ReleaseStickySlot() => Interlocked.Decrement(ref _activeStickyWorkers);

	private void CountTermination() => Interlocked.Increment(ref _totalTerminated);

	#endregion

	/// <summary>A started process reduced to delegates, so its owner keeps the only reference to it.</summary>
	private sealed class DelegatedWorkerProcessHandle : IWorkerProcessHandle {

		private readonly Func<bool> _hasExited;
		private readonly Func<int?> _exitCode;
		private readonly Func<CancellationToken, Task> _waitForExitAsync;
		private readonly Action _killProcessTree;
		private readonly Action _dispose;

		public DelegatedWorkerProcessHandle(int processId, DateTime startTimeUtc, string executablePath,
			Stream standardInput, Stream standardOutput, Stream standardError, Func<bool> hasExited,
			Func<int?> exitCode, Func<CancellationToken, Task> waitForExitAsync, Action killProcessTree,
			Action dispose) {
			ProcessId = processId;
			StartTimeUtc = startTimeUtc;
			ExecutablePath = executablePath;
			StandardInput = standardInput;
			StandardOutput = standardOutput;
			StandardError = standardError;
			_hasExited = hasExited;
			_exitCode = exitCode;
			_waitForExitAsync = waitForExitAsync;
			_killProcessTree = killProcessTree;
			_dispose = dispose;
		}

		public int ProcessId { get; }

		public DateTime StartTimeUtc { get; }

		public string ExecutablePath { get; }

		public Stream StandardInput { get; }

		public Stream StandardOutput { get; }

		public Stream StandardError { get; }

		public bool HasExited => _hasExited();

		public int? ExitCode => _exitCode();

		public Task WaitForExitAsync(CancellationToken cancellationToken) => _waitForExitAsync(cancellationToken);

		public void KillProcessTree() => _killProcessTree();

		public void Dispose() => _dispose();
	}

	/// <summary>
	/// A conversation with a worker somebody else holds: the lease's talking surface, and nothing that
	/// could end it or return its slot.
	/// </summary>
	/// <remarks>
	/// A wrapper rather than the lease handed out under a narrower static type, because a static type is
	/// a suggestion: <c>(IWorkerLease)channel</c> or a stray <c>using</c> would put the kill switch back
	/// in the hands of a caller that only wanted to read <c>compile-status</c>, and terminate the
	/// operation it was observing. This class does not implement <see cref="IWorkerLease"/> or
	/// <see cref="IDisposable"/>, so neither is expressible.
	/// </remarks>
	private sealed class NonOwningWorkerChannel : IWorkerChannel {

		private readonly IWorkerChannel _worker;

		internal NonOwningWorkerChannel(IWorkerChannel worker) => _worker = worker;

		public int ProcessId => _worker.ProcessId;

		public Stream StandardInput => _worker.StandardInput;

		public Stream StandardOutput => _worker.StandardOutput;

		public Stream StandardError => _worker.StandardError;

		public bool HasExited => _worker.HasExited;

		public int? ExitCode => _worker.ExitCode;

		public Task WaitForExitAsync(CancellationToken cancellationToken) =>
			_worker.WaitForExitAsync(cancellationToken);
	}

	/// <summary>One held worker: a concurrency slot, a contained process and a registry entry.</summary>
	private sealed class SupervisedWorkerLease : IWorkerLease {

		private readonly WorkerProcessSupervisor _supervisor;
		private readonly IContainedWorker _worker;
		private readonly WorkerSlotPool _pool;
		private readonly bool _sticky;
		private int _disposed;

		public SupervisedWorkerLease(WorkerProcessSupervisor supervisor, IContainedWorker worker,
			WorkerSlotPool pool, bool sticky, DateTimeOffset spawnedAtUtc, TimeSpan budget) {
			_supervisor = supervisor;
			_worker = worker;
			// Recorded rather than assumed: the lease is what returns the slot, so it must know which pool
			// granted it and whether that grant also took a place under the sticky ceiling. Both are
			// returned together in Dispose, which is the only place either is given back.
			_pool = pool;
			_sticky = sticky;
			SpawnedAtUtc = spawnedAtUtc;
			Budget = budget;
		}

		public int ProcessId => _worker.ProcessId;

		public DateTimeOffset SpawnedAtUtc { get; }

		public TimeSpan Budget { get; }

		public DateTimeOffset BudgetExpiresAtUtc => SpawnedAtUtc + Budget;

		public Stream StandardInput => _worker.StandardInput;

		public Stream StandardOutput => _worker.StandardOutput;

		public Stream StandardError => _worker.StandardError;

		public bool HasExited => _worker.HasExited;

		public int? ExitCode => _worker.ExitCode;

		public Task WaitForExitAsync(CancellationToken cancellationToken) =>
			_worker.WaitForExitAsync(cancellationToken);

		public WorkerTerminationOutcome Terminate() {
			WorkerTerminationOutcome outcome = _worker.Kill();
			if (outcome != WorkerTerminationOutcome.AlreadyExited) {
				_supervisor.CountTermination();
			}
			return outcome;
		}

		internal bool IssuedBy(WorkerProcessSupervisor supervisor) =>
			ReferenceEquals(_supervisor, supervisor);

		public void Dispose() {
			if (Interlocked.Exchange(ref _disposed, 1) != 0) {
				return;
			}
			if (!_worker.HasExited) {
				Terminate();
			}
			_supervisor.ReleaseLease(_worker, _pool, _sticky);
		}
	}

	/// <summary>
	/// One pool of concurrency slots: a cap, the semaphore that enforces it, and a count of the callers
	/// currently waiting on it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A type rather than three loose fields because the cap, the queue depth and the wait belong
	/// together in the refusal: <see cref="WorkerQueueWaitExpiredException"/> reports all three, and it
	/// must report them for the pool the caller actually waited on. One pool exists today — the per-call
	/// pool every ordinary tool call takes a slot from. A second one with its own cap, for workers whose
	/// lifetime outlives a single answer and which must therefore not queue behind (or ahead of)
	/// ordinary per-call work, is then an added field and an added <c>AcquireAsync</c> call site rather
	/// than a rewrite of the release path: the lease already names the pool it must release into.
	/// </para>
	/// <para>
	/// Not disposed: the semaphore lives as long as the supervisor, and disposing it while a caller is
	/// queued is the one thing that turns a bounded wait back into an unbounded failure.
	/// </para>
	/// </remarks>
	private sealed class WorkerSlotPool {

		private readonly SemaphoreSlim _slots;
		private int _queuedRequests;

		internal WorkerSlotPool(int cap) {
			Cap = cap;
			// A cap of ZERO is a legitimate pool, not a misconfiguration: DeriveStickyConcurrencyCap returns
			// it on a host whose total capacity is one. SemaphoreSlim rejects a maximum of zero, so the
			// semaphore is built with a maximum of one that is never filled, and both acquisition paths
			// short-circuit on Cap before they reach it.
			_slots = new SemaphoreSlim(cap, Math.Max(cap, 1));
		}

		/// <summary>Gets the maximum number of slots this pool hands out at once.</summary>
		internal int Cap { get; }

		/// <summary>Gets the callers waiting for a slot on this pool right now.</summary>
		internal int QueuedRequests => Volatile.Read(ref _queuedRequests);

		/// <summary>Gets the slots of this pool that are held right now.</summary>
		internal int SlotsInUse => Cap - _slots.CurrentCount;

		/// <summary>
		/// Waits for a slot, for at most <paramref name="queueWaitBound"/>.
		/// </summary>
		/// <param name="queueWaitBound">How long the caller may wait before it is refused.</param>
		/// <param name="cancellationToken">Ends the wait early on the caller's behalf.</param>
		/// <exception cref="WorkerQueueWaitExpiredException">The bound elapsed with no slot free.</exception>
		internal async Task AcquireAsync(TimeSpan queueWaitBound, CancellationToken cancellationToken) {
			if (Cap == 0) {
				// Nothing will ever free, so waiting the bound out would only delay the same refusal.
				throw new WorkerQueueWaitExpiredException(TimeSpan.Zero, queueWaitBound, Cap, 1);
			}
			long startedAt = Stopwatch.GetTimestamp();
			Interlocked.Increment(ref _queuedRequests);
			try {
				if (await _slots.WaitAsync(queueWaitBound, cancellationToken).ConfigureAwait(false)) {
					return;
				}
				// Depth is read BEFORE this caller leaves the queue, so the number includes the call being
				// refused: "4 running, 9 queued" is what a caller needs to tell a burst from saturation.
				throw new WorkerQueueWaitExpiredException(Stopwatch.GetElapsedTime(startedAt),
					queueWaitBound, Cap, QueuedRequests);
			} finally {
				Interlocked.Decrement(ref _queuedRequests);
			}
		}

		/// <summary>
		/// Takes a slot if one is free at this instant, and otherwise gives up immediately.
		/// </summary>
		/// <returns><see langword="true"/> when a slot was taken.</returns>
		/// <remarks>
		/// <see cref="QueuedRequests"/> is deliberately untouched: nobody queues on this path, and counting
		/// a caller that waited for nothing would report a phantom depth to the next one that reads it.
		/// </remarks>
		internal bool TryAcquireImmediately() => Cap > 0 && _slots.Wait(0);

		/// <summary>Returns one slot to this pool.</summary>
		internal void Release() => _slots.Release();
	}
}
