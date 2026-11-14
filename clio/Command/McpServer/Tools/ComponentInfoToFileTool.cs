using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool that returns what <see cref="ComponentInfoTool"/> returns, with the documentation markdown written to
/// a local file instead of carried inline.
/// </summary>
/// <remarks>
/// A separate tool rather than an argument on <see cref="ComponentInfoTool"/>: the MCP safety annotations are
/// static per tool, and an optional output-file would have made every catalog read write-capable. The lookup runs
/// through <see cref="ComponentInfoTool.GetComponentInfo"/>, so both tools resolve the same catalog version and
/// refuse the same arguments.
/// </remarks>
[McpServerToolType]
public sealed class ComponentInfoToFileTool(ComponentInfoTool infoTool, IMcpOutputFileWriter outputFileWriter) {

	internal const string ToolName = "get-component-info-to-file";

	/// <summary>Returns component metadata with its documentation written to a local file.</summary>
	/// <param name="args">The get-component-info arguments plus output-file.</param>
	/// <param name="cancellationToken">Cancellation token propagated by the MCP host.</param>
	/// <returns>The get-component-info response with <c>documentation</c> replaced by the file path and headings.</returns>
	// ReadOnly and Idempotent are FALSE because the call creates a local file and refuses an existing one.
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	[Description("Write the documentation markdown of a get-component-info detail or composite response to a local file, " +
		"returning every other field of that response plus documentationFile (the path) and documentationSections (the " +
		"markdown headings) instead of documentation. Takes the get-component-info arguments plus output-file; use " +
		"get-component-info when the documentation should come back inline. When the response has no documentation, " +
		"no file is written and the response is returned as get-component-info returns it. output-file is required, " +
		"is confined to the workspace or the OS temp directory, and must not exist yet, so a retry must use a different path.")]
	public async Task<JsonObject> GetComponentInfoToFile(
		[Description("output-file (required) plus the get-component-info arguments: component-type or composite, search, schema-type, environment-name or version, uri/login/password fallback.")]
		[Required] ComponentInfoToFileArgs args,
		CancellationToken cancellationToken = default) {
		if (args is null) {
			return DocumentationFileProjection.Failure("args is required");
		}
		// Confine the path BEFORE the lookup: a rejected path must not cost a catalog and docs fetch first.
		if (!outputFileWriter.TryResolve(args.OutputFile, out string outputPath, out string pathError)) {
			return DocumentationFileProjection.Failure(
				pathError + " Use get-component-info when the documentation should be returned inline.");
		}
		ComponentInfoResponse response = await infoTool.GetComponentInfo(args, cancellationToken);
		return DocumentationFileProjection.Project(response, response.Documentation, outputPath, outputFileWriter);
	}
}

/// <summary>Arguments for <see cref="ComponentInfoToFileTool"/>: every <see cref="ComponentInfoArgs"/> member plus the file destination.</summary>
public sealed record ComponentInfoToFileArgs : ComponentInfoArgs {

	/// <summary>Path the documentation markdown is written to.</summary>
	[JsonPropertyName("output-file")]
	[Description("Path for the documentation markdown file, confined to the workspace or the OS temp directory. Required. The file must not already exist, so a retry must use a different path.")]
	[Required]
	public required string OutputFile { get; init; }
}
