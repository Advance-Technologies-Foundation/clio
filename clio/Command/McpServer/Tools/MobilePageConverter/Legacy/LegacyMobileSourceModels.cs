using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Clio.Command.McpServer.Tools.MobilePageConverter.Legacy;

/// <summary>
/// Facts about a LEGACY mobile wizard settings source (a <c>Mobile&lt;Entity&gt;GridPageSettings&lt;Workplace&gt;</c>
/// schema) surfaced on the conversion guide (ENG-95730). Carries everything the plan and the final report need —
/// which packages contributed, how each wizard column maps onto the mobile list row, which column properties were
/// transferred / dropped, and the open decisions — WITHOUT any raw schema body (bodies never enter an agent's context).
/// </summary>
public sealed class LegacyMobileSourceInfo {
	/// <summary>The wizard <c>settingsType</c> of the merged settings node (today always <c>GridPage</c>).</summary>
	[JsonPropertyName("settingsType")]
	public string SettingsType { get; init; }

	/// <summary>The entity the wizard page was bound to (<c>settings.entitySchemaName</c>) — the mobile page binds to the same object.</summary>
	[JsonPropertyName("entitySchemaName")]
	public string EntitySchemaName { get; init; }

	/// <summary>Workplace suffix parsed from the schema name (e.g. <c>DefaultWorkplace</c>); null when the name carries none.</summary>
	[JsonPropertyName("workplace")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Workplace { get; init; }

	/// <summary>
	/// Static classification of the merged settings: <c>plain</c> (wizard buckets only),
	/// <c>freedom-ui-overrides</c> (also carries viewConfigDiff / viewModelConfigDiff / modelConfigDiff / diffV2 —
	/// recognised, NOT converted, ENG-95733), or <c>custom-viewconfig</c> (refused before conversion).
	/// </summary>
	[JsonPropertyName("classification")]
	public string Classification { get; init; }

	/// <summary>Freedom UI override sections found in the settings and left untouched (ENG-95733).</summary>
	[JsonPropertyName("overrideSections")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<LegacyOverrideSectionInfo> OverrideSections { get; init; }

	/// <summary>
	/// Every schema layer that contributed to the effective settings, ROOT → HEAD in package-hierarchy order.
	/// Package names and operation counts only — no bodies.
	/// </summary>
	[JsonPropertyName("layers")]
	public IReadOnlyList<LegacyMobileSettingsLayerInfo> Layers { get; init; } = [];

	/// <summary>The wizard <c>items</c> column that becomes <c>ListItem.title</c>; null when the wizard defined none.</summary>
	[JsonPropertyName("titleColumn")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public LegacyColumnMappingInfo TitleColumn { get; init; }

	/// <summary>The <c>subtitleItems</c> then <c>groupItems</c> columns, in the order they become <c>ListItem.body</c> rows.</summary>
	[JsonPropertyName("bodyColumns")]
	public IReadOnlyList<LegacyColumnMappingInfo> BodyColumns { get; init; } = [];

	/// <summary>Coverage table: for every wizard column property, whether it was transferred, is informational, or was dropped and why.</summary>
	[JsonPropertyName("columnPropertyCoverage")]
	public IReadOnlyList<LegacyPropertyCoverageInfo> ColumnPropertyCoverage { get; init; } = [];

	/// <summary>Open decisions the user must take before the page is written (e.g. no title column, dropped view types).</summary>
	[JsonPropertyName("decisions")]
	public IReadOnlyList<string> Decisions { get; init; } = [];

	/// <summary>Advisory notes about the source (e.g. a package layer the hierarchy service did not return).</summary>
	[JsonPropertyName("notes")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<string> Notes { get; init; }
}

/// <summary>One schema layer of a legacy settings hierarchy (a package's replacing schema). No body.</summary>
public sealed class LegacyMobileSettingsLayerInfo {
	/// <summary>The settings schema name of this layer (the same name in every package).</summary>
	[JsonPropertyName("schemaName")]
	public string SchemaName { get; init; }

	/// <summary>The package that carries this layer.</summary>
	[JsonPropertyName("packageName")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string PackageName { get; init; }

	/// <summary>Number of diff operations this layer contributed (0 for a body-less replacing schema).</summary>
	[JsonPropertyName("operationCount")]
	public int OperationCount { get; init; }
}

/// <summary>A Freedom UI override section embedded in legacy settings (recognised, not converted).</summary>
public sealed class LegacyOverrideSectionInfo {
	/// <summary><c>viewConfigDiff</c> | <c>viewModelConfigDiff</c> | <c>modelConfigDiff</c> | <c>diffV2</c>.</summary>
	[JsonPropertyName("section")]
	public string Section { get; init; }

	/// <summary>Operation count when the section parsed as an array; -1 when it could not be counted.</summary>
	[JsonPropertyName("operationCount")]
	public int OperationCount { get; init; }

	/// <summary>The story that owns converting this section.</summary>
	[JsonPropertyName("ticket")]
	public string Ticket { get; init; }
}

/// <summary>How one wizard column maps onto the mobile list row.</summary>
public sealed class LegacyColumnMappingInfo {
	/// <summary><c>title</c> | <c>subtitle</c> | <c>group</c> — the wizard bucket the column came from.</summary>
	[JsonPropertyName("bucket")]
	public string Bucket { get; init; }

	/// <summary>The wizard <c>row</c> (display order inside its bucket).</summary>
	[JsonPropertyName("row")]
	public int Row { get; init; }

	/// <summary>The wizard <c>columnName</c> — an entity column path, possibly dotted (e.g. <c>Account.Type</c>).</summary>
	[JsonPropertyName("columnName")]
	public string ColumnName { get; init; }

	/// <summary>The wizard caption (<c>content</c>). Informational: mobile list rows show values, not captions.</summary>
	[JsonPropertyName("caption")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Caption { get; init; }

	/// <summary>The platform data-value type the wizard recorded for the column (informational).</summary>
	[JsonPropertyName("dataValueType")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public int? DataValueType { get; init; }

	/// <summary>The view-model attribute the row binds to (<c>PDS_&lt;path with '.' → '_'&gt;</c>).</summary>
	[JsonPropertyName("attribute")]
	public string Attribute { get; init; }

	/// <summary>The attribute's <c>modelConfig.path</c> (<c>PDS.&lt;same&gt;</c>).</summary>
	[JsonPropertyName("modelPath")]
	public string ModelPath { get; init; }

	/// <summary>Where the column lands on the mobile row: <c>ListItem.title</c> or <c>ListItem.body[i]</c>.</summary>
	[JsonPropertyName("target")]
	public string Target { get; init; }
}

/// <summary>Coverage of one wizard column property across the converted columns.</summary>
public sealed class LegacyPropertyCoverageInfo {
	/// <summary>The wizard column property (e.g. <c>columnName</c>, <c>content</c>, a view type).</summary>
	[JsonPropertyName("property")]
	public string Property { get; init; }

	/// <summary><c>transferred</c> | <c>informational</c> | <c>dropped</c>.</summary>
	[JsonPropertyName("status")]
	public string Status { get; init; }

	/// <summary>What happened to the property and why.</summary>
	[JsonPropertyName("note")]
	public string Note { get; init; }

	/// <summary>Columns that carried the property.</summary>
	[JsonPropertyName("columns")]
	public IReadOnlyList<string> Columns { get; init; } = [];
}
