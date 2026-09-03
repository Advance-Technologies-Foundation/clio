using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Command.Branding;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool that sets an image as the environment's shell background — either uploading a local file
/// in the same call or applying an already-uploaded image by id. Replaces the currently configured
/// background for all users, so it is annotated <c>Destructive=true</c>.
/// </summary>
public class SetBackgroundImageTool(
	SetBackgroundImageCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver) : BaseTool<SetBackgroundImageOptions>(command, logger, commandResolver) {

	internal const string ToolName = "set-background-image";

	private static readonly Dictionary<string, string> LegacyAliases =
		new(McpToolArgumentSupport.EnvironmentNameAliases, StringComparer.Ordinal) {
			["imageId"] = "image-id",
			["image_id"] = "image-id",
			["packageName"] = "package",
			["package_name"] = "package",
			["package-name"] = "package",
			["keepIconBackground"] = "keep-icon-background",
			["keep_icon_background"] = "keep-icon-background"
		};

	/// <summary>Sets the image as the shell background, binds it into the package, and returns a structured result.</summary>
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false),
	 Description("Set an image as a registered environment's shell background and bind it into a package " +
		"as data bindings so it ships with the package. Pass exactly one of: file (a local image file — " +
		"uploaded and applied in one call) or image-id (an image already uploaded with upload-image). " +
		"The background changes for all users after a page refresh, replacing the currently configured " +
		"one; the panel's own icon background is turned off so the new background is actually visible, " +
		"unless keep-icon-background is true. When package is omitted, the environment's CurrentPackageId " +
		"system setting decides where the bindings land. Returns { success, image-id, bound?, package, " +
		"warnings?, error? } — bound names the parts that reached the package (absent when none did, which " +
		"can happen even on a successful apply); relay the warnings, they are the only place a delivery gap " +
		"is reported. For the full branding flow (logos, background), read get-guidance branding first.")]
	public SetBackgroundImageResult SetBackgroundImage(
		[Description("Parameters: environment-name (required); exactly one of file (local image path) or image-id (id returned by upload-image); package (optional, the environment's CurrentPackageId when omitted); keep-icon-background (optional bool).")]
		[Required] SetBackgroundImageArgs args) {
		string? aliasError = McpToolArgumentSupport.BuildLegacyAliasError(
			args.ExtensionData, LegacyAliases, ".",
			"Valid: environment-name, file, image-id, package, keep-icon-background.");
		if (!string.IsNullOrWhiteSpace(aliasError)) {
			return SetBackgroundImageResult.Failure(aliasError);
		}
		if (string.IsNullOrWhiteSpace(args.EnvironmentName)) {
			return SetBackgroundImageResult.Failure("environment-name is required and cannot be empty.");
		}
		bool hasFile = !string.IsNullOrWhiteSpace(args.File);
		bool hasImageId = !string.IsNullOrWhiteSpace(args.ImageId);
		if (hasFile && hasImageId) {
			return SetBackgroundImageResult.Failure(SetBackgroundImageCommand.BothSourcesError);
		}
		if (!hasFile && !hasImageId) {
			return SetBackgroundImageResult.Failure(SetBackgroundImageCommand.NoSourceError);
		}
		SetBackgroundImageOptions options = new() {
			Environment = args.EnvironmentName,
			ImageId = args.ImageId,
			File = args.File,
			PackageName = args.Package,
			KeepIconBackground = args.KeepIconBackground ?? false
		};
		return Execute(options);
	}

	private SetBackgroundImageResult Execute(SetBackgroundImageOptions options) {
		return ExecuteResolved<SetBackgroundImageCommand, SetBackgroundImageResult>(options,
			resolvedCommand => {
				SetBackgroundResult result = resolvedCommand.SetBackground(options);
				if (!result.Success) {
					return SetBackgroundImageResult.Failure(string.IsNullOrWhiteSpace(result.Error)
						? "SetBackground returned success=false."
						: SensitiveErrorTextRedactor.Redact(result.Error), result.Warnings, result.Package, result.Bound);
				}
				return SetBackgroundImageResult.Successful(result);
			},
			error => SetBackgroundImageResult.Failure(error));
	}
}

/// <summary>
/// MCP arguments for the <c>set-background-image</c> tool.
/// </summary>
public sealed record SetBackgroundImageArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered clio environment name.")]
	[property: Required]
	string? EnvironmentName = null,

	[property: JsonPropertyName("image-id")]
	[property: Description("Id of an already-uploaded image to set as the background (returned by upload-image). Pass either this or file, not both.")]
	string? ImageId = null,

	[property: JsonPropertyName("file")]
	[property: Description("Path to a local image file to upload and set as the background in one call. Pass either this or image-id, not both.")]
	string? File = null,

	[property: JsonPropertyName("package")]
	[property: Description("Package that receives the background data bindings. When omitted, the package from the environment's CurrentPackageId system setting is used.")]
	string? Package = null,

	[property: JsonPropertyName("keep-icon-background")]
	[property: Description("When true, leaves the panel's own icon background in place instead of turning it off (the UsePanelIconBackground feature). While it is on it can cover the shell background, so the new background may not be visible.")]
	bool? KeepIconBackground = null
) {
	/// <summary>Overflow bag for unknown JSON fields; drives the legacy-alias rename hints.</summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

/// <summary>
/// Structured result of the <c>set-background-image</c> MCP tool.
/// </summary>
public sealed record SetBackgroundImageResult {
	/// <summary>Whether the background was set and bound.</summary>
	[JsonPropertyName("success")]
	public bool Success { get; init; }

	/// <summary>The id of the image the background points at; omitted on failure.</summary>
	[JsonPropertyName("image-id")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string ImageId { get; init; }

	/// <summary>
	/// The parts of the background the package delivery confirmed it bound, as the stable tokens
	/// <c>image</c>, <c>gallery-membership</c>, <c>background-config</c>, and <c>panel-icon-off-state</c>.
	/// Omitted when nothing was bound, which can happen even on a successful apply — every part can be
	/// refused by the delivery.
	/// </summary>
	[JsonPropertyName("bound")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<string> Bound { get; init; }

	/// <summary>
	/// The package the background data was bound into (also populated when binding failed partway, where the
	/// parts that landed are in it); omitted when the run never got as far as resolving one.
	/// </summary>
	[JsonPropertyName("package")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Package { get; init; }

	/// <summary>
	/// Every non-fatal problem: an apply-side caveat such as a failed <c>UsePanelIconBackground</c> turn-off, and
	/// each gap between what was applied and what the package will deliver. Relay them to the user — a run with
	/// warnings still succeeded, but delivers less than it looks like it did. Omitted when empty.
	/// </summary>
	[JsonPropertyName("warnings")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<string> Warnings { get; init; }

	/// <summary>The failure message; omitted on success.</summary>
	[JsonPropertyName("error")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Error { get; init; }

	/// <summary>Creates a success result from the command outcome.</summary>
	public static SetBackgroundImageResult Successful(SetBackgroundResult result) {
		return new SetBackgroundImageResult {
			Success = true,
			ImageId = result.ImageId.ToString(),
			Bound = result.Bound.Count > 0 ? result.Bound : null,
			Package = result.Package,
			Warnings = result.Warnings.Count > 0 ? SensitiveErrorTextRedactor.RedactAll(result.Warnings) : null
		};
	}

	/// <summary>
	/// Creates a failure result carrying the diagnostic message, any warnings raised before the failure — an
	/// apply-side caveat must not be lost just because binding failed after it — and the package the parts that
	/// landed were bound into, when one was resolved.
	/// </summary>
	public static SetBackgroundImageResult Failure(string error, IReadOnlyList<string> warnings = null,
		string package = null, IReadOnlyList<string> bound = null) {
		return new SetBackgroundImageResult {
			Success = false,
			Error = string.IsNullOrWhiteSpace(error) ? "unknown" : error,
			Bound = bound is { Count: > 0 } ? bound : null,
			Warnings = warnings is { Count: > 0 } ? SensitiveErrorTextRedactor.RedactAll(warnings) : null,
			Package = package
		};
	}

}
