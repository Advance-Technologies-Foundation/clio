using System;
using System.Text.Json;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Shared JSON parsing helpers for OData v4 responses. Eliminates duplicated parse logic across
/// <see cref="ODataReadTool"/>, <see cref="ODataCreateTool"/>, and domain-specific tools such as
/// the printable-* MCP tools that issue their own OData reads and creates.
/// </summary>
internal static class ODataResponseParser {

	/// <summary>
	/// Parses a Creatio OData collection or single-entity GET response.
	/// Returns a failure when the body is a server error envelope.
	/// </summary>
	internal static ODataReadResponse ParseODataRead(string json) {
		try {
			using JsonDocument doc = JsonDocument.Parse(json);
			JsonElement root = doc.RootElement;
			if (ODataResponseError.TryDetect(root, out string serverError)) {
				return ODataReadResponse.Failure(serverError);
			}
			if (root.TryGetProperty("value", out JsonElement valueEl)) {
				int count = valueEl.ValueKind == JsonValueKind.Array ? valueEl.GetArrayLength() : 1;
				string? nextLink = root.TryGetProperty("@odata.nextLink", out JsonElement nl) ? nl.GetString() : null;
				return new ODataReadResponse(true, null, count, valueEl.Clone(), nextLink);
			}
			// Single-entity response (no value wrapper).
			return new ODataReadResponse(true, null, 1, root.Clone(), null);
		} catch (Exception ex) {
			return ODataReadResponse.Failure($"Failed to parse OData response: {ex.Message} | Response: {Truncate(json)}");
		}
	}

	/// <summary>
	/// Parses a Creatio OData POST (create) response. A non-JSON or empty body on a successful
	/// POST is treated as success without a record id.
	/// </summary>
	internal static ODataWriteResponse ParseODataCreated(string json) {
		if (string.IsNullOrWhiteSpace(json)) {
			return new ODataWriteResponse(true);
		}
		try {
			using JsonDocument doc = JsonDocument.Parse(json);
			JsonElement root = doc.RootElement;
			if (ODataResponseError.TryDetect(root, out string serverError)) {
				return ODataWriteResponse.Failure(serverError);
			}
			// The primary key is normally a GUID string, but some entities key on a numeric column;
			// accept either representation so a created record is never misreported as a failure.
			string? id = root.TryGetProperty("Id", out JsonElement idEl)
				? idEl.ValueKind switch {
					JsonValueKind.String => idEl.GetString(),
					JsonValueKind.Number => idEl.GetRawText(),
					_ => null
				}
				: null;
			if (string.IsNullOrEmpty(id)) {
				return ODataWriteResponse.Failure($"OData create did not return a record Id. Response: {Truncate(json)}");
			}
			return new ODataWriteResponse(true, null, id, root.Clone());
		} catch (JsonException) {
			// A non-JSON body on a successful POST still means the record was created.
			return new ODataWriteResponse(true);
		}
	}

	/// <summary>Truncates a raw response body for inclusion in error messages.</summary>
	internal static string Truncate(string value) {
		if (string.IsNullOrEmpty(value)) {
			return "<empty>";
		}
		return value.Length > 500 ? value[..500] + "..." : value;
	}
}
