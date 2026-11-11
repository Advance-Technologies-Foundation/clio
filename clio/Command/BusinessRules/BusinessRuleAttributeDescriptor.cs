namespace Clio.Command.BusinessRules;

internal sealed record BusinessRuleAttributeDescriptor(
	string Path,
	string DataValueTypeName,
	string? ReferenceSchemaName,
	string? ScopeName = null) {
	public bool IsScoped => !string.IsNullOrEmpty(ScopeName);

	/// <summary>
	/// True for an attribute that holds a view-config component's state rather than a data source column;
	/// such attributes are listed first in hints.
	/// </summary>
	public bool IsComponentAttribute { get; init; }
}
