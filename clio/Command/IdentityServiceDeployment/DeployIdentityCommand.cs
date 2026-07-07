using System;
using CommandLine;
using Clio.Command;
using Clio.Common;

namespace Clio.Command.IdentityServiceDeployment;

/// <summary>
/// Options for the <c>deploy-identity</c> command.
/// </summary>
[Verb("deploy-identity", HelpText = "Deploy IdentityService to IIS and connect it to a Creatio environment")]
[FeatureToggle("deploy-identity")]
public sealed class DeployIdentityOptions : EnvironmentNameOptions
{
	/// <summary>
	/// Gets or sets the IdentityService archive path. The path may point either to a standalone
	/// IdentityService zip or to a Creatio distribution zip containing IdentityService.zip.
	/// </summary>
	[Option("zip-file", Required = false,
		HelpText = "Path to IdentityService.zip or to a Creatio distribution zip that contains IdentityService.zip. Defaults to IdentityService.zip under the registered EnvironmentPath")]
	public string ZipFile { get; set; }

	/// <summary>
	/// Gets or sets the nested IdentityService archive path inside a Creatio distribution archive.
	/// </summary>
	[Option("identity-archive-path-in-bundle", Required = false, Default = "IdentityService.zip",
		HelpText = "Nested IdentityService archive path when --zip-file points to a Creatio distribution bundle")]
	public string IdentityArchivePathInBundle { get; set; }

	/// <summary>
	/// Gets or sets the IIS site and application pool name for IdentityService.
	/// </summary>
	[Option("identity-site-name", Required = false,
		HelpText = "IIS site and application pool name for IdentityService. Defaults to <environment>-identity")]
	public string IdentitySiteName { get; set; }

	/// <summary>
	/// Gets or sets the HTTP port used by the IdentityService IIS site.
	/// </summary>
	[Option("identity-site-port", Required = false,
		HelpText = "HTTP port where IdentityService will listen. Defaults to the first free IIS port in range 40001-40100")]
	public int? IdentitySitePort { get; set; }

	/// <summary>
	/// Gets or sets the target directory where IdentityService files are deployed.
	/// </summary>
	[Option("identity-path", Required = false,
		HelpText = "Target IdentityService directory. Defaults to a sibling <environment>-identity folder")]
	public string IdentityPath { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether an existing target directory can be overwritten.
	/// </summary>
	[Option("overwrite", Required = false, Default = false,
		HelpText = "Overwrite existing IdentityService files in --identity-path")]
	public bool Overwrite { get; set; }

	/// <summary>
	/// Gets or sets how Creatio should be connected to IdentityService.
	/// </summary>
	[Option("configuration-mode", Required = false, Default = "db-first",
		HelpText = "Creatio connection mode: db-first, rest, or db")]
	public string ConfigurationMode { get; set; }

	/// <summary>
	/// Gets or sets the OAuth client display name to create for clio.
	/// </summary>
	[Option("client-name", Required = false, Default = "clio cli",
		HelpText = "OAuth client display name created for clio")]
	public string ClientName { get; set; }

	/// <summary>
	/// Gets or sets the OAuth client application URL.
	/// </summary>
	[Option("client-application-url", Required = false,
		Default = "https://github.com/Advance-Technologies-Foundation/clio.git",
		HelpText = "OAuth client application URL")]
	public string ClientApplicationUrl { get; set; }

	/// <summary>
	/// Gets or sets the OAuth client description.
	/// </summary>
	[Option("client-description", Required = false, Default = "integration for clio cli",
		HelpText = "OAuth client description")]
	public string ClientDescription { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether OAuth app creation should be skipped.
	/// </summary>
	[Option("no-app", Required = false, Default = false,
		HelpText = "Deploy and connect IdentityService without creating a clio OAuth app")]
	public bool NoApp { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether a new technical user should be created for the OAuth client.
	/// </summary>
	[Option("create-tech-user", Required = false, Default = false,
		HelpText = "Create a new technical user for the OAuth app instead of binding it to an existing user")]
	public bool CreateTechUser { get; set; }

	/// <summary>
	/// Gets or sets the existing Creatio system user used by the OAuth client.
	/// </summary>
	[Option("user", Required = false,
		HelpText = "Existing Creatio system user used by the OAuth client. Defaults to Supervisor")]
	public string SystemUser { get; set; }

	/// <summary>
	/// Gets or sets a backward-compatible alias for <see cref="SystemUser"/>.
	/// </summary>
	[Option("system-user", Required = false, Hidden = true, HelpText = "Alias for --user")]
	public string SystemUserAlias {
		get => SystemUser;
		set {
			if (!string.IsNullOrWhiteSpace(value)) {
				SystemUser = value;
			}
		}
	}
}

/// <summary>
/// Deploys IdentityService and connects the target Creatio environment to it.
/// </summary>
public sealed class DeployIdentityCommand : Command<DeployIdentityOptions>
{
	private readonly IIdentityServiceDeploymentService _deploymentService;
	private readonly ILogger _logger;

	/// <summary>
	/// Initializes a new instance of the <see cref="DeployIdentityCommand"/> class.
	/// </summary>
	/// <param name="deploymentService">Service that performs deployment and configuration work.</param>
	/// <param name="logger">Logger used for command output.</param>
	public DeployIdentityCommand(IIdentityServiceDeploymentService deploymentService, ILogger logger) {
		_deploymentService = deploymentService ?? throw new ArgumentNullException(nameof(deploymentService));
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
	}

	/// <inheritdoc />
	public override int Execute(DeployIdentityOptions options) {
		try {
			IdentityServiceDeploymentResult result = _deploymentService.Deploy(options);
			if (!result.Success) {
				_logger.WriteError(result.Message);
				return 1;
			}
			_logger.WriteInfo(result.Message);
			_logger.WriteInfo($"IdentityService URL: {result.IdentityServiceUrl}");
			if (string.IsNullOrWhiteSpace(result.ClientId)) {
				_logger.WriteInfo("OAuth client: skipped");
			} else {
				_logger.WriteInfo($"OAuth client id: {result.ClientId}");
				_logger.WriteInfo("OAuth client secret: <stored in clio settings>");
			}
			return 0;
		}
		catch (Exception ex) {
			_logger.WriteError($"IdentityService deployment failed: {ex.Message}");
			return 1;
		}
	}
}
