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
/// MCP tool that uploads a local file into a file entity and attaches it to a record. It is the write path
/// for a file entity's binary <c>Data</c> column, which <c>odata-create</c> cannot write. Each call stores a
/// new file record and never overwrites existing data, so it is annotated <c>Destructive=false</c>.
/// </summary>
public class UploadFileTool(
	UploadFileCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver) : BaseTool<UploadFileOptions>(command, logger, commandResolver) {

	internal const string ToolName = "upload-file";

	private static readonly Dictionary<string, string> LegacyAliases =
		new(McpToolArgumentSupport.EnvironmentNameAliases, StringComparer.Ordinal) {
			["parentColumn"] = "parent-column",
			["parent_column"] = "parent-column",
			["parentId"] = "parent-id",
			["parent_id"] = "parent-id"
		};

	/// <summary>Uploads the file and returns a structured result carrying the created file id.</summary>
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false),
	 Description("Upload a local file into a Creatio file entity (for example ContactFile, AccountFile or " +
		"KnowledgeBaseFile) and attach it to a record. Use it instead of odata-create for a file: OData cannot " +
		"write the binary Data column. Additive only - each call creates a new file record. The file is read back " +
		"and compared byte for byte before success is reported. Limit: 10 MB. Requires forms-auth credentials on " +
		"the environment (login/password). Returns { success, file-id, error? }; file-id is also set on a failure " +
		"after the file was sent - read that record before you upload again. For an image in SysImage use upload-image.")]
	public UploadFileResult UploadFile(
		[Description("Parameters: environment-name, file, entity, parent-column, parent-id (all required).")]
		[Required] UploadFileArgs args) {
		string? aliasError = McpToolArgumentSupport.BuildLegacyAliasError(
			args.ExtensionData, LegacyAliases, ".",
			"Valid: environment-name, file, entity, parent-column, parent-id.");
		if (!string.IsNullOrWhiteSpace(aliasError)) {
			return UploadFileResult.Failure(aliasError);
		}
		(string? Value, string Name)[] requiredArguments = [
			(args.EnvironmentName, "environment-name"),
			(args.File, "file"),
			(args.Entity, "entity"),
			(args.ParentColumn, "parent-column"),
			(args.ParentId, "parent-id")
		];
		foreach ((string? value, string name) in requiredArguments) {
			if (string.IsNullOrWhiteSpace(value)) {
				return UploadFileResult.Failure($"{name} is required and cannot be empty.");
			}
		}
		UploadFileOptions options = new() {
			Environment = args.EnvironmentName,
			File = args.File,
			Entity = args.Entity,
			ParentColumn = args.ParentColumn,
			ParentId = args.ParentId
		};
		return ExecuteResolved<UploadFileCommand, UploadFileResult>(options,
			resolvedCommand => {
				RecordFileUploadResult result = resolvedCommand.UploadFile(options);
				return result.Success
					? UploadFileResult.Successful(result.FileId!.Value)
					: UploadFileResult.Failure(string.IsNullOrWhiteSpace(result.Error)
						? "UploadFile returned success=false."
						: SensitiveErrorTextRedactor.Redact(result.Error), result.FileId);
			},
			error => UploadFileResult.Failure(error));
	}
}

/// <summary>
/// MCP arguments for the <c>upload-file</c> tool.
/// </summary>
public sealed record UploadFileArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered clio environment name.")]
	[property: Required]
	string? EnvironmentName = null,

	[property: JsonPropertyName("file")]
	[property: Description("Path to the local file to upload (10 MB limit).")]
	[property: Required]
	string? File = null,

	[property: JsonPropertyName("entity")]
	[property: Description("File entity schema name, for example ContactFile, AccountFile or KnowledgeBaseFile.")]
	[property: Required]
	string? Entity = null,

	[property: JsonPropertyName("parent-column")]
	[property: Description("Lookup column of the file entity that points to the parent record, for example Contact for ContactFile.")]
	[property: Required]
	string? ParentColumn = null,

	[property: JsonPropertyName("parent-id")]
	[property: Description("Id (GUID) of the parent record to attach the file to.")]
	[property: Required]
	string? ParentId = null
) {
	/// <summary>Overflow bag for unknown JSON fields; drives the legacy-alias rename hints.</summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

/// <summary>
/// Structured result of the <c>upload-file</c> MCP tool.
/// </summary>
public sealed record UploadFileResult {
	/// <summary>Whether the file was uploaded and read back unchanged.</summary>
	[JsonPropertyName("success")]
	public bool Success { get; init; }

	/// <summary>
	/// Id of the created file record. Set on success, and on a failure that happened after the file was sent.
	/// </summary>
	[JsonPropertyName("file-id")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? FileId { get; init; }

	/// <summary>The failure message; omitted on success.</summary>
	[JsonPropertyName("error")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Error { get; init; }

	/// <summary>Creates a success result carrying the created file record id.</summary>
	public static UploadFileResult Successful(Guid fileId) => new() { Success = true, FileId = fileId.ToString() };

	/// <summary>Creates a failure result; <paramref name="fileId"/> is set when the file was already sent.</summary>
	public static UploadFileResult Failure(string error, Guid? fileId = null) => new() {
		Success = false,
		FileId = fileId?.ToString(),
		Error = string.IsNullOrWhiteSpace(error) ? "unknown" : error
	};
}
