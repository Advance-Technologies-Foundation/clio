using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Clio.Command.BusinessRules;

/// <summary>
/// Request for reading business rules from one entity or page scope.
/// </summary>
public sealed record BusinessRuleReadRequest(
	string ScopeType,
	string SchemaName);

/// <summary>
/// Request for reading one business rule from one entity or page scope.
/// </summary>
public sealed record BusinessRuleGetRequest(
	string ScopeType,
	string SchemaName,
	string? RuleUId,
	string? RuleName,
	string? Caption);

/// <summary>
/// Structured response for listing normalized business rules.
/// </summary>
public sealed record BusinessRuleListResponse {
	[JsonPropertyName("success")]
	public bool Success { get; init; }

	[JsonPropertyName("error")]
	public string? Error { get; init; }

	[JsonPropertyName("scopeType")]
	public string? ScopeType { get; init; }

	[JsonPropertyName("schemaName")]
	public string? SchemaName { get; init; }

	[JsonPropertyName("count")]
	public int Count { get; init; }

	[JsonPropertyName("rules")]
	public IReadOnlyList<BusinessRuleReadItem> Rules { get; init; } = [];
}

/// <summary>
/// Structured response for retrieving one normalized business rule.
/// </summary>
public sealed record BusinessRuleGetResponse {
	[JsonPropertyName("success")]
	public bool Success { get; init; }

	[JsonPropertyName("error")]
	public string? Error { get; init; }

	[JsonPropertyName("scopeType")]
	public string? ScopeType { get; init; }

	[JsonPropertyName("schemaName")]
	public string? SchemaName { get; init; }

	[JsonPropertyName("rule")]
	public BusinessRuleReadItem? Rule { get; init; }

	[JsonPropertyName("matches")]
	public IReadOnlyList<BusinessRuleIdentity> Matches { get; init; } = [];
}

/// <summary>
/// Stable business-rule identity used by read and follow-up edit/delete flows.
/// </summary>
public sealed record BusinessRuleIdentity(
	[property: JsonPropertyName("uId")] string? UId,
	[property: JsonPropertyName("name")] string? Name,
	[property: JsonPropertyName("caption")] string? Caption,
	[property: JsonPropertyName("enabled")] bool? Enabled);

/// <summary>
/// Normalized business-rule payload returned by read MCP tools.
/// </summary>
public sealed record BusinessRuleReadItem {
	[JsonPropertyName("uId")]
	public string? UId { get; init; }

	[JsonPropertyName("name")]
	public string? Name { get; init; }

	[JsonPropertyName("caption")]
	public string? Caption { get; init; }

	[JsonPropertyName("enabled")]
	public bool? Enabled { get; init; }

	[JsonPropertyName("condition")]
	public BusinessRuleConditionGroup? Condition { get; init; }

	[JsonPropertyName("actions")]
	public IReadOnlyList<BusinessRuleAction> Actions { get; init; } = [];
}
