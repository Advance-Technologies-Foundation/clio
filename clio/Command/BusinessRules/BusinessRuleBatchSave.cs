using System;
using System.Collections.Generic;

namespace Clio.Command.BusinessRules;

/// <summary>
/// Shared helper for the entity/page batch business-rule services that performs the single
/// add-on save for a batch and stamps each pending item's outcome. On a clean save every
/// converted rule becomes a success; on a save failure every converted rule of the batch shares
/// the same error (single-save atomicity), while per-rule validation/conversion failures recorded
/// before the save are left untouched.
/// </summary>
internal static class BusinessRuleBatchSave {
	/// <summary>
	/// Runs <paramref name="save"/> once and writes the result of every <paramref name="pending"/>
	/// item into <paramref name="results"/> by its original input index.
	/// </summary>
	internal static void StampOutcome(
		BusinessRuleBatchItemResult[] results,
		IReadOnlyList<(int Index, string Caption, string RuleName)> pending,
		Action save) {
		try {
			save();
			foreach ((int index, string caption, string ruleName) in pending) {
				results[index] = new BusinessRuleBatchItemResult(caption, true, ruleName, null);
			}
		} catch (Exception exception) {
			foreach ((int index, string caption, string _) in pending) {
				results[index] = new BusinessRuleBatchItemResult(caption, false, null, exception.Message);
			}
		}
	}
}
