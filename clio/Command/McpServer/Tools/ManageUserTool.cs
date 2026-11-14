using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Command.Administration;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>Environment-aware agent access to user administration.</summary>
[McpServerToolType]
public sealed class ManageUserTool(ManageUserCommand command, ILogger logger, IToolCommandResolver resolver)
	: BaseTool<ManageUserOptions>(command, logger, resolver) {
	internal const string ToolName = "manage-user";
	internal const string InspectToolName = "inspect-user";

	/// <summary>Reads administration state without permitting mutation actions.</summary>
	[McpToolExecution(Location = McpToolExecutionLocation.Worker, Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None, BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None, SharedFileResource = McpToolSharedFileResource.None)]
	[McpServerTool(Name = InspectToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description("Inspect user administration. Actions: list, lock-status. Read get-guidance name=administration before interpreting inheritance, managers, passwords, licenses or access rules.")]
	public CommandExecutionResult Inspect([Required, Description("Environment and exact action parameters.")] ManageUserArgs args) {
		if (args.Action is not ("list" or "lock-status")) {
			return CommandExecutionResult.FromValidationError("This inspection tool accepts only: list, lock-status.");
		}
		return Execute(args, false);
	}

	/// <summary>Applies an explicitly selected administration mutation.</summary>
	// Password references resolve in the host process. Workers intentionally strip inherited secrets.
	[McpToolExecution(Location = McpToolExecutionLocation.InProcess, Lifetime = McpToolExecutionLifetime.NotApplicable,
		OperationFamily = McpToolOperationFamily.None, BudgetPolicy = McpToolBudgetPolicy.None,
		RequiresClientRequests = McpToolClientRequests.None, SharedFileResource = McpToolSharedFileResource.None)]
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
	[Description("Manage user administration through native Creatio services. Create and password actions require Creatio 10.1.585.0 or newer because older native services may log passwords. Read get-guidance name=administration first. Mutations verify persisted state; inspect after a failure before retrying because partial changes may exist.")]
	public CommandExecutionResult Manage([Required, Description("Environment and exact action parameters.")] ManageUserArgs args) => Execute(args, true);

	private CommandExecutionResult Execute(ManageUserArgs args, bool confirm) {
		try {
			return InternalExecute<ManageUserCommand>(new ManageUserOptions {
				Environment = args.EnvironmentName,
				Action = args.Action,
				Id = args.Id,
				UserLogin = args.UserLogin,
				ContactId = args.ContactId,
				Active = args.Active,
				External = args.External,
				PasswordEnvironmentVariable = args.PasswordEnvironmentVariable,
				ForceChangePassword = args.ForceChangePassword,
				Offset = args.Offset,
				Limit = args.Limit,
				Confirm = confirm
			});
		} catch (Exception) {
			return CommandExecutionResult.FromValidationError("Administration execution failed. Check the environment and permissions; inspect state before retrying.");
		}
	}
}

/// <summary>Explicit user administration inputs. Secret values are never accepted as arguments.</summary>
public sealed record ManageUserArgs {
	/// <summary>Registered environment.</summary>
	[JsonPropertyName("environment-name"), Required, Description(McpToolDescriptions.EnvironmentName)]
	public string EnvironmentName { get; init; }
	/// <summary>list | create | update | delete | lock-status | unlock | password</summary>
	[JsonPropertyName("action"), Required, Description("list | create | update | delete | lock-status | unlock | password")]
	public string Action { get; init; }
	/// <summary>Exact account GUID; a new GUID for create.</summary>
	[JsonPropertyName("id"), Description("Exact account GUID; a new GUID for create.")]
	public Guid Id { get; init; }
	/// <summary>Exact login filter or new login.</summary>
	[JsonPropertyName("user-login"), Description("Exact login filter or new login.")]
	public string UserLogin { get; init; }
	/// <summary>Existing contact GUID.</summary>
	[JsonPropertyName("contact-id"), Description("Existing contact GUID.")]
	public Guid? ContactId { get; init; }
	/// <summary>Explicit active state for update.</summary>
	[JsonPropertyName("active"), Description("Explicit active state for update.")]
	public bool? Active { get; init; }
	/// <summary>Create an external user.</summary>
	[JsonPropertyName("external"), Description("Create an external user.")]
	public bool External { get; init; }
	/// <summary>Name of a populated CLIO_ADMIN_PASSWORD_&lt;SUFFIX&gt; process variable; suffix uses uppercase letters, digits or underscores. Never supply the password itself.</summary>
	[JsonPropertyName("password-env"), Description("Name of a populated CLIO_ADMIN_PASSWORD_<SUFFIX> process variable; suffix uses uppercase letters, digits or underscores. Never supply the password itself.")]
	public string PasswordEnvironmentVariable { get; init; }
	/// <summary>Require a password change at next login.</summary>
	[JsonPropertyName("force-change-password"), Description("Require a password change at next login.")]
	public bool ForceChangePassword { get; init; } = true;
	/// <summary>Read offset.</summary>
	[JsonPropertyName("offset"), Description("Read offset.")]
	public int Offset { get; init; }
	/// <summary>Page size from 1 to 200.</summary>
	[JsonPropertyName("limit"), Description("Page size from 1 to 200.")]
	public int Limit { get; init; } = 100;
}
