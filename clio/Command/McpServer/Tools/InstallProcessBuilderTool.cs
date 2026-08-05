using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
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
	/// UNLIKE <see cref="RestartTool"/>, the work stays under the per-tenant execution lock for its whole
	/// duration rather than being split into a locked request plus a lock-free poll. That is deliberate:
	/// RestartTool's phase 2 is a read-only poll AFTER its write returned, whereas here the install itself is
	/// the long part and it is a WRITE — letting another same-tenant call interleave with a package install
	/// and a restart would be worse than making it wait. The cost is that a same-tenant call does serialize
	/// behind this one; the deadline bounds what the CALLER waits for, not the lock.
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
	             need to install the CrtProcessBuilder package". Then retry the original call.

	             The package ships as source and the target environment compiles it during installation, so
	             this takes longer than a plain package install (roughly 15-75 seconds depending on the
	             environment). You never restart anything yourself, though a restart does happen - the platform
	             recycles itself on .NET Framework, the installer issues it on .NET - and the tool waits for the
	             instance to come back before judging it. It then verifies the OUTCOME rather than the install
	             call: it queries ListUserTasks and fails if ProcessDesignService does not answer, so
	             "installed but never compiled" is reported instead of looking like success.

	             It always installs - there is no skip. Do NOT use `list-packages` to decide whether to call it:
	             Creatio does not rewrite a package's recorded version when re-installing a package it already
	             has, so that version is inert and says nothing about what is running. Re-running is safe and
	             costs one configuration build on the target.

	             Long-running: streams notifications/progress while working. If the MCP response deadline is
	             reached first you get an in-progress note - the install is still running server-side. Do NOT
	             retry immediately; call again later instead (it is idempotent).
	             """)]
	public async Task<CommandExecutionResult> InstallProcessBuilder(
		[Description("install-process-builder parameters")] [Required] InstallProcessBuilderArgs args,
		global::ModelContextProtocol.Server.McpServer server = null,
		RequestContext<CallToolRequestParams> requestContext = null,
		CancellationToken cancellationToken = default) {
		InstallProcessBuilderOptions options = new() {
			Environment = args.EnvironmentName
		};
		try {
			return await McpProgressHeartbeat.RunWithProgressAndDeadlineAsync(
				server,
				requestContext?.Params?.ProgressToken,
				InstallProcessBuilderToolName,
				() => InternalExecute<InstallProcessBuilderCommand>(options),
				deadline: ResponseDeadlineOverride,
				cancellationToken: cancellationToken).ConfigureAwait(false);
		} catch (McpResponseDeadlineExceededException) {
			return CommandExecutionResult.FromInfo(
				$"The {BundledPackages.ProcessBuilderPackageName} install on '{args.EnvironmentName}' is still "
				+ "running server-side: the target is compiling the package and will restart. Do NOT retry now "
				+ "— that would queue behind this install. Wait, then re-run "
				+ $"{InstallProcessBuilderToolName} to confirm ProcessDesignService answers (it is idempotent).");
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
