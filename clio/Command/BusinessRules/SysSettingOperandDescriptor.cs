namespace Clio.Command.BusinessRules;

internal sealed record SysSettingOperandDescriptor(
	string SysSettingName,
	string DataValueTypeName,
	string? ReferenceSchemaName);
