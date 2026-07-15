using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Command.McpServer.Progress;
using Clio.Common;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool surface for the <c>uninstall-creatio</c> command.
/// </summary>
/// <remarks>
/// Like <c>deploy-creatio</c>, uninstall is a local-only command (no <see cref="IApplicationClient"/>),
/// so it runs through <c>InternalExecute(options)</c> and streams progress by subscribing the injected
/// command's <see cref="IStageEventSource.StageChanged"/> for the run — see
/// <see cref="IStageEventProgressForwarder"/> for the corrected ADR-D4 environment-bound assumption.
/// </remarks>
public class UninstallCreatioTool(
	UninstallCreatioCommand command,
	ILogger logger,
	IStageEventProgressForwarder progressForwarder,
	ModelContextProtocol.Server.McpServer server) : BaseTool<UninstallCreatioCommandOptions>(command, logger) {
	/// <summary>Stable MCP tool name used by discovery and tests.</summary>
	public const string UninstallCreatioToolName = "uninstall-creatio";

	[McpServerTool(Name = UninstallCreatioToolName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false )]
	[Description("""
				 uninstall-creatio command completely removes local Creatio instance from
				 the machine, including the target IIS site or application, database (both
				 local and containerized), and application files. Its application pool is
				 removed only when no other IIS application uses it. On Windows it also attempts
				 to remove the registered IIS application-pool profile. A locked or denied
				 profile is returned as a warning with successful tool completion. When automatic
				 dbHub synchronization is enabled, it removes the clio-owned source after cleanup
				 and before unregistering; an offline or failed dbHub verification is also a
				 non-fatal warning with success-with-warnings completion.
				 A shared application pool and its profile are preserved.

				 The command reads the database connection string from ConnectionStrings.config
				 in the Creatio installation directory and uses it to connect and drop the
				 database. This works for both local databases (PostgreSQL, MSSQL) and
				 containerized databases (Kubernetes/Rancher).
				 """)]
	public CommandExecutionResult UninstallCreatio(
		RequestContext<CallToolRequestParams> requestContext,
		[Description("Uninstall parameters")] [Required] UninstallCreatioArgs args
	) => UninstallCreatio(requestContext.Params?.ProgressToken, args);

	// Progress-token overload kept internal so unit tests can drive it without constructing a
	// RequestContext<CallToolRequestParams> (which requires a live McpServer).
	internal CommandExecutionResult UninstallCreatio(ProgressToken? progressToken, UninstallCreatioArgs args) {

		UninstallCreatioCommandOptions options = new() {
			EnvironmentName = args.EnvironmentName
		};

		// Subscribe the injected command (an IStageEventSource) so each ClioStageEvent is forwarded as a
		// notifications/progress with the typed envelope in _meta.clioStageEvent. No-op when the caller
		// did not send a ProgressToken.
		using System.IDisposable subscription = progressForwarder.Subscribe(
			command,
			progressToken,
			notification => server.SendNotificationAsync(
				NotificationMethods.ProgressNotification, notification).GetAwaiter().GetResult());

		return InternalExecute(options);
	}
}

public record UninstallCreatioArgs(
	[property:JsonPropertyName("environment-name")][Description("Creatio environment name to uninstall")] [Required] string EnvironmentName
);
