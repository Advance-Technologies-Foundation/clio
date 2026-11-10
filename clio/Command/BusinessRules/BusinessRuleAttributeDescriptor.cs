namespace Clio.Command.BusinessRules;

internal sealed record BusinessRuleAttributeDescriptor(
	string Path,
	string DataValueTypeName,
	string? ReferenceSchemaName,
	string? ScopeName = null) {
	public bool IsScoped => !string.IsNullOrEmpty(ScopeName);

	/// <summary>
	/// True for an attribute a view-config component generates at runtime (for example a stepper's
	/// active step) rather than one declared in the page body; such attributes are listed first in hints.
	/// </summary>
	public bool IsRuntimeGenerated { get; init; }
}
