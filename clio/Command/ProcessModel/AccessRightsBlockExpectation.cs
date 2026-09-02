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
	private const string ElementsKey = "elements";
	private const string AccessRightsKey = "accessRights";

	/// <summary>
	/// Element names that a build descriptor asks to configure with access rights — every entry under
	/// <c>elements[]</c> carrying a non-null <c>accessRights</c> object. Returns an empty list for a payload with
	/// no such block, which is the common case and skips the verification entirely.
	/// </summary>
	/// <param name="descriptorJson">The build descriptor JSON exactly as the caller supplied it.</param>
	public static IReadOnlyList<string> FromDescriptor(string descriptorJson) {
		JsonObject? descriptor = TryParse(descriptorJson) as JsonObject;
		if (descriptor?[ElementsKey] is not JsonArray elements) {
			return Array.Empty<string>();
		}

		List<string> names = [];
		foreach (JsonNode? element in elements) {
			if (element is not JsonObject candidate || candidate[AccessRightsKey] is not JsonObject) {
				continue;
			}

			string? name = candidate["name"]?.GetValue<string>();
			if (!string.IsNullOrWhiteSpace(name)) {
				names.Add(name);
			}
		}

		return names;
	}

	/// <summary>
	/// Element names that a modify operations array asks to configure with access rights. Only <c>setElement</c>
	/// carries the block: unlike <c>email</c>, the server's <c>addElement</c> applies just the email and performer
	/// blocks, so an <c>accessRights</c> block sent with <c>addElement</c> is ignored BY DESIGN and reporting it
	/// as a silent drop would be a false alarm.
	/// </summary>
	/// <param name="operationsJson">The operations array JSON exactly as the caller supplied it.</param>
	public static IReadOnlyList<string> FromOperations(string operationsJson) {
		if (TryParse(operationsJson) is not JsonArray operations) {
			return Array.Empty<string>();
		}

		List<string> names = [];
		foreach (JsonNode? operation in operations) {
			if (operation is not JsonObject op) {
				continue;
			}

			if (op["elementUpdate"] is JsonObject update && update[AccessRightsKey] is JsonObject) {
				string? name = op["elementName"]?.GetValue<string>();
				if (!string.IsNullOrWhiteSpace(name)) {
					names.Add(name);
				}
			}
		}

		return names;
	}

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
			DescribedElement? element = described.Elements.FirstOrDefault(e =>
				string.Equals(e?.Name, name, StringComparison.OrdinalIgnoreCase)
				|| string.Equals(e?.Uid, name, StringComparison.OrdinalIgnoreCase));

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

		List<string> unresolved = [];
		foreach (string name in expected) {
			bool found = described.Elements.Any(e =>
				string.Equals(e?.Name, name, StringComparison.OrdinalIgnoreCase)
				|| string.Equals(e?.Uid, name, StringComparison.OrdinalIgnoreCase));
			if (!found) {
				unresolved.Add(name);
			}
		}

		return unresolved;
	}

	/// <summary>
	/// Element names an <c>addElement</c> operation carried an <c>accessRights</c> block for. The server
	/// applies only the email and performer blocks there, so such a block is dropped BY DESIGN — and the
	/// caller's outcome is identical to the silent drop this class exists to catch, which is why it is
	/// reported rather than quietly excluded from <see cref="FromOperations"/>.
	/// </summary>
	public static IReadOnlyList<string> IgnoredOnAddElement(string operationsJson) {
		if (TryParse(operationsJson) is not JsonArray operations) {
			return Array.Empty<string>();
		}

		List<string> names = [];
		foreach (JsonNode? operation in operations) {
			if (operation is not JsonObject op
				|| op["element"] is not JsonObject added
				|| added[AccessRightsKey] is not JsonObject) {
				continue;
			}

			string? name = added["name"]?.GetValue<string>();
			if (!string.IsNullOrWhiteSpace(name)) {
				names.Add(name);
			}
		}

		return names;
	}

	/// <summary>
	/// The caller-facing warning for an <c>accessRights</c> block sent with <c>addElement</c>, which the
	/// server ignores. Returns null when there is nothing to report.
	/// </summary>
	public static string? BuildAddElementWarning(IReadOnlyList<string> ignored) {
		if (ignored.Count == 0) {
			return null;
		}

		string elements = string.Join("', '", ignored);
		string subject = ignored.Count == 1 ? "element" : "elements";
		return $"The 'accessRights' block sent with addElement for the {subject} '{elements}' was NOT applied: "
			+ "addElement applies only the email and performer blocks, so the element was created without any "
			+ "permission configuration and will grant and revoke nothing when it runs. Configure it with a "
			+ "setElement operation carrying accessRights (you can put it in the same operations array).";
	}

	/// <summary>
	/// Of the elements the caller asked to configure, those the read-back shows with NO record filter.
	/// <para>This is the first of the configurations that build green and then do nothing: the element's
	/// record filter decides WHICH records it acts on, and without one the runtime matches nothing, grants
	/// and revokes nothing, and has no output parameter to say so. Nobody refuses it — and the modify surface
	/// makes it easy to reach by accident, because changing the target object clears a filter that pointed at
	/// the old one.</para>
	/// <para>A warning, not a refusal: whether the SERVER should reject this state is open decision D9 in the
	/// package repository, and reporting it here does not pre-empt that.</para>
	/// </summary>
	public static IReadOnlyList<string> WithoutRecordFilter(
			DescribeProcessResult described, IReadOnlyList<string> expected) {
		if (expected.Count == 0 || described?.Elements is null) {
			return Array.Empty<string>();
		}

		List<string> unfiltered = [];
		foreach (string name in expected) {
			DescribedElement? element = described.Elements.FirstOrDefault(e =>
				string.Equals(e?.Name, name, StringComparison.OrdinalIgnoreCase)
				|| string.Equals(e?.Uid, name, StringComparison.OrdinalIgnoreCase));

			// Only an element the read-back actually returned can be judged; an absent one is reported by
			// Unresolved() instead, so it is not accused twice.
			if (element is not null && ReportsAccessRights(element) && !HasRecordFilter(element)) {
				unfiltered.Add(name);
			}
		}

		return unfiltered;
	}

	/// <summary>
	/// The caller-facing warning for an element saved without a record filter. Returns null when there is
	/// nothing to report.
	/// </summary>
	public static string? BuildNoFilterWarning(IReadOnlyList<string> unfiltered) {
		if (unfiltered.Count == 0) {
			return null;
		}

		string elements = string.Join("', '", unfiltered);
		string subject = unfiltered.Count == 1 ? "element" : "elements";
		return $"The 'accessRights' configuration for the {subject} '{elements}' was saved, but the "
			+ $"{(unfiltered.Count == 1 ? "element has" : "elements have")} NO record filter. The filter is what "
			+ "decides WHICH records the element acts on, so at run time it will match no records and change no "
			+ "permissions — silently, because the element has no output parameters. Nothing refuses this state. "
			+ "Add a filter with the setFilter operation (to act on one record, filter Id against a process "
			+ "parameter or a trigger output), and do not report a grant or revoke as applied until you have.";
	}

	// A filter counts as present only if it actually narrows something: the server reports an element with no
	// DataSourceFilters as a null filter, and a filter object carrying neither conditions nor groups selects
	// every record, which is the same run-time outcome as none at all.
	private static bool HasRecordFilter(DescribedElement element) =>
		element.Filter is not null
		&& ((element.Filter.Conditions?.Count ?? 0) > 0 || (element.Filter.Groups?.Count ?? 0) > 0);

	/// <summary>
	/// The caller-facing warning for dropped blocks: what happened, why, and the one action that fixes it.
	/// Returns null when nothing was dropped, so a caller can treat null as "no warning to emit".
	/// </summary>
	public static string? BuildWarning(IReadOnlyList<string> missing) {
		if (missing.Count == 0) {
			return null;
		}

		string elements = string.Join("', '", missing);
		string subject = missing.Count == 1 ? "element" : "elements";
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
			+ "configure the element in the designer.";
	}

	// A server that supports the element reports the block through the extension bag, because DescribedElement
	// declares no typed member for it. A null/absent value counts as not reported.
	// <para>The key comparison is deliberately case-INSENSITIVE. [JsonExtensionData] stores unmatched properties
	// under the server's exact JSON name and the bag's default comparer is ordinal, so the describer's
	// PropertyNameCaseInsensitive option does NOT reach it: an exact-match lookup would be the only
	// casing-sensitive comparison on this path, and a server that ever spelled the property differently would
	// flip this guard into a permanent false alarm telling callers their permissions were discarded when they
	// were not.</para>
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
	private static JsonNode? TryParse(string json) {
		if (string.IsNullOrWhiteSpace(json)) {
			return null;
		}

		try {
			return JsonNode.Parse(json);
		} catch (JsonException) {
			return null;
		}
	}
}
