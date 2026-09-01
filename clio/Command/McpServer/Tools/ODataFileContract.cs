using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Clio.Common;
using IFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Loads OData write payloads and persists OData read responses without routing large values through MCP.
/// </summary>
public interface IODataFileContract {

	/// <summary>
	/// Reads a UTF-8 JSON file and returns a caller-facing error instead of throwing. The path is confined to
	/// the workspace anchor or the OS temp directory, symmetric with the write path: without that, a
	/// file-backed payload argument is an arbitrary file reader, and a prompt-injection payload could point it
	/// at clio's own credentials store and have the contents POSTed to the OData endpoint.
	/// </summary>
	/// <param name="path">Caller-supplied path.</param>
	/// <param name="optionName">Argument name used in the caller-facing messages.</param>
	/// <param name="json">Decoded file contents when the method returns <see langword="true"/>.</param>
	/// <param name="error">Caller-facing error when the method returns <see langword="false"/>.</param>
	bool TryReadJson(string path, string optionName, out string json, out string error);

	/// <summary>
	/// Confines an output path and returns the resolved absolute form, WITHOUT writing anything. Callers
	/// resolve before the remote call so a rejected path does not cost a full fetch first.
	/// </summary>
	/// <param name="outputFile">Caller-supplied output path.</param>
	/// <param name="resolvedPath">Confined absolute path when the method returns <see langword="true"/>.</param>
	/// <param name="error">Caller-facing error when the method returns <see langword="false"/>.</param>
	bool TryResolveOutputPath(string outputFile, out string resolvedPath, out string error);

	/// <summary>
	/// Writes a raw OData response to an already-confined path and returns its compact summary.
	/// </summary>
	/// <param name="resolvedPath">Path previously returned by <see cref="TryResolveOutputPath"/>.</param>
	/// <param name="responseJson">Raw response body.</param>
	/// <param name="countRequested">Whether the caller asked for a verified total count.</param>
	/// <param name="summary">Row/column summary when the method returns <see langword="true"/>.</param>
	/// <param name="error">Caller-facing error when the method returns <see langword="false"/>.</param>
	bool TryWriteReadResponse(
		string resolvedPath,
		string responseJson,
		bool countRequested,
		out ODataReadFileSummary summary,
		out string error);
}

/// <inheritdoc cref="IODataFileContract"/>
public sealed class ODataFileContract(IFileSystem fileSystem) : IODataFileContract {

	//File access and confinement are the whole behaviour of this service, and IFileSystem is registered in
	//DI, so a `new FileSystem()` fallback would mask missing wiring and let a unit test touch the real host.
	private readonly IFileSystem _fileSystem =
		fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

	/// <summary>
	/// Upper bound on a file-backed payload. The decoded string plus <c>JsonDocument.Parse</c> plus
	/// <c>Clone</c> hold the content three times over, so an unbounded input is a memory-exhaustion lever for
	/// a caller that only controls a path. 10 MB is far above any legitimate OData write payload.
	/// </summary>
	public const long MaxPayloadBytes = 10L * 1024 * 1024;

	/// <summary>
	/// Upper bound on a response body persisted through <see cref="TryWriteReadResponse"/>. <c>top &lt;= 100</c>
	/// bounds the ROW count, not the byte count: a single large field or an $expand projection can return tens
	/// of megabytes, and summarizing it allocates several times its own size again. Without this bound one call
	/// could exhaust the MCP server's memory. 64 MB is far above any legitimate page of 100 records.
	/// </summary>
	public const long MaxResponseBytes = 64L * 1024 * 1024;

	/// <summary>
	/// UTF-8 that THROWS on an invalid byte sequence instead of substituting U+FFFD. A replaced character
	/// still parses as JSON, so a corrupted payload would reach the OData endpoint as altered data.
	/// </summary>
	private static readonly Encoding StrictUtf8 = new UTF8Encoding(
		encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

	/// <inheritdoc/>
	public bool TryReadJson(string path, string optionName, out string json, out string error) {
		json = null;
		error = null;
		if (string.IsNullOrWhiteSpace(path)) {
			error = $"{optionName} must not be empty.";
			return false;
		}
		try {
			(string resolvedPath, string pathError) = OutputPathConfinement.ResolveForRead(_fileSystem, path, optionName);
			if (pathError is not null) {
				error = pathError;
				return false;
			}
			// ONE open, and every check runs against THAT handle: the length bound is read from the open
			// stream and the bytes come from the same stream. Validating a path and then opening it again
			// left a window in which an intermediate component could be swapped, so the size check and the
			// read could land on different files - one of them outside the allowed roots.
			FileStreamOptions options = new() {
				Mode = FileMode.Open,
				Access = FileAccess.Read,
				Share = FileShare.Read
			};
			using Stream stream = _fileSystem.File.Open(resolvedPath, options);
			// ResolveForRead returns the CANONICAL path, so nothing in it should still resolve elsewhere.
			// Re-checking it while the handle is open catches a component swapped between the resolve and
			// the open; the handle already points at the file this either accepts or abandons.
			string revalidationError = OutputPathConfinement.RevalidateResolved(_fileSystem, resolvedPath, optionName);
			if (revalidationError is not null) {
				error = revalidationError;
				return false;
			}
			long length = stream.Length;
			if (length > MaxPayloadBytes) {
				error = $"{optionName} is {length} bytes, which exceeds the {MaxPayloadBytes}-byte limit.";
				return false;
			}
			byte[] payload = new byte[(int)length];
			stream.ReadExactly(payload, 0, payload.Length);
			//Decode explicitly rather than through a StreamReader: a StreamReader detects the byte-order mark
			//and a UTF-16 BOM SELECTS UTF-16, so the payload decodes happily and the strict UTF-8 encoding is
			//never consulted - a UTF-16 JSON file would then be POSTed despite the UTF-8-only contract. Here a
			//UTF-16 BOM starts with 0xFF or 0xFE, neither of which is a legal UTF-8 byte, so StrictUtf8 throws
			//and the caller gets the input error.
			json = StrictUtf8.GetString(StripUtf8Bom(payload));
			return true;
		} catch (DecoderFallbackException) {
			// Encoding.UTF8 replaces an invalid byte sequence with U+FFFD, so a corrupted payload still parsed
			// as JSON and was POSTed or PATCHed with silently altered characters. Strict decoding turns that
			// into a caller-facing input error instead. No path echo, and nothing of the bytes is quoted.
			error = $"{optionName} is not valid UTF-8. Re-encode the file as UTF-8 and retry.";
			return false;
		} catch (Exception ex) {
			// No path echo, and the platform message is redacted: an UnauthorizedAccessException or IOException
			// can carry the resolved path, the owning user, or a raw error code.
			error = SensitiveErrorTextRedactor.Redact($"Failed to read {optionName}: {ex.Message}");
			return false;
		}
	}

	/// <summary>
	/// Drops a leading UTF-8 BOM, which is legal UTF-8 but is not legal JSON - <c>JsonDocument.Parse</c> rejects
	/// the resulting U+FEFF. Only the UTF-8 BOM is stripped: a UTF-16 BOM must survive into the decoder so it is
	/// reported as invalid UTF-8 rather than silently accepted.
	/// </summary>
	/// <param name="payload">Raw file bytes.</param>
	private static ReadOnlySpan<byte> StripUtf8Bom(byte[] payload) =>
		payload.Length >= 3 && payload[0] == 0xEF && payload[1] == 0xBB && payload[2] == 0xBF
			? payload.AsSpan(3)
			: payload.AsSpan();

	/// <inheritdoc/>
	public bool TryResolveOutputPath(string outputFile, out string resolvedPath, out string error) {
		resolvedPath = null;
		error = null;
		try {
			// The CANONICAL path is what the create runs against, so the confinement decision and the write
			// target cannot be two different files.
			(string path, string pathError) = OutputPathConfinement.ResolveCanonicalOutput(_fileSystem, outputFile);
			if (pathError is not null) {
				error = pathError;
				return false;
			}
			resolvedPath = path;
			return true;
		} catch (Exception ex) {
			error = SensitiveErrorTextRedactor.Redact($"Failed to resolve output-file: {ex.Message}");
			return false;
		}
	}

	/// <inheritdoc/>
	/// <remarks>
	/// The response is parsed ONCE. The error shape, the paging annotations and the row/column summary all
	/// come out of that single pass and nothing is cloned: the file destination is the mode meant for large
	/// results, and parsing the body twice (once to build an inline response, once to summarize it) allocated
	/// several times the response size on top of the response string itself.
	/// <para>
	/// The summary is built BEFORE the write: building it after would leave an orphaned file on disk whenever
	/// summarizing threw, with the call reported as failed and every retry then refused by the
	/// "already exists" guard.
	/// </para>
	/// </remarks>
	public bool TryWriteReadResponse(
		string resolvedPath,
		string responseJson,
		bool countRequested,
		out ODataReadFileSummary summary,
		out string error) {
		summary = null;
		error = null;
		try {
			// UTF-16 chars, so this over-counts a purely ASCII body by 2x - deliberately conservative, and it
			// runs before the parse allocates anything further.
			long responseBytes = responseJson is null ? 0 : (long)responseJson.Length * sizeof(char);
			if (responseBytes > MaxResponseBytes) {
				error = $"OData response is about {responseBytes} bytes, which exceeds the {MaxResponseBytes}-byte "
					+ "limit for one call. Narrow the query with select, or page it with top and skip.";
				return false;
			}
			(ODataReadFileSummary built, string summaryError) = BuildSummary(responseJson, countRequested);
			if (summaryError is not null) {
				error = summaryError;
				return false;
			}
			byte[] payload = Encoding.UTF8.GetBytes(responseJson);
			OutputPathConfinement.WriteAtomic(_fileSystem, resolvedPath, payload);
			summary = built;
			return true;
		} catch (Exception ex) {
			summary = null;
			error = SensitiveErrorTextRedactor.Redact($"Failed to write output-file: {ex.Message}");
			return false;
		}
	}

	private static (ODataReadFileSummary summary, string error) BuildSummary(string json, bool countRequested) {
		JsonDocument document;
		try {
			document = JsonDocument.Parse(json);
		} catch (JsonException ex) {
			return (null, SensitiveErrorTextRedactor.Redact($"Failed to parse OData response: {ex.Message}"));
		}
		using (document) {
			JsonElement root = document.RootElement;
			if (ODataResponseError.TryDetect(root, out string serverError)) {
				// Nothing is written for an error body: a file named after a successful read that holds a
				// server error is worse than no file at all.
				return (null, SensitiveErrorTextRedactor.Redact(serverError));
			}
			// Only an object or an array is OData content. A scalar body - null, true, 42, "Unauthorized" -
			// is what a proxy, an auth redirect or a misrouted request returns; persisting one as the raw
			// response reported a successful read of a file holding no records at all.
			if (root.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array)) {
				return (null,
					$"OData response is a JSON {ODataReadQuery.DescribeKind(root.ValueKind)}, not a record or a "
					+ "collection. The endpoint did not answer with OData content; check the environment and the entity name.");
			}
			// Three shapes reach here: the OData collection envelope, a bare top-level array (some endpoints
			// and $expand projections return one), and a single entity object. Without the bare-array branch
			// such a response summarized silently as zero rows and no columns. The kind has to be settled
			// BEFORE probing for the envelope property: TryGetProperty throws on anything that is not an
			// object, so a bare top-level array never reached the branch written for it.
			bool hasEnvelope = root.ValueKind == JsonValueKind.Object;
			JsonElement rows = root;
			if (hasEnvelope
				&& root.TryGetProperty("value", out JsonElement value)
				&& value.ValueKind == JsonValueKind.Array) {
				rows = value;
			}
			long? totalCount = hasEnvelope
				&& root.TryGetProperty("@odata.count", out JsonElement totalCountElement)
				&& totalCountElement.TryGetInt64(out long parsedTotalCount)
				? parsedTotalCount
				: null;
			if (countRequested && !totalCount.HasValue) {
				return (null, "Creatio did not return @odata.count for count=true; total count cannot be verified.");
			}
			string nextLink = hasEnvelope
				&& root.TryGetProperty("@odata.nextLink", out JsonElement nextLinkElement)
				&& nextLinkElement.ValueKind == JsonValueKind.String
				? nextLinkElement.GetString()
				: null;
			IEnumerable<JsonElement> rowElements = rows.ValueKind == JsonValueKind.Array
				? rows.EnumerateArray()
				: [rows];
			int recordCount = rows.ValueKind == JsonValueKind.Array ? rows.GetArrayLength() : 1;
			Dictionary<string, long> columnSizes = new(StringComparer.Ordinal);
			int rowCount = 0;
			foreach (JsonElement row in rowElements) {
				if (row.ValueKind != JsonValueKind.Object) {
					continue;
				}
				rowCount++;
				foreach (JsonProperty property in row.EnumerateObject()) {
					long size = Encoding.UTF8.GetByteCount(property.Value.GetRawText());
					columnSizes[property.Name] = columnSizes.TryGetValue(property.Name, out long current) ? current + size : size;
				}
			}
			return (new ODataReadFileSummary(rowCount, columnSizes, recordCount, nextLink, totalCount), null);
		}
	}
}

/// <summary>Compact metadata returned when an OData response is written to disk.</summary>
/// <param name="RowCount">Number of object rows written.</param>
/// <param name="ColumnSizes">UTF-8 byte totals by column.</param>
/// <param name="RecordCount">Number of records the response carries, matching the inline read's count.</param>
/// <param name="NextLink">OData next-link when more records are available beyond the requested top.</param>
/// <param name="TotalCount">Total matching records before paging, present when count=true was requested.</param>
public sealed record ODataReadFileSummary(
	int RowCount,
	IReadOnlyDictionary<string, long> ColumnSizes,
	int RecordCount,
	string NextLink,
	long? TotalCount);
