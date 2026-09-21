using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Clio.Command.ProcessModel;

/// <summary>
/// The payload-reading and element-matching mechanics shared by the post-operation block guards
/// (<see cref="EmailBlockExpectation"/>, <see cref="AccessRightsBlockExpectation"/>,
/// <see cref="FlowLabelExpectation"/>).
/// <para>The two BLOCK guards answer the same three questions about a caller's payload — which elements
/// asked for this block on a build, which asked for it on a modify, and which described element
/// corresponds to a name the caller used — and only the block key and the presence predicate differ.
/// Keeping the mechanics here means a fix to the parsing or the name-or-uid matching lands once instead of
/// once per block, and the next block to need a guard adds a key rather than a fourth copy.</para>
/// <para><see cref="FlowLabelExpectation"/> is in the see-also list but is NOT one of those: it has no
/// block key, no presence predicate and no element matching, because a label hangs off a FLOW and a flow
/// is addressed by its endpoint pair rather than by an element name. It reuses exactly one thing from
/// here, <see cref="Parse"/> and <see cref="ReadText"/>, so do not read this class as an extension point
/// that would accommodate it - the element-shaped helpers below do not apply to it.</para>
/// </summary>
internal static class BlockExpectationJson {

	private const string ElementsKey = "elements";

	/// <summary>
	/// Element names a BUILD descriptor asks to configure with the named block — every entry under
	/// <c>elements[]</c> carrying it as a non-null object. Empty for a payload that carries none, which is
	/// the common case and skips the verification entirely.
	/// </summary>
	internal static IReadOnlyList<string> ElementsCarrying(string descriptorJson, string blockKey) {
		if (Parse(descriptorJson) is not JsonObject descriptor
			|| descriptor[ElementsKey] is not JsonArray elements) {
			return Array.Empty<string>();
		}

		List<string> names = [];
		foreach (JsonNode? element in elements) {
			if (element is JsonObject candidate && candidate[blockKey] is JsonObject) {
				AddName(names, candidate["name"]);
			}
		}

		return names;
	}

	/// <summary>
	/// Element names a MODIFY operations array asks to configure with the named block through
	/// <c>setElement</c> (the block lives under <c>elementUpdate</c>, the name on the operation itself).
	/// </summary>
	internal static IReadOnlyList<string> SetElementTargets(string operationsJson, string blockKey) {
		if (Parse(operationsJson) is not JsonArray operations) {
			return Array.Empty<string>();
		}

		List<string> names = [];
		foreach (JsonNode? operation in operations) {
			if (operation is JsonObject op
				&& op["elementUpdate"] is JsonObject update
				&& update[blockKey] is JsonObject) {
				AddName(names, op["elementName"]);
			}
		}

		return names;
	}

	/// <summary>
	/// Element names an <c>addElement</c> operation carries the named block for (the descriptor, and so the
	/// name, nested under <c>element</c>).
	/// </summary>
	internal static IReadOnlyList<string> AddElementTargets(string operationsJson, string blockKey) {
		if (Parse(operationsJson) is not JsonArray operations) {
			return Array.Empty<string>();
		}

		List<string> names = [];
		foreach (JsonNode? operation in operations) {
			if (operation is JsonObject op
				&& op["element"] is JsonObject added
				&& added[blockKey] is JsonObject) {
				AddName(names, added["name"]);
			}
		}

		return names;
	}

	/// <summary>
	/// The described element a caller's identifier refers to, matched on NAME OR UID: <c>setElement</c>
	/// accepts either (the server's ResolveFlowElement canonicalizes both), so matching on name alone would
	/// tell a caller who passed a UId that its configuration had been discarded when the edit applied
	/// cleanly. Null when the read-back does not contain it.
	/// </summary>
	internal static DescribedElement? ResolveElement(DescribeProcessResult described, string name) =>
		described?.Elements?.FirstOrDefault(e =>
			string.Equals(e?.Name, name, StringComparison.OrdinalIgnoreCase)
			|| string.Equals(e?.Uid, name, StringComparison.OrdinalIgnoreCase));

	/// <summary>
	/// Element names the operations array targets with any of <paramref name="opTokens"/>, read from the
	/// operation's own <c>elementName</c>. Used for operations that carry no block of their own but still change
	/// state a guard has to check afterwards.
	/// </summary>
	internal static IReadOnlyList<string> OperationTargets(string operationsJson, params string[] opTokens) {
		if (Parse(operationsJson) is not JsonArray operations) {
			return Array.Empty<string>();
		}

		List<string> names = [];
		foreach (JsonNode? operation in operations) {
			if (operation is JsonObject op
				&& op["op"] is JsonValue token
				&& token.TryGetValue(out string? op_name)
				&& opTokens.Any(t => string.Equals(t, op_name, StringComparison.OrdinalIgnoreCase))) {
				AddName(names, op["elementName"]);
			}
		}

		return names;
	}

	/// <summary>One element, named once, however many operations configured it.</summary>
	internal static IReadOnlyList<string> Distinct(IReadOnlyList<string> names) =>
		[.. names.Distinct(StringComparer.OrdinalIgnoreCase)];

	/// <summary>Singular/plural of "element", so the warnings agree with their own subject count.</summary>
	internal static string ElementNoun(int count) => count == 1 ? "element" : "elements";

	/// <summary>
	/// A JSON member read as a string, or <c>null</c> when it is absent or is not a string.
	/// <para>Tolerant on purpose, and this is the piece most worth having in one place: the obvious
	/// <c>GetValue&lt;string&gt;()</c> THROWS on a number, an object or a bool, and every caller runs inside a
	/// post-success guard - so a payload with a mistyped member would turn a write that landed into a reported
	/// failure. The server's own <c>DataContractJsonSerializer</c> rejects the same payload, so answering
	/// null here agrees with it rather than guessing.</para>
	/// </summary>
	internal static string? ReadText(JsonNode? node) =>
		node is JsonValue value && value.TryGetValue(out string? text) ? text : null;

	private static void AddName(List<string> names, JsonNode? node) {
		string? name = ReadText(node);
		if (!string.IsNullOrWhiteSpace(name)) {
			names.Add(name);
		}
	}

	/// <summary>
	/// Parsed defensively: an unparseable payload is the command's problem to report through the normal
	/// error path, not a guard's. Returning null skips the verification rather than masking the real
	/// failure with a second, less useful message.
	/// </summary>
	internal static JsonNode? Parse(string json) {
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
