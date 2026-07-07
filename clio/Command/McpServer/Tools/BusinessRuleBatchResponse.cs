using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json.Serialization;
using Clio.Command.BusinessRules;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Response for the batch business-rule tools (create-entity-business-rules /
/// create-page-business-rules). Carries an aggregate created/failed summary plus a per-rule result
/// array so a partial failure never hides the rules that did succeed.
/// </summary>
public sealed record BusinessRuleBatchResponse {

	/// <summary>Gets the number of rules created.</summary>
	[JsonPropertyName("created")]
	[Description("Number of rules created.")]
	public int Created { get; init; }

	/// <summary>Gets the number of rules that failed.</summary>
	[JsonPropertyName("failed")]
	[Description("Number of rules that failed.")]
	public int Failed { get; init; }

	/// <summary>Gets the per-rule outcomes, one entry per input rule, in input order.</summary>
	[JsonPropertyName("results")]
	[Description("Per-rule outcomes, in input order.")]
	public IReadOnlyList<BusinessRuleResultDto> Results { get; init; } = [];

	/// <summary>Gets a request-level error that prevented the whole batch from running.</summary>
	[JsonPropertyName("error")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[Description("Request-level error that prevented the whole batch from running.")]
	public string? Error { get; init; }

	/// <summary>Builds a response from per-rule batch outcomes.</summary>
	public static BusinessRuleBatchResponse From(IReadOnlyList<BusinessRuleBatchItemResult> items) {
		IReadOnlyList<BusinessRuleResultDto> results = items
			.Select(item => new BusinessRuleResultDto {
				Name = item.Name,
				Success = item.Success,
				RuleName = item.RuleName,
				Error = item.Error
			})
			.ToList();
		return new BusinessRuleBatchResponse {
			Created = results.Count(result => result.Success),
			Failed = results.Count(result => !result.Success),
			Results = results
		};
	}

	/// <summary>Builds a response for a request-level failure (no per-rule work was attempted).</summary>
	public static BusinessRuleBatchResponse RequestError(string message) =>
		new() { Error = message };
}

public sealed record BusinessRulesReadResponse {

	[JsonPropertyName("count")]
	[Description("Number of rules returned.")]
	public int Count { get; init; }

	[JsonPropertyName("rules")]
	[Description("Persisted business rules in the create/update contract shape, including name, enabled, and block uIds for update round-trips.")]
	public IReadOnlyList<BusinessRule> Rules { get; init; } = [];

	[JsonPropertyName("error")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[Description("Request-level error that prevented the read.")]
	public string? Error { get; init; }

	public static BusinessRulesReadResponse From(IReadOnlyList<BusinessRule> rules) =>
		new() { Count = rules.Count, Rules = rules };

	public static BusinessRulesReadResponse RequestError(string message) =>
		new() { Error = message };
}

public sealed record BusinessRuleUpdateBatchResponse {

	[JsonPropertyName("updated")]
	[Description("Number of rules updated.")]
	public int Updated { get; init; }

	[JsonPropertyName("failed")]
	[Description("Number of rules that failed.")]
	public int Failed { get; init; }

	[JsonPropertyName("results")]
	[Description("Per-rule outcomes, in input order. 'name' is the rule name used as the match key.")]
	public IReadOnlyList<BusinessRuleResultDto> Results { get; init; } = [];

	[JsonPropertyName("error")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[Description("Request-level error that prevented the whole batch from running.")]
	public string? Error { get; init; }

	public static BusinessRuleUpdateBatchResponse From(IReadOnlyList<BusinessRuleBatchItemResult> items) {
		IReadOnlyList<BusinessRuleResultDto> results = ToResultDtos(items);
		return new BusinessRuleUpdateBatchResponse {
			Updated = results.Count(result => result.Success),
			Failed = results.Count(result => !result.Success),
			Results = results
		};
	}

	public static BusinessRuleUpdateBatchResponse RequestError(string message) =>
		new() { Error = message };

	internal static IReadOnlyList<BusinessRuleResultDto> ToResultDtos(IReadOnlyList<BusinessRuleBatchItemResult> items) =>
		items
			.Select(item => new BusinessRuleResultDto {
				Name = item.Name,
				Success = item.Success,
				RuleName = item.RuleName,
				Error = item.Error
			})
			.ToList();
}

public sealed record BusinessRuleDeleteBatchResponse {

	[JsonPropertyName("deleted")]
	[Description("Number of rules deleted.")]
	public int Deleted { get; init; }

	[JsonPropertyName("failed")]
	[Description("Number of rule names that failed.")]
	public int Failed { get; init; }

	[JsonPropertyName("results")]
	[Description("Per-name outcomes, in input order.")]
	public IReadOnlyList<BusinessRuleResultDto> Results { get; init; } = [];

	[JsonPropertyName("error")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[Description("Request-level error that prevented the whole batch from running.")]
	public string? Error { get; init; }

	public static BusinessRuleDeleteBatchResponse From(IReadOnlyList<BusinessRuleBatchItemResult> items) {
		IReadOnlyList<BusinessRuleResultDto> results = BusinessRuleUpdateBatchResponse.ToResultDtos(items);
		return new BusinessRuleDeleteBatchResponse {
			Deleted = results.Count(result => result.Success),
			Failed = results.Count(result => !result.Success),
			Results = results
		};
	}

	public static BusinessRuleDeleteBatchResponse RequestError(string message) =>
		new() { Error = message };
}

/// <summary>Per-rule outcome inside a <see cref="BusinessRuleBatchResponse"/>.</summary>
public sealed record BusinessRuleResultDto {

	[JsonPropertyName("name")]
	[Description("Caller-supplied item identifier: the rule caption for create, the internal rule name for update/delete.")]
	public string Name { get; init; } = string.Empty;

	[JsonPropertyName("success")]
	[Description("Whether the operation succeeded for this item.")]
	public bool Success { get; init; }

	[JsonPropertyName("ruleName")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[Description("Internal rule name when the item succeeded (generated on create; the match key on update/delete).")]
	public string? RuleName { get; init; }

	/// <summary>Gets the failure reason when not created.</summary>
	[JsonPropertyName("error")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[Description("The failure reason when not created.")]
	public string? Error { get; init; }
}
