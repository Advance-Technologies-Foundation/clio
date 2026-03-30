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
	[Description("Constant default value payload. Used only when source is Const.")]
	public object? Value { get; init; }

	/// <summary>
	/// Gets or sets the source payload for system settings or system values.
	/// </summary>
	[JsonPropertyName("value-source")]
	[Description("System setting code or system value name. Used when source is Settings or SystemValue.")]
	public string? ValueSource { get; init; }

	/// <summary>
	/// Gets or sets the optional prefix for sequence defaults.
	/// </summary>
	[JsonPropertyName("sequence-prefix")]
	[Description("Optional sequence prefix. Used when source is Sequence.")]
	public string? SequencePrefix { get; init; }

	/// <summary>
	/// Gets or sets the sequence width for sequence defaults.
	/// </summary>
	[JsonPropertyName("sequence-number-of-chars")]
	[Description("Sequence width. Used when source is Sequence.")]
	public int? SequenceNumberOfChars { get; init; }
}
