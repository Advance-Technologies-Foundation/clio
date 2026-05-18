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

		if (string.Equals(response.Mode, "detail", System.StringComparison.OrdinalIgnoreCase)
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
	}

	private static void AppendList(StringBuilder sb, ComponentInfoResponse response) {
		if (response.Items is null || response.Items.Count == 0) {
			sb.AppendLine().AppendLine("(no components)");
			return;
		}

		sb.AppendLine();
		int width = response.Items.Max(item => item.ComponentType.Length);
		foreach (ComponentInfoListItem item in response.Items) {
			sb.Append("  ").Append(item.ComponentType.PadRight(width));
			if (!string.IsNullOrWhiteSpace(item.Description)) {
				sb.Append("  ").Append(item.Description);
			}
			sb.AppendLine();
		}
	}

	private static void AppendDetail(StringBuilder sb, ComponentInfoResponse response) {
		sb.AppendLine();
		sb.Append("componentType:    ").AppendLine(response.ComponentType);
		if (!string.IsNullOrWhiteSpace(response.Description)) {
			sb.Append("description:      ").AppendLine(response.Description);
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
		AppendExample(sb, response.Example);
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
}
