using System;
using System.Collections.Generic;
using System.Linq;

namespace Clio.Command.ProcessModel;

/// <summary>
/// What one create/modify payload asked of each element, named once so the post-operation checks stop
/// choosing between element lists at every call site.
///
/// <para>Each check speaks for a different subset, and the subsets are NOT interchangeable: a check that
/// speaks for a block the caller SENT must never accuse an element that sent none, while the record-filter
/// check has to cover both — a <c>clearFilter</c> carries no block, yet it is the edit that moves an element
/// from narrowing to acting on every record. Before this type those subsets were re-derived inline, four
/// different ways across six checks, and fixing one of them meant re-reasoning about the other five. Three
/// separate fixes were applied to some of the paths and not the rest, each time leaving the remaining ones
/// stating something false about a live permission change.</para>
///
/// <para>So the mapping is declared here, once, with the reason attached to the name. A new check picks the
/// property whose documentation matches what it asserts; it does not rebuild a list.</para>
/// </summary>
/// <param name="ConfiguredRights">
/// Elements this payload sent an <c>accessRights</c> block for. Everything that speaks for a block the caller
/// SENT — did it land, was the read-back lossy, is it missing — uses exactly this list.
/// </param>
/// <param name="ConfiguredEmail">Elements this payload sent an email block for.</param>
/// <param name="FilterTouched">
/// Elements whose record FILTER this payload changed in a way that can widen them (today: <c>clearFilter</c>).
/// These sent no block, so no block-shaped check may accuse them.
/// </param>
internal sealed record BlockExpectationIntent(
	IReadOnlyList<string> ConfiguredRights,
	IReadOnlyList<string> ConfiguredEmail,
	IReadOnlyList<string> FilterTouched) {

	/// <summary>Nothing to verify, so the caller can skip the read-back entirely.</summary>
	internal bool IsEmpty =>
		ConfiguredRights.Count == 0 && ConfiguredEmail.Count == 0 && FilterTouched.Count == 0;

	/// <summary>
	/// Elements this payload re-filtered WITHOUT sending a block. They get their own wording wherever the
	/// message would otherwise name the <c>accessRights</c> configuration: they sent none, and
	/// <c>clearFilter</c> is equally legal on readData / changeData / signalStart, so the block wording would
	/// be false for the commonest use of the operation.
	/// </summary>
	internal IReadOnlyList<string> RefilteredOnly =>
		[.. FilterTouched.Except(ConfiguredRights, StringComparer.OrdinalIgnoreCase)];

	/// <summary>
	/// Every element whose record-filter STATE this payload could have changed — configured or merely
	/// re-filtered. Only the filter-state check gets this wider list; the block-shaped checks would accuse a
	/// payload that never sent a block.
	/// </summary>
	internal IReadOnlyList<string> RecordFilterSubjects =>
		FilterTouched.Count == 0
			? ConfiguredRights
			: [.. ConfiguredRights.Concat(FilterTouched).Distinct(StringComparer.OrdinalIgnoreCase)];

	/// <summary>The create path sends blocks but never re-filters, so its intent carries no filter targets.</summary>
	internal static BlockExpectationIntent FromDescriptor(string descriptorJson) =>
		new(AccessRightsBlockExpectation.FromDescriptor(descriptorJson),
			EmailBlockExpectation.FromDescriptor(descriptorJson),
			[]);

	/// <summary>The modify path can do both, and can do the second WITHOUT the first.</summary>
	internal static BlockExpectationIntent FromOperations(string operationsJson) =>
		new(AccessRightsBlockExpectation.FromOperations(operationsJson),
			EmailBlockExpectation.FromOperations(operationsJson),
			AccessRightsBlockExpectation.FilterTouched(operationsJson));
}
