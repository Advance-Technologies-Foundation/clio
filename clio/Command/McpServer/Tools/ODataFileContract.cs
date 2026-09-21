using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
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
	/// <param name="entityName">
	/// The requested OData entity set, used to classify a non-OData body the same way the inline read does.
	/// </param>
	/// <param name="countRequested">Whether the caller asked for a verified total count.</param>
	/// <param name="summary">Row/column summary when the method returns <see langword="true"/>.</param>
	/// <param name="failure">
	/// The classified failure when the method returns <see langword="false"/>. File mode classifies a
	/// rejected body the way the inline read does - same locally authored sentence, same
	/// <c>ODataReadErrorCodes</c> value, same HTTP status when one could be established - so a caller can
	/// branch on the code rather than on the wording, whichever read path produced it. The one part that
	/// stays inline-only is the echo of the caller's own filter/select/expand names on an
	/// <c>invalid-query</c>: that needs the bound arguments, which this contract deliberately does not take.
	/// </param>
	bool TryWriteReadResponse(
		string resolvedPath,
		byte[] responseUtf8,
		string entityName,
		bool countRequested,
		out ODataReadFileSummary summary,
		out ODataFileReadFailure failure);
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
			// Sizing the allocation from stream.Length would reopen the hole the ceiling closes: on Unix the
			// open checks the descriptor length once and hands back a LIVE stream, so a writer that grows that
			// same inode before this line is reached gets its new length allocated and read in full. Copy
			// through a bounded loop instead - the buffer never exceeds the ceiling, and the first byte past it
			// is the same typed error the open raises, whatever the file became after it was opened.
			byte[] payload = ReadBounded(stream, MaxPayloadBytes);
			//Decode explicitly rather than through a StreamReader: a StreamReader detects the byte-order mark
			//and a UTF-16 BOM SELECTS UTF-16, so the payload decodes happily and the strict UTF-8 encoding is
			//never consulted - a UTF-16 JSON file would then be POSTed despite the UTF-8-only contract. Here a
			//UTF-16 BOM starts with 0xFF or 0xFE, neither of which is a legal UTF-8 byte, so StrictUtf8 throws
			//and the caller gets the input error.
			json = StrictUtf8.GetString(StripUtf8Bom(payload));
			return true;
		} catch (InputFileTooLargeException ex) {
			// The size ceiling, reported by the confined open before the content was pulled into memory. Typed,
			// not matched on the message: the message is the platform implementation's to word.
			error = $"{optionName} is at least {ex.ObservedBytes} bytes, which exceeds the {ex.MaxBytes}-byte limit.";
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
	/// Starting size of the <see cref="ReadBounded"/> buffer. A rows file is typically a few hundred bytes to a
	/// few kilobytes, so the read usually completes in one growth step without ever approaching the ceiling.
	/// </summary>
	private const int InitialReadBufferBytes = 64 * 1024;

	/// <summary>Reads the whole stream, refusing at the first byte past <paramref name="maxBytes"/>.</summary>
	/// <param name="stream">Stream opened under the confinement; read to its end or to the ceiling.</param>
	/// <param name="maxBytes">Ceiling the result may not exceed.</param>
	/// <returns>The bytes read, never more than <paramref name="maxBytes"/>.</returns>
	/// <exception cref="InputFileTooLargeException">The stream carried more than the ceiling allows.</exception>
	/// <remarks>
	/// The ceiling is enforced HERE rather than from a length read beforehand, because the length a stream
	/// reports is a fact about the moment it was read, not a promise about the bytes that follow: the file
	/// behind an already-open descriptor can grow. Reading one byte past the ceiling is what proves the file is
	/// over it, so the buffer is sized to that one extra byte and never to whatever the file claims to be.
	/// </remarks>
	private static byte[] ReadBounded(Stream stream, long maxBytes) {
		long ceiling = maxBytes + 1;
		//Start small and grow: sizing to the ceiling up front would charge every read - most of them a few
		//hundred bytes - a full MaxPayloadBytes zeroed allocation. Growth is still capped at the ceiling, so
		//the refusal point is unchanged and stream.Length is still not trusted.
		byte[] buffer = new byte[(int)Math.Min(ceiling, InitialReadBufferBytes)];
		int total = 0;
		while (total < ceiling) {
			if (total == buffer.Length) {
				Array.Resize(ref buffer, (int)Math.Min(ceiling, (long)buffer.Length * 2));
			}
			int read = stream.Read(buffer, total, buffer.Length - total);
			if (read == 0) {
				break;
			}
			total += read;
		}
		if (total > maxBytes) {
			throw new InputFileTooLargeException(total, maxBytes);
		}
		if (total == buffer.Length) {
			return buffer;
		}
		byte[] payload = new byte[total];
		Array.Copy(buffer, payload, total);
		return payload;
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
		string entityName,
		bool countRequested,
		out ODataReadFileSummary summary,
		out ODataFileReadFailure failure) {
		summary = null;
		failure = null;
		try {
			// The ceiling is enforced by the caller WHILE the body arrives, which is the only place it can
			// actually bound anything; by here the payload is already in memory and within it.
			(ODataReadFileSummary built, ODataFileReadFailure summaryFailure) =
				BuildSummary(responseUtf8 ?? [], entityName, countRequested);
			if (summaryFailure is not null) {
				failure = summaryFailure;
				return false;
			}
			// The bytes go to disk exactly as they arrived - no decode to UTF-16 and re-encode, which would
			// both double the footprint and let an encoding round-trip alter the persisted response.
			_confinedFileAccess.WriteNew(resolvedPath, responseUtf8);
			summary = built;
			return true;
		} catch (IOException alreadyExists) when (IsAlreadyPublished(alreadyExists)) {
			// The name was taken between the pre-fetch confinement check and the publish - two concurrent
			// calls naming one output-file, which the linkat test-and-create is there to make safe. It is an
			// ARGUMENT failure, not a transport one: the response arrived whole and the request is fine,
			// only the path is. Reported as transport it reads as retryable, and a retry against the same
			// path can never succeed.
			summary = null;
			failure = new ODataFileReadFailure(
				SensitiveErrorTextRedactor.Redact(alreadyExists.Message), ODataReadErrorCodes.Argument);
			return false;
		} catch (Exception ex) {
			summary = null;
			failure = new ODataFileReadFailure(
				SensitiveErrorTextRedactor.Redact($"Failed to write output-file: {ex.Message}"),
				ODataReadErrorCodes.Transport);
			return false;
		}
	}

	/// <summary>Whether the write failed because the target name was already taken.</summary>
	/// <remarks>
	/// Matched on the locally authored sentence the confined writers raise, not on an errno: both the Unix
	/// and the Windows implementation turn their platform's "already exists" into that one message, and it
	/// is the only IOException either of them raises for a name collision.
	/// </remarks>
	/// <param name="exception">The write failure.</param>
	private static bool IsAlreadyPublished(IOException exception) =>
		exception.Message.Contains("already exists; refusing to overwrite it", StringComparison.Ordinal);

	/// <summary>
	/// Classifies the response and, when it is a genuine OData body for the entity that was requested,
	/// builds its compact summary. Nothing is written for a body this rejects.
	/// </summary>
	/// <remarks>
	/// The classification is the INLINE read's, reached through <see cref="ODataReadTool"/>: the same
	/// <c>@odata.context</c> identity test, in the same order, with the same fixed diagnostics and the
	/// same error codes. A file-mode-only "is it an object or an array" test accepted bodies the inline
	/// read refuses - an authentication page shaped as <c>{"detail":"authentication required"}</c>, and an
	/// <c>Account</c> collection answered to a read of <c>Contact</c> - and published them as a successful
	/// export.
	/// </remarks>
	private static (ODataReadFileSummary summary, ODataFileReadFailure failure) BuildSummary(byte[] json,
		string entityName, bool countRequested) {
		string entity = entityName?.Trim() ?? string.Empty;
		// An ABSENT body is not a parse failure, and must not be reported as one: the inline read gives it
		// its own transport-class diagnostic, and a 204 or an empty 200 reaches here as a zero-length array
		// that JsonDocument would reject with the "this was not JSON" sentence instead.
		if (json.Length == 0) {
			return (null, new ODataFileReadFailure(
				CreatioResponseError.DescribeEmptyReadResponse(), ODataReadErrorCodes.Transport));
		}
		JsonDocument document;
		try {
			document = JsonDocument.Parse(json);
		} catch (JsonException) {
			// The parser's own message ("'<' is an invalid start of a value") names the symptom and not the
			// cause, and it is the same body the INLINE read already classifies properly - an IIS error page
			// for an entity set with no OData controller is by far the most common one, and the actionable
			// answer is to use execute-esq instead. Answering the file mode with a raw parser message left
			// the two paths giving different diagnostics for the same server response; a real stand returned
			// exactly that for an unknown entity. The exception message is dropped rather than appended: the
			// fixed diagnostics are locally authored on purpose, so nothing server-controlled is echoed.
			string body = Encoding.UTF8.GetString(json);
			if (CreatioResponseError.TryClassifyMarkupError(body, out int? markupStatusCode)) {
				// The status travels as its own member for the reason the inline read states: the documented
				// async-gap retry after create-entity-schema has to key off 404 PROGRAMMATICALLY, and a
				// caller cannot do that by matching on a message it does not own.
				return (null, new ODataFileReadFailure(
					ODataReadTool.DescribeMarkupError(entity, markupStatusCode),
					markupStatusCode == (int)HttpStatusCode.NotFound
						? ODataReadErrorCodes.EntityNotFound
						: ODataReadErrorCodes.Transport,
					markupStatusCode));
			}
			return (null, new ODataFileReadFailure(
				CreatioResponseError.DescribeNonJsonReadResponse(), ODataReadErrorCodes.NonJsonResponse));
		}
		using (document) {
			JsonElement root = document.RootElement;
			ODataFileReadFailure contentFailure = RejectNonODataContent(root, entity);
			if (contentFailure is not null) {
				return (null, contentFailure);
			}
			(JsonElement rows, string nextLink, long? totalCount) = ReadEnvelope(root);
			if (countRequested && !totalCount.HasValue) {
				return (null, new ODataFileReadFailure(
					"Creatio did not return @odata.count for count=true; total count cannot be verified.",
					ODataReadErrorCodes.IncompleteResponse));
			}
			int recordCount = rows.ValueKind == JsonValueKind.Array ? rows.GetArrayLength() : 1;
			(int rowCount, Dictionary<string, long> columnSizes) = SummarizeRows(rows);
			return (new ODataReadFileSummary(rowCount, columnSizes, recordCount, nextLink, totalCount), null);
		}
	}

	/// <summary>
	/// Rejects a body that is not an OData read of the REQUESTED entity, before anything about it is
	/// summarized or written.
	/// </summary>
	/// <param name="root">Parsed response root.</param>
	/// <param name="entityName">Trimmed entity set the caller asked for.</param>
	/// <returns><see langword="null"/> when the body may be summarized, otherwise the classified failure.</returns>
	private static ODataFileReadFailure RejectNonODataContent(JsonElement root, string entityName) {
		// Identity FIRST, exactly as the inline read orders it: a response whose @odata.context proves it is
		// the requested top-level entity is that entity, whatever its columns are called. Running the error
		// heuristics first rejected a genuine record holding a legal column named ExceptionMessage.
		bool hasMatchingIdentity = ODataReadTool.HasMatchingODataIdentity(root, entityName);
		// The KIND matters, not just "this is an error": the richer overload is what separates an
		// invalid-query - a request the caller must change - from a server fault they should not keep
		// retrying. The cheap `out bool` overload skips building the server text and therefore cannot tell
		// the two apart, which left the file path answering server-reported-error for a body the inline
		// read classifies as invalid-query, with the two paths disagreeing on whether a retry can ever work.
		// The detected text itself is DROPPED here rather than redacted, and rather than logged: the
		// redactor removes known secret shapes, not forged instructions, opaque tokens or tenant data, and
		// this result is read by a model as trusted content. The returned sentence is locally authored and
		// bounded by construction, whatever the size of the body behind it.
		if (!hasMatchingIdentity && CreatioResponseError.TryClassify(root, CreatioResponseContext.ODataPayload,
				out ODataErrorKind kind, out string _)) {
			// Nothing is written for an error body: a file named after a successful read that holds a
			// server error is worse than no file at all.
			return new ODataFileReadFailure(
				CreatioResponseError.DescribeServerReportedReadError(kind), ODataReadTool.ErrorCodeFor(kind));
		}
		// Only a body that identifies ITSELF as the requested entity set may be published. An object or an
		// array alone was not enough: a proxy or auth body ({"detail":"authentication required"}), and an
		// Account collection answered to a read of Contact, both satisfied that test and were written out as
		// a successful export of one row.
		if (root.ValueKind != JsonValueKind.Object) {
			return new ODataFileReadFailure(
				CreatioResponseError.DescribeNonJsonReadResponse(), ODataReadErrorCodes.NonJsonResponse);
		}
		// The PRESENCE of `value` commits the body to the collection shape, exactly as the inline reader
		// commits: a non-array `value` is a refusal, not a reason to try the single-entity test instead.
		// Falling through to that test accepted {"@odata.context":"...#Contact/$entity","value":"marker"} -
		// the single-entity context is genuine, so the identity check passes - and published the marker,
		// while the inline read refused the same body. Two read paths disagreeing on one body is the defect
		// this whole classification was brought over to remove.
		bool isODataRead = root.TryGetProperty("value", out JsonElement value)
			? value.ValueKind == JsonValueKind.Array && ODataReadTool.IsCollectionResponse(root, entityName)
			: ODataReadTool.IsSingleEntityResponse(root, entityName);
		return isODataRead
			? null
			: new ODataFileReadFailure(
				CreatioResponseError.DescribeNonJsonReadResponse(), ODataReadErrorCodes.NonJsonResponse);
	}

	/// <summary>
	/// Separates the rows from the OData envelope and reads its paging annotations.
	/// </summary>
	/// <param name="root">Parsed response root, already known to be an object or an array.</param>
	/// <returns>The row carrier, the next-link, and the verified total count when the envelope carried one.</returns>
	/// <remarks>
	/// Two shapes reach here, and only two: the collection envelope, and a single entity object. A bare
	/// top-level array used to be a third, and the branch for it was removed with the code that could
	/// reach it - RejectNonODataContent now requires an object carrying an @odata.context that names the
	/// requested set, which a bare array cannot have. That IS a behaviour change against this branch's own
	/// earlier revision (a bare array used to be written), and it is the inline read's behaviour: master's
	/// identity model gives such a body the fixed non-OData rejection.
	/// </remarks>
	private static (JsonElement rows, string nextLink, long? totalCount) ReadEnvelope(JsonElement root) {
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

	/// <summary>
	/// Whether a response property is an OData control annotation rather than a data column. Any
	/// <c>@odata.*</c> member belongs to the envelope, and a single-entity response carries
	/// <c>@odata.context</c> alongside the real fields.
	/// </summary>
	/// <param name="name">Property name from the response object.</param>
	private static bool IsODataAnnotation(string name) =>
		name.StartsWith("@odata.", StringComparison.Ordinal);

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
				if (IsODataAnnotation(property.Name)) {
					continue;
				}
				long size = Encoding.UTF8.GetByteCount(property.Value.GetRawText());
				columnSizes[property.Name] = columnSizes.TryGetValue(property.Name, out long current) ? current + size : size;
			}
		}
		return (rowCount, columnSizes);
	}
}

/// <summary>A classified refusal from the file read path, shaped so the tool can answer exactly as the inline read does.</summary>
/// <param name="Error">The locally authored caller-facing sentence; never server prose.</param>
/// <param name="ErrorCode">One of <c>ODataReadErrorCodes</c>.</param>
/// <param name="StatusCode">The HTTP status when one could be established from an error page; otherwise null.</param>
public sealed record ODataFileReadFailure(string Error, string ErrorCode, int? StatusCode = null);

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
