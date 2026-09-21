using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>Long-tail MCP adapter for Binary system-setting downloads through clio-run.</summary>
[McpServerToolType]
public sealed class DownloadSysSettingFileTool(ILogger logger, IToolCommandResolver commandResolver)
	: BaseTool<DownloadSysSettingFileOptions>(null, logger, commandResolver) {

	internal const string ToolName = "download-sys-setting-file";

	/// <summary>Copies a Binary setting's decoded bytes into the caller's chosen filename.</summary>
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
	[McpToolExecution(Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall, OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None, SharedFileResource = McpToolSharedFileResource.None)]
	[Description("Saves a Binary system setting's effective current-user value (with default fallback) to a required absolute file-name. " +
		"Copies decoded bytes exactly; does not guess MIME type, extension, or text encoding. JSON, XML, images and other content stored as Binary are copied unchanged. " +
		"Rejects Text settings, empty/invalid values, values above 10 MB, and existing destinations. Requires ClioGate and an existing destination directory. " +
		"Returns file-name and byte-count, never file content. For system-setting workflows, read get-guidance name=sys-settings.")]
	public DownloadSysSettingFileResult Download([Required] DownloadSysSettingFileArgs args) {
		if (string.IsNullOrWhiteSpace(args.FileName) || !System.IO.Path.IsPathFullyQualified(args.FileName)) {
			return Failure("file-name must be an absolute path including the destination filename.");
		}
		if (string.IsNullOrWhiteSpace(args.EnvironmentName) || string.IsNullOrWhiteSpace(args.Code)) {
			return Failure("environment-name and code are required.");
		}
		DownloadSysSettingFileOptions options = new() {
			Environment = args.EnvironmentName, Code = args.Code, FileName = args.FileName
		};
		return ExecuteResolved<DownloadSysSettingFileCommand, DownloadSysSettingFileResult>(options,
			command => {
				DownloadSysSettingFileReceipt receipt = command.Download(options);
				return new DownloadSysSettingFileResult(0,
					[new InfoMessage("Binary system setting saved without content conversion.")], receipt.FileName, receipt.ByteCount);
			}, Failure);
	}

	private static DownloadSysSettingFileResult Failure(string message) => new(1, [new ErrorMessage(message)], null, null);
}

/// <summary>Required arguments for the long-tail download tool.</summary>
/// <param name="EnvironmentName">Registered Creatio environment.</param>
/// <param name="Code">Existing Binary system setting code.</param>
/// <param name="FileName">Absolute output path including the caller-chosen filename.</param>
public sealed record DownloadSysSettingFileArgs(
	[property: JsonPropertyName("environment-name"), Required, Description("Registered Creatio environment name.")] string EnvironmentName,
	[property: JsonPropertyName("code"), Required, Description("Existing Binary system setting code.")] string Code,
	[property: JsonPropertyName("file-name"), Required, Description("Absolute destination path including filename; no type or extension is inferred.")] string FileName);

/// <summary>Download metadata and execution diagnostics, without the Binary payload.</summary>
/// <param name="ExitCode">Zero on success; nonzero on failure.</param>
/// <param name="Output">Information or error diagnostics.</param>
/// <param name="FileName">Absolute saved path on success.</param>
/// <param name="ByteCount">Saved byte count on success.</param>
public sealed record DownloadSysSettingFileResult(
	[property: JsonPropertyName("exit-code")] int ExitCode,
	[property: JsonPropertyName("execution-log-messages")] LogMessage[] Output,
	[property: JsonPropertyName("file-name")] string FileName,
	[property: JsonPropertyName("byte-count")] long? ByteCount);
