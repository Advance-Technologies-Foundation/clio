using System.ComponentModel;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>Independent host-side operations over a portable Studio handoff.</summary>
[McpServerToolType, FeatureToggle(ExperimentalFeature.Runtime)]
public sealed class StudioTools(StudioCommand command, ILogger logger, IRuntimeMcpHostPolicy host,
	ICredentialPassthroughToolGuard guard) : BaseTool<StudioOptions>(command, logger, passthroughGuard: guard) {
	private CommandExecutionResult Run(StudioOptions options) {
		var rejected = RejectIfPassthroughUnsupported("studio-" + options.Action, "Use the developer's host-side MCP server with --runtime-host.");
		if (rejected != null) return rejected;
		if (!host.Enabled) return CommandExecutionResult.FromValidationError("Studio tools require a developer-host server started with clio mcp-server --runtime-host. Container-hosted servers are not supported.");
		return InternalExecute(options);
	}

	/// <summary>Submit a portable deployment to the local operator, without sources or image builds.</summary>
	[McpServerTool(Name = "studio-deploy", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = true)]
	[McpToolExecution(Location = McpToolExecutionLocation.InProcess, Lifetime = McpToolExecutionLifetime.NotApplicable,
		OperationFamily = McpToolOperationFamily.None, BudgetPolicy = McpToolBudgetPolicy.None,
		RequiresClientRequests = McpToolClientRequests.None, SharedFileResource = McpToolSharedFileResource.None)]
	[Description("Deploy a trusted portable AI Studio handoff into Rancher Desktop using creatio-operator. No source checkout or image build. Returns Submitted, or InputRequired with every missing input; collect these from the user, write a JSON inputs file on this host and retry. Use studio-status after submission. Requires runtime feature and --runtime-host. All paths refer to this MCP host.")]
	public CommandExecutionResult Deploy([Description("Absolute JSON handoff path.")] string profile,
		[Description("Explicit local Rancher Desktop Kubernetes context.")] string context,
		[Description("Optional JSON inputs file path; secret values must not be command arguments.")] string inputs = null)
		=> Run(new StudioOptions { Action = "deploy", Profile = profile, Context = context, Inputs = inputs });

	/// <summary>Create exact source worktrees without contacting Kubernetes.</summary>
	[McpServerTool(Name = "studio-checkout", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = true)]
	[McpToolExecution(Location = McpToolExecutionLocation.InProcess, Lifetime = McpToolExecutionLifetime.NotApplicable,
		OperationFamily = McpToolOperationFamily.None, BudgetPolicy = McpToolBudgetPolicy.None,
		RequiresClientRequests = McpToolClientRequests.None, SharedFileResource = McpToolSharedFileResource.None)]
	[Description("Check out every source in a portable AI Studio handoff at its exact commit in detached Git worktrees. Independent of deployment and Kubernetes. Uses the host's existing repository access and leaves existing mismatching or dirty worktrees unchanged. Requires runtime feature and --runtime-host; paths refer to this MCP host.")]
	public CommandExecutionResult Checkout([Description("Absolute JSON handoff path.")] string profile,
		[Description("Absolute workspace destination.")] string directory)
		=> Run(new StudioOptions { Action = "checkout", Profile = profile, Directory = directory });

	/// <summary>Read reconciliation status; Ready is workload readiness, not a functional acceptance test.</summary>
	[McpServerTool(Name = "studio-status", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
	[McpToolExecution(Location = McpToolExecutionLocation.InProcess, Lifetime = McpToolExecutionLifetime.NotApplicable,
		OperationFamily = McpToolOperationFamily.None, BudgetPolicy = McpToolBudgetPolicy.None,
		RequiresClientRequests = McpToolClientRequests.None, SharedFileResource = McpToolSharedFileResource.None)]
	[Description("Read operator phase, revision and progress for a Studio installation. Ready means declared Kubernetes resources are ready; functional login and agent checks are separate. Requires runtime feature and --runtime-host.")]
	public CommandExecutionResult Status([Description("Installation name.")] string name,
		[Description("Kubernetes context.")] string context, [Description("Installation namespace.")] string namespaceName)
		=> Run(new StudioOptions { Action = "status", Name = name, Context = context, Namespace = namespaceName });
}
