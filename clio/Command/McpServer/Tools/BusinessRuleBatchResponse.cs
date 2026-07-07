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

/// <summary>
/// Response for the business-rule read tools (read-entity-business-rules /
/// read-page-business-rules).
/// </summary>
public sealed record BusinessRulesReadResponse {

	/// <summary>Gets the number of rules returned.</summary>
	[JsonPropertyName("count")]
	[Description("Number of rules returned.")]
	public int Count { get; init; }

	/// <summary>Gets the persisted rules, in persisted order.</summary>
	[JsonPropertyName("rules")]
	[Description("Persisted business rules. Convertible rules carry the friendly contract (with block uIds for update); non-convertible rules carry the raw metadata.")]
	public IReadOnlyList<BusinessRuleReadModel> Rules { get; init; } = [];

	/// <summary>Gets a request-level error that prevented the read.</summary>
	[JsonPropertyName("error")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[Description("Request-level error that prevented the read.")]
	public string? Error { get; init; }

	/// <summary>Builds a response from read models.</summary>
	public static BusinessRulesReadResponse From(IReadOnlyList<BusinessRuleReadModel> rules) =>
		new() { Count = rules.Count, Rules = rules };

	/// <summary>Builds a response for a request-level failure.</summary>
	public static BusinessRulesReadResponse RequestError(string message) =>
		new() { Error = message };
}

/// <summary>
/// Response for the batch business-rule update tools (update-entity-business-rules /
/// update-page-business-rules). Carries an aggregate updated/failed summary plus a per-rule result
/// array so a partial failure never hides the rules that did succeed.
/// </summary>
public sealed record BusinessRuleUpdateBatchResponse {

	/// <summary>Gets the number of rules updated.</summary>
	[JsonPropertyName("updated")]
	[Description("Number of rules updated.")]
	public int Updated { get; init; }

	/// <summary>Gets the number of rules that failed.</summary>
	[JsonPropertyName("failed")]
	[Description("Number of rules that failed.")]
	public int Failed { get; init; }

	/// <summary>Gets the per-rule outcomes, one entry per input rule, in input order.</summary>
	[JsonPropertyName("results")]
	[Description("Per-rule outcomes, in input order. 'name' is the rule name used as the match key.")]
	public IReadOnlyList<BusinessRuleResultDto> Results { get; init; } = [];

	/// <summary>Gets a request-level error that prevented the whole batch from running.</summary>
	[JsonPropertyName("error")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[Description("Request-level error that prevented the whole batch from running.")]
	public string? Error { get; init; }

	/// <summary>Builds a response from per-rule batch outcomes.</summary>
	public static BusinessRuleUpdateBatchResponse From(IReadOnlyList<BusinessRuleBatchItemResult> items) {
		IReadOnlyList<BusinessRuleResultDto> results = ToResultDtos(items);
		return new BusinessRuleUpdateBatchResponse {
			Updated = results.Count(result => result.Success),
			Failed = results.Count(result => !result.Success),
			Results = results
		};
	}

	/// <summary>Builds a response for a request-level failure (no per-rule work was attempted).</summary>
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

/// <summary>
/// Response for the batch business-rule delete tools (delete-entity-business-rules /
/// delete-page-business-rules).
/// </summary>
public sealed record BusinessRuleDeleteBatchResponse {

	/// <summary>Gets the number of rules deleted.</summary>
	[JsonPropertyName("deleted")]
	[Description("Number of rules deleted.")]
	public int Deleted { get; init; }

	/// <summary>Gets the number of rule names that failed.</summary>
	[JsonPropertyName("failed")]
	[Description("Number of rule names that failed.")]
	public int Failed { get; init; }

	/// <summary>Gets the per-name outcomes, one entry per input name, in input order.</summary>
	[JsonPropertyName("results")]
	[Description("Per-name outcomes, in input order.")]
	public IReadOnlyList<BusinessRuleResultDto> Results { get; init; } = [];

	/// <summary>Gets a request-level error that prevented the whole batch from running.</summary>
	[JsonPropertyName("error")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[Description("Request-level error that prevented the whole batch from running.")]
	public string? Error { get; init; }

	/// <summary>Builds a response from per-name batch outcomes.</summary>
	public static BusinessRuleDeleteBatchResponse From(IReadOnlyList<BusinessRuleBatchItemResult> items) {
		IReadOnlyList<BusinessRuleResultDto> results = BusinessRuleUpdateBatchResponse.ToResultDtos(items);
		return new BusinessRuleDeleteBatchResponse {
			Deleted = results.Count(result => result.Success),
			Failed = results.Count(result => !result.Success),
			Results = results
		};
	}

	/// <summary>Builds a response for a request-level failure (no per-rule work was attempted).</summary>
	public static BusinessRuleDeleteBatchResponse RequestError(string message) =>
		new() { Error = message };
}

/// <summary>Per-rule outcome inside a <see cref="BusinessRuleBatchResponse"/>.</summary>
public sealed record BusinessRuleResultDto {

	/// <summary>
	/// Gets the caller-supplied item identifier: the rule caption for create batches, the
	/// internal rule name for update and delete batches.
	/// </summary>
	[JsonPropertyName("name")]
	[Description("Caller-supplied item identifier: the rule caption for create, the internal rule name for update/delete.")]
	public string Name { get; init; } = string.Empty;

	/// <summary>Gets a value indicating whether the rule was created.</summary>
	[JsonPropertyName("success")]
	[Description("Whether the rule was created.")]
	public bool Success { get; init; }

	/// <summary>Gets the generated internal rule name when created.</summary>
	[JsonPropertyName("ruleName")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[Description("The generated internal rule name when created.")]
	public string? RuleName { get; init; }

	/// <summary>Gets the failure reason when not created.</summary>
	[JsonPropertyName("error")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[Description("The failure reason when not created.")]
	public string? Error { get; init; }
}
