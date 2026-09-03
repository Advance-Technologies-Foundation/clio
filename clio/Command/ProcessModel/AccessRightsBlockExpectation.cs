using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Clio.Command.ProcessModel;

/// <summary>
/// Post-operation check that an <c>accessRights</c> block the caller SENT was actually APPLIED by the server —
/// the <see cref="EmailBlockExpectation"/> guard, for the Change access rights element.
/// <para>The failure is the same silent one: a <c>CrtProcessBuilder</c> that predates this element declares no
/// <c>accessRights</c> member on its element descriptor and does not implement <c>IExtensibleDataObject</c>, so
/// its <c>DataContractJsonSerializer</c> DISCARDS the block and still answers <c>success:true</c>. The
/// consequence here is worse than an unconfigured email step: this element grants and REVOKES record
/// permissions, it has no output parameters, and nothing at run time reports what it did — so a revoke that
/// never landed leaves privileges in place while every signal the caller can see says the operation worked.</para>
/// <para>Detection is BEHAVIOURAL rather than version-based for the same reasons as the email guard: it verifies
/// the outcome instead of the advertised capability, and stays correct without tracking rebundle timing.</para>
/// <para>One difference from the email guard, and it is load-bearing. <see cref="DescribedElement"/> declares no
/// typed <c>accessRights</c> member, so a server that DOES support the element reports the block through the
/// <see cref="DescribedElement.AdditionalData"/> extension bag. Presence is therefore tested against that bag,
/// and the check keys on the block being ABSENT ENTIRELY rather than on entry counts — describe reports a
/// stored-but-undecodable collection as an empty array, so counting entries would raise false alarms on
/// elements that are configured perfectly well.</para>
/// <para>The checks below are pure so they can be tested without a server; the describe round trip is the
/// caller's job (the commands own the <see cref="IProcessDescriber"/> dependency).</para>
/// </summary>
public static class AccessRightsBlockExpectation {

	// Descriptor/operation JSON keys, named once so the parsing shape reads consistently and to keep the
	// repeated string literals out of the analyzer's duplicate-literal radar.
	private const string AccessRightsKey = "accessRights";

	/// <summary>
	/// Element names that a build descriptor asks to configure with access rights — every entry under
	/// <c>elements[]</c> carrying a non-null <c>accessRights</c> object. Returns an empty list for a payload with
	/// no such block, which is the common case and skips the verification entirely.
	/// </summary>
	/// <param name="descriptorJson">The build descriptor JSON exactly as the caller supplied it.</param>
	public static IReadOnlyList<string> FromDescriptor(string descriptorJson) =>
		BlockExpectationJson.Distinct(
			BlockExpectationJson.ElementsCarrying(descriptorJson, AccessRightsKey));

	/// <summary>
	/// Element names that a modify operations array asks to configure with access rights. Only <c>setElement</c>
	/// carries the block: unlike <c>email</c>, the server's <c>addElement</c> applies just the email and performer
	/// blocks, so an <c>accessRights</c> block sent with <c>addElement</c> is ignored BY DESIGN and reporting it
	/// as a silent drop would be a false alarm.
	/// </summary>
	/// <param name="operationsJson">The operations array JSON exactly as the caller supplied it.</param>
	public static IReadOnlyList<string> FromOperations(string operationsJson) =>
		BlockExpectationJson.Distinct(
			BlockExpectationJson.SetElementTargets(operationsJson, AccessRightsKey));

	/// <summary>
	/// Of the elements the caller asked to configure, those the server does NOT report an <c>accessRights</c>
	/// block for — i.e. the ones whose configuration was discarded.
	/// </summary>
	/// <param name="described">The description read back after the successful operation.</param>
	/// <param name="expected">Element names returned by <see cref="FromDescriptor"/> / <see cref="FromOperations"/>.</param>
	public static IReadOnlyList<string> Missing(DescribeProcessResult described, IReadOnlyList<string> expected) {
		if (expected.Count == 0 || described?.Elements is null) {
			// Nothing to compare against: report nothing rather than accuse the server on missing evidence.
			return Array.Empty<string>();
		}

		List<string> missing = [];
		foreach (string name in expected) {
			// Matched on NAME OR UID on purpose: setElement identifies an element by either (the server's
			// ResolveFlowElement canonicalizes both), so a caller who passed a UId would otherwise match nothing
			// and be told its configuration had been discarded when the edit in fact applied cleanly.
			DescribedElement? element = BlockExpectationJson.ResolveElement(described, name);

			// An element that is not in the read-back at all is NOT reported, matching the email guard: the
			// read-back is the only evidence this check has, and "I cannot find the element I asked about" is a
			// reason to stay quiet rather than to accuse the server.
			if (element is not null && !ReportsAccessRights(element)) {
				missing.Add(name);
			}
		}

		return missing;
	}

	/// <summary>
	/// Of the elements the caller asked to configure, those the read-back does not contain at all.
	/// <para><see cref="Missing"/> stays silent about these on purpose — the read-back cannot prove a drop for
	/// an element it never returned — but silence is exactly what this guard must not do: on an element with
	/// no output parameters, "I could not find it" and "it is configured" would otherwise reach the caller as
	/// the same empty output. They are reported separately, as unverified rather than as discarded.</para>
	/// </summary>
	public static IReadOnlyList<string> Unresolved(DescribeProcessResult described, IReadOnlyList<string> expected) {
		if (expected.Count == 0) {
			return Array.Empty<string>();
		}

		if (described?.Elements is null) {
			return expected;
		}

		return [.. expected.Where(name => BlockExpectationJson.ResolveElement(described, name) is null)];
	}

	/// <summary>
	/// Element names an <c>addElement</c> operation carried an <c>accessRights</c> block for. The server
	/// applies only the email and performer blocks there, so such a block is dropped BY DESIGN — and the
	/// caller's outcome is identical to the silent drop this class exists to catch, which is why it is
	/// reported rather than quietly excluded from <see cref="FromOperations"/>.
	/// </summary>
	public static IReadOnlyList<string> IgnoredOnAddElement(string operationsJson) =>
		BlockExpectationJson.Distinct(
			BlockExpectationJson.AddElementTargets(operationsJson, AccessRightsKey));

	/// <summary>
	/// The caller-facing warning for an <c>accessRights</c> block sent with <c>addElement</c>, which the
	/// server ignores. Returns null when there is nothing to report.
	/// </summary>
	public static string? BuildAddElementWarning(IReadOnlyList<string> ignored) {
		if (ignored.Count == 0) {
			return null;
		}

		string elements = string.Join("', '", ignored);
		string subject = BlockExpectationJson.ElementNoun(ignored.Count);
		return $"The 'accessRights' block sent with addElement for the {subject} '{elements}' was NOT applied: "
			+ "addElement applies only the email and performer blocks, so the element was created without any "
			+ "permission configuration and will grant and revoke nothing when it runs. Configure it with a "
			+ "setElement operation carrying accessRights (you can put it in the same operations array).";
	}

	/// <summary>
	/// Of the elements the caller asked to configure, those the read-back shows with NO record filter.
	/// <para>This is the element's WIDEST configuration, not one of its no-ops. The record filter decides
	/// WHICH records it acts on, and without one the runtime never enters its filter block: the query runs
	/// unfiltered and the grant or revoke lands on EVERY record of the target object, with record permissions
	/// disabled so the radius is every row rather than the rows the caller can see. Nothing refuses it and it
	/// has no output parameter to say so — and the modify surface makes it easy to reach by accident, because
	/// changing the target object clears a filter that pointed at the old one.</para>
	/// <para>A warning, not a refusal: whether the SERVER should reject this state is open decision D9 in the
	/// package repository, and reporting it here does not pre-empt that.</para>
	/// </summary>
	public static IReadOnlyList<string> WithoutRecordFilter(
			DescribeProcessResult described, IReadOnlyList<string> expected) =>
		[.. FilterlessElements(described, expected).Select(entry => entry.Key)];

	/// <summary>
	/// The same elements, paired with WHICH filter state they are in. The states have opposite blast radius
	/// and must never be reported with the same words: an element with NO filter at all acts on EVERY record,
	/// while one whose filter carries a root but no conditions takes the runtime's "filters empty" exit and
	/// changes nothing. Reasoning about either by analogy with the other gets it exactly backwards — which is
	/// how this guard shipped with the two swapped.
	/// </summary>
	private static IReadOnlyList<KeyValuePair<string, RecordFilterState>> FilterlessElements(
			DescribeProcessResult described, IReadOnlyList<string> expected) {
		if (expected.Count == 0 || described?.Elements is null) {
			return Array.Empty<KeyValuePair<string, RecordFilterState>>();
		}

		List<KeyValuePair<string, RecordFilterState>> unfiltered = [];
		foreach (string name in expected) {
			DescribedElement? element = BlockExpectationJson.ResolveElement(described, name);

			// Only an element the read-back actually returned can be judged; an absent one is reported by
			// Unresolved() instead, so it is not accused twice.
			if (element is null || !ReportsAccessRights(element)) {
				continue;
			}

			RecordFilterState state = ClassifyRecordFilter(element);
			if (state != RecordFilterState.Narrowing) {
				unfiltered.Add(new KeyValuePair<string, RecordFilterState>(name, state));
			}
		}

		return unfiltered;
	}

	/// <summary>
	/// The caller-facing warning for an element saved without a record filter. Returns null when there is
	/// nothing to report.
	/// </summary>
	public static string? BuildNoFilterWarning(
			DescribeProcessResult described, IReadOnlyList<string> expected) {
		IReadOnlyList<KeyValuePair<string, RecordFilterState>> unfiltered = FilterlessElements(described, expected);
		if (unfiltered.Count == 0) {
			return null;
		}

		// Three states, different consequences. Reporting them with one wording is how a caller who just built
		// the WIDEST possible configuration gets told the element is inert and stops looking — and how a
		// caller with a perfectly good legacy filter gets told to replace it.
		string Names(RecordFilterState state) =>
			string.Join("', '", unfiltered.Where(e => e.Value == state).Select(e => e.Key));
		string absent = Names(RecordFilterState.Absent);
		string conditionless = Names(RecordFilterState.Conditionless);
		string undecodable = Names(RecordFilterState.Undecodable);

		List<string> parts = [];
		if (absent.Length > 0) {
			// The LOUDEST of the three, because it is the only one where a successful build means a live,
			// unbounded permission change. The runtime never enters its filter block, so the query runs
			// unfiltered — and with record permissions disabled, so the radius is every row in the table.
			parts.Add($"'{absent}' has NO record filter at all, so at run time it will apply the permission "
				+ "change to EVERY record of the target object — not to none. The element's query runs with "
				+ "record permissions DISABLED, so that is every row in the table, not the rows you can see");
		}

		if (conditionless.Length > 0) {
			parts.Add($"'{conditionless}' has a record filter with NO conditions, so the runtime takes its "
				+ "\"filters empty\" exit and changes nothing — the element is inert rather than wide");
		}

		if (undecodable.Length > 0) {
			parts.Add($"'{undecodable}' HAS a stored record filter that this read-back could not decode (a "
				+ "filter saved in the legacy designer format reads back empty) — it may be narrowing exactly "
				+ "as intended, so verify it in the designer and do NOT replace it on the strength of this "
				+ "message");
		}

		// The setFilter remedy belongs only to the states that actually lack a usable filter. Prescribing it
		// for an undecodable one would tell the caller to overwrite a working filter on a live permission
		// change — the failure this whole guard exists to prevent, pointed the other way.
		// The remedy differs by direction, so it cannot be one sentence. An ABSENT filter is urgent — the
		// element is about to touch every record — while a conditionless one is merely inert. Telling a
		// caller with no filter that "nothing will happen" was the inversion this guard shipped with.
		return "The 'accessRights' configuration was saved, but " + string.Join("; and ", parts)
			+ ". This happens silently, because the element has no output parameters"
			+ (absent.Length > 0
				? ". Set the filter you mean BEFORE this process runs, with the setFilter operation (to act on "
					+ "one record, filter Id against a process parameter or a trigger output). Note that ANY "
					+ "change to the element's object clears its record filter, so an ordinary retarget lands in "
					+ "exactly this state."
				: conditionless.Length > 0
					? ". A conditionless filter is refused at build by a current CrtProcessBuilder, so an element "
						+ "carrying one was configured by an older package or in the designer. Give it the "
						+ "conditions you mean, and do not report a grant or revoke as applied until you have."
					: ". Confirm the filter in the designer before reporting a grant or revoke as applied.");
	}

	/// <summary>Which of the three reportable record-filter states an element's read-back puts it in.</summary>
	private enum RecordFilterState {

		/// <summary>A decoded filter carrying conditions or groups — it narrows something. Not reported.</summary>
		Narrowing,

		/// <summary>A decoded filter carrying neither conditions nor groups — it narrows nothing.</summary>
		Conditionless,

		/// <summary>A filter IS stored, but this read-back could not decode it.</summary>
		Undecodable,

		/// <summary>No filter is stored at all.</summary>
		Absent
	}

	// The element parameter the record filter lives in; describe decodes it into DescribedElement.Filter.
	private const string RecordFilterParameterName = "DataSourceFilters";

	// A filter counts as present only if it actually NARROWS something — but "describe returned no filter" and
	// "no filter is stored" are DIFFERENT facts, and collapsing them is how a caller gets told a working
	// element is inert. describe decodes only the modern filter wrapper, so a legacy designer-built filter
	// reads back as null while narrowing perfectly; claiming absence there invites a setFilter that OVERWRITES
	// a correct filter on a live permission change. So the stored parameter is consulted before absence is
	// claimed, and the three states get three different words.
	private static RecordFilterState ClassifyRecordFilter(DescribedElement element) {
		if (element.Filter is not null) {
			return NarrowsSomething(element.Filter) ? RecordFilterState.Narrowing : RecordFilterState.Conditionless;
		}

		DescribedParameter? stored = element.Parameters?.FirstOrDefault(parameter =>
			string.Equals(parameter?.Name, RecordFilterParameterName, StringComparison.OrdinalIgnoreCase));
		return string.IsNullOrWhiteSpace(stored?.Value)
			? RecordFilterState.Absent
			: RecordFilterState.Undecodable;
	}

	/// <summary>
	/// The caller-facing warning for dropped blocks: what happened, why, and the one action that fixes it.
	/// Returns null when nothing was dropped, so a caller can treat null as "no warning to emit".
	/// </summary>
	public static string? BuildWarning(IReadOnlyList<string> missing) {
		if (missing.Count == 0) {
			return null;
		}

		string elements = string.Join("', '", missing);
		string subject = BlockExpectationJson.ElementNoun(missing.Count);
		// States the OBSERVATION as fact and the CAUSE as the likely one — all this check saw is that the block
		// is absent from the read-back.
		return $"The operation reported success, but the saved process does NOT carry the 'accessRights' "
			+ $"configuration for the {subject} '{elements}' — the read-back shows no accessRights block. The usual "
			+ "cause is a deployed CrtProcessBuilder that predates the Change access rights element: it has no "
			+ "'accessRights' member and does not implement IExtensibleDataObject, so it discards the block instead "
			+ "of rejecting it and still answers success. The element is therefore UNCONFIGURED and will grant and "
			+ "revoke NOTHING when it runs, silently — it has no output parameters to report that. If you asked for "
			+ "a REVOKE, treat those permissions as still in place and do not report the change as applied. Install "
			+ "a package that supports the element (clio install-process-builder) and re-apply the block, or "
			+ "configure the element in the designer. If re-applying reproduces this same warning, the package "
			+ "bundled with THIS clio also predates the element — updating clio itself, or deploying a newer "
			+ "CrtProcessBuilder by other means, is then the only fix, and the block cannot be applied here.";
	}

	// A server that supports the element reports the block through the extension bag, because DescribedElement
	// declares no typed member for it. A null/absent value counts as not reported.
	// <para>The key comparison is deliberately case-INSENSITIVE. [JsonExtensionData] stores unmatched properties
	// under the server's exact JSON name and the bag's default comparer is ordinal, so the describer's
	// PropertyNameCaseInsensitive option does NOT reach it: an exact-match lookup would be the only
	// casing-sensitive comparison on this path, and a server that ever spelled the property differently would
	// flip this guard into a permanent false alarm telling callers their permissions were discarded when they
	// were not.</para>
	/// <summary>
	/// Elements whose record FILTER this batch changed without sending an accessRights block — a
	/// <c>setFilter</c> or <c>clearFilter</c> on its own.
	/// <para>These reach no other check: the guard returns early when the payload carries no block, so a batch
	/// whose only operation is <c>clearFilter</c> used to emit nothing at all. That is the most dangerous edit
	/// the surface offers, because clearing the filter moves the element from narrowing to acting on EVERY
	/// record. Naming them here lets the filter-state check cover them; they are NOT added to the
	/// block-landed check, which would accuse a payload that never sent a block.</para>
	/// </summary>
	public static IReadOnlyList<string> FilterTouched(string operationsJson) =>
		BlockExpectationJson.Distinct(
			BlockExpectationJson.OperationTargets(operationsJson, "setFilter", "clearFilter"));

	/// <summary>
	/// The caller-facing warning for a read-back that could NOT report every stored permission entry. Null when
	/// every described element reported its collections in full.
	/// <para>This is the one warning that fires on a HEALTHY write. It exists because a supplied collection
	/// REPLACES the stored one: describe, edit one entry, send it back, and every entry the read-back omitted is
	/// deleted — permissions disappearing on a routine read-modify-write, with success:true and no output
	/// parameter. The server now reports how many entries it could not show, so this can say so instead of the
	/// contract relying on the caller having read a paragraph of prose.</para>
	/// </summary>
	public static string? BuildLossyReadWarning(
			DescribeProcessResult described, IReadOnlyList<string> expected) {
		if (expected.Count == 0 || described?.Elements is null) {
			return null;
		}

		List<string> lossy = [];
		foreach (string name in expected) {
			DescribedElement? element = BlockExpectationJson.ResolveElement(described, name);
			if (element is not null && TryGetAccessRights(element, out JsonElement block)
					&& (Unreadable(block, "addUnreadable") != 0 || Unreadable(block, "removeUnreadable") != 0)) {
				lossy.Add(name);
			}
		}

		if (lossy.Count == 0) {
			return null;
		}

		string elements = string.Join("', '", lossy);
		return $"The read-back of the {BlockExpectationJson.ElementNoun(lossy.Count)} '{elements}' could NOT report "
			+ "every stored permission entry. Do NOT build a replacement collection from this description: a "
			+ "supplied 'add' or 'remove' REPLACES the stored one, so every entry the read-back omitted would be "
			+ "deleted. Omit the collection to keep what is stored, or inspect the element in the designer.";
	}

	// Reads the stored count the server reports for a collection it could not fully describe: 0 = complete,
	// a positive number = that many entries dropped, -1 = the collection did not decode so the count is unknown.
	// Absent on a server that predates the field, which reads as 0 - the old behaviour, no false alarm.
	private static int Unreadable(JsonElement block, string property) =>
		block.ValueKind == JsonValueKind.Object
		&& block.TryGetProperty(property, out JsonElement value)
		&& value.ValueKind == JsonValueKind.Number
		&& value.TryGetInt32(out int count)
			? count
			: 0;

	// Recursive on purpose: counting Groups was enough to call a filter "narrowing", so groups:[{conditions:[]}]
	// classified as Narrowing and escaped the guard while narrowing nothing. The described filter IS the root
	// group (DescribedFilter derives from DescribedFilterGroup), so the walk starts at it.
	private static bool NarrowsSomething(DescribedFilterGroup? group) =>
		group is not null
		&& ((group.Conditions?.Count ?? 0) > 0
			|| (group.Groups?.Any(NarrowsSomething) ?? false));

	private static bool TryGetAccessRights(DescribedElement element, out JsonElement block) {
		block = default;
		if (element.AdditionalData is null) {
			return false;
		}

		foreach (KeyValuePair<string, JsonElement> entry in element.AdditionalData) {
			if (string.Equals(entry.Key, AccessRightsKey, StringComparison.OrdinalIgnoreCase)) {
				block = entry.Value;
				return block.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null);
			}
		}

		return false;
	}

	private static bool ReportsAccessRights(DescribedElement element) {
		if (element.AdditionalData is null) {
			return false;
		}

		foreach (KeyValuePair<string, JsonElement> entry in element.AdditionalData) {
			if (string.Equals(entry.Key, AccessRightsKey, StringComparison.OrdinalIgnoreCase)) {
				return entry.Value.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null);
			}
		}

		return false;
	}

	// Parsed defensively: an unparseable payload is the command's problem to report through the normal error
	// path, not this check's. Returning null here just skips the verification rather than masking the real
	// failure with a second, less useful message.
}
