using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Clio.Common;

namespace Clio.Command.BusinessRules;

/// <summary>
/// Reads existing entity and page business rules from Creatio without mutating metadata.
/// </summary>
public interface IBusinessRuleReadService {
	/// <summary>
	/// Lists business rules in one entity or page scope.
	/// </summary>
	/// <param name="request">Read request.</param>
	/// <returns>Normalized read response.</returns>
	BusinessRuleListResponse List(BusinessRuleReadRequest request);

	/// <summary>
	/// Gets one business rule from one entity or page scope.
	/// </summary>
	/// <param name="request">Get request with exactly one selector.</param>
	/// <returns>Normalized get response.</returns>
	BusinessRuleGetResponse Get(BusinessRuleGetRequest request);
}

internal sealed class BusinessRuleReadService(
	IApplicationClient applicationClient,
	IServiceUrlBuilder serviceUrlBuilder)
	: IBusinessRuleReadService {

	private static readonly JsonSerializerOptions JsonOptions = new() {
		PropertyNameCaseInsensitive = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		WriteIndented = true
	};

	public BusinessRuleListResponse List(BusinessRuleReadRequest request) {
		ArgumentNullException.ThrowIfNull(request);
		string normalizedScopeType = NormalizeScopeType(request.ScopeType);
		ValidateSchemaName(request.SchemaName);

		BusinessRulesManagerResponse response = ExecuteGetBusinessRules(request.SchemaName, normalizedScopeType);
		if (!response.Success) {
			return CreateListError(
				normalizedScopeType,
				request.SchemaName,
				response.ErrorInfo?.Message ?? "BusinessRulesManagerService.GetBusinessRules failed.");
		}

		ScopeBusinessRulesResponse? scopeResponse = response.BusinessRules?.FirstOrDefault();
		if (scopeResponse is null || string.IsNullOrWhiteSpace(scopeResponse.BusinessRulesConfig)) {
			return new BusinessRuleListResponse {
				Success = true,
				ScopeType = normalizedScopeType,
				SchemaName = request.SchemaName,
				Count = 0,
				Rules = []
			};
		}

		IReadOnlyList<BusinessRuleReadItem> rules = ParseRules(
			scopeResponse.BusinessRulesConfig,
			normalizedScopeType);
		return new BusinessRuleListResponse {
			Success = true,
			ScopeType = normalizedScopeType,
			SchemaName = request.SchemaName,
			Count = rules.Count,
			Rules = rules
		};
	}

	public BusinessRuleGetResponse Get(BusinessRuleGetRequest request) {
		ArgumentNullException.ThrowIfNull(request);
		string? selectorError = ValidateSelector(request);
		if (!string.IsNullOrWhiteSpace(selectorError)) {
			return new BusinessRuleGetResponse {
				Success = false,
				ScopeType = request.ScopeType,
				SchemaName = request.SchemaName,
				Error = selectorError
			};
		}

		BusinessRuleListResponse listResponse = List(new BusinessRuleReadRequest(
			request.ScopeType,
			request.SchemaName));
		if (!listResponse.Success) {
			return new BusinessRuleGetResponse {
				Success = false,
				ScopeType = listResponse.ScopeType,
				SchemaName = listResponse.SchemaName,
				Error = listResponse.Error
			};
		}

		IReadOnlyList<BusinessRuleReadItem> matches = FindMatches(listResponse.Rules, request);
		if (matches.Count == 0) {
			return new BusinessRuleGetResponse {
				Success = false,
				ScopeType = listResponse.ScopeType,
				SchemaName = listResponse.SchemaName,
				Error = BuildNotFoundMessage(request)
			};
		}

		if (matches.Count > 1) {
			return new BusinessRuleGetResponse {
				Success = false,
				ScopeType = listResponse.ScopeType,
				SchemaName = listResponse.SchemaName,
				Error = "Business-rule selector is ambiguous. Use ruleUId or ruleName.",
				Matches = matches.Select(ToIdentity).ToList()
			};
		}

		return new BusinessRuleGetResponse {
			Success = true,
			ScopeType = listResponse.ScopeType,
			SchemaName = listResponse.SchemaName,
			Rule = matches[0]
		};
	}

	private BusinessRulesManagerResponse ExecuteGetBusinessRules(string schemaName, string scopeType) {
		BusinessRulesManagerRequest request = new([
			new BusinessRulesScopeRequest(
				schemaName,
				ToPlatformScopeType(scopeType),
				null)
		]);
		string body = JsonSerializer.Serialize(request, JsonOptions);
		string responseBody = applicationClient.ExecutePostRequest(
			serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.GetBusinessRules),
			body);
		return JsonSerializer.Deserialize<BusinessRulesManagerResponse>(responseBody, JsonOptions)
			?? throw new InvalidOperationException("BusinessRulesManagerService.GetBusinessRules returned an empty response.");
	}

	private static IReadOnlyList<BusinessRuleReadItem> ParseRules(
		string businessRulesConfig,
		string scopeType) {
		JsonNode? rulesNode = JsonNode.Parse(businessRulesConfig);
		if (rulesNode is not JsonArray rulesArray) {
			throw new InvalidOperationException("BusinessRulesManagerService returned businessRulesConfig that is not a JSON array.");
		}

		return rulesArray
			.OfType<JsonObject>()
			.Select(rule => BusinessRuleMetadataReadConverter.FromMetadata(rule, scopeType))
			.ToList();
	}

	private static string NormalizeScopeType(string scopeType) {
		if (string.Equals(scopeType, BusinessRuleScopeTypes.Entity, StringComparison.OrdinalIgnoreCase)) {
			return BusinessRuleScopeTypes.Entity;
		}
		if (string.Equals(scopeType, BusinessRuleScopeTypes.Page, StringComparison.OrdinalIgnoreCase)) {
			return BusinessRuleScopeTypes.Page;
		}
		throw new ArgumentException("scopeType must be either 'entity' or 'page'.");
	}

	private static string ToPlatformScopeType(string scopeType) =>
		string.Equals(scopeType, BusinessRuleScopeTypes.Entity, StringComparison.OrdinalIgnoreCase)
			? "Model"
			: "ViewModel";

	private static void ValidateSchemaName(string schemaName) {
		if (string.IsNullOrWhiteSpace(schemaName)) {
			throw new ArgumentException("schemaName is required.");
		}
	}

	private static string? ValidateSelector(BusinessRuleGetRequest request) {
		int selectorCount = CountProvided(request.RuleUId)
			+ CountProvided(request.RuleName)
			+ CountProvided(request.Caption);
		return selectorCount == 1
			? null
			: "Provide exactly one selector: ruleUId, ruleName, or caption.";
	}

	private static int CountProvided(string? value) =>
		string.IsNullOrWhiteSpace(value) ? 0 : 1;

	private static IReadOnlyList<BusinessRuleReadItem> FindMatches(
		IReadOnlyList<BusinessRuleReadItem> rules,
		BusinessRuleGetRequest request) {
		if (!string.IsNullOrWhiteSpace(request.RuleUId)) {
			return rules
				.Where(rule => string.Equals(rule.UId, request.RuleUId, StringComparison.OrdinalIgnoreCase))
				.ToList();
		}

		if (!string.IsNullOrWhiteSpace(request.RuleName)) {
			return rules
				.Where(rule => string.Equals(rule.Name, request.RuleName, StringComparison.Ordinal))
				.ToList();
		}

		return rules
			.Where(rule => string.Equals(rule.Caption, request.Caption, StringComparison.Ordinal))
			.ToList();
	}

	private static string BuildNotFoundMessage(BusinessRuleGetRequest request) {
		string selector = !string.IsNullOrWhiteSpace(request.RuleUId)
			? $"ruleUId '{request.RuleUId}'"
			: !string.IsNullOrWhiteSpace(request.RuleName)
				? $"ruleName '{request.RuleName}'"
				: $"caption '{request.Caption}'";
		return $"Business rule with {selector} was not found for {request.ScopeType} schema '{request.SchemaName}'.";
	}

	private static BusinessRuleIdentity ToIdentity(BusinessRuleReadItem rule) =>
		new(rule.UId, rule.Name, rule.Caption, rule.Enabled);

	private static BusinessRuleListResponse CreateListError(
		string scopeType,
		string schemaName,
		string error) =>
		new() {
			Success = false,
			ScopeType = scopeType,
			SchemaName = schemaName,
			Error = error
		};

	private sealed record BusinessRulesManagerRequest(
		[property: JsonPropertyName("scopes")] IReadOnlyList<BusinessRulesScopeRequest> Scopes);

	private sealed record BusinessRulesScopeRequest(
		[property: JsonPropertyName("name")] string Name,
		[property: JsonPropertyName("type")] string Type,
		[property: JsonPropertyName("modelType")] string? ModelType);

	private sealed record BusinessRulesManagerResponse {
		[JsonPropertyName("success")]
		public bool Success { get; init; }

		[JsonPropertyName("businessRules")]
		public IReadOnlyList<ScopeBusinessRulesResponse>? BusinessRules { get; init; }

		[JsonPropertyName("errorInfo")]
		public BusinessRulesErrorInfo? ErrorInfo { get; init; }
	}

	private sealed record ScopeBusinessRulesResponse {
		[JsonPropertyName("schemaName")]
		public string? SchemaName { get; init; }

		[JsonPropertyName("type")]
		public string? Type { get; init; }

		[JsonPropertyName("modelType")]
		public string? ModelType { get; init; }

		[JsonPropertyName("businessRulesConfig")]
		public string? BusinessRulesConfig { get; init; }
	}

	private sealed record BusinessRulesErrorInfo(
		[property: JsonPropertyName("message")] string? Message);
}
