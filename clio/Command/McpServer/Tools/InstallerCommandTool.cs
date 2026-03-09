using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Command.CreatioInstallCommand;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool surface for the <c>deploy-creatio</c> command.
/// </summary>
public class InstallerCommandTool(InstallerCommand command, ILogger logger)
	: BaseTool<PfInstallerOptions>(command, logger)
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
				 """)]
	public CommandExecutionResult DeployCreatio(
		[Description("Deployment parameters")] [Required] DeployCreatioArgs args)
	{
		PfInstallerOptions options = new()
		{
			SiteName = args.SiteName,
			ZipFile = args.ZipFile,
			SitePort = args.SitePort,
			DbServerName = args.DbServerName,
			RedisServerName = args.RedisServerName,
			RedisDb = -1,
			DisableResetPassword = true,
			AutoRun = true,
			IsSilent = true,
			DropIfExists = true
		};

		return InternalExecute(options);
	}
}

/// <summary>
/// Minimal MCP arguments for the <c>deploy-creatio</c> tool.
/// </summary>
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
	[property: Description("Port where Creatio will be deployed")]
	[property: Required]
	int SitePort,

	[property: JsonPropertyName("dbServerName")]
	[property: Description("Optional local database server configuration name; omit to keep the default Kubernetes deployment path")]
	string? DbServerName,

	[property: JsonPropertyName("redisServerName")]
	[property: Description("Optional local Redis server configuration name")]
	string? RedisServerName
);
