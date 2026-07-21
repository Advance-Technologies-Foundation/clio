using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool that sets a previously uploaded image (see <c>upload-image</c>) as the environment's shell
/// background. Replaces the currently configured background for all users, so it is annotated
/// <c>Destructive=true</c>.
/// </summary>
public class SetBackgroundImageTool(
	SetBackgroundImageCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver) : BaseTool<SetBackgroundImageOptions>(command, logger, commandResolver) {

	internal const string ToolName = "set-background-image";

	private static readonly Dictionary<string, string> LegacyAliases =
		new(McpToolArgumentSupport.EnvironmentNameAliases, StringComparer.Ordinal) {
			["imageId"] = "image-id",
			["image_id"] = "image-id"
		};

	/// <summary>Sets the image as the shell background and returns a structured result.</summary>
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false),
	 Description("Set a previously uploaded image (image-id returned by upload-image) as a registered " +
		"environment's shell background. The background changes for all users after a page refresh, " +
		"replacing the currently configured one. Returns { success, error? }. " +
		"For the full branding flow (logos, background), read get-guidance branding first.")]
	public SetBackgroundImageResult SetBackgroundImage(
		[Description("Parameters: environment-name (required), image-id (required — id returned by upload-image).")]
		[Required] SetBackgroundImageArgs args) {
		string? aliasError = McpToolArgumentSupport.BuildLegacyAliasError(
			args.ExtensionData, LegacyAliases, ".",
			"Valid: environment-name, image-id.");
		if (!string.IsNullOrWhiteSpace(aliasError)) {
			return SetBackgroundImageResult.Failure(aliasError);
		}
		if (string.IsNullOrWhiteSpace(args.EnvironmentName)) {
			return SetBackgroundImageResult.Failure("environment-name is required and cannot be empty.");
		}
		if (string.IsNullOrWhiteSpace(args.ImageId)) {
			return SetBackgroundImageResult.Failure("image-id is required and cannot be empty.");
		}
		SetBackgroundImageOptions options = new() {
			Environment = args.EnvironmentName,
			ImageId = args.ImageId
		};
		return Execute(options);
	}

	private SetBackgroundImageResult Execute(SetBackgroundImageOptions options) {
		return ExecuteResolved<SetBackgroundImageCommand, SetBackgroundImageResult>(options,
			resolvedCommand => {
				SetBackgroundResult result = resolvedCommand.SetBackground(options);
				if (!result.Success) {
					// The error can carry transport detail (URI/credentials/path), so redact it before
					// it crosses into the MCP transcript.
					return SetBackgroundImageResult.Failure(string.IsNullOrWhiteSpace(result.Error)
						? "SetBackground returned success=false."
						: SensitiveErrorTextRedactor.Redact(result.Error));
				}
				return SetBackgroundImageResult.Successful();
			},
			SetBackgroundImageResult.Failure);
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
	[property: Description("Id of the uploaded image to set as the background (returned by upload-image).")]
	[property: Required]
	string? ImageId = null
) {
	/// <summary>Overflow bag for unknown JSON fields; drives the legacy-alias rename hints.</summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

/// <summary>
/// Structured result of the <c>set-background-image</c> MCP tool.
/// </summary>
public sealed record SetBackgroundImageResult {
	/// <summary>Whether the background was set.</summary>
	[JsonPropertyName("success")]
	public bool Success { get; init; }

	/// <summary>The failure message; omitted on success.</summary>
	[JsonPropertyName("error")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Error { get; init; }

	/// <summary>Creates a success result.</summary>
	public static SetBackgroundImageResult Successful() => new() { Success = true };

	/// <summary>Creates a failure result carrying the diagnostic message.</summary>
	public static SetBackgroundImageResult Failure(string error) {
		return new SetBackgroundImageResult {
			Success = false,
			Error = string.IsNullOrWhiteSpace(error) ? "unknown" : error
		};
	}
}
