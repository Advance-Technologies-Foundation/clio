using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Renders <see cref="ComponentInfoResponse"/> as a human-readable text block for the
/// <c>clio get-component-info --pretty</c> CLI surface. Output is a single string,
/// suitable for direct stdout printing.
/// </summary>
public static class ComponentInfoPrettyRenderer {
	private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

	public static string Render(ComponentInfoResponse response) {
		StringBuilder sb = new();

		if (!response.Success && !string.IsNullOrWhiteSpace(response.Error)) {
			sb.Append("Error: ").AppendLine(response.Error);
			sb.AppendLine();
		}

		AppendHeader(sb, response);

		if (string.Equals(response.Mode, "composite", System.StringComparison.OrdinalIgnoreCase)) {
			AppendComposite(sb, response);
		} else if (string.Equals(response.Mode, "detail", System.StringComparison.OrdinalIgnoreCase)
			&& !string.IsNullOrWhiteSpace(response.ComponentType)) {
			AppendDetail(sb, response);
		} else {
			AppendList(sb, response);
		}

		return sb.ToString().TrimEnd() + "\n";
	}

	private static void AppendHeader(StringBuilder sb, ComponentInfoResponse response) {
		sb.Append("Freedom UI components")
			.Append("  (version=").Append(response.ResolvedTargetVersion ?? "?")
			.Append(", resolvedFrom=").Append(response.ResolvedFrom ?? "?")
			.Append(", count=").Append(response.Count)
			.Append(")")
			.AppendLine();
		if (!string.IsNullOrWhiteSpace(response.VersionWarning)) {
			sb.Append("WARNING: ").Append(response.VersionWarning);
			// Surface the machine-readable markers the JSON consumers receive so the human --pretty view
			// reaches parity: on latest-fallback the version is unknown (hard stop), and resolvedFromReason
			// tells the operator whether it is worth a retry. Omitted on environment-superset (soft caveat,
			// version known) and environment (exact match), exactly like the wire shape.
			if (response.RequiresVersionConfirmation == true) {
				sb.Append(" [requiresVersionConfirmation=true");
				if (!string.IsNullOrWhiteSpace(response.ResolvedFromReason)) {
					sb.Append("; resolvedFromReason=").Append(response.ResolvedFromReason);
				}
				sb.Append(']');
			}
			sb.AppendLine();
		}
	}

	private static void AppendList(StringBuilder sb, ComponentInfoResponse response) {
		bool hasItems = response.Items is { Count: > 0 };
		bool hasComposites = response.Composites is { Count: > 0 };
		if (!hasItems && !hasComposites) {
			sb.AppendLine().AppendLine("(no components)");
			return;
		}

		if (hasItems) {
			sb.AppendLine();
			int width = response.Items!.Max(item => item.ComponentType.Length);
			foreach (ComponentInfoListItem item in response.Items) {
				sb.Append("  ").Append(item.ComponentType.PadRight(width));
				if (!string.IsNullOrWhiteSpace(item.Description)) {
					sb.Append("  ").Append(item.Description);
				}
				if (item.CompositeOnly == true) {
					sb.Append("  (composite-only)");
				}
				sb.AppendLine();
			}
		}

		AppendComposites(sb, response.Composites);
	}

	/// <summary>
	/// Renders the list-mode <c>composites:</c> section — the pre-built Designer elements
	/// (e.g. "Expanded list", "Next steps") that have no <c>componentType</c> of their own.
	/// Without this, list-mode <c>--pretty</c> would silently hide every composite the JSON
	/// surface returns. Fetch a composite's assembly docs with <c>--composite "&lt;caption&gt;"</c>.
	/// </summary>
	private static void AppendComposites(StringBuilder sb, IReadOnlyList<CompositeSummary>? composites) {
		if (composites is null || composites.Count == 0) {
			return;
		}
		sb.AppendLine().AppendLine("composites:");
		int width = composites.Max(composite => composite.Caption.Length);
		foreach (CompositeSummary composite in composites) {
			sb.Append("  ").Append(composite.Caption.PadRight(width));
			if (!string.IsNullOrWhiteSpace(composite.Description)) {
				sb.Append("  ").Append(composite.Description);
			}
			sb.AppendLine();
		}
	}

	/// <summary>
	/// Renders a <c>mode: "composite"</c> detail response: the composite's caption,
	/// description, and concatenated assembly documentation. When the composite declares
	/// docs but none could be loaded (transient CDN/cache failure) the response carries
	/// <c>documentationUnavailable</c>; surface that here so the human view reaches parity
	/// with the JSON consumers. On a not-found composite the caption is empty (the error
	/// line is already printed by <see cref="Render"/>), so nothing is emitted here.
	/// </summary>
	private static void AppendComposite(StringBuilder sb, ComponentInfoResponse response) {
		if (string.IsNullOrWhiteSpace(response.Caption)) {
			return;
		}
		sb.AppendLine();
		sb.Append("composite:        ").AppendLine(response.Caption);
		if (!string.IsNullOrWhiteSpace(response.Description)) {
			sb.Append("description:      ").AppendLine(response.Description);
		}
		if (response.DocumentationUnavailable == true) {
			sb.AppendLine().AppendLine("(documentation is declared for this composite but could not be loaded)");
		}
		AppendDocumentation(sb, response.Documentation);
	}

	private static void AppendDetail(StringBuilder sb, ComponentInfoResponse response) {
		sb.AppendLine();
		sb.Append("componentType:    ").AppendLine(response.ComponentType);
		if (!string.IsNullOrWhiteSpace(response.Description)) {
			sb.Append("description:      ").AppendLine(response.Description);
		}
		if (response.CompositeOnly == true) {
			sb.Append("compositeOnly:    ").AppendLine("yes");
			if (!string.IsNullOrWhiteSpace(response.CompositeOnlyHint)) {
				sb.Append("compositeOnlyHint: ").AppendLine(response.CompositeOnlyHint);
			}
		}
		if (response.Container is { } container) {
			sb.Append("container:        ").AppendLine(container ? "yes" : "no");
		}
		if (response.ParentTypes is { Count: > 0 } parents) {
			sb.Append("parentTypes:      ").AppendLine(string.Join(", ", parents));
		}
		if (response.TypicalChildren is { Count: > 0 } children) {
			sb.Append("typicalChildren:  ").AppendLine(string.Join(", ", children));
		}
		AppendProperties(sb, response.Properties);
		AppendBindings(sb, "inputs", response.Inputs);
		AppendBindings(sb, "outputs", response.Outputs);
		AppendTypeDefinitions(sb, response.References?.TypeDefinitions);
		AppendExample(sb, response.Example);
		AppendDocumentation(sb, response.Documentation);
	}

	/// <summary>
	/// Renders the producer's named type schemas (e.g. <c>ButtonIcon</c>,
	/// <c>DataGridColumnDefinition</c>) under a dedicated <c>references.typeDefinitions:</c>
	/// header. Each entry shows the type name on its own line, then the producer's raw
	/// schema indented under it as a compact one-line JSON blob. We intentionally do
	/// NOT walk into <c>fields</c>/<c>values</c> here — type definitions are arbitrary
	/// producer-defined schemas, and a one-line JSON gives the operator a stable
	/// diff-friendly read regardless of how nested the schema gets.
	/// </summary>
	private static void AppendTypeDefinitions(StringBuilder sb, IReadOnlyDictionary<string, JsonElement>? typeDefinitions) {
		if (typeDefinitions is null || typeDefinitions.Count == 0) {
			return;
		}
		sb.AppendLine().AppendLine("references.typeDefinitions:");
		int nameWidth = typeDefinitions.Keys.Max(key => key.Length);
		foreach (KeyValuePair<string, JsonElement> definition in typeDefinitions) {
			sb.Append("  ").Append(definition.Key.PadRight(nameWidth)).Append("  ")
				.AppendLine(definition.Value.GetRawText());
		}
	}

	private static void AppendProperties(StringBuilder sb, IReadOnlyDictionary<string, ComponentPropertyDefinition>? properties) {
		if (properties is null || properties.Count == 0) {
			return;
		}
		sb.AppendLine().AppendLine("properties:");
		int nameWidth = properties.Keys.Max(key => key.Length);
		int typeWidth = properties.Values.Max(value => value.Type?.Length ?? 0);
		foreach (KeyValuePair<string, ComponentPropertyDefinition> kvp in properties) {
			sb.Append("  ")
				.Append(kvp.Key.PadRight(nameWidth))
				.Append("  ")
				.Append((kvp.Value.Type ?? "").PadRight(typeWidth))
				.Append("  ")
				.Append(kvp.Value.Description ?? string.Empty);
			if (kvp.Value.Required == true) {
				sb.Append("  (required)");
			}
			if (kvp.Value.Values is { Count: > 0 } values) {
				sb.Append("  values=[").Append(string.Join(", ", values)).Append(']');
			}
			sb.AppendLine();
		}
	}

	/// <summary>
	/// Renders the wrapped-shape <c>inputs</c> / <c>outputs</c> dictionaries. Each
	/// value is a producer-owned <see cref="JsonElement"/> blob, so the renderer only
	/// pulls well-known string fields (<c>type</c>, <c>description</c>, <c>default</c>,
	/// <c>values</c>) onto the one-line summary; richer keys (<c>keyType</c>, <c>items</c>,
	/// <c>deprecated</c>, future additions) stay invisible at the CLI surface but remain
	/// available through the JSON detail mode.
	/// </summary>
	private static void AppendBindings(StringBuilder sb, string label, IReadOnlyDictionary<string, JsonElement>? bindings) {
		if (bindings is null || bindings.Count == 0) {
			return;
		}
		sb.AppendLine().Append(label).AppendLine(":");
		int nameWidth = bindings.Keys.Max(key => key.Length);
		int typeWidth = bindings.Values.Max(value => GetBindingString(value, "type")?.Length ?? 0);
		foreach (KeyValuePair<string, JsonElement> binding in bindings) {
			string? type = GetBindingString(binding.Value, "type");
			string? description = GetBindingString(binding.Value, "description");
			string? defaultValue = GetBindingDefaultLiteral(binding.Value);
			sb.Append("  ")
				.Append(binding.Key.PadRight(nameWidth))
				.Append("  ")
				.Append((type ?? string.Empty).PadRight(typeWidth));
			if (!string.IsNullOrWhiteSpace(description)) {
				sb.Append("  ").Append(description);
			}
			if (!string.IsNullOrWhiteSpace(defaultValue)) {
				sb.Append("  default=").Append(defaultValue);
			}
			List<string>? values = GetBindingStringArray(binding.Value, "values");
			if (values is { Count: > 0 }) {
				sb.Append("  values=[").Append(string.Join(", ", values)).Append(']');
			}
			sb.AppendLine();
		}
	}

	private static string? GetBindingString(JsonElement element, string propertyName) {
		if (element.ValueKind != JsonValueKind.Object
			|| !element.TryGetProperty(propertyName, out JsonElement property)
			|| property.ValueKind != JsonValueKind.String) {
			return null;
		}
		return property.GetString();
	}

	/// <summary>
	/// Renders the <c>default</c> property of a binding value as a compact literal:
	/// numbers / bools / null come through as their raw text; strings are wrapped in
	/// quotes; complex shapes serialise to single-line JSON. Returns <c>null</c> when
	/// the binding does not declare a default.
	/// </summary>
	private static string? GetBindingDefaultLiteral(JsonElement element) {
		if (element.ValueKind != JsonValueKind.Object
			|| !element.TryGetProperty("default", out JsonElement property)) {
			return null;
		}
		return property.ValueKind switch {
			JsonValueKind.Null => "null",
			JsonValueKind.True => "true",
			JsonValueKind.False => "false",
			JsonValueKind.Number => property.GetRawText(),
			JsonValueKind.String => "\"" + property.GetString() + "\"",
			_ => property.GetRawText()
		};
	}

	private static List<string>? GetBindingStringArray(JsonElement element, string propertyName) {
		if (element.ValueKind != JsonValueKind.Object
			|| !element.TryGetProperty(propertyName, out JsonElement property)
			|| property.ValueKind != JsonValueKind.Array) {
			return null;
		}
		List<string> values = [];
		foreach (JsonElement item in property.EnumerateArray()) {
			if (item.ValueKind == JsonValueKind.String) {
				values.Add(item.GetString() ?? string.Empty);
			}
		}
		return values;
	}

	private static void AppendExample(StringBuilder sb, JsonElement? example) {
		if (example is null) {
			return;
		}
		sb.AppendLine().AppendLine("example:");
		string indented = JsonSerializer.Serialize(example, IndentedJson);
		foreach (string line in indented.Split('\n')) {
			sb.Append("  ").AppendLine(line.TrimEnd('\r'));
		}
	}

	/// <summary>
	/// Appends the long-form documentation block when the response carries one. The
	/// payload is markdown concatenated from every successfully-fetched <c>content.docs[]</c>
	/// file; the renderer indents it under a labelled header so the operator can tell
	/// the block apart from the structured metadata above.
	/// </summary>
	private static void AppendDocumentation(StringBuilder sb, string? documentation) {
		if (string.IsNullOrWhiteSpace(documentation)) {
			return;
		}
		sb.AppendLine().AppendLine("documentation:");
		foreach (string line in documentation.Split('\n')) {
			sb.Append("  ").AppendLine(line.TrimEnd('\r'));
		}
	}
}
