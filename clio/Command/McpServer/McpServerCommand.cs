using System;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command.McpServer.Knowledge;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Common.McpWorker;
using Clio.Common.Telemetry;
using CommandLine;

namespace Clio.Command.McpServer;


[Verb("mcp-server", Aliases = ["mcp"], HelpText = "Starts mcp server in stdio mode")]
public class McpServerCommandOptions : BaseCommandOptions
{

	/// <summary>
	/// Gets or sets a value indicating whether this process serves MCP calls as a short-lived child worker
	/// of an MCP host, which skips the host's startup bootstrap and shutdown drains.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Hidden and internal: a worker is spawned by clio's own MCP host, never by a user, and the flag is not
	/// part of the documented surface.
	/// </para>
	/// <para>
	/// Mode is selected by an OPTION rather than an environment variable for three reasons. An environment
	/// variable is inherited by grandchildren, so it would silently turn any clio a worker spawns through
	/// <c>clio-run</c> into a worker too; the flag shows up in the child's command line, which is already the
	/// worker's identity for the stale-worker reap; and the argument keeps the verb itself first, so the MCP
	/// mode detection that suppresses the startup update check and neutralises an ambient proxy keeps working
	/// unchanged. The flag is not secret, so carrying it in argv costs nothing.
	/// </para>
	/// <para>
	/// There is deliberately no <c>--worker-lifetime</c>: sticky versus ordinary is decided parent-side, by
	/// which environment the parent composes for the child.
	/// </para>
	/// </remarks>
	[Option("worker", Required = false, Hidden = true,
		HelpText = "Internal: serve MCP calls as a short-lived child worker; skips host bootstrap.")]
	public bool Worker { get; set; }
}


/// <summary>
/// Starts Clio's standard-input/output MCP host.
/// </summary>
public class McpServerCommand(ModelContextProtocol.Server.McpServer server,
	ITelemetryFlushScheduler flushScheduler,
	ISessionContainerCache sessionContainerCache,
	ITenantExecutionLockProvider tenantExecutionLockProvider,
	ICuratedKnowledgeBootstrapService curatedKnowledgeBootstrapService,
	Common.McpWorker.IWorkerProcessSupervisor workerProcessSupervisor,
	Relay.ISharedResourceReservation sharedResourceReservation,
	ILogger logger) : Command<McpServerCommandOptions>{
	internal static readonly TimeSpan CuratedKnowledgeBootstrapTimeout = TimeSpan.FromMilliseconds(
		CuratedKnowledgeSourceDefaults.StartupInstallDeadlineMilliseconds);

	public override int Execute(McpServerCommandOptions options) {
		ArgumentNullException.ThrowIfNull(options);
		// Worker-side containment first, BEFORE anything that could spawn a descendant: the worker leads its
		// own process group and arms parent-death signalling, so a hard-killed parent takes the worker and
		// everything below it. A parent that is SIGKILLed runs no code, so this half cannot live there.
		ArmWorkerContainment(options, logger);
		ReapStaleWorkersForHost(options, workerProcessSupervisor, logger);
		BootstrapCuratedKnowledgeForHost(options, curatedKnowledgeBootstrapService, logger);
		// FR-05/FR-08 (ENG-93208): wire the tool-execution-lock facade to this host's DI-registered
		// per-tenant lock provider and session-container cache, so per-tenant serialization and the
		// in-flight eviction guard operate on the SAME instances ToolCommandResolver uses.
		//
		// The third argument is the ENG-95262 story 7 / AC-03 half: the same singleton
		// McpWorkerCallDispatcher reserves through before it spawns a worker. Without it the facade keeps
		// its own static dictionary, and a target-keyed reservation taken by the dispatcher for a
		// worker-routed compile-creatio sits in a DIFFERENT store from the one an in-process
		// install-process-builder consults — same key, two dictionaries, so the two stop excluding each
		// other exactly where the shipped configuration needs them to (install-process-builder is withheld
		// from the worker cohort deliberately: the kill-safety audit lists it as leaving damage nothing
		// repairs). One store, therefore, and this is the seam that makes it one.
		McpToolExecutionLock.Configure(
			tenantExecutionLockProvider, sessionContainerCache, sharedResourceReservation);
		McpLogNotifier.Initialize(server);
		// The using-scoped source is disposed at the END of Execute — strictly after the finally
		// block has detached the handlers and drained. Do not narrow this scope or dispose earlier:
		// the detach-before-dispose ordering is precisely what keeps a late signal off the disposed
		// source.
		using var cts = new CancellationTokenSource();

		// Capture the signal handlers in locals (rather than inline lambdas) so the finally
		// block can detach them before the cancellation source is disposed. When standard
		// input reaches end of file the host loop returns normally, yet a still-subscribed
		// process-exit handler could cancel the already-disposed source and crash an
		// otherwise-clean exit with an unhandled ObjectDisposedException.
		ConsoleCancelEventHandler onCancelKeyPress = (_, e) => {
			e.Cancel = true;
			RequestShutdown(cts);
		};
		EventHandler onProcessExit = (_, _) => RequestShutdown(cts);

		Console.CancelKeyPress += onCancelKeyPress;
		AppDomain.CurrentDomain.ProcessExit += onProcessExit;
		// Drain the telemetry spool left over from previous sessions; fire-and-forget,
		// the server starts serving immediately.
		ScheduleStartupTelemetryFlush(options, flushScheduler);
		try {
			server.RunAsync(cts.Token).GetAwaiter().GetResult();
		} catch (OperationCanceledException) {
			// Ctrl+C / ProcessExit path: the triggered token makes RunAsync throw here. A plain
			// stdin EOF instead returns from RunAsync normally, without throwing.
		} finally {
			// Detach the OS-signal handlers before the cancellation source is disposed so a
			// late signal can no longer reach the disposed source. This unsubscribe is the
			// deterministic fix for the EOF/ProcessExit race; the guard inside RequestShutdown
			// is only a defense-in-depth net for the residual concurrent-teardown window.
			// Detaching the Ctrl+C handler here also means a second Ctrl+C during the drain
			// below is no longer intercepted and will hard-kill the process — intended, so a
			// stuck drain stays interruptible.
			Console.CancelKeyPress -= onCancelKeyPress;
			AppDomain.CurrentDomain.ProcessExit -= onProcessExit;
			// Flush any in-flight background work (CDN refreshes, telemetry uploads) before the
			// process exits. Without this, the fire-and-forget Task.Run tasks are killed by the
			// runtime as soon as the main (foreground) thread exits, leaving the on-disk cache
			// stale indefinitely. The two drains run concurrently so shutdown stays bounded at
			// ~10 seconds.
			DrainHostBackgroundWork(options, flushScheduler);
			McpLogNotifier.Reset();
		}
		return 0;
	}

	/// <summary>
	/// Runs the curated-knowledge bootstrap unless this process is a worker.
	/// </summary>
	/// <remarks>
	/// This is the single biggest worker startup saving: the bootstrap is a budgeted git clone of the
	/// clio-knowledge repository with a 5,000 ms startup deadline. A worker serves one call and never answers a
	/// guidance request, so paying that on every spawn would dominate the spawn cost the whole design rests on.
	/// </remarks>
	/// <param name="options">The parsed command options; <c>--worker</c> suppresses the bootstrap.</param>
	/// <param name="bootstrapService">The curated knowledge bootstrap service.</param>
	/// <param name="logger">The host logger.</param>
	internal static void BootstrapCuratedKnowledgeForHost(
		McpServerCommandOptions options,
		ICuratedKnowledgeBootstrapService bootstrapService,
		ILogger logger) {
		if (options.Worker) {
			return;
		}
		BootstrapCuratedKnowledge(bootstrapService, logger);
	}

	/// <summary>
	/// Schedules the startup telemetry flush unless this process is a worker.
	/// </summary>
	/// <remarks>
	/// Telemetry stays the PARENT's job. N workers posting where one process did is a regression, not a
	/// feature — which is also why <c>send-telemetry</c> stays classified as an in-process tool: a branch here
	/// covers the host's own flush, not a tool call that would reach the same endpoint from a child.
	/// </remarks>
	/// <param name="options">The parsed command options; <c>--worker</c> suppresses the flush.</param>
	/// <param name="flushScheduler">The telemetry flush scheduler.</param>
	internal static void ScheduleStartupTelemetryFlush(
		McpServerCommandOptions options,
		ITelemetryFlushScheduler flushScheduler) {
		if (options.Worker) {
			return;
		}
		flushScheduler.TryScheduleFlush();
	}

	/// <summary>
	/// Drains the host's fire-and-forget background work at shutdown unless this process is a worker.
	/// </summary>
	/// <remarks>
	/// Both drains are bounded at 10 seconds, so a worker that ran them would pay up to ten seconds of pure
	/// exit latency per call — and neither drain has anything to do: a worker refreshes no component-registry
	/// catalog and posts no telemetry.
	/// </remarks>
	/// <param name="options">The parsed command options; <c>--worker</c> suppresses both drains.</param>
	/// <param name="flushScheduler">The telemetry flush scheduler.</param>
	internal static void DrainHostBackgroundWork(
		McpServerCommandOptions options,
		ITelemetryFlushScheduler flushScheduler) {
		if (options.Worker) {
			return;
		}
		Task.WhenAll(
				ComponentRegistryClient.DrainAsync(TimeSpan.FromSeconds(10)),
				flushScheduler.DrainAsync(TimeSpan.FromSeconds(10)))
			.GetAwaiter().GetResult();
	}

	/// <summary>
	/// Arms worker-side containment when this process is a worker: process-group promotion plus parent-death
	/// signalling. A no-op for an ordinary host, which nothing supervises.
	/// </summary>
	/// <param name="options">The parsed command options; containment is armed only under <c>--worker</c>.</param>
	/// <param name="logger">The host logger; the outcome is recorded as a debug line.</param>
	/// <returns>What arming produced, or <see langword="null"/> when this process is not a worker.</returns>
	internal static ParentDeathWatchResult ArmWorkerContainment(McpServerCommandOptions options, ILogger logger) {
		if (!options.Worker) {
			return null;
		}
		ParentDeathWatchResult result = UnixParentDeathWatch.Arm();
		logger.WriteDebug(
			$"MCP worker containment armed: group-leader={result.ProcessGroupPromoted}, "
			+ $"mode={result.Mode}, parent={result.ParentProcessId}, "
			+ $"parent-already-exited={result.ParentAlreadyExited}.");
		return result;
	}

	/// <summary>
	/// Kills workers left behind by a PREVIOUS parent that died without taking them with it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Containment normally makes this unnecessary: a worker leads its own process group and arms
	/// parent-death signalling, so a hard-killed parent takes its workers down. But arming can FAIL — the
	/// watch reports that — and a parent killed before it armed leaves a worker recorded on disk with
	/// nothing to end it. That worker keeps its authenticated session and every descendant it spawned,
	/// indefinitely.
	/// </para>
	/// <para>
	/// <b>The registry has had an identity-checked reaper since stage 2 and nothing called it.</b> Found
	/// 2026-08-19 by an external review; the method, its interface declaration and its tests all existed,
	/// so the gap was invisible to everything except a search for callers. It runs on the HOST only — a
	/// worker spawns no workers, and a worker reaping the registry would kill its siblings.
	/// </para>
	/// <para>
	/// Non-fatal by construction: a startup that cannot reap must still serve. The reaper itself compares
	/// the full pid / start-time / executable-path triple before killing anything, so it cannot take a
	/// stranger that inherited a recycled pid.
	/// </para>
	/// </remarks>
	/// <param name="options">The parsed command options; skipped under <c>--worker</c>.</param>
	/// <param name="supervisor">The supervisor owning the on-disk worker registry.</param>
	/// <param name="logger">The host logger.</param>
	internal static void ReapStaleWorkersForHost(McpServerCommandOptions options,
		Common.McpWorker.IWorkerProcessSupervisor supervisor, ILogger logger) {
		if (options.Worker) {
			return;
		}
		try {
			Common.McpWorker.StaleWorkerReapReport report = supervisor.ReapStaleWorkers();
			if (report.Terminated > 0 || report.Warnings.Count > 0) {
				logger.WriteWarning(
					$"Reaped {report.Terminated} worker(s) left by a previous clio host"
					+ (report.Warnings.Count > 0 ? $"; {report.Warnings.Count} warning(s)." : "."));
			}
		}
		catch (Exception exception) {
			// A startup that cannot clean up must still serve requests.
			logger.WriteWarning(
				$"Stale worker cleanup did not run: {SensitiveErrorTextRedactor.Redact(exception.Message)}");
		}
	}

	/// <summary>
	/// Reports one non-fatal curated knowledge bootstrap phase.
	/// </summary>
	/// <param name="result">The phase result to report.</param>
	/// <param name="logger">The host logger.</param>
	/// <returns>The bootstrap result.</returns>
	internal static CuratedKnowledgeBootstrapResult ReportCuratedKnowledgeBootstrap(
		CuratedKnowledgeBootstrapResult result,
		ILogger logger) {
		if (result.Success) {
			logger.WriteDebug(result.Message);
		} else {
			logger.WriteWarning(
				$"MCP is starting without built-in curated knowledge: {result.Message} "
				+ $"Retry with install-knowledge --source {CuratedKnowledgeSourceDefaults.Alias}.");
		}
		return result;
	}

	/// <summary>
	/// Repairs and installs the curated source before the MCP transport starts accepting requests.
	/// </summary>
	/// <param name="bootstrapService">The curated knowledge bootstrap service.</param>
	/// <param name="logger">The host logger.</param>
	/// <returns>The non-fatal bootstrap result.</returns>
	internal static CuratedKnowledgeBootstrapResult BootstrapCuratedKnowledge(
		ICuratedKnowledgeBootstrapService bootstrapService,
		ILogger logger) {
		using CancellationTokenSource startupBudget = new(CuratedKnowledgeBootstrapTimeout);
		return ReportCuratedKnowledgeBootstrap(bootstrapService.Bootstrap(startupBudget.Token), logger);
	}

	/// <summary>
	/// Requests graceful shutdown from an OS-signal handler (Ctrl+C or process exit) in a way
	/// that tolerates the source already being disposed.
	/// </summary>
	/// <remarks>
	/// EOF on stdin is a legitimate stdio-transport termination signal: it makes the host loop
	/// return and disposes <paramref name="cancellationTokenSource"/>. A <see cref="AppDomain.ProcessExit"/>
	/// callback can still fire afterwards, and calling <see cref="CancellationTokenSource.Cancel()"/> on the
	/// disposed source would raise an unhandled <see cref="ObjectDisposedException"/> that crashes
	/// the process during an otherwise-clean shutdown. Swallowing it keeps the exit code at 0.
	/// <see cref="CancellationTokenSource.Cancel()"/> also runs the host's cancellation callbacks
	/// synchronously, so a callback that throws during teardown surfaces as an
	/// <see cref="AggregateException"/>; that is swallowed for the same reason, since the process is
	/// already terminating and the fault is not actionable.
	/// </remarks>
	/// <param name="cancellationTokenSource">The shutdown token source driving the MCP host loop.</param>
	internal static void RequestShutdown(CancellationTokenSource cancellationTokenSource) {
		try {
			cancellationTokenSource.Cancel();
		} catch (ObjectDisposedException) {
			// The host already shut down (EOF teardown disposed the source); nothing to cancel.
		} catch (AggregateException) {
			// Cancel() runs the host's cancellation callbacks synchronously; a callback that throws
			// during teardown surfaces here. Swallow it so the shutdown signal still exits cleanly —
			// the process is already terminating, mirroring the disposed-source case above.
		}
	}
}
