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
	/// Blocker, ENG-91315, the same reason <c>compile-creatio</c> takes no lock). What genuinely must not
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
	             `describe-business-process`, `list-user-tasks`, `validate-process-graph`) refuses with "you
	             need to install the CrtProcessBuilder package" - whether it is missing entirely or older than
	             the version this clio bundles. Then retry the original call.

	             The package ships as source and the target environment compiles it during installation, so
	             this takes longer than a plain package install (roughly 15-75 seconds depending on the
	             environment). You never restart anything yourself, though a restart does happen - the platform
	             recycles itself on .NET Framework, the installer issues it on .NET - and the tool waits for the
	             instance to come back before judging it. It then checks the OUTCOME rather than the install
	             call: it queries ListUserTasks and fails if ProcessDesignService does not answer, so
	             "installed but never compiled" is reported instead of looking like success.

	             How much that proves depends on the case, so do not over-read a success. On a FIRST install it
	             is conclusive: nothing served before, so an answer can only come from a fresh build. On an
	             UPGRADE it is not: if the new sources fail to compile, the previously built assembly keeps
	             serving and answers this check, so success means "the service works", not "your new version is
	             the one running".

	             It always installs - there is no skip, and re-running is safe (it costs one configuration build
	             on the target). Take the refusal itself as the signal to call this tool rather than comparing
	             versions yourself: `list-packages` reports the version the environment RECORDED, which is what
	             the gate already checks for you.

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
		if (!McpToolExecutionLock.TryReserveConfigurationBuild(tenantKey)) {
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
			McpToolExecutionLock.ReleaseConfigurationBuild(tenantKey);
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
