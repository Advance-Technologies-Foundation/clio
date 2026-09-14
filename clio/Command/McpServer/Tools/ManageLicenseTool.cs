using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Command.Administration;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>Environment-aware agent access to license administration.</summary>
[McpServerToolType]
public sealed class ManageLicenseTool(ManageLicenseCommand command, ILogger logger, IToolCommandResolver resolver)
	: BaseTool<ManageLicenseOptions>(command, logger, resolver) {
	internal const string ToolName = "manage-license";
	internal const string InspectToolName = "inspect-license";

	/// <summary>Reads administration state without permitting mutation actions.</summary>
	[McpToolExecution(Location = McpToolExecutionLocation.Worker, Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None, BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None, SharedFileResource = McpToolSharedFileResource.None)]
	[McpServerTool(Name = InspectToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description("Inspect license administration. Actions: user-list, role-list. Read get-guidance name=administration before interpreting inheritance, managers, passwords, licenses or access rules.")]
	public CommandExecutionResult Inspect([Required, Description("Environment and exact action parameters.")] ManageLicenseArgs args) {
		if (args.Action is not ("user-list" or "role-list")) {
			return CommandExecutionResult.FromValidationError("This inspection tool accepts only: user-list, role-list.");
		}
		return Execute(args, false);
	}

	/// <summary>Applies an explicitly selected administration mutation.</summary>
	[McpToolExecution(Location = McpToolExecutionLocation.Worker, Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None, BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None, SharedFileResource = McpToolSharedFileResource.None)]
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
	[Description("Manage license administration through native Creatio services. Read get-guidance name=administration first. role-redistribute requires ClioGate 2.0.0.50 or newer in the selected environment; use install-gate to satisfy the requirement. Scheduling is not proof of completed user assignments. Mutations verify persisted state; inspect after a failure before retrying because partial changes may exist.")]
	public CommandExecutionResult Manage([Required, Description("Environment and exact action parameters.")] ManageLicenseArgs args) => Execute(args, true);

	private CommandExecutionResult Execute(ManageLicenseArgs args, bool confirm) {
		try {
			return InternalExecute<ManageLicenseCommand>(new ManageLicenseOptions {
				Environment = args.EnvironmentName,
				Action = args.Action,
				UserId = args.UserId,
				RoleId = args.RoleId,
				PackageId = args.PackageId,
				IncludeManual = args.IncludeManual,
				Offset = args.Offset,
				Limit = args.Limit,
				Confirm = confirm
			});
		} catch (Exception) {
			return CommandExecutionResult.FromValidationError("Administration execution failed. Check the environment and permissions; inspect state before retrying.");
		}
	}
}

/// <summary>Explicit license administration inputs. Secret values are never accepted as arguments.</summary>
public sealed record ManageLicenseArgs {
	/// <summary>Allows redistribution to change manual assignments.</summary>
	[JsonPropertyName("include-manual"), Description("Allow role-redistribute to change manually assigned licenses; defaults to false.")]
	public bool IncludeManual { get; init; }
	/// <summary>Registered environment.</summary>
	[JsonPropertyName("environment-name"), Required, Description(McpToolDescriptions.EnvironmentName)]
	public string EnvironmentName { get; init; }
	/// <summary>user-list | user-assign | user-remove | role-list | role-assign | role-remove | role-redistribute</summary>
	[JsonPropertyName("action"), Required, Description("user-list | user-assign | user-remove | role-list | role-assign | role-remove | role-redistribute")]
	public string Action { get; init; }
	/// <summary>Target user GUID.</summary>
	[JsonPropertyName("user-id"), Description("Target user GUID.")]
	public Guid UserId { get; init; }
	/// <summary>Target role GUID.</summary>
	[JsonPropertyName("role-id"), Description("Target role GUID.")]
	public Guid RoleId { get; init; }
	/// <summary>License package GUID.</summary>
	[JsonPropertyName("package-id"), Description("License package GUID.")]
	public Guid PackageId { get; init; }
	/// <summary>Read offset.</summary>
	[JsonPropertyName("offset"), Description("Read offset.")]
	public int Offset { get; init; }
	/// <summary>Read page size from 1 to 200.</summary>
	[JsonPropertyName("limit"), Description("Read page size from 1 to 200.")]
	public int Limit { get; init; } = 100;
}
