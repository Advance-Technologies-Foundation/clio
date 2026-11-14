using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Command.IdentityServiceDeployment;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>MCP adapter for removal of the recorded local identity component.</summary>
[McpServerToolType]
[FeatureToggle("deploy-identity")]
public sealed class UninstallIdentityTool(ILogger logger, IToolCommandResolver commandResolver)
	: BaseTool<UninstallIdentityOptions>(null, logger, commandResolver) {
	/// <summary>Stable discovery name for identity removal.</summary>
	public const string ToolName = "uninstall-identity";

	/// <summary>Removes only the environment's recorded identity, preserving CRM and its database.</summary>
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
	[McpToolExecution(Location = McpToolExecutionLocation.Worker, Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None, BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None, SharedFileResource = McpToolSharedFileResource.None)]
	[Description("Removes the optional IdentityService recorded in a local environment's appsettings: its verified IIS target, unused pool and files. Preserves Creatio and its database. Clears matching CRM references before stopping identity and matching Clio OAuth credentials afterwards. Empty attachment is a no-op; ambiguous or changed targets fail before deletion. Shared pools survive. Failed cleanup retains the attachment for retry. Recovery option skip-crm-cleanup intentionally leaves CRM system settings unchanged and reports a warning.")]
	public CommandExecutionResult Uninstall([Required] UninstallIdentityArgs args) {
		if (string.IsNullOrWhiteSpace(args.EnvironmentName)) {
			return CommandExecutionResult.FromValidationError("environment-name is required and cannot be empty.");
		}
		return InternalExecute<UninstallIdentityCommand>(new UninstallIdentityOptions {
			Environment = args.EnvironmentName, SkipCrmCleanup = args.SkipCrmCleanup
		});
	}
}

/// <summary>Arguments for standalone identity cleanup.</summary>
/// <param name="EnvironmentName">Registered environment whose identity is removed.</param>
/// <param name="SkipCrmCleanup">Explicit recovery opt-in leaving CRM system settings unchanged.</param>
public sealed record UninstallIdentityArgs(
	[property: JsonPropertyName("environment-name"), Required, Description("Registered owner of the local identity component")]
	string EnvironmentName,
	[property: JsonPropertyName("skip-crm-cleanup"), Description("Recovery only: leave CRM system settings unchanged and warn")]
	bool SkipCrmCleanup = false);
