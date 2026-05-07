using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;

namespace Clio.Command.BusinessRules.Filters;

/// <summary>
/// Converts a static filter contract into a Creatio ESQ envelope JSON string by calling
/// the server-side ESQ converter (currently exposed as <c>LlmEsqConverterService.svc/ConvertToEsqFilters</c>
/// in the CrtCopilot package). Returns the envelope ready to be embedded verbatim into
/// <c>BusinessRuleValueExpression.value</c>.
/// </summary>
internal interface IEsqFilterConverterClient {
	/// <summary>
	/// Converts a static filter into a Creatio ESQ envelope JSON string. Throws
	/// <see cref="BusinessRuleFilterException"/> with
	/// <see cref="BusinessRuleFilterErrorCodes.ServerRejected"/> when the server returns
	/// a non-success response, an HTML error page, or a payload that does not match the
	/// documented contract.
	/// </summary>
	string ConvertToEsqFilter(string rootSchemaName, StaticFilterGroup filter);
}

internal sealed class EsqFilterConverterClient(
	IApplicationClient applicationClient)
	: IEsqFilterConverterClient {

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

	public string ConvertToEsqFilter(string rootSchemaName, StaticFilterGroup filter) {
		ArgumentException.ThrowIfNullOrWhiteSpace(rootSchemaName);
		ArgumentNullException.ThrowIfNull(filter);

		EsqFilterConvertRequestEnvelopeDto envelope = new() {
			FilterRequests = [
				new EsqFilterRequestDto {
					Filter = EsqFilterRequestMapper.Map(filter),
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
				$"{ServiceName}.{ServiceMethod} failed: {ex.Message}");
		}
		return ExtractFirstFilter(rawResponse);
	}

	private static string ExtractFirstFilter(string rawResponse) {
		if (string.IsNullOrWhiteSpace(rawResponse)) {
			throw NewServerException($"{ServiceName} returned an empty response.");
		}
		string trimmed = rawResponse.TrimStart();
		if (trimmed.StartsWith('<')) {
			throw NewServerException(
				$"{ServiceName} returned an HTML error page instead of JSON. "
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
					$"{ServiceName} returned an empty filter array. Body: {Truncate(rawResponse, 500)}");
			}
			using JsonDocument inner = JsonDocument.Parse(innerJson);
			if (inner.RootElement.ValueKind != JsonValueKind.Array
				|| inner.RootElement.GetArrayLength() == 0) {
				throw NewServerException(
					$"{ServiceName} did not return an ESQ filter array. Body: {Truncate(rawResponse, 500)}");
			}
			return inner.RootElement[0].GetRawText();
		} catch (JsonException ex) {
			throw NewServerException(
				$"{ServiceName} response was not valid JSON: {ex.Message}. Body: {Truncate(rawResponse, 500)}");
		}
	}

	private static BusinessRuleFilterException NewServerException(string message) =>
		new(BusinessRuleFilterErrorCodes.ServerRejected, FilterFieldPath, message);

	private static string Truncate(string value, int maxLength) =>
		string.IsNullOrEmpty(value) || value.Length <= maxLength ? value : value[..maxLength];
}

internal sealed class EsqFilterConvertRequestEnvelopeDto {
	[JsonPropertyName("filterRequests")]
	public EsqFilterRequestDto[] FilterRequests { get; set; } = [];
}

internal sealed class EsqFilterRequestDto {
	[JsonPropertyName("filter")]
	public EsqFilterRequestNodeDto Filter { get; set; } = default!;

	[JsonPropertyName("rootSchemaName")]
	public string RootSchemaName { get; set; } = default!;
}

/// <summary>
/// Wire shape for one node of the LlmEsqConverter request payload. Property names match
/// the server-side <c>LLMUnknownFilterResponseContract</c> exactly so the WCF
/// <c>DataContractJsonSerializer</c> binds correctly.
/// </summary>
internal sealed class EsqFilterRequestNodeDto {
	[JsonPropertyName("columnPath")]
	public string? ColumnPath { get; set; }

	[JsonPropertyName("comparisonType")]
	public string? ComparisonType { get; set; }

	[JsonPropertyName("value")]
	public JsonElement? Value { get; set; }

	[JsonPropertyName("logicalOperation")]
	public string? LogicalOperation { get; set; }

	[JsonPropertyName("filters")]
	public List<EsqFilterRequestNodeDto>? Filters { get; set; }

	[JsonPropertyName("backwardReferenceFilters")]
	public List<EsqFilterRequestNodeDto>? BackwardReferenceFilters { get; set; }

	[JsonPropertyName("subFilters")]
	public EsqFilterRequestNodeDto? SubFilters { get; set; }
}

internal static class EsqFilterRequestMapper {

	// Server-side LlmEsqConverter.ConvertToEsqFilter calls .Select() on filtersConfig.filters
	// AND filtersConfig.backwardReferenceFilters without null-checks. If we omit either
	// (or send null), the service raises NullReferenceException and WCF returns HTTP 400
	// "Request Error". So always emit empty arrays at every nesting level.

	public static EsqFilterRequestNodeDto Map(StaticFilterGroup group) {
		ArgumentNullException.ThrowIfNull(group);
		return new EsqFilterRequestNodeDto {
			LogicalOperation = group.LogicalOperation,
			Filters = group.Filters is { Count: > 0 }
				? group.Filters.Select(MapLeaf).ToList()
				: [],
			BackwardReferenceFilters = group.BackwardReferenceFilters is { Count: > 0 }
				? group.BackwardReferenceFilters.Select(MapBackward).ToList()
				: []
		};
	}

	private static EsqFilterRequestNodeDto MapLeaf(StaticFilterLeaf leaf) =>
		new() {
			ColumnPath = leaf.ColumnPath,
			ComparisonType = leaf.ComparisonType,
			Value = leaf.Value,
			Filters = [],
			BackwardReferenceFilters = []
		};

	private static EsqFilterRequestNodeDto MapBackward(StaticFilterBackwardReference brf) =>
		new() {
			ColumnPath = brf.ReferenceColumnPath,
			SubFilters = brf.Filter is null ? null : Map(brf.Filter),
			Filters = [],
			BackwardReferenceFilters = []
		};
}
