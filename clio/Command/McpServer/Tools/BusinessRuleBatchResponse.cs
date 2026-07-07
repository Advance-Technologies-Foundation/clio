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

/// <summary>Per-rule outcome inside a <see cref="BusinessRuleBatchResponse"/>.</summary>
public sealed record BusinessRuleResultDto {

	/// <summary>Gets the rule caption supplied by the caller (the item identifier).</summary>
	[JsonPropertyName("name")]
	[Description("The rule caption supplied by the caller.")]
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
