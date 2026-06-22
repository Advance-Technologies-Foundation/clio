using System;
using System.Text.Json;
using Clio.Command;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Detects whether an incoming Freedom UI page body <em>adds or lays out visual components</em> —
/// the body shape that the write-path layout-guidance gate (update-page / sync-pages) cares about.
/// </summary>
/// <remarks>
/// "Adds or lays out components" means the body's <c>viewConfigDiff</c> contains at least one
/// <c>operation:"insert"</c> entry whose <c>values.type</c> is a <c>crt.*</c> view element (any
/// visual or container component — input, button, panel, grid, tab, group, …). Bodies that only
/// <c>merge</c>/<c>remove</c>/<c>move</c> existing entries, and handler-only, converter-only, or
/// validator-only bodies, do <strong>not</strong> add or lay out components and return
/// <see langword="false"/>. A missing, empty, or unparseable <c>viewConfigDiff</c> also returns
/// <see langword="false"/> (fail-open: a body the detector cannot understand is never blocked by the
/// layout gate). Implementations must be side-effect-free and thread-safe — sync-pages evaluates
/// pages concurrently.
/// </remarks>
public interface IPageLayoutCompositionDetector {
	/// <summary>
	/// Returns whether <paramref name="body"/> adds or lays out at least one <c>crt.*</c> visual
	/// component via an <c>insert</c> operation in its <c>viewConfigDiff</c>.
	/// </summary>
	/// <param name="body">The raw JavaScript page body (marker-delimited Freedom UI schema).</param>
	/// <returns>
	/// <see langword="true"/> when the body inserts at least one <c>crt.*</c> view element;
	/// otherwise <see langword="false"/> (including for null/empty bodies and bodies with no
	/// parseable <c>viewConfigDiff</c>).
	/// </returns>
	bool BodyAddsOrLaysOutComponents(string body);
}

/// <summary>
/// Default <see cref="IPageLayoutCompositionDetector"/>. Reuses the established
/// <see cref="PageSchemaSectionReader"/> marker extraction + <see cref="SchemaValidationService.NormalizeJson"/>
/// parsing approach (the same one <c>ValidateInsertedFieldSelfConsistency</c> uses) so the detector
/// stays aligned with how the rest of the validation pipeline reads <c>viewConfigDiff</c>.
/// Registered as a singleton — it holds no mutable state.
/// </summary>
public sealed class PageLayoutCompositionDetector : IPageLayoutCompositionDetector {

	private const string SchemaViewConfigDiff = "SCHEMA_VIEW_CONFIG_DIFF";
	private const string SchemaDiffMarker = "SCHEMA_DIFF";
	private const string OperationPropertyName = "operation";
	private const string ValuesPropertyName = "values";
	private const string TypePropertyName = "type";
	private const string InsertOperation = "insert";
	private const string CrtTypePrefix = "crt.";

	/// <inheritdoc />
	public bool BodyAddsOrLaysOutComponents(string body) {
		if (string.IsNullOrWhiteSpace(body)) {
			return false;
		}
		if (!PageSchemaSectionReader.TryRead(body, out string vcdContent, SchemaViewConfigDiff, SchemaDiffMarker)) {
			return false;
		}
		JsonDocument document;
		try {
			document = JsonDocument.Parse(SchemaValidationService.NormalizeJson(vcdContent));
		} catch (JsonException) {
			// A body whose viewConfigDiff is not valid JSON is rejected (or accepted) by the
			// syntax/content validators that run BEFORE this gate — never block on a parse failure here.
			return false;
		}
		using (document) {
			if (document.RootElement.ValueKind != JsonValueKind.Array) {
				return false;
			}
			foreach (JsonElement entry in document.RootElement.EnumerateArray()) {
				if (IsCrtComponentInsert(entry)) {
					return true;
				}
			}
		}
		return false;
	}

	private static bool IsCrtComponentInsert(JsonElement entry) {
		if (entry.ValueKind != JsonValueKind.Object) {
			return false;
		}
		if (!entry.TryGetProperty(OperationPropertyName, out JsonElement operation) ||
			operation.ValueKind != JsonValueKind.String ||
			!string.Equals(operation.GetString(), InsertOperation, StringComparison.OrdinalIgnoreCase)) {
			return false;
		}
		if (!entry.TryGetProperty(ValuesPropertyName, out JsonElement values) ||
			values.ValueKind != JsonValueKind.Object) {
			return false;
		}
		if (!values.TryGetProperty(TypePropertyName, out JsonElement typeElement) ||
			typeElement.ValueKind != JsonValueKind.String) {
			return false;
		}
		string type = typeElement.GetString();
		return type != null && type.StartsWith(CrtTypePrefix, StringComparison.OrdinalIgnoreCase);
	}
}
