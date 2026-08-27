using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Clio.Command;
using IFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Command.McpServer.Tools;

/// <summary>Loads OData write payloads and persists OData read responses without routing large values through MCP.</summary>
internal static class ODataFileContract {

	/// <summary>Reads a UTF-8 JSON file and returns a caller-facing error instead of throwing.</summary>
	internal static bool TryReadJson(string path, string optionName, out string json, out string error) {
		json = null;
		error = null;
		if (string.IsNullOrWhiteSpace(path)) {
			error = $"{optionName} must not be empty.";
			return false;
		}
		try {
			if (!File.Exists(path)) {
				error = $"{optionName} file was not found: '{path}'.";
				return false;
			}
			json = File.ReadAllText(path, Encoding.UTF8);
			return true;
		} catch (Exception ex) {
			error = $"Failed to read {optionName} '{path}': {ex.Message}";
			return false;
		}
	}

	/// <summary>Writes a raw OData response to a confined, new file and returns its compact summary.</summary>
	internal static bool TryWriteReadResponse(
		string outputFile,
		string responseJson,
		out string resolvedPath,
		out ODataReadFileSummary summary,
		out string error) {
		resolvedPath = null;
		summary = null;
		error = null;
		try {
			IFileSystem fileSystem = new System.IO.Abstractions.FileSystem();
			(string path, string pathError) = OutputPathConfinement.Resolve(fileSystem, outputFile);
			if (pathError is not null) {
				error = pathError;
				return false;
			}
			OutputPathConfinement.WriteAtomic(fileSystem, path, responseJson);
			resolvedPath = path;
			summary = BuildSummary(responseJson);
			return true;
		} catch (Exception ex) {
			error = $"Failed to write output-file '{outputFile}': {ex.Message}";
			return false;
		}
	}

	private static ODataReadFileSummary BuildSummary(string json) {
		using JsonDocument document = JsonDocument.Parse(json);
		JsonElement root = document.RootElement;
		JsonElement rows = root.TryGetProperty("value", out JsonElement value) && value.ValueKind == JsonValueKind.Array
			? value
			: root.ValueKind == JsonValueKind.Object ? root : default;
		IEnumerable<JsonElement> rowElements = rows.ValueKind == JsonValueKind.Array
			? rows.EnumerateArray()
			: rows.ValueKind == JsonValueKind.Object ? [rows] : [];
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
public sealed record ODataReadFileSummary(
	int RowCount,
	IReadOnlyDictionary<string, long> ColumnSizes);
