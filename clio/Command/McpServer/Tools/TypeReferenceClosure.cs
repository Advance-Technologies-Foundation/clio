using System;
using System.Collections.Generic;
using System.Linq;
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
/// <item>type identifiers tokenised from every <c>"type"</c> / <c>"keyType"</c> /
/// <c>"valueType"</c> string in <c>entry.inputs</c> and <c>entry.outputs</c>;</item>
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
	// The pattern is linear (no quantifier nesting, no alternation that could
	// backtrack) so a malicious type string cannot induce catastrophic
	// backtracking. The 1-second timeout is a defence-in-depth guard against
	// pathological inputs and clears Sonar S6444 — orders of magnitude above any
	// realistic type-string length. Keep on a single line so the rule's pattern
	// matcher picks the timeout up.
	private static readonly Regex IdentifierToken = new(@"[A-Za-z_][A-Za-z0-9_]*", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

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
	/// Property names whose string value carries type references to tokenise.
	/// <c>type</c> is the primary carrier; Record-shaped schemas reference their key and
	/// value types through <c>keyType</c> / <c>valueType</c> instead (e.g. a request's
	/// <c>parameters</c> map or <c>RequestBindingConfig.params</c>), so those strings
	/// must feed the closure too - otherwise the detail response names types it never
	/// defines.
	/// </summary>
	private static readonly HashSet<string> TypeReferencePropertyNames = new(System.StringComparer.Ordinal) {
		"type",
		"keyType",
		"valueType",
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
		if (IsEmpty(perComponentTypeDefinitions) && IsEmpty(globalTypeDefinitions)) {
			return null;
		}

		Dictionary<string, JsonElement> resolved = new(System.StringComparer.Ordinal);
		Queue<string> queue = new();
		Seed(resolved, queue, inputs, outputs, perComponentTypeDefinitions);
		Walk(resolved, queue, perComponentTypeDefinitions, globalTypeDefinitions);
		return resolved.Count == 0 ? null : resolved;
	}

	private static bool IsEmpty(IReadOnlyDictionary<string, JsonElement>? bag) =>
		bag is null || bag.Count == 0;

	/// <summary>
	/// Populates the resolved set with every per-component typedef verbatim (the
	/// producer already filtered them) and queues every identifier tokenised from
	/// the per-component typedefs and the input/output binding surfaces.
	/// </summary>
	private static void Seed(
		Dictionary<string, JsonElement> resolved,
		Queue<string> queue,
		IReadOnlyDictionary<string, JsonElement>? inputs,
		IReadOnlyDictionary<string, JsonElement>? outputs,
		IReadOnlyDictionary<string, JsonElement>? perComponentTypeDefinitions) {
		if (perComponentTypeDefinitions is not null) {
			foreach (KeyValuePair<string, JsonElement> typedef in perComponentTypeDefinitions) {
				resolved[typedef.Key] = typedef.Value;
				ExtractIdentifiers(typedef.Value, queue);
			}
		}
		QueueIdentifiersFromBindings(queue, inputs);
		QueueIdentifiersFromBindings(queue, outputs);
	}

	private static void QueueIdentifiersFromBindings(
		Queue<string> queue,
		IReadOnlyDictionary<string, JsonElement>? bindings) {
		if (bindings is null) {
			return;
		}
		foreach (JsonElement value in bindings.Values) {
			ExtractIdentifiers(value, queue);
		}
	}

	/// <summary>
	/// BFS over the queue. Each dequeued identifier is looked up first in the
	/// per-component bag then in the global one; new typedefs contribute their own
	/// inner type references to the queue. Identifiers that resolve to neither
	/// bag are built-ins (or producer-side typos) and are silently skipped.
	/// </summary>
	private static void Walk(
		Dictionary<string, JsonElement> resolved,
		Queue<string> queue,
		IReadOnlyDictionary<string, JsonElement>? perComponent,
		IReadOnlyDictionary<string, JsonElement>? global) {
		while (queue.Count > 0) {
			string name = queue.Dequeue();
			if (resolved.ContainsKey(name)) {
				continue;
			}
			if (!TryFind(name, perComponent, global, out JsonElement element)) {
				continue;
			}
			resolved[name] = element;
			ExtractIdentifiers(element, queue);
		}
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
	/// found under a <see cref="TypeReferencePropertyNames"/> string (<c>type</c>,
	/// <c>keyType</c>, <c>valueType</c>) into the queue. Recurses through nested
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
					if (TypeReferencePropertyNames.Contains(property.Name) && property.Value.ValueKind == JsonValueKind.String) {
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
		// PascalCase filter weeds out built-ins (string, number, boolean, any,
		// void, …) without an explicit allowlist. Producer-defined types are
		// PascalCase by convention.
		IEnumerable<string> identifiers = IdentifierToken.Matches(typeString)
			.Select(match => match.Value)
			.Where(token => token.Length > 0 && char.IsUpper(token[0]));
		foreach (string token in identifiers) {
			queue.Enqueue(token);
		}
	}
}
