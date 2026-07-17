namespace Clio.Command.PackageCommand
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.Text.Json;
	using Clio.Common;

	/// <summary>
	/// Adds or removes users from a Creatio license package via LicenseManagerProxyService.SaveLicenseData.
	/// Accepts either raw Guids or human-readable names for --package-id/--add-user/--remove-user: names are
	/// resolved to Guids before the request is sent, via the same license-exempt LicenseManagerProxyService.svc
	/// endpoints the Supervisor "License" UI itself calls (GetUsersList, GetLicenses) — so resolution works
	/// even on an environment with zero distributed licenses, unlike a DataService query.
	/// </summary>
	public class DistributeLicenseCommand : RemoteCommand<DistributeLicenseOptions>
	{

		private const string GetLicensesServicePath = @"/ServiceModel/LicenseManagerProxyService.svc/GetLicenses";
		private const string GetUsersListServicePath = @"/ServiceModel/LicenseManagerProxyService.svc/GetUsersList";

		public DistributeLicenseCommand(IApplicationClient applicationClient, EnvironmentSettings settings)
			: base(applicationClient, settings) {
		}

		protected override string ServicePath => @"/ServiceModel/LicenseManagerProxyService.svc/SaveLicenseData";

		protected override string GetRequestData(DistributeLicenseOptions options) {
			string[] addedUsers = ResolveUserIds(options.AddUser);
			string[] deletedUsers = ResolveUserIds(options.RemoveUser);
			if (addedUsers.Length == 0 && deletedUsers.Length == 0) {
				throw new ArgumentException(
					"Specify at least one --add-user or --remove-user to distribute the license");
			}
			string conflictingUserId = addedUsers.FirstOrDefault(id =>
				deletedUsers.Contains(id, StringComparer.OrdinalIgnoreCase));
			if (conflictingUserId is not null) {
				throw new ArgumentException(
					$"User Id '{conflictingUserId}' cannot be in both --add-user and --remove-user");
			}
			string packageId = ResolvePackageId(options.PackageId);
			var payload = new {
				deletedUsers,
				addedUsers,
				packageId,
				source = 1 // manual assignment source, matches the Supervisor "License" section UI
			};
			return JsonSerializer.Serialize(payload);
		}

		protected override void ProceedResponse(string response, DistributeLicenseOptions options) {
			LicenseResponseParser.EnsureSuccess(response, "License data not saved");
			base.ProceedResponse(response, options);
		}

		private string[] ResolveUserIds(IEnumerable<string> userIdsOrNames) {
			return (userIdsOrNames ?? Enumerable.Empty<string>())
				.Select(id => id?.Trim())
				.Where(id => !string.IsNullOrEmpty(id))
				.Select(ResolveUserId)
				.ToArray();
		}

		private string ResolveUserId(string userIdOrName) =>
			Guid.TryParse(userIdOrName, out _)
				? userIdOrName
				: ResolveByName(GetUsersListServicePath, userIdOrName, LicenseUserLookup.FindIdsByName,
					"user", "--add-user/--remove-user");

		private string ResolvePackageId(string packageIdOrName) =>
			Guid.TryParse(packageIdOrName, out _)
				? packageIdOrName
				: ResolveByName(GetLicensesServicePath, packageIdOrName, LicensePackageLookup.FindIdsByName,
					"license package", "--package-id");

		private string ResolveByName(string servicePath, string name,
			Func<string, string, IReadOnlyList<string>> findIdsByName, string entityDescription, string optionHint) {
			IReadOnlyList<string> matchingIds;
			try {
				string response = ApplicationClient.ExecutePostRequest(RootPath + servicePath, "{}");
				matchingIds = findIdsByName(response, name);
			}
			catch (Exception ex) when (ex is JsonException or InvalidOperationException) {
				throw new ArgumentException(
					$"Could not resolve {entityDescription} '{name}' via {servicePath} ({ex.Message}). "
					+ $"Pass the Guid directly via {optionHint}.", ex);
			}
			if (matchingIds.Count == 0) {
				throw new ArgumentException(
					$"No {entityDescription} named '{name}' was found via {servicePath}. Pass its Guid directly via {optionHint}.");
			}
			if (matchingIds.Count > 1) {
				throw new ArgumentException(
					$"'{name}' matches {matchingIds.Count} {entityDescription} records. Pass the Guid directly via {optionHint} to disambiguate.");
			}
			if (!Guid.TryParse(matchingIds[0], out _)) {
				throw new ArgumentException(
					$"{entityDescription} '{name}' resolved to a non-Guid Id from the server response.");
			}
			return matchingIds[0];
		}
	}
}
