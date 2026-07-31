namespace Clio.Command;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

internal static class ResourceStringHelper {
	private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
	private static readonly Regex MacroResourceStringPattern = new(
		@"#ResourceString\(([^)]+)\)#",
		RegexOptions.Compiled,
		RegexTimeout);
	private static readonly Regex DollarResourceStringPattern = new(
		@"\$Resources\.Strings\.([A-Za-z0-9_]+)",
		RegexOptions.Compiled,
		RegexTimeout);
	private static readonly Regex CaptionBoundaryPattern = new(
		"([a-z])([A-Z])",
		RegexOptions.Compiled,
		RegexTimeout);

	public static HashSet<string> ExtractKeys(string body) {
		var keys = new HashSet<string>();
		if (string.IsNullOrEmpty(body)) {
			return keys;
		}
		foreach (Match match in MacroResourceStringPattern.Matches(body)) {
			keys.Add(match.Groups[1].Value);
		}
		foreach (Match match in DollarResourceStringPattern.Matches(body)) {
			keys.Add(match.Groups[1].Value);
		}
		return keys;
	}

	public static HashSet<string> GetExistingKeys(JArray localizableStrings) {
		var keys = new HashSet<string>();
		if (localizableStrings == null) {
			return keys;
		}
		foreach (JObject item in localizableStrings.Children<JObject>()) {
			string name = item["name"]?.ToString();
			if (!string.IsNullOrEmpty(name)) {
				keys.Add(name);
			}
		}
		return keys;
	}

	/// <summary>
	/// Reports whether a localizable-string <paramref name="key"/> referenced by a freshly-inserted
	/// view node will resolve to a caption at runtime. It mirrors the register-missing-body-key decision
	/// in <see cref="TryResolveResourceValue"/> and additionally treats DS-bound keys as resolving.
	/// </summary>
	/// <param name="key">Localizable-string key referenced by the body.</param>
	/// <param name="resources">Explicit resources passed to the save, or <c>null</c>.</param>
	/// <param name="dsBoundKeys">View-model attribute names bound to a data source, or <c>null</c>.</param>
	/// <returns><c>true</c> when the key resolves to a caption at runtime; otherwise <c>false</c>.</returns>
	public static bool WillResolve(
		string key,
		IReadOnlyDictionary<string, string> resources,
		IReadOnlySet<string> dsBoundKeys) {
		if (string.IsNullOrEmpty(key)) {
			return false;
		}
		if (resources != null && resources.ContainsKey(key)) {
			return true;
		}
		if (dsBoundKeys != null && dsBoundKeys.Contains(key)) {
			return true;
		}
		return IsUsrPrefixed(key);
	}

	/// <summary>
	/// Single definition of the "<c>Usr</c>-prefixed key clio auto-derives a caption for" test, shared by
	/// <see cref="WillResolve"/> and <see cref="TryResolveResourceValue"/> so the read-side verdict and the
	/// write-side registration cannot drift on the prefix comparison.
	/// </summary>
	private static bool IsUsrPrefixed(string key) => key.StartsWith("Usr", StringComparison.Ordinal);

	public static string DeriveCaption(string key) {
		string result = key;
		if (result.EndsWith("_caption")) {
			result = result[..^"_caption".Length];
		}
		if (result.StartsWith("Usr")) {
			result = result[3..];
		}
		result = CaptionBoundaryPattern.Replace(result, "$1 $2");
		result = result.Replace('_', ' ');
		return result.Trim();
	}

	private static JObject CreateLocalizableEntry(string key, string value) {
		return new JObject {
			["uId"] = Guid.NewGuid().ToString(),
			["name"] = key,
			["values"] = new JArray {
				new JObject {
					["cultureName"] = "en-US",
					["value"] = value
				}
			}
		};
	}

	public static (JArray cleaned, List<string> registered) CleanAndMerge(
		JArray localizableStrings,
		Dictionary<string, string> resources,
		HashSet<string> bodyKeys,
		IReadOnlySet<string> dsBoundKeys = null) {
		var result = new JArray();
		var existingKeys = new HashSet<string>();
		var registered = new List<string>();
		CopyExistingEntries(localizableStrings, result, existingKeys);
		RegisterMissingBodyKeys(bodyKeys, resources, dsBoundKeys, existingKeys, result, registered);
		if (resources != null) {
			foreach (KeyValuePair<string, string> kvp in resources.Where(kvp =>
				         !existingKeys.Contains(kvp.Key) &&
				         !bodyKeys.Contains(kvp.Key))) {
				result.Add(CreateLocalizableEntry(kvp.Key, kvp.Value));
				registered.Add(kvp.Key);
			}
		}
		return (result, registered);
	}

	private static void CopyExistingEntries(
		JArray localizableStrings,
		JArray result,
		ISet<string> existingKeys) {
		if (localizableStrings == null) {
			return;
		}
		foreach (JObject entry in localizableStrings.Children<JObject>()) {
			string name = entry["name"]?.ToString();
			if (string.IsNullOrEmpty(name)) {
				continue;
			}
			result.Add(entry);
			existingKeys.Add(name);
		}
	}

	private static void RegisterMissingBodyKeys(
		IEnumerable<string> bodyKeys,
		IReadOnlyDictionary<string, string> resources,
		IReadOnlySet<string> dsBoundKeys,
		ISet<string> existingKeys,
		JArray result,
		ICollection<string> registered) {
		foreach (string key in bodyKeys.Where(key => !existingKeys.Contains(key))) {
			if (!TryResolveResourceValue(resources, key, dsBoundKeys, out string value)) {
				continue;
			}
			result.Add(CreateLocalizableEntry(key, value));
			registered.Add(key);
		}
	}

	private static bool TryResolveResourceValue(
		IReadOnlyDictionary<string, string> resources,
		string key,
		IReadOnlySet<string> dsBoundKeys,
		out string value) {
		if (resources != null && resources.TryGetValue(key, out string explicitValue)) {
			value = explicitValue;
			return true;
		}
		// Skip auto-derivation for DS-bound attributes — the platform auto-provides their captions.
		if (dsBoundKeys != null && dsBoundKeys.Contains(key)) {
			value = null;
			return false;
		}
		if (IsUsrPrefixed(key)) {
			value = DeriveCaption(key);
			return true;
		}
		value = null;
		return false;
	}
}
