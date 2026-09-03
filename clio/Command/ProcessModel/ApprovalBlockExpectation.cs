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
	private const string NotifyApproverKey = "notifyApprover";
	private const string NotifyAuthorKey = "notifyAuthor";
	private const string EmailTemplateKey = "emailTemplate";
	private const string RecipientKey = "recipient";
	// A member that exists ONLY on the read shape. Its presence in a REQUEST means a described block was fed
	// back verbatim rather than translated, which is the one caller mistake this check can see before the server does.
	private const string ApproverTypeKey = "approverType";

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
		if (string.IsNullOrWhiteSpace(name)) {
			return;
		}

		expectations.Add(new ApprovalExpectation(
			ElementName: name,
			ExpectsApprover: approval[ApproverKey] is JsonObject,
			ExpectsApproverTemplate: HasTemplate(approval[NotifyApproverKey]),
			ExpectsAuthorTemplate: HasTemplate(approval[NotifyAuthorKey]),
			ExpectsAuthorRecipient: approval[NotifyAuthorKey] is JsonObject author
				&& author[RecipientKey] is JsonObject,
			DescribeShaped: approval[ApproverTypeKey] is not null
				|| approval[NotifyApproverKey] is JsonValue
				|| approval[NotifyAuthorKey] is JsonValue));
	}

	/// <summary>True when a notification block was sent carrying an <c>emailTemplate</c> the server must store.</summary>
	private static bool HasTemplate(JsonNode? notification) =>
		notification is JsonObject block && !string.IsNullOrWhiteSpace(ReadName(block[EmailTemplateKey]));

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
			// Decidable from the REQUEST alone, and reported first because it explains every other symptom that
			// follows from it. A described block fed back verbatim carries approverType / a boolean notifyApprover
			// where the write contract expects approver:{…} / notifyApprover:{…}; the flat members bind to nothing
			// and DataContractJsonSerializer drops them while the server answers success. Without this the request
			// also reads as "carried no approver", so the approver check below would never even run.
			if (expectation.DescribeShaped) {
				missing.Add(new DroppedApproval(expectation.ElementName, ApprovalDropKind.DescribeShapedRequest));
				continue;
			}

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
				missing.Add(new DroppedApproval(expectation.ElementName, ApprovalDropKind.WholeBlock));
				continue;
			}

			// A block that came back WITHOUT the approver it was sent. This is the newer half of the same silent
			// drop: a server that has 'approval' but predates 'approver' returns the block and discards that one
			// member, so a presence-only check sees a healthy element while nobody is assigned to approve it.
			// Checked here rather than left to the versioned RequiresPackage precisely because this class exists
			// for the case where the version signal is unavailable or untrustworthy.
			if (expectation.ExpectsApprover && string.IsNullOrWhiteSpace(element.Approval.ApproverType)) {
				missing.Add(new DroppedApproval(expectation.ElementName, ApprovalDropKind.ApproverOnly));
			}

			// The floor names THREE silent drops, and the read-back this command already fetched can see all three.
			// A notification whose flag came back on with no template stored is the second: the runtime fails inside
			// CreateEmailMessage and IgnoreEmailErrors swallows it, so the element reports the notification as
			// configured and never sends. The third is the same on the author side with no recipient resolved.
			if (expectation.ExpectsApproverTemplate
					&& string.IsNullOrWhiteSpace(element.Approval.ApproverEmailTemplate)) {
				missing.Add(new DroppedApproval(expectation.ElementName, ApprovalDropKind.NotificationTemplate));
			}

			if (expectation.ExpectsAuthorTemplate
					&& string.IsNullOrWhiteSpace(element.Approval.AuthorEmailTemplate)) {
				missing.Add(new DroppedApproval(expectation.ElementName, ApprovalDropKind.NotificationTemplate));
			}

			if (expectation.ExpectsAuthorRecipient && string.IsNullOrWhiteSpace(element.Approval.Recipient)) {
				missing.Add(new DroppedApproval(expectation.ElementName, ApprovalDropKind.AuthorRecipient));
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

		string[] describeShaped = Named(missing, ApprovalDropKind.DescribeShapedRequest);
		string[] wholeBlock = Named(missing, ApprovalDropKind.WholeBlock);
		string[] approverOnly = Named(missing, ApprovalDropKind.ApproverOnly);
		string[] noTemplate = Named(missing, ApprovalDropKind.NotificationTemplate);
		string[] noRecipient = Named(missing, ApprovalDropKind.AuthorRecipient);
		List<string> parts = [];
		// FIRST, because it is the caller's own mistake rather than a stale server, and it explains the rest.
		if (describeShaped.Length > 0) {
			parts.Add("The 'approval' block sent for the "
				+ $"{ElementNoun(describeShaped.Length)} '{string.Join("', '", describeShaped)}' is in the shape "
				+ "describe REPORTS, not the shape create/modify accepts. The read shape is flat "
				+ "('approverType', 'approverEmployee', a boolean 'notifyApprover' with 'approverEmailTemplate' "
				+ "beside it); the write shape is nested ('approver': {type, employee}, 'notifyApprover': "
				+ "{emailTemplate}), and 'recordId' is an object on write and a string on read. The values a "
				+ "describe returns are accepted — the SHAPE has to be translated. Flat members bind to nothing "
				+ "and are dropped without an error while the operation still answers success, so translate the "
				+ "block and re-apply it before reporting this element as configured.");
		}

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

		if (noTemplate.Length > 0) {
			parts.Add("The operation reported success, but a notification switched ON came back with NO email "
				+ $"template stored, for the {ElementNoun(noTemplate.Length)} "
				+ $"'{string.Join("', '", noTemplate)}'. The send then fails inside the runtime and is swallowed "
				+ "because approval elements ignore email errors by default, so the element reports the "
				+ "notification as configured and never sends. Do not report it as configured.");
		}

		if (noRecipient.Length > 0) {
			parts.Add("The operation reported success, but the author notification came back with NO recipient "
				+ $"resolved, for the {ElementNoun(noRecipient.Length)} "
				+ $"'{string.Join("', '", noRecipient)}'. \"Author\" resolves nobody on its own — the runtime "
				+ "reads only the address that was written — so the notification is switched on and silently "
				+ "sends to no one.");
		}

		// Only the version-caused drops have a version fix; a describe-shaped request is fixed by translating it,
		// which its own paragraph already says. Appending the install line to that case alone would send the
		// caller to upgrade a server that is behaving correctly.
		if (missing.Any(m => m.Kind != ApprovalDropKind.DescribeShapedRequest)) {
			parts.Add("Check the package version, install one that supports what you sent "
				+ "(clio install-process-builder) and re-apply the approval block, or configure the element in the "
				+ "designer.");
		}

		return string.Join(" ", parts);
	}

	/// <summary>The element names reported for one drop kind, in the order they were found.</summary>
	private static string[] Named(IReadOnlyList<DroppedApproval> missing, ApprovalDropKind kind) =>
		missing.Where(m => m.Kind == kind).Select(m => m.ElementName).Distinct().ToArray();

	/// <summary>
	/// One element the caller asked to configure, and whether the request carried an <c>approver</c> — the member
	/// a server that has the Approval element but predates the approver drops without saying so.
	/// </summary>
	/// <param name="ElementName">The element's local name or UId, exactly as the caller wrote it.</param>
	/// <param name="ExpectsApprover">True when the sent block carried an <c>approver</c> object.</param>
	/// <param name="ExpectsApproverTemplate">True when <c>notifyApprover</c> carried an <c>emailTemplate</c>.</param>
	/// <param name="ExpectsAuthorTemplate">True when <c>notifyAuthor</c> carried an <c>emailTemplate</c>.</param>
	/// <param name="ExpectsAuthorRecipient">True when <c>notifyAuthor</c> carried a <c>recipient</c> object.</param>
	/// <param name="DescribeShaped">
	/// True when the sent block is in the shape describe REPORTS rather than the one create/modify accepts. Unlike
	/// the others this is decidable without the server, and it is the caller's own mistake rather than a stale
	/// deployment — so it is reported first and does not carry the install-a-newer-package advice.
	/// </param>
	public sealed record ApprovalExpectation(string ElementName, bool ExpectsApprover,
		bool ExpectsApproverTemplate = false, bool ExpectsAuthorTemplate = false,
		bool ExpectsAuthorRecipient = false, bool DescribeShaped = false);

	/// <summary>
	/// What did not survive. Each value is a DIFFERENT cause with a different fix, which is why the warning states
	/// them separately rather than folding them into one "approval not applied".
	/// </summary>
	public enum ApprovalDropKind {
		/// <summary>The read-back carries no <c>approval</c> block at all.</summary>
		WholeBlock,

		/// <summary>The block came back, without the <c>approver</c> that was sent.</summary>
		ApproverOnly,

		/// <summary>A notification came back switched on with no email template stored.</summary>
		NotificationTemplate,

		/// <summary>The author notification came back with no recipient resolved.</summary>
		AuthorRecipient,

		/// <summary>The REQUEST was in the describe shape; nothing was sent that the server could bind.</summary>
		DescribeShapedRequest
	}

	/// <summary>
	/// One element whose approval configuration did not survive the round trip.
	/// </summary>
	/// <param name="ElementName">The element the caller named.</param>
	/// <param name="Kind">Which of the drops this is — see <see cref="ApprovalDropKind"/>.</param>
	public sealed record DroppedApproval(string ElementName, ApprovalDropKind Kind);

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
