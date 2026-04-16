using System;
using System.Linq;
using ATF.Repository;
using ATF.Repository.Providers;
using CreatioModel;

namespace Clio.Common.DataForge;

/// <summary>
/// Reads a Creatio SysSetting value directly via OData (IDataProvider) without requiring cliogate.
/// Used as a fallback when <see cref="ISysSettingsManager.GetSysSettingValueByCode"/> fails.
/// </summary>
public interface IDataForgeSysSettingDirectReader {
	/// <summary>
	/// Attempts to read the text value of a system setting by its code using direct OData access.
	/// </summary>
	/// <param name="code">The SysSetting code (e.g. "DataForgeServiceUrl").</param>
	/// <returns>The direct-read result describing whether a value was found and whether the lookup failed.</returns>
	DataForgeSysSettingReadResult ReadTextValue(string code);
}

/// <summary>
/// Result of a direct Data Forge syssetting read.
/// </summary>
/// <param name="Found">Whether the setting value was found.</param>
/// <param name="Value">The resolved text value when available.</param>
/// <param name="FailureReason">The direct-read failure reason, or <c>null</c> when the read completed normally.</param>
public sealed record DataForgeSysSettingReadResult(
	bool Found,
	string? Value,
	string? FailureReason
);

public sealed class DataForgeSysSettingDirectReader(IDataProvider dataProvider) : IDataForgeSysSettingDirectReader {
	private static readonly Guid AllUsersId = new("a29a3ba5-4b0d-de11-9a51-005056c00008");

	public DataForgeSysSettingReadResult ReadTextValue(string code) {
		try {
			var context = AppDataContextFactory.GetAppDataContext(dataProvider);
			SysSettings? sysSetting = context
				.Models<SysSettings>()
				.Where(s => s.Code == code)
				.ToList()
				.FirstOrDefault();

			if (sysSetting is null) {
				return new DataForgeSysSettingReadResult(false, null, null);
			}

			if (!IsTextValueType(sysSetting.ValueTypeName)) {
				return new DataForgeSysSettingReadResult(
					false,
					null,
					$"SysSetting '{code}' has unsupported value type '{sysSetting.ValueTypeName}'.");
			}

			SysSettingsValue? sysSettingValue = context
				.Models<SysSettingsValue>()
				.Where(v => v.SysSettingsId == sysSetting.Id)
				.ToList()
				.OrderByDescending(v => v.SysAdminUnitId == AllUsersId)
				.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v.TextValue));

			if (sysSettingValue is null) {
				return new DataForgeSysSettingReadResult(false, null, null);
			}

			return new DataForgeSysSettingReadResult(true, sysSettingValue.TextValue?.Trim(), null);
		} catch (Exception ex) {
			return new DataForgeSysSettingReadResult(false, null, ex.Message);
		}
	}

	private static bool IsTextValueType(string? valueTypeName) {
		return valueTypeName is "Text" or "ShortText" or "MediumText" or "LongText" or "MaxSizeText" or "SecureText";
	}
}
