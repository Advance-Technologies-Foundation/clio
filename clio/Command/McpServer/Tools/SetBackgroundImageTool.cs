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
			["image_id"] = "image-id"
		};

	/// <summary>Sets the image as the shell background and returns a structured result.</summary>
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false),
	 Description("Set an image as a registered environment's shell background. Pass exactly one of: " +
		"file (a local image file — uploaded and applied in one call) or image-id (an image already " +
		"uploaded with upload-image). The background changes for all users after a page refresh, " +
		"replacing the currently configured one. Returns { success, image-id, error? }. " +
		"For the full branding flow (logos, background), read get-guidance branding first.")]
	public SetBackgroundImageResult SetBackgroundImage(
		[Description("Parameters: environment-name (required), and exactly one of file (local image path) or image-id (id returned by upload-image).")]
		[Required] SetBackgroundImageArgs args) {
		string? aliasError = McpToolArgumentSupport.BuildLegacyAliasError(
			args.ExtensionData, LegacyAliases, ".",
			"Valid: environment-name, file, image-id.");
		if (!string.IsNullOrWhiteSpace(aliasError)) {
			return SetBackgroundImageResult.Failure(aliasError);
		}
		if (string.IsNullOrWhiteSpace(args.EnvironmentName)) {
			return SetBackgroundImageResult.Failure("environment-name is required and cannot be empty.");
		}
		bool hasFile = !string.IsNullOrWhiteSpace(args.File);
		bool hasImageId = !string.IsNullOrWhiteSpace(args.ImageId);
		if (hasFile && hasImageId) {
			return SetBackgroundImageResult.Failure("Pass either file or image-id, not both.");
		}
		if (!hasFile && !hasImageId) {
			return SetBackgroundImageResult.Failure(
				"Pass exactly one of file (local image path) or image-id (id returned by upload-image).");
		}
		SetBackgroundImageOptions options = new() {
			Environment = args.EnvironmentName,
			ImageId = args.ImageId,
			File = args.File
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
						: SensitiveErrorTextRedactor.Redact(result.Error));
				}
				return SetBackgroundImageResult.Successful(result.ImageId);
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
	[property: Description("Id of an already-uploaded image to set as the background (returned by upload-image). Pass either this or file, not both.")]
	string? ImageId = null,

	[property: JsonPropertyName("file")]
	[property: Description("Path to a local image file to upload and set as the background in one call. Pass either this or image-id, not both.")]
	string? File = null
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

	/// <summary>The id of the image the background points at; omitted on failure.</summary>
	[JsonPropertyName("image-id")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string ImageId { get; init; }

	/// <summary>The failure message; omitted on success.</summary>
	[JsonPropertyName("error")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Error { get; init; }

	/// <summary>Creates a success result carrying the applied image id.</summary>
	public static SetBackgroundImageResult Successful(Guid imageId) {
		return new SetBackgroundImageResult {
			Success = true,
			ImageId = imageId.ToString()
		};
	}

	/// <summary>Creates a failure result carrying the diagnostic message.</summary>
	public static SetBackgroundImageResult Failure(string error) {
		return new SetBackgroundImageResult {
			Success = false,
			Error = string.IsNullOrWhiteSpace(error) ? "unknown" : error
		};
	}
}
