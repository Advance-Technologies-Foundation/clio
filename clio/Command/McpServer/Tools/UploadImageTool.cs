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
/// MCP tool that uploads a local image file to a target environment and returns the created image id —
/// the supported write path for images such as the shell background (see <c>get-guidance branding</c>).
/// Each call stores a new image and never overwrites existing data, so it is annotated
/// <c>Destructive=false</c>.
/// </summary>
public class UploadImageTool(
	UploadImageCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver) : BaseTool<UploadImageOptions>(command, logger, commandResolver) {

	internal const string ToolName = "upload-image";

	private static readonly Dictionary<string, string> LegacyAliases =
		new(McpToolArgumentSupport.EnvironmentNameAliases, StringComparer.Ordinal);

	/// <summary>Uploads the image and returns a structured result carrying the created image id.</summary>
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false),
	 Description("Upload a local image file (png, jpg, jpeg, gif, bmp, webp, or svg) to a registered environment " +
		"and return the created image-id. Additive only — each call stores a new image, nothing is overwritten. " +
		"Requires forms-auth credentials on the environment (login/password). Returns { success, image-id, error? }. " +
		"To set the uploaded image as the shell background, call set-background-image; " +
		"for the full branding flow read get-guidance branding first.")]
	public UploadImageResult UploadImage(
		[Description("Parameters: environment-name (required), file (required — path to the local image file).")]
		[Required] UploadImageArgs args) {
		string? aliasError = McpToolArgumentSupport.BuildLegacyAliasError(
			args.ExtensionData, LegacyAliases, ".",
			"Valid: environment-name, file.");
		if (!string.IsNullOrWhiteSpace(aliasError)) {
			return UploadImageResult.Failure(aliasError);
		}
		if (string.IsNullOrWhiteSpace(args.EnvironmentName)) {
			return UploadImageResult.Failure("environment-name is required and cannot be empty.");
		}
		if (string.IsNullOrWhiteSpace(args.File)) {
			return UploadImageResult.Failure("file is required and cannot be empty.");
		}
		UploadImageOptions options = new() {
			Environment = args.EnvironmentName,
			File = args.File
		};
		return Execute(options);
	}

	private UploadImageResult Execute(UploadImageOptions options) {
		return ExecuteResolved<UploadImageCommand, UploadImageResult>(options,
			resolvedCommand => {
				SysImageUploadResult result = resolvedCommand.UploadImage(options);
				if (!result.Success) {
					// The error can carry a server-supplied image-API message or a transport message
					// (URI/credentials/path), so redact it before it crosses into the MCP transcript.
					return UploadImageResult.Failure(string.IsNullOrWhiteSpace(result.Error)
						? "UploadImage returned success=false."
						: SensitiveErrorTextRedactor.Redact(result.Error));
				}
				return UploadImageResult.Successful(result.ImageId);
			},
			UploadImageResult.Failure);
	}
}

/// <summary>
/// MCP arguments for the <c>upload-image</c> tool.
/// </summary>
public sealed record UploadImageArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered clio environment name.")]
	[property: Required]
	string? EnvironmentName = null,

	[property: JsonPropertyName("file")]
	[property: Description("Path to the local image file to upload (png, jpg, jpeg, gif, bmp, webp, or svg).")]
	[property: Required]
	string? File = null
) {
	/// <summary>Overflow bag for unknown JSON fields; drives the legacy-alias rename hints.</summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

/// <summary>
/// Structured result of the <c>upload-image</c> MCP tool.
/// </summary>
public sealed record UploadImageResult {
	/// <summary>Whether the image was uploaded and verified.</summary>
	[JsonPropertyName("success")]
	public bool Success { get; init; }

	/// <summary>The created image id; omitted on failure.</summary>
	[JsonPropertyName("image-id")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string ImageId { get; init; }

	/// <summary>The failure message; omitted on success.</summary>
	[JsonPropertyName("error")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Error { get; init; }

	/// <summary>Creates a success result carrying the created SysImage id.</summary>
	public static UploadImageResult Successful(Guid imageId) {
		return new UploadImageResult {
			Success = true,
			ImageId = imageId.ToString()
		};
	}

	/// <summary>Creates a failure result carrying the diagnostic message.</summary>
	public static UploadImageResult Failure(string error) {
		return new UploadImageResult {
			Success = false,
			Error = string.IsNullOrWhiteSpace(error) ? "unknown" : error
		};
	}
}
