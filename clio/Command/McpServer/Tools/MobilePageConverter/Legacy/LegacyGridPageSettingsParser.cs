namespace Clio.Command.McpServer.Tools.MobilePageConverter.Legacy;

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

/// <summary>
/// One column the classic Mobile wizard placed on a list page: the entity column path (possibly dotted), its
/// caption, its display row inside the bucket, the platform data-value type, and every other property the
/// wizard recorded (view types, formats, …) which the converter reports rather than silently drops.
/// </summary>
public sealed record LegacyGridColumn(
	string Name,
	string ColumnName,
	string Caption,
	int Row,
	int? DataValueType,
	IReadOnlyDictionary<string, JToken> OtherProperties);

/// <summary>
/// The effective (package-hierarchy-merged) classic Mobile wizard list settings, parsed into the three wizard
/// buckets. <see cref="Items"/> holds the title column (the wizard always writes exactly one; the parser keeps
/// whatever it finds so the analysis can report deviations), <see cref="SubtitleItems"/> and
/// <see cref="GroupItems"/> the body columns, each bucket ordered by the wizard <c>row</c>.
/// </summary>
public sealed record LegacyGridPageSettings(
	string EntitySchemaName,
	string SettingsType,
	IReadOnlyList<LegacyGridColumn> Items,
	IReadOnlyList<LegacyGridColumn> SubtitleItems,
	IReadOnlyList<LegacyGridColumn> GroupItems,
	IReadOnlyDictionary<string, JToken> OtherSettingsProperties,
	string GridType = null);

/// <summary>
/// Pure parser of the merged <c>settings</c> node of a legacy <c>Mobile&lt;Entity&gt;GridPageSettings&lt;Workplace&gt;</c>
/// schema (ENG-95730). The node is the result of applying every package layer's diff array with the page
/// diff applier: the wizard's <c>values</c> are hoisted onto the item, so columns arrive as plain objects
/// inside the <c>items</c> / <c>subtitleItems</c> / <c>groupItems</c> arrays.
/// </summary>
public static class LegacyGridPageSettingsParser {

	/// <summary>Name of the root node of a legacy settings diff array.</summary>
	public const string SettingsNodeName = "settings";

	/// <summary><c>settingsType</c> of a wizard LIST page.</summary>
	public const string GridPageSettingsType = "GridPage";

	/// <summary><c>settingsType</c> of a wizard RECORD page (ENG-95731, not converted here).</summary>
	public const string RecordPageSettingsType = "RecordPage";

	/// <summary>Wizard bucket holding the single title column.</summary>
	public const string ItemsBucket = "items";

	/// <summary>Wizard bucket holding the subtitle columns.</summary>
	public const string SubtitleItemsBucket = "subtitleItems";

	/// <summary>Wizard bucket holding the group columns.</summary>
	public const string GroupItemsBucket = "groupItems";

	private const string RowKey = "row";
	private const string ContentKey = "content";
	private const string ColumnNameKey = "columnName";
	private const string DataValueTypeKey = "dataValueType";

	/// <summary>Column keys the converter consumes; anything else is reported through the coverage table.</summary>
	private static readonly HashSet<string> KnownColumnKeys = new(StringComparer.Ordinal) {
		"name", "operation", RowKey, ContentKey, ColumnNameKey, DataValueTypeKey
	};

	/// <summary>
	/// Settings keys the converter or the classifier consumes; anything else is surfaced as an unconverted settings
	/// property. The override-section keys are listed here because the classifier owns their reporting.
	/// </summary>
	internal static readonly HashSet<string> KnownSettingsKeys = new(StringComparer.Ordinal) {
		"name", "operation", "entitySchemaName", "settingsType", ItemsBucket, SubtitleItemsBucket, GroupItemsBucket,
		"localizableStrings", "viewConfigDiff", "viewModelConfigDiff", "modelConfigDiff", "diffV2", "viewConfig",
		"modelViewConfig", GridTypeKey, "rows", "columns"
	};

	/// <summary>
	/// Classic grid layout settings the wizard writes on every list page (<c>gridType</c> listed/tiled and the
	/// tiled <c>rows</c>/<c>columns</c> geometry). They describe the classic list only — a mobile ListItem row has
	/// one layout — so they are reported as informational, never as a decision.
	/// </summary>
	private const string GridTypeKey = "gridType";

	/// <summary>
	/// Parses the merged settings node.
	/// </summary>
	/// <param name="settings">The merged <c>settings</c> item (values hoisted).</param>
	/// <returns>The parsed wizard settings.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="settings"/> is null.</exception>
	/// <exception cref="InvalidOperationException">The node declares no <c>entitySchemaName</c>.</exception>
	public static LegacyGridPageSettings Parse(JObject settings) {
		ArgumentNullException.ThrowIfNull(settings);
		string entity = settings.Value<string>("entitySchemaName");
		if (string.IsNullOrWhiteSpace(entity)) {
			throw new InvalidOperationException(
				"Legacy mobile settings do not declare 'entitySchemaName'; the mobile list page cannot be bound to an object.");
		}
		var other = new Dictionary<string, JToken>(StringComparer.Ordinal);
		foreach (JProperty property in settings.Properties()) {
			if (!KnownSettingsKeys.Contains(property.Name)) {
				other[property.Name] = property.Value;
			}
		}
		return new LegacyGridPageSettings(
			entity.Trim(),
			settings.Value<string>("settingsType"),
			ParseBucket(settings, ItemsBucket),
			ParseBucket(settings, SubtitleItemsBucket),
			ParseBucket(settings, GroupItemsBucket),
			other,
			settings.Value<string>(GridTypeKey));
	}

	/// <summary>
	/// Reads one wizard bucket. Columns are ordered by <c>row</c> (stable, so two columns sharing a row keep
	/// their merged-array order); a column without <c>row</c> takes its array position.
	/// </summary>
	private static List<LegacyGridColumn> ParseBucket(JObject settings, string bucket) {
		if (settings[bucket] is not JArray array) {
			return [];
		}
		var columns = new List<LegacyGridColumn>(array.Count);
		int position = 0;
		foreach (JToken token in array) {
			if (token is not JObject column) {
				position++;
				continue;
			}
			string columnName = column.Value<string>(ColumnNameKey);
			if (string.IsNullOrWhiteSpace(columnName)) {
				position++;
				continue;
			}
			var extra = new Dictionary<string, JToken>(StringComparer.Ordinal);
			foreach (JProperty property in column.Properties()) {
				if (!KnownColumnKeys.Contains(property.Name)) {
					extra[property.Name] = property.Value;
				}
			}
			int row = column[RowKey]?.Type == JTokenType.Integer ? column.Value<int>(RowKey) : position;
			int? dataValueType = column[DataValueTypeKey]?.Type == JTokenType.Integer
				? column.Value<int>(DataValueTypeKey)
				: null;
			columns.Add(new LegacyGridColumn(
				column.Value<string>("name"),
				columnName.Trim(),
				column.Value<string>(ContentKey),
				row,
				dataValueType,
				extra));
			position++;
		}
		return columns.OrderBy(c => c.Row).ToList();
	}
}
