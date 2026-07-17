namespace Clio.Command.PackageCommand
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.Text.Json;
	using Clio.Common;

	/// <summary>
	/// Adds or removes users from a Creatio license package via LicenseManagerProxyService.SaveLicenseData.
	/// </summary>
	public class DistributeLicenseCommand : RemoteCommand<DistributeLicenseOptions>
	{

		public DistributeLicenseCommand(IApplicationClient applicationClient, EnvironmentSettings settings)
			: base(applicationClient, settings) {
		}

		protected override string ServicePath => @"/ServiceModel/LicenseManagerProxyService.svc/SaveLicenseData";

		protected override string GetRequestData(DistributeLicenseOptions options) {
			string[] addedUsers = NormalizeUserIds(options.AddUser);
			string[] deletedUsers = NormalizeUserIds(options.RemoveUser);
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
			if (!Guid.TryParse(options.PackageId, out _)) {
				throw new ArgumentException($"'{options.PackageId}' is not a valid --package-id Guid");
			}
			var payload = new {
				deletedUsers,
				addedUsers,
				packageId = options.PackageId,
				source = 1 // manual assignment source, matches the Supervisor "License" section UI
			};
			return JsonSerializer.Serialize(payload);
		}

		protected override void ProceedResponse(string response, DistributeLicenseOptions options) {
			LicenseResponseParser.EnsureSuccess(response, "License data not saved");
			base.ProceedResponse(response, options);
		}

		private static string[] NormalizeUserIds(IEnumerable<string> userIds) {
			return (userIds ?? Enumerable.Empty<string>())
				.Select(id => id?.Trim())
				.Where(id => !string.IsNullOrEmpty(id))
				.Select(id => {
					if (!Guid.TryParse(id, out _)) {
						throw new ArgumentException($"'{id}' is not a valid user Id (Guid)");
					}
					return id;
				})
				.ToArray();
		}
	}
}
