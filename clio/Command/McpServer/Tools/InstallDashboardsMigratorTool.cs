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
/// MCP tool surface for the <c>install-dashboards-migrator</c> command.
/// </summary>
/// <remarks>
/// Not feature-gated, for the same reason as <see cref="InstallProcessBuilderTool"/>: a gated primitive is
/// filtered out of registration, and an installer that cannot be reached cannot remediate anything.
/// Execution shape — heartbeat, response deadline, the narrow configuration-build reservation — is the same
/// as the process-builder install and for the same reasons; see that tool's remarks.
/// </remarks>
public sealed class InstallDashboardsMigratorTool(
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<InstallDashboardsMigratorOptions>(null, logger, commandResolver) {

	/// <summary>
	/// Stable MCP tool name for installing the bundled dashboards-migrator package.
	/// </summary>
	internal const string InstallDashboardsMigratorToolName = "install-dashboards-migrator";

	/// <summary>
	/// Test seam overriding the MCP response deadline; <see langword="null"/> in production.
	/// </summary>
	internal TimeSpan? ResponseDeadlineOverride { get; set; }

	/// <summary>
	/// Installs (or updates) the bundled dashboards-migrator package into a registered Creatio environment.
	/// </summary>
	[McpServerTool(Name = InstallDashboardsMigratorToolName, ReadOnly = false, Destructive = true,
		Idempotent = true, OpenWorld = false)]
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.Sticky,
		OperationFamily = McpToolOperationFamily.ConfigurationBuild,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillExtended,
		RequiresClientRequests = McpToolClientRequests.Progress,
		SharedFileResource = McpToolSharedFileResource.ConfigurationBuild,
		StartsOperation = true)]
	[Description("""
	             Installs (or updates) the bundled CrtDashboardsMigratorApp package - the "Dashboards migrator"
	             app - into a registered Creatio environment. The app converts Classic UI (7.x) dashboards into
	             Freedom UI dashboards: after installing, the user runs the migration from System Designer
	             ("Dashboards migration") and reviews the result in the Dashboards migration log section.

	             Run this when a Creatio environment still has Classic dashboards that must be moved to Freedom
	             UI and `list-packages` does not show CrtDashboardsMigratorApp, or shows an older version than
	             `get-info` reports for this clio. There is no gated tool that refuses and sends you here; the
	             need comes from the user's task. Confirm the target environment with the user first.

	             The package ships prebuilt - its assembly is included for both .NET Framework and .NET, so
	             the target does not compile it - but the install still runs the platform's configuration
	             build for the package's schemas and the platform restarts afterwards. How long that takes
	             depends entirely on the target - its configuration size, host and current load - so do NOT
	             quote a duration to the user or treat an overrun as a failure. You never restart anything
	             yourself - the platform recycles itself on .NET Framework, the installer issues it on .NET -
	             and the tool waits for the instance to come back before judging it. It then checks the
	             OUTCOME rather than the install call: it asks the package's own service whether it is serving
	             (DashboardsMigratorService Ping, ungated) and fails unless it answers, so a package that was
	             accepted but is not serving is reported instead of looking like success. Note the limit: the
	             check is liveness, not identity, so on an UPGRADE a stale assembly that still answers will
	             pass. Treat a successful install of a NEW version as authoritative only after the migration
	             itself works.

	             The package requires Creatio 8.3.1 or later. This is not checked up front: an older instance
	             accepts the archive and fails the configuration build, which the outcome check then reports.

	             It always installs except in two cases, and re-running is otherwise safe (it costs one
	             configuration build on the target). Both exceptions exist to stop an environment moving
	             BACKWARDS, both report exit code 1, and neither is retryable. The override is the same for
	             both and is deliberately NOT available to you - it is a command-line flag a human runs after
	             deciding the rollback is what they want. Do not reach for a shell to get around either one.

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
	             second call is refused. Wait, then confirm with list-packages and by opening the migration
	             page.
	             """)]
	public async Task<CommandExecutionResult> InstallDashboardsMigrator(
		[Description("install-dashboards-migrator parameters")] [Required] InstallDashboardsMigratorArgs args,
		global::ModelContextProtocol.Server.McpServer server = null,
		RequestContext<CallToolRequestParams> requestContext = null,
		CancellationToken cancellationToken = default) {
		InstallDashboardsMigratorOptions options = new() {
			Environment = args.EnvironmentName
		};
		StrongBox<bool> callerAlreadyAnswered = new(false);
		try {
			return await McpProgressHeartbeat.RunWithProgressAndDeadlineAsync(
				server,
				requestContext?.Params?.ProgressToken,
				InstallDashboardsMigratorToolName,
				() => RunInstall(options, callerAlreadyAnswered),
				deadline: ResponseDeadlineOverride,
				cancellationToken: cancellationToken).ConfigureAwait(false);
		} catch (McpResponseDeadlineExceededException) {
			callerAlreadyAnswered.Value = true;
			return CommandExecutionResult.FromInfo(
				$"The {BundledPackages.DashboardsMigratorPackageName} install on '{args.EnvironmentName}' is still "
				+ "running server-side: the target is compiling the package and will restart. This is NOT a "
				+ "verdict — nothing is confirmed yet, and the install may still fail. Do NOT call "
				+ $"{InstallDashboardsMigratorToolName} again: while this one runs a second call is refused, and "
				+ "it would only trigger another configuration build. Wait, then confirm with list-packages and "
				+ "by opening the Dashboards migration page in System Designer.");
		}
	}

	private CommandExecutionResult RunInstall(
		InstallDashboardsMigratorOptions options, StrongBox<bool> callerAlreadyAnswered) {
		string buildKey = ResolveTargetResourceKey(options);
		if (!McpToolExecutionLock.TryReserveConfigurationBuild(buildKey, out McpToolExecutionLock.BuildReservation reservation)) {
			return CommandExecutionResult.FromValidationError(
				$"A configuration build is already running on '{options.Environment}' — either an earlier "
				+ $"{InstallDashboardsMigratorToolName} that is still working server-side, or a compile. Wait for "
				+ $"it to finish, then check list-packages before calling {InstallDashboardsMigratorToolName} again.");
		}
		try {
			CommandExecutionResult result = InternalExecuteWithoutTenantLock<InstallDashboardsMigratorCommand>(options);
			if (result.ExitCode != 0 && callerAlreadyAnswered.Value) {
				ReportPostDeadlineFailure(options.Environment, result.ExitCode);
			}
			return result;
		}
		finally {
			McpToolExecutionLock.ReleaseConfigurationBuild(buildKey, reservation);
		}
	}

	private static void ReportPostDeadlineFailure(string environmentName, int exitCode) {
		try {
			Console.Error.WriteLine(
				$"[{InstallDashboardsMigratorToolName}] the install on '{environmentName}' FAILED after the "
				+ $"response deadline (exit code {exitCode}); the caller was told it was still running.");
		}
		catch {
			// Best-effort diagnostics only.
		}
	}
}

/// <summary>
/// MCP arguments for the <c>install-dashboards-migrator</c> tool.
/// </summary>
public sealed record InstallDashboardsMigratorArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description(McpToolDescriptions.EnvironmentName)]
	[property: Required]
	string EnvironmentName
);
