namespace Clio.Command.BusinessRules;

internal sealed record BusinessRuleAttributeDescriptor(
	string Path,
	string DataValueTypeName,
	string? ReferenceSchemaName);
