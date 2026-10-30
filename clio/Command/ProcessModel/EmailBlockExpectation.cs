using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Clio.Command.ProcessModel;

/// <summary>
/// Post-operation check that an <c>email</c> block the caller SENT was actually APPLIED by the server.
/// <para>The failure this exists to catch is silent. A <c>CrtProcessBuilder</c> that predates the
/// <c>sendEmail</c> element declares no <c>email</c> member on its element descriptor and does not implement
/// <c>IExtensibleDataObject</c>, so its <c>DataContractJsonSerializer</c> DISCARDS the block and the build still
/// answers <c>success:true</c>. The caller gets a process whose email step is unconfigured while every signal it
/// can see says the operation worked.</para>
/// <para>Detection is deliberately BEHAVIOURAL rather than version-based, because it verifies the OUTCOME instead
/// of the advertised capability: a version floor states what an environment should support, while a describe round
/// trip states what it actually did. It also stays correct without coordinating with rebundle timing — the version
/// an environment reports is stamped at rebundle time, so anything keyed to a particular number has to be revisited
/// whenever the bundle moves, and this does not.</para>
/// <para>The checks below are pure so they can be tested without a server; the describe round trip is the
/// caller's job (the commands own the <see cref="IProcessDescriber"/> dependency).</para>
/// </summary>
public static class EmailBlockExpectation {

	// Descriptor/operation JSON keys, named once so the parsing shape reads consistently and to keep the
	// repeated string literals out of the analyzer's duplicate-literal radar.
	private const string ElementsKey = "elements";
	private const string EmailKey = "email";

	/// <summary>
	/// The one phrase every template-landed warning carries. Public so the e2e negative assertion ("the warning
	/// did NOT fire on a healthy server") references the same literal the warning is built from — a reworded
	/// warning then fails to compile the test instead of turning it into a pass-always check.
	/// </summary>
	public const string TemplateWarningMarker = "sent a 'template' that the read-back does NOT show";

	/// <summary>
	/// The one phrase of the warning that fewer attachments landed than were sent (<see cref="BuildAttachmentWarning"/>).
	/// Public for the same reason as <see cref="TemplateWarningMarker"/>: the e2e assertion that it does NOT fire on a
	/// healthy build references the literal the warning is built from.
	/// </summary>
	public const string UnlandedAttachmentsWarningMarker = "the read-back shows fewer attachments than were sent";

	/// <summary>
	/// The one phrase of clio's own replace-rule removal warning (<see cref="BuildRemovedAttachmentsWarning"/>), which
	/// is written only for an element the server's reply reported no removal for (<see cref="NotReportedByServer"/>).
	/// </summary>
	public const string RemovedAttachmentsWarningMarker = "by the replace rule: a sent attachments list REPLACES";

	/// <summary>
	/// The one phrase of clio's own note about what an empty attachments list removed
	/// (<see cref="BuildClearedAttachmentsNote"/>), written under the same condition as the removal warning.
	/// </summary>
	public const string ClearedAttachmentsNoteMarker = "removed every attachment of the Send email";

	// How CrtProcessBuilder's per-slot removal notice reads around the element's name: every attachment notice starts
	// "Send email '<name>': " and the removal one goes on "removed attachment '<slot>'".
	private const string ServerNoticePrefix = "Send email '";
	private const string ServerRemovalNoticeTail = "': removed attachment '";

	/// <summary>
	/// Element names that a build descriptor asks to configure as email elements — every entry under
	/// <c>elements[]</c> carrying a non-null <c>email</c> object. Returns an empty list for a payload with no email
	/// block, which is the common case and skips the verification entirely.
	/// </summary>
	/// <param name="descriptorJson">The build descriptor JSON exactly as the caller supplied it.</param>
	public static IReadOnlyList<string> FromDescriptor(string descriptorJson) {
		JsonObject? descriptor = BlockExpectationJson.Parse(descriptorJson) as JsonObject;
		if (descriptor?[ElementsKey] is not JsonArray elements) {
			return Array.Empty<string>();
		}

		List<string> names = [];
		foreach (JsonNode? element in elements) {
			if (element is not JsonObject candidate || candidate[EmailKey] is not JsonObject) {
				continue;
			}

			string? name = ReadName(candidate["name"]);
			if (!string.IsNullOrWhiteSpace(name)) {
				names.Add(name);
			}
		}

		return names;
	}

	/// <summary>
	/// Element names that a modify operations array asks to configure as email elements. Covers both routes that
	/// carry the block: <c>addElement</c> (under <c>element.email</c>) and <c>setElement</c> (under
	/// <c>elementUpdate.email</c>, where the element name lives on the operation itself).
	/// </summary>
	/// <param name="operationsJson">The operations array JSON exactly as the caller supplied it.</param>
	public static IReadOnlyList<string> FromOperations(string operationsJson) {
		if (BlockExpectationJson.Parse(operationsJson) is not JsonArray operations) {
			return Array.Empty<string>();
		}

		List<string> names = [];
		foreach (JsonNode? operation in operations) {
			if (operation is not JsonObject op) {
				continue;
			}

			// addElement: the descriptor (and therefore the name) is nested under "element".
			if (op["element"] is JsonObject added && added[EmailKey] is JsonObject) {
				string? name = ReadName(added["name"]);
				if (!string.IsNullOrWhiteSpace(name)) {
					names.Add(name);
				}
			}

			// setElement: the name is on the operation, the block is under "elementUpdate".
			if (op["elementUpdate"] is JsonObject update && update[EmailKey] is JsonObject) {
				string? name = ReadName(op["elementName"]);
				if (!string.IsNullOrWhiteSpace(name)) {
					names.Add(name);
				}
			}
		}

		return names;
	}

	/// <summary>
	/// Of the elements the caller asked to configure, those the server does NOT report an <c>email</c> block for —
	/// i.e. the ones whose configuration was discarded. An element ABSENT from the description is deliberately not
	/// reported: the read-back is the only evidence this check has, and an identifier it cannot resolve is a
	/// reason to stay quiet rather than to accuse the server (see the body).
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
			// ResolveFlowElement canonicalizes both), so a caller who passed a UId would otherwise match nothing
			// and be told its email configuration had been discarded when the edit in fact applied cleanly.
			DescribedElement? element = BlockExpectationJson.ResolveElement(described, name);

			// An element that is not in the read-back at all is NOT reported. The read-back is the only evidence
			// this check has, and "I cannot find the element I asked about" is a reason to stay quiet rather than
			// to accuse the server: the identifier may simply be one this comparison cannot resolve.
			if (element is not null && element.Email is null) {
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
		// States the OBSERVATION as fact and the CAUSE as the likely one. All this check saw is that the block is
		// absent from the read-back; "the package predates sendEmail" is the explanation that fits, not something
		// it measured, and asserting it outright would be a diagnosis dressed up as evidence.
		return $"The operation reported success, but the saved process does NOT carry the 'email' configuration for "
			+ $"the {subject} '{elements}' — the read-back shows no email block. The usual cause is a deployed "
			+ "CrtProcessBuilder that predates the sendEmail element: it has no 'email' member and does not "
			+ "implement IExtensibleDataObject, so it discards the block instead of rejecting it and still answers "
			+ "success. Either way the element is UNCONFIGURED (no sender, no recipients, no subject or body), so do "
			+ "not report it as configured. Check the package version, install one that supports sendEmail "
			+ "(" + Clio.Common.BundledPackages.ProcessBuilderInstallRemedy + ") and re-apply the email block, or configure the element in the designer.";
	}

	/// <summary>
	/// Element names whose build descriptor <c>email.body</c> carries at least one friendly process-macro
	/// placeholder (<c>[[param:</c> / <c>[[element:</c>). These bodies must be verified after a build: a
	/// CrtProcessBuilder that predates the body-macro feature stores the placeholder text verbatim and still
	/// answers success, so the literal <c>[[…]]</c> would be emailed to the recipient.
	/// </summary>
	/// <param name="descriptorJson">The build descriptor JSON exactly as the caller supplied it.</param>
	public static IReadOnlyList<string> MacroBodyElements(string descriptorJson) {
		JsonObject? descriptor = BlockExpectationJson.Parse(descriptorJson) as JsonObject;
		if (descriptor?[ElementsKey] is not JsonArray elements) {
			return Array.Empty<string>();
		}

		List<string> names = [];
		foreach (JsonNode? element in elements) {
			if (element is not JsonObject candidate || candidate[EmailKey] is not JsonObject email) {
				continue;
			}

			string? body = email["body"] is JsonValue value && value.TryGetValue(out string? text) ? text : null;
			if (!string.IsNullOrEmpty(body) && ContainsMacro(body)) {
				string? name = ReadName(candidate["name"]);
				if (!string.IsNullOrWhiteSpace(name)) {
					names.Add(name);
				}
			}
		}

		return names;
	}

	/// <summary>
	/// Of the elements whose sent body carried a macro (<see cref="MacroBodyElements"/>), those the read-back shows
	/// were NOT resolved: the element reports a body (<c>HasBody == true</c>) yet its decoded <c>Body</c> came back
	/// null/empty — the signature of a package that stored the HTML verbatim without resolving the macros. A healthy
	/// build decodes the tokens back into a NON-null <c>[[…]]</c> body, so this never fires on a resolved body
	/// (which is why the presence of <c>[[</c> in the read-back is NOT the signal — decode reproduces it). An element
	/// absent from the read-back is not reported, matching <see cref="Missing"/>.
	/// </summary>
	/// <param name="described">The description read back after the successful build.</param>
	/// <param name="macroElements">Element names returned by <see cref="MacroBodyElements"/>.</param>
	public static IReadOnlyList<string> UnresolvedBodyMacros(
			DescribeProcessResult described, IReadOnlyList<string> macroElements) {
		if (macroElements.Count == 0 || described?.Elements is null) {
			return Array.Empty<string>();
		}

		List<string> unresolved = [];
		foreach (string name in macroElements) {
			DescribedElement? element = BlockExpectationJson.ResolveElement(described, name);

			if (element?.Email is { HasBody: true } email && string.IsNullOrWhiteSpace(email.Body)) {
				unresolved.Add(name);
			}
		}

		return unresolved;
	}

	/// <summary>
	/// The caller-facing warning for a body whose macros did not resolve. Returns null when nothing is unresolved,
	/// so a caller can treat null as "no warning to emit".
	/// </summary>
	public static string? BuildMacroWarning(IReadOnlyList<string> unresolved) {
		if (unresolved.Count == 0) {
			return null;
		}

		string elements = string.Join("', '", unresolved);
		string subject = ElementNoun(unresolved.Count);
		return $"The operation reported success, but the Send email body for the {subject} '{elements}' carried "
			+ "process-macro placeholders ([[param:…]] / [[element:…]]) that the read-back shows did NOT resolve "
			+ "(the element reports a body but describe returned none). The usual cause is a deployed CrtProcessBuilder "
			+ "that predates the body-macro feature: it stores the HTML verbatim, so the literal placeholder text "
			+ "would be emailed to the recipient. Install a package that supports body macros "
			+ "(" + Clio.Common.BundledPackages.ProcessBuilderInstallRemedy + ") and re-apply the body, or insert the process data through the "
			+ "designer's Content designer.";
	}

	/// <summary>
	/// Element names whose build descriptor <c>email.template</c> names a template (ENG-95986). A template must be
	/// verified after a build for the same reason a body macro is: a CrtProcessBuilder that predates template mode
	/// declares no <c>template</c> member, so its serializer DISCARDS the field and the build still answers success —
	/// leaving an element that either has NO mode (which the platform runs as template mode with no template, the
	/// <c>Localizable template not found</c> run-time failure) or, when a subject travelled with it, a CUSTOM-message
	/// element with no body. Both are wrong modes reported as configured.
	/// </summary>
	/// <param name="descriptorJson">The build descriptor JSON exactly as the caller supplied it.</param>
	public static IReadOnlyList<string> TemplateElements(string descriptorJson) {
		JsonObject? descriptor = BlockExpectationJson.Parse(descriptorJson) as JsonObject;
		if (descriptor?[ElementsKey] is not JsonArray elements) {
			return Array.Empty<string>();
		}

		List<string> names = [];
		foreach (JsonNode? element in elements) {
			if (element is JsonObject candidate && CarriesTemplate(candidate[EmailKey])) {
				string? name = ReadName(candidate["name"]);
				if (!string.IsNullOrWhiteSpace(name)) {
					names.Add(name);
				}
			}
		}

		return BlockExpectationJson.Distinct(names);
	}

	/// <summary>
	/// Element names whose modify operations name a template — <c>addElement</c> (under <c>element.email</c>) and
	/// <c>setElement</c> (under <c>elementUpdate.email</c>) — for the same post-operation verification.
	/// </summary>
	/// <param name="operationsJson">The operations array JSON exactly as the caller supplied it.</param>
	public static IReadOnlyList<string> TemplateElementsFromOperations(string operationsJson) {
		if (BlockExpectationJson.Parse(operationsJson) is not JsonArray operations) {
			return Array.Empty<string>();
		}

		// Walked IN ORDER, because operations run in order and a later one can legitimately undo the template: a
		// batch that sets a template and then sends a body (or messageSource:"custom") for the SAME element ends,
		// by contract, as a custom-message element with no template — reading "no template" back is then the
		// requested state, not a discarded member, and accusing the server would send the caller to reinstall a
		// package that did exactly what it was told.
		List<string> names = [];
		foreach (JsonNode? operation in operations) {
			if (operation is not JsonObject op) {
				continue;
			}

			string? name = null;
			JsonNode? email = null;
			if (op["element"] is JsonObject added) {
				name = ReadName(added["name"]);
				email = added[EmailKey];
			} else if (op["elementUpdate"] is JsonObject update) {
				name = ReadName(op["elementName"]);
				email = update[EmailKey];
			}

			if (string.IsNullOrWhiteSpace(name) || email is not JsonObject) {
				continue;
			}

			if (CarriesTemplate(email)) {
				names.Add(name);
			} else if (SwitchesToCustom(email)) {
				names.RemoveAll(existing => string.Equals(existing, name, StringComparison.OrdinalIgnoreCase));
			}
		}

		return BlockExpectationJson.Distinct(names);
	}

	/// <summary>
	/// Of the elements that sent a template (<see cref="TemplateElements"/> / <see cref="TemplateElementsFromOperations"/>),
	/// those whose read-back reports NO template — the signature of a server that discarded the member. An element
	/// absent from the read-back, or one reporting no email block at all, is not reported here: <see cref="Missing"/>
	/// already covers the dropped block, and an unresolvable identifier is a reason to stay quiet.
	/// <para>COVERAGE: this sees a discarded <c>template</c> only. A block carrying <c>templateEntity</c> or
	/// <c>messageSource</c> WITHOUT a template (a macro-source rebind, an explicit mode) is discarded just as
	/// silently by a pre-template server and is not detected here — the <c>[RequiresPackage]</c> floor on the
	/// two commands is what gates those, not this read-back.</para>
	/// </summary>
	/// <param name="described">The description read back after the successful operation.</param>
	/// <param name="templateElements">Element names returned by the two template detectors.</param>
	public static IReadOnlyList<string> UnlandedTemplates(DescribeProcessResult described,
			IReadOnlyList<string> templateElements) {
		if (templateElements.Count == 0 || described?.Elements is null) {
			return Array.Empty<string>();
		}

		List<string> unlanded = [];
		foreach (string name in templateElements) {
			DescribedElement? element = BlockExpectationJson.ResolveElement(described, name);
			if (element?.Email is { } email && string.IsNullOrWhiteSpace(email.Template)) {
				unlanded.Add(name);
			}
		}

		return unlanded;
	}

	/// <summary>
	/// The caller-facing warning for a template that did not land. Returns null when every template landed, so a
	/// caller can treat null as "no warning to emit".
	/// </summary>
	public static string? BuildTemplateWarning(IReadOnlyList<string> unlanded) {
		if (unlanded.Count == 0) {
			return null;
		}

		string elements = string.Join("', '", unlanded);
		string subject = ElementNoun(unlanded.Count);
		return $"The operation reported success, but the Send email {subject} '{elements}' {TemplateWarningMarker} "
			+ "(describe reports no template on the element). The usual cause is a deployed "
			+ "CrtProcessBuilder that predates the template message mode (ENG-95986): it has no 'template' member, so it "
			+ "discards the field while answering success. The element is then in the WRONG mode — no mode at all, which "
			+ "the platform runs as template mode with no template and fails with 'Localizable template not found', or a "
			+ "custom message with no body when a subject travelled with the template. Do not report it as configured: "
			+ "install a package that supports template mode (" + Clio.Common.BundledPackages.ProcessBuilderInstallRemedy + ") and re-apply the email block, "
			+ "or pick the template in the designer.";
	}

	/// <summary>
	/// An attachment entry's source in the write shape of <c>email.attachments</c>: the key a sent entry and a
	/// described one are compared on. The describe-only members (slot, bindingLevels, issues) are not part of it.
	/// </summary>
	public sealed record AttachmentSource(string? ProcessParameter, string? SourceElement,
		string? SourceElementParameter, string? Expression);

	/// <summary>
	/// The attachments list one payload sent for one Send email element - the LAST list sent for it, because a modify
	/// list REPLACES the element's attachments and only the final one decides what the element ends with.
	/// </summary>
	/// <param name="Element">The element name (or UId) as the caller wrote it.</param>
	/// <param name="Entries">The sent entries; empty for <c>attachments: []</c>, which removes every attachment.</param>
	public sealed record SentAttachments(string Element, IReadOnlyList<AttachmentSource> Entries);

	/// <summary>An element whose read-back carries fewer attachment rows than its sent list had entries.</summary>
	public sealed record UnlandedAttachments(string Element, int Sent, int Described);

	/// <summary>
	/// An element whose attachment rows were taken away by the edit: the rows it carried BEFORE the edit, as describe
	/// decoded them then, whose source it no longer carries.
	/// </summary>
	/// <param name="Element">The element name (or UId) as the caller wrote it.</param>
	/// <param name="Rows">The removed rows as the before-read described them, each with its slot and source.</param>
	/// <param name="Requested">
	/// True when the caller sent an EMPTY list: removing every row is then exactly what was asked, not a side effect of
	/// the replace rule.
	/// </param>
	public sealed record RemovedAttachments(string Element, IReadOnlyList<DescribedEmailAttachment> Rows, bool Requested) {
		/// <summary>How many rows were removed.</summary>
		public int Count => Rows.Count;
	}

	/// <summary>
	/// The attachments lists a build descriptor sends: every <c>elements[]</c> entry whose <c>email</c> block carries
	/// an <c>attachments</c> array. An element whose block omits the member is not tracked - an absent list leaves the
	/// attachments untouched, so there is nothing to verify.
	/// </summary>
	/// <param name="descriptorJson">The build descriptor JSON exactly as the caller supplied it.</param>
	public static IReadOnlyList<SentAttachments> AttachmentElements(string descriptorJson) {
		if (BlockExpectationJson.Parse(descriptorJson) is not JsonObject descriptor
			|| descriptor[ElementsKey] is not JsonArray elements) {
			return Array.Empty<SentAttachments>();
		}

		List<SentAttachments> sent = [];
		foreach (JsonNode? element in elements) {
			if (element is JsonObject candidate) {
				Track(sent, ReadName(candidate["name"]), candidate[EmailKey]);
			}
		}

		return sent;
	}

	/// <summary>
	/// The attachments lists a modify operations array sends through <c>addElement</c> (<c>element.email</c>) and
	/// <c>setElement</c> (<c>elementUpdate.email</c>), walked IN ORDER: a later list for the same element replaces the
	/// earlier one, as it does on the server. "The same element" here is the same SPELLING: a batch that names one
	/// element by its name and then by its UId yields two lists, because only a read-back can tell they are one
	/// element; <see cref="Unlanded"/> and <see cref="Removed"/> collapse them on the element the read-back resolves,
	/// keeping the list sent last.
	/// <para>A <c>removeElement</c> forgets the list sent for that element, and so does an <c>addElement</c> of that
	/// name: the element the batch ends with is a NEW one, and it carries only the list its own addElement sends (none
	/// when the addElement sends no list). Keeping the earlier list would compare a removed element's attachments with
	/// the new one's and report the difference as a loss.</para>
	/// </summary>
	/// <param name="operationsJson">The operations array JSON exactly as the caller supplied it.</param>
	public static IReadOnlyList<SentAttachments> AttachmentElementsFromOperations(string operationsJson) {
		if (BlockExpectationJson.Parse(operationsJson) is not JsonArray operations) {
			return Array.Empty<SentAttachments>();
		}

		List<SentAttachments> sent = [];
		foreach (JsonNode? operation in operations) {
			if (operation is not JsonObject op) {
				continue;
			}
			if (string.Equals(ReadName(op["op"])?.Trim(), "removeElement", StringComparison.OrdinalIgnoreCase)) {
				Forget(sent, ReadName(op["elementName"]));
			} else if (op["element"] is JsonObject added) {
				string? name = ReadName(added["name"]);
				Forget(sent, name);
				Track(sent, name, added[EmailKey]);
			} else if (op["elementUpdate"] is JsonObject update) {
				Track(sent, ReadName(op["elementName"]), update[EmailKey]);
			}
		}

		return sent;
	}

	/// <summary>
	/// Of the elements that sent an attachments list, those whose read-back reports FEWER attachment rows than the
	/// list had entries - the signature of a server that discarded the member while answering success (no row at all)
	/// or of a list that did not land in full. An element absent from the read-back, or one reporting no email block,
	/// is not reported here: <see cref="Missing"/> covers the dropped block, and an unresolvable identifier is a
	/// reason to stay quiet. Two lists that name one element two ways count once, as the list sent last.
	/// </summary>
	/// <param name="described">The description read back after the successful operation.</param>
	/// <param name="sent">The lists returned by <see cref="AttachmentElements"/> / <see cref="AttachmentElementsFromOperations"/>.</param>
	public static IReadOnlyList<UnlandedAttachments> Unlanded(DescribeProcessResult described,
			IReadOnlyList<SentAttachments> sent) {
		if (sent.Count == 0 || described?.Elements is null) {
			return Array.Empty<UnlandedAttachments>();
		}

		List<UnlandedAttachments> unlanded = [];
		foreach ((SentAttachments list, DescribedElement? element) in LastPerElement(described, sent)) {
			if (element?.Email is not { } email) {
				continue;
			}
			int landed = email.Attachments?.Count ?? 0;
			if (landed < list.Entries.Count) {
				unlanded.Add(new UnlandedAttachments(list.Element, list.Entries.Count, landed));
			}
		}

		return unlanded;
	}

	/// <summary>
	/// The caller-facing warning for attachments that did not land. Qualitative on purpose: it names the likely
	/// cause as a capability gap, never a package version (a version in such a message is advice the reader has
	/// already satisfied). Returns null when every list landed.
	/// </summary>
	public static string? BuildAttachmentWarning(IReadOnlyList<UnlandedAttachments> unlanded) {
		if (unlanded.Count == 0) {
			return null;
		}

		string counts = string.Join("; ", unlanded.Select(item =>
			$"'{item.Element}' was sent {item.Sent} and reports {item.Described}"));
		string subject = ElementNoun(unlanded.Count);
		return $"The operation reported success, but {UnlandedAttachmentsWarningMarker} on the Send "
			+ $"email {subject} ({counts}). The usual cause is a deployed CrtProcessBuilder that does not support email "
			+ "attachments: it discards the 'attachments' list while answering success, so the email goes out WITHOUT "
			+ "those files. Do not report them as attached: install a package that supports email attachments ("
			+ Clio.Common.BundledPackages.ProcessBuilderInstallRemedy + ") and send the list again, or add the "
			+ "attachments in the designer.";
	}

	/// <summary>
	/// Of the elements that sent an attachments list, those the edit took attachment rows away from: rows the element
	/// carried BEFORE the edit (by source, as describe decodes it) that it no longer carries after it. A row whose
	/// source the list still names is not counted, even when the server rebuilt it in its own shape. A row whose slot
	/// and source both survive is matched first, so with two rows of one source the removed slot is the one named. An element the
	/// before-read cannot show (added by this batch, or an unreadable before-read) is not reported. Two lists that
	/// name one element two ways count once, as the list sent last.
	/// </summary>
	/// <param name="before">The description read before the write, or null when that read failed.</param>
	/// <param name="described">The description read back after the successful write.</param>
	/// <param name="sent">The lists returned by <see cref="AttachmentElementsFromOperations"/>.</param>
	public static IReadOnlyList<RemovedAttachments> Removed(DescribeProcessResult? before,
			DescribeProcessResult described, IReadOnlyList<SentAttachments> sent) {
		if (sent.Count == 0 || before?.Elements is null || described?.Elements is null) {
			return Array.Empty<RemovedAttachments>();
		}

		List<RemovedAttachments> removed = [];
		foreach ((SentAttachments list, DescribedElement? element) in LastPerElement(described, sent)) {
			List<DescribedEmailAttachment>? had =
				BlockExpectationJson.ResolveElement(before, list.Element)?.Email?.Attachments;
			DescribedEmail? after = element?.Email;
			if (had is not { Count: > 0 } || after is null) {
				continue;
			}
			List<DescribedEmailAttachment> gone = Gone(had, after.Attachments ?? []);
			if (gone.Count > 0) {
				removed.Add(new RemovedAttachments(list.Element, gone, Requested: list.Entries.Count == 0));
			}
		}

		return removed;
	}

	// The before-rows the after-rows do not account for. A row whose slot AND source both survive is cancelled first;
	// only the rest is matched by source alone. Describe lists rows in stored order, not slot order, so with two rows
	// of one source a single by-source pass walking that order can cancel the wrong one - before [Attachments2 Doc,
	// Attachments1 Doc], after [Attachments1 Doc] would name Attachments1 as removed, while the package kept the
	// lowest-numbered slot and removed Attachments2. The by-source pass still keeps a row the server rebuilt under a
	// new slot for a source the list names.
	private static List<DescribedEmailAttachment> Gone(IEnumerable<DescribedEmailAttachment> had,
			IEnumerable<DescribedEmailAttachment> kept) {
		List<DescribedEmailAttachment> remaining = [.. kept];
		List<DescribedEmailAttachment> unmatched = [];
		foreach (DescribedEmailAttachment row in had) {
			int index = remaining.FindIndex(survivor => SameSlot(survivor, row) && SourceOf(survivor) == SourceOf(row));
			if (index >= 0) {
				remaining.RemoveAt(index);
			} else {
				unmatched.Add(row);
			}
		}
		List<AttachmentSource> sources = [.. remaining.Select(SourceOf)];
		return [.. unmatched.Where(row => !sources.Remove(SourceOf(row)))];
	}

	private static bool SameSlot(DescribedEmailAttachment left, DescribedEmailAttachment right) =>
		!string.IsNullOrWhiteSpace(left.Slot)
		&& string.Equals(left.Slot.Trim(), right.Slot?.Trim(), StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// The caller-facing warning for attachments the REPLACE rule removed - a non-empty list that did not name them:
	/// how many, from which element, and each removed row's slot and source in the write shape. After the edit
	/// describe no longer shows a removed row, so the source printed here (or the describe taken before the edit) is
	/// what restores it. Returns null when the replace rule removed none; a removal an empty list asked for is
	/// reported by <see cref="BuildClearedAttachmentsNote"/> instead.
	/// </summary>
	public static string? BuildRemovedAttachmentsWarning(IReadOnlyList<RemovedAttachments> removed) {
		List<RemovedAttachments> replaced = [.. removed.Where(item => !item.Requested)];
		if (replaced.Count == 0) {
			return null;
		}

		string counts = string.Join("; ", replaced.Select(item =>
			$"{item.Count} {(item.Count == 1 ? "attachment was" : "attachments were")} removed from '{item.Element}' "
			+ $"({RowsInWriteShape(item.Rows)})"));
		return $"{counts} {RemovedAttachmentsWarningMarker} the Send email element's attachments, "
			+ "so every attachment the list did not name is gone, and describe no longer shows it. If a removal was not "
			+ "intended, send the list again with the removed entry added as printed here; a row printed without a "
			+ "source is one describe could not name, before this edit as well as after it, so rebuild it in the "
			+ "designer.";
	}

	/// <summary>
	/// The caller-facing note for an EMPTY attachments list: it removed every row, which is what it asks for, so this
	/// is informative rather than a warning - it says what went, so a list emptied by mistake can still be restored.
	/// Returns null when no empty list removed anything.
	/// </summary>
	public static string? BuildClearedAttachmentsNote(IReadOnlyList<RemovedAttachments> removed) {
		List<RemovedAttachments> cleared = [.. removed.Where(item => item.Requested)];
		if (cleared.Count == 0) {
			return null;
		}

		string counts = string.Join("; ", cleared.Select(item =>
			$"'{item.Element}' ({item.Count}: {RowsInWriteShape(item.Rows)})"));
		string lists = cleared.Count == 1 ? "The empty attachments list" : "The empty attachments lists";
		return $"{lists} {ClearedAttachmentsNoteMarker} {ElementNoun(cleared.Count)} {counts}, as asked.";
	}

	/// <summary>
	/// Of the removals <see cref="Removed"/> found, those the server's own reply did not report. CrtProcessBuilder
	/// returns a removal notice per slot a sent list removed (<c>Send email 'SE1': removed attachment 'Attachments2'
	/// ...</c>), with the entry that restores it; for an element it reported, clio's own line would say the same thing
	/// twice. Matched on the element's name as the read-back reports it (the notice names it so) and as the caller
	/// spelled it. A notice clio cannot match leaves clio's line in, which repeats the information rather than losing it.
	/// </summary>
	/// <param name="removed">What <see cref="Removed"/> returned.</param>
	/// <param name="described">The description read back after the write, which resolves a UId to the element's name.</param>
	/// <param name="serverWarnings">The warnings the write returned; <c>null</c> when it returned none.</param>
	public static IReadOnlyList<RemovedAttachments> NotReportedByServer(IReadOnlyList<RemovedAttachments> removed,
			DescribeProcessResult described, IReadOnlyList<string>? serverWarnings) {
		if (removed.Count == 0 || serverWarnings is not { Count: > 0 }) {
			return removed;
		}

		return [.. removed.Where(item => !ServerReportedRemoval(serverWarnings,
			[item.Element, BlockExpectationJson.ResolveElement(described, item.Element)?.Name]))];
	}

	private static bool ServerReportedRemoval(IReadOnlyList<string> serverWarnings, IEnumerable<string?> names) =>
		names.Where(name => !string.IsNullOrWhiteSpace(name))
			.Select(name => ServerNoticePrefix + name!.Trim() + ServerRemovalNoticeTail)
			.Any(notice => serverWarnings.Any(warning =>
				warning is not null && warning.StartsWith(notice, StringComparison.OrdinalIgnoreCase)));

	// The entries are printed with the MCP result encoder rather than the default one: the default escapes for HTML,
	// so an expression's ', <, > and + would read as \u0027, \u003C, \u003E and \u002B in the very line the caller
	// copies back. The relaxed form still escapes what JSON requires (and the invisible Format characters), so the
	// printed entry parses back to the same value.
	private static readonly JsonSerializerOptions WriteShapeJson = new() {
		Encoder = Clio.Command.McpServer.McpResultJsonEncoder.Instance
	};

	// Each removed row as "<slot> <entry in the write shape>", the entry that restores it; a row describe could not
	// name a source for says so instead of printing an empty entry.
	private static string RowsInWriteShape(IEnumerable<DescribedEmailAttachment> rows) =>
		string.Join(", ", rows.Select(row => {
			JsonObject entry = new();
			AddMember(entry, "processParameter", row.ProcessParameter);
			AddMember(entry, "sourceElement", row.SourceElement);
			AddMember(entry, "sourceElementParameter", row.SourceElementParameter);
			AddMember(entry, "expression", row.Expression);
			string slot = string.IsNullOrWhiteSpace(row.Slot) ? "a row" : row.Slot;
			return entry.Count == 0 ? $"{slot} with no source describe could name" : $"{slot} {entry.ToJsonString(WriteShapeJson)}";
		}));

	private static void AddMember(JsonObject entry, string key, string? value) {
		if (!string.IsNullOrWhiteSpace(value)) {
			entry[key] = value;
		}
	}

	// The lists to judge, one per element AS THE READ-BACK RESOLVES IT. Track can only collapse lists by the spelling
	// the caller used, so a batch that names one element by its name and by its UId arrives as two; the later of the
	// two is the one the element ends with, since the server applies the operations in order. A list whose element the
	// read-back cannot resolve is kept as it is: every check is quiet about it anyway.
	private static List<(SentAttachments List, DescribedElement? Element)> LastPerElement(
			DescribeProcessResult described, IReadOnlyList<SentAttachments> sent) {
		List<(SentAttachments List, DescribedElement? Element)> lists = [];
		foreach (SentAttachments list in sent) {
			DescribedElement? element = BlockExpectationJson.ResolveElement(described, list.Element);
			if (element is not null) {
				lists.RemoveAll(existing => ReferenceEquals(existing.Element, element));
			}
			lists.Add((list, element));
		}
		return lists;
	}

	private static AttachmentSource SourceOf(DescribedEmailAttachment entry) =>
		new(entry.ProcessParameter, entry.SourceElement, entry.SourceElementParameter, entry.Expression);

	// Tracks the attachments list of one email block, replacing an earlier list for the same element as SPELLED (one
	// element named two ways is collapsed by LastPerElement, on the read-back). A block without
	// the member is skipped: an absent list leaves the element's attachments as they are.
	private static void Track(List<SentAttachments> sent, string? name, JsonNode? email) {
		if (string.IsNullOrWhiteSpace(name) || email is not JsonObject block
			|| block["attachments"] is not JsonArray entries) {
			return;
		}
		List<AttachmentSource> sources = [];
		foreach (JsonNode? entry in entries) {
			if (entry is JsonObject item) {
				sources.Add(new AttachmentSource(ReadName(item["processParameter"]), ReadName(item["sourceElement"]),
					ReadName(item["sourceElementParameter"]), ReadName(item["expression"])));
			}
		}
		Forget(sent, name);
		sent.Add(new SentAttachments(name, sources));
	}

	// Drops the list tracked for an element as SPELLED; a name that is blank forgets nothing.
	private static void Forget(List<SentAttachments> sent, string? name) {
		if (!string.IsNullOrWhiteSpace(name)) {
			sent.RemoveAll(existing => string.Equals(existing.Element, name, StringComparison.OrdinalIgnoreCase));
		}
	}

	// True when an email block moves the element to (or keeps it in) the custom message: a NON-EMPTY body, or an
	// explicit messageSource:"custom". The server clears the template on that switch, so a template sent EARLIER
	// in the same batch for this element is expected to be gone from the read-back.
	//
	// Deliberately narrower than "the block mentions a body". An EMPTY or whitespace body is refused at build
	// ("requires a non-empty 'email.body'"), and a bodyFormat with no body is refused as well ("the 'email' block
	// specifies no field to apply") - neither can leave a custom-message element behind, so neither is a reason to
	// stop verifying that the template landed. Counting them would drop the element from the check on a batch the
	// server in fact refuses outright, which is the one case where the check still has something to say.
	private static bool SwitchesToCustom(JsonNode? email) {
		if (email is not JsonObject block) {
			return false;
		}
		if (block["body"] is JsonValue body
				&& body.TryGetValue(out string? bodyText)
				&& !string.IsNullOrWhiteSpace(bodyText)) {
			return true;
		}
		// No null-conditional on `token`: TryGetValue is [NotNullWhen(true)], so a true result already guarantees it.
		return block["messageSource"] is JsonValue mode
			&& mode.TryGetValue(out string? token)
			&& string.Equals(token.Trim(), "custom", StringComparison.OrdinalIgnoreCase);
	}

	// True when an email block names a template — the field that a pre-template server discards.
	private static bool CarriesTemplate(JsonNode? email) =>
		email is JsonObject block
		&& block["template"] is JsonValue value
		&& value.TryGetValue(out string? template)
		&& !string.IsNullOrWhiteSpace(template);

	// Singular/plural of "element" for a count, kept in one place so the noun is not a duplicated string literal.
	private static string ElementNoun(int count) => BlockExpectationJson.ElementNoun(count);

	/// <summary>
	/// Reads an element name, tolerating a node that is not a string. Delegates to
	/// <see cref="BlockExpectationJson.ReadText"/>, which owns the reasoning: this check runs AFTER a
	/// successful operation, inside the command's try, so a throw on <c>"name": 123</c> would report a build
	/// the server accepted as a failure - and take the sibling guards in the same block down with it.
	/// </summary>
	private static string? ReadName(JsonNode? node) => BlockExpectationJson.ReadText(node);

	private static bool ContainsMacro(string body) =>
		body.IndexOf("[[param:", StringComparison.OrdinalIgnoreCase) >= 0
		|| body.IndexOf("[[element:", StringComparison.OrdinalIgnoreCase) >= 0;

}
