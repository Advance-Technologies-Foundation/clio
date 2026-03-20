using System;
using System.Collections.Generic;
using System.Linq;
using ATF.Repository;
using ATF.Repository.Providers;
using CreatioModel;

namespace Clio.Command;

/// <summary>
/// Optional filters for installed-application queries.
/// </summary>
/// <param name="AppId">Optional installed application identifier.</param>
/// <param name="AppCode">Optional installed application code.</param>
public sealed record InstalledApplicationQuery(string? AppId = null, string? AppCode = null);

/// <summary>
/// Structured installed-application item used by non-CLI read flows.
/// </summary>
/// <param name="Id">Installed application identifier.</param>
/// <param name="Name">Installed application name.</param>
/// <param name="Code">Installed application code.</param>
/// <param name="Version">Installed application version.</param>
/// <param name="Description">Installed application description.</param>
public sealed record InstalledApplicationListItem(Guid Id, string Name, string Code, string Version, string Description);

/// <summary>
/// Reads installed applications from the current Creatio environment data context.
/// </summary>
public interface IInstalledApplicationQueryService {
	/// <summary>
	/// Returns installed applications that match the optional filters.
	/// </summary>
	/// <param name="query">Optional application filters.</param>
	/// <returns>Installed applications from <c>SysInstalledApp</c>.</returns>
	IReadOnlyList<SysInstalledApp> GetApplications(InstalledApplicationQuery? query = null);
}

/// <summary>
/// Default query service for installed applications.
/// </summary>
public sealed class InstalledApplicationQueryService(IDataProvider provider) : IInstalledApplicationQueryService {
	/// <inheritdoc />
	public IReadOnlyList<SysInstalledApp> GetApplications(InstalledApplicationQuery? query = null) {
		IEnumerable<SysInstalledApp> applications = AppDataContextFactory.GetAppDataContext(provider)
			.Models<SysInstalledApp>();

		if (TryParseAppId(query?.AppId, out Guid appId)) {
			applications = applications.Where(application => application.Id == appId);
		} else if (!string.IsNullOrWhiteSpace(query?.AppId)) {
			return [];
		}

		if (!string.IsNullOrWhiteSpace(query?.AppCode)) {
			applications = applications.Where(application =>
				string.Equals(application.Code, query.AppCode, StringComparison.OrdinalIgnoreCase));
		}

		return applications.ToList();
	}

	private static bool TryParseAppId(string? appId, out Guid parsedAppId) {
		if (string.IsNullOrWhiteSpace(appId)) {
			parsedAppId = Guid.Empty;
			return false;
		}

		return Guid.TryParse(appId.Trim(), out parsedAppId);
	}
}
