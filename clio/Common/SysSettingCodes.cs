namespace Clio.Common;

internal static class SysSettingCodes {
	internal const string SchemaNamePrefix = "SchemaNamePrefix";

	internal static string ReadSchemaNamePrefix(ISysSettingsManager sysSettingsManager) {
		string value = sysSettingsManager.GetSysSettingValueByCode(SchemaNamePrefix);
		return value?.Trim().Trim('"') ?? string.Empty;
	}
}
