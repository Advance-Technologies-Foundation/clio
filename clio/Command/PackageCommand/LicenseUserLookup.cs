namespace Clio.Command.PackageCommand
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.Text.Json;

	/// <summary>
	/// Lookup over LicenseManagerProxyService.svc/GetUsersList responses: matches a user by display
	/// name. This is the same license-exempt service SaveLicenseData and GetLicenses live on, so —
	/// unlike a DataService query — it resolves users even on an environment with zero distributed
	/// licenses, exactly how the Supervisor "License" section populates its user picker to grant the
	/// very first license. The response wraps entries in a "users" array, each nesting its id/name
	/// under "sysAdminUnit": <c>{"value": "&lt;guid&gt;", "displayValue": "&lt;name&gt;"}</c>.
	/// </summary>
	internal static class LicenseUserLookup
	{
		/// <summary>
		/// Returns the distinct user Id of every entry in <paramref name="response"/> whose display
		/// name matches <paramref name="name"/> case-insensitively.
		/// </summary>
		public static IReadOnlyList<string> FindIdsByName(string response, string name) {
			using JsonDocument document = JsonDocument.Parse(response);
			JsonElement items = LicenseServiceJsonHelper.UnwrapArray(document.RootElement, "users");
			var matches = new List<string>();
			foreach (JsonElement item in items.EnumerateArray()) {
				if (item.ValueKind != JsonValueKind.Object) {
					continue;
				}
				JsonElement lookup = item.TryGetProperty("sysAdminUnit", out JsonElement nested) &&
					nested.ValueKind == JsonValueKind.Object
					? nested
					: item;
				string id = LicenseServiceJsonHelper.TryGetString(lookup, "value")
					?? LicenseServiceJsonHelper.TryGetString(lookup, "id");
				string displayName = LicenseServiceJsonHelper.TryGetString(lookup, "displayValue")
					?? LicenseServiceJsonHelper.TryGetString(lookup, "name");
				if (id is not null && displayName is not null &&
					string.Equals(displayName, name, StringComparison.OrdinalIgnoreCase)) {
					matches.Add(id);
				}
			}
			return matches.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		}
	}
}
