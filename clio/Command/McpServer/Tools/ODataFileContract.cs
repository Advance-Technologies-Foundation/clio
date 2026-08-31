using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Clio.Command;
using Clio.Common;
using IFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Command.McpServer.Tools;

/// <summary>Loads OData write payloads and persists OData read responses without routing large values through MCP.</summary>
internal static class ODataFileContract {

	/// <summary>
	/// Upper bound on a file-backed payload. <c>ReadAllText</c> + <c>JsonDocument.Parse</c> + <c>Clone</c> hold
	/// the content three times over, so an unbounded input is a memory-exhaustion lever for a caller that only
	/// controls a path. 10 MB is far above any legitimate OData write payload.
	/// </summary>
	internal const long MaxPayloadBytes = 10L * 1024 * 1024;

	/// <summary>
	/// UTF-8 that THROWS on an invalid byte sequence instead of substituting U+FFFD. A replaced character
	/// still parses as JSON, so a corrupted payload would reach the OData endpoint as altered data.
	/// </summary>
	private static readonly Encoding StrictUtf8 = new UTF8Encoding(
		encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

	/// <summary>
	/// Reads a UTF-8 JSON file and returns a caller-facing error instead of throwing. The path is confined to the
	/// workspace anchor or the OS temp directory, symmetric with the write path: without that, a file-backed
	/// payload argument is an arbitrary file reader, and a prompt-injection payload could point it at clio's own
	/// credentials store and have the contents POSTed to the OData endpoint.
	/// </summary>
	internal static bool TryReadJson(
		IFileSystem fileSystem, string path, string optionName, out string json, out string error) {
		json = null;
		error = null;
		if (string.IsNullOrWhiteSpace(path)) {
			error = $"{optionName} must not be empty.";
			return false;
		}
		try {
			(string resolvedPath, string pathError) = OutputPathConfinement.ResolveForRead(fileSystem, path, optionName);
			if (pathError is not null) {
				error = pathError;
				return false;
			}
			long length = fileSystem.FileInfo.New(resolvedPath).Length;
			if (length > MaxPayloadBytes) {
				error = $"{optionName} is {length} bytes, which exceeds the {MaxPayloadBytes}-byte limit.";
				return false;
			}
			json = fileSystem.File.ReadAllText(resolvedPath, StrictUtf8);
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
	/// Confines an output path and returns the resolved absolute form, WITHOUT writing anything. Callers resolve
	/// before the remote call so a rejected path does not cost a full fetch first.
	/// </summary>
	internal static bool TryResolveOutputPath(
		IFileSystem fileSystem, string outputFile, out string resolvedPath, out string error) {
		resolvedPath = null;
		error = null;
		try {
			(string path, string pathError) = OutputPathConfinement.Resolve(fileSystem, outputFile);
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

	/// <summary>
	/// Writes a raw OData response to an already-confined path and returns its compact summary. The summary is
	/// built BEFORE the write: building it after would leave an orphaned file on disk whenever summarizing threw,
	/// with the call reported as failed and every retry then refused by the "already exists" guard.
	/// </summary>
	internal static bool TryWriteReadResponse(
		IFileSystem fileSystem,
		string resolvedPath,
		string responseJson,
		out ODataReadFileSummary summary,
		out string error) {
		summary = null;
		error = null;
		try {
			summary = BuildSummary(responseJson);
			OutputPathConfinement.WriteAtomic(fileSystem, resolvedPath, responseJson);
			return true;
		} catch (Exception ex) {
			summary = null;
			error = SensitiveErrorTextRedactor.Redact($"Failed to write output-file: {ex.Message}");
			return false;
		}
	}

	private static ODataReadFileSummary BuildSummary(string json) {
		using JsonDocument document = JsonDocument.Parse(json);
		JsonElement root = document.RootElement;
		// Three shapes reach here: the OData collection envelope, a bare top-level array (some endpoints and
		// $expand projections return one), and a single entity object. Without the bare-array branch such a
		// response summarized silently as zero rows and no columns.
		// The kind has to be settled BEFORE probing for the envelope property: TryGetProperty throws
		// InvalidOperationException on anything that is not an object, so a bare top-level array never
		// reached the branch written for it and the whole file write was reported as failed.
		JsonElement rows = default;
		if (root.ValueKind == JsonValueKind.Array) {
			rows = root;
		}
		else if (root.ValueKind == JsonValueKind.Object) {
			rows = root.TryGetProperty("value", out JsonElement value) && value.ValueKind == JsonValueKind.Array
				? value
				: root;
		}
		IEnumerable<JsonElement> rowElements = [];
		if (rows.ValueKind == JsonValueKind.Array) {
			rowElements = rows.EnumerateArray();
		}
		else if (rows.ValueKind == JsonValueKind.Object) {
			rowElements = [rows];
		}
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
		return new ODataReadFileSummary(rowCount, columnSizes);
	}
}

/// <summary>Compact metadata returned when an OData response is written to disk.</summary>
internal sealed record ODataReadFileSummary(
	int RowCount,
	IReadOnlyDictionary<string, long> ColumnSizes);
