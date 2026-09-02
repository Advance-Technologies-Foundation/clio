using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool that executes a raw EntitySchemaQuery (ESQ) SelectQuery against a Creatio environment
/// through the DataService <c>SelectQuery</c> endpoint, so AI callers can read Creatio data with a raw
/// ESQ query and also confirm that an ESQ filter is valid before saving it into a page.
/// </summary>
[McpServerToolType]
public sealed class ExecuteEsqTool(IToolCommandResolver commandResolver) {

	internal const string ToolName = "execute-esq";

	private const int DefaultTimeoutMs = 30_000;
	private const int MinTimeoutMs = 1_000;
	private const int MaxTimeoutMs = 120_000;
	private const int MaxDiagnosticPathLength = 500;
	internal const int MaxResponseSizeBytes = 200_000;
	internal const string ResultTooLargeErrorClass = "result-too-large";

	/// <summary>Executes a raw ESQ SelectQuery and returns the resulting rows.</summary>
	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description(
		"Run a raw EntitySchemaQuery (ESQ) SelectQuery against a Creatio environment and return the rows — to read " +
		"Creatio data, or to validate a widget/page filter before saving it. ESQ is a proprietary format that is " +
		"easy to mis-guess: call get-guidance for 'esq' and 'esq-filters' before composing a query. " +
		"A requested column whose columnPath does not resolve fails the call (success:false) instead of being " +
		"silently dropped from the rows. DataService responses larger than the fixed MCP-safe budget fail with " +
		"error-class=result-too-large; select explicit columns, lower rowCount, or page the query.")]
	public ExecuteEsqResponse Execute(
		[Description("Parameters: query, environment-name (required); timeout (optional).")]
		[Required]
		ExecuteEsqArgs args) {
		try {
			if (!TryNormalizeQuery(args.Query, out JsonElement query, out string queryError)) {
				return ExecuteEsqResponse.Failure(queryError);
			}
			if (!TryValidateTemporalParameterValues(query, [], out string temporalParameterError)) {
				return ExecuteEsqResponse.Failure(temporalParameterError);
			}
			if (!query.TryGetProperty("rootSchemaName", out JsonElement rootSchema)
				|| rootSchema.ValueKind != JsonValueKind.String
				|| string.IsNullOrWhiteSpace(rootSchema.GetString())) {
				return ExecuteEsqResponse.Failure(
					"query must be a SelectQuery object with a non-empty 'rootSchemaName'. See the 'esq' guidance for the envelope.");
			}
			if (string.IsNullOrWhiteSpace(args.EnvironmentName)) {
				return ExecuteEsqResponse.Failure("environment-name is required.");
			}

			int timeout = args.Timeout is { } requested
				? Math.Clamp(requested, MinTimeoutMs, MaxTimeoutMs)
				: DefaultTimeoutMs;

			EnvironmentOptions options = new() { Environment = args.EnvironmentName };
			IApplicationClient client = commandResolver.Resolve<IApplicationClient>(options);
			IServiceUrlBuilder urlBuilder = commandResolver.Resolve<IServiceUrlBuilder>(options);

			string url = urlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select);
			string responseJson = client.ExecutePostRequest(url, query.GetRawText(), timeout);
			bool responseIsTooLarge = responseJson.Length > MaxResponseSizeBytes
				|| Encoding.UTF8.GetByteCount(responseJson) > MaxResponseSizeBytes;
			if (responseIsTooLarge) {
				return ExecuteEsqResponse.ResultTooLarge(MaxResponseSizeBytes);
			}

			IReadOnlyList<string> requestedColumns = ExtractRequestedColumnAliases(query);
			string rootSchemaName = rootSchema.GetString() ?? string.Empty;
			return ParseSelectQueryResponse(responseJson, requestedColumns, rootSchemaName);
		} catch (Exception ex) when (ex is OperationCanceledException or TimeoutException) {
			// A timeout/cancellation is not a malformed-query problem, so it must not carry the
			// "you probably guessed the ESQ format wrong" recovery hint.
			return ExecuteEsqResponse.FailureWithoutGuidance(
				$"SelectQuery request timed out or was canceled (timeout window {MinTimeoutMs}-{MaxTimeoutMs} ms). Increase 'timeout' or narrow the query, then retry.");
		} catch (Exception ex) {
			return ExecuteEsqResponse.Failure(SensitiveErrorTextRedactor.Redact(ex.Message));
		}
	}

	private static bool TryValidateTemporalParameterValues(
		JsonElement element, List<(string? PropertyName, int? ArrayIndex)> path, out string error) {
		error = string.Empty;
		switch (element.ValueKind) {
			case JsonValueKind.Object:
				foreach (JsonProperty property in element.EnumerateObject()) {
					path.Add((property.Name, null));
					if (property.NameEquals("parameter")
						&& property.Value.ValueKind == JsonValueKind.Object
						&& !TryValidateTemporalParameter(property.Value, path, out error)) {
						return false;
					}
					if (!TryValidateTemporalParameterValues(property.Value, path, out error)) {
						return false;
					}
					path.RemoveAt(path.Count - 1);
				}
				return true;
			case JsonValueKind.Array:
				int index = 0;
				foreach (JsonElement item in element.EnumerateArray()) {
					path.Add((null, index));
					if (!TryValidateTemporalParameterValues(item, path, out error)) {
						return false;
					}
					path.RemoveAt(path.Count - 1);
					index++;
				}
				return true;
			default:
				return true;
		}
	}

	private static bool TryValidateTemporalParameter(
		JsonElement parameter, IReadOnlyList<(string? PropertyName, int? ArrayIndex)> path, out string error) {
		error = string.Empty;
		if (!parameter.TryGetProperty("dataValueType", out JsonElement dataValueType)
			|| !dataValueType.TryGetInt32(out int dataValueTypeCode)
			|| dataValueTypeCode is not (7 or 8 or 9)) {
			return true;
		}

		bool isJsonEncodedString = parameter.TryGetProperty("value", out JsonElement value)
			&& IsJsonEncodedString(value);
		if (isJsonEncodedString) {
			return true;
		}

		string renderedPath = RenderJsonPath(path);
		error = $"query temporal parameter at '{renderedPath}.value' has an invalid or missing value for "
			+ $"dataValueType {dataValueTypeCode}. Date, DateTime, and Time values must be JSON-encoded strings, "
			+ "for example \"value\": \"\\\"2026-01-01T00:00:00.000Z\\\"\". A plain ISO string is not accepted. "
			+ "See the 'esq' and 'esq-filters-frontend' guidance.";
		return false;
	}

	private static bool IsJsonEncodedString(JsonElement value) {
		if (value.ValueKind == JsonValueKind.String) {
			string raw = value.GetString() ?? string.Empty;
			if (raw.Length < 2
				|| raw[0] is not ('"' or '\'')
				|| raw[^1] != raw[0]) {
				return false;
			}
			try {
				return Newtonsoft.Json.JsonConvert.DeserializeObject<string>(raw) is not null;
			} catch (Newtonsoft.Json.JsonException) {
				return false;
			}
		}
		return false;
	}

	private static string RenderJsonPath(
		IReadOnlyList<(string? PropertyName, int? ArrayIndex)> segments) {
		StringBuilder builder = new("$");
		foreach ((string? propertyName, int? arrayIndex) in segments) {
			if (arrayIndex is not null) {
				if (!TryAppendBounded(builder, $"[{arrayIndex.Value}]")) {
					break;
				}
				continue;
			}
			if (!TryAppendPropertyName(builder, propertyName ?? string.Empty)) {
				break;
			}
		}
		return builder.ToString();
	}

	private static bool TryAppendPropertyName(StringBuilder builder, string propertyName) {
		bool isIdentifier = propertyName.Length > 0
			&& (char.IsLetter(propertyName[0]) || propertyName[0] == '_')
			&& propertyName.Skip(1).All(character => char.IsLetterOrDigit(character) || character == '_');
		if (isIdentifier) {
			return TryAppendBounded(builder, ".") && TryAppendBounded(builder, propertyName);
		}

		if (!TryAppendBounded(builder, "['")) {
			return false;
		}
		foreach (char character in propertyName) {
			string escaped = character switch {
				'\\' => "\\\\",
				'\'' => "\\'",
				'\n' => "\\n",
				'\r' => "\\r",
				'\t' => "\\t",
				_ when char.IsControl(character) => $"\\u{(int)character:x4}",
				_ => character.ToString()
			};
			if (!TryAppendBounded(builder, escaped)) {
				return false;
			}
		}
		return TryAppendBounded(builder, "']");
	}

	private static bool TryAppendBounded(StringBuilder builder, string value) {
		int remaining = MaxDiagnosticPathLength - builder.Length;
		if (value.Length <= remaining) {
			builder.Append(value);
			return true;
		}
		const string truncationMarker = "...";
		if (remaining < truncationMarker.Length) {
			builder.Length = MaxDiagnosticPathLength - truncationMarker.Length;
		} else {
			builder.Append(value, 0, remaining - truncationMarker.Length);
		}
		builder.Append(truncationMarker);
		return false;
	}

	private static bool TryNormalizeQuery(JsonElement query, out JsonElement normalized, out string error) {
		error = string.Empty;
		switch (query.ValueKind) {
			case JsonValueKind.Object:
				normalized = query;
				return true;
			case JsonValueKind.String:
				// Tolerate a query passed as a JSON-encoded string.
				string raw = query.GetString() ?? string.Empty;
				try {
					using JsonDocument parsed = JsonDocument.Parse(raw);
					if (parsed.RootElement.ValueKind != JsonValueKind.Object) {
						normalized = default;
						error = "query string must contain a JSON SelectQuery object.";
						return false;
					}
					normalized = parsed.RootElement.Clone();
					return true;
				} catch (JsonException jsonEx) {
					normalized = default;
					error = SensitiveErrorTextRedactor.Redact($"query is not valid JSON: {jsonEx.Message}");
					return false;
				}
			default:
				normalized = default;
				error = "query must be a JSON SelectQuery object.";
				return false;
		}
	}

	private static ExecuteEsqResponse ParseSelectQueryResponse(
		string json, IReadOnlyList<string> requestedColumns, string rootSchemaName) {
		if (string.IsNullOrWhiteSpace(json)) {
			return ExecuteEsqResponse.Failure("SelectQuery returned an empty response.");
		}
		try {
			using JsonDocument doc = JsonDocument.Parse(json);
			JsonElement root = doc.RootElement;

			bool hasSuccess = root.TryGetProperty("success", out JsonElement successEl);
			bool successIsTrue = hasSuccess && successEl.ValueKind == JsonValueKind.True;

			// Explicit DataService failure envelope: 'success' is present but not true
			// (false, or any non-true shape such as a string/number).
			if (hasSuccess && !successIsTrue) {
				return ExecuteEsqResponse.Failure(ExtractErrorMessage(root) ?? Truncate(json));
			}

			bool hasRowsArray = root.TryGetProperty("rows", out JsonElement rowsEl)
				&& rowsEl.ValueKind == JsonValueKind.Array;

			// Error bodies that were NOT explicitly marked successful and carry no rows array:
			// DataService responseStatus / errorInfo, or an ASP.NET error body. A response that
			// explicitly reported success:true is never reclassified as an error here, even if it
			// also carries a top-level responseStatus/Message (e.g. a non-row projection envelope).
			if (!successIsTrue
				&& !hasRowsArray
				&& (root.TryGetProperty("responseStatus", out _)
					|| root.TryGetProperty("errorInfo", out _)
					|| root.TryGetProperty("ExceptionMessage", out _)
					|| root.TryGetProperty("Message", out _))) {
				return ExecuteEsqResponse.Failure(ExtractErrorMessage(root) ?? Truncate(json));
			}

			if (hasRowsArray) {
				// A column path that does not resolve is NOT always rejected by the server: a
				// success:true response can simply omit the unresolved alias from every row, which
				// would silently hand a caller rows that are missing a column it explicitly asked
				// for. Detect that here and fail loudly instead, mirroring odata-read's bad-$select
				// behavior so the caller can tell a requested column was invalid.
				string? unknownColumnError = DetectUnknownColumns(rowsEl, requestedColumns, rootSchemaName);
				if (unknownColumnError is not null) {
					return ExecuteEsqResponse.Failure(unknownColumnError);
				}
				return new ExecuteEsqResponse(true, null, rowsEl.GetArrayLength(), rowsEl.Clone());
			}

			// Succeeded but no rows array (e.g. a non-standard projection): return the whole body.
			return new ExecuteEsqResponse(true, null, null, root.Clone());
		} catch (Exception ex) {
			return ExecuteEsqResponse.Failure(SensitiveErrorTextRedactor.Redact($"Failed to parse SelectQuery response: {ex.Message} | Response: {Truncate(json)}"));
		}
	}

	/// <summary>
	/// Reads the result aliases requested in <c>query.columns.items</c>. Each key of the
	/// <c>items</c> map is the alias the SelectQuery projects each requested column under, so the
	/// same keys are expected to appear on every returned row.
	/// </summary>
	private static IReadOnlyList<string> ExtractRequestedColumnAliases(JsonElement query) {
		if (query.ValueKind != JsonValueKind.Object
			|| !query.TryGetProperty("columns", out JsonElement columns)
			|| columns.ValueKind != JsonValueKind.Object
			|| !columns.TryGetProperty("items", out JsonElement items)
			|| items.ValueKind != JsonValueKind.Object) {
			return [];
		}
		return items.EnumerateObject().Select(property => property.Name).ToList();
	}

	/// <summary>
	/// Returns a descriptive error when one or more explicitly requested column aliases are absent
	/// from the returned rows (the SelectQuery silently dropped an unresolved column path), or
	/// <c>null</c> when every requested alias is present (or detection is not possible).
	/// </summary>
	private static string? DetectUnknownColumns(
		JsonElement rowsEl, IReadOnlyList<string> requestedColumns, string rootSchemaName) {
		if (requestedColumns.Count == 0 || rowsEl.GetArrayLength() == 0) {
			// With no requested aliases to verify, or no row to inspect, a dropped column cannot be
			// distinguished from a legitimately empty result, so do not raise a false positive.
			return null;
		}

		HashSet<string> presentColumns = [];
		foreach (JsonElement row in rowsEl.EnumerateArray()) {
			if (row.ValueKind != JsonValueKind.Object) {
				// A non-object row (e.g. a scalar projection) cannot be inspected by alias.
				return null;
			}
			foreach (JsonProperty property in row.EnumerateObject()) {
				presentColumns.Add(property.Name);
			}
		}

		List<string> missingColumns = requestedColumns
			.Where(alias => !presentColumns.Contains(alias))
			.ToList();
		if (missingColumns.Count == 0) {
			return null;
		}

		string columnList = string.Join(", ", missingColumns.Select(alias => $"'{alias}'"));
		string schemaSuffix = string.IsNullOrWhiteSpace(rootSchemaName)
			? string.Empty
			: $" on schema '{rootSchemaName}'";
		string columnWord = missingColumns.Count == 1 ? "column" : "columns";
		return $"unknown {columnWord} {columnList}{schemaSuffix}: the requested column was not returned by the "
			+ "SelectQuery, which means its columnPath did not resolve. Verify the column path against the schema "
			+ "(get-entity-schema-properties / find-entity-schema) and read the 'esq' guidance for the columns shape.";
	}

	private static string? ExtractErrorMessage(JsonElement root) {
		// DataService SelectQuery failure shape: { "responseStatus": { "ErrorCode", "Message" }, "success": false }.
		if (root.TryGetProperty("responseStatus", out JsonElement responseStatus)
			&& responseStatus.ValueKind == JsonValueKind.Object
			&& responseStatus.TryGetProperty("Message", out JsonElement responseStatusMessage)
			&& responseStatusMessage.ValueKind == JsonValueKind.String) {
			string message = responseStatusMessage.GetString() ?? string.Empty;
			if (responseStatus.TryGetProperty("ErrorCode", out JsonElement errorCode)
				&& errorCode.ValueKind == JsonValueKind.String
				&& !string.IsNullOrWhiteSpace(errorCode.GetString())) {
				return $"{errorCode.GetString()}: {message}";
			}
			return message;
		}
		// DataService write/contract failure shape: { "errorInfo": { "message" } }.
		if (root.TryGetProperty("errorInfo", out JsonElement errorInfo)
			&& errorInfo.ValueKind == JsonValueKind.Object
			&& errorInfo.TryGetProperty("message", out JsonElement message2)
			&& message2.ValueKind == JsonValueKind.String) {
			return message2.GetString();
		}
		// ASP.NET error body.
		if (root.TryGetProperty("ExceptionMessage", out JsonElement exceptionMessage)
			&& exceptionMessage.ValueKind == JsonValueKind.String) {
			return exceptionMessage.GetString();
		}
		if (root.TryGetProperty("Message", out JsonElement plainMessage)
			&& plainMessage.ValueKind == JsonValueKind.String) {
			return plainMessage.GetString();
		}
		return null;
	}

	private static string Truncate(string value) =>
		value.Length > 500 ? value[..500] + "..." : value;
}

/// <summary>
/// Arguments for <see cref="ExecuteEsqTool"/>.
/// </summary>
public sealed record ExecuteEsqArgs {
	/// <summary>The raw ESQ SelectQuery object to execute.</summary>
	[JsonPropertyName("query")]
	[Description(
		"Raw ESQ SelectQuery object (the same shape stored in page bodies and accepted by the DataService). " +
		"Must include 'rootSchemaName' and usually 'columns' (with an 'items' map) and/or 'filters'. " +
		"For a quick filter check, select a single COUNT(Id) aggregation column. See the 'esq' guidance for the envelope.")]
	[Required]
	public JsonElement Query { get; init; }

	/// <summary>Registered clio environment name.</summary>
	[JsonPropertyName("environment-name")]
	[Description(McpToolDescriptions.EnvironmentName)]
	[Required]
	public required string EnvironmentName { get; init; }

	/// <summary>Request timeout in milliseconds (1000-120000, default 30000).</summary>
	[JsonPropertyName("timeout")]
	[Description("Request timeout in milliseconds. Range: 1000-120000. Default: 30000.")]
	public int? Timeout { get; init; }
}

/// <summary>
/// Response returned by <see cref="ExecuteEsqTool"/>.
/// </summary>
public sealed record ExecuteEsqResponse(
	[property: JsonPropertyName("success")]
	[property: Description("Whether the SelectQuery executed successfully.")]
	bool Success,

	[property: JsonPropertyName("error")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[property: Description("Error message when success is false.")]
	string? Error,

	[property: JsonPropertyName("count")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[property: Description("Number of rows returned.")]
	int? Count,

	[property: JsonPropertyName("rows")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[property: Description("Rows returned by the SelectQuery.")]
	JsonElement? Rows,

	[property: JsonPropertyName("hint")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[property: Description("Recovery hint returned on failure, pointing to the ESQ guidance.")]
	string? Hint = null,

	[property: JsonPropertyName("error-class")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[property: Description("Stable failure classification. result-too-large means the DataService response exceeded the MCP-safe byte budget.")]
	string? ErrorClass = null) {

	/// <summary>
	/// Recovery hint attached to every failure so a caller that guessed the ESQ format is pointed at the guidance.
	/// </summary>
	internal const string GuidanceHint =
		"ESQ is a Creatio-specific JSON format that is easy to get wrong from general API knowledge. " +
		"If you have not already, call get-guidance for 'esq' and 'esq-filters' and follow their shapes before " +
		"composing or retrying this query (common mistakes: wrong expressionType/comparisonType enum values, " +
		"invented filter properties, and plain ISO date strings instead of JSON-encoded date values).";

	/// <summary>Creates a failure response carrying the ESQ guidance recovery hint.</summary>
	public static ExecuteEsqResponse Failure(string message) =>
		new(false, message, null, null, GuidanceHint);

	/// <summary>
	/// Creates a failure response without the ESQ-format guidance hint, for failures that are not
	/// about a mis-composed query (for example a timeout or cancellation).
	/// </summary>
	public static ExecuteEsqResponse FailureWithoutGuidance(string message) =>
		new(false, message, null, null, null);

	/// <summary>Creates a structured failure for a DataService response that exceeds the MCP-safe byte budget.</summary>
	/// <param name="maximumSizeBytes">Largest UTF-8 response the tool may return.</param>
	public static ExecuteEsqResponse ResultTooLarge(int maximumSizeBytes) =>
		new(
			false,
			$"SelectQuery result exceeds the {maximumSizeBytes} UTF-8 byte limit. "
			+ "Select explicit columns instead of allColumns, lower rowCount, or page with rowsOffset, then retry.",
			null,
			null,
			null,
			ExecuteEsqTool.ResultTooLargeErrorClass);
}
