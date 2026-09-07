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
	/// Element names that a build descriptor asks to configure as email elements — every entry under
	/// <c>elements[]</c> carrying a non-null <c>email</c> object. Returns an empty list for a payload with no email
	/// block, which is the common case and skips the verification entirely.
	/// </summary>
	/// <param name="descriptorJson">The build descriptor JSON exactly as the caller supplied it.</param>
	public static IReadOnlyList<string> FromDescriptor(string descriptorJson) {
		JsonObject? descriptor = TryParse(descriptorJson) as JsonObject;
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
		if (TryParse(operationsJson) is not JsonArray operations) {
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
	/// i.e. the ones whose configuration was discarded. An element missing from the description entirely counts as
	/// dropped too: the caller asked for an email element and the read-back cannot show one.
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
			DescribedElement? element = described.Elements.FirstOrDefault(e =>
				string.Equals(e?.Name, name, StringComparison.OrdinalIgnoreCase)
				|| string.Equals(e?.Uid, name, StringComparison.OrdinalIgnoreCase));

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
			+ "(clio install-process-builder) and re-apply the email block, or configure the element in the designer.";
	}

	/// <summary>
	/// Element names whose build descriptor <c>email.body</c> carries at least one friendly process-macro
	/// placeholder (<c>[[param:</c> / <c>[[element:</c>). These bodies must be verified after a build: a
	/// CrtProcessBuilder that predates the body-macro feature stores the placeholder text verbatim and still
	/// answers success, so the literal <c>[[…]]</c> would be emailed to the recipient.
	/// </summary>
	/// <param name="descriptorJson">The build descriptor JSON exactly as the caller supplied it.</param>
	public static IReadOnlyList<string> MacroBodyElements(string descriptorJson) {
		JsonObject? descriptor = TryParse(descriptorJson) as JsonObject;
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
			DescribedElement? element = described.Elements.FirstOrDefault(e =>
				string.Equals(e?.Name, name, StringComparison.OrdinalIgnoreCase)
				|| string.Equals(e?.Uid, name, StringComparison.OrdinalIgnoreCase));

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
			+ "(clio install-process-builder) and re-apply the body, or insert the process data through the "
			+ "designer's Content designer.";
	}

	// Singular/plural of "element" for a count, kept in one place so the noun is not a duplicated string literal.
	private static string ElementNoun(int count) => count == 1 ? "element" : "elements";

	/// <summary>
	/// Reads an element name, tolerating a node that is not a string.
	/// <para><c>GetValue&lt;string&gt;()</c> THROWS on <c>"name": 123</c>, and this check runs AFTER a successful
	/// operation, inside the command's try — so a payload the server happily accepted would be reported to the
	/// caller as a failed build. Both expectation checks run in the same block, email first, so a throw here would
	/// take the approval one down with it. Same idiom <c>ApprovalBlockExpectation</c> uses.</para>
	/// </summary>
	private static string? ReadName(JsonNode? node) =>
		node is JsonValue value && value.TryGetValue(out string? text) ? text : null;

	private static bool ContainsMacro(string body) =>
		body.IndexOf("[[param:", StringComparison.OrdinalIgnoreCase) >= 0
		|| body.IndexOf("[[element:", StringComparison.OrdinalIgnoreCase) >= 0;

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
