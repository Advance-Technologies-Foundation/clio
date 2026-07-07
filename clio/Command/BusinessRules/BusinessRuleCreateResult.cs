namespace Clio.Command.BusinessRules;

/// <summary>
/// Returns the generated internal rule name created in add-on metadata.
/// </summary>
public sealed record BusinessRuleCreateResult(string RuleName);

/// <summary>
/// Per-rule outcome of a batch business-rule creation call. A failed item never aborts the
/// remaining items: validation/conversion failures are isolated, and a failed remote save marks
/// every successfully-converted rule of the batch as failed with the same error.
/// </summary>
/// <param name="Name">The rule caption supplied by the caller (the item identifier).</param>
/// <param name="Success">Whether the rule was created.</param>
/// <param name="RuleName">The generated internal rule name when created; otherwise <c>null</c>.</param>
/// <param name="Error">The failure reason when not created; otherwise <c>null</c>.</param>
public sealed record BusinessRuleBatchItemResult(
	string Name,
	bool Success,
	string? RuleName,
	string? Error);
