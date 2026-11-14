using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Command.Administration;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>Environment-aware agent access to access administration.</summary>
[McpServerToolType]
public sealed class ManageAccessTool(ManageAccessCommand command, ILogger logger, IToolCommandResolver resolver)
	: BaseTool<ManageAccessOptions>(command, logger, resolver) {
	internal const string ToolName = "manage-access";
	internal const string InspectToolName = "inspect-access";

	/// <summary>Reads administration state without permitting mutation actions.</summary>
	[McpToolExecution(Location = McpToolExecutionLocation.Worker, Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None, BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None, SharedFileResource = McpToolSharedFileResource.None)]
	[McpServerTool(Name = InspectToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description("Inspect access administration. Actions: ip-list, delegations, operations, operation-grants. Read get-guidance name=administration before interpreting inheritance, managers, passwords, licenses or access rules.")]
	public CommandExecutionResult Inspect([Required, Description("Environment and exact action parameters.")] ManageAccessArgs args) {
		if (args.Action is not ("ip-list" or "delegations" or "operations" or "operation-grants")) {
			return CommandExecutionResult.FromValidationError("This inspection tool accepts only: ip-list, delegations, operations, operation-grants.");
		}
		return Execute(args, false);
	}

	/// <summary>Applies an explicitly selected administration mutation.</summary>
	[McpToolExecution(Location = McpToolExecutionLocation.Worker, Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None, BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None, SharedFileResource = McpToolSharedFileResource.None)]
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
	[Description("Manage access administration through native Creatio services. Operation-position requires Creatio 10.1.585.0 and ClioGate 2.0.0.52 or newer to invalidate native rights caches. Read get-guidance name=administration first. Mutations verify persisted state; inspect after a failure before retrying because partial changes may exist.")]
	public CommandExecutionResult Manage([Required, Description("Environment and exact action parameters.")] ManageAccessArgs args) => Execute(args, true);

	private CommandExecutionResult Execute(ManageAccessArgs args, bool confirm) {
		try {
			return InternalExecute<ManageAccessCommand>(new ManageAccessOptions {
				Environment = args.EnvironmentName,
				Action = args.Action,
				Id = args.Id,
				UnitId = args.UnitId,
				GrantorId = args.GrantorId,
				OperationId = args.OperationId,
				Code = args.Code,
				BeginIp = args.BeginIp,
				EndIp = args.EndIp,
				Position = args.Position,
				Offset = args.Offset,
				Limit = args.Limit,
				Confirm = confirm
			});
		} catch (Exception) {
			return CommandExecutionResult.FromValidationError("Administration execution failed. Check the environment and permissions; inspect state before retrying.");
		}
	}
}

/// <summary>Explicit access administration inputs. Secret values are never accepted as arguments.</summary>
public sealed record ManageAccessArgs {
	/// <summary>Registered environment.</summary>
	[JsonPropertyName("environment-name"), Required, Description(McpToolDescriptions.EnvironmentName)]
	public string EnvironmentName { get; init; }
	/// <summary>Operation: ip-list, ip-create, ip-update, ip-delete, delegations, delegate, revoke-delegation, operations, operation-grants, grant-operation, deny-operation, revoke-operation, operation-position.</summary>
	[JsonPropertyName("action"), Required, Description("Operation: ip-list, ip-create, ip-update, ip-delete, delegations, delegate, revoke-delegation, operations, operation-grants, grant-operation, deny-operation, revoke-operation, operation-position.")]
	public string Action { get; init; }
	/// <summary>IP rule or operation grant record GUID.</summary>
	[JsonPropertyName("id"), Description("IP rule or operation grant record GUID.")]
	public Guid Id { get; init; }
	/// <summary>Target user or role GUID; grantee for delegation and operation permissions.</summary>
	[JsonPropertyName("unit-id"), Description("Target user or role GUID; grantee for delegation and operation permissions.")]
	public Guid UnitId { get; init; }
	/// <summary>User or role whose rights are delegated to the target user.</summary>
	[JsonPropertyName("grantor-id"), Description("User or role whose rights are delegated to the target user.")]
	public Guid GrantorId { get; init; }
	/// <summary>System operation GUID.</summary>
	[JsonPropertyName("operation-id"), Description("System operation GUID.")]
	public Guid OperationId { get; init; }
	/// <summary>Exact operation code filter.</summary>
	[JsonPropertyName("code"), Description("Exact operation code filter.")]
	public string Code { get; init; }
	/// <summary>Beginning canonical IPv4 address.</summary>
	[JsonPropertyName("begin-ip"), Description("Beginning canonical IPv4 address.")]
	public string BeginIp { get; init; }
	/// <summary>Ending canonical IPv4 address.</summary>
	[JsonPropertyName("end-ip"), Description("Ending canonical IPv4 address.")]
	public string EndIp { get; init; }
	/// <summary>Zero-based operation grant priority.</summary>
	[JsonPropertyName("position"), Description("Zero-based operation grant priority.")]
	public int? Position { get; init; }
	/// <summary>Read offset.</summary>
	[JsonPropertyName("offset"), Description("Read offset.")]
	public int Offset { get; init; }
	/// <summary>Read page size from 1 to 200.</summary>
	[JsonPropertyName("limit"), Description("Read page size from 1 to 200.")]
	public int Limit { get; init; } = 100;
}
