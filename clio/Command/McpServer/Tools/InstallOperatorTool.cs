using System.ComponentModel;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>Developer-host operator installation through MCP.</summary>
[McpServerToolType]
[FeatureToggle(ExperimentalFeature.Runtime)]
public sealed class InstallOperatorTool(InstallOperatorCommand command, ILogger logger, IRuntimeMcpHostPolicy host,
	ICredentialPassthroughToolGuard passthroughGuard) : BaseTool<InstallOperatorOptions>(command, logger, passthroughGuard: passthroughGuard) {
	internal const string ToolName = "install-operator";
	/// <summary>Installs the operator into the selected Rancher Desktop cluster without destructive takeover.</summary>
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = true)]
	[McpToolExecution(Location = McpToolExecutionLocation.InProcess, Lifetime = McpToolExecutionLifetime.NotApplicable,
		OperationFamily = McpToolOperationFamily.None, BudgetPolicy = McpToolBudgetPolicy.None,
		RequiresClientRequests = McpToolClientRequests.None, SharedFileResource = McpToolSharedFileResource.None)]
	[Description("Install the Creatio operator on this developer host's Rancher Desktop cluster. Preserves existing infrastructure and does not wipe the cluster. Requires runtime feature and clio mcp-server --runtime-host; unsupported inside containers.")]
	public CommandExecutionResult Install([Description("Installation profile; rancher-desktop only.")] string target,
		[Description("Explicit destination context.")] string context = "rancher-desktop",
		[Description("Optional operator image override; otherwise bundled pinned image.")] string image = null) {
		var rejected = RejectIfPassthroughUnsupported(ToolName, "Use the developer's host-side MCP server with --runtime-host.");
		if (rejected != null) return rejected;
		if (!host.Enabled) return CommandExecutionResult.FromValidationError("Operator installation requires a developer-host server started with clio mcp-server --runtime-host. Container-hosted servers are not supported.");
		return InternalExecute(new InstallOperatorOptions { Target = target, Context = context, Image = image });
	}
}
