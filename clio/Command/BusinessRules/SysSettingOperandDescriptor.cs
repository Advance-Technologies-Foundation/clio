namespace Clio.Command.BusinessRules;

/// <summary>
/// Resolved type metadata for a system-setting condition operand: the setting code together with the
/// business-rule data value type it resolves to and, for lookup settings, the referenced entity schema.
/// </summary>
/// <param name="SysSettingName">System-setting code the operand references.</param>
/// <param name="DataValueTypeName">Business-rule data value type the setting resolves to.</param>
/// <param name="ReferenceSchemaName">Referenced entity schema for a lookup setting, otherwise <c>null</c>.</param>
internal sealed record SysSettingOperandDescriptor(
	string SysSettingName,
	string DataValueTypeName,
	string? ReferenceSchemaName);
