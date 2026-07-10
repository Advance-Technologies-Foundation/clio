namespace Clio.Command.BusinessRules;

internal sealed record BusinessRuleAttributeDescriptor(
	string Path,
	string DataValueTypeName,
	string? ReferenceSchemaName,
	string? ScopeName = null) {
	public bool IsScoped => !string.IsNullOrEmpty(ScopeName);
}
