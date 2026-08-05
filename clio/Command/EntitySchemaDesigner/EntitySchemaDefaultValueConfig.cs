using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Clio.Command.EntitySchemaDesigner;

/// <summary>
/// Represents structured entity-schema default value metadata used by MCP requests and readback responses.
/// </summary>
public sealed class EntitySchemaDefaultValueConfig
{
	/// <summary>
	/// Gets or sets the default value source name.
	/// </summary>
	[JsonPropertyName("source")]
	[Description("Default value source: None, Const, Settings, SystemValue, or Sequence.")]
	public string? Source { get; init; }

	/// <summary>
	/// Gets or sets the constant default value payload.
	/// </summary>
	[JsonPropertyName("value")]
	[Description("Constant default value payload when source is Const. When source is Sequence, accepts a mask whose single '{0}' placeholder is at the end (for example 'LN-{0}' produces LN-00001); the static text before '{0}' becomes the sequence prefix. Static text after '{0}' (a suffix) is not supported and is rejected. Do not combine with sequence-prefix.")]
	public object? Value { get; init; }

	/// <summary>
	/// Gets or sets the source payload for system settings or system values.
	/// </summary>
	[JsonPropertyName("value-source")]
	[Description("System setting or system value selector. For Settings accepts code, name, or id. For SystemValue accepts GUID, enum alias, or display caption. Used when source is Settings or SystemValue.")]
	public string? ValueSource { get; init; }

	/// <summary>
	/// Gets or sets the canonical backend identifier resolved from <c>value-source</c>.
	/// </summary>
	[JsonPropertyName("resolved-value-source")]
	[Description("Canonical persisted identifier resolved from value-source. SystemValue resolves to GUID; Settings resolves to setting code.")]
	public string? ResolvedValueSource { get; init; }

	/// <summary>
	/// Gets or sets the optional prefix for sequence defaults.
	/// </summary>
	[JsonPropertyName("sequence-prefix")]
	[Description("Optional static prefix prepended before the padded sequence number when source is Sequence (for example 'LN-' produces LN-00001). Alternative to passing a 'value' mask such as 'LN-{0}'; do not set both. Suffixes after the number are not supported.")]
	public string? SequencePrefix { get; init; }

	/// <summary>
	/// Gets or sets the sequence width for sequence defaults.
	/// </summary>
	[JsonPropertyName("sequence-number-of-chars")]
	[Description("Sequence width. Used when source is Sequence.")]
	public int? SequenceNumberOfChars { get; init; }

	/// <summary>
	/// Gets the resolved display value of the referenced record for a lookup <c>Const</c> default.
	/// </summary>
	[JsonPropertyName("display-value")]
	[Description("Display value of the referenced record for a lookup Const default, resolved in the connected user's culture. Null for non-lookup defaults or when unavailable (see record-resolution).")]
	public string? DisplayValue { get; init; }

	/// <summary>
	/// Gets the honest marker explaining why a lookup <c>Const</c> default's display value is unavailable.
	/// </summary>
	[JsonPropertyName("record-resolution")]
	[Description("Marker when the referenced record's display value could not be resolved: no-access, not-found-or-no-access, or display-column-unavailable. Null when display-value is present or enrichment does not apply.")]
	public string? RecordResolution { get; init; }

	/// <summary>
	/// Returns a copy of this configuration enriched with the referenced-record display value and/or
	/// record-resolution marker, leaving all other fields unchanged.
	/// </summary>
	/// <param name="displayValue">Resolved display value, or null.</param>
	/// <param name="recordResolution">Record-resolution marker, or null.</param>
	/// <returns>A new <see cref="EntitySchemaDefaultValueConfig"/> with the display fields populated.</returns>
	public EntitySchemaDefaultValueConfig WithDisplay(string? displayValue, string? recordResolution) {
		return new EntitySchemaDefaultValueConfig {
			Source = Source,
			Value = Value,
			ValueSource = ValueSource,
			ResolvedValueSource = ResolvedValueSource,
			SequencePrefix = SequencePrefix,
			SequenceNumberOfChars = SequenceNumberOfChars,
			DisplayValue = displayValue,
			RecordResolution = recordResolution
		};
	}
}
