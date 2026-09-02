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
	/// <param name="responseUtf8">Raw response body, as the UTF-8 bytes that arrived on the wire.</param>
	/// <param name="countRequested">Whether the caller asked for a verified total count.</param>
	/// <param name="summary">Row/column summary when the method returns <see langword="true"/>.</param>
	/// <param name="error">Caller-facing error when the method returns <see langword="false"/>.</param>
	bool TryWriteReadResponse(
		string resolvedPath,
		byte[] responseUtf8,
		bool countRequested,
		out ODataReadFileSummary summary,
		out string error);
}

/// <inheritdoc cref="IODataFileContract"/>
public sealed class ODataFileContract(IFileSystem fileSystem, IConfinedFileAccess confinedFileAccess)
	: IODataFileContract {

	//File access and confinement are the whole behaviour of this service, and IFileSystem is registered in
	//DI, so a `new FileSystem()` fallback would mask missing wiring and let a unit test touch the real host.
	private readonly IFileSystem _fileSystem =
		fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

	//The confinement DECISION is made against IFileSystem; the actual open is made through this, which binds
	//the operation to directory handles so a component swapped after the decision cannot redirect it.
	private readonly IConfinedFileAccess _confinedFileAccess =
		confinedFileAccess ?? throw new ArgumentNullException(nameof(confinedFileAccess));

	/// <summary>
	/// Upper bound on a file-backed payload. The decoded string plus <c>JsonDocument.Parse</c> plus
	/// <c>Clone</c> hold the content three times over, so an unbounded input is a memory-exhaustion lever for
	/// a caller that only controls a path. 10 MB is far above any legitimate OData write payload.
	/// </summary>
	public const long MaxPayloadBytes = 10L * 1024 * 1024;

	/// <summary>
	/// Upper bound on a response body. <c>top &lt;= 100</c> bounds the ROW count, not the byte count: a single
	/// large field or an $expand projection can return tens of megabytes. It is enforced WHILE the body is
	/// received (see <c>BoundedHttpResponseReader</c>), because a check that runs after the body has been
	/// materialized cannot prevent the allocation it is meant to prevent. 64 MB is far above any legitimate
	/// page of 100 records.
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
			// The open is bound to DIRECTORY HANDLES, not to the pathname that was just approved: the
			// descent refuses to follow a link at any component, so a directory replaced between the
			// approval and the open cannot redirect the read. The length bound and the bytes both come
			// from that one opened stream, so no re-open can land on a different file either.
			// The ceiling is passed INTO the open so it bounds the read itself: a stream handed back and
			// measured afterwards has already cost whatever the file contained, which is the exhaustion the
			// bound exists to prevent.
			using Stream stream = _confinedFileAccess.OpenRead(resolvedPath, MaxPayloadBytes);
			long length = stream.Length;
			byte[] payload = new byte[(int)length];
			stream.ReadExactly(payload, 0, payload.Length);
			//Decode explicitly rather than through a StreamReader: a StreamReader detects the byte-order mark
			//and a UTF-16 BOM SELECTS UTF-16, so the payload decodes happily and the strict UTF-8 encoding is
			//never consulted - a UTF-16 JSON file would then be POSTed despite the UTF-8-only contract. Here a
			//UTF-16 BOM starts with 0xFF or 0xFE, neither of which is a legal UTF-8 byte, so StrictUtf8 throws
			//and the caller gets the input error.
			json = StrictUtf8.GetString(StripUtf8Bom(payload));
			return true;
		} catch (IOException ex) when (ex.Message.Contains("exceeds", StringComparison.Ordinal)) {
			// The size ceiling, reported by the confined open before the content was pulled into memory.
			error = $"{optionName} {ex.Message}";
			return false;
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
		byte[] responseUtf8,
		bool countRequested,
		out ODataReadFileSummary summary,
		out string error) {
		summary = null;
		error = null;
		try {
			// The ceiling is enforced by the caller WHILE the body arrives, which is the only place it can
			// actually bound anything; by here the payload is already in memory and within it.
			(ODataReadFileSummary built, string summaryError) = BuildSummary(responseUtf8 ?? [], countRequested);
			if (summaryError is not null) {
				return Fail(summaryError, out error);
			}
			// The bytes go to disk exactly as they arrived - no decode to UTF-16 and re-encode, which would
			// both double the footprint and let an encoding round-trip alter the persisted response.
			_confinedFileAccess.WriteNew(resolvedPath, responseUtf8);
			summary = built;
			return true;
		} catch (Exception ex) {
			summary = null;
			return Fail(SensitiveErrorTextRedactor.Redact($"Failed to write output-file: {ex.Message}"), out error);
		}
	}

	private static bool Fail(string message, out string error) {
		error = message;
		return false;
	}

	private static (ODataReadFileSummary summary, string error) BuildSummary(byte[] json, bool countRequested) {
		JsonDocument document;
		try {
			document = JsonDocument.Parse(json);
		} catch (JsonException ex) {
			return (null, SensitiveErrorTextRedactor.Redact($"Failed to parse OData response: {ex.Message}"));
		}
		using (document) {
			JsonElement root = document.RootElement;
			string contentError = RejectNonODataContent(root);
			if (contentError is not null) {
				return (null, contentError);
			}
			(JsonElement rows, string nextLink, long? totalCount) = ReadEnvelope(root);
			if (countRequested && !totalCount.HasValue) {
				return (null, ODataReadQuery.MissingCountMessage);
			}
			int recordCount = rows.ValueKind == JsonValueKind.Array ? rows.GetArrayLength() : 1;
			(int rowCount, Dictionary<string, long> columnSizes) = SummarizeRows(rows);
			return (new ODataReadFileSummary(rowCount, columnSizes, recordCount, nextLink, totalCount), null);
		}
	}

	/// <summary>
	/// Rejects a body that is not OData content at all, before anything about it is summarized or written.
	/// </summary>
	/// <param name="root">Parsed response root.</param>
	/// <returns><c>null</c> when the body may be summarized, otherwise the caller-facing error.</returns>
	private static string RejectNonODataContent(JsonElement root) {
		if (ODataResponseError.TryDetect(root, out string serverError)) {
			// Nothing is written for an error body: a file named after a successful read that holds a
			// server error is worse than no file at all.
			return SensitiveErrorTextRedactor.Redact(serverError);
		}
		// Only an object or an array is OData content. A scalar body - null, true, 42, "Unauthorized" -
		// is what a proxy, an auth redirect or a misrouted request returns; persisting one as the raw
		// response reported a successful read of a file holding no records at all.
		return root.ValueKind is JsonValueKind.Object or JsonValueKind.Array
			? null
			: ODataReadQuery.DescribeNonODataContent(root.ValueKind);
	}

	/// <summary>
	/// Separates the rows from the OData envelope and reads its paging annotations.
	/// </summary>
	/// <param name="root">Parsed response root, already known to be an object or an array.</param>
	/// <returns>The row carrier, the next-link, and the verified total count when the envelope carried one.</returns>
	/// <remarks>
	/// Three shapes reach here: the collection envelope, a bare top-level array (some endpoints and $expand
	/// projections return one), and a single entity object. The kind has to be settled BEFORE probing for the
	/// envelope property: TryGetProperty throws on anything that is not an object, so a bare top-level array
	/// never reached the branch written for it.
	/// </remarks>
	private static (JsonElement rows, string nextLink, long? totalCount) ReadEnvelope(JsonElement root) {
		if (root.ValueKind != JsonValueKind.Object) {
			return (root, null, null);
		}
		JsonElement rows = root.TryGetProperty("value", out JsonElement value) && value.ValueKind == JsonValueKind.Array
			? value
			: root;
		long? totalCount = root.TryGetProperty("@odata.count", out JsonElement totalCountElement)
			&& totalCountElement.TryGetInt64(out long parsedTotalCount)
			? parsedTotalCount
			: null;
		string nextLink = root.TryGetProperty("@odata.nextLink", out JsonElement nextLinkElement)
			&& nextLinkElement.ValueKind == JsonValueKind.String
			? nextLinkElement.GetString()
			: null;
		return (rows, nextLink, totalCount);
	}

	/// <summary>Counts object rows and totals each column's UTF-8 byte size across them.</summary>
	/// <param name="rows">Row array, or a single entity object.</param>
	private static (int rowCount, Dictionary<string, long> columnSizes) SummarizeRows(JsonElement rows) {
		IEnumerable<JsonElement> rowElements = rows.ValueKind == JsonValueKind.Array
			? rows.EnumerateArray()
			: [rows];
		Dictionary<string, long> columnSizes = new(StringComparer.Ordinal);
		int rowCount = 0;
		foreach (JsonElement row in rowElements) {
			if (row.ValueKind != JsonValueKind.Object) {
				continue;
			}
			rowCount++;
			foreach (JsonProperty property in row.EnumerateObject()) {
				// Envelope annotations are not columns. A single-entity response carries @odata.context
				// beside its real fields, so the summary reported a data column the inline read never
				// surfaces - the same body described two different ways by the two read paths.
				if (ODataReadQuery.IsODataAnnotation(property.Name)) {
					continue;
				}
				long size = Encoding.UTF8.GetByteCount(property.Value.GetRawText());
				columnSizes[property.Name] = columnSizes.TryGetValue(property.Name, out long current) ? current + size : size;
			}
		}
		return (rowCount, columnSizes);
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
