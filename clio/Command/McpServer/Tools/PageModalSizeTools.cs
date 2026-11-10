using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>MCP front-end of <see cref="GetPageModalSizeCommand"/>.</summary>
[McpServerToolType]
public sealed class GetPageModalSizeTool(
	GetPageModalSizeCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<GetPageModalSizeOptions>(command, logger, commandResolver) {

	internal const string ToolName = "get-page-modal-size";

	internal const string ValidArguments =
		"Valid arguments: page-schema-name, package-name, environment-name (uri, login, password as an emergency fallback).";

	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	[Description("Read a modal window's size. " +
		"Applies to Freedom UI modal pages (pages inheriting BaseMiniPageTemplate). " +
		"The size lives in the page's AppearanceSettings add-on, NOT in the page body: layoutConfig.width of the 'Main' container is not the window size. " +
		"Returns size (small/medium/large/extra-large, absent when none is set), sourcePackageName (the add-on layer size comes from), effectiveSize (what the window opens with, including the runtime default), effectiveSourcePackageName (on an override: the package that sets effectiveSize) and availableSizes. " +
		"Refuses a page that is not a modal page. Read-only. Call it before set-page-modal-size to name the current size to the user. " +
		"Call get-guidance with name page-modal-size to learn the full flow. Prefer environment-name; keep direct connection args for emergency fallback only.")]
	public GetPageModalSizeResponse GetPageModalSize(
		[Description("Parameters: page-schema-name, package-name (required); environment-name preferred; uri/login/password emergency fallback only.")]
		[Required] GetPageModalSizeArgs args) {
		if (args is null) {
			return new GetPageModalSizeResponse { Success = false, Error = "args is required. " + ValidArguments };
		}
		// A misspelled key would otherwise be dropped by the binder and the call would read another page or fail
		// with a misleading "required" error.
		string? argumentError = McpToolArgumentSupport.BuildUnknownArgumentError(args.ExtensionData, ValidArguments);
		if (!string.IsNullOrWhiteSpace(argumentError)) {
			return new GetPageModalSizeResponse { Success = false, Error = argumentError };
		}
		GetPageModalSizeOptions options = new() {
			PageSchemaName = args.PageSchemaName,
			PackageName = args.PackageName,
			Environment = args.EnvironmentName,
			Uri = args.Uri,
			Login = args.Login,
			Password = args.Password
		};
		return ExecuteWithCleanLog(options, () => {
			GetPageModalSizeCommand resolvedCommand;
			try {
				resolvedCommand = ResolveCommand<GetPageModalSizeCommand>(options);
			} catch (Exception ex) {
				return new GetPageModalSizeResponse { Success = false, Error = ex.Message };
			}
			resolvedCommand.TryGet(options, out GetPageModalSizeResponse response);
			return response;
		});
	}
}

/// <summary>MCP front-end of <see cref="SetPageModalSizeCommand"/>.</summary>
[McpServerToolType]
public sealed class SetPageModalSizeTool(
	SetPageModalSizeCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<SetPageModalSizeOptions>(command, logger, commandResolver) {

	internal const string ToolName = "set-page-modal-size";

	internal const string ValidArguments =
		"Valid arguments: page-schema-name, package-name, size, environment-name (uri, login, password as an emergency fallback).";

	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	[Description("Set a modal window's size. " +
		"The ONLY way to resize a Freedom UI modal page (a page inheriting BaseMiniPageTemplate). " +
		"Never resize a modal window through the page body: layoutConfig.width of 'Main', inner container widths and CSS do not change the window size. " +
		"Writes modalSettings.size to the page's AppearanceSettings add-on in package-name (creating the add-on when the page has none), keeps every other add-on field, and rebuilds static content. " +
		"size accepts the Page Designer options: small (S), medium (M), large (L), extra-large (XL); any other value is refused with the accepted list. " +
		"When the user's request is vague ('wider', 'bigger'), do NOT pick a size: read the current one with get-page-modal-size, offer the options, and write only the one the user chooses. " +
		"Returns previousSize, previousSourcePackageName and size. Refuses a page that is not a modal page, and a write another package overrides; the refusal names that package. " +
		"Call get-guidance with name page-modal-size to learn the full flow. Prefer environment-name; keep direct connection args for emergency fallback only.")]
	public SetPageModalSizeResponse SetPageModalSize(
		[Description("Parameters: page-schema-name, package-name, size (required); environment-name preferred; uri/login/password emergency fallback only.")]
		[Required] SetPageModalSizeArgs args) {
		if (args is null) {
			return new SetPageModalSizeResponse { Success = false, Error = "args is required. " + ValidArguments };
		}
		// A misspelled key ("window-size") would otherwise be dropped by the binder; refuse it before any write.
		string? argumentError = McpToolArgumentSupport.BuildUnknownArgumentError(args.ExtensionData, ValidArguments);
		if (!string.IsNullOrWhiteSpace(argumentError)) {
			return new SetPageModalSizeResponse { Success = false, Error = argumentError };
		}
		SetPageModalSizeOptions options = new() {
			PageSchemaName = args.PageSchemaName,
			PackageName = args.PackageName,
			Size = args.Size,
			Environment = args.EnvironmentName,
			Uri = args.Uri,
			Login = args.Login,
			Password = args.Password
		};
		return ExecuteWithCleanLog(options, () => {
			SetPageModalSizeCommand resolvedCommand;
			try {
				resolvedCommand = ResolveCommand<SetPageModalSizeCommand>(options);
			} catch (Exception ex) {
				return new SetPageModalSizeResponse { Success = false, Error = ex.Message };
			}
			resolvedCommand.TrySet(options, out SetPageModalSizeResponse response);
			return response;
		});
	}
}

/// <summary>
/// Connection args and the unknown-field bag shared by the page modal size tools.
/// </summary>
public abstract record PageModalSizeArgsBase : ConnectionArgsBase {
	/// <summary>Overflow bag for unknown JSON fields; a non-empty bag refuses the call.</summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

/// <summary>Arguments of <c>get-page-modal-size</c>.</summary>
public sealed record GetPageModalSizeArgs(
	[property: JsonPropertyName("page-schema-name")]
	[property: Description("Modal page schema name, e.g. 'UsrOrder_MiniPage'. When the user names the window by its title or by the button that opens it, find the page through that button's crt.OpenPageRequest schemaName.")]
	[property: Required]
	string PageSchemaName,

	[property: JsonPropertyName("package-name")]
	[property: Description("Package the page is read from — the app's editable package.")]
	[property: Required]
	string PackageName
) : PageModalSizeArgsBase;

/// <summary>Arguments of <c>set-page-modal-size</c>.</summary>
public sealed record SetPageModalSizeArgs(
	[property: JsonPropertyName("page-schema-name")]
	[property: Description("Modal page schema name, e.g. 'UsrOrder_MiniPage'.")]
	[property: Required]
	string PageSchemaName,

	[property: JsonPropertyName("package-name")]
	[property: Description("Editable package the AppearanceSettings add-on is written to — the app's package, also for a vendor page.")]
	[property: Required]
	string PackageName,

	[property: JsonPropertyName("size")]
	[property: Description("Window size: small (S), medium (M), large (L) or extra-large (XL).")]
	[property: Required]
	string Size
) : PageModalSizeArgsBase;
