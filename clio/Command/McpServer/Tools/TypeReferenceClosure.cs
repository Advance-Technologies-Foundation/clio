using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Resolves which named type schemas a component detail response actually needs.
///
/// The producer publishes one big <c>root.references.typeDefinitions</c> bag (shared
/// across all components, ~190 keys at 8.1) and a per-component
/// <c>entry.references.typeDefinitions</c> bag that lists only the types directly
/// referenced by that component's inputs/outputs. A naive merge would surface every
/// global type on every detail response — 97% noise for a small component like
/// <c>crt.Button</c>, and a forcing trap for AI that should never need to look at
/// unrelated types.
///
/// This helper walks the transitive closure starting from:
/// <list type="bullet">
/// <item>type identifiers tokenised from every <c>"type": "..."</c> string in
/// <c>entry.inputs</c> and <c>entry.outputs</c>;</item>
/// <item>the type identifiers tokenised from inside the per-component typedefs
/// themselves (a per-component schema can reference a global type — e.g. a
/// <c>filter</c> field whose type is <c>BaseFilter</c>).</item>
/// </list>
/// Each newly reached name is looked up in the per-component bag first, then in the
/// global bag; if it lands, its own <c>"type"</c> strings are queued. Identifiers
/// that resolve to neither bag are silently dropped — they are built-ins
/// (<c>string</c>, <c>Record</c>, <c>Promise</c>, …) and not part of the wire shape.
/// </summary>
internal static class TypeReferenceClosure {
	private static readonly Regex IdentifierToken = new(@"[A-Za-z_][A-Za-z0-9_]*", RegexOptions.Compiled);

	/// <summary>
	/// Property names whose <c>JsonElement</c> value is payload, not a type reference.
	/// Walking into them would tokenise allowed values like <c>"close-icon"</c> as
	/// type names.
	/// </summary>
	private static readonly HashSet<string> PayloadPropertyNames = new(System.StringComparer.Ordinal) {
		"description",
		"default",
		"values",
		"valueSource",
	};

	/// <summary>
	/// Returns the set of type definitions reachable from the component's inputs,
	/// outputs, and per-component typedefs. Returns <see langword="null"/> when
	/// neither bag has anything to surface.
	/// </summary>
	internal static IReadOnlyDictionary<string, JsonElement>? Resolve(
		IReadOnlyDictionary<string, JsonElement>? inputs,
		IReadOnlyDictionary<string, JsonElement>? outputs,
		IReadOnlyDictionary<string, JsonElement>? perComponentTypeDefinitions,
		IReadOnlyDictionary<string, JsonElement>? globalTypeDefinitions) {
		bool perComponentEmpty = perComponentTypeDefinitions is null || perComponentTypeDefinitions.Count == 0;
		bool globalEmpty = globalTypeDefinitions is null || globalTypeDefinitions.Count == 0;
		if (perComponentEmpty && globalEmpty) {
			return null;
		}

		Dictionary<string, JsonElement> resolved = new(System.StringComparer.Ordinal);
		Queue<string> queue = new();

		// Seed: every per-component typedef is already known to be relevant (the
		// producer filtered them). Surface them verbatim and queue their inner
		// references for the closure.
		if (perComponentTypeDefinitions is not null) {
			foreach (KeyValuePair<string, JsonElement> typedef in perComponentTypeDefinitions) {
				resolved[typedef.Key] = typedef.Value;
				ExtractIdentifiers(typedef.Value, queue);
			}
		}

		// Seed from the input/output surfaces — these are the AI-visible binding
		// shapes that frame which globals AI actually needs to resolve.
		if (inputs is not null) {
			foreach (JsonElement value in inputs.Values) {
				ExtractIdentifiers(value, queue);
			}
		}
		if (outputs is not null) {
			foreach (JsonElement value in outputs.Values) {
				ExtractIdentifiers(value, queue);
			}
		}

		while (queue.Count > 0) {
			string name = queue.Dequeue();
			if (resolved.ContainsKey(name)) {
				continue;
			}
			if (TryFind(name, perComponentTypeDefinitions, globalTypeDefinitions, out JsonElement element)) {
				resolved[name] = element;
				ExtractIdentifiers(element, queue);
			}
			// else: built-in or producer-side typo — silently skip, matches the
			// loader's graceful-degradation posture.
		}

		return resolved.Count == 0 ? null : resolved;
	}

	private static bool TryFind(
		string name,
		IReadOnlyDictionary<string, JsonElement>? perComponent,
		IReadOnlyDictionary<string, JsonElement>? global,
		out JsonElement element) {
		if (perComponent is not null && perComponent.TryGetValue(name, out element)) {
			return true;
		}
		if (global is not null && global.TryGetValue(name, out element)) {
			return true;
		}
		element = default;
		return false;
	}

	/// <summary>
	/// Walks a <see cref="JsonElement"/> and pushes every PascalCase-ish identifier
	/// found under a <c>"type"</c> string into the queue. Recurses through nested
	/// objects and arrays; skips properties listed in <see cref="PayloadPropertyNames"/>
	/// because their string values are user-facing data (allowed values, defaults,
	/// descriptions), not type references.
	/// </summary>
	private static void ExtractIdentifiers(JsonElement element, Queue<string> queue) {
		switch (element.ValueKind) {
			case JsonValueKind.Object:
				foreach (JsonProperty property in element.EnumerateObject()) {
					if (PayloadPropertyNames.Contains(property.Name)) {
						continue;
					}
					if (property.NameEquals("type") && property.Value.ValueKind == JsonValueKind.String) {
						TokenizeTypeString(property.Value.GetString(), queue);
						continue;
					}
					ExtractIdentifiers(property.Value, queue);
				}
				break;
			case JsonValueKind.Array:
				foreach (JsonElement item in element.EnumerateArray()) {
					ExtractIdentifiers(item, queue);
				}
				break;
		}
	}

	private static void TokenizeTypeString(string? typeString, Queue<string> queue) {
		if (string.IsNullOrEmpty(typeString)) {
			return;
		}
		foreach (Match match in IdentifierToken.Matches(typeString)) {
			string token = match.Value;
			// PascalCase filter weeds out built-ins (string, number, boolean, any,
			// void, …) without an explicit allowlist. Producer-defined types are
			// PascalCase by convention.
			if (token.Length > 0 && char.IsUpper(token[0])) {
				queue.Enqueue(token);
			}
		}
	}
}
