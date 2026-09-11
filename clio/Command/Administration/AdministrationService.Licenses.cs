using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Clio.Common;

namespace Clio.Command.Administration;

public sealed partial class AdministrationService {
	private static readonly string[] RoleLicenseColumns = ["Id", "SysRole", "SysLicPackage"];

	/// <inheritdoc />
	public JsonElement RedistributeRoleLicenses(Guid roleId, bool includeManual) {
		GetRole(roleId);
		return client.Post(ServiceUrlBuilder.KnownRoute.AdministrationRedistributeRoleLicenses,
			"ScheduleRoleLicenseRedistributionResult", new { roleId, redistributeManuallyAssignedLicenses = includeManual },
			AdministrationResponseKind.SuccessObject);
	}

	/// <inheritdoc />
	public JsonElement GetUserLicenses(Guid userId) {
		JsonElement user = GetUser(userId);
		if (TypeOf(user) == 7) { throw new ArgumentException("Technical accounts do not support personal user licenses."); }
		return client.Post(ServiceUrlBuilder.KnownRoute.AdministrationGetLicenses, "GetAvailableLicPackagesResult",
			new { userId }, AdministrationResponseKind.Array);
	}

	/// <inheritdoc />
	public JsonElement SetUserLicense(Guid userId, Guid packageId, bool remove) {
		RequireId(packageId);
		JsonElement user = GetUser(userId);
		if (!remove && !user.GetProperty("Active").GetBoolean()) { throw new ArgumentException("Activate the user before assigning a license."); }
		JsonElement[] packages = GetUserLicenses(userId).EnumerateArray()
			.Where(package => package.GetProperty("Id").GetGuid() == packageId).ToArray();
		if (packages.Length != 1) { throw new ArgumentException("The package ID must identify one available license package."); }
		if (!remove && !packages[0].GetProperty("Enabled").GetBoolean()) {
			throw new ArgumentException("The selected license is unavailable for assignment to this account.");
		}
		client.Post(ServiceUrlBuilder.KnownRoute.AdministrationUpdateLicenses, "UpdateLicenseInfoResult",
			new { userId, licenseItems = JsonSerializer.Serialize(new Dictionary<string, bool> { [packageId.ToString()] = !remove }) },
			AdministrationResponseKind.ErrorString);
		packages = GetUserLicenses(userId).EnumerateArray().Where(package => package.GetProperty("Id").GetGuid() == packageId).ToArray();
		if (packages.Length != 1 || packages[0].GetProperty("Checked").GetBoolean() == remove) {
			throw new AdministrationStateException("License assignment readback differs from the request.");
		}
		return packages[0].Clone();
	}

	/// <inheritdoc />
	public JsonElement GetRoleLicenses(Guid roleId, int offset, int limit) {
		GetRole(roleId);
		return client.Select("SysLicPackageInRole", RoleLicenseColumns,
			new Dictionary<string, object> { ["SysRole"] = roleId }, offset, limit);
	}

	/// <inheritdoc />
	public JsonElement SetRoleLicense(Guid roleId, Guid packageId, bool remove) {
		GetRole(roleId);
		RequireId(packageId);
		RequireExpectedCount(client.Select("SysLicPackage", ["Id"],
			new Dictionary<string, object> { ["Id"] = packageId }, limit: 2), 1);
		Dictionary<string, object> filter = new() { ["SysRole"] = roleId, ["SysLicPackage"] = packageId };
		JsonElement rows = client.Select("SysLicPackageInRole", RoleLicenseColumns, filter, limit: 2);
		if (rows.GetArrayLength() > 1) { throw new AdministrationStateException("Duplicate role license associations require explicit repair."); }
		if (remove && rows.GetArrayLength() == 1) {
			client.DeleteEntity("SysLicPackageInRole", rows[0].GetProperty("Id").GetGuid());
		} else if (!remove && rows.GetArrayLength() == 0) {
			client.WriteEntity("SysLicPackageInRole", Guid.NewGuid(), filter, true);
		}
		rows = client.Select("SysLicPackageInRole", RoleLicenseColumns, filter, limit: 2);
		RequireExpectedCount(rows, remove ? 0 : 1);
		return rows;
	}
}
