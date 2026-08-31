using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Command.CreatioInstallCommand;
using Clio.Command.McpServer.Progress;
using Clio.Common;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool surface for the <c>deploy-creatio</c> command.
/// </summary>
/// <remarks>
/// The tool executes the injected command via <c>InternalExecute(options)</c> (not
/// <c>InternalExecute&lt;TCommand&gt;</c>): ADR D4 assumed deploy was environment-bound, but the
/// command is local-only (no <see cref="IApplicationClient"/>; it CREATES the instance), so it is
/// resolved environmentlessly. Progress is streamed by subscribing the injected command's
/// <see cref="IStageEventSource.StageChanged"/> for the duration of the run.
/// </remarks>
public class InstallerCommandTool(
	InstallerCommand command,
	ILogger logger,
	IStageEventProgressForwarder progressForwarder,
	ModelContextProtocol.Server.McpServer server,
	IDbOperationLogContextAccessor dbOperationLogContextAccessor = null)
	: BaseTool<PfInstallerOptions>(command, logger, dbOperationLogContextAccessor: dbOperationLogContextAccessor)
{
	/// <summary>
	/// Stable MCP tool name for Creatio deployment.
	/// </summary>
	internal const string DeployCreatioToolName = "deploy-creatio";

	/// <summary>
	/// Deploys Creatio from a zip archive using the same execution path as the CLI command.
	/// </summary>
	[McpServerTool(Name = DeployCreatioToolName, ReadOnly = false, Destructive = true, Idempotent = false,
		OpenWorld = false)]
	[Description("""
				 Deploys Creatio from a zip archive using the real deploy-creatio command path.

				 Before calling this tool, first run `assert-infrastructure` for full infrastructure visibility,
				 then run `show-passing-infrastructure` for deployable choices and recommendations.
				 If you are deploying locally to IIS, also run `find-empty-iis-port` to choose a safe `sitePort`.
				 Review the failing areas from `assert-infrastructure`, prefer the recommended bundle from
				 `show-passing-infrastructure`, and then call `deploy-creatio` with the selected arguments.
				 For an IIS deployment, clio reserves and revalidates `sitePort` across concurrent clio
				 processes before changing files, databases, or IIS. A collision fails the deployment;
				 deploy and uninstall are also serialized by environment name and physical target directory.
				 Deployments on different names, ports, and target directories can proceed in parallel.
				 Deployment preserves the build database's existing forced-password-change state and does not
				 clear it automatically.
				 For local IIS, `useHttps` prefers a matching usable LocalMachine/My certificate and falls back
				 to HTTP with a warning when no usable certificate is installed.
				 For dotnet deployment, set `deployment` to `dotnet` (or rely on automatic selection on macOS/Linux).
				 `useHttps` then requires `certificatePath` or an existing Kestrel certificate configuration;
				 PEM/DER certificate files also require `certificateKeyPath`. `bindAllInterfaces` is an explicit
				 network-exposure opt-in and requires HTTPS; loopback remains the default. For a PFX,
				 `certificatePassword` is the name of an environment variable (not the password value), or
				 use `certificatePasswordFile`. The resolved password is passed only to the deployed host
				 through Kestrel environment configuration; never echo or log it.
				 When local dbHub synchronization is enabled, deployment reconciles its database source only after
				 readiness succeeds; a dbHub warning is non-fatal and produces success-with-warnings progress.
				 """)]
	public CommandExecutionResult DeployCreatio(
		RequestContext<CallToolRequestParams> requestContext,
		[Description("Deployment parameters")] [Required] DeployCreatioArgs args)
		=> DeployCreatio(requestContext.Params?.ProgressToken, args);

	// Progress-token overload kept internal so unit tests can drive it without constructing a
	// RequestContext<CallToolRequestParams> (which requires a live McpServer).
	internal CommandExecutionResult DeployCreatio(ProgressToken? progressToken, DeployCreatioArgs args)
	{
		PfInstallerOptions options = new()
		{
			SiteName = args.SiteName,
			ZipFile = args.ZipFile,
			SitePort = args.SitePort,
			DbServerName = args.DbServerName,
			RedisServerName = args.RedisServerName,
			UseHttps = args.UseHttps,
			DeploymentMethod = args.DeploymentMethod,
			BindAllInterfaces = args.BindAllInterfaces,
			CertificatePath = args.CertificatePath,
			CertificateKeyPath = args.CertificateKeyPath,
			CertificatePassword = args.CertificatePassword,
			CertificatePasswordFile = args.CertificatePasswordFile,
			RedisDb = -1,
			DisableResetPassword = false,
			AutoRun = true,
			IsSilent = true,
			DropIfExists = true
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

/// <summary>
/// Minimal MCP arguments for the <c>deploy-creatio</c> tool.
/// </summary>
/// <param name="SiteName">Creatio instance name.</param>
/// <param name="ZipFile">Path to the Creatio archive file.</param>
/// <param name="SitePort">Port where Creatio will be deployed.</param>
/// <param name="DbServerName">Optional local database server configuration name.</param>
/// <param name="RedisServerName">Optional local Redis server configuration name.</param>
/// <param name="UseHttps">Whether to use HTTPS; dotnet deployment requires certificate settings.</param>
/// <param name="DeploymentMethod">Optional deployment method: auto, iis, or dotnet.</param>
/// <param name="BindAllInterfaces">Whether dotnet HTTPS hosting may listen on all network interfaces.</param>
/// <param name="CertificatePath">Optional PFX, PEM, or DER certificate path for dotnet HTTPS.</param>
/// <param name="CertificateKeyPath">Optional private-key path for a PEM or DER certificate.</param>
/// <param name="CertificatePassword">Optional name of an environment variable containing the PFX password; never pass the raw password.</param>
/// <param name="CertificatePasswordFile">Optional path to a file containing the PFX password.</param>
public sealed record DeployCreatioArgs(
	[property: JsonPropertyName("siteName")]
	[property: Description("Creatio instance name")]
	[property: Required]
	string SiteName,

	[property: JsonPropertyName("zipFile")]
	[property: Description("Path to the Creatio archive file")]
	[property: Required]
	string ZipFile,

	[property: JsonPropertyName("sitePort")]
	[property: Description("Port where Creatio will be deployed; IIS deployments reserve and validate it before mutation")]
	[property: Required]
	int SitePort,

	[property: JsonPropertyName("dbServerName")]
	[property: Description("Optional local database server configuration name; omit to keep the default Kubernetes deployment path")]
	string? DbServerName,

	[property: JsonPropertyName("redisServerName")]
	[property: Description("Optional local Redis server configuration name")]
	string? RedisServerName,

	[property: JsonPropertyName("useHttps")]
	[property: Description("For IIS, prefer HTTPS and fall back to HTTP when no usable certificate is installed; for dotnet, require certificatePath or existing Kestrel certificate settings")]
	bool UseHttps = false,

	[property: JsonPropertyName("deployment")]
	[property: Description("Optional deployment method: auto, iis, or dotnet. Use dotnet to select Kestrel explicitly")]
	string? DeploymentMethod = null,

	[property: JsonPropertyName("bindAllInterfaces")]
	[property: Description("Optional dotnet-only opt-in to bind Kestrel HTTPS on all network interfaces; loopback is the default")]
	bool BindAllInterfaces = false,

	[property: JsonPropertyName("certificatePath")]
	[property: Description("Optional dotnet HTTPS certificate path (.pfx, PEM .pem/.crt, or DER .crt); PEM/DER requires certificateKeyPath")]
	string? CertificatePath = null,

	[property: JsonPropertyName("certificateKeyPath")]
	[property: Description("Optional private-key path for a dotnet PEM or DER certificate")]
	string? CertificateKeyPath = null,

	[property: JsonPropertyName("certificatePassword")]
	[property: Description("Optional name of an environment variable containing the dotnet PFX password; do not pass the raw password")]
	string? CertificatePassword = null,

	[property: JsonPropertyName("certificatePasswordFile")]
	[property: Description("Optional path to a file containing the dotnet PFX password")]
	string? CertificatePasswordFile = null
);
