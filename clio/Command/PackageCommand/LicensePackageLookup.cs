namespace Clio.Command.PackageCommand
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.Text.Json;

	/// <summary>
	/// Lookup over LicenseManagerProxyService.svc/GetLicenses responses: matches a license package by
	/// display name. The response wraps entries in a "licenses" array; each entry carries both its own
	/// license-record "id" and the distinct "packageId" that LicenseManagerProxyService.svc/SaveLicenseData
	/// actually expects — "packageId" must be preferred, since sending the record "id" is silently accepted
	/// as a well-formed Guid but rejected by SaveLicenseData with a "Product ... not found" error.
	/// </summary>
	internal static class LicensePackageLookup
	{
		private static readonly string[] NameCandidates = { "packagename", "name", "caption", "displayname", "title" };

		/// <summary>
		/// Returns the distinct packageId of every entry in <paramref name="response"/> whose name-like
		/// property matches <paramref name="name"/> case-insensitively.
		/// </summary>
		public static IReadOnlyList<string> FindIdsByName(string response, string name) {
			using JsonDocument document = JsonDocument.Parse(response);
			JsonElement items = LicenseServiceJsonHelper.UnwrapArray(document.RootElement, "licenses");
			var matches = new List<string>();
			foreach (JsonElement item in items.EnumerateArray()) {
				if (item.ValueKind != JsonValueKind.Object) {
					continue;
				}
				string preferredId = null; // "packageId": the id SaveLicenseData expects
				string fallbackId = null; // "id": the license record's own id, used only if packageId is absent
				bool nameMatches = false;
				foreach (JsonProperty property in item.EnumerateObject()) {
					if (property.Value.ValueKind != JsonValueKind.String) {
						continue;
					}
					string lowerName = property.Name.ToLowerInvariant();
					if (lowerName == "packageid") {
						preferredId = property.Value.GetString();
					}
					else if (lowerName == "id") {
						fallbackId = property.Value.GetString();
					}
					else if (Array.IndexOf(NameCandidates, lowerName) >= 0 &&
						string.Equals(property.Value.GetString(), name, StringComparison.OrdinalIgnoreCase)) {
						nameMatches = true;
					}
				}
				string id = preferredId ?? fallbackId;
				if (nameMatches && id is not null) {
					matches.Add(id);
				}
			}
			return matches.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		}
	}
}
