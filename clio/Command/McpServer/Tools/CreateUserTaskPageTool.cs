using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>Creates Classic process parameter pages without connecting to an environment.</summary>
[McpServerToolType]
public sealed class CreateUserTaskPageTool(IUserTaskPageScaffolder scaffolder) {
	internal const string ToolName = "create-user-task-page";
	/// <summary>Scaffolds a page, associates it with an existing user task and applies supplied SVG slots.</summary>
	/// <param name="args">Workspace identity, page name/caption, culture and optional SVG files.</param>
	/// <returns>Created source path or an actionable validation failure.</returns>
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
	[McpToolExecution(Location = McpToolExecutionLocation.Worker, Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None, BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None, SharedFileResource = McpToolSharedFileResource.None)]
	[Description("Create an editable Classic parameter page for an existing workspace user task. Read get-guidance name=process-custom-elements before scaffolding or customizing this page. Offline: inherits ProcessFlowElementPropertiesPage, generates MAPPING editors for In/Variable parameters, excludes Out/Internal, and sets the task's parameter-page UId. Refuses an existing page/link. Optional SVG files populate task-owned small/large/title image resources, preserving other resources. Deploy afterward and verify process save/reopen. This is not a Freedom UI page or toolbox registration.")]
	public CommandExecutionResult Create([Required] CreateUserTaskPageArgs args) {
		try {
			ArgumentNullException.ThrowIfNull(args);
			string path = scaffolder.Create(new CreateUserTaskPageOptions {
				WorkspacePath = args.WorkspacePath, PackageName = args.PackageName, UserTaskUId = args.UserTaskUId,
				PageName = args.PageName, Caption = args.Caption, Culture = args.Culture,
				SmallIconPath = args.SmallIconPath, LargeIconPath = args.LargeIconPath, TitleIconPath = args.TitleIconPath
			});
			return new CommandExecutionResult(0, [new InfoMessage($"Created Classic parameter page: {path}")],
				Note: "Only workspace artifacts were changed. Deploy the package/workspace and verify process mappings after save/reopen.");
		} catch (Exception exception) {
			return CommandExecutionResult.FromValidationError(exception.Message);
		}
	}
}

/// <summary>Inputs for one new Classic user-task parameter page.</summary>
/// <param name="WorkspacePath">Explicit workspace root.</param>
/// <param name="PackageName">Owning workspace package.</param>
/// <param name="UserTaskUId">Existing task schema identity.</param>
/// <param name="PageName">New unique page name.</param>
/// <param name="Caption">Page caption.</param>
/// <param name="Culture">Native caption/resource culture.</param>
/// <param name="SmallIconPath">Optional local small SVG.</param>
/// <param name="LargeIconPath">Optional local large SVG.</param>
/// <param name="TitleIconPath">Optional local title SVG.</param>
public sealed record CreateUserTaskPageArgs(
	[property: JsonPropertyName("workspace-path"), Required, Description("Explicit workspace root.")] string WorkspacePath,
	[property: JsonPropertyName("package-name"), Required, Description("Package owning the task.")] string PackageName,
	[property: JsonPropertyName("user-task-uid"), Required, Description("Existing user-task schema UId.")] Guid UserTaskUId,
	[property: JsonPropertyName("page-name"), Required, Description("Unique new Classic page schema name.")] string PageName,
	[property: JsonPropertyName("caption"), Required, Description("Page display caption.")] string Caption,
	[property: JsonPropertyName("culture"), Description("Resource culture, default en-US.")] string Culture = "en-US",
	[property: JsonPropertyName("small-icon-path"), Description("Optional local SVG for the small image.")] string SmallIconPath = null,
	[property: JsonPropertyName("large-icon-path"), Description("Optional local SVG for the diagram image.")] string LargeIconPath = null,
	[property: JsonPropertyName("title-icon-path"), Description("Optional local SVG for the properties header.")] string TitleIconPath = null);

