using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;

namespace Clio.Command.BusinessRules.Filters;

/// <summary>
/// Client for the Creatio CrtCopilot <c>LlmEsqConverterService.svc/ConvertToEsqFilters</c>
/// endpoint. Delegates friendly-filter validation and ESQ-envelope construction to the
/// server, where in-process <c>EntitySchemaManager</c> resolves schemas and lookup records
/// without any <c>EntitySchemaDesignerService</c> round-trips. Returns the envelope as a
/// JSON string ready to be embedded verbatim into <c>BusinessRuleValueExpression.value</c>.
/// </summary>
internal interface ILlmEsqConverterServiceClient {
	/// <summary>
	/// Converts a friendly filter into a Creatio ESQ envelope JSON string by calling the
	/// server-side CrtCopilot converter. Throws <see cref="BusinessRuleFilterException"/>
	/// with <see cref="BusinessRuleFilterErrorCodes.ServerRejected"/> when the server
	/// returns a non-success response, an HTML error page, or a payload that does not match
	/// the documented contract.
	/// </summary>
	string ConvertToEsqFilter(string rootSchemaName, FriendlyFilterGroup filter);
}

internal sealed class LlmEsqConverterServiceClient(
	IApplicationClient applicationClient)
	: ILlmEsqConverterServiceClient {

	// Service classes from configuration packages with [ServiceContract] + [WebInvoke] are
	// dynamically registered by Terrasoft.Web.Common.CustomServicesParser at app start under
	// route prefix `rest/{serviceName}` (see Global.asax InitializeCustomWebServices); they
	// have no physical .svc file under ServiceModel/. CallConfigurationService is the native
	// path for these endpoints — it builds the correct URL and applies the JSON content-type
	// the WCF binding expects, avoiding the WCF "Request Error" HTML response that a raw
	// ExecutePostRequest produces.
	private const string ServiceName = "LlmEsqConverterService";
	private const string ServiceMethod = "ConvertToEsqFilters";
	private const string ResultPropertyName = "ConvertToEsqFiltersResult";
	private const string FilterFieldPath = "rule.actions[*].filter";

	private static readonly JsonSerializerOptions RequestJsonOptions = new() {
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		PropertyNamingPolicy = null
	};

	public string ConvertToEsqFilter(string rootSchemaName, FriendlyFilterGroup filter) {
		ArgumentException.ThrowIfNullOrWhiteSpace(rootSchemaName);
		ArgumentNullException.ThrowIfNull(filter);

		LlmEsqConvertRequestEnvelopeDto envelope = new() {
			FilterRequests = [
				new LlmFilterRequestDto {
					Filter = LlmEsqConverterRequestMapper.Map(filter),
					RootSchemaName = rootSchemaName
				}
			]
		};
		string requestBody = JsonSerializer.Serialize(envelope, RequestJsonOptions);
		string rawResponse;
		try {
			rawResponse = applicationClient.CallConfigurationService(ServiceName, ServiceMethod, requestBody);
		} catch (Exception ex) {
			throw new BusinessRuleFilterException(
				BusinessRuleFilterErrorCodes.ServerRejected,
				FilterFieldPath,
				$"LlmEsqConverterService.ConvertToEsqFilters failed: {ex.Message}");
		}
		return ExtractFirstFilter(rawResponse);
	}

	private static string ExtractFirstFilter(string rawResponse) {
		if (string.IsNullOrWhiteSpace(rawResponse)) {
			throw NewServerException("LlmEsqConverterService returned an empty response.");
		}
		string trimmed = rawResponse.TrimStart();
		if (trimmed.StartsWith('<')) {
			throw NewServerException(
				"LlmEsqConverterService returned an HTML error page instead of JSON. "
				+ "Verify that the CrtCopilot package is installed on the target environment "
				+ $"and that the rule payload is well-formed. Body: {Truncate(rawResponse, 500)}");
		}
		try {
			using JsonDocument outer = JsonDocument.Parse(rawResponse);
			JsonElement payload = outer.RootElement;
			// BodyStyle=Wrapped on the WCF service surfaces the result under a single
			// {"ConvertToEsqFiltersResult": "<inner-json-string>"} property. The legacy
			// non-wrapped response format is also accepted as a defensive fallback.
			if (payload.ValueKind == JsonValueKind.Object
				&& payload.TryGetProperty(ResultPropertyName, out JsonElement wrapped)) {
				payload = wrapped;
			}
			string innerJson = payload.ValueKind == JsonValueKind.String
				? payload.GetString() ?? string.Empty
				: payload.GetRawText();
			if (string.IsNullOrWhiteSpace(innerJson)) {
				throw NewServerException(
					$"LlmEsqConverterService returned an empty filter array. Body: {Truncate(rawResponse, 500)}");
			}
			using JsonDocument inner = JsonDocument.Parse(innerJson);
			if (inner.RootElement.ValueKind != JsonValueKind.Array
				|| inner.RootElement.GetArrayLength() == 0) {
				throw NewServerException(
					$"LlmEsqConverterService did not return an ESQ filter array. Body: {Truncate(rawResponse, 500)}");
			}
			return inner.RootElement[0].GetRawText();
		} catch (JsonException ex) {
			throw NewServerException(
				$"LlmEsqConverterService response was not valid JSON: {ex.Message}. Body: {Truncate(rawResponse, 500)}");
		}
	}

	private static BusinessRuleFilterException NewServerException(string message) =>
		new(BusinessRuleFilterErrorCodes.ServerRejected, FilterFieldPath, message);

	private static string Truncate(string value, int maxLength) =>
		string.IsNullOrEmpty(value) || value.Length <= maxLength ? value : value[..maxLength];
}

internal sealed class LlmEsqConvertRequestEnvelopeDto {
	[JsonPropertyName("filterRequests")]
	public LlmFilterRequestDto[] FilterRequests { get; set; } = [];
}

internal sealed class LlmFilterRequestDto {
	[JsonPropertyName("filter")]
	public LlmUnknownFilterDto Filter { get; set; } = default!;

	[JsonPropertyName("rootSchemaName")]
	public string RootSchemaName { get; set; } = default!;
}

internal sealed class LlmUnknownFilterDto {
	[JsonPropertyName("columnPath")]
	public string? ColumnPath { get; set; }

	[JsonPropertyName("comparisonType")]
	public string? ComparisonType { get; set; }

	[JsonPropertyName("value")]
	public JsonElement? Value { get; set; }

	[JsonPropertyName("logicalOperation")]
	public string? LogicalOperation { get; set; }

	[JsonPropertyName("filters")]
	public List<LlmUnknownFilterDto>? Filters { get; set; }

	[JsonPropertyName("backwardReferenceFilters")]
	public List<LlmUnknownFilterDto>? BackwardReferenceFilters { get; set; }

	[JsonPropertyName("subFilters")]
	public LlmUnknownFilterDto? SubFilters { get; set; }
}

internal static class LlmEsqConverterRequestMapper {

	public static LlmUnknownFilterDto Map(FriendlyFilterGroup group) {
		ArgumentNullException.ThrowIfNull(group);
		return new LlmUnknownFilterDto {
			LogicalOperation = group.LogicalOperation,
			Filters = group.Filters is { Count: > 0 }
				? group.Filters.Select(MapLeaf).ToList()
				: null,
			BackwardReferenceFilters = group.BackwardReferenceFilters is { Count: > 0 }
				? group.BackwardReferenceFilters.Select(MapBackward).ToList()
				: null
		};
	}

	private static LlmUnknownFilterDto MapLeaf(FriendlyFilterLeaf leaf) =>
		new() {
			ColumnPath = leaf.ColumnPath,
			ComparisonType = leaf.ComparisonType,
			Value = leaf.Value
		};

	private static LlmUnknownFilterDto MapBackward(BackwardReferenceFilter brf) =>
		new() {
			ColumnPath = brf.ReferenceColumnPath,
			SubFilters = brf.Filter is null ? null : Map(brf.Filter)
		};
}
