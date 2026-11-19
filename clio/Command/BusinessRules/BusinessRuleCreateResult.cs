namespace Clio.Command.BusinessRules;

/// <summary>
/// Returns the generated internal rule name created in add-on metadata, and for an apply-static-filter rule what
/// running its filter showed.
/// </summary>
/// <param name="RuleName">The generated internal rule name.</param>
/// <param name="FilterMatchCount">For an apply-static-filter rule, how many records of the target lookup its filter
/// matched on the environment before saving.</param>
/// <param name="Warning">Set when the rule was saved but something about it could not be checked.</param>
public sealed record BusinessRuleCreateResult(string RuleName, int? FilterMatchCount = null, string? Warning = null);

/// <summary>
/// Per-rule outcome of a batch business-rule creation call. A failed item never aborts the
/// remaining items: validation/conversion failures are isolated, and a failed remote save marks
/// every successfully-converted rule of the batch as failed with the same error.
/// </summary>
/// <param name="Name">The rule caption supplied by the caller (the item identifier).</param>
/// <param name="Success">Whether the rule was created.</param>
/// <param name="RuleName">The generated internal rule name when created; otherwise <c>null</c>.</param>
/// <param name="Error">The failure reason when not created; otherwise <c>null</c>.</param>
/// <param name="FilterMatchCount">For an apply-static-filter rule, how many records of the target lookup its filter
/// matched on the environment when it was checked.</param>
/// <param name="Warning">Set when something about the rule could not be checked.</param>
public sealed record BusinessRuleBatchItemResult(
	string Name,
	bool Success,
	string? RuleName,
	string? Error,
	int? FilterMatchCount = null,
	string? Warning = null);
