using System;
using System.Collections.Generic;
using System.Text.Json;
using Clio.Command.BusinessRules;
using Clio.Command.BusinessRules.Filters.Schema;

namespace Clio.Command.Filters;

/// <summary>A stored filter group found in a page body, with the schema the runtime runs it against.</summary>
internal sealed record PageWidgetFilter(string Path, string RootSchemaName, JsonElement Group);

public sealed record PageWidgetFilterReport(IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings) {
	internal static readonly PageWidgetFilterReport Empty = new([], []);
}

/// <summary>
/// Validates the stored ESQ filters of a page's widgets against the environment's entity schemas
/// (ENG-95270). Before this, a chart/indicator filter leaf was checked by nothing: the registry validator
/// stops at the opaque filter types, and the only signal was an optional <c>execute-esq</c> dry run.
/// </summary>
public interface IWidgetFilterValidator {
	/// <param name="body">Freedom UI page body (web).</param>
	/// <param name="packageUId">Package the page is saved to; entity schemas are read in its context.</param>
	PageWidgetFilterReport Validate(string body, Guid packageUId);
}

internal sealed class WidgetFilterValidator(IEntityBusinessRuleSchemaProvider entitySchemaProvider) : IWidgetFilterValidator {

	private const string ViewConfigDiffMarker = "SCHEMA_VIEW_CONFIG_DIFF";
	private const string LegacyDiffMarker = "SCHEMA_DIFF";

	public PageWidgetFilterReport Validate(string body, Guid packageUId) {
		IReadOnlyList<PageWidgetFilter> filters = WidgetFilterLocator.Locate(body);
		if (filters.Count == 0) {
			return PageWidgetFilterReport.Empty;
		}
		FilterSchemaProvider schemaProvider = new(entitySchemaProvider);
		schemaProvider.Initialize(packageUId);
		EsqFilterSemanticValidator validator = new(schemaProvider);
		List<string> errors = [];
		List<string> warnings = [];
		foreach (PageWidgetFilter filter in filters) {
			EsqFilterGroupNode? group = EsqFilterReader.ReadGroup(filter.Group, filter.Path);
			if (group is null) {
				continue;
			}
			EsqFilterFindings findings = validator.Validate(group, filter.RootSchemaName);
			errors.AddRange(findings.Errors);
			warnings.AddRange(findings.Warnings);
		}
		return new PageWidgetFilterReport(errors, warnings);
	}

	/// <summary>
	/// Finds widget filter groups in the view-config diff: a data-providing block (<c>schemaName</c> +
	/// <c>filters.filter</c>, as in chart series and indicators) or an entity block whose <c>filters</c> is
	/// the group itself (funnel). Both inserts and merges are scanned, since a merge that edits a filter is the
	/// common way an agent changes one.
	/// </summary>
	internal static class WidgetFilterLocator {

		internal static IReadOnlyList<PageWidgetFilter> Locate(string body) {
			List<PageWidgetFilter> found = [];
			if (string.IsNullOrEmpty(body)
				|| !PageSchemaSectionReader.TryRead(body, out string content, ViewConfigDiffMarker, LegacyDiffMarker)) {
				return found;
			}
			JsonDocument document;
			try {
				document = JsonDocument.Parse(content, new JsonDocumentOptions {
					AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip
				});
			} catch (JsonException) {
				return found;
			}
			using (document) {
				if (document.RootElement.ValueKind != JsonValueKind.Array) {
					return found;
				}
				foreach (JsonElement operation in document.RootElement.EnumerateArray()) {
					if (operation.ValueKind == JsonValueKind.Object
						&& operation.TryGetProperty("values", out JsonElement values)) {
						string name = operation.TryGetProperty("name", out JsonElement nameElement)
							&& nameElement.ValueKind == JsonValueKind.String
								? nameElement.GetString()!
								: "?";
						Walk(values, $"{name}.values", found);
					}
				}
			}
			return found;
		}

		private static void Walk(JsonElement node, string path, List<PageWidgetFilter> found) {
			switch (node.ValueKind) {
				case JsonValueKind.Object:
					if (TryGetFilter(node, path, out PageWidgetFilter? filter)) {
						found.Add(filter!);
					}
					foreach (JsonProperty property in node.EnumerateObject()) {
						if (property.NameEquals("filters")) {
							continue;
						}
						Walk(property.Value, $"{path}.{property.Name}", found);
					}
					break;
				case JsonValueKind.Array:
					int index = 0;
					foreach (JsonElement item in node.EnumerateArray()) {
						Walk(item, $"{path}[{index}]", found);
						index++;
					}
					break;
			}
		}

		private static bool TryGetFilter(JsonElement node, string path, out PageWidgetFilter? filter) {
			filter = null;
			if (!node.TryGetProperty("filters", out JsonElement filters) || filters.ValueKind != JsonValueKind.Object) {
				return false;
			}
			bool isProviding = filters.TryGetProperty("filter", out JsonElement providingGroup)
				&& providingGroup.ValueKind == JsonValueKind.Object;
			JsonElement group = isProviding ? providingGroup : filters;
			if (!group.TryGetProperty("items", out _)) {
				return false;
			}
			string? root = ReadString(node, "schemaName") ?? ReadString(group, "rootSchemaName");
			if (string.IsNullOrWhiteSpace(root)) {
				return false;
			}
			filter = new PageWidgetFilter(isProviding ? $"{path}.filters.filter" : $"{path}.filters", root, group.Clone());
			return true;
		}

		private static string? ReadString(JsonElement element, string property) =>
			element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
				? value.GetString()
				: null;
	}
}
