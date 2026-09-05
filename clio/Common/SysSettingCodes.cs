namespace Clio.Common;

internal static class SysSettingCodes {
	internal const string SchemaNamePrefix = "SchemaNamePrefix";

	internal static string ReadSchemaNamePrefix(ISysSettingsManager sysSettingsManager) {
		string value = sysSettingsManager.GetSysSettingValueByCode(SchemaNamePrefix);
		// Trimmed on both sides of the quote strip: a legacy shape that arrives as "\" Usr \"" would
		// otherwise keep its inner spaces and read as a prefix no generated identifier can use.
		return value?.Trim().Trim('"').Trim() ?? string.Empty;
	}
}
