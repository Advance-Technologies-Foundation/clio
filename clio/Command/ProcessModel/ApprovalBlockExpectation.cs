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
	private const string ApproverKey = "approver";

	/// <summary>
	/// Element names that a build descriptor asks to configure as Approval elements — every entry under
	/// <c>elements[]</c> carrying a non-null <c>approval</c> object. Returns an empty list for a payload with no
	/// approval block, which is the common case and skips the verification entirely.
	/// </summary>
	/// <param name="descriptorJson">The build descriptor JSON exactly as the caller supplied it.</param>
	public static IReadOnlyList<ApprovalExpectation> FromDescriptor(string descriptorJson) {
		JsonObject? descriptor = TryParse(descriptorJson) as JsonObject;
		if (descriptor?[ElementsKey] is not JsonArray elements) {
			return Array.Empty<ApprovalExpectation>();
		}

		List<ApprovalExpectation> expectations = [];
		foreach (JsonNode? element in elements) {
			JsonObject? candidate = element as JsonObject;
			AddIfConfigured(expectations, candidate?[ApprovalKey], candidate?[NameKey]);
		}

		return expectations;
	}

	/// <summary>
	/// Element names that a modify operations array asks to configure as Approval elements. Covers both routes
	/// that carry the block: <c>addElement</c> (under <c>element.approval</c>) and <c>setElement</c> (under
	/// <c>elementUpdate.approval</c>, where the element name lives on the operation itself).
	/// </summary>
	/// <param name="operationsJson">The operations array JSON exactly as the caller supplied it.</param>
	public static IReadOnlyList<ApprovalExpectation> FromOperations(string operationsJson) {
		if (TryParse(operationsJson) is not JsonArray operations) {
			return Array.Empty<ApprovalExpectation>();
		}

		List<ApprovalExpectation> expectations = [];
		foreach (JsonNode? operation in operations) {
			JsonObject? op = operation as JsonObject;

			// addElement: the descriptor (and therefore the name) is nested under "element".
			JsonObject? added = op?[ElementKey] as JsonObject;
			AddIfConfigured(expectations, added?[ApprovalKey], added?[NameKey]);

			// setElement: the name is on the operation, the block is under "elementUpdate".
			JsonObject? update = op?[ElementUpdateKey] as JsonObject;
			AddIfConfigured(expectations, update?[ApprovalKey], op?[ElementNameKey]);
		}

		return expectations;
	}

	/// <summary>
	/// Records one expectation when BOTH halves are present: an <c>approval</c> object, and a name usable to find
	/// the element in the read-back. Every route that can carry the block — a build descriptor entry,
	/// <c>addElement</c>, <c>setElement</c> — differs only in WHERE those two nodes sit, so the rule itself lives
	/// here once. A missing half is skipped rather than recorded: an expectation with no name could never be
	/// matched against the described process, so it could only ever produce a false accusation.
	/// </summary>
	private static void AddIfConfigured(List<ApprovalExpectation> expectations, JsonNode? approvalNode,
			JsonNode? nameNode) {
		if (approvalNode is not JsonObject approval) {
			return;
		}

		string? name = ReadName(nameNode);
		if (!string.IsNullOrWhiteSpace(name)) {
			expectations.Add(new ApprovalExpectation(name, approval[ApproverKey] is JsonObject));
		}
	}

	/// <summary>
	/// Of the elements the caller asked to configure, those whose configuration did NOT survive: the server
	/// reports no <c>approval</c> block at all, or it reports one without the <c>approver</c> that was sent.
	/// </summary>
	/// <param name="described">The description read back after the successful operation.</param>
	/// <param name="expected">Expectations returned by <see cref="FromDescriptor"/> / <see cref="FromOperations"/>.</param>
	public static IReadOnlyList<DroppedApproval> Missing(DescribeProcessResult described,
			IReadOnlyList<ApprovalExpectation> expected) {
		if (expected.Count == 0) {
			return Array.Empty<DroppedApproval>();
		}

		if (described?.Elements is null) {
			// Nothing to compare against: report nothing rather than accuse the server on missing evidence.
			return Array.Empty<DroppedApproval>();
		}

		List<DroppedApproval> missing = [];
		foreach (ApprovalExpectation expectation in expected) {
			// Matched on NAME OR UID on purpose: setElement identifies an element by either (the server's
			// ResolveFlowElement canonicalizes both), so a caller who passed a UId would otherwise match nothing and
			// be told its approval configuration had been discarded when the edit in fact applied cleanly.
			DescribedElement? element = described.Elements.FirstOrDefault(e =>
				string.Equals(e?.Name, expectation.ElementName, StringComparison.OrdinalIgnoreCase)
				|| string.Equals(e?.Uid, expectation.ElementName, StringComparison.OrdinalIgnoreCase));

			// An element absent from the read-back is NOT reported: the read-back is the only evidence this check
			// has, and "I cannot find the element I asked about" is a reason to stay quiet rather than to accuse.
			if (element is null) {
				continue;
			}

			if (element.Approval is null) {
				missing.Add(new DroppedApproval(expectation.ElementName, BlockPresent: false));
				continue;
			}

			// A block that came back WITHOUT the approver it was sent. This is the newer half of the same silent
			// drop: a server that has 'approval' but predates 'approver' returns the block and discards that one
			// member, so a presence-only check sees a healthy element while nobody is assigned to approve it.
			// Checked here rather than left to the versioned RequiresPackage precisely because this class exists
			// for the case where the version signal is unavailable or untrustworthy.
			if (expectation.ExpectsApprover && string.IsNullOrWhiteSpace(element.Approval.ApproverType)) {
				missing.Add(new DroppedApproval(expectation.ElementName, BlockPresent: true));
			}
		}

		return missing;
	}

	/// <summary>
	/// The caller-facing warning for dropped blocks: what happened, why, and the one action that fixes it. Returns
	/// null when nothing was dropped, so a caller can treat null as "no warning to emit".
	/// </summary>
	public static string? BuildWarning(IReadOnlyList<DroppedApproval> missing) {
		if (missing.Count == 0) {
			return null;
		}

		string[] wholeBlock = missing.Where(m => !m.BlockPresent).Select(m => m.ElementName).ToArray();
		string[] approverOnly = missing.Where(m => m.BlockPresent).Select(m => m.ElementName).ToArray();
		List<string> parts = [];
		// States the OBSERVATION as fact and the CAUSE as the likely one — all this check saw is what the
		// read-back does and does not carry.
		if (wholeBlock.Length > 0) {
			parts.Add("The operation reported success, but the saved process does NOT carry the 'approval' "
				+ $"configuration for the {ElementNoun(wholeBlock.Length)} '{string.Join("', '", wholeBlock)}' — the "
				+ "read-back shows no approval block. The usual cause is a deployed CrtProcessBuilder that predates "
				+ "the Approval element: it has no 'approval' member and does not implement IExtensibleDataObject, "
				+ "so it discards the block instead of rejecting it and still answers success. Either way the "
				+ "element is UNCONFIGURED (no approval object, no record under approval, no notifications), so do "
				+ "not report it as configured.");
		}

		if (approverOnly.Length > 0) {
			parts.Add("The operation reported success and the approval block came back, but WITHOUT the approver "
				+ $"that was sent, for the {ElementNoun(approverOnly.Length)} "
				+ $"'{string.Join("', '", approverOnly)}'. The usual cause is a deployed CrtProcessBuilder that has "
				+ "the Approval element but predates its 'approver' member, which it discards the same silent way. "
				+ "The element therefore has NOBODY assigned to approve it: it saves and runs, and the approval it "
				+ "raises cannot be acted on, so do not report it as configured.");
		}

		parts.Add("Check the package version, install one that supports what you sent "
			+ "(clio install-process-builder) and re-apply the approval block, or configure the element in the "
			+ "designer.");
		return string.Join(" ", parts);
	}

	/// <summary>
	/// One element the caller asked to configure, and whether the request carried an <c>approver</c> — the member
	/// a server that has the Approval element but predates the approver drops without saying so.
	/// </summary>
	/// <param name="ElementName">The element's local name or UId, exactly as the caller wrote it.</param>
	/// <param name="ExpectsApprover">True when the sent block carried an <c>approver</c> object.</param>
	public sealed record ApprovalExpectation(string ElementName, bool ExpectsApprover);

	/// <summary>
	/// One element whose approval configuration did not survive the round trip.
	/// </summary>
	/// <param name="ElementName">The element the caller named.</param>
	/// <param name="BlockPresent">
	/// False when the whole <c>approval</c> block is absent from the read-back; true when the block came back but
	/// the <c>approver</c> that was sent did not. The two have different causes and different fixes, so the
	/// warning states them separately.
	/// </param>
	public sealed record DroppedApproval(string ElementName, bool BlockPresent);

	/// <summary>Singular/plural noun for the warning, so one dropped element does not read as "elements".</summary>
	private static string ElementNoun(int count) => count == 1 ? "element" : "elements";

	/// <summary>
	/// Reads an element name, tolerating a node that is not a string.
	/// <para><c>GetValue&lt;string&gt;()</c> THROWS on <c>"name": 123</c>, and this check runs AFTER a successful
	/// operation, inside the command's try — so a payload the server happily accepted would be reported to the
	/// caller as a failed build. The check exists to warn about a dropped block; it must never be the thing that
	/// fails. Same idiom <see cref="EmailBlockExpectation"/> uses for the email body.</para>
	/// </summary>
	private static string? ReadName(JsonNode? node) =>
		node is JsonValue value && value.TryGetValue(out string? text) ? text : null;

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
