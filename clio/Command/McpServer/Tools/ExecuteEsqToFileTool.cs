using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool that runs a raw ESQ SelectQuery like <see cref="ExecuteEsqTool"/> and writes the rows to a local file
/// instead of returning them inline.
/// </summary>
/// <remarks>
/// A separate tool rather than an argument on <see cref="ExecuteEsqTool"/>: the MCP safety annotations are static
/// per tool, and an optional output-file would have made every ordinary query write-capable. The query itself is
/// not duplicated - both tools validate, run and parse through <see cref="ExecuteEsqTool.Run"/>.
/// </remarks>
[McpServerToolType]
public sealed class ExecuteEsqToFileTool(IToolCommandResolver commandResolver, IMcpOutputFileWriter outputFileWriter) {

	internal const string ToolName = "execute-esq-to-file";

	/// <summary>
	/// Largest DataService response the file mode writes, the same ceiling as <c>odata-read-to-file</c>. The
	/// SelectQuery POST has no bounded variant, so the whole response is already in memory when this is checked:
	/// it limits what is written, not what is read.
	/// </summary>
	internal const long MaxResponseSizeBytes = ODataFileContract.MaxResponseBytes;

	/// <summary>Runs a raw ESQ SelectQuery and writes the resulting rows to a local file.</summary>
	/// <param name="args">The execute-esq arguments plus output-file.</param>
	/// <returns>The execute-esq response with <c>rows</c> replaced by <c>output-file</c>.</returns>
	// ReadOnly and Idempotent are FALSE because the call creates a local file and refuses an existing one, so a
	// retry against the same path fails instead of repeating the read - the same classification as
	// odata-read-to-file.
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
	// SharedFileResource is None: output-file is a caller-named path confined to the workspace or the OS temp
	// root, not one of the stores clio itself coordinates on.
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	[Description(
		"Write the rows of a raw EntitySchemaQuery (ESQ) SelectQuery to a local JSON file, returning the path and the row " +
		"count instead of the rows. Use it when the rows are too many to read inline; execute-esq is the read-only tool " +
		"for ordinary queries and takes the same query, environment-name and timeout. The file holds the same JSON rows " +
		"array execute-esq returns inline (the whole response body when it has no rows array; count is then absent). output-file is required, is confined to the workspace or the OS temp " +
		"directory, and must not exist yet, so a retry must use a different path. Call get-guidance for 'esq' and " +
		"'esq-filters' before composing a query.")]
	public ExecuteEsqResponse ExecuteToFile(
		[Description("Parameters: query, environment-name, output-file (required); timeout (optional).")]
		[Required]
		ExecuteEsqToFileArgs args) {
		if (args is null) {
			return ExecuteEsqResponse.FailureWithoutGuidance("args is required.");
		}
		// Confine the path BEFORE the query: a rejected path must not cost a full result first.
		if (!outputFileWriter.TryResolve(args.OutputFile, out string outputPath, out string pathError)) {
			return ExecuteEsqResponse.FailureWithoutGuidance(
				pathError + " Use execute-esq when the rows should be returned inline.");
		}
		ExecuteEsqResponse response = ExecuteEsqTool.Run(commandResolver, args, MaxResponseSizeBytes, suggestFileTwin: false);
		if (!response.Success || response.Rows is null) {
			return response;
		}
		byte[] content = Encoding.UTF8.GetBytes(response.Rows.Value.GetRawText());
		if (!outputFileWriter.TryWriteNew(outputPath, content, out string writeError)) {
			return ExecuteEsqResponse.FailureWithoutGuidance(writeError);
		}
		return response with { Rows = null, OutputFile = outputPath };
	}
}

/// <summary>Arguments for <see cref="ExecuteEsqToFileTool"/>: every <see cref="ExecuteEsqArgs"/> member plus the file destination.</summary>
public sealed record ExecuteEsqToFileArgs : ExecuteEsqArgs {

	/// <summary>Path the rows are written to.</summary>
	[JsonPropertyName("output-file")]
	[Description("Path for the JSON rows file, confined to the workspace or the OS temp directory. Required. The file must not already exist, so a retry must use a different path.")]
	[Required]
	public required string OutputFile { get; init; }
}
