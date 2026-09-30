using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool that returns what <see cref="RequestInfoTool"/> returns, with the documentation markdown written to
/// a local file instead of carried inline.
/// </summary>
/// <remarks>
/// A separate tool rather than an argument on <see cref="RequestInfoTool"/>: the MCP safety annotations are
/// static per tool, and an optional output-file would have made every catalog read write-capable. The lookup runs
/// through <see cref="RequestInfoTool.GetRequestInfo"/>, so both tools resolve the same catalog version and
/// refuse the same arguments.
/// </remarks>
[McpServerToolType]
public sealed class RequestInfoToFileTool(RequestInfoTool infoTool, IMcpOutputFileWriter outputFileWriter) {

	internal const string ToolName = "get-request-info-to-file";

	/// <summary>Returns request metadata with its documentation written to a local file.</summary>
	/// <param name="args">The get-request-info arguments plus output-file.</param>
	/// <param name="cancellationToken">Cancellation token propagated by the MCP host.</param>
	/// <returns>The get-request-info response with <c>documentation</c> replaced by the file path and headings.</returns>
	// ReadOnly and Idempotent are FALSE because the call creates a local file and refuses an existing one.
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	[Description("Write the documentation markdown of a get-request-info detail response to a local file, " +
		"returning every other field of that response plus documentationFile (the path) and documentationSections (the " +
		"markdown headings) instead of documentation. Takes the get-request-info arguments plus output-file; use " +
		"get-request-info when the documentation should come back inline. When the response has no documentation, " +
		"no file is written and the response is returned as get-request-info returns it. output-file is required, " +
		"is confined to the workspace or the OS temp directory, and must not exist yet, so a retry must use a different path.")]
	public async Task<JsonObject> GetRequestInfoToFile(
		[Description("output-file (required) plus the get-request-info arguments: request-type, search, schema-type, environment-name or version, uri/login/password fallback.")]
		[Required] RequestInfoToFileArgs args,
		CancellationToken cancellationToken = default) {
		if (args is null) {
			return DocumentationFileProjection.Failure("args is required");
		}
		// Confine the path BEFORE the lookup: a rejected path must not cost a catalog and docs fetch first.
		if (!outputFileWriter.TryResolve(args.OutputFile, out string outputPath, out string pathError)) {
			return DocumentationFileProjection.Failure(
				pathError + " Use get-request-info when the documentation should be returned inline.");
		}
		RequestInfoResponse response = await infoTool.GetRequestInfo(args, cancellationToken);
		return DocumentationFileProjection.Project(response, response.Documentation, outputPath, outputFileWriter);
	}
}

/// <summary>Arguments for <see cref="RequestInfoToFileTool"/>: every <see cref="RequestInfoArgs"/> member plus the file destination.</summary>
public sealed record RequestInfoToFileArgs : RequestInfoArgs {

	/// <summary>Path the documentation markdown is written to.</summary>
	[JsonPropertyName("output-file")]
	[Description("Path for the documentation markdown file, confined to the workspace or the OS temp directory. Required. The file must not already exist, so a retry must use a different path.")]
	[Required]
	public required string OutputFile { get; init; }
}
