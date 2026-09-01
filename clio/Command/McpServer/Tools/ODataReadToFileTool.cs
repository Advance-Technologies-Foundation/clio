using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool for querying Creatio records via OData v4 and persisting the raw response to a local file.
/// </summary>
/// <remarks>
/// The file destination is a SEPARATE tool rather than an argument on <see cref="ODataReadTool"/> because the
/// MCP safety annotations and the durable read routing are static per tool: an optional output-file would have
/// made every ordinary query write-capable, costing it both raw-name compatibility and the bounded retry-safe
/// read semantics. The query itself is not duplicated - both tools build and parse through
/// <see cref="ODataReadQuery"/>.
/// </remarks>
[McpServerToolType]
public sealed class ODataReadToFileTool(IToolCommandResolver commandResolver, IODataFileContract fileContract) {

	private readonly IODataFileContract _fileContract =
		fileContract ?? throw new ArgumentNullException(nameof(fileContract));

	internal const string ToolName = "odata-read-to-file";

	private const string ValidArgumentsHint =
		"Valid: entity, environment-name, output-file, filters, select, expand, order-by, top, skip, count. " +
		"Raw filter strings are not supported; use the structured filters object.";

	private static readonly IReadOnlyDictionary<string, string> ArgumentAliases = BuildArgumentAliases();

	private static IReadOnlyDictionary<string, string> BuildArgumentAliases() {
		Dictionary<string, string> aliases = new(ODataReadQuery.SharedArgumentAliases, StringComparer.Ordinal) {
			["outputFile"] = "output-file",
			["output_file"] = "output-file"
		};
		return aliases;
	}

	/// <summary>Reads Creatio records using OData v4 and writes the raw response to a local file.</summary>
	// ReadOnly is FALSE because this tool writes a local file. The MCP read-deadline pipeline treats a
	// ReadOnly call as retry-safe and races it against a deadline; if that deadline fired after the file
	// landed but before the response returned, the retry would be refused by the "already exists" guard and
	// the agent would be stuck with a file it was told was never written. Same reasoning as get-page.
	// Idempotent is FALSE for the same reason: a second call to the same output-file is rejected, not a no-op.
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
	[Description(
		"Query Creatio records via OData v4 and write the raw JSON response to a local file, returning a compact " +
		"row/column-size summary instead of inline values. Use this only when the response is too large to return " +
		"inline; odata-read is the read-only tool for ordinary queries and takes the same query arguments. " +
		"output-file is required and is confined to the workspace or the OS temp directory. " +
		"The write refuses an existing target, so a call is NOT retry-safe against the same path: a retry must use a different path. " +
		"top must be between 1 and 100 (default 25); an out-of-range top (including 0 or negative) is rejected, never silently widened. " +
		"Unknown arguments and malformed filter conditions fail before any Creatio request; raw filter strings are not supported. " +
		"Call get-tool-contract for odata-read-to-file to see usage examples and discovery workflow hints.")]
	public ODataReadResponse ReadToFile(
		[Description("Parameters: entity, environment-name, output-file (required); filters, select, expand, order-by, top, skip, count (optional).")]
		[Required]
		ODataReadToFileArgs args) {
		try {
			string argumentError = ODataReadQuery.ValidateArguments(args, ArgumentAliases, ValidArgumentsHint)
				?? ODataReadQuery.ValidateTarget(args);
			if (argumentError is not null) {
				return ODataReadResponse.Failure(argumentError);
			}
			if (string.IsNullOrWhiteSpace(args.OutputFile)) {
				return ODataReadResponse.Failure(
					"output-file is required. Use odata-read when the response should be returned inline.");
			}

			EnvironmentOptions options = new() { Environment = args.EnvironmentName };
			IApplicationClient client = commandResolver.Resolve<IApplicationClient>(options);
			IServiceUrlBuilder urlBuilder = commandResolver.Resolve<IServiceUrlBuilder>(options);

			// Confine the output path BEFORE the fetch: a rejected path should not cost a full (possibly large)
			// OData response first.
			if (!_fileContract.TryResolveOutputPath(args.OutputFile, out string outputPath, out string pathError)) {
				return ODataReadResponse.Failure(pathError);
			}

			string url = urlBuilder.Build(ODataReadQuery.BuildRequestPath(args));
			string responseJson = client.ExecuteGetRequest(url, 30_000);
			// The response is NOT parsed into an inline response first: for the file mode that second parse
			// (plus the value Clone) allocated several times the response size for a value that is thrown
			// away. Error detection, the paging annotations and the summary all come out of one pass.
			if (!_fileContract.TryWriteReadResponse(
				outputPath, responseJson, args.Count, out ODataReadFileSummary summary, out string fileError)) {
				return ODataReadResponse.Failure(fileError);
			}
			return new ODataReadResponse(
				true,
				null,
				summary.RecordCount,
				null,
				summary.NextLink,
				summary.TotalCount,
				outputPath,
				summary.RowCount,
				summary.ColumnSizes);
		} catch (Exception ex) {
			return ODataReadResponse.Failure(SensitiveErrorTextRedactor.Redact(ex.Message));
		}
	}
}

/// <summary>Arguments for <see cref="ODataReadToFileTool"/>: every <see cref="ODataReadArgs"/> member plus the file destination.</summary>
public sealed record ODataReadToFileArgs : ODataReadArgs {

	/// <summary>Path the raw OData JSON response is written to.</summary>
	[JsonPropertyName("output-file")]
	[Description("Path for the raw OData JSON response, confined to the workspace or the OS temp directory. Required. The inline value is omitted and a compact row/column-size summary is returned instead. The file must not already exist, so a retry must use a different path.")]
	[Required]
	public required string OutputFile { get; init; }
}
