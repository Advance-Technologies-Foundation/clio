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
				 Review the failing areas from `assert-infrastructure`, prefer the recommended bundle from
				 `show-passing-infrastructure`, and then call `deploy-creatio` with the selected arguments.
				 """)]
	public CommandExecutionResult DeployCreatio(
		[Description("Deployment parameters")] [Required] DeployCreatioArgs args)
	{
		PfInstallerOptions options = new()
		{
			Environment = args.Environment,
			SiteName = args.SiteName,
			ZipFile = args.ZipFile,
			SitePort = args.SitePort,
			DB = args.Db,
			DbServerName = args.DbServerName,
			RedisServerName = args.RedisServerName,
			RedisDb = args.RedisDb ?? -1,
			DropIfExists = args.DropIfExists ?? false,
			DisableResetPassword = args.DisableResetPassword ?? true,
			Platform = args.Platform,
			Product = args.Product,
			DeploymentMethod = args.DeploymentMethod ?? "auto",
			NoIIS = args.NoIis ?? false,
			AppPath = args.AppPath,
			UseHttps = args.UseHttps ?? false,
			CertificatePath = args.CertificatePath,
			CertificatePassword = args.CertificatePassword,
			AutoRun = args.AutoRun ?? true,
			Uri = args.Uri,
			Login = args.Login,
			Password = args.Password,
			ClientId = args.ClientId,
			ClientSecret = args.ClientSecret,
			AuthAppUri = args.AuthAppUri,
			IsNetCore = args.IsNetCore,
			IsSilent = true
		};

		return InternalExecute(options);
	}
}

/// <summary>
/// MCP arguments for the <c>deploy-creatio</c> tool.
/// </summary>
public sealed record DeployCreatioArgs(
	[property: JsonPropertyName("environment")]
	[property: Description("Optional registered environment name")]
	string? Environment,

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

	[property: JsonPropertyName("db")]
	[property: Description("Database engine: pg or mssql")]
	string? Db,

	[property: JsonPropertyName("dbServerName")]
	[property: Description("Optional local database server configuration name; omit to keep the default Kubernetes deployment path")]
	string? DbServerName,

	[property: JsonPropertyName("redisServerName")]
	[property: Description("Optional local Redis server configuration name")]
	string? RedisServerName,

	[property: JsonPropertyName("redisDb")]
	[property: Description("Optional Redis database index; defaults to -1 for auto-detection")]
	int? RedisDb,

	[property: JsonPropertyName("dropIfExists")]
	[property: Description("Automatically drop an existing target database without prompting")]
	bool? DropIfExists,

	[property: JsonPropertyName("disableResetPassword")]
	[property: Description("Disable force-password-reset after installation; defaults to true")]
	bool? DisableResetPassword,

	[property: JsonPropertyName("platform")]
	[property: Description("Runtime platform: net6 or netframework")]
	string? Platform,

	[property: JsonPropertyName("product")]
	[property: Description("Creatio product name or product key")]
	string? Product,

	[property: JsonPropertyName("deploymentMethod")]
	[property: Description("Deployment method: auto, iis, or dotnet")]
	string? DeploymentMethod,

	[property: JsonPropertyName("noIis")]
	[property: Description("Skip IIS on Windows and use dotnet run instead")]
	bool? NoIis,

	[property: JsonPropertyName("appPath")]
	[property: Description("Application installation path")]
	string? AppPath,

	[property: JsonPropertyName("useHttps")]
	[property: Description("Use HTTPS for dotnet deployment")]
	bool? UseHttps,

	[property: JsonPropertyName("certificatePath")]
	[property: Description("Path to the SSL certificate file")]
	string? CertificatePath,

	[property: JsonPropertyName("certificatePassword")]
	[property: Description("Password for the SSL certificate")]
	string? CertificatePassword,

	[property: JsonPropertyName("autoRun")]
	[property: Description("Automatically run the application after deployment; defaults to true")]
	bool? AutoRun,

	[property: JsonPropertyName("uri")]
	[property: Description("Optional Creatio application URI")]
	string? Uri,

	[property: JsonPropertyName("login")]
	[property: Description("Optional Creatio user login")]
	string? Login,

	[property: JsonPropertyName("password")]
	[property: Description("Optional Creatio user password")]
	string? Password,

	[property: JsonPropertyName("clientId")]
	[property: Description("Optional OAuth client ID")]
	string? ClientId,

	[property: JsonPropertyName("clientSecret")]
	[property: Description("Optional OAuth client secret")]
	string? ClientSecret,

	[property: JsonPropertyName("authAppUri")]
	[property: Description("Optional OAuth application URI")]
	string? AuthAppUri,

	[property: JsonPropertyName("isNetCore")]
	[property: Description("Optional flag that indicates whether the target environment runs on .NET Core")]
	bool? IsNetCore
);
