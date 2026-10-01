using System.ComponentModel;
using System.Linq;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>Host-side MCP adapters for the experimental runtime command family.</summary>
[McpServerToolType]
[FeatureToggle(ExperimentalFeature.Runtime)]
public sealed class RuntimeTools(RuntimeCommand command, ILogger logger, IRuntimeMcpHostPolicy host,
	ICredentialPassthroughToolGuard passthroughGuard) : BaseTool<RuntimeOptions>(command, logger, passthroughGuard: passthroughGuard) {
	internal const string ImagesName = "runtime-images";
	internal const string ListName = "runtime-list";
	internal const string StatusName = "runtime-status";
	internal const string CreateName = "runtime-create";
	internal const string BuildName = "runtime-build";
	internal const string AttachName = "runtime-attach";
	internal const string DetachName = "runtime-detach";

	private CommandExecutionResult Run(RuntimeOptions options) {
		var rejected = RejectIfPassthroughUnsupported("runtime-" + options.Action, "Use the developer's host-side MCP server with --runtime-host.");
		if (rejected != null) return rejected;
		if (!host.Enabled) return CommandExecutionResult.FromValidationError("Runtime tools require a developer-host server started with clio mcp-server --runtime-host. Container-hosted servers are not supported.");
		var result = InternalExecute(options);
		return result.ExitCode == 0 && !result.Output.Any(message => message is InfoMessage)
			? result with { Output = result.Output.Append(new InfoMessage("Runtime operation completed.")) }
			: result;
	}
	/// <summary>List built Creatio distributions on an explicit cluster, including exact deployable image references and database readiness. Does not download, build or deploy. The output log contains the JSON catalogue.</summary>
	[McpServerTool(Name = ImagesName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
	[McpToolExecution(Location = McpToolExecutionLocation.InProcess, Lifetime = McpToolExecutionLifetime.NotApplicable,
		OperationFamily = McpToolOperationFamily.None, BudgetPolicy = McpToolBudgetPolicy.None,
		RequiresClientRequests = McpToolClientRequests.None, SharedFileResource = McpToolSharedFileResource.None)]
	[Description("List built Creatio distributions on an explicit cluster, including exact deployable image references and database readiness. Does not download, build or deploy. The output log contains the JSON catalogue. Requires runtime feature and --runtime-host; all paths refer to the MCP server host.")]
	public CommandExecutionResult Images([Description("Kubernetes context on this MCP host.")] string context, [Description("Operator namespace.")] string operatorNamespace = "creatio-system") => Run(new RuntimeOptions { Action = "images", Context = context, OperatorNamespace = operatorNamespace, Json = true });
	/// <summary>List deployed Creatio runtimes in the explicit Kubernetes context and namespace.</summary>
	[McpServerTool(Name = ListName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
	[McpToolExecution(Location = McpToolExecutionLocation.InProcess, Lifetime = McpToolExecutionLifetime.NotApplicable,
		OperationFamily = McpToolOperationFamily.None, BudgetPolicy = McpToolBudgetPolicy.None,
		RequiresClientRequests = McpToolClientRequests.None, SharedFileResource = McpToolSharedFileResource.None)]
	[Description("List deployed Creatio runtimes in the explicit Kubernetes context and namespace. Requires runtime feature and --runtime-host; all paths refer to the MCP server host.")]
	public CommandExecutionResult List([Description("Kubernetes context on this MCP host.")] string context, [Description("Runtime namespace.")] string namespaceName = "creatio-runtimes") => Run(new RuntimeOptions { Action = "list", Context = context, Namespace = namespaceName });
	/// <summary>Read the provisioning status and endpoints of an existing Creatio runtime.</summary>
	[McpServerTool(Name = StatusName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
	[McpToolExecution(Location = McpToolExecutionLocation.InProcess, Lifetime = McpToolExecutionLifetime.NotApplicable,
		OperationFamily = McpToolOperationFamily.None, BudgetPolicy = McpToolBudgetPolicy.None,
		RequiresClientRequests = McpToolClientRequests.None, SharedFileResource = McpToolSharedFileResource.None)]
	[Description("Read the provisioning status and endpoints of an existing Creatio runtime. Requires runtime feature and --runtime-host; all paths refer to the MCP server host.")]
	public CommandExecutionResult Status([Description("Runtime name.")] string name, [Description("Kubernetes context.")] string context, [Description("Runtime namespace.")] string namespaceName = "creatio-runtimes") => Run(new RuntimeOptions { Action = "status", Name = name, Context = context, Namespace = namespaceName });
	/// <summary>Create a new Creatio runtime from an exact image reference returned by runtime-images. Returns after submission; use runtime-status to wait for readiness. Never overwrites an existing instance.</summary>
	[McpServerTool(Name = CreateName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true)]
	[McpToolExecution(Location = McpToolExecutionLocation.InProcess, Lifetime = McpToolExecutionLifetime.NotApplicable,
		OperationFamily = McpToolOperationFamily.None, BudgetPolicy = McpToolBudgetPolicy.None,
		RequiresClientRequests = McpToolClientRequests.None, SharedFileResource = McpToolSharedFileResource.None)]
	[Description("Create a new Creatio runtime from an exact image reference returned by runtime-images. Returns after submission; use runtime-status to wait for readiness. Never overwrites an existing instance. Requires runtime feature and --runtime-host; all paths refer to the MCP server host.")]
	public CommandExecutionResult Create([Description("New runtime name.")] string name, [Description("Kubernetes context.")] string context, [Description("Exact repository:tag image reference.")] string image, [Description("Runtime namespace.")] string namespaceName = "creatio-runtimes") => Run(new RuntimeOptions { Action = "create", Name = name, Context = context, Namespace = namespaceName, Image = image });
	/// <summary>Build Creatio images from a ZIP on this developer host. Windows and Rancher Desktop Moby only. Downloads build dependencies and consumes local disk; requires an explicit source path.</summary>
	[McpServerTool(Name = BuildName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true)]
	[McpToolExecution(Location = McpToolExecutionLocation.InProcess, Lifetime = McpToolExecutionLifetime.NotApplicable,
		OperationFamily = McpToolOperationFamily.None, BudgetPolicy = McpToolBudgetPolicy.None,
		RequiresClientRequests = McpToolClientRequests.None, SharedFileResource = McpToolSharedFileResource.None)]
	[Description("Build Creatio images from a ZIP on this developer host. Windows and Rancher Desktop Moby only. Downloads build dependencies and consumes local disk; requires an explicit source path. Requires runtime feature and --runtime-host; all paths refer to the MCP server host.")]
	public CommandExecutionResult Build([Description("Absolute ZIP path on this MCP host, not on the client or pod.")] string source, [Description("Kubernetes context; build supports rancher-desktop only.")] string context) => Run(new RuntimeOptions { Action = "build", Source = source, Context = context });
	/// <summary>Attach a workspace on this developer host to an existing runtime via SSH and Mutagen two-way-safe sync. Requires local Mutagen, kubectl, a trusted SSH alias, prepared FSM and exported packages. Does not deploy an instance.</summary>
	[McpServerTool(Name = AttachName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true)]
	[McpToolExecution(Location = McpToolExecutionLocation.InProcess, Lifetime = McpToolExecutionLifetime.NotApplicable,
		OperationFamily = McpToolOperationFamily.None, BudgetPolicy = McpToolBudgetPolicy.None,
		RequiresClientRequests = McpToolClientRequests.None, SharedFileResource = McpToolSharedFileResource.None)]
	[Description("Attach a workspace on this developer host to an existing runtime via SSH and Mutagen two-way-safe sync. Requires local Mutagen, kubectl, a trusted SSH alias, prepared FSM and exported packages. Does not deploy an instance. Requires runtime feature and --runtime-host; all paths refer to the MCP server host.")]
	public CommandExecutionResult Attach([Description("Runtime name.")] string name, [Description("Absolute existing workspace path on this MCP host.")] string workspace, [Description("Existing trusted SSH alias on this host.")] string sshAlias, [Description("Explicit Kubernetes context.")] string context, [Description("Runtime namespace.")] string namespaceName = "creatio-runtimes", [Description("Discovery provider.")] string provider = "operator-kubernetes", [Description("Attachment mode; only develop supported.")] string mode = "develop") => Run(new RuntimeOptions { Action = "attach", Name = name, Workspace = workspace, SshAlias = sshAlias, Context = context, Namespace = namespaceName, Provider = provider, Mode = mode });
	/// <summary>Stop the Mutagen session owned by a local workspace attachment receipt. Preserves the runtime, database, files and FSM links. Must run on the same host and Mutagen account used to attach.</summary>
	[McpServerTool(Name = DetachName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true)]
	[McpToolExecution(Location = McpToolExecutionLocation.InProcess, Lifetime = McpToolExecutionLifetime.NotApplicable,
		OperationFamily = McpToolOperationFamily.None, BudgetPolicy = McpToolBudgetPolicy.None,
		RequiresClientRequests = McpToolClientRequests.None, SharedFileResource = McpToolSharedFileResource.None)]
	[Description("Stop the Mutagen session owned by a local workspace attachment receipt. Preserves the runtime, database, files and FSM links. Must run on the same host and Mutagen account used to attach. Requires runtime feature and --runtime-host; all paths refer to the MCP server host.")]
	public CommandExecutionResult Detach([Description("Absolute workspace path on this MCP host.")] string workspace) => Run(new RuntimeOptions { Action = "detach", Workspace = workspace });
}
