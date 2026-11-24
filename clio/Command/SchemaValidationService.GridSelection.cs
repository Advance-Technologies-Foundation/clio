using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Clio.Command;

/// <summary>
/// ENG-107305: the save-time warning for a grid that reads its row selection but never writes it back.
/// </summary>
public static partial class SchemaValidationService {

	/// <summary>Rule id of the grid-selection warning, the first token of its message.</summary>
	internal const string RuleGridSelectionWithoutWriteBack = "grid-selection-without-write-back";

	private const string SelectionStatePropertyName = "selectionState";
	private const string SelectionOptionsPropertyName = "_selectionOptions";
	private const string SelectionOptionsAttributePropertyName = "attribute";

	/// <summary>
	/// Warns when a grid binds <c>selectionState</c> but no operation of the body gives it
	/// <c>_selectionOptions.attribute</c>, the half that writes the user's row selection back to the bound
	/// attribute. Without it the attribute never changes, so every action enabled by the selection stays
	/// disabled. The Designer always writes both halves; only a hand-written body can lose one.
	/// </summary>
	/// <remarks>
	/// Checked on <c>insert</c> operations and on <c>merge</c> operations of an element the same body inserts.
	/// A <c>merge</c> of a parent's element is skipped: the parent may carry <c>_selectionOptions</c>, and its
	/// body is not available here. The write-back may come from any operation of the body on the same element.
	/// All findings are warnings: the check never rejects a save.
	/// </remarks>
	/// <param name="jsBody">Raw JavaScript body of a Freedom UI page schema.</param>
	/// <returns>An always-valid result with one warning per grid that lacks the write-back.</returns>
	public static SchemaValidationResult ValidateGridSelectionWriteBack(string jsBody) {
		var result = new SchemaValidationResult { IsValid = true };
		if (string.IsNullOrEmpty(jsBody)
			|| !PageSchemaSectionReader.TryRead(jsBody, out string vcdContent, SchemaViewConfigDiff, SchemaDiffMarker)
			|| !TryParseJsonDocument(vcdContent, out JsonDocument vcdDoc, out _)) {
			return result;
		}
		using (vcdDoc) {
			if (vcdDoc.RootElement.ValueKind != JsonValueKind.Array) {
				return result;
			}
			GridSelectionScan scan = ScanGridSelection(vcdDoc.RootElement);
			foreach ((string name, string binding) in scan.SelectionBindings) {
				if (scan.WrittenBack.Contains(name) || !scan.Inserted.Contains(name)) {
					continue;
				}
				result.Warnings.Add(FormatGridSelectionWarning(name, binding));
			}
		}
		return result;
	}

	private sealed record GridSelectionScan(
		HashSet<string> Inserted,
		HashSet<string> WrittenBack,
		List<(string Name, string Binding)> SelectionBindings);

	private static GridSelectionScan ScanGridSelection(JsonElement operations) {
		GridSelectionScan scan = new(new HashSet<string>(StringComparer.Ordinal),
			new HashSet<string>(StringComparer.Ordinal), []);
		var bound = new HashSet<string>(StringComparer.Ordinal);
		foreach (JsonElement entry in operations.EnumerateArray()) {
			if (!TryReadNamedOperation(entry, out string operation, out string name, out JsonElement values)) {
				continue;
			}
			if (operation == "insert") {
				scan.Inserted.Add(name);
			}
			if (HasSelectionWriteBack(values)) {
				scan.WrittenBack.Add(name);
			}
			if (values.TryGetProperty(SelectionStatePropertyName, out JsonElement selectionState)
				&& selectionState.ValueKind == JsonValueKind.String
				&& !string.IsNullOrWhiteSpace(selectionState.GetString())
				&& bound.Add(name)) {
				scan.SelectionBindings.Add((name, selectionState.GetString()!));
			}
		}
		return scan;
	}

	private static bool TryReadNamedOperation(JsonElement entry, out string operation, out string name,
		out JsonElement values) {
		operation = null!;
		name = null!;
		values = default;
		if (entry.ValueKind != JsonValueKind.Object
			|| !entry.TryGetProperty("operation", out JsonElement operationElement)
			|| operationElement.ValueKind != JsonValueKind.String
			|| !entry.TryGetProperty("name", out JsonElement nameElement)
			|| nameElement.ValueKind != JsonValueKind.String
			|| !entry.TryGetProperty("values", out values)
			|| values.ValueKind != JsonValueKind.Object) {
			return false;
		}
		operation = operationElement.GetString()!;
		name = nameElement.GetString()!;
		return operation is "insert" or "merge" && !string.IsNullOrEmpty(name);
	}

	private static bool HasSelectionWriteBack(JsonElement values) =>
		values.TryGetProperty(SelectionOptionsPropertyName, out JsonElement options)
		&& options.ValueKind == JsonValueKind.Object
		&& options.TryGetProperty(SelectionOptionsAttributePropertyName, out JsonElement attribute)
		&& attribute.ValueKind == JsonValueKind.String
		&& !string.IsNullOrWhiteSpace(attribute.GetString());

	private static string FormatGridSelectionWarning(string name, string binding) {
		string attribute = binding.StartsWith('$') ? binding[1..] : binding;
		return $"{RuleGridSelectionWithoutWriteBack} — '{Sanitize(name)}' binds selectionState "
			+ $"'{Sanitize(binding)}' but no operation gives it {SelectionOptionsPropertyName}.attribute, so the row "
			+ "selection is never written back and every action enabled by it stays disabled. Add "
			+ $"\"{SelectionOptionsPropertyName}\": {{ \"attribute\": \"{Sanitize(attribute)}\" }} next to selectionState.";
	}
}
