using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Clio.Command.ProcessModel;

/// <summary>
/// Post-operation check that an <c>approval</c> block the caller SENT was actually APPLIED by the server.
/// <para>The failure this exists to catch is silent, and it is the same one
/// <see cref="EmailBlockExpectation"/> catches for <c>email</c>. A <c>CrtProcessBuilder</c> that predates the
/// Approval element declares no <c>approval</c> member on its element descriptor and does not implement
/// <c>IExtensibleDataObject</c>, so its <c>DataContractJsonSerializer</c> DISCARDS the block and the operation
/// still answers <c>success:true</c>. The caller is left with an Approval element that has no object, no record
/// and no notifications, while every signal it can see says the operation worked.</para>
/// <para>Detection is BEHAVIOURAL rather than version-based for the reason recorded on the email check: it
/// verifies the outcome instead of the advertised capability, and stays correct without being revisited every
/// time the bundled package version moves.</para>
/// <para>The checks are pure so they can be tested without a server; the describe round trip is the caller's job
/// (the commands own the <see cref="IProcessDescriber"/> dependency).</para>
/// </summary>
public static class ApprovalBlockExpectation {

	// Descriptor/operation JSON keys, named once so the parsing shape reads consistently and to keep the
	// repeated string literals out of the analyzer's duplicate-literal radar.
	private const string ElementsKey = "elements";
	private const string ApprovalKey = "approval";
	private const string NameKey = "name";
	private const string ElementKey = "element";
	private const string ElementUpdateKey = "elementUpdate";
	private const string ElementNameKey = "elementName";

	/// <summary>
	/// Element names that a build descriptor asks to configure as Approval elements — every entry under
	/// <c>elements[]</c> carrying a non-null <c>approval</c> object. Returns an empty list for a payload with no
	/// approval block, which is the common case and skips the verification entirely.
	/// </summary>
	/// <param name="descriptorJson">The build descriptor JSON exactly as the caller supplied it.</param>
	public static IReadOnlyList<string> FromDescriptor(string descriptorJson) {
		JsonObject? descriptor = TryParse(descriptorJson) as JsonObject;
		if (descriptor?[ElementsKey] is not JsonArray elements) {
			return Array.Empty<string>();
		}

		List<string> names = [];
		foreach (JsonNode? element in elements) {
			if (element is not JsonObject candidate || candidate[ApprovalKey] is not JsonObject) {
				continue;
			}

			string? name = candidate[NameKey]?.GetValue<string>();
			if (!string.IsNullOrWhiteSpace(name)) {
				names.Add(name);
			}
		}

		return names;
	}

	/// <summary>
	/// Element names that a modify operations array asks to configure as Approval elements. Covers both routes
	/// that carry the block: <c>addElement</c> (under <c>element.approval</c>) and <c>setElement</c> (under
	/// <c>elementUpdate.approval</c>, where the element name lives on the operation itself).
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

			// addElement: the descriptor (and therefore the name) is nested under "element".
			if (op[ElementKey] is JsonObject added && added[ApprovalKey] is JsonObject) {
				string? name = added[NameKey]?.GetValue<string>();
				if (!string.IsNullOrWhiteSpace(name)) {
					names.Add(name);
				}
			}

			// setElement: the name is on the operation, the block is under "elementUpdate".
			if (op[ElementUpdateKey] is JsonObject update && update[ApprovalKey] is JsonObject) {
				string? name = op[ElementNameKey]?.GetValue<string>();
				if (!string.IsNullOrWhiteSpace(name)) {
					names.Add(name);
				}
			}
		}

		return names;
	}

	/// <summary>
	/// Of the elements the caller asked to configure, those the server does NOT report an <c>approval</c> block
	/// for — i.e. the ones whose configuration was discarded.
	/// </summary>
	/// <param name="described">The description read back after the successful operation.</param>
	/// <param name="expected">Element names returned by <see cref="FromDescriptor"/> / <see cref="FromOperations"/>.</param>
	public static IReadOnlyList<string> Missing(DescribeProcessResult described, IReadOnlyList<string> expected) {
		if (expected.Count == 0) {
			return Array.Empty<string>();
		}

		if (described?.Elements is null) {
			// Nothing to compare against: report nothing rather than accuse the server on missing evidence.
			return Array.Empty<string>();
		}

		List<string> missing = [];
		foreach (string name in expected) {
			// Matched on NAME OR UID on purpose: setElement identifies an element by either (the server's
			// ResolveFlowElement canonicalizes both), so a caller who passed a UId would otherwise match nothing and
			// be told its approval configuration had been discarded when the edit in fact applied cleanly.
			DescribedElement? element = described.Elements.FirstOrDefault(e =>
				string.Equals(e?.Name, name, StringComparison.OrdinalIgnoreCase)
				|| string.Equals(e?.Uid, name, StringComparison.OrdinalIgnoreCase));

			// An element absent from the read-back is NOT reported: the read-back is the only evidence this check
			// has, and "I cannot find the element I asked about" is a reason to stay quiet rather than to accuse.
			if (element is not null && element.Approval is null) {
				missing.Add(name);
			}
		}

		return missing;
	}

	/// <summary>
	/// The caller-facing warning for dropped blocks: what happened, why, and the one action that fixes it. Returns
	/// null when nothing was dropped, so a caller can treat null as "no warning to emit".
	/// </summary>
	public static string? BuildWarning(IReadOnlyList<string> missing) {
		if (missing.Count == 0) {
			return null;
		}

		string elements = string.Join("', '", missing);
		string subject = ElementNoun(missing.Count);
		// States the OBSERVATION as fact and the CAUSE as the likely one — all this check saw is that the block is
		// absent from the read-back.
		return $"The operation reported success, but the saved process does NOT carry the 'approval' configuration "
			+ $"for the {subject} '{elements}' — the read-back shows no approval block. The usual cause is a deployed "
			+ "CrtProcessBuilder that predates the Approval element: it has no 'approval' member and does not "
			+ "implement IExtensibleDataObject, so it discards the block instead of rejecting it and still answers "
			+ "success. Either way the element is UNCONFIGURED (no approval object, no record under approval, no "
			+ "notifications), so do not report it as configured. Check the package version, install one that "
			+ "supports the Approval element (clio install-process-builder) and re-apply the approval block, or "
			+ "configure the element in the designer.";
	}

	/// <summary>Singular/plural noun for the warning, so one dropped element does not read as "elements".</summary>
	private static string ElementNoun(int count) => count == 1 ? "element" : "elements";

	/// <summary>
	/// Parses caller-supplied JSON, returning null on anything malformed. A payload this cannot parse is not this
	/// check's problem: the operation itself would have failed on it, and guessing would only produce noise.
	/// </summary>
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
