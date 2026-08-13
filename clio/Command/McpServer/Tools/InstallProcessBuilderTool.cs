using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool surface for the <c>install-process-builder</c> command.
/// </summary>
/// <remarks>
/// Deliberately NOT feature-gated, even though every tool it unblocks carries
/// <c>[FeatureToggle("process-designer")]</c>. A gated primitive is filtered out of registration, so the
/// remediation the process-designer tools point at would be unreachable exactly when it is needed.
/// </remarks>
public sealed class InstallProcessBuilderTool(
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<InstallProcessBuilderOptions>(null, logger, commandResolver) {

	/// <summary>
	/// Stable MCP tool name for installing the bundled process-builder package.
	/// </summary>
	internal const string InstallProcessBuilderToolName = "install-process-builder";

	/// <summary>
	/// Test seam overriding the MCP response deadline. <see langword="null"/> in production (the default
	/// <see cref="McpProgressHeartbeat.DefaultResponseDeadline"/> applies); unit tests set a tiny value to
	/// exercise the deadline-exceeded in-progress branch deterministically.
	/// </summary>
	internal TimeSpan? ResponseDeadlineOverride { get; set; }

	/// <summary>
	/// Installs (or updates) the bundled process-builder package into a registered Creatio environment.
	/// </summary>
	/// <remarks>
	/// Runs under the heartbeat+deadline helper because the call is minutes long: the target compiles the
	/// package, restarts, and the command waits the instance back out before judging the result. Without it
	/// a client with a per-request timeout reports the tool as failed while the install is still running.
	/// <para>
	/// Mutual exclusion is the NARROW configuration-build reservation, not the broad per-tenant execution
	/// monitor — <see cref="InternalExecuteWithoutTenantLock{TCommand}"/>. Past the response deadline the
	/// work runs detached, so the monitor would stay held after the caller was answered and every unrelated
	/// same-tenant tool, read-only ones included, would stall behind work nobody awaits any more (review
	/// Blocker, the same reason <c>compile-creatio</c> takes no lock). What genuinely must not
	/// overlap is the configuration build this install triggers on the target, and that is exactly what the
	/// reservation excludes — against another install AND against a concurrent <c>compile-creatio</c>.
	/// </para>
	/// <para>
	/// A duplicate is therefore REFUSED rather than queued. Without the monitor there is nothing to queue
	/// behind, and queueing was never the right answer anyway: it made a second call start another install,
	/// another build and another restart on an instance already being rebuilt.
	/// </para>
	/// </remarks>
	// Destructive = true. It only ADDS a package, which is what argued for false at first, but the annotation
	// is what clio's own core-rules guidance ties the "confirm the target environment with the user first"
	// requirement to — and a client with an auto-approve policy for non-destructive tools would install into
	// a live instance, run a configuration build on it and restart it with no human in the loop. Recovery
	// from a failed compile is an explicit RestoreFromBackup, not a rollback. compile-creatio and
	// restart-by-environment-name are both Destructive = true and this tool causes the effects of both.
	// install-gate is Destructive = false and is NOT the precedent: it ships a prebuilt assembly and never
	// makes the target rebuild its configuration.
	[McpServerTool(Name = InstallProcessBuilderToolName, ReadOnly = false, Destructive = true,
		Idempotent = true, OpenWorld = false)]
	[Description("""
	             Installs (or updates) the bundled CrtProcessBuilder package into a registered Creatio
	             environment, making ProcessDesignService reachable there.

	             Run this when a process-designer tool (`create-business-process`, `modify-business-process`,
	             `describe-business-process`, `list-user-tasks`, `validate-process-graph`) refuses because of
	             the CrtProcessBuilder package. There are two such refusals and both are fixed by this tool:
	             the package is missing entirely ("you need to install the CrtProcessBuilder package"), or the
	             environment carries an older version than this clio ships ("This clio carries CrtProcessBuilder
	             X, but the target environment has Y"). Both name this tool in their hint. Then retry the
	             original call.

	             The package ships as source and the target environment compiles it during installation, so
	             this takes substantially longer than a plain package install. How long depends entirely on
	             the target - its configuration size, host and current load - so do NOT quote a duration to
	             the user or treat an overrun as a failure. You never restart anything yourself, though a restart does happen - the platform
	             recycles itself on .NET Framework, the installer issues it on .NET - and the tool waits for the
	             instance to come back before judging it. It then checks the OUTCOME rather than the install
	             call: it asks the package's own service whether it is serving (Ping, ungated) and fails unless
	             it answers. So "installed but never compiled" is reported instead of looking like success -
	             which no version reported by list-packages can distinguish, because the database records what
	             was accepted, not what was built. Note the limit: the check is liveness, not identity, so on an
	             UPGRADE a stale assembly that still answers will pass. Treat a successful install of a NEW
	             version as authoritative only after the functionality you needed actually works.

	             It always installs except in two cases, and re-running is otherwise safe (it costs one
	             configuration build on the target). Take the refusal itself as the signal to call this tool
	             rather than comparing versions yourself: `list-packages` reports the version the environment
	             RECORDED, which is what the gate already checks for you.

	             Both exceptions exist to stop an environment moving BACKWARDS, both report exit code 1, and
	             neither is retryable. The override is the same for both and is deliberately NOT available to
	             you - it is a command-line flag a human runs after deciding the rollback is what they want. Do
	             not reach for a shell to get around either one.

	             1. The environment already carries a NEWER version than this clio ships, so installing would
	             move its recorded version backwards for everyone using it. Say the fix is to update clio.
	             2. This clio's OWN bundled version carries a pre-release suffix, which makes a rollback
	             undetectable, so the distribution is refused rather than installed. Nothing about the target
	             environment is wrong. Say the fix is to reinstall or update clio.

	             Reinstalling the SAME version is not a downgrade and is allowed - that is the repair path when
	             a package installed but never compiled.

	             Long-running: streams notifications/progress while working. If the MCP response deadline is
	             reached first you get an in-progress note, which is NOT a verdict - the install is still
	             running server-side and may still fail. Do not call this tool again while that is true; a
	             second call is refused. Wait, then retry the process-designer tool you came from: its package
	             gate is the confirmation, and it refuses again if the install did not take.
	             """)]
	public async Task<CommandExecutionResult> InstallProcessBuilder(
		[Description("install-process-builder parameters")] [Required] InstallProcessBuilderArgs args,
		global::ModelContextProtocol.Server.McpServer server = null,
		RequestContext<CallToolRequestParams> requestContext = null,
		CancellationToken cancellationToken = default) {
		InstallProcessBuilderOptions options = new() {
			Environment = args.EnvironmentName
		};
		// Set when the deadline wins the race, so the detached continuation can tell that the caller was
		// already answered and its exit code has nowhere to travel but stderr. A holder rather than a
		// captured local because the writer and the reader are on different threads. Benign race: if the
		// work finishes in the same instant the deadline fires, whoever won the race decided what the caller
		// got, and a result that reached the caller needs no stderr copy.
		StrongBox<bool> callerAlreadyAnswered = new(false);
		try {
			return await McpProgressHeartbeat.RunWithProgressAndDeadlineAsync(
				server,
				requestContext?.Params?.ProgressToken,
				InstallProcessBuilderToolName,
				() => RunInstall(options, callerAlreadyAnswered),
				deadline: ResponseDeadlineOverride,
				cancellationToken: cancellationToken).ConfigureAwait(false);
		} catch (McpResponseDeadlineExceededException) {
			callerAlreadyAnswered.Value = true;
			// Exit code 0, per FromInfo's in-progress contract — a still-running install is not a failure, and
			// reporting one would send an agent into remediation for a healthy build. But the message must not
			// read as a verdict: at this point NOTHING is established, unlike restart-by-environment-name
			// (whose write already returned) or compile-creatio (whose operation is recorded and pollable).
			// So it points at a READ-ONLY confirmation instead of at itself.
			return CommandExecutionResult.FromInfo(
				$"The {BundledPackages.ProcessBuilderPackageName} install on '{args.EnvironmentName}' is still "
				+ "running server-side: the target is compiling the package and will restart. This is NOT a "
				+ "verdict — nothing is confirmed yet, and the install may still fail. Do NOT call "
				+ $"{InstallProcessBuilderToolName} again: while this one runs a second call is refused, and it "
				+ "would only trigger another configuration build. Wait, then retry the process-designer tool "
				+ "that sent you here — its package gate IS the confirmation, and it refuses again if the "
				+ "install did not take.");
		}
	}

	// Takes the narrow configuration-build reservation, runs the install without the per-tenant execution
	// monitor, and releases the reservation where the REAL work ends — including the detached continuation
	// past the response deadline — rather than where the tool method returned.
	private CommandExecutionResult RunInstall(
		InstallProcessBuilderOptions options, StrongBox<bool> callerAlreadyAnswered) {
		string tenantKey = ResolveTenantLockKey(options);
		if (!McpToolExecutionLock.TryReserveConfigurationBuild(tenantKey, out McpToolExecutionLock.BuildReservation reservation)) {
			// Caller-actionable refusal (exit 1), not a clio failure: waiting fixes it. Deliberately fails
			// fast — a second install would rebuild and restart an instance that is already being rebuilt.
			return CommandExecutionResult.FromValidationError(
				$"A configuration build is already running on '{options.Environment}' — either an earlier "
				+ $"{InstallProcessBuilderToolName} that is still working server-side, or a compile. Wait for "
				+ "it to finish, then retry the process-designer tool you were using; it refuses again if the "
				+ $"package is still missing, and only then call {InstallProcessBuilderToolName}.");
		}
		try {
			CommandExecutionResult result = InternalExecuteWithoutTenantLock<InstallProcessBuilderCommand>(options);
			if (result.ExitCode != 0 && callerAlreadyAnswered.Value) {
				ReportPostDeadlineFailure(options.Environment, result.ExitCode);
			}
			return result;
		}
		finally {
			McpToolExecutionLock.ReleaseConfigurationBuild(tenantKey, reservation);
		}
	}

	// The command REPORTS failure by returning a non-zero exit code, it does not throw, so the heartbeat's
	// own faulted-task observer never sees it. Past the deadline that exit code has no response to travel on,
	// which would make a failed install indistinguishable from a slow one. stderr is the stdio-MCP-safe
	// diagnostic channel McpProgressHeartbeat.ObserveInBackground uses for the faulted case; best-effort for
	// the same reason it is there — a closed or redirected stream must not raise from a continuation.
	private static void ReportPostDeadlineFailure(string environmentName, int exitCode) {
		try {
			Console.Error.WriteLine(
				$"[{InstallProcessBuilderToolName}] the install on '{environmentName}' FAILED after the "
				+ $"response deadline (exit code {exitCode}); the caller was told it was still running.");
		}
		catch {
			// Best-effort diagnostics only.
		}
	}
}

/// <summary>
/// MCP arguments for the <c>install-process-builder</c> tool.
/// </summary>
public sealed record InstallProcessBuilderArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description(McpToolDescriptions.EnvironmentName)]
	[property: Required]
	string EnvironmentName
);
