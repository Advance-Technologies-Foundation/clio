using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Command;
using Clio.Command.IdentityServiceDeployment;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool surface for the <c>deploy-identity</c> command.
/// </summary>
[McpServerToolType]
[FeatureToggle("deploy-identity")]
public sealed class DeployIdentityTool(
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<DeployIdentityOptions>(null, logger, commandResolver)
{
	/// <summary>
	/// Stable MCP tool name for IdentityService deployment.
	/// </summary>
	internal const string DeployIdentityToolName = "deploy-identity";

	/// <summary>
	/// Deploys IdentityService to IIS and connects Creatio to it.
	/// </summary>
	[McpServerTool(Name = DeployIdentityToolName, ReadOnly = false, Destructive = true, Idempotent = false,
		OpenWorld = false)]
	[Description("""
				 Deploys IdentityService to IIS, connects the target Creatio environment, creates a fresh clio OAuth
				 client bound to an existing Creatio user, and stores the returned client credentials in the local
				 clio appsettings environment.

				 When zipFile is omitted, the command finds IdentityService.zip under the registered environment
				 EnvironmentPath. When identitySitePort is omitted, the command selects the first free IIS port
				 in range 40001-40100. Explicit zipFile and identitySitePort values still win.
				 Set noApp to deploy and connect IdentityService without creating an OAuth app or verifying
				 client_credentials. Set createTechUser to create a new technical user instead of binding the
				 fresh OAuth app to the existing Supervisor user.
				 Secret values are written only to clio settings and are not returned in the tool response.
				 """)]
	public CommandExecutionResult DeployIdentity(
		[Description("Deployment parameters")]
		[Required]
		DeployIdentityArgs args)
	{
		DeployIdentityOptions options = new() {
			Environment = args.EnvironmentName,
			ZipFile = args.ZipFile,
			IdentityArchivePathInBundle = args.IdentityArchivePathInBundle ?? "IdentityService.zip",
			IdentitySiteName = args.IdentitySiteName,
			IdentitySitePort = args.IdentitySitePort,
			IdentityPath = args.IdentityPath,
			Overwrite = args.Overwrite,
			ConfigurationMode = args.ConfigurationMode ?? "db-first",
			ClientName = args.ClientName ?? "clio cli",
			ClientApplicationUrl = args.ClientApplicationUrl
				?? "https://github.com/Advance-Technologies-Foundation/clio.git",
			ClientDescription = args.ClientDescription ?? "integration for clio cli",
			NoApp = args.NoApp,
			CreateTechUser = args.CreateTechUser,
			SystemUser = args.User
		};

		return InternalExecute<DeployIdentityCommand>(options);
	}
}

/// <summary>
/// MCP arguments for the <c>deploy-identity</c> tool.
/// </summary>
public sealed record DeployIdentityArgs(
	[property: JsonPropertyName("environmentName")]
	[property: Description("Registered clio environment name")]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("zipFile")]
	[property: Description("Optional path to IdentityService.zip or to a Creatio distribution bundle. Defaults to IdentityService.zip under EnvironmentPath")]
	string? ZipFile = null,

	[property: JsonPropertyName("identitySitePort")]
	[property: Description("Optional HTTP port where IdentityService will listen. Defaults to first free IIS port in range 40001-40100")]
	int? IdentitySitePort = null,

	[property: JsonPropertyName("identityArchivePathInBundle")]
	[property: Description("Nested IdentityService archive path when zipFile is a Creatio bundle")]
	string? IdentityArchivePathInBundle = "IdentityService.zip",

	[property: JsonPropertyName("identitySiteName")]
	[property: Description("Optional IIS site and app pool name. Defaults to <environment>-identity")]
	string? IdentitySiteName = null,

	[property: JsonPropertyName("identityPath")]
	[property: Description("Optional target directory for IdentityService files")]
	string? IdentityPath = null,

	[property: JsonPropertyName("overwrite")]
	[property: Description("Overwrite the target IdentityService directory when it already exists")]
	bool Overwrite = false,

	[property: JsonPropertyName("configurationMode")]
	[property: Description("Creatio connection mode: db-first, rest, or db")]
	string? ConfigurationMode = "db-first",

	[property: JsonPropertyName("clientName")]
	[property: Description("OAuth client display name created for clio")]
	string? ClientName = "clio cli",

	[property: JsonPropertyName("clientApplicationUrl")]
	[property: Description("OAuth client application URL")]
	string? ClientApplicationUrl = "https://github.com/Advance-Technologies-Foundation/clio.git",

	[property: JsonPropertyName("clientDescription")]
	[property: Description("OAuth client description")]
	string? ClientDescription = "integration for clio cli",

	[property: JsonPropertyName("noApp")]
	[property: Description("Deploy and connect IdentityService without creating an OAuth app")]
	bool NoApp = false,

	[property: JsonPropertyName("createTechUser")]
	[property: Description("Create a new technical user for the OAuth app instead of binding it to an existing user")]
	bool CreateTechUser = false,

	[property: JsonPropertyName("user")]
	[property: Description("Existing Creatio system user used by the OAuth client. Defaults to Supervisor")]
	string? User = null
);
