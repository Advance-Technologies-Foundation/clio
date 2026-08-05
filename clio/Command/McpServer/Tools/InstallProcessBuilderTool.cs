using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
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
	/// Installs (or updates) the bundled process-builder package into a registered Creatio environment.
	/// </summary>
	[McpServerTool(Name = InstallProcessBuilderToolName, ReadOnly = false, Destructive = false,
		Idempotent = true, OpenWorld = false)]
	[Description("""
	             Installs (or updates) the bundled CrtProcessBuilder package into a registered Creatio
	             environment, making ProcessDesignService reachable there.

	             Run this when a process-designer tool (`create-business-process`, `modify-business-process`,
	             `describe-business-process`, `list-user-tasks`, `validate-process-graph`) refuses with "you
	             need to install the CrtProcessBuilder package" — whether it is missing entirely or older
	             than the version this clio bundles. Then retry the original call.

	             The package ships as source and the target environment compiles it during installation, so
	             this takes longer than a plain package install (roughly 15-75 seconds depending on the
	             environment). You never restart anything yourself, though a restart does happen - the platform
	             recycles itself on .NET Framework, the installer issues it on .NET - and the tool waits for the
	             instance to come back before judging it. It then
	             verifies the outcome rather than the install call - it queries ListUserTasks and fails if the
	             service does not answer, so "installed but not compiled" is reported instead of looking like
	             success. Re-running against an already-current environment does nothing.
	             """)]
	public CommandExecutionResult InstallProcessBuilder(
		[Description("install-process-builder parameters")] [Required] InstallProcessBuilderArgs args) {
		InstallProcessBuilderOptions options = new() {
			Environment = args.EnvironmentName
		};
		return InternalExecute<InstallProcessBuilderCommand>(options);
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
