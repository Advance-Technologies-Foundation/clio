using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using System.Threading;
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

	/// <summary>Request timeout for the OData fetch, in milliseconds.</summary>
	internal const int RequestTimeoutMs = 30_000;

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
		ODataReadToFileArgs args,
		CancellationToken cancellationToken = default) {
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
			if (!TryFetch(client, url, cancellationToken, out byte[] responseUtf8, out string fetchError)) {
				return ODataReadResponse.Failure(fetchError);
			}
			// Checked AFTER the fetch and BEFORE anything is published: the transport may well finish an
			// abandoned request, and a file appearing for a call the caller was told nothing about is worse
			// than a slow one. Nothing has been written yet, so there is nothing to clean up either. This is
			// honoured but NOT advertised: a cancelled MCP call is not observed to reach the running tool
			// (docs/knowledge/McpServer/mcp-cancellation-does-not-reach-tools.md), so the guarantee the tool
			// states is the size ceiling, not cancellation.
			cancellationToken.ThrowIfCancellationRequested();
			// The response is NOT parsed into an inline response first: for the file mode that second parse
			// (plus the value Clone) allocated several times the response size for a value that is thrown
			// away. Error detection, the paging annotations and the summary all come out of one pass, and the
			// file is published only after that pass accepts the body.
			if (!_fileContract.TryWriteReadResponse(
				outputPath, responseUtf8, args.Count, out ODataReadFileSummary summary, out string fileError)) {
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
		} catch (OperationCanceledException) {
			// The caller went away. Nothing has been written - the file is published only after the body is
			// fully received and accepted - so there is nothing to clean up, and the failure says why.
			return ODataReadResponse.Failure(
				"The call was cancelled before the response was received; no output file was written.");
		} catch (Exception ex) {
			return ODataReadResponse.Failure(SensitiveErrorTextRedactor.Redact(ex.Message));
		}
	}

	/// <summary>
	/// Fetches the response body with the size ceiling enforced as the bytes arrive and the caller's
	/// cancellation honored.
	/// </summary>
	/// <param name="client">Environment-scoped client.</param>
	/// <param name="url">Absolute request URL.</param>
	/// <param name="cancellationToken">Caller token; the MCP host cancels it when it disconnects.</param>
	/// <param name="responseUtf8">Response body when the method returns <see langword="true"/>.</param>
	/// <param name="error">Caller-facing error when the method returns <see langword="false"/>.</param>
	/// <remarks>
	/// The ceiling is only a ceiling while the body is still arriving, so file mode REQUIRES the transport
	/// level client that pulls the body incrementally and abandons the transfer once the limit is passed.
	/// There is deliberately no buffered fallback: applying the limit to a body already fully in memory
	/// measures an allocation the long-lived MCP process has already made, which is the failure the limit
	/// exists to prevent. A client without the streamed call fails explicitly instead.
	/// </remarks>
	private static bool TryFetch(
		IApplicationClient client,
		string url,
		CancellationToken cancellationToken,
		out byte[] responseUtf8,
		out string error) {
		responseUtf8 = null;
		error = null;
		if (client is not ICreatioApplicationClient streamingClient) {
			error = "This environment's client cannot stream a bounded response, and file mode will not read "
				+ "an unbounded one into memory. Use odata-read without output-file, narrowed with select and "
				+ "top.";
			return false;
		}
		try {
			responseUtf8 = streamingClient
				.ExecuteGetRequestBoundedAsync(
					url, ODataFileContract.MaxResponseBytes, RequestTimeoutMs, cancellationToken)
				.GetAwaiter()
				.GetResult();
			return true;
		} catch (ResponseTooLargeException tooLarge) {
			error = DescribeTooLarge(tooLarge.ObservedBytes);
			return false;
		} catch (TimeoutException timeout) {
			// The transport's deadline elapsed - a distinct outcome from caller cancellation, which stays an
			// exception. Reported as a tool error so the caller learns the request timed out instead of seeing
			// an exception escape the tool boundary; retrying the buffered path would just stall again.
			error = timeout.Message;
			return false;
		} catch (NotSupportedException notSupported) {
			// The streamed GET declining is now a hard failure rather than a hand-off: the buffered path it
			// used to fall through to defeats the ceiling, so the caller is told the request cannot be
			// bounded instead of having it silently read into memory.
			error = notSupported.Message;
			return false;
		}
	}

	private static string DescribeTooLarge(long observedBytes) =>
		$"OData response is at least {observedBytes} bytes, which exceeds the "
		+ $"{ODataFileContract.MaxResponseBytes}-byte limit for one call. Narrow the query with select, or "
		+ "page it with top and skip.";
}

/// <summary>Arguments for <see cref="ODataReadToFileTool"/>: every <see cref="ODataReadArgs"/> member plus the file destination.</summary>
public sealed record ODataReadToFileArgs : ODataReadArgs {

	/// <summary>Path the raw OData JSON response is written to.</summary>
	[JsonPropertyName("output-file")]
	[Description("Path for the raw OData JSON response, confined to the workspace or the OS temp directory. Required. The inline value is omitted and a compact row/column-size summary is returned instead. The file must not already exist, so a retry must use a different path.")]
	[Required]
	public required string OutputFile { get; init; }
}
