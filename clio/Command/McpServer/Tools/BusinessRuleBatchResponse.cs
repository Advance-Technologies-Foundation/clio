using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json.Serialization;
using Clio.Command.BusinessRules;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Response for every batch business-rule tool (create / update / delete, entity and page). Carries an
/// aggregate succeeded/failed summary plus a per-item result array so a partial failure never hides the
/// items that did succeed. The operation is named by the tool, so the count field stays neutral.
/// </summary>
public sealed record BusinessRuleBatchResponse {

	/// <summary>Gets the number of items that succeeded.</summary>
	[JsonPropertyName("succeeded")]
	[Description("Number of items that succeeded.")]
	public int Succeeded { get; init; }

	/// <summary>Gets the number of items that failed.</summary>
	[JsonPropertyName("failed")]
	[Description("Number of items that failed.")]
	public int Failed { get; init; }

	/// <summary>Gets the per-item outcomes, one entry per input item, in input order.</summary>
	[JsonPropertyName("results")]
	[Description("Per-item outcomes, in input order.")]
	public IReadOnlyList<BusinessRuleResultDto> Results { get; init; } = [];

	/// <summary>Gets a request-level error that prevented the whole batch from running.</summary>
	[JsonPropertyName("error")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[Description("Request-level error that prevented the whole batch from running.")]
	public string? Error { get; init; }

	/// <summary>Builds a response from per-item batch outcomes.</summary>
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
			Succeeded = results.Count(result => result.Success),
			Failed = results.Count(result => !result.Success),
			Results = results
		};
	}

	/// <summary>Builds a response for a request-level failure (no per-item work was attempted).</summary>
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

/// <summary>Per-item outcome inside a <see cref="BusinessRuleBatchResponse"/>.</summary>
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
