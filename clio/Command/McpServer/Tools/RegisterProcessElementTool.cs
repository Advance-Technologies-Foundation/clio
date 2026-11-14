using System;
using System.Linq;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>Exposes local package-owned process element registration.</summary>
[McpServerToolType]
public sealed class RegisterProcessElementTool(IProcessElementRegistration registration) {
	internal const string ToolName = "register-process-element";

	/// <summary>Generates both database dialects for an existing workspace user task.</summary>
	/// <param name="args">Local workspace, package, task identity and initial caption.</param>
	/// <returns>The command result containing script paths or validation errors.</returns>
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
	[McpToolExecution(Location = McpToolExecutionLocation.Worker, Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None, BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None, SharedFileResource = McpToolSharedFileResource.None)]
	[Description("Generate package-owned PostgreSQL and SQL Server after-package registration scripts for an existing user-task schema UId. Offline: validates local workspace/package/schema identities and preserves matching existing scripts and their UIds. Conflicting artifacts are rejected without overwrite. Install using push-pkg or push-workspace; pkg-to-db does not execute the scripts. Existing database registrations and captions are preserved. Read get-guidance name=process-custom-elements for the complete authoring workflow.")]
	public CommandExecutionResult RegisterProcessElement([Required] RegisterProcessElementArgs args) {
		try {
			ArgumentNullException.ThrowIfNull(args);
			// This operation is entirely local. BaseTool command resolution would require an unrelated environment.
			var paths = registration.Generate(new RegisterProcessElementOptions {
				WorkspacePath = args.WorkspacePath, PackageName = args.PackageName,
				UserTaskUId = args.UserTaskUId, Caption = args.Caption
			});
			return new CommandExecutionResult(0, paths.Select(path => (LogMessage)new InfoMessage($"Registration script ready: {path}")).ToArray(),
				Note: "Install with push-pkg or push-workspace to apply registration. No environment was changed.");
		} catch (Exception exception) {
			return CommandExecutionResult.FromValidationError(exception.Message);
		}
	}
}

/// <summary>Arguments for local registration script generation.</summary>
/// <param name="WorkspacePath">Explicit local clio workspace root.</param>
/// <param name="PackageName">Workspace package owning the task.</param>
/// <param name="UserTaskUId">Existing user-task schema UId.</param>
/// <param name="Caption">Caption inserted only when no registration exists.</param>
public sealed record RegisterProcessElementArgs(
	[property: JsonPropertyName("workspace-path"), Required, Description("Explicit local clio workspace directory.")] string WorkspacePath,
	[property: JsonPropertyName("package-name"), Required, Description("Package owning the existing user task.")] string PackageName,
	[property: JsonPropertyName("user-task-uid"), Required, Description("Existing user-task schema UId.")] Guid UserTaskUId,
	[property: JsonPropertyName("caption"), Required, Description("Initial toolbox caption; existing installed captions are preserved.")] string Caption);
