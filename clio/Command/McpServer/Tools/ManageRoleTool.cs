using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Command.Administration;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>Environment-aware agent access to role administration.</summary>
[McpServerToolType]
public sealed class ManageRoleTool(ManageRoleCommand command, ILogger logger, IToolCommandResolver resolver)
	: BaseTool<ManageRoleOptions>(command, logger, resolver) {
	internal const string ToolName = "manage-role";
	internal const string InspectToolName = "inspect-role";

	/// <summary>Reads administration state without permitting mutation actions.</summary>
	[McpToolExecution(Location = McpToolExecutionLocation.Worker, Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None, BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None, SharedFileResource = McpToolSharedFileResource.None)]
	[McpServerTool(Name = InspectToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description("Inspect role administration. Actions: list, memberships, members, functional-roles. Read get-guidance name=administration before interpreting inheritance, managers, passwords, licenses or access rules.")]
	public CommandExecutionResult Inspect([Required, Description("Environment and exact action parameters.")] ManageRoleArgs args) {
		if (args.Action is not ("list" or "memberships" or "members" or "functional-roles")) {
			return CommandExecutionResult.FromValidationError("This inspection tool accepts only: list, memberships, members, functional-roles.");
		}
		return Execute(args, false);
	}

	/// <summary>Applies an explicitly selected administration mutation.</summary>
	[McpToolExecution(Location = McpToolExecutionLocation.Worker, Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None, BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None, SharedFileResource = McpToolSharedFileResource.None)]
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
	[Description("Manage role administration through native Creatio services. Read get-guidance name=administration first. remove-functional requires ClioGate 2.0.0.50 or newer in the selected environment; use install-gate to satisfy the requirement. Mutations verify persisted state; inspect after a failure before retrying because partial changes may exist.")]
	public CommandExecutionResult Manage([Required, Description("Environment and exact action parameters.")] ManageRoleArgs args) => Execute(args, true);

	private CommandExecutionResult Execute(ManageRoleArgs args, bool confirm) {
		try {
			return InternalExecute<ManageRoleCommand>(new ManageRoleOptions {
				Environment = args.EnvironmentName,
				Action = args.Action,
				Id = args.Id,
				Name = args.Name,
				Type = args.Type,
				ParentId = args.ParentId,
				UserId = args.UserId,
				FunctionalId = args.FunctionalId,
				Effective = args.Effective,
				Offset = args.Offset,
				Limit = args.Limit,
				Confirm = confirm
			});
		} catch (Exception) {
			return CommandExecutionResult.FromValidationError("Administration execution failed. Check the environment and permissions; inspect state before retrying.");
		}
	}
}

/// <summary>Explicit role administration inputs. Secret values are never accepted as arguments.</summary>
public sealed record ManageRoleArgs {
	/// <summary>Registered environment.</summary>
	[JsonPropertyName("environment-name"), Required, Description(McpToolDescriptions.EnvironmentName)]
	public string EnvironmentName { get; init; }
	/// <summary>list | create | update | delete | ensure-manager | add-member | remove-member | memberships | members | functional-roles | add-functional | remove-functional</summary>
	[JsonPropertyName("action"), Required, Description("list | create | update | delete | ensure-manager | add-member | remove-member | memberships | members | functional-roles | add-functional | remove-functional")]
	public string Action { get; init; }
	/// <summary>Role GUID, new for create.</summary>
	[JsonPropertyName("id"), Description("Role GUID, new for create.")]
	public Guid Id { get; init; }
	/// <summary>Exact name filter or new name.</summary>
	[JsonPropertyName("name"), Description("Exact name filter or new name.")]
	public string Name { get; init; }
	/// <summary>0 organization, 1 division, 2 manager (list only), 3 team, 6 functional.</summary>
	[JsonPropertyName("type"), Description("0 organization, 1 division, 2 manager (list only), 3 team, 6 functional.")]
	public int? Type { get; init; }
	/// <summary>Parent role GUID; required for create/ensure-manager.</summary>
	[JsonPropertyName("parent-id"), Description("Parent role GUID; required for create/ensure-manager.")]
	public Guid? ParentId { get; init; }
	/// <summary>User GUID for memberships.</summary>
	[JsonPropertyName("user-id"), Description("User GUID for memberships.")]
	public Guid UserId { get; init; }
	/// <summary>Functional role GUID to associate.</summary>
	[JsonPropertyName("functional-id"), Description("Functional role GUID to associate.")]
	public Guid FunctionalId { get; init; }
	/// <summary>Read effective memberships.</summary>
	[JsonPropertyName("effective"), Description("Read effective memberships.")]
	public bool Effective { get; init; }
	/// <summary>Read offset.</summary>
	[JsonPropertyName("offset"), Description("Read offset.")]
	public int Offset { get; init; }
	/// <summary>Page size from 1 to 200.</summary>
	[JsonPropertyName("limit"), Description("Page size from 1 to 200.")]
	public int Limit { get; init; } = 100;
}
