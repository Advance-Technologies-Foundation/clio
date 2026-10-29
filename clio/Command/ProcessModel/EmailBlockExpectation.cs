using System;
using System.Collections.Generic;
using System.Linq;
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

	/// <summary>An element the replace rule took attachment rows away from, and how many.</summary>
	public sealed record RemovedAttachments(string Element, int Count);

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
	/// earlier one, as it does on the server.
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
			if (op["element"] is JsonObject added) {
				Track(sent, ReadName(added["name"]), added[EmailKey]);
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
	/// reason to stay quiet.
	/// </summary>
	/// <param name="described">The description read back after the successful operation.</param>
	/// <param name="sent">The lists returned by <see cref="AttachmentElements"/> / <see cref="AttachmentElementsFromOperations"/>.</param>
	public static IReadOnlyList<UnlandedAttachments> Unlanded(DescribeProcessResult described,
			IReadOnlyList<SentAttachments> sent) {
		if (sent.Count == 0 || described?.Elements is null) {
			return Array.Empty<UnlandedAttachments>();
		}

		List<UnlandedAttachments> unlanded = [];
		foreach (SentAttachments list in sent) {
			DescribedElement? element = BlockExpectationJson.ResolveElement(described, list.Element);
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
		return $"The operation reported success, but the read-back shows fewer attachments than were sent on the Send "
			+ $"email {subject} ({counts}). The usual cause is a deployed CrtProcessBuilder that does not support email "
			+ "attachments: it discards the 'attachments' list while answering success, so the email goes out WITHOUT "
			+ "those files. Do not report them as attached: install a package that supports email attachments ("
			+ Clio.Common.BundledPackages.ProcessBuilderInstallRemedy + ") and send the list again, or add the "
			+ "attachments in the designer.";
	}

	/// <summary>
	/// Of the elements that sent an attachments list, those the REPLACE rule took attachment rows away from: rows the
	/// element carried BEFORE the edit (by source, as describe decodes it) that it no longer carries after it. A row
	/// whose source the list still names is not counted, even when the server rebuilt it in its own shape. An element
	/// the before-read cannot show (added by this batch, or an unreadable before-read) is not reported.
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
		foreach (SentAttachments list in sent) {
			List<DescribedEmailAttachment>? had =
				BlockExpectationJson.ResolveElement(before, list.Element)?.Email?.Attachments;
			DescribedEmail? after = BlockExpectationJson.ResolveElement(described, list.Element)?.Email;
			if (had is not { Count: > 0 } || after is null) {
				continue;
			}
			List<AttachmentSource> remaining = [.. (after.Attachments ?? []).Select(SourceOf)];
			int count = 0;
			foreach (AttachmentSource source in had.Select(SourceOf)) {
				if (!remaining.Remove(source)) {
					count++;
				}
			}
			if (count > 0) {
				removed.Add(new RemovedAttachments(list.Element, count));
			}
		}

		return removed;
	}

	/// <summary>
	/// The caller-facing line for attachments the replace rule removed: how many, from which element, and why. Returns
	/// null when the edit removed none.
	/// </summary>
	public static string? BuildRemovedAttachmentsWarning(IReadOnlyList<RemovedAttachments> removed) {
		if (removed.Count == 0) {
			return null;
		}

		string counts = string.Join("; ", removed.Select(item =>
			$"{item.Count} {(item.Count == 1 ? "attachment was" : "attachments were")} removed from '{item.Element}'"));
		return $"{counts} by the replace rule: a sent attachments list REPLACES the Send email element's attachments, "
			+ "so every attachment the list did not name is gone. The server's notices name each removed attachment and "
			+ "the entry that restores it; if a removal was not intended, describe the element and send the list again "
			+ "with that entry.";
	}

	private static AttachmentSource SourceOf(DescribedEmailAttachment entry) =>
		new(entry.ProcessParameter, entry.SourceElement, entry.SourceElementParameter, entry.Expression);

	// Tracks the attachments list of one email block, replacing an earlier list for the same element. A block without
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
		sent.RemoveAll(existing => string.Equals(existing.Element, name, StringComparison.OrdinalIgnoreCase));
		sent.Add(new SentAttachments(name, sources));
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
